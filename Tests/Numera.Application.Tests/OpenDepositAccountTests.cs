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
public sealed class OpenDepositAccountTests
{
    private const ulong GuildId = 900UL;

    private const string Institution = "NUM0001";
    private const ulong FirstUser = 700_000_000_000_000_001UL;
    private const ulong SecondUser = 700_000_000_000_000_002UL;

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

        public BankQueryApplicationService Queries { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public static Harness Create(string bankStatus = "OPERATING", bool withProduct = true)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-open", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(bankStatus, withProduct);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway =
                new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Queries = new BankQueryApplicationService(
                new SqliteBankingReadGateway(harness.ConnectionFactory));

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(string bankStatus, bool withProduct)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                    '{bankStatus}', {Blob(4)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
                VALUES({Blob(6)}, {Blob(5)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

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
                """);

            if (!withProduct)
            {
                return;
            }

            Execute($"""
                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                    effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                    daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                    deposit_insurance_class_code, overdraft_policy, created_at)
                VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                    'INTERNAL', 'STANDARD', 'NONE', 1);
                """);
        }

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

        public async Task<CustomerAccountId> RegisterAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> result = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            return result.Value.Id;
        }

        public Task<Result<AccountOpeningView>> OpenAsync(
            CustomerAccountId customerAccountId,
            string institutionCode = Institution) =>
            Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, customerAccountId, institutionCode),
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
    public async Task OpeningCreatesAccountLedgerAndProjection()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(DepositAccountStatus.Active, result.Value.Status);
        Assert.AreEqual(Institution, result.Value.InstitutionCode);
        Assert.AreEqual("0000000001", result.Value.AccountNumber);

        Assert.AreEqual(1L, harness.Count("deposit_accounts"));
        Assert.AreEqual(1L, harness.Count("bank_customer_relationships"));
        Assert.AreEqual(1L, harness.Count("ledger_balance_projections"));
        Assert.AreEqual(2L, harness.Count("ledger_accounts"));
    }

    [TestMethod]
    public async Task OpenedAccountStartsAtZeroBalance()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Assert.AreEqual(
            "0",
            harness.ReadText("SELECT CAST(posted_balance_minor AS TEXT) FROM ledger_balance_projections;"));
        Assert.AreEqual(
            "0",
            harness.ReadText("SELECT CAST(held_minor AS TEXT) FROM ledger_balance_projections;"));
    }

    [TestMethod]
    public async Task PostingAccountIsLinkedOneToOneWithDepositAccount()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Assert.AreEqual(
            "1",
            harness.ReadText("SELECT CAST(posting_allowed AS TEXT) FROM ledger_accounts WHERE account_code <> '2000';"));
        Assert.AreEqual(
            "DepositAccount",
            harness.ReadText("SELECT owner_reference_type FROM ledger_accounts WHERE account_code <> '2000';"));
    }

    [TestMethod]
    public async Task RelationshipBecomesActive()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM bank_customer_relationships;"));
        Assert.AreEqual("0000000001", harness.ReadText("SELECT customer_number FROM bank_customer_relationships;"));
    }

    [TestMethod]
    public async Task SecondOpeningAtTheSameBankIsRejected()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Result<AccountOpeningView> second = await harness.OpenAsync(customer);

        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(1L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task DistinctCustomersReceiveDistinctAccountNumbers()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId first = await harness.RegisterAsync(FirstUser, "taro");
        CustomerAccountId second = await harness.RegisterAsync(SecondUser, "hanako");

        Result<AccountOpeningView> firstAccount = await harness.OpenAsync(first);
        Result<AccountOpeningView> secondAccount = await harness.OpenAsync(second);

        Assert.AreEqual("0000000001", firstAccount.Value.AccountNumber);
        Assert.AreEqual("0000000002", secondAccount.Value.AccountNumber);
        Assert.AreEqual(2L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task UnknownBankIsRejected()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer, "NUM9999");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankNotFound, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task BankWithoutAPolicyVersionRejectsOpening()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        harness.Execute("UPDATE banks SET current_policy_version_id = NULL;");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankPolicyUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task BankWithoutAFeeScheduleRejectsOpening()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        harness.Execute("UPDATE banks SET current_fee_schedule_version_id = NULL;");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.FeeScheduleUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task OpeningRecordsAnApprovedApplication()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1L, harness.Count("account_opening_applications"));
        Assert.AreEqual(
            "COMPLETED",
            harness.ReadText("SELECT status FROM account_opening_applications;"));
    }

    [TestMethod]
    public async Task MalformedInstitutionCodeIsRejected()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer, "abc");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
    }

    [TestMethod]
    [DataRow("PENDING_ACTIVATION")]
    [DataRow("RESTRICTED")]
    [DataRow("SETTLEMENT_SUSPENDED")]
    [DataRow("CLOSING")]
    public async Task NonOperatingBankRejectsOpening(string status)
    {
        await using Harness harness = Harness.Create(bankStatus: status);
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankNotOperating, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("deposit_accounts"));
    }

    [TestMethod]
    public async Task BankWithoutActiveProductRejectsOpening()
    {
        await using Harness harness = Harness.Create(withProduct: false);
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Result<AccountOpeningView> result = await harness.OpenAsync(customer);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.BankUnavailable, result.Error!.Category);
    }

    [TestMethod]
    public async Task UnknownCustomerIsRejected()
    {
        await using Harness harness = Harness.Create();

        Result<AccountOpeningView> result = await harness.OpenAsync(
            CustomerAccountId.FromValue(EntityIdValue.FromBits(999)));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CustomerAccountNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task FailedOpeningLeavesNoLedgerAccount()
    {
        await using Harness harness = Harness.Create(bankStatus: "RESTRICTED");
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        await harness.OpenAsync(customer);

        Assert.AreEqual(1L, harness.Count("ledger_accounts"));
        Assert.AreEqual(0L, harness.Count("ledger_balance_projections"));
        Assert.AreEqual(0L, harness.Count("bank_customer_relationships"));
    }

    [TestMethod]
    public async Task ConcurrentOpeningsProduceOneAccount()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");

        Task<Result<AccountOpeningView>>[] attempts =
        [
            harness.OpenAsync(customer),
            harness.OpenAsync(customer),
            harness.OpenAsync(customer),
            harness.OpenAsync(customer),
        ];

        Result<AccountOpeningView>[] results = await Task.WhenAll(attempts);

        Assert.AreEqual(1L, harness.Count("deposit_accounts"));
        Assert.AreEqual(1L, harness.Count("ledger_balance_projections"));
        Assert.AreEqual(4, results.Count(static result => result.IsSuccess));
        Assert.AreEqual(1, results.Select(static result => result.Value.Id).Distinct().Count());
    }

    [TestMethod]
    public async Task OpeningEmitsOutboxEvent()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Assert.AreEqual(2L, harness.Count("outbox_events"));
        Assert.AreEqual(
            BankAccountApplicationService.OpenedEventType,
            harness.ReadText($"""
                SELECT event_type FROM outbox_events
                WHERE event_type = '{BankAccountApplicationService.OpenedEventType}';
                """));
    }

    [TestMethod]
    public async Task OpeningRecordsCommittedBusinessOperation()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        await harness.OpenAsync(customer);

        Assert.AreEqual(2L, harness.Count("business_operations"));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText("""
                SELECT status FROM business_operations WHERE idempotency_scope = 'ACCOUNT_OPEN';
                """));
    }

    [TestMethod]
    public async Task ListBanksReturnsTheSeededBank()
    {
        await using Harness harness = Harness.Create();

        Result<BankPageView> result = await harness.Queries.ListBanksAsync(
            new ListBanksQuery(GuildId, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Institution, result.Value.Items.Single().InstitutionCode);
        Assert.IsNull(result.Value.NextCursor);
    }

    [TestMethod]
    public async Task ListBanksIsNotFoundForAGuildWithoutAnEconomy()
    {
        await using Harness harness = Harness.Create();

        Result<BankPageView> result = await harness.Queries.ListBanksAsync(
            new ListBanksQuery(999UL, null), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.GuildEconomyNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task BankDetailCarriesTheOpeningFlag()
    {
        await using Harness harness = Harness.Create();

        Result<BankDetailView> result = await harness.Queries.GetBankDetailAsync(
            new GetBankDetailQuery(GuildId, Institution), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(BankStatus.Operating, result.Value.Status);
        Assert.IsTrue(result.Value.AcceptsAccountOpening);
    }

    [TestMethod]
    public async Task AnUnknownBankIsNotFound()
    {
        await using Harness harness = Harness.Create();

        Result<BankDetailView> result = await harness.Queries.GetBankDetailAsync(
            new GetBankDetailQuery(GuildId, "NUM9999"), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankNotFound, result.Error!.Code);
    }

    [TestMethod]
    public async Task ListProductsReturnsTheDefaultProduct()
    {
        await using Harness harness = Harness.Create();

        Result<BankProductPageView> result = await harness.Queries.ListBankProductsAsync(
            new ListBankProductsQuery(GuildId, Institution, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value.Items.Single().IsDefault);
    }

    [TestMethod]
    public async Task CustomerAccountsAreListedAfterOpening()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Result<BankAccountPageView> result = await harness.Queries.ListCustomerBankAccountsAsync(
            new ListCustomerBankAccountsQuery(customer, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        BankAccountItem item = result.Value.Items.Single();
        Assert.AreEqual(opened.Value.Id, item.DepositAccountId);
        Assert.AreEqual(Institution, item.InstitutionCode);
        Assert.AreEqual(4, item.AccountNumberSuffix.Length);
    }

    [TestMethod]
    public async Task AccountDetailCarriesTheCanonicalFields()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Result<DepositAccountDetailView> result = await harness.Queries.GetDepositAccountDetailAsync(
            new GetDepositAccountDetailQuery(customer, opened.Value.Id), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Institution, result.Value.InstitutionCode);
        Assert.AreEqual(DepositAccountStatus.Active, result.Value.Status);
        Assert.AreEqual(
            result.Value.PostedBalance.Value - result.Value.HeldAmount.Value,
            result.Value.AvailableBalance.Value);
    }

    [TestMethod]
    public async Task AnotherCustomerCannotSeeTheAccountDetail()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(FirstUser, "taro");
        Result<AccountOpeningView> opened = await harness.OpenAsync(owner);
        CustomerAccountId intruder = await harness.RegisterAsync(SecondUser, "hanako");

        Result<DepositAccountDetailView> result = await harness.Queries.GetDepositAccountDetailAsync(
            new GetDepositAccountDetailQuery(intruder, opened.Value.Id), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
    }

    [TestMethod]
    public async Task TheStatementOfANewAccountIsEmpty()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId customer = await harness.RegisterAsync(FirstUser, "taro");
        Result<AccountOpeningView> opened = await harness.OpenAsync(customer);

        Result<AccountStatementPageView> result = await harness.Queries.GetAccountStatementAsync(
            new GetAccountStatementQuery(customer, opened.Value.Id, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value.Items.Count);
        Assert.IsNull(result.Value.NextCursor);
    }

    [TestMethod]
    public async Task AStatementForAForeignAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        CustomerAccountId owner = await harness.RegisterAsync(FirstUser, "taro");
        Result<AccountOpeningView> opened = await harness.OpenAsync(owner);
        CustomerAccountId intruder = await harness.RegisterAsync(SecondUser, "hanako");

        Result<AccountStatementPageView> result = await harness.Queries.GetAccountStatementAsync(
            new GetAccountStatementQuery(intruder, opened.Value.Id, null), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
    }
}
