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
public sealed class PaymentManagementTests
{
    private const ulong GuildId = 970UL;
    private const string Institution = "NUM0001";
    private const ulong FirstUser = 770_000_000_000_000_001UL;
    private const ulong SecondUser = 770_000_000_000_000_002UL;
    private const ulong ThirdUser = 770_000_000_000_000_003UL;

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

        public PaymentManagementApplicationService Payments { get; private set; } = null!;

        public ExpiryMaintenanceService Expiries { get; private set; } = null!;

        public ScheduledPaymentMaintenanceService Scheduled { get; private set; } = null!;

        public SqliteBankingWriteGateway Gateway { get; private set; } = null!;

        public DirectDebitCollectionRailService Rail { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-paymgmt", Guid.NewGuid().ToString("n"));
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
            harness.Payments = new PaymentManagementApplicationService(gateway, harness.Clock, ids);
            harness.Expiries = new ExpiryMaintenanceService(gateway, harness.Clock);
            harness.Gateway = gateway;
            harness.Rail = new DirectDebitCollectionRailService(ids);
            harness.Scheduled = new ScheduledPaymentMaintenanceService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);

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

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
            VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(5)}, {Blob(1)}, {Blob(3)}, '{Institution}', 'ヌメラ銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES({Blob(6)}, {Blob(5)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(7)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', 1);

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

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(32)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(33)}, {Blob(4)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(35)}, {Blob(4)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT',
                {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            VALUES({Blob(35)}, 0, 0, 1, 1);

            INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                free_occurrences_per_business_month)
            VALUES({Blob(34)}, {Blob(31)}, 'SAME_BANK_TRANSFER', 0, 'ANY', NULL, NULL, NULL, 0, NULL,
                'ANY', NULL, NULL, 0, 0, 0, NULL, NULL, 0);

            UPDATE banks
            SET current_policy_version_id = {Blob(30)},
                current_fee_schedule_version_id = {Blob(31)},
                version = version + 1
            WHERE bank_id = {Blob(5)};
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Fund(DepositAccountId accountId, long amount)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ledger_balance_projections
                SET posted_balance_minor = $amount, version = version + 1
                WHERE ledger_account_id = (
                    SELECT ledger_account_id FROM deposit_accounts WHERE deposit_account_id = $id);
                """;
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public long Balance(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(p.posted_balance_minor, 0)
                FROM deposit_accounts a
                LEFT JOIN ledger_balance_projections p ON p.ledger_account_id = a.ledger_account_id
                WHERE a.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public void Close(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deposit_accounts
                SET status = 'CLOSED_USER', closed_at = 1, closure_reason = 'USER', version = version + 1
                WHERE deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public void FundByJournal(DepositAccountId accountId, long amount, int seed)
        {
            Execute($"""
                INSERT INTO business_operations(business_operation_id, operation_type, economy_scope_id,
                    actor_party_id, correlation_id, idempotency_scope, idempotency_key, status,
                    created_at, committed_at, version)
                VALUES({Blob(seed)}, 'TEST_FUNDING', {Blob(1)}, {Blob(3)}, {Blob(seed + 1)},
                    'TEST_FUNDING', 'fund-{seed}', 'COMMITTED', 1, 1, 1);

                INSERT INTO accounting_transactions(accounting_transaction_id, accounting_book_id,
                    accounting_period_id, business_operation_id, currency_id, transaction_type,
                    business_date, occurred_at, posted_at, reverses_transaction_id, status, version)
                VALUES({Blob(seed + 2)}, {Blob(4)}, {Blob(32)}, {Blob(seed)}, {Blob(2)}, 'TEST_FUNDING',
                    '2026-04-12', 1, 1, NULL, 'POSTED', 1);

                INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id,
                    ledger_account_id, entry_sequence, side, amount_minor, created_at)
                VALUES({Blob(seed + 3)}, {Blob(seed + 2)}, {Blob(35)}, 0, 'DEBIT', {amount}, 1);

                INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id,
                    ledger_account_id, entry_sequence, side, amount_minor, created_at)
                SELECT {Blob(seed + 4)}, {Blob(seed + 2)}, ledger_account_id, 1, 'CREDIT', {amount}, 1
                FROM deposit_accounts WHERE deposit_account_id = {Literal(accountId)};

                UPDATE ledger_balance_projections
                SET posted_balance_minor = posted_balance_minor + {amount}, version = version + 1
                WHERE ledger_account_id = {Blob(35)};

                UPDATE ledger_balance_projections
                SET posted_balance_minor = posted_balance_minor + {amount}, version = version + 1
                WHERE ledger_account_id = (
                    SELECT ledger_account_id FROM deposit_accounts
                    WHERE deposit_account_id = {Literal(accountId)});
                """);
        }

        private static string Literal(DepositAccountId accountId) =>
            "x'" + Convert.ToHexString(accountId.Value.ToByteArray()).ToLowerInvariant() + "'";

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

        public async Task<(CustomerAccountId Customer, DepositAccountId Account)> OpenAsync(
            ulong discordUserId,
            string handle)
        {
            Result<CustomerAccountView> customer = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, customer.Value.Id, Institution),
                CancellationToken.None);

            return (customer.Value.Id, opened.Value.Id);
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
    public async Task SavingABeneficiarySnapshotsTheRouting()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, _) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId target) = await harness.OpenAsync(SecondUser, "jiro");

        Result<SavedBeneficiaryView> result = await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎"), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("次郎", result.Value.DisplayName);
        Assert.AreEqual(Institution, result.Value.InstitutionCode);
        Assert.AreEqual(SavedBeneficiaryStatus.Active, result.Value.Status);
        Assert.AreEqual("001", harness.ReadText("SELECT branch_code_snapshot FROM saved_beneficiaries;"));
    }

    [TestMethod]
    public async Task ASecondActiveBeneficiaryForTheSameAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, _) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId target) = await harness.OpenAsync(SecondUser, "jiro");

        Assert.IsTrue((await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎"), CancellationToken.None)).IsSuccess);

        Result<SavedBeneficiaryView> second = await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎2"), CancellationToken.None);

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, second.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BeneficiaryAlreadySaved, second.Error.Code);
    }

    [TestMethod]
    public async Task HidingABeneficiaryRemovesItFromTheActiveUniqueness()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, _) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId target) = await harness.OpenAsync(SecondUser, "jiro");

        Result<SavedBeneficiaryView> saved = await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎"), CancellationToken.None);

        Result hidden = await harness.Payments.HideBeneficiaryAsync(
            new HideBeneficiaryCommand(owner, saved.Value.Id), CancellationToken.None);

        Assert.IsTrue(hidden.IsSuccess);
        Assert.AreEqual("HIDDEN", harness.ReadText("SELECT status FROM saved_beneficiaries;"));

        Result<SavedBeneficiaryView> resaved = await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎"), CancellationToken.None);

        Assert.IsTrue(resaved.IsSuccess);
        Assert.AreEqual(2L, harness.Count("saved_beneficiaries"));
    }

    [TestMethod]
    public async Task AnotherCustomerCannotHideABeneficiary()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, _) = await harness.OpenAsync(FirstUser, "taro");
        (CustomerAccountId intruder, DepositAccountId target) = await harness.OpenAsync(SecondUser, "jiro");

        Result<SavedBeneficiaryView> saved = await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, target, "次郎"), CancellationToken.None);

        Result result = await harness.Payments.HideBeneficiaryAsync(
            new HideBeneficiaryCommand(intruder, saved.Value.Id), CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BeneficiaryNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task ListingBeneficiariesReturnsTheSavedEntries()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, _) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId second) = await harness.OpenAsync(SecondUser, "jiro");
        (_, DepositAccountId third) = await harness.OpenAsync(ThirdUser, "saburo");

        await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, second, "次郎"), CancellationToken.None);
        await harness.Payments.SaveBeneficiaryAsync(
            new SaveBeneficiaryCommand(owner, third, "三郎"), CancellationToken.None);

        Result<SavedBeneficiaryPageView> result = await harness.Payments.ListBeneficiariesAsync(
            new ListBeneficiariesQuery(owner, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.Items.Count);
        Assert.IsNull(result.Value.NextCursor);
    }

    [TestMethod]
    public async Task CreatingAMonthlyPlanSchedulesTheFirstOccurrence()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        Result<ScheduledPaymentPlanView> result = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, ScheduledPaymentKind.Monthly, 1_000, 540, 15),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(ScheduledPaymentKind.Monthly, result.Value.Kind);
        Assert.AreEqual(ScheduledPaymentPlanStatus.Active, result.Value.Status);
        Assert.IsNotNull(result.Value.NextDueAt);
        Assert.AreEqual(1L, harness.Count("scheduled_payment_plans"));
        Assert.AreEqual(1L, harness.Count("scheduled_payment_occurrences"));
        Assert.AreEqual(
            "Asia/Tokyo", harness.ReadText("SELECT canonical_timezone FROM scheduled_payment_plans;"));
    }

    [TestMethod]
    public async Task AMonthlyPlanRequiresAnAnchorDay()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        Result<ScheduledPaymentPlanView> result = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, ScheduledPaymentKind.Monthly, 1_000, 540, null),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.ScheduledPaymentScheduleInvalid, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("scheduled_payment_plans"));
    }

    [TestMethod]
    public async Task APlanCannotTargetItsOwnSourceAccount()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");

        Result<ScheduledPaymentPlanView> result = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, source, ScheduledPaymentKind.Weekly, 1_000, 540, null),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
    }

    [TestMethod]
    public async Task PausingAndResumingAPlanKeepsItSchedulable()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        Result<ScheduledPaymentPlanView> created = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, ScheduledPaymentKind.Weekly, 1_000, 540, null),
            CancellationToken.None);

        Result<ScheduledPaymentPlanView> paused = await harness.Payments.SetScheduledPaymentStateAsync(
            new SetScheduledPaymentStateCommand(
                owner, created.Value.Id, ScheduledPaymentPlanStatus.Paused),
            CancellationToken.None);

        Assert.IsTrue(paused.IsSuccess);
        Assert.AreEqual(ScheduledPaymentPlanStatus.Paused, paused.Value.Status);

        Result<ScheduledPaymentPlanView> resumed = await harness.Payments.SetScheduledPaymentStateAsync(
            new SetScheduledPaymentStateCommand(
                owner, created.Value.Id, ScheduledPaymentPlanStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(resumed.IsSuccess);
        Assert.AreEqual(ScheduledPaymentPlanStatus.Active, resumed.Value.Status);
        Assert.IsTrue(resumed.Value.NextDueAt > created.Value.NextDueAt);
    }

    [TestMethod]
    public async Task CancellingAPlanClearsItsNextDueDate()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        Result<ScheduledPaymentPlanView> created = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, ScheduledPaymentKind.Once, 1_000, 540, null),
            CancellationToken.None);

        Result<ScheduledPaymentPlanView> cancelled = await harness.Payments.SetScheduledPaymentStateAsync(
            new SetScheduledPaymentStateCommand(
                owner, created.Value.Id, ScheduledPaymentPlanStatus.Cancelled),
            CancellationToken.None);

        Assert.IsTrue(cancelled.IsSuccess);
        Assert.IsNull(cancelled.Value.NextDueAt);
        Assert.AreEqual(
            0L, harness.Count("scheduled_payment_plans WHERE next_due_at IS NOT NULL"));
    }

    [TestMethod]
    public async Task AMandateStartsPendingAndActivatesOnDebtorConsent()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        Result<DirectDebitMandateView> created = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, creditorAccount, 5_000, null),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(DirectDebitMandateStatus.Pending, created.Value.Status);

        Result<DirectDebitMandateView> activated = await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(activated.IsSuccess);
        Assert.AreEqual(DirectDebitMandateStatus.Active, activated.Value.Status);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM direct_debit_mandates;"));
    }

    [TestMethod]
    public async Task AMandatePastItsValidityIsExpiredByMaintenance()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        Result<DirectDebitMandateView> created = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(
                debtor, debtorAccount, creditorAccount, 5_000, 1_776_000_060_000L),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        Assert.IsTrue((await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Active),
            CancellationToken.None)).IsSuccess);

        harness.Clock.Advance(120_000L);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Mandates);
        Assert.AreEqual("EXPIRED", harness.ReadText("SELECT status FROM direct_debit_mandates;"));
    }

    [TestMethod]
    public async Task RevokingAMandateRecordsATerminationInstant()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        Result<DirectDebitMandateView> created = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, creditorAccount, 5_000, null),
            CancellationToken.None);

        await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Active),
            CancellationToken.None);

        Result<DirectDebitMandateView> revoked = await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Revoked),
            CancellationToken.None);

        Assert.IsTrue(revoked.IsSuccess);
        Assert.AreEqual(DirectDebitMandateStatus.Revoked, revoked.Value.Status);
        Assert.AreEqual(
            0L, harness.Count("direct_debit_mandates WHERE terminated_at IS NULL"));
    }

    [TestMethod]
    public async Task ARevokedMandateCannotBeReactivated()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        Result<DirectDebitMandateView> created = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, creditorAccount, 5_000, null),
            CancellationToken.None);

        await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Revoked),
            CancellationToken.None);

        Result<DirectDebitMandateView> result = await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Active),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Conflict, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DirectDebitMandateStateInvalid, result.Error.Code);
    }

    [TestMethod]
    public async Task AMandateCannotSettleIntoTheDebtorsOwnAccount()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");

        Result<DirectDebitMandateView> result = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, debtorAccount, 5_000, null),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DirectDebitMandateInvalid, result.Error.Code);
    }

    [TestMethod]
    public async Task ListingMandatesReturnsTheDebtorsMandates()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, creditorAccount, 5_000, null),
            CancellationToken.None);

        Result<DirectDebitMandatePageView> result = await harness.Payments.ListDirectDebitMandatesAsync(
            new ListDirectDebitMandatesQuery(debtor, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Items.Count);
        Assert.AreEqual(DirectDebitMandateStatus.Pending, result.Value.Items[0].Status);
    }

    [TestMethod]
    public async Task ListingScheduledPaymentsReturnsThePlans()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, ScheduledPaymentKind.Weekly, 1_000, 540, null),
            CancellationToken.None);

        Result<ScheduledPaymentPageView> result = await harness.Payments.ListScheduledPaymentsAsync(
            new ListScheduledPaymentsQuery(owner, null), CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Items.Count);
        Assert.AreEqual(ScheduledPaymentKind.Weekly, result.Value.Items[0].Kind);
    }

    private const long ThirtyDays = 30L * 24 * 60 * 60 * 1000;

    private static async Task<(CustomerAccountId Owner, DepositAccountId Source, DepositAccountId Destination)>
        PlanAsync(Harness harness, ScheduledPaymentKind kind, long amount)
    {
        (CustomerAccountId owner, DepositAccountId source) = await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId destination) = await harness.OpenAsync(SecondUser, "jiro");

        Result<ScheduledPaymentPlanView> created = await harness.Payments.CreateScheduledPaymentAsync(
            new CreateScheduledPaymentCommand(
                GuildId, owner, source, destination, kind, amount, 540, null),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess);

        return (owner, source, destination);
    }

    [TestMethod]
    public async Task ADueOccurrenceMovesMoneyAndSchedulesTheNextOne()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId source, DepositAccountId destination) =
            await PlanAsync(harness, ScheduledPaymentKind.Weekly, 1_000);

        harness.Fund(source, 10_000);
        harness.Clock.Advance(ThirtyDays);

        ScheduledPaymentMaintenanceReport report =
            await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Occurrences);
        Assert.AreEqual(1, report.Executed);
        Assert.AreEqual(
            "SUCCEEDED",
            harness.ReadText(
                "SELECT status FROM scheduled_payment_occurrences ORDER BY scheduled_for LIMIT 1;"));
        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM scheduled_payment_plans;"));
        Assert.AreEqual(2L, harness.Count("scheduled_payment_occurrences"));
        Assert.AreEqual(9_000L, harness.Balance(source));
        Assert.AreEqual(1_000L, harness.Balance(destination));
    }

    [TestMethod]
    public async Task AnUnfundedOccurrenceEndsInFailedFundsAndKeepsThePlanActive()
    {
        await using Harness harness = Harness.Create();
        await PlanAsync(harness, ScheduledPaymentKind.Weekly, 1_000);

        harness.Clock.Advance(ThirtyDays);

        ScheduledPaymentMaintenanceReport report =
            await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(0, report.Executed);
        Assert.AreEqual(
            "FAILED_FUNDS",
            harness.ReadText(
                "SELECT status FROM scheduled_payment_occurrences ORDER BY scheduled_for LIMIT 1;"));
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM scheduled_payment_plans;"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task AOncePlanCompletesAfterItsOnlyOccurrence()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId source, _) = await PlanAsync(harness, ScheduledPaymentKind.Once, 1_000);

        harness.Fund(source, 10_000);
        harness.Clock.Advance(ThirtyDays);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM scheduled_payment_plans;"));
        Assert.AreEqual(1L, harness.Count("scheduled_payment_occurrences"));
    }

    [TestMethod]
    public async Task AClosedDestinationCancelsTheRecurringPlan()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId source, DepositAccountId destination) =
            await PlanAsync(harness, ScheduledPaymentKind.Weekly, 1_000);

        harness.Fund(source, 10_000);
        harness.Close(destination);
        harness.Clock.Advance(ThirtyDays);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(
            "FAILED_DESTINATION",
            harness.ReadText("SELECT status FROM scheduled_payment_occurrences;"));
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM scheduled_payment_plans;"));
        Assert.AreEqual(1L, harness.Count("scheduled_payment_occurrences"));
    }

    [TestMethod]
    public async Task APausedPlanCancelsItsPendingOccurrence()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId owner, DepositAccountId source, _) =
            await PlanAsync(harness, ScheduledPaymentKind.Weekly, 1_000);

        harness.Fund(source, 10_000);

        Result<ScheduledPaymentPageView> plans = await harness.Payments.ListScheduledPaymentsAsync(
            new ListScheduledPaymentsQuery(owner, null), CancellationToken.None);

        await harness.Payments.SetScheduledPaymentStateAsync(
            new SetScheduledPaymentStateCommand(
                owner, plans.Value.Items[0].Id, ScheduledPaymentPlanStatus.Paused),
            CancellationToken.None);

        harness.Clock.Advance(ThirtyDays);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM scheduled_payment_occurrences;"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task ASecondPassOverTheSameOccurrenceMovesMoneyOnlyOnce()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId source, DepositAccountId destination) =
            await PlanAsync(harness, ScheduledPaymentKind.Once, 1_000);

        harness.Fund(source, 10_000);
        harness.Clock.Advance(ThirtyDays);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);
        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(1_000L, harness.Balance(destination));
    }

    private static async Task<DirectDebitMandateId> MandateAsync(
        Harness harness,
        CustomerAccountId debtor,
        DepositAccountId debtorAccount,
        DepositAccountId creditorAccount)
    {
        Result<DirectDebitMandateView> created = await harness.Payments.CreateDirectDebitMandateAsync(
            new CreateDirectDebitMandateCommand(debtor, debtorAccount, creditorAccount, 5_000, null),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess);

        Result<DirectDebitMandateView> activated = await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(
                debtor, created.Value.Id, DirectDebitMandateStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(activated.IsSuccess);

        return created.Value.Id;
    }

    private static Task<Result<DirectDebitCollection>> RequestCollectionAsync(
        Harness harness,
        DirectDebitMandateId mandateId,
        string reference,
        long amount) =>
        harness.Gateway.ExecuteAsync(
            unitOfWork => harness.Rail.Request(
                unitOfWork,
                new DirectDebitCollectionRequest(
                    mandateId, reference, MoneyMinor.FromMinor(amount), harness.Clock.Now())),
            CancellationToken.None);

    [TestMethod]
    public async Task ADueCollectionSettlesThroughTheNormalPaymentOrder()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        DirectDebitMandateId mandate = await MandateAsync(
            harness, debtor, debtorAccount, creditorAccount);

        harness.Fund(debtorAccount, 10_000);

        Assert.IsTrue((await RequestCollectionAsync(harness, mandate, "INV-1", 1_500)).IsSuccess);

        ScheduledPaymentMaintenanceReport report =
            await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Collections);
        Assert.AreEqual(1, report.Executed);
        Assert.AreEqual("SETTLED", harness.ReadText("SELECT status FROM direct_debit_collections;"));
        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(8_500L, harness.Balance(debtorAccount));
        Assert.AreEqual(1_500L, harness.Balance(creditorAccount));
    }

    [TestMethod]
    public async Task ACollectionAboveTheMandateLimitIsRefusedAtRequestTime()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        DirectDebitMandateId mandate = await MandateAsync(
            harness, debtor, debtorAccount, creditorAccount);

        Result<DirectDebitCollection> result = await RequestCollectionAsync(
            harness, mandate, "INV-1", 5_001);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DirectDebitCollectionAmountInvalid, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("direct_debit_collections"));
    }

    [TestMethod]
    public async Task ACollectionAgainstARevokedMandateFailsWithoutMovingMoney()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        DirectDebitMandateId mandate = await MandateAsync(
            harness, debtor, debtorAccount, creditorAccount);

        harness.Fund(debtorAccount, 10_000);

        Assert.IsTrue((await RequestCollectionAsync(harness, mandate, "INV-1", 1_500)).IsSuccess);

        await harness.Payments.SetDirectDebitMandateStateAsync(
            new SetDirectDebitMandateStateCommand(debtor, mandate, DirectDebitMandateStatus.Revoked),
            CancellationToken.None);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual("FAILED_MANDATE", harness.ReadText("SELECT status FROM direct_debit_collections;"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual(10_000L, harness.Balance(debtorAccount));
    }

    [TestMethod]
    public async Task AClosedDebtorAccountRevokesTheMandate()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        DirectDebitMandateId mandate = await MandateAsync(
            harness, debtor, debtorAccount, creditorAccount);

        harness.Fund(debtorAccount, 10_000);

        Assert.IsTrue((await RequestCollectionAsync(harness, mandate, "INV-1", 1_500)).IsSuccess);

        harness.Close(debtorAccount);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual("FAILED_ACCOUNT", harness.ReadText("SELECT status FROM direct_debit_collections;"));
        Assert.AreEqual("REVOKED", harness.ReadText("SELECT status FROM direct_debit_mandates;"));
    }

    [TestMethod]
    public async Task RepeatingACollectionReferenceReturnsThePendingCollection()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId debtor, DepositAccountId debtorAccount) =
            await harness.OpenAsync(FirstUser, "taro");
        (_, DepositAccountId creditorAccount) = await harness.OpenAsync(SecondUser, "jiro");

        DirectDebitMandateId mandate = await MandateAsync(
            harness, debtor, debtorAccount, creditorAccount);

        Result<DirectDebitCollection> first = await RequestCollectionAsync(
            harness, mandate, "INV-1", 1_500);
        Result<DirectDebitCollection> second = await RequestCollectionAsync(
            harness, mandate, "INV-1", 1_500);

        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(first.Value.Id, second.Value.Id);
        Assert.AreEqual(1L, harness.Count("direct_debit_collections"));
    }

    [TestMethod]
    public async Task ARealTransferLeavesTheLedgerReconciled()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId source, _) =
            await PlanAsync(harness, ScheduledPaymentKind.Weekly, 1_000);

        harness.FundByJournal(source, 10_000, 200);
        harness.Clock.Advance(ThirtyDays);

        await harness.Scheduled.ProcessDueAsync(CancellationToken.None);

        int next = 0xE0;
        SqliteDatabaseReconciliationRunner runner = new(
            harness.ConnectionFactory,
            () =>
            {
                byte[] id = new byte[16];
                id[14] = (byte)(next >> 8);
                id[15] = (byte)next++;
                return id;
            });

        ReconciliationOutcome financial = runner.RunFinancialReconciliation(
            harness.Clock.Now().UnixMilliseconds);
        ReconciliationOutcome orphans = runner.VerifyNoOrphanState(
            harness.Clock.Now().UnixMilliseconds);

        Assert.IsTrue(financial.IsOk, string.Join(",", financial.Findings.Select(f => f.IssueCode)));
        Assert.IsTrue(orphans.IsOk, string.Join(",", orphans.Findings.Select(f => f.IssueCode)));
        Assert.AreEqual(0L, harness.Count("reconciliation_issues"));
    }
}
