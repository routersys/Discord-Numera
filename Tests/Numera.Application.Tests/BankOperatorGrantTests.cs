using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class BankOperatorGrantTests
{
    private const ulong OperatorDiscordUserId = 830_000_000_000_000_001UL;
    private const ulong OwnerDiscordUserId = 830_000_000_000_000_002UL;
    private const ulong TargetDiscordUserId = 830_000_000_000_000_003UL;
    private const ulong GuildId = 940UL;
    private const ulong OtherGuildId = 941UL;
    private const string Institution = "0001";

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public BankOperatorGrantApplicationService Grants { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-operator", Guid.NewGuid().ToString("n"));
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

            harness.Grants = new BankOperatorGrantApplicationService(
                new SqliteBankingWriteGateway(new FinancialWriteCoordinator(harness.Coordinator)),
                new FixedClock(),
                new SequentialIdGenerator(9_000));

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(3)}, 'BANK', 'ヌメラ銀行', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO system_owner_identities(discord_user_id, created_at)
            VALUES('{OwnerDiscordUserId}', 1);
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string? ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar() as string;
        }

        public long Count(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return (long)command.ExecuteScalar()!;
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            SqliteConnection.ClearPool(ConnectionFactory.OpenRuntimeConnection());

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AuthorizationContext GuildOperator(ulong guildId = GuildId) =>
        new(AuthorizationLevel.GuildOperator, OperatorDiscordUserId, guildId);

    private static AuthorizationContext Customer() =>
        new(AuthorizationLevel.Customer, OperatorDiscordUserId, GuildId);

    private static Task<Result<BankOperatorGrantView>> GrantAsync(
        Harness harness,
        AuthorizationContext actor,
        ulong target = TargetDiscordUserId,
        string institutionCode = Institution) =>
        harness.Grants.GrantAsync(
            new GrantBankOperatorCommand(actor, institutionCode, target), CancellationToken.None);

    [TestMethod]
    public async Task AGuildOperatorGrantsBankOperatorAuthority()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await GrantAsync(harness, GuildOperator());

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ACTIVE", result.Value.Status);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM bank_operator_grants;"));
    }

    [TestMethod]
    public async Task ASecondActiveGrantForTheSameUserIsRejected()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await GrantAsync(harness, GuildOperator())).IsSuccess);

        Result<BankOperatorGrantView> second = await GrantAsync(harness, GuildOperator());

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, second.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankOperatorGrantAlreadyActive, second.Error.Code);
        Assert.AreEqual(1L, harness.Count("SELECT COUNT(*) FROM bank_operator_grants;"));
    }

    [TestMethod]
    public async Task AnOperatorCannotGrantAuthorityToItself()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await GrantAsync(
            harness, GuildOperator(), OperatorDiscordUserId);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankOperatorGrantSelfService, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("SELECT COUNT(*) FROM bank_operator_grants;"));
    }

    [TestMethod]
    public async Task ACustomerCannotGrantBankOperatorAuthority()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await GrantAsync(harness, Customer());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, result.Error.Code);
    }

    [TestMethod]
    public async Task AnOperatorOfAnotherGuildIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await GrantAsync(harness, GuildOperator(OtherGuildId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.GuildEconomyNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task AnUnknownInstitutionCodeIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await GrantAsync(
            harness, GuildOperator(), TargetDiscordUserId, "9999");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task RevokeTerminatesTheActiveGrant()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await GrantAsync(harness, GuildOperator())).IsSuccess);

        Result<BankOperatorGrantView> result = await harness.Grants.RevokeAsync(
            new RevokeBankOperatorCommand(GuildOperator(), Institution, TargetDiscordUserId),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("REVOKED", result.Value.Status);
        Assert.AreEqual("REVOKED", harness.ReadText("SELECT status FROM bank_operator_grants;"));
        Assert.AreEqual(
            0L, harness.Count("SELECT COUNT(*) FROM bank_operator_grants WHERE revoked_at IS NULL;"));
    }

    [TestMethod]
    public async Task RevokingAnAbsentGrantIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<BankOperatorGrantView> result = await harness.Grants.RevokeAsync(
            new RevokeBankOperatorCommand(GuildOperator(), Institution, TargetDiscordUserId),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankOperatorGrantNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task AGrantCanBeReissuedAfterRevocation()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await GrantAsync(harness, GuildOperator())).IsSuccess);
        Assert.IsTrue((await harness.Grants.RevokeAsync(
            new RevokeBankOperatorCommand(GuildOperator(), Institution, TargetDiscordUserId),
            CancellationToken.None)).IsSuccess);

        Result<BankOperatorGrantView> reissued = await GrantAsync(harness, GuildOperator());

        Assert.IsTrue(reissued.IsSuccess);
        Assert.AreEqual(2L, harness.Count("SELECT COUNT(*) FROM bank_operator_grants;"));
        Assert.AreEqual(
            1L, harness.Count("SELECT COUNT(*) FROM bank_operator_grants WHERE status = 'ACTIVE';"));
    }
}
