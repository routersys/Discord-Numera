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
}
