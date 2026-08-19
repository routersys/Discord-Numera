using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class DepositInsuranceTests
{
    private const ulong GuildId = 980UL;
    private const string Institution = "NUM0080";
    private const ulong OwnerDiscordUserId = 780_000_000_000_000_001UL;
    private const ulong CustomerDiscordUserId = 780_000_000_000_000_002UL;

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

        public CustomerAccountApplicationService Registration { get; private set; } = null!;

        public BankAccountApplicationService Accounts { get; private set; } = null!;

        public DepositInsuranceAdministrationApplicationService Administration { get; private set; } =
            null!;

        public DepositInsuranceApplicationService Insurance { get; private set; } = null!;

        public CurrencyId Currency { get; } = CurrencyId.FromValue(EntityIdValue.FromBits(2));

        public PartyId FundParty { get; } = PartyId.FromValue(EntityIdValue.FromBits(210));

        public AccountingBookId Book { get; } = AccountingBookId.FromValue(EntityIdValue.FromBits(211));

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-insurance", Guid.NewGuid().ToString("n"));
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

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Administration =
                new DepositInsuranceAdministrationApplicationService(gateway, harness.Clock, ids);
            harness.Insurance = new DepositInsuranceApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES({Blob(6)}, {Blob(5)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(200)}, 'GOVERNMENT', '中央銀行主体', 'ACTIVE', 1, 1),
                ({Blob(210)}, 'SYSTEM', '保険基金主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(201)}, {Blob(200)}, 'CENTRAL_BANK', 'OPEN', 1, 1),
                ({Blob(211)}, {Blob(210)}, 'SYSTEM', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(202)}, {Blob(201)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1),
                ({Blob(212)}, {Blob(211)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES
                ({Blob(40)}, {Blob(201)}, NULL, '4001', 'CENTRAL_BANK_SETTLEMENT_LIABILITY',
                    'LIABILITY', 'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(41)}, {Blob(211)}, NULL, '4002', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET',
                    'DEBIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(42)}, {Blob(211)}, NULL, '4003', 'FEE_REVENUE', 'REVENUE',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(43)}, {Blob(211)}, NULL, '4004', 'RESOLUTION_LOSS_EXPENSE', 'EXPENSE',
                    'DEBIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(44)}, {Blob(4)}, NULL, '4005', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(203)}, {Blob(201)}, NULL, '2100', 'CENTRAL_BANK_SETTLEMENT_LIABILITY',
                    'LIABILITY', 'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
            VALUES({Blob(204)}, {Blob(5)}, {Blob(2)}, {Blob(203)}, 'ACTIVE', 1, NULL, 1);

            INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                effective_to, version)
            VALUES({Blob(205)}, {Blob(5)}, 'DIRECT', NULL, {Blob(204)}, 'ACTIVE', 1, NULL, 1);

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(30)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 0, 0, 0, 'NONE', 1, NULL, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(31)}, {Blob(5)}, 1, NULL, 1);

            UPDATE banks
            SET current_policy_version_id = {Blob(30)},
                current_fee_schedule_version_id = {Blob(31)},
                version = version + 1
            WHERE bank_id = {Blob(5)};

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', 1);

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

        public long Count(string table)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public void OpenPeriods() => Execute("""
            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            SELECT randomblob(16), accounting_book_id, '2026', '2000-01-01', '2100-12-31', 'OPEN',
                NULL, 1
            FROM accounting_books
            WHERE accounting_book_id NOT IN (SELECT accounting_book_id FROM accounting_periods);
            """);

        public void Fund(DepositAccountId accountId, long amount) => Execute($"""
            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor,
                held_minor, version, updated_at)
            SELECT ledger_account_id, {amount}, 0, 1, 1 FROM deposit_accounts
            WHERE deposit_account_id = x'{Convert.ToHexString(accountId.Value.ToByteArray())}'
            ON CONFLICT(ledger_account_id) DO UPDATE SET posted_balance_minor = {amount};
            """);

        public async Task<DepositAccountId> OpenAccountAsync()
        {
            Result<CustomerAccountView> registered = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, CustomerDiscordUserId, "hoken", "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, registered.Value.Id, Institution),
                CancellationToken.None);

            return opened.Value.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
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

    private static AuthorizationContext Owner() =>
        new(AuthorizationLevel.SystemOwner, OwnerDiscordUserId, GuildId);

    private static AuthorizationContext Customer() =>
        new(AuthorizationLevel.Customer, CustomerDiscordUserId, GuildId);

    private static Task<Result<DepositInsuranceFundView>> CreateFundAsync(Harness harness) =>
        harness.Administration.CreateFundAsync(
            new CreateDepositInsuranceFundCommand(
                Owner(),
                harness.Currency,
                harness.FundParty,
                harness.Book,
                LedgerAccountId.FromValue(EntityIdValue.FromBits(40)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(41)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(42)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(43))),
            CancellationToken.None);

    private static async Task<DepositInsuranceSchemeId> PublishSchemeAsync(
        Harness harness,
        long enrollmentFeeMinor = 0)
    {
        Result<DepositInsuranceFundView> fund = await CreateFundAsync(harness);
        Assert.IsTrue(fund.IsSuccess, fund.Error?.Code);

        Result<DepositInsuranceSchemeDraftView> draft = await harness.Administration.StartDraftAsync(
            new StartDepositInsuranceSchemeDraftCommand(
                Owner(), harness.Currency, "STANDARD", fund.Value.Id, 1_000_000, enrollmentFeeMinor),
            CancellationToken.None);

        Assert.IsTrue(draft.IsSuccess, draft.Error?.Code);

        Result<DepositInsuranceSchemeVersionView> published =
            await harness.Administration.PublishAsync(
                new PublishDepositInsuranceSchemeCommand(Owner(), draft.Value.Id),
                CancellationToken.None);

        Assert.IsTrue(published.IsSuccess, published.Error?.Code);
        return draft.Value.Id;
    }

    [TestMethod]
    public async Task AFundIsUniquePerCurrency()
    {
        await using Harness harness = Harness.Create();

        Assert.IsTrue((await CreateFundAsync(harness)).IsSuccess);

        Result<DepositInsuranceFundView> duplicate = await CreateFundAsync(harness);

        Assert.IsFalse(duplicate.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositInsuranceFundAlreadyExists, duplicate.Error!.Code);
    }

    [TestMethod]
    public async Task PublishingActivatesTheSchemeAndFixesTheCurrentVersion()
    {
        await using Harness harness = Harness.Create();

        await PublishSchemeAsync(harness);

        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM deposit_insurance_schemes;"));
        Assert.AreEqual(1L, harness.Count(
            "deposit_insurance_schemes WHERE current_version_id IS NOT NULL"));
    }

    [TestMethod]
    public async Task ARetiredSchemeCannotResume()
    {
        await using Harness harness = Harness.Create();
        DepositInsuranceSchemeId scheme = await PublishSchemeAsync(harness);

        Assert.IsTrue((await harness.Administration.RetireAsync(
            new RetireDepositInsuranceSchemeCommand(Owner(), scheme), CancellationToken.None)).IsSuccess);

        Result resumed = await harness.Administration.ResumeSchemeAsync(
            new ResumeDepositInsuranceSchemeCommand(Owner(), scheme), CancellationToken.None);

        Assert.IsFalse(resumed.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositInsuranceSchemeStateInvalid, resumed.Error!.Code);
    }

    [TestMethod]
    public async Task EnrollmentCreatesTheCoverageReservation()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness);
        DepositAccountId account = await harness.OpenAccountAsync();

        Result<DepositInsuranceEnrollmentView> enrolled = await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-1"), CancellationToken.None);

        Assert.IsTrue(enrolled.IsSuccess, enrolled.Error?.Code);
        Assert.AreEqual(DepositInsuranceEnrollmentStatus.Active, enrolled.Value.Status);
        Assert.AreEqual(1_000_000L, enrolled.Value.CoverageLimit.Value);
        Assert.AreEqual(1L, harness.Count("deposit_insurance_reservations"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM deposit_insurance_reservations;"));
    }

    [TestMethod]
    public async Task ASecondEnrollmentOnTheSameAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness);
        DepositAccountId account = await harness.OpenAccountAsync();

        Assert.IsTrue((await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-2"),
            CancellationToken.None)).IsSuccess);

        Result<DepositInsuranceEnrollmentView> second = await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-3"), CancellationToken.None);

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositInsuranceAlreadyEnrolled, second.Error!.Code);
    }

    [TestMethod]
    public async Task APricedSchemePostsThePremiumAcrossTheThreeBooks()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness, enrollmentFeeMinor: 500);
        harness.OpenPeriods();
        DepositAccountId account = await harness.OpenAccountAsync();
        harness.Fund(account, 10_000);

        Result<DepositInsuranceEnrollmentView> enrolled = await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-4"),
            CancellationToken.None);

        Assert.IsTrue(enrolled.IsSuccess, enrolled.Error?.Code);
        Assert.AreEqual(1L, harness.Count("deposit_insurance_premium_payments"));
        Assert.AreEqual(
            "500",
            harness.ReadText("SELECT CAST(amount_minor AS TEXT) FROM deposit_insurance_premium_payments;"));
        Assert.AreEqual(
            "9500",
            harness.ReadText($"""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = x'{Convert.ToHexString(account.Value.ToByteArray())}';
                """));
        Assert.AreEqual(
            "500",
            harness.ReadText("""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_code = '4003';
                """));
        Assert.AreEqual(
            "500",
            harness.ReadText("""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_code = '4002';
                """));
        Assert.AreEqual(3L, harness.Count("accounting_transactions"));
    }

    [TestMethod]
    public async Task AnUnfundedPremiumRejectsTheEnrollmentWithoutEffect()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness, enrollmentFeeMinor: 500);
        harness.OpenPeriods();
        DepositAccountId account = await harness.OpenAccountAsync();

        Result<DepositInsuranceEnrollmentView> enrolled = await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-5"),
            CancellationToken.None);

        Assert.IsFalse(enrolled.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, enrolled.Error!.Code);
        Assert.AreEqual(0L, harness.Count("deposit_insurance_enrollments"));
        Assert.AreEqual(0L, harness.Count("deposit_insurance_premium_payments"));
    }

    [TestMethod]
    public async Task CancellationReleasesTheReservation()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness);
        DepositAccountId account = await harness.OpenAccountAsync();

        Assert.IsTrue((await harness.Insurance.EnrollAsync(
            new EnrollDepositInsuranceCommand(Customer(), account, "STANDARD", "enroll-5"),
            CancellationToken.None)).IsSuccess);

        Result cancelled = await harness.Insurance.CancelAsync(
            new CancelDepositInsuranceCommand(Customer(), account), CancellationToken.None);

        Assert.IsTrue(cancelled.IsSuccess, cancelled.Error?.Code);
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM deposit_insurance_enrollments;"));
        Assert.AreEqual("SETTLED", harness.ReadText("SELECT status FROM deposit_insurance_reservations;"));
        Assert.AreEqual(
            1L,
            harness.Count("deposit_insurance_reservations WHERE released_minor = reserved_minor"));
    }

    [TestMethod]
    public async Task OptionsListThePublishedSchemes()
    {
        await using Harness harness = Harness.Create();
        await PublishSchemeAsync(harness);
        DepositAccountId account = await harness.OpenAccountAsync();

        Result<DepositInsuranceOptionsView> options = await harness.Insurance.GetOptionsAsync(
            new GetDepositInsuranceOptionsQuery(Customer(), account), CancellationToken.None);

        Assert.IsTrue(options.IsSuccess, options.Error?.Code);
        Assert.AreEqual(1, options.Value.Options.Count);
        Assert.AreEqual("STANDARD", options.Value.Options[0].ProtectionClassCode);
        Assert.IsFalse(options.Value.Enrolled);
    }

    [TestMethod]
    public async Task ACustomerCannotAdministerSchemes()
    {
        await using Harness harness = Harness.Create();

        Result<DepositInsuranceFundView> denied = await harness.Administration.CreateFundAsync(
            new CreateDepositInsuranceFundCommand(
                Customer(),
                harness.Currency,
                harness.FundParty,
                harness.Book,
                LedgerAccountId.FromValue(EntityIdValue.FromBits(40)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(41)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(42)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(43))),
            CancellationToken.None);

        Assert.IsFalse(denied.IsSuccess);
        Assert.AreEqual(ErrorCategory.Forbidden, denied.Error!.Category);
    }

    [TestMethod]
    public async Task AFundRejectsDuplicateLedgerAccounts()
    {
        await using Harness harness = Harness.Create();

        Result<DepositInsuranceFundView> invalid = await harness.Administration.CreateFundAsync(
            new CreateDepositInsuranceFundCommand(
                Owner(),
                harness.Currency,
                harness.FundParty,
                harness.Book,
                LedgerAccountId.FromValue(EntityIdValue.FromBits(40)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(40)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(42)),
                LedgerAccountId.FromValue(EntityIdValue.FromBits(43))),
            CancellationToken.None);

        Assert.IsFalse(invalid.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositInsuranceFundAccountInvalid, invalid.Error!.Code);
    }
}
