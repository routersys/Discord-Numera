using Microsoft.Data.Sqlite;
using Numera.Application.Common;
using Numera.Discord.Sessions;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class InteractionSessionTests
{
    private const ulong Owner = 111_000_000_000_000_001UL;
    private const ulong Intruder = 111_000_000_000_000_002UL;
    private const ulong Guild = 900UL;
    private const string Flow = "BANK_TRANSFER";
    private const string InitialState = "AMOUNT_INPUT";

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
            Clock = new FixedClock();
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public FixedClock Clock { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public InteractionSessionService Service { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public EconomyScopeId OtherScope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-session", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed();

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            harness.Service = new InteractionSessionService(
                new SqliteBankingWriteGateway(new FinancialWriteCoordinator(harness.Coordinator)),
                new SqliteBankingReadGateway(harness.ConnectionFactory),
                harness.Clock,
                new SequentialIdGenerator(5_000));

            return harness;
        }

        private void Seed()
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES($first, '900', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES($second, '901', 'Asia/Tokyo', 'ACTIVE', 1);
                """;
            command.Parameters.AddWithValue("$first", Scope.Value.ToByteArray());
            command.Parameters.AddWithValue("$second", OtherScope.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public Task<Result<InteractionSessionTicket>> OpenAsync(ulong user = Owner, string payload = "{}") =>
            Service.OpenAsync(
                new OpenInteractionSessionRequest(user, Guild, Scope, Flow, InitialState, payload),
                CancellationToken.None);

        public ConsumeInteractionSessionRequest Request(
            string rawToken,
            ulong user = Owner,
            ulong guild = Guild,
            EconomyScopeId? scope = null,
            string state = InitialState,
            long version = 0) =>
            new(rawToken, user, guild, scope ?? Scope, state, version);

        public long CountSessions(string status)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM interaction_sessions WHERE status = $status;";
            command.Parameters.AddWithValue("$status", status);
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            using (SqliteConnection pooled = ConnectionFactory.OpenRuntimeConnection())
            {
                SqliteConnection.ClearPool(pooled);
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public void RawTokenIsUnpredictableAndFixedLength()
    {
        HashSet<string> tokens = [];

        for (int index = 0; index < 128; index++)
        {
            string token = InteractionSessionService.CreateRawToken();

            Assert.AreEqual(InteractionSessionService.RawTokenTextLength, token.Length);
            Assert.IsTrue(tokens.Add(token));
        }
    }

    [TestMethod]
    public void TokenHashIsDeterministicAndThirtyTwoBytes()
    {
        string token = InteractionSessionService.CreateRawToken();

        Assert.IsTrue(InteractionSessionService.TryComputeTokenHash(token, out byte[] first));
        Assert.IsTrue(InteractionSessionService.TryComputeTokenHash(token, out byte[] second));

        Assert.AreEqual(InteractionSession.TokenHashLength, first.Length);
        CollectionAssert.AreEqual(first, second);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("short")]
    [DataRow("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    public void MalformedTokenIsRejected(string token) =>
        Assert.IsFalse(InteractionSessionService.TryComputeTokenHash(token, out _));

    [TestMethod]
    public async Task OpenedSessionCanBeConsumedByItsOwner()
    {
        await using Harness harness = Harness.Create();

        Result<InteractionSessionTicket> opened = await harness.OpenAsync();
        Assert.IsTrue(opened.IsSuccess);

        Result<InteractionSessionSnapshot> consumed =
            await harness.Service.ConsumeAsync(harness.Request(opened.Value.RawToken), CancellationToken.None);

        Assert.IsTrue(consumed.IsSuccess);
        Assert.AreEqual(Flow, consumed.Value.FlowType);
        Assert.AreEqual(InitialState, consumed.Value.State);
        Assert.AreEqual(0L, consumed.Value.StateVersion);
    }

    [TestMethod]
    public async Task UnknownTokenIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.OpenAsync();

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(InteractionSessionService.CreateRawToken()), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, consumed.Error!.Category);
    }

    [TestMethod]
    public async Task AnotherUserCannotHijackTheSession()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken, user: Intruder), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, consumed.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.SessionNotFound, consumed.Error.Code);
    }

    [TestMethod]
    public async Task GuildMismatchIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken, guild: 999), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
    }

    [TestMethod]
    public async Task EconomyScopeMismatchIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken, scope: harness.OtherScope), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
    }

    [TestMethod]
    public async Task StateMismatchIsReportedAsConcurrencyConflict()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken, state: "CONFIRM"), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(ErrorCategory.ConcurrencyConflict, consumed.Error!.Category);
    }

    [TestMethod]
    public async Task StaleStateVersionIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        await harness.Service.AdvanceAsync(
            harness.Request(opened.Value.RawToken), "CONFIRM", "{}", CancellationToken.None);

        Result<InteractionSessionSnapshot> replayed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken), CancellationToken.None);

        Assert.IsFalse(replayed.IsSuccess);
        Assert.AreEqual(ErrorCategory.ConcurrencyConflict, replayed.Error!.Category);
    }

    [TestMethod]
    public async Task AdvanceIncrementsStateVersion()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        Result<InteractionSessionSnapshot> advanced = await harness.Service.AdvanceAsync(
            harness.Request(opened.Value.RawToken), "CONFIRM", """{"amount":"100"}""", CancellationToken.None);

        Assert.IsTrue(advanced.IsSuccess);
        Assert.AreEqual("CONFIRM", advanced.Value.State);
        Assert.AreEqual(1L, advanced.Value.StateVersion);
    }

    [TestMethod]
    public async Task ExpiredSessionIsRejectedAndMarkedExpired()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(InteractionSession.DefaultLifetimeMinutes).Ticks / TimeSpan.TicksPerMillisecond);

        Result<InteractionSessionSnapshot> consumed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SessionExpired, consumed.Error!.Code);
        Assert.AreEqual(1L, harness.CountSessions("ACTIVE"));

        Result<int> swept = await harness.Service.ExpireStaleAsync(100, CancellationToken.None);

        Assert.AreEqual(1, swept.Value);
        Assert.AreEqual(1L, harness.CountSessions("EXPIRED"));
        Assert.AreEqual(0L, harness.CountSessions("ACTIVE"));
    }

    [TestMethod]
    public async Task CompletedSessionCannotBeReused()
    {
        await using Harness harness = Harness.Create();
        Result<InteractionSessionTicket> opened = await harness.OpenAsync();

        await harness.Service.CompleteAsync(harness.Request(opened.Value.RawToken), CancellationToken.None);

        Result<InteractionSessionSnapshot> replayed = await harness.Service.ConsumeAsync(
            harness.Request(opened.Value.RawToken), CancellationToken.None);

        Assert.IsFalse(replayed.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SessionExpired, replayed.Error!.Code);
        Assert.AreEqual(1L, harness.CountSessions("COMPLETED"));
    }

    [TestMethod]
    public async Task NinthSessionSupersedesTheOldest()
    {
        await using Harness harness = Harness.Create();

        List<InteractionSessionTicket> tickets = [];
        for (int index = 0; index < InteractionSession.MaximumActivePerUser; index++)
        {
            harness.Clock.Advance(1_000);
            tickets.Add((await harness.OpenAsync()).Value);
        }

        Assert.AreEqual((long)InteractionSession.MaximumActivePerUser, harness.CountSessions("ACTIVE"));

        harness.Clock.Advance(1_000);
        await harness.OpenAsync();

        Assert.AreEqual((long)InteractionSession.MaximumActivePerUser, harness.CountSessions("ACTIVE"));
        Assert.AreEqual(1L, harness.CountSessions("SUPERSEDED"));

        Result<InteractionSessionSnapshot> oldest = await harness.Service.ConsumeAsync(
            harness.Request(tickets[0].RawToken), CancellationToken.None);

        Assert.IsFalse(oldest.IsSuccess);
    }

    [TestMethod]
    public async Task SessionsOfDistinctUsersAreCountedSeparately()
    {
        await using Harness harness = Harness.Create();

        for (int index = 0; index < InteractionSession.MaximumActivePerUser; index++)
        {
            harness.Clock.Advance(1_000);
            await harness.OpenAsync(Owner);
        }

        harness.Clock.Advance(1_000);
        await harness.OpenAsync(Intruder);

        Assert.AreEqual(InteractionSession.MaximumActivePerUser + 1L, harness.CountSessions("ACTIVE"));
        Assert.AreEqual(0L, harness.CountSessions("SUPERSEDED"));
    }

    [TestMethod]
    public async Task OversizedPayloadIsRejected()
    {
        await using Harness harness = Harness.Create();

        string oversized = new('a', InteractionSession.MaximumPayloadBytes + 1);

        await Assert.ThrowsExactlyAsync<InvariantViolationException>(
            async () => await harness.OpenAsync(payload: oversized));
    }

    [TestMethod]
    public void VerificationCoversEveryCanonicalCheck()
    {
        UtcTimestamp now = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
        EconomyScopeId scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));
        byte[] hash = new byte[InteractionSession.TokenHashLength];

        InteractionSession session = InteractionSession.Open(
            InteractionSessionId.FromValue(EntityIdValue.FromBits(9)),
            Owner.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Guild.ToString(System.Globalization.CultureInfo.InvariantCulture),
            scope, Flow, InitialState, hash, "{}", now,
            UtcTimestamp.FromUnixMilliseconds(now.UnixMilliseconds + 60_000));

        ConsumeInteractionSessionRequest valid = new(
            "token", Owner, Guild, scope, InitialState, 0);

        Assert.AreEqual(SessionVerification.NotFound, InteractionSessionService.Verify(null, valid, now));
        Assert.AreEqual(SessionVerification.Accepted, InteractionSessionService.Verify(session, valid, now));

        Assert.AreEqual(
            SessionVerification.UserMismatch,
            InteractionSessionService.Verify(session, valid with { DiscordUserId = Intruder }, now));
        Assert.AreEqual(
            SessionVerification.GuildMismatch,
            InteractionSessionService.Verify(session, valid with { GuildId = 999 }, now));
        Assert.AreEqual(
            SessionVerification.ScopeMismatch,
            InteractionSessionService.Verify(
                session, valid with { EconomyScopeId = EconomyScopeId.FromValue(EntityIdValue.FromBits(2)) }, now));
        Assert.AreEqual(
            SessionVerification.StateMismatch,
            InteractionSessionService.Verify(session, valid with { ExpectedState = "OTHER" }, now));
        Assert.AreEqual(
            SessionVerification.VersionMismatch,
            InteractionSessionService.Verify(session, valid with { ExpectedStateVersion = 7 }, now));
        Assert.AreEqual(
            SessionVerification.Expired,
            InteractionSessionService.Verify(
                session, valid, UtcTimestamp.FromUnixMilliseconds(now.UnixMilliseconds + 60_000)));
    }
}
