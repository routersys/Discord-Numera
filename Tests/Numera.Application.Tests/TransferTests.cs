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
public sealed class TransferTests
{
    private const ulong GuildId = 900UL;

    private const string Institution = "NUM0001";
    private const string OtherInstitution = "NUM0002";
    private const string Branch = "001";
    private const ulong PayerUser = 710_000_000_000_000_001UL;
    private const ulong PayeeUser = 710_000_000_000_000_002UL;
    private const int FreeScheduleSeed = 40;
    private const int PricedScheduleSeed = 60;
    private const int UnlimitedPolicySeed = 80;
    private const int CappedPolicySeed = 81;

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

        public PaymentApplicationService Payments { get; private set; } = null!;

        public SettlementMaintenanceService Maintenance { get; private set; } = null!;

        public EconomyScopeId Scope { get; } = EconomyScopeId.FromValue(EntityIdValue.FromBits(1));

        public static Harness Create(
            bool withPeriod = true,
            bool withSecondBank = false,
            bool withSettlement = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-transfer", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(withPeriod, withSecondBank || withSettlement, withSettlement);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(gateway, harness.Clock, ids);
            harness.Payments = new PaymentApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Maintenance = new SettlementMaintenanceService(
                gateway, harness.Payments, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(bool withPeriod, bool withSecondBank, bool withSettlement)
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
                    'OPERATING', {Blob(4)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
                VALUES({Blob(6)}, {Blob(5)}, '{Branch}', '本店', 'ACTIVE', 1, NULL, 1);

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

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(11)}, {Blob(4)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);
                """);

            PublishBankLimits(UnlimitedPolicySeed);
            PublishTransferFee(FreeScheduleSeed, fixedMinor: 0);
            PublishTransferFee(FreeScheduleSeed, fixedMinor: 0, priority: 1, feeType: "INTERBANK_TRANSFER");

            if (withPeriod)
            {
                Execute($"""
                    INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                        starts_on, ends_on, status, closed_at, version)
                    VALUES({Blob(10)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);
                    """);
            }

            if (!withSecondBank)
            {
                return;
            }

            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(20)}, 'BANK', '第二銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(21)}, {Blob(20)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(22)}, {Blob(1)}, {Blob(20)}, '{OtherInstitution}', '第二銀行', 'NORMAL', NULL,
                    'OPERATING', {Blob(21)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
                VALUES({Blob(23)}, {Blob(22)}, '{Branch}', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(24)}, {Blob(21)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(25)}, {Blob(22)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                    effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                    daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                    deposit_insurance_class_code, overdraft_policy, created_at)
                VALUES({Blob(26)}, {Blob(25)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                    'INTERNAL', 'STANDARD', 'NONE', 1);
                """);

            if (withSettlement)
            {
                SeedSettlement();
            }
        }

        private void SeedSettlement()
        {
            Execute($"""
                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(113)}, {Blob(21)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(100)}, 'SYSTEM', '中央銀行', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status, created_at, version)
                VALUES({Blob(101)}, {Blob(100)}, 'CENTRAL_BANK', 'OPEN', 1, 1);

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(102)}, {Blob(101)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(103)}, {Blob(101)}, NULL, '2100-1', 'CENTRAL_BANK_SETTLEMENT_LIABILITY', 'LIABILITY',
                        'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(104)}, {Blob(101)}, NULL, '2100-2', 'CENTRAL_BANK_SETTLEMENT_LIABILITY', 'LIABILITY',
                        'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(105)}, {Blob(4)}, NULL, '2200', 'SETTLEMENT_PAYABLE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(106)}, {Blob(4)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(107)}, {Blob(21)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(108)}, {Blob(21)}, NULL, '2300', 'INCOMING_SETTLEMENT_SUSPENSE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                    currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
                VALUES
                    ({Blob(109)}, {Blob(5)}, {Blob(2)}, {Blob(103)}, 'ACTIVE', 1, NULL, 1),
                    ({Blob(110)}, {Blob(22)}, {Blob(2)}, {Blob(104)}, 'ACTIVE', 1, NULL, 1);

                INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                    settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                    effective_to, version)
                VALUES
                    ({Blob(111)}, {Blob(5)}, 'DIRECT', NULL, {Blob(109)}, 'ACTIVE', 1, NULL, 1),
                    ({Blob(112)}, {Blob(22)}, 'DIRECT', NULL, {Blob(110)}, 'ACTIVE', 1, NULL, 1);
                """);
        }

        public void FundReserve(long amount)
        {
            Execute($"""
                INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                    version, updated_at)
                VALUES({Blob(106)}, {amount}, 0, 1, 1)
                ON CONFLICT(ledger_account_id) DO UPDATE SET
                    posted_balance_minor = {amount}, version = version + 1;

                INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                    version, updated_at)
                VALUES({Blob(103)}, {amount}, 0, 1, 1)
                ON CONFLICT(ledger_account_id) DO UPDATE SET
                    posted_balance_minor = {amount}, version = version + 1;
                """);
        }

        public void MakeDestinationIndirect()
        {
            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(120)}, 'BANK', '代理決済銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(121)}, {Blob(120)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(122)}, {Blob(1)}, {Blob(120)}, 'NUM0003', '代理決済銀行', 'NORMAL', NULL,
                    'OPERATING', {Blob(121)}, NULL, NULL, 1, 1);

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(123)}, {Blob(121)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(124)}, {Blob(121)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(125)}, {Blob(101)}, NULL, '2100-3', 'CENTRAL_BANK_SETTLEMENT_LIABILITY', 'LIABILITY',
                        'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(128)}, {Blob(121)}, NULL, '2400', 'CLIENT_BANK_SETTLEMENT_DEPOSIT', 'LIABILITY',
                        'CREDIT', {Blob(2)}, 1, 'Bank', {Blob(22)}, 'ACTIVE', 1, 1),
                    ({Blob(129)}, {Blob(21)}, NULL, '1200', 'SETTLEMENT_AGENT_BALANCE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                    currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
                VALUES({Blob(126)}, {Blob(122)}, {Blob(2)}, {Blob(125)}, 'ACTIVE', 1, NULL, 1);

                INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                    settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                    effective_to, version)
                VALUES({Blob(127)}, {Blob(122)}, 'DIRECT', NULL, {Blob(126)}, 'ACTIVE', 1, NULL, 1);

                UPDATE settlement_participations
                SET mode = 'INDIRECT', settlement_agent_bank_id = {Blob(122)},
                    central_bank_settlement_account_id = NULL, version = version + 1
                WHERE bank_id = {Blob(22)};
                """);
        }

        public void SuspendDestinationAgent() => Execute($"""
            UPDATE settlement_participations SET status = 'SUSPENDED', version = version + 1
            WHERE bank_id = {Blob(122)};
            """);

        public long LedgerBalanceOf(int seed) => long.Parse(
            ReadText($"""
                SELECT CAST(COALESCE(posted_balance_minor, 0) AS TEXT)
                FROM ledger_balance_projections WHERE ledger_account_id = {Blob(seed)};
                """) is { Length: > 0 } text ? text : "0",
            System.Globalization.CultureInfo.InvariantCulture);

        public void PublishBankLimits(
            int policySeed,
            long? perTransfer = null,
            long? dailyOutgoing = null,
            long? maximumActiveHolds = null)
        {
            Execute($"""
                INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                    minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                    reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                    debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                    cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                    per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                    daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                    daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                    maximum_active_holds_minor, effective_from, effective_to, version)
                VALUES({Blob(policySeed)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 1, 1, 0, 'NONE', 1, NULL, 12,
                    {Nullable(perTransfer)}, {Nullable(dailyOutgoing)}, NULL, NULL, NULL, NULL, NULL,
                    {Nullable(maximumActiveHolds)}, 1, NULL, 1);

                UPDATE banks SET current_policy_version_id = {Blob(policySeed)}, version = version + 1
                WHERE bank_id = {Blob(5)};
                """);
        }

        public void SetCustomerLimits(
            DepositAccountId depositAccountId,
            long? perTransfer = null,
            long? dailyOutgoing = null)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO account_limit_preferences(deposit_account_id, per_transfer_limit_minor,
                    daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                    daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                    daily_debit_purchase_limit_minor, version)
                VALUES($id, {Nullable(perTransfer)}, {Nullable(dailyOutgoing)}, NULL, NULL, NULL, NULL, 1);
                """;
            command.Parameters.AddWithValue("$id", depositAccountId.Value.ToByteArray());
            command.ExecuteNonQuery();
        }

        public void PublishTransferFee(
            int scheduleSeed,
            long fixedMinor,
            int basisPoints = 0,
            long minimumMinor = 0,
            long? maximumMinor = null,
            string? waiverCounterKey = null,
            int freeOccurrences = 0,
            string dayClass = "ANY",
            int? startMinute = null,
            int? endMinute = null,
            int priority = 0,
            string feeType = "SAME_BANK_TRANSFER")
        {
            Execute($"""
                INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                    effective_to, version)
                SELECT {Blob(scheduleSeed)}, {Blob(5)}, 1, NULL, 1
                WHERE NOT EXISTS(
                    SELECT 1 FROM fee_schedule_versions WHERE fee_schedule_version_id = {Blob(scheduleSeed)});

                INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                    account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                    amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                    basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                    free_occurrences_per_business_month)
                VALUES({Blob(scheduleSeed + 1 + priority)}, {Blob(scheduleSeed)}, '{feeType}',
                    {priority}, 'ANY', NULL, NULL, NULL, 0, NULL, '{dayClass}',
                    {Nullable(startMinute)}, {Nullable(endMinute)}, {fixedMinor}, {basisPoints},
                    {minimumMinor}, {Nullable(maximumMinor)}, {Text(waiverCounterKey)}, {freeOccurrences});

                UPDATE banks SET current_fee_schedule_version_id = {Blob(scheduleSeed)}, version = version + 1
                WHERE bank_id = {Blob(5)};
                """);
        }

        private static string Nullable(long? value) =>
            value is { } present ? present.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";

        private static string Nullable(int? value) =>
            value is { } present ? present.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL";

        private static string Text(string? value) => value is null ? "NULL" : $"'{value}'";

        public void PublishPaymentNetwork(string settlementMode, long? rtgsThreshold)
        {
            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(140)}, 'SYSTEM', '清算機関', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(141)}, {Blob(140)}, 'SYSTEM', 'OPEN', 1, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(142)}, {Blob(141)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT', {Blob(2)}, 1,
                        NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(145)}, {Blob(4)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(146)}, {Blob(21)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(147)}, {Blob(4)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(148)}, {Blob(21)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO payment_networks(payment_network_id, economy_scope_id, network_code, operator_party_id,
                    accounting_book_id, liquid_asset_ledger_account_id, status, current_policy_version_id, version)
                VALUES({Blob(143)}, {Blob(1)}, 'ZENGIN', {Blob(140)}, {Blob(141)}, {Blob(142)}, 'DRAFT', NULL, 1);

                INSERT INTO payment_network_policy_versions(payment_network_policy_version_id, payment_network_id,
                    settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                    clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                    per_bank_precredit_exposure_limit_minor, created_at, version)
                VALUES({Blob(144)}, {Blob(143)}, '{settlementMode}', 'AFTER_FINAL_SETTLEMENT',
                    {Nullable(rtgsThreshold)},
                    {(settlementMode == "CLEARING" ? "3600" : "NULL")}, 0, 10000, 0, 1, 1);

                UPDATE payment_networks
                SET status = 'ACTIVE', current_policy_version_id = {Blob(144)}, version = 2
                WHERE payment_network_id = {Blob(143)};
                """);
        }

        public void PublishPreCreditNetwork(long exposureLimit, long prefundBalance)
        {
            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(140)}, 'SYSTEM', '清算機関', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(141)}, {Blob(140)}, 'SYSTEM', 'OPEN', 1, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
                    account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(142)}, {Blob(141)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT', {Blob(2)}, 1,
                        NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(145)}, {Blob(4)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(146)}, {Blob(21)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(147)}, {Blob(4)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(148)}, {Blob(21)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(149)}, {Blob(141)}, NULL, '2500', 'SUSPENSE_LIABILITY', 'LIABILITY', 'CREDIT',
                        {Blob(2)}, 1, 'BANK', {Blob(5)}, 'ACTIVE', 1, 1);

                INSERT INTO payment_networks(payment_network_id, economy_scope_id, network_code, operator_party_id,
                    accounting_book_id, liquid_asset_ledger_account_id, status, current_policy_version_id, version)
                VALUES({Blob(143)}, {Blob(1)}, 'ZENGIN', {Blob(140)}, {Blob(141)}, {Blob(142)}, 'DRAFT', NULL, 1);

                INSERT INTO payment_network_policy_versions(payment_network_policy_version_id, payment_network_id,
                    settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                    clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                    per_bank_precredit_exposure_limit_minor, created_at, version)
                VALUES({Blob(144)}, {Blob(143)}, 'CLEARING', 'GUARANTEED_PRE_CREDIT', NULL, 3600, 1, 10000,
                    {exposureLimit}, 1, 1);

                UPDATE payment_networks
                SET status = 'ACTIVE', current_policy_version_id = {Blob(144)}, version = 2
                WHERE payment_network_id = {Blob(143)};

                INSERT INTO payment_network_prefunds(payment_network_prefund_id, payment_network_id, bank_id,
                    currency_id, prefund_liability_ledger_account_id, created_at, version)
                VALUES({Blob(150)}, {Blob(143)}, {Blob(5)}, {Blob(2)}, {Blob(149)}, 1, 1);

                INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                    version, updated_at)
                VALUES({Blob(149)}, {prefundBalance}, 0, 1, 1);
                """);
        }

        public void LockClearingCycles() => Execute("""
            UPDATE clearing_cycles
            SET status = 'LOCKED', locked_at = 1, version = version + 1
            WHERE status = 'OPEN';
            """);

        public DepositAccountId RemoteDepositAccountId()
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT deposit_account_id FROM deposit_accounts WHERE bank_id = {Blob(22)} LIMIT 1;
                """;

            return DepositAccountId.FromValue(
                EntityIdValue.FromBytes((byte[])command.ExecuteScalar()!));
        }

        public void SuspendPaymentNetwork() => Execute($"""
            UPDATE payment_networks SET status = 'SUSPENDED', version = version + 1
            WHERE payment_network_id = {Blob(143)};
            """);

        public string? PolicyVersionOf(PaymentOrderId orderId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT payment_network_policy_version_id FROM payment_orders WHERE payment_order_id = $id;";
            command.Parameters.AddWithValue("$id", orderId.Value.ToByteArray());

            object? value = command.ExecuteScalar();
            return value is byte[] bytes ? Convert.ToHexString(bytes) : null;
        }

        public void OverrideCalendarDay(string localDate, string dayClass) => Execute($"""
            INSERT INTO economy_calendar_overrides(economy_scope_id, local_date, day_class, description, version)
            VALUES({Blob(1)}, '{localDate}', '{dayClass}', NULL, 1);
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public byte[] ReadBlob(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return (byte[])(command.ExecuteScalar() ?? Array.Empty<byte>());
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
            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        public long Balance(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.posted_balance_minor
                FROM ledger_balance_projections p
                JOIN deposit_accounts d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public long Held(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.held_minor
                FROM ledger_balance_projections p
                JOIN deposit_accounts d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
            return (long)(command.ExecuteScalar() ?? 0L);
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

        public async Task<CustomerAccountId> RegisterAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> result = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            return result.Value.Id;
        }

        public async Task<AccountOpeningView> OpenAsync(
            CustomerAccountId customerAccountId,
            string institutionCode = Institution)
        {
            Result<AccountOpeningView> result = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(Scope, customerAccountId, institutionCode),
                CancellationToken.None);

            return result.Value;
        }

        public Task<Result<PaymentOrderView>> TransferAsync(
            CustomerAccountId payer,
            DepositAccountId source,
            string destinationAccountNumber,
            long amount,
            string token = "interaction-1",
            string institution = Institution,
            string branch = Branch,
            string? memo = null) =>
            Payments.CreatePaymentOrderAsync(
                new CreatePaymentOrderCommand(
                    Scope, payer, source, institution, branch, destinationAccountNumber, amount, memo, token),
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

    private sealed record Parties(
        CustomerAccountId Payer,
        AccountOpeningView Source,
        CustomerAccountId Payee,
        AccountOpeningView Destination);

    private static async Task<Parties> SetupAsync(Harness harness, long funding = 1_000)
    {
        CustomerAccountId payer = await harness.RegisterAsync(PayerUser, "taro");
        CustomerAccountId payee = await harness.RegisterAsync(PayeeUser, "hanako");

        AccountOpeningView source = await harness.OpenAsync(payer);
        AccountOpeningView destination = await harness.OpenAsync(payee);

        harness.Fund(source.Id, funding);

        return new Parties(payer, source, payee, destination);
    }

    [TestMethod]
    public async Task TransferMovesMoneyBetweenAccountsInTheSameBank()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task TransferLeavesNoActiveHold()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM holds;"));
        Assert.AreEqual("0", harness.ReadText("SELECT CAST(remaining_minor AS TEXT) FROM holds;"));
    }

    [TestMethod]
    public async Task TransferPostsABalancedJournal()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(1L, harness.Count("accounting_transactions"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
        Assert.AreEqual(
            "300",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'DEBIT';"));
        Assert.AreEqual(
            "300",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'CREDIT';"));
    }

    [TestMethod]
    public async Task CompletedTransferRecordsBothCanonicalFacts()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
        Assert.AreEqual("INTERNAL", harness.ReadText("SELECT settlement_mode FROM payment_orders;"));
        Assert.AreEqual(
            "IMMEDIATE_AFTER_ACCEPTANCE",
            harness.ReadText("SELECT beneficiary_posting_policy FROM payment_orders;"));
        Assert.AreNotEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(beneficiary_posted_at AS TEXT) FROM payment_orders;"));
        Assert.AreEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(settlement_finalized_at AS TEXT) FROM payment_orders;"));
    }

    [TestMethod]
    public async Task TransferEmitsOutboxEventAndCommitsTheOperation()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(
            PaymentApplicationService.CompletedEventType,
            harness.ReadText($"""
                SELECT event_type FROM outbox_events
                WHERE event_type = '{PaymentApplicationService.CompletedEventType}';
                """));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText($"""
                SELECT status FROM business_operations
                WHERE idempotency_scope = '{PaymentApplicationService.OperationType}';
                """));
    }

    [TestMethod]
    public async Task InsufficientAvailableBalanceIsRejectedWithoutAnyEffect()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 100);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, result.Error!.Code);
        Assert.AreEqual(100L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual(0L, harness.Count("journal_entries"));
    }

    [TestMethod]
    public async Task ForeignSourceAccountIsNormalizedToNotFound()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payee, parties.Source.Id, parties.Destination.AccountNumber, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.NotFound, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error.Code);
    }

    [TestMethod]
    public async Task UnknownDestinationAccountIsNotFound()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, "0009999999", 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DepositAccountNotFound, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task TransferToOwnAccountIsRejected()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Source.AccountNumber, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SelfTransferRejected, result.Error!.Code);
    }

    private const int SourceReserveSeed = 106;
    private const int DestinationReserveSeed = 107;
    private const int SourceCentralBankLiabilitySeed = 103;
    private const int DestinationCentralBankLiabilitySeed = 104;
    private const int SettlementPayableSeed = 105;
    private const int IncomingSuspenseSeed = 108;

    private static async Task<AccountOpeningView> RemoteAccountAsync(Harness harness)
    {
        CustomerAccountId remote = await harness.RegisterAsync(710_000_000_000_000_003UL, "jiro");
        return await harness.OpenAsync(remote, OtherInstitution);
    }

    private static Task<Result<PaymentOrderView>> InterbankTransferAsync(
        Harness harness,
        Parties parties,
        AccountOpeningView remote,
        long amount,
        string token = "interaction-1") =>
        harness.TransferAsync(
            parties.Payer, parties.Source.Id, remote.AccountNumber, amount, token, OtherInstitution);

    [TestMethod]
    public async Task InterbankTransferSettlesThroughTheCentralBank()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(remote.Id));
        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
    }

    [TestMethod]
    public async Task InterbankSettlementMovesReservesAndMirrorsTheCentralBankLiabilities()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(4_700L, harness.LedgerBalanceOf(SourceReserveSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(DestinationReserveSeed));
        Assert.AreEqual(4_700L, harness.LedgerBalanceOf(SourceCentralBankLiabilitySeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(DestinationCentralBankLiabilitySeed));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(SettlementPayableSeed));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
    }

    [TestMethod]
    public async Task InterbankTransferPostsSourceSettlementAndBeneficiaryBooks()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(5L, harness.Count("accounting_transactions"));
        Assert.AreEqual(10L, harness.Count("journal_entries"));
        Assert.AreEqual(
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'DEBIT';"),
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'CREDIT';"));
    }

    [TestMethod]
    public async Task InterbankTransferRecordsBothFinalityFacts()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual("RTGS", harness.ReadText("SELECT settlement_mode FROM payment_orders;"));
        Assert.AreEqual(
            "AFTER_FINAL_SETTLEMENT",
            harness.ReadText("SELECT beneficiary_posting_policy FROM payment_orders;"));
        Assert.AreNotEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(settlement_finalized_at AS TEXT) FROM payment_orders;"));
        Assert.AreNotEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(beneficiary_posted_at AS TEXT) FROM payment_orders;"));
        Assert.AreEqual("SETTLED", harness.ReadText("SELECT status FROM settlement_instructions;"));
    }

    [TestMethod]
    public async Task InsufficientReserveKeepsThePaymentQueuedAfterTheCustomerDebit()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Queued, result.Value.Status);
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Balance(remote.Id));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(SettlementPayableSeed));
        Assert.AreEqual("QUEUED", harness.ReadText("SELECT status FROM settlement_instructions;"));
        Assert.AreEqual(
            string.Empty,
            harness.ReadText("SELECT CAST(settlement_finalized_at AS TEXT) FROM payment_orders;"));
    }

    [TestMethod]
    public async Task InterbankFeeIsChargedByTheSourceBankOnly()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5, feeType: "INTERBANK_TRANSFER");
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(5L, result.Value.FeeAmount.Value);
        Assert.AreEqual(695L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(remote.Id));
        Assert.AreEqual("INTERBANK_TRANSFER", harness.ReadText("SELECT fee_type FROM fee_assessments;"));
    }

    [TestMethod]
    public async Task InterbankTransferIsIdempotentAcrossRetries()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

            Assert.IsTrue(result.IsSuccess);
        }

        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(1L, harness.Count("settlement_instructions"));
        Assert.AreEqual(5L, harness.Count("accounting_transactions"));
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(remote.Id));
    }

    [TestMethod]
    public async Task QueuedSettlementConvergesOnceReservesArrive()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> queued = await InterbankTransferAsync(harness, parties, remote, 300);
        Assert.AreEqual(PaymentOrderStatus.Queued, queued.Value.Status);

        harness.FundReserve(5_000);
        SettlementMaintenanceReport report = await harness.Maintenance.ProcessQueuedAsync(
            CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(1, report.Settled);
        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
        Assert.AreEqual("SETTLED", harness.ReadText("SELECT status FROM settlement_instructions;"));
        Assert.AreEqual(300L, harness.Balance(remote.Id));
        Assert.AreEqual(4_700L, harness.LedgerBalanceOf(SourceReserveSeed));
    }

    [TestMethod]
    public async Task QueuedSettlementStaysQueuedWhileReservesRemainShort()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        SettlementMaintenanceReport report = await harness.Maintenance.ProcessQueuedAsync(
            CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(0, report.Settled);
        Assert.AreEqual("QUEUED", harness.ReadText("SELECT status FROM settlement_instructions;"));
        Assert.AreEqual(0L, harness.Balance(remote.Id));
    }

    [TestMethod]
    public async Task MaintenanceIsANoOpWhenNothingIsQueued()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        SettlementMaintenanceReport report = await harness.Maintenance.ProcessQueuedAsync(
            CancellationToken.None);

        Assert.AreEqual(0, report.Examined);
        Assert.AreEqual(5L, harness.Count("accounting_transactions"));
    }

    [TestMethod]
    public async Task ResumedSettlementDoesNotDuplicateTheBeneficiaryCredit()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        harness.FundReserve(5_000);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await harness.Maintenance.ProcessQueuedAsync(CancellationToken.None);
        }

        Assert.AreEqual(300L, harness.Balance(remote.Id));
        Assert.AreEqual(5L, harness.Count("accounting_transactions"));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText($"""
                SELECT status FROM business_operations
                WHERE idempotency_scope = '{PaymentApplicationService.OperationType}';
                """));
    }

    private static string QueuedOperationSql =>
        "SELECT business_operation_id FROM settlement_instructions WHERE status = 'QUEUED';";

    private static BusinessOperationId QueuedOperation(Harness harness) =>
        BusinessOperationId.FromValue(EntityIdValue.FromBytes(harness.ReadBlob(QueuedOperationSql)));

    [TestMethod]
    public async Task CancellingAQueuedSettlementRestoresTheSourceCustomer()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        BusinessOperationId operation = QueuedOperation(harness);

        Result<PaymentOrderView> result = await harness.Maintenance.CancelQueuedAsync(
            operation, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Cancelled, result.Value.Status);
        Assert.AreEqual(1_000L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Balance(remote.Id));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(SettlementPayableSeed));
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM settlement_instructions;"));
    }

    [TestMethod]
    public async Task CancellationIsPostedAsASeparateReversalOperation()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        BusinessOperationId operation = QueuedOperation(harness);
        await harness.Maintenance.CancelQueuedAsync(operation, CancellationToken.None);

        Assert.AreEqual(2L, harness.Count("accounting_transactions"));
        Assert.AreEqual(4L, harness.Count("journal_entries"));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText($"""
                SELECT status FROM business_operations
                WHERE idempotency_scope = '{PaymentApplicationService.ReversalOperationType}';
                """));
        Assert.AreEqual(
            "COMMITTED",
            harness.ReadText($"""
                SELECT status FROM business_operations
                WHERE idempotency_scope = '{PaymentApplicationService.OperationType}';
                """));
    }

    [TestMethod]
    public async Task CancellingTwiceHasNoAdditionalEffect()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(100);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        BusinessOperationId operation = QueuedOperation(harness);

        await harness.Maintenance.CancelQueuedAsync(operation, CancellationToken.None);
        Result<PaymentOrderView> second = await harness.Maintenance.CancelQueuedAsync(
            operation, CancellationToken.None);

        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(2L, harness.Count("accounting_transactions"));
        Assert.AreEqual(1_000L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task SettledPaymentCannotBeCancelled()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        BusinessOperationId operation = BusinessOperationId.FromValue(
            EntityIdValue.FromBytes(harness.ReadBlob(
                "SELECT business_operation_id FROM settlement_instructions;")));

        Result<PaymentOrderView> result = await harness.Maintenance.CancelQueuedAsync(
            operation, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ConcurrentModification, result.Error!.Code);
        Assert.AreEqual(300L, harness.Balance(remote.Id));
    }

    [TestMethod]
    public async Task BankWithoutSettlementParticipationCannotSendInterbank()
    {
        await using Harness harness = Harness.Create(withSecondBank: true);
        Parties parties = await SetupAsync(harness);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.BankUnavailable, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.SettlementParticipationUnavailable, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual(0L, harness.Count("holds"));
    }

    private const int AgentReserveSeed = 124;
    private const int AgentCentralBankLiabilitySeed = 125;
    private const int AgentClientDepositSeed = 128;
    private const int IndirectAgentBalanceSeed = 129;

    [TestMethod]
    public async Task IndirectBeneficiarySettlesThroughItsSettlementAgent()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);
        harness.MakeDestinationIndirect();

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(remote.Id));
    }

    [TestMethod]
    public async Task AgentLegKeepsTheClientDepositAndAgentBalanceMirrored()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);
        harness.MakeDestinationIndirect();

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(300L, harness.LedgerBalanceOf(IndirectAgentBalanceSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(AgentClientDepositSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(AgentReserveSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(AgentCentralBankLiabilitySeed));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(DestinationReserveSeed));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(DestinationCentralBankLiabilitySeed));
    }

    [TestMethod]
    public async Task IndirectSettlementInsertsTheAgentLegAsAnExtraBook()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);
        harness.MakeDestinationIndirect();

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(6L, harness.Count("accounting_transactions"));
        Assert.AreEqual(
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'DEBIT';"),
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'CREDIT';"));
    }

    [TestMethod]
    public async Task IndirectParticipantWithoutAnActiveAgentIsRejected()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);
        harness.MakeDestinationIndirect();
        harness.SuspendDestinationAgent();

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 100);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.SettlementAgentUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task NonPositiveAmountIsRejected(long amount)
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, amount);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AmountInvalid, result.Error!.Code);
    }

    [TestMethod]
    public async Task OverlongMemoIsRejected()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer,
            parties.Source.Id,
            parties.Destination.AccountNumber,
            100,
            memo: new string('あ', PaymentOrder.MaximumMemoLength + 1));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MemoTooLong, result.Error!.Code);
    }

    [TestMethod]
    public async Task MissingAccountingPeriodStopsBeforeTheHold()
    {
        await using Harness harness = Harness.Create(withPeriod: false);
        Parties parties = await SetupAsync(harness);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AccountingPeriodUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("journal_entries"));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(1_000L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task FeeIsDebitedFromThePayerAndCreditedToFeeRevenue()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(5L, result.Value.FeeAmount.Value);
        Assert.AreEqual(305L, result.Value.TotalDebitAmount.Value);
        Assert.AreEqual(695L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
        Assert.AreEqual("5", harness.ReadText(FeeRevenueBalanceSql));
    }

    [TestMethod]
    public async Task FeeAndPrincipalArePostedAsOneBalancedTransaction()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(1L, harness.Count("accounting_transactions"));
        Assert.AreEqual(4L, harness.Count("journal_entries"));
        Assert.AreEqual(
            "305",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'DEBIT';"));
        Assert.AreEqual(
            "305",
            harness.ReadText("SELECT CAST(SUM(amount_minor) AS TEXT) FROM journal_entries WHERE side = 'CREDIT';"));
    }

    [TestMethod]
    public async Task JournalEntriesFollowAscendingLedgerAccountOrder()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(
            "0",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM journal_entries AS earlier
                JOIN journal_entries AS later
                  ON later.accounting_transaction_id = earlier.accounting_transaction_id
                 AND later.entry_sequence > earlier.entry_sequence
                WHERE later.ledger_account_id < earlier.ledger_account_id;
                """));
    }

    [TestMethod]
    public async Task HoldReservesThePrincipalAndTheFee()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual("305", harness.ReadText("SELECT CAST(amount_minor AS TEXT) FROM holds;"));
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM holds;"));
        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
    }

    [TestMethod]
    public async Task AvailableBalanceMustCoverThePrincipalAndTheFee()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 300);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, result.Error!.Code);
        Assert.AreEqual(300L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task ProportionalFeeUsesBasisPoints()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 10, basisPoints: 100);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(13L, result.Value.FeeAmount.Value);
    }

    [TestMethod]
    public async Task FeeAssessmentRecordsTheSelectedRule()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(1L, harness.Count("fee_assessments"));
        Assert.AreEqual("SAME_BANK_TRANSFER", harness.ReadText("SELECT fee_type FROM fee_assessments;"));
        Assert.AreEqual("5", harness.ReadText("SELECT CAST(amount_minor AS TEXT) FROM fee_assessments;"));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM fee_assessments a
                JOIN fee_rules r ON r.fee_rule_id = a.fee_rule_id
                JOIN fee_schedule_versions v
                  ON v.fee_schedule_version_id = a.fee_schedule_version_id
                 AND v.fee_schedule_version_id = r.fee_schedule_version_id;
                """));
    }

    [TestMethod]
    public async Task ZeroFeeLeavesNoAssessmentAndNoFeeEntries()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(0L, harness.Count("fee_assessments"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
    }

    [TestMethod]
    public async Task FreeOccurrenceWaivesTheFeeExactlyOncePerBusinessMonth()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(
            PricedScheduleSeed, fixedMinor: 5, waiverCounterKey: "same-bank-transfer", freeOccurrences: 1);

        Result<PaymentOrderView> waived = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300, "first");
        Result<PaymentOrderView> charged = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300, "second");

        Assert.AreEqual(0L, waived.Value.FeeAmount.Value);
        Assert.AreEqual(5L, charged.Value.FeeAmount.Value);
        Assert.AreEqual("1", harness.ReadText("SELECT CAST(used_count AS TEXT) FROM fee_waiver_usage_counters;"));
        Assert.AreEqual(395L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task WaivedFeeIsStillRecordedAsAnAssessment()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(
            PricedScheduleSeed, fixedMinor: 5, waiverCounterKey: "same-bank-transfer", freeOccurrences: 1);

        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual("0", harness.ReadText("SELECT CAST(amount_minor AS TEXT) FROM fee_assessments;"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
    }

    [TestMethod]
    public async Task ConditionalRuleWinsOverTheCatchAllByPriority()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(
            PricedScheduleSeed, fixedMinor: 50, dayClass: "NON_BUSINESS_DAY", priority: 0);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5, priority: 1);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(50L, result.Value.FeeAmount.Value);
    }

    [TestMethod]
    public async Task NonMatchingConditionalRuleFallsBackToTheCatchAll()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 50, dayClass: "BUSINESS_DAY", priority: 0);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5, priority: 1);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(5L, result.Value.FeeAmount.Value);
    }

    [TestMethod]
    public async Task CalendarOverrideTurnsTheDayIntoABusinessDay()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 50, dayClass: "BUSINESS_DAY", priority: 0);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5, priority: 1);
        harness.OverrideCalendarDay("2026-04-12", "BUSINESS_DAY");

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(50L, result.Value.FeeAmount.Value);
    }

    [TestMethod]
    public async Task BankWithoutAFeeScheduleCannotTransfer()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.Execute("UPDATE banks SET current_fee_schedule_version_id = NULL;");

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.BankUnavailable, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.FeeScheduleUnavailable, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("holds"));
    }

    [TestMethod]
    public async Task MissingCatchAllRuleIsNotSilentlyTreatedAsZero()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5, dayClass: "BUSINESS_DAY");

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.FeeRuleUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task MissingFeeRevenueAccountStopsTheTransfer()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);
        harness.Execute("DELETE FROM ledger_accounts WHERE account_kind = 'FEE_REVENUE';");

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.FeeRevenueAccountUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("holds"));
    }

    [TestMethod]
    public async Task RepeatedInteractionChargesTheFeeOnlyOnce()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<PaymentOrderView> result = await harness.TransferAsync(
                parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(5L, result.Value.FeeAmount.Value);
        }

        Assert.AreEqual(1L, harness.Count("fee_assessments"));
        Assert.AreEqual(4L, harness.Count("journal_entries"));
        Assert.AreEqual(695L, harness.Balance(parties.Source.Id));
        Assert.AreEqual("5", harness.ReadText(FeeRevenueBalanceSql));
    }

    [TestMethod]
    public async Task PerTransferCeilingRejectsALargerAmount()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, perTransfer: 200);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 201);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.Validation, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.AmountLimitExceeded, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task PerTransferCeilingAllowsExactlyTheCeiling()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, perTransfer: 200);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 200);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(800L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task CustomerPreferenceTightensTheBankCeiling()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, perTransfer: 500);
        harness.SetCustomerLimits(parties.Source.Id, perTransfer: 100);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 101);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AmountLimitExceeded, result.Error!.Code);
    }

    [TestMethod]
    public async Task CustomerPreferenceCannotRaiseTheBankCeiling()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, perTransfer: 100);
        harness.SetCustomerLimits(parties.Source.Id, perTransfer: 900);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 101);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AmountLimitExceeded, result.Error!.Code);
    }

    [TestMethod]
    public async Task ZeroLimitStopsTheOperationInsteadOfLookingLikeAnInputError()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, perTransfer: 500);
        harness.SetCustomerLimits(parties.Source.Id, perTransfer: 0);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 1);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.AccountRestricted, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.TransferOperationDisabled, result.Error.Code);
    }

    [TestMethod]
    public async Task DailyOutgoingLimitAccumulatesAcrossTransfers()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, dailyOutgoing: 500);

        Result<PaymentOrderView> first = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300, "first");
        Result<PaymentOrderView> second = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 200, "second");
        Result<PaymentOrderView> third = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 1, "third");

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.IsFalse(third.IsSuccess);
        Assert.AreEqual(ErrorCategory.AccountRestricted, third.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.DailyOutgoingLimitExceeded, third.Error.Code);
        Assert.AreEqual(500L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task DailyOutgoingWindowFollowsTheCanonicalTimezone()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, dailyOutgoing: 300);

        await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300, "first");

        harness.Clock.Advance(TwoHoursInMilliseconds);

        Result<PaymentOrderView> nextDay = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300, "second");

        Assert.IsTrue(nextDay.IsSuccess);
        Assert.AreEqual(600L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task DailyOutgoingLimitIgnoresTheFee()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, dailyOutgoing: 300);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(5L, result.Value.FeeAmount.Value);
        Assert.AreEqual(695L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task RejectedTransfersDoNotConsumeTheDailyAllowance()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 400);
        harness.PublishBankLimits(CappedPolicySeed, dailyOutgoing: 400);

        Result<PaymentOrderView> overdraw = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 401, "first");
        Result<PaymentOrderView> allowed = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 400, "second");

        Assert.IsFalse(overdraw.IsSuccess);
        Assert.IsTrue(allowed.IsSuccess);
        Assert.AreEqual(400L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task BankWithoutAPolicyVersionCannotTransfer()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.Execute("UPDATE banks SET current_policy_version_id = NULL;");

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.BankUnavailable, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.BankPolicyUnavailable, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("holds"));
    }

    [TestMethod]
    public async Task RepeatedInteractionIsNotCountedTwiceAgainstTheDailyLimit()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, dailyOutgoing: 300);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<PaymentOrderView> result = await harness.TransferAsync(
                parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

            Assert.IsTrue(result.IsSuccess);
        }

        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task ActiveHoldCeilingCountsThePrincipalAndTheFee()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, maximumActiveHolds: 302);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorCategory.AccountRestricted, result.Error!.Category);
        Assert.AreEqual(BankingErrorCodes.ActiveHoldLimitExceeded, result.Error.Code);
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(1_000L, harness.Balance(parties.Source.Id));
    }

    [TestMethod]
    public async Task ActiveHoldCeilingAllowsExactlyTheReservedTotal()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);
        harness.PublishBankLimits(CappedPolicySeed, maximumActiveHolds: 305);
        harness.PublishTransferFee(PricedScheduleSeed, fixedMinor: 5);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(695L, harness.Balance(parties.Source.Id));
    }

    private const long TwoHoursInMilliseconds = 2 * 60 * 60 * 1000;

    private const string FeeRevenueBalanceSql = """
        SELECT CAST(p.posted_balance_minor AS TEXT)
        FROM ledger_balance_projections p
        JOIN ledger_accounts l ON l.ledger_account_id = p.ledger_account_id
        WHERE l.account_kind = 'FEE_REVENUE';
        """;

    [TestMethod]
    public async Task RepeatedInteractionProducesExactlyOneMonetaryEffect()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<PaymentOrderView> result = await harness.TransferAsync(
                parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

            Assert.IsTrue(result.IsSuccess);
        }

        Assert.AreEqual(1L, harness.Count("payment_orders"));
        Assert.AreEqual(2L, harness.Count("journal_entries"));
        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(300L, harness.Balance(parties.Destination.Id));
    }

    [TestMethod]
    public async Task ConcurrentTransfersNeverOverdrawTheSourceAccount()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness, funding: 100);

        Task<Result<PaymentOrderView>>[] attempts =
        [
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "a"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "b"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "c"),
            harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 80, "d"),
        ];

        Result<PaymentOrderView>[] results = await Task.WhenAll(attempts);

        Assert.AreEqual(1, results.Count(static result => result.IsSuccess));
        Assert.AreEqual(20L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(80L, harness.Balance(parties.Destination.Id));
        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
    }

    [TestMethod]
    public async Task TransferRecordsCustomerActivityOnTheSourceAccount()
    {
        await using Harness harness = Harness.Create();
        Parties parties = await SetupAsync(harness);

        harness.Clock.Advance(60_000);
        await harness.TransferAsync(parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.AreEqual(
            harness.ReadText("SELECT CAST(completed_at AS TEXT) FROM payment_orders;"),
            harness.ReadText($"""
                SELECT CAST(last_customer_activity_at AS TEXT) FROM deposit_accounts
                WHERE account_number = '{parties.Source.AccountNumber}';
                """));
    }

    [TestMethod]
    public async Task InterbankTransferStaysRealTimeWithoutAPaymentNetwork()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.IsNull(harness.PolicyVersionOf(result.Value.Id));
    }

    [TestMethod]
    public async Task RealTimeNetworkPolicyIsSnapshotOnThePaymentOrder()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPaymentNetwork("RTGS", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.IsNotNull(harness.PolicyVersionOf(result.Value.Id));
    }

    [TestMethod]
    public async Task AmountAtTheRealTimeThresholdBypassesClearing()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: 300);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
    }

    private const int ClearingPayableSeed = 145;
    private const int ClearingReceivableSeed = 146;

    [TestMethod]
    public async Task AmountBelowTheRealTimeThresholdRoutesToClearing()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: 300);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 299);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Accepted, result.Value.Status);
    }

    [TestMethod]
    public async Task ClearingAcceptanceDebitsThePayerAndCreditsClearingPayable()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(700L, harness.Balance(parties.Source.Id));
        Assert.AreEqual(0L, harness.Held(parties.Source.Id));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(ClearingPayableSeed));
    }

    [TestMethod]
    public async Task ClearingAcceptanceRecognisesTheReceivableWithoutCreditingTheBeneficiary()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual(300L, harness.LedgerBalanceOf(ClearingReceivableSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
        Assert.AreEqual(0L, harness.Balance(remote.Id));
    }

    [TestMethod]
    public async Task ClearingAcceptanceLeavesBothFinalityFactsUnset()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual("0", harness.ReadText(
            "SELECT CAST(count(*) AS TEXT) FROM payment_orders WHERE beneficiary_posted_at IS NOT NULL;"));
        Assert.AreEqual("0", harness.ReadText(
            "SELECT CAST(count(*) AS TEXT) FROM payment_orders WHERE settlement_finalized_at IS NOT NULL;"));
    }

    [TestMethod]
    public async Task ClearingAcceptanceEnrolsTheInstructionIntoAnOpenCycle()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual("ACCEPTED", harness.ReadText("SELECT status FROM clearing_instructions;"));
        Assert.AreEqual("OPEN", harness.ReadText("SELECT status FROM clearing_cycles;"));
        Assert.AreEqual("1", harness.ReadText(
            "SELECT CAST(count(*) AS TEXT) FROM clearing_instructions WHERE clearing_cycle_id IS NOT NULL;"));
    }

    [TestMethod]
    public async Task ClearingPositionsNetToZeroAcrossParticipants()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.AreEqual("2", harness.ReadText("SELECT CAST(count(*) AS TEXT) FROM clearing_positions;"));
        Assert.AreEqual("0", harness.ReadText(
            "SELECT CAST(coalesce(sum(net_minor), 0) AS TEXT) FROM clearing_positions;"));
        Assert.AreEqual("300", harness.ReadText(
            "SELECT CAST(sum(gross_payable_minor) AS TEXT) FROM clearing_positions;"));
    }

    [TestMethod]
    public async Task TwoClearingPaymentsInTheSameIntervalShareOneCycle()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        await InterbankTransferAsync(harness, parties, remote, 200, "interaction-2");

        Assert.AreEqual("1", harness.ReadText("SELECT CAST(count(*) AS TEXT) FROM clearing_cycles;"));
        Assert.AreEqual("2", harness.ReadText("SELECT CAST(count(*) AS TEXT) FROM clearing_instructions;"));
        Assert.AreEqual("500", harness.ReadText(
            "SELECT CAST(sum(gross_payable_minor) AS TEXT) FROM clearing_positions;"));
    }

    [TestMethod]
    public async Task SuspendedPaymentNetworkFallsBackToRealTimeSettlement()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        harness.SuspendPaymentNetwork();
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        Result<PaymentOrderView> result = await InterbankTransferAsync(harness, parties, remote, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.IsNull(harness.PolicyVersionOf(result.Value.Id));
    }

    [TestMethod]
    public async Task SameBankTransferIgnoresThePaymentNetwork()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);

        Result<PaymentOrderView> result = await harness.TransferAsync(
            parties.Payer, parties.Source.Id, parties.Destination.AccountNumber, 300);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentOrderStatus.Completed, result.Value.Status);
        Assert.IsNull(harness.PolicyVersionOf(result.Value.Id));
    }

    private const long ClearingIntervalMilliseconds = 3_600_000;

    private static async Task<Harness> AcceptedClearingAsync(long amount = 300, long reserve = 5_000)
    {
        Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(reserve);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, amount);

        return harness;
    }

    [TestMethod]
    public async Task ClearingCycleIsNotSettledBeforeTheIntervalElapses()
    {
        await using Harness harness = await AcceptedClearingAsync();

        SettlementMaintenanceReport report = await harness.Maintenance.ProcessClearingCyclesAsync(
            CancellationToken.None);

        Assert.AreEqual(0, report.Examined);
        Assert.AreEqual("OPEN", harness.ReadText("SELECT status FROM clearing_cycles;"));
    }

    [TestMethod]
    public async Task ElapsedClearingCycleSettlesAndCloses()
    {
        await using Harness harness = await AcceptedClearingAsync();
        harness.Clock.Advance(ClearingIntervalMilliseconds);

        SettlementMaintenanceReport report = await harness.Maintenance.ProcessClearingCyclesAsync(
            CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(1, report.Settled);
        Assert.AreEqual("CLOSED", harness.ReadText("SELECT status FROM clearing_cycles;"));
        Assert.AreEqual("SETTLED", harness.ReadText("SELECT status FROM clearing_instructions;"));
    }

    [TestMethod]
    public async Task ClearingNetSettlementUnwindsBothPositions()
    {
        await using Harness harness = await AcceptedClearingAsync();
        harness.Clock.Advance(ClearingIntervalMilliseconds);

        await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

        Assert.AreEqual(0L, harness.LedgerBalanceOf(ClearingPayableSeed));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(ClearingReceivableSeed));
    }

    [TestMethod]
    public async Task ClearingNetSettlementMovesReservesAndMirrorsCentralBankLiabilities()
    {
        await using Harness harness = await AcceptedClearingAsync();
        harness.Clock.Advance(ClearingIntervalMilliseconds);

        await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

        Assert.AreEqual(4_700L, harness.LedgerBalanceOf(SourceReserveSeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(DestinationReserveSeed));
        Assert.AreEqual(4_700L, harness.LedgerBalanceOf(SourceCentralBankLiabilitySeed));
        Assert.AreEqual(300L, harness.LedgerBalanceOf(DestinationCentralBankLiabilitySeed));
    }

    [TestMethod]
    public async Task ClearingBeneficiaryIsCreditedOnlyAfterFinalSettlement()
    {
        await using Harness harness = await AcceptedClearingAsync();
        DepositAccountId beneficiary = harness.RemoteDepositAccountId();

        Assert.AreEqual(0L, harness.Balance(beneficiary));

        harness.Clock.Advance(ClearingIntervalMilliseconds);
        await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

        Assert.AreEqual(300L, harness.Balance(beneficiary));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
    }

    [TestMethod]
    public async Task ClearingPaymentCompletesWithBothFinalityFacts()
    {
        await using Harness harness = await AcceptedClearingAsync();
        harness.Clock.Advance(ClearingIntervalMilliseconds);

        await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

        Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
        Assert.AreEqual("1", harness.ReadText("""
            SELECT CAST(count(*) AS TEXT) FROM payment_orders
            WHERE beneficiary_posted_at IS NOT NULL AND settlement_finalized_at IS NOT NULL;
            """));
    }

    [TestMethod]
    public async Task SettlingTheSameCycleTwiceIsIdempotent()
    {
        await using Harness harness = await AcceptedClearingAsync();
        harness.Clock.Advance(ClearingIntervalMilliseconds);

        await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);
        SettlementMaintenanceReport second = await harness.Maintenance.ProcessClearingCyclesAsync(
            CancellationToken.None);

        Assert.AreEqual(0, second.Examined);
        Assert.AreEqual(300L, harness.Balance(harness.RemoteDepositAccountId()));
        Assert.AreEqual(0L, harness.LedgerBalanceOf(ClearingPayableSeed));
    }

    private static async Task<(Harness Harness, AccountOpeningView Remote)> PreCreditAsync(
        long exposureLimit,
        long prefundBalance,
        long amount)
    {
        Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPreCreditNetwork(exposureLimit, prefundBalance);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, amount);

        return (harness, remote);
    }

    [TestMethod]
    public async Task CoveredPreCreditCreditsTheBeneficiaryAtAcceptance()
    {
        (Harness harness, AccountOpeningView remote) = await PreCreditAsync(10_000, 1_000, 300);

        await using (harness)
        {
            Assert.AreEqual(300L, harness.Balance(remote.Id));
            Assert.AreEqual(0L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
            Assert.AreEqual("ACCEPTED", harness.ReadText("SELECT status FROM payment_orders;"));
        }
    }

    [TestMethod]
    public async Task PreCreditDoesNotImplyInterbankFinality()
    {
        (Harness harness, AccountOpeningView _) = await PreCreditAsync(10_000, 1_000, 300);

        await using (harness)
        {
            Assert.AreEqual("1", harness.ReadText("""
                SELECT CAST(count(*) AS TEXT) FROM payment_orders
                WHERE beneficiary_posted_at IS NOT NULL AND settlement_finalized_at IS NULL;
                """));
        }
    }

    [TestMethod]
    public async Task PreCreditedPaymentCompletesWithoutASecondBeneficiaryCredit()
    {
        (Harness harness, AccountOpeningView remote) = await PreCreditAsync(10_000, 1_000, 300);

        await using (harness)
        {
            harness.Clock.Advance(ClearingIntervalMilliseconds);
            await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

            Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
            Assert.AreEqual(300L, harness.Balance(remote.Id));
            Assert.AreEqual(0L, harness.LedgerBalanceOf(ClearingReceivableSeed));
        }
    }

    [TestMethod]
    public async Task PrefundShortfallKeepsTheBeneficiaryUncredited()
    {
        (Harness harness, AccountOpeningView remote) = await PreCreditAsync(10_000, 299, 300);

        await using (harness)
        {
            Assert.AreEqual(0L, harness.Balance(remote.Id));
            Assert.AreEqual(300L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
            Assert.AreEqual("0", harness.ReadText(
                "SELECT CAST(count(*) AS TEXT) FROM payment_orders WHERE beneficiary_posted_at IS NOT NULL;"));
        }
    }

    [TestMethod]
    public async Task ExposureLimitBlocksTheSecondPreCredit()
    {
        Harness harness = Harness.Create(withSettlement: true);

        await using (harness)
        {
            Parties parties = await SetupAsync(harness, funding: 5_000);
            harness.FundReserve(5_000);
            harness.PublishPreCreditNetwork(exposureLimit: 300, prefundBalance: 10_000);
            AccountOpeningView remote = await RemoteAccountAsync(harness);

            await InterbankTransferAsync(harness, parties, remote, 300);
            await InterbankTransferAsync(harness, parties, remote, 100, "interaction-2");

            Assert.AreEqual(300L, harness.Balance(remote.Id));
            Assert.AreEqual(100L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
        }
    }

    [TestMethod]
    public async Task UncoveredPreCreditIsPostedAfterFinalSettlement()
    {
        (Harness harness, AccountOpeningView remote) = await PreCreditAsync(10_000, 0, 300);

        await using (harness)
        {
            harness.Clock.Advance(ClearingIntervalMilliseconds);
            await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

            Assert.AreEqual("COMPLETED", harness.ReadText("SELECT status FROM payment_orders;"));
            Assert.AreEqual(300L, harness.Balance(remote.Id));
            Assert.AreEqual(0L, harness.LedgerBalanceOf(IncomingSuspenseSeed));
        }
    }

    [TestMethod]
    public async Task LockedClearingCycleRejectsNewInstructions()
    {
        await using Harness harness = Harness.Create(withSettlement: true);
        Parties parties = await SetupAsync(harness);
        harness.FundReserve(5_000);
        harness.PublishPaymentNetwork("CLEARING", rtgsThreshold: null);
        AccountOpeningView remote = await RemoteAccountAsync(harness);

        await InterbankTransferAsync(harness, parties, remote, 300);
        harness.LockClearingCycles();

        Result<PaymentOrderView> blocked = await InterbankTransferAsync(
            harness, parties, remote, 100, "interaction-2");

        Assert.IsFalse(blocked.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.ConcurrentModification, blocked.Error!.Code);
    }
}
