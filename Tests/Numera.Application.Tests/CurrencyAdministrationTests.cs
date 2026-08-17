using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class CurrencyAdministrationTests
{
    private const ulong OperatorDiscordUserId = 820_000_000_000_000_001UL;
    private const ulong OwnerDiscordUserId = 820_000_000_000_000_002UL;
    private const ulong OutsiderDiscordUserId = 820_000_000_000_000_003UL;
    private const ulong GuildId = 910UL;
    private const ulong OtherGuildId = 911UL;

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

        public CurrencyAdministrationApplicationService Currencies { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public AccountingBookId Book { get; } = AccountingBookId.FromValue(EntityIdValue.FromBits(3));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-currency", Guid.NewGuid().ToString("n"));
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

            harness.Currencies = new CurrencyAdministrationApplicationService(
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
            VALUES({Blob(2)}, 'GUILD_TREASURY', 'ギルド金庫', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(3)}, {Blob(2)}, 'SYSTEM', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(4)}, {Blob(3)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

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

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public long ReadLong(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? scalar = command.ExecuteScalar();

            return scalar is null or DBNull
                ? 0L
                : Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
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

    private static AuthorizationContext SystemOwner() =>
        new(AuthorizationLevel.SystemOwner, OwnerDiscordUserId, GuildId);

    private static AuthorizationContext Customer() =>
        new(AuthorizationLevel.Customer, OutsiderDiscordUserId, GuildId);

    private static Task<Result<CurrencyView>> CreateAsync(
        Harness harness,
        AuthorizationContext actor,
        long genesisMinor = 1_000,
        long? capMinor = null,
        string code = "NUM",
        string token = "create-1") =>
        harness.Currencies.CreateCurrencyAsync(
            new CreateCurrencyCommand(
                actor,
                harness.Scope,
                harness.Book,
                "ヌメラ",
                code,
                "N",
                "{symbol}{amount}",
                2,
                capMinor,
                genesisMinor,
                "GENESIS_MINT",
                token),
            CancellationToken.None);

    private static LedgerAccountId TreasuryAccountOf(Harness harness, string code = "NUM") =>
        LedgerAccountId.FromValue(EntityIdValue.FromBytes(TreasuryBlob(harness, code)));

    private static byte[] TreasuryBlob(Harness harness, string code)
    {
        using SqliteConnection connection = harness.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT ledger_account_id FROM ledger_accounts
            WHERE account_code = '{CurrencyAdministrationApplicationService.TreasuryPostingCodePrefix}-{code}';
            """;

        return (byte[])command.ExecuteScalar()!;
    }

    [TestMethod]
    public async Task GuildOperatorCreatesTheCurrencyWithAGenesisMint()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(harness, GuildOperator());

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(CurrencyStatus.Active, created.Value.Status);
        Assert.AreEqual(1_000L, created.Value.BaseMoneySupply.Value);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM currencies;"));
        Assert.AreEqual("GENESIS", harness.ReadText("SELECT operation_kind FROM currency_supply_operations;"));
        Assert.AreEqual(1L, harness.ReadLong("SELECT count(*) FROM currency_metadata_versions;"));
    }

    [TestMethod]
    public async Task GenesisIsBalancedAcrossTreasuryAndIssuanceLiability()
    {
        await using Harness harness = Harness.Create();

        await CreateAsync(harness, SystemOwner());

        Assert.AreEqual(1_000L, harness.ReadLong("""
            SELECT posted_balance_minor FROM ledger_balance_projections p
            JOIN ledger_accounts a USING(ledger_account_id)
            WHERE a.account_kind = 'CASH_ASSET' AND a.posting_allowed = 1;
            """));

        Assert.AreEqual(1_000L, harness.ReadLong("""
            SELECT posted_balance_minor FROM ledger_balance_projections p
            JOIN ledger_accounts a USING(ledger_account_id)
            WHERE a.account_kind = 'BASE_MONEY_ISSUANCE_LIABILITY' AND a.posting_allowed = 1;
            """));

        Assert.AreEqual(2L, harness.ReadLong("SELECT count(*) FROM journal_entries;"));
    }

    [TestMethod]
    public async Task ZeroGenesisCreatesNoSupplyOperation()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner(), genesisMinor: 0);

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(0L, created.Value.BaseMoneySupply.Value);
        Assert.AreEqual(0L, harness.ReadLong("SELECT count(*) FROM currency_supply_operations;"));
    }

    [TestMethod]
    public async Task ASecondCurrencyInTheSameGuildIsRejected()
    {
        await using Harness harness = Harness.Create();
        await CreateAsync(harness, SystemOwner());

        Result<CurrencyView> second = await CreateAsync(
            harness, SystemOwner(), code: "OTH", token: "create-2");

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyAlreadyExists, second.Error!.Code);
    }

    [TestMethod]
    public async Task ReplayingTheCreateTokenReturnsTheExistingCurrency()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> first = await CreateAsync(harness, SystemOwner());

        Result<CurrencyView> replay = await CreateAsync(harness, SystemOwner());

        Assert.IsTrue(replay.IsSuccess);
        Assert.AreEqual(first.Value.Id, replay.Value.Id);
        Assert.AreEqual("NUM", replay.Value.Code);
        Assert.AreEqual(1L, harness.ReadLong("SELECT count(*) FROM currency_supply_operations;"));
    }

    [TestMethod]
    public async Task GuildOperatorOfAnotherGuildCannotCreateTheCurrency()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(harness, GuildOperator(OtherGuildId));

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, created.Error!.Code);
    }

    [TestMethod]
    public async Task ClaimedSystemOwnerLevelIsVerifiedAgainstTheDatabase()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(
            harness,
            new AuthorizationContext(AuthorizationLevel.SystemOwner, OutsiderDiscordUserId, OtherGuildId));

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, created.Error!.Code);
    }

    [TestMethod]
    public async Task CustomerCannotCreateTheCurrency()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(harness, Customer());

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, created.Error!.Category);
    }

    [TestMethod]
    public async Task GenesisAboveTheSupplyCapIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await CreateAsync(
            harness, SystemOwner(), genesisMinor: 1_001, capMinor: 1_000);

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencySupplyCapExceeded, created.Error!.Code);
        Assert.AreEqual(0L, harness.ReadLong("SELECT count(*) FROM currencies;"));
    }

    [TestMethod]
    public async Task InvalidMetadataIsRejectedAsValidation()
    {
        await using Harness harness = Harness.Create();

        Result<CurrencyView> created = await harness.Currencies.CreateCurrencyAsync(
            new CreateCurrencyCommand(
                SystemOwner(),
                harness.Scope,
                harness.Book,
                "ヌメラ",
                new string('X', 17),
                "N",
                "{amount}",
                2,
                null,
                0,
                "GENESIS_MINT",
                "create-1"),
            CancellationToken.None);

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyMetadataInvalid, created.Error!.Code);
    }

    [TestMethod]
    public async Task IssueIncreasesBaseMoneySupply()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 500, "MONETARY_POLICY", "issue-1"),
            CancellationToken.None);

        Assert.IsTrue(issued.IsSuccess);
        Assert.AreEqual(1_500L, issued.Value.BaseMoneySupply.Value);
        Assert.AreEqual(1L, harness.ReadLong(
            "SELECT count(*) FROM currency_supply_operations WHERE operation_kind = 'ISSUE';"));
        Assert.AreEqual(1_500L, harness.ReadLong("""
            SELECT posted_balance_minor FROM ledger_balance_projections p
            JOIN ledger_accounts a USING(ledger_account_id)
            WHERE a.account_kind = 'BASE_MONEY_ISSUANCE_LIABILITY' AND a.posting_allowed = 1;
            """));
    }

    [TestMethod]
    public async Task IssueBeyondTheSupplyCapIsRejectedInsideTheTransaction()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner(), capMinor: 1_200);

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 201, "MONETARY_POLICY", "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencySupplyCapExceeded, issued.Error!.Code);
        Assert.AreEqual(1_000L, harness.ReadLong(
            "SELECT total(amount_minor) FROM currency_supply_operations;"));
    }

    [TestMethod]
    public async Task IssueExactlyAtTheSupplyCapIsAccepted()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner(), capMinor: 1_200);

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 200, "MONETARY_POLICY", "issue-1"),
            CancellationToken.None);

        Assert.IsTrue(issued.IsSuccess);
        Assert.AreEqual(1_200L, issued.Value.BaseMoneySupply.Value);
    }

    [TestMethod]
    public async Task BurnDecreasesBaseMoneySupply()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> burned = await harness.Currencies.BurnAsync(
            new BurnCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 400, "WITHDRAWAL", "burn-1"),
            CancellationToken.None);

        Assert.IsTrue(burned.IsSuccess);
        Assert.AreEqual(600L, burned.Value.BaseMoneySupply.Value);
        Assert.AreEqual(600L, harness.ReadLong("""
            SELECT posted_balance_minor FROM ledger_balance_projections p
            JOIN ledger_accounts a USING(ledger_account_id)
            WHERE a.account_kind = 'CASH_ASSET' AND a.posting_allowed = 1;
            """));
        Assert.AreEqual("BURN", harness.ReadText(
            "SELECT operation_kind FROM currency_supply_operations WHERE operation_kind = 'BURN';"));
    }

    [TestMethod]
    public async Task BurnBeyondTheOutstandingSupplyIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> burned = await harness.Currencies.BurnAsync(
            new BurnCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 1_001, "WITHDRAWAL", "burn-1"),
            CancellationToken.None);

        Assert.IsFalse(burned.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencySupplyInsufficient, burned.Error!.Code);
    }

    [TestMethod]
    public async Task SuspendedCurrencyRejectsIssue()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());
        harness.Execute("UPDATE currencies SET status = 'SUSPENDED', version = version + 1;");

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 100, "MONETARY_POLICY", "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyNotIssuable, issued.Error!.Code);
    }

    [TestMethod]
    public async Task IssueOnAnUnknownCurrencyIsNotFound()
    {
        await using Harness harness = Harness.Create();
        await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(),
                CurrencyId.FromValue(EntityIdValue.FromBits(999)),
                TreasuryAccountOf(harness),
                100,
                "MONETARY_POLICY",
                "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyNotFound, issued.Error!.Code);
    }

    [TestMethod]
    public async Task IssuingIntoTheIssuanceLiabilityAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        byte[] issuance;

        using (SqliteConnection connection = harness.ConnectionFactory.OpenRuntimeConnection())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT ledger_account_id FROM ledger_accounts
                WHERE account_kind = 'BASE_MONEY_ISSUANCE_LIABILITY' AND posting_allowed = 1;
                """;
            issuance = (byte[])command.ExecuteScalar()!;
        }

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(),
                created.Value.Id,
                LedgerAccountId.FromValue(EntityIdValue.FromBytes(issuance)),
                100,
                "MONETARY_POLICY",
                "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencySupplyAccountInvalid, issued.Error!.Code);
    }

    [TestMethod]
    public async Task LowercaseReasonCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 100, "policy", "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyReasonCodeInvalid, issued.Error!.Code);
    }

    [TestMethod]
    public async Task ReplayingAnIssueTokenDoesNotMintTwice()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        IssueCurrencyCommand command = new(
            SystemOwner(), created.Value.Id, TreasuryAccountOf(harness), 500, "MONETARY_POLICY", "issue-1");

        await harness.Currencies.IssueAsync(command, CancellationToken.None);
        Result<CurrencySupplyView> replay = await harness.Currencies.IssueAsync(
            command, CancellationToken.None);

        Assert.IsTrue(replay.IsSuccess);
        Assert.AreEqual(1_500L, replay.Value.BaseMoneySupply.Value);
        Assert.AreEqual(1L, harness.ReadLong(
            "SELECT count(*) FROM currency_supply_operations WHERE operation_kind = 'ISSUE';"));
    }

    [TestMethod]
    public async Task GuildOperatorOfAnotherGuildCannotIssue()
    {
        await using Harness harness = Harness.Create();
        Result<CurrencyView> created = await CreateAsync(harness, SystemOwner());

        Result<CurrencySupplyView> issued = await harness.Currencies.IssueAsync(
            new IssueCurrencyCommand(
                GuildOperator(OtherGuildId),
                created.Value.Id,
                TreasuryAccountOf(harness),
                100,
                "MONETARY_POLICY",
                "issue-1"),
            CancellationToken.None);

        Assert.IsFalse(issued.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ManagementAuthorityMissing, issued.Error!.Code);
    }
}
