using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Common;
using Numera.Domain.Identity;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

internal sealed class FixedClock : IClock
{
    private long current = 1_776_000_000_000;

    public UtcTimestamp Now() => UtcTimestamp.FromUnixMilliseconds(current);

    public void Advance(long milliseconds) => current += milliseconds;
}

internal sealed class SequentialIdGenerator : IIdGenerator
{
    private ulong next;

    public SequentialIdGenerator(ulong seed) => next = seed;

    public EntityIdValue NextId() => EntityIdValue.FromBits(++next);
}

[TestClass]
public sealed class RegisterCustomerAccountTests
{
    private const ulong GuildId = 900UL;

    private const ulong DiscordUser = 123456789012345678UL;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            Options = options;
            ConnectionFactory = new SqliteConnectionFactory(options);
            Clock = new FixedClock();
            IdGenerator = new SequentialIdGenerator(1_000);
        }

        public SqliteDatabaseOptions Options { get; }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public FixedClock Clock { get; }

        public SequentialIdGenerator IdGenerator { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public CustomerAccountApplicationService Service { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-app-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            harness.Initialize();
            return harness;
        }

        private void Initialize()
        {
            SqliteDatabaseInitializer initializer = new(
                Options, ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]));
            initializer.Initialize(1_776_000_000_000);

            SeedEconomyScope();

            Coordinator = new SqliteWriteCoordinator(
                ConnectionFactory,
                new SqliteRetryPolicy(3, 1, static () => 0));
            Coordinator.Start();

            Service = new CustomerAccountApplicationService(
                new SqliteBankingWriteGateway(new FinancialWriteCoordinator(Coordinator)),
                new SqliteBankingReadGateway(ConnectionFactory),
                Clock,
                IdGenerator);
        }

        private void SeedEconomyScope()
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES($id, '900', 'Asia/Tokyo', 'ACTIVE', 1);
                """;
            command.Parameters.AddWithValue("$id", Scope.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public long Count(string table)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public string ReadSingleText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public Task<Result<CustomerAccountView>> RegisterAsync(
            ulong discordUserId = DiscordUser,
            string handle = "taro",
            string displayName = "山田太郎") =>
            Service.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, displayName),
                CancellationToken.None);

        public Task<Result<CustomerAccountStatusView>> StatusAsync(ulong discordUserId = DiscordUser) =>
            Service.GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(discordUserId),
                CancellationToken.None);

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
    public async Task RegistrationCreatesEveryIdentityRecord()
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountView> result = await harness.RegisterAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("taro", result.Value.PublicHandle);
        Assert.AreEqual("山田太郎", result.Value.DisplayName);
        Assert.AreEqual(CustomerAccountStatus.Active, result.Value.Status);

        Assert.AreEqual(1L, harness.Count("parties"));
        Assert.AreEqual(1L, harness.Count("customer_accounts"));
        Assert.AreEqual(1L, harness.Count("discord_identity_links"));
        Assert.AreEqual(1L, harness.Count("business_operations"));
        Assert.AreEqual(1L, harness.Count("outbox_events"));
    }

    [TestMethod]
    public async Task RegisteredLinkIsActiveAndPrimary()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Assert.AreEqual("ACTIVE", harness.ReadSingleText("SELECT status FROM discord_identity_links;"));
        Assert.AreEqual(
            "1",
            harness.ReadSingleText("SELECT CAST(is_primary AS TEXT) FROM discord_identity_links;"));
    }

    [TestMethod]
    public async Task BusinessOperationIsCommittedWithIdempotencyKey()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Assert.AreEqual("COMMITTED", harness.ReadSingleText("SELECT status FROM business_operations;"));
        Assert.AreEqual(
            CustomerAccountApplicationService.OperationType,
            harness.ReadSingleText("SELECT idempotency_scope FROM business_operations;"));
        Assert.AreEqual(
            DiscordUser.ToString(System.Globalization.CultureInfo.InvariantCulture),
            harness.ReadSingleText("SELECT idempotency_key FROM business_operations;"));
    }

    [TestMethod]
    public async Task OutboxEventIsEnqueuedAsPending()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Assert.AreEqual("PENDING", harness.ReadSingleText("SELECT status FROM outbox_events;"));
        Assert.AreEqual(
            CustomerAccountApplicationService.RegisteredEventType,
            harness.ReadSingleText("SELECT event_type FROM outbox_events;"));
    }

    [TestMethod]
    public async Task StatusResolvesTheRegisteredAccountFromTheDiscordUser()
    {
        await using Harness harness = Harness.Create();
        Result<CustomerAccountView> registered = await harness.RegisterAsync();

        Result<CustomerAccountStatusView> status = await harness.StatusAsync();

        Assert.IsTrue(status.IsSuccess);
        Assert.AreEqual(registered.Value.Id, status.Value.Id);
        Assert.AreEqual("taro", status.Value.PublicHandle);
        Assert.AreEqual("山田太郎", status.Value.DisplayName);
        Assert.AreEqual(CustomerAccountStatus.Active, status.Value.Status);
        Assert.AreEqual(registered.Value.CreatedAt, status.Value.RegisteredAt);
    }

    [TestMethod]
    public async Task StatusIsNotFoundForAnUnregisteredDiscordUser()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<CustomerAccountStatusView> status = await harness.StatusAsync(discordUserId: 999);

        Assert.IsFalse(status.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, status.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountNotFound, status.Error.Code);
    }

    [TestMethod]
    public async Task StatusIsNotFoundForTheZeroDiscordUser()
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountStatusView> status = await harness.StatusAsync(discordUserId: 0);

        Assert.IsFalse(status.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountNotFound, status.Error!.Code);
    }

    [TestMethod]
    public async Task RepeatedRegistrationReturnsTheSameAccount()
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountView> first = await harness.RegisterAsync();
        Result<CustomerAccountView> second = await harness.RegisterAsync();

        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value.Id, second.Value.Id);
        Assert.AreEqual(1L, harness.Count("customer_accounts"));
        Assert.AreEqual(1L, harness.Count("outbox_events"));
    }

    [TestMethod]
    public async Task AnotherDiscordUserCannotReuseTakenHandle()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<CustomerAccountView> result = await harness.RegisterAsync(discordUserId: 999, handle: "taro");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.HandleAlreadyTaken, result.Error.Code);
        Assert.AreEqual(1L, harness.Count("customer_accounts"));
        Assert.AreEqual(1L, harness.Count("business_operations"));
    }

    [TestMethod]
    public async Task FailedRegistrationLeavesNoPartialState()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        await harness.RegisterAsync(discordUserId: 999, handle: "taro");

        Assert.AreEqual(1L, harness.Count("parties"));
        Assert.AreEqual(1L, harness.Count("discord_identity_links"));
        Assert.AreEqual(1L, harness.Count("outbox_events"));
    }

    [TestMethod]
    [DataRow("Taro")]
    [DataRow("1taro")]
    [DataRow("ta")]
    [DataRow("taro_")]
    [DataRow("ta__ro")]
    public async Task MalformedHandleIsRejectedBeforePersistence(string handle)
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountView> result = await harness.RegisterAsync(handle: handle);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.HandleFormatInvalid, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("business_operations"));
    }

    [TestMethod]
    public async Task BlankDisplayNameIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountView> result = await harness.RegisterAsync(displayName: "   ");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DisplayNameInvalid, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("parties"));
    }

    [TestMethod]
    public async Task ConcurrentRegistrationsOfOneDiscordUserProduceOneAccount()
    {
        await using Harness harness = Harness.Create();

        Task<Result<CustomerAccountView>>[] attempts =
        [
            harness.RegisterAsync(handle: "taro1"),
            harness.RegisterAsync(handle: "taro2"),
            harness.RegisterAsync(handle: "taro3"),
            harness.RegisterAsync(handle: "taro4"),
        ];

        Result<CustomerAccountView>[] results = await Task.WhenAll(attempts);

        Assert.AreEqual(1L, harness.Count("customer_accounts"));
        Assert.AreEqual(1L, harness.Count("discord_identity_links"));
        Assert.AreEqual(1L, harness.Count("business_operations"));
        Assert.AreEqual(1L, harness.Count("outbox_events"));

        int succeeded = results.Count(static result => result.IsSuccess);
        Assert.AreEqual(4, succeeded);
        Assert.AreEqual(1, results.Select(static result => result.Value.Id).Distinct().Count());
    }

    [TestMethod]
    public async Task ConcurrentRegistrationsOfDistinctUsersAllSucceed()
    {
        await using Harness harness = Harness.Create();

        Task<Result<CustomerAccountView>>[] attempts =
        [
            harness.RegisterAsync(discordUserId: 1001, handle: "user1"),
            harness.RegisterAsync(discordUserId: 1002, handle: "user2"),
            harness.RegisterAsync(discordUserId: 1003, handle: "user3"),
        ];

        Result<CustomerAccountView>[] results = await Task.WhenAll(attempts);

        Assert.IsTrue(results.All(static result => result.IsSuccess));
        Assert.AreEqual(3L, harness.Count("customer_accounts"));
        Assert.AreEqual(3L, harness.Count("outbox_events"));
    }

    [TestMethod]
    public async Task ZeroDiscordUserIdIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<CustomerAccountView> result = await harness.RegisterAsync(discordUserId: 0);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
    }

    [TestMethod]
    public async Task ALinkGrantIsIssuedForARegisteredUser()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<LinkGrantView> result = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(32, result.Value.Code.Length);
        Assert.AreEqual(1L, harness.Count("account_link_grants"));
        Assert.AreEqual("ISSUED", harness.ReadSingleText("SELECT status FROM account_link_grants;"));
    }

    [TestMethod]
    public async Task AnUnregisteredUserCannotIssueALinkGrant()
    {
        await using Harness harness = Harness.Create();

        Result<LinkGrantView> result = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task TheRawCodeIsNotStored()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<LinkGrantView> issued = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        string stored = harness.ReadSingleText(
            "SELECT CAST(code_digest AS TEXT) FROM account_link_grants;");

        Assert.IsFalse(stored.Contains(issued.Value.Code, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ASecondDiscordUserConsumesTheGrant()
    {
        await using Harness harness = Harness.Create();
        Result<CustomerAccountView> owner = await harness.RegisterAsync();

        Result<LinkGrantView> issued = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        Result<CustomerAccountView> consumed = await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(999UL, issued.Value.Code), CancellationToken.None);

        Assert.IsTrue(consumed.IsSuccess);
        Assert.AreEqual(owner.Value.Id, consumed.Value.Id);
        Assert.AreEqual("CONSUMED", harness.ReadSingleText("SELECT status FROM account_link_grants;"));
        Assert.AreEqual(2L, harness.Count("discord_identity_links"));
    }

    [TestMethod]
    public async Task AGrantIsSingleUse()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<LinkGrantView> issued = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(999UL, issued.Value.Code), CancellationToken.None);

        Result<CustomerAccountView> second = await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(1000UL, issued.Value.Code), CancellationToken.None);

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LinkGrantInvalid, second.Error!.Code);
    }

    [TestMethod]
    public async Task AnExpiredGrantIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<LinkGrantView> issued = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);

        harness.Clock.Advance(AccountLinkGrant.LifetimeMilliseconds);

        Result<CustomerAccountView> consumed = await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(999UL, issued.Value.Code), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LinkGrantExpired, consumed.Error!.Code);
        Assert.AreEqual("ISSUED", harness.ReadSingleText("SELECT status FROM account_link_grants;"));
    }

    [TestMethod]
    public async Task AnUnknownCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<CustomerAccountView> consumed = await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(999UL, "DEADBEEF"), CancellationToken.None);

        Assert.IsFalse(consumed.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LinkGrantInvalid, consumed.Error!.Code);
    }

    [TestMethod]
    public async Task TheLastLinkCannotBeUnlinked()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result result = await harness.Service.UnlinkDiscordIdentityAsync(
            new UnlinkDiscordIdentityCommand(DiscordUser, DiscordUser), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.LastLinkCannotBeRemoved, result.Error!.Code);
    }

    [TestMethod]
    public async Task UnlinkingPromotesAnotherLinkToPrimary()
    {
        await using Harness harness = Harness.Create();
        await harness.RegisterAsync();

        Result<LinkGrantView> issued = await harness.Service.CreateLinkGrantAsync(
            new CreateLinkGrantCommand(DiscordUser), CancellationToken.None);
        await harness.Service.ConsumeLinkGrantAsync(
            new ConsumeLinkGrantCommand(999UL, issued.Value.Code), CancellationToken.None);

        Result result = await harness.Service.UnlinkDiscordIdentityAsync(
            new UnlinkDiscordIdentityCommand(DiscordUser, DiscordUser), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            "UNLINKED",
            harness.ReadSingleText(
                $"SELECT status FROM discord_identity_links WHERE discord_user_id = '{DiscordUser}';"));
        Assert.AreEqual(
            "1",
            harness.ReadSingleText(
                "SELECT CAST(COUNT(*) AS TEXT) FROM discord_identity_links WHERE is_primary = 1 AND status = 'ACTIVE';"));
    }
}
