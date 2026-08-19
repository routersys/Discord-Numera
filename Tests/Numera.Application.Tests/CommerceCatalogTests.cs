using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class CommerceCatalogTests
{
    private const ulong GuildId = 960UL;
    private const ulong OtherGuildId = 961UL;
    private const string Institution = "NUM0060";
    private const ulong ForeignGuildId = 961UL;
    private const string ForeignInstitution = "NUM0061";

    private const string PartnerInstitution = "NUM0062";
    private const ulong LiquidityUser = 760_000_000_000_000_003UL;
    private const ulong MerchantUser = 760_000_000_000_000_001UL;
    private const ulong BuyerUser = 760_000_000_000_000_002UL;

    private sealed class StubCommerceCardImageRenderer : IBankCardImageRenderer
    {
        public BankCardImage? TryRender(BankCardRenderModel model) =>
            new("bank-card.png", 1026, 647, [0x89, 0x50, 0x4E, 0x47, 0x0D]);
    }

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

        public MerchantAdministrationApplicationService Merchants { get; private set; } = null!;

        public CommerceApplicationService Commerce { get; private set; } = null!;

        public CommerceMaintenanceService Maintenance { get; private set; } = null!;

        public BankCardApplicationService Cards { get; private set; } = null!;

        public ExpiryMaintenanceService Expiries { get; private set; } = null!;

        public DormancyMaintenanceService Dormancy { get; private set; } = null!;

        public FxApplicationService Markets { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-commerce", Guid.NewGuid().ToString("n"));
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
            harness.Markets = new FxApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Merchants = new MerchantAdministrationApplicationService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Markets,
                harness.Clock,
                ids);
            harness.Commerce = new CommerceApplicationService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Markets,
                harness.Clock,
                ids);
            harness.Maintenance = new CommerceMaintenanceService(gateway, harness.Clock, ids);
            harness.Cards = new BankCardApplicationService(
                gateway, harness.Clock, ids, new StubCommerceCardImageRenderer());
            harness.Expiries = new ExpiryMaintenanceService(gateway, harness.Clock);
            harness.Dormancy = new DormancyMaintenanceService(gateway, harness.Clock, ids);

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

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(30)}, {Blob(5)}, 1, 0, 0, 0, 1, 1, 1, 1, 1, 'NONE', 1, 12, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(31)}, {Blob(5)}, 1, NULL, 1);

            INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                free_occurrences_per_business_month)
            VALUES({Blob(32)}, {Blob(31)}, 'DEBIT_PURCHASE', 0, 'ANY', NULL, NULL, NULL, 0, NULL,
                'ANY', NULL, NULL, 0, 0, 0, NULL, NULL, 0),
                ({Blob(33)}, {Blob(31)}, 'DORMANCY_WEEKLY', 0, 'ANY', NULL, NULL, NULL, 0, NULL,
                'ANY', NULL, NULL, 1, 0, 0, NULL, NULL, 0);

            UPDATE banks
            SET current_policy_version_id = {Blob(30)},
                current_fee_schedule_version_id = {Blob(31)},
                version = version + 1
            WHERE bank_id = {Blob(5)};

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(8)}, {Blob(5)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(40)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(41)}, {Blob(4)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(9)}, {Blob(8)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', 1);
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

        public DepositAccountId HomeAccountOf(CustomerAccountId customerAccountId) =>
            AccountOf(customerAccountId, Blob(5));

        public DepositAccountId ForeignAccountOf(CustomerAccountId customerAccountId) =>
            AccountOf(customerAccountId, Blob(75));

        private DepositAccountId AccountOf(CustomerAccountId customerAccountId, string bank) =>
            DepositAccountId.FromValue(EntityIdValue.FromBytes(
                Convert.FromHexString(ReadText($"""
                    SELECT hex(deposit_account_id) FROM deposit_accounts
                    WHERE customer_account_id
                        = x'{Convert.ToHexString(customerAccountId.Value.ToByteArray())}'
                      AND bank_id = {bank};
                    """))));

        public DepositAccountId SourceOf(CommerceOrderId orderId) =>
            DepositAccountId.FromValue(EntityIdValue.FromBytes(
                Convert.FromHexString(ReadText($"""
                    SELECT hex(d.deposit_account_id) FROM deposit_accounts AS d
                    JOIN commerce_orders AS o
                        ON o.customer_account_id = d.customer_account_id
                    WHERE o.commerce_order_id = x'{Convert.ToHexString(orderId.Value.ToByteArray())}'
                      AND d.status = 'ACTIVE'
                    ORDER BY d.opened_at
                    LIMIT 1;
                    """))));

        public DebitCardId DebitCardOf(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT d.debit_card_id FROM debit_cards d
                INNER JOIN bank_cards c ON c.bank_card_id = d.bank_card_id
                WHERE c.deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());

            return DebitCardId.FromValue(EntityIdValue.FromBytes((byte[])command.ExecuteScalar()!));
        }

        public long PostedBalance(DepositAccountId accountId)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT posted_balance_minor FROM ledger_balance_projections
                WHERE ledger_account_id = (
                    SELECT ledger_account_id FROM deposit_accounts WHERE deposit_account_id = $id);
                """;
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());

            return (long)(command.ExecuteScalar() ?? 0L);
        }

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public async Task<DepositAccountId> OpenAccountAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> registered = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, registered.Value.Id, Institution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);

            return opened.Value.Id;
        }

        public void SeedPartnerBank() => Execute($"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(90)}, 'BANK', '提携銀行主体', 'ACTIVE', 1, 1),
                ({Blob(99)}, 'SYSTEM', '決済網主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(91)}, {Blob(90)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1),
                ({Blob(100)}, {Blob(99)}, 'SYSTEM', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(92)}, {Blob(91)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(93)}, {Blob(1)}, {Blob(90)}, '{PartnerInstitution}', '提携銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(91)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at,
                version)
            VALUES({Blob(94)}, {Blob(93)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES
                ({Blob(95)}, {Blob(91)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(96)}, {Blob(91)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(101)}, {Blob(4)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(102)}, {Blob(91)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(103)}, {Blob(91)}, NULL, '2450', 'INCOMING_SETTLEMENT_SUSPENSE', 'LIABILITY',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(110)}, {Blob(4)}, NULL, '1400', 'CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(111)}, {Blob(91)}, NULL, '2400', 'CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(104)}, {Blob(100)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(97)}, {Blob(93)}, 1, 0, 0, 0, 1, 1, 1, 1, 1, 'NONE', 1, 12, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(98)}, {Blob(93)}, 1, NULL, 1);

            INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                free_occurrences_per_business_month)
            VALUES({Blob(105)}, {Blob(98)}, 'DEBIT_PURCHASE', 0, 'ANY', NULL, NULL, NULL, 0, NULL,
                'ANY', NULL, NULL, 0, 0, 0, NULL, NULL, 0);

            UPDATE banks
            SET current_policy_version_id = {Blob(97)},
                current_fee_schedule_version_id = {Blob(98)},
                version = version + 1
            WHERE bank_id = {Blob(93)};

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(106)}, {Blob(93)}, 'STD', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(107)}, {Blob(106)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL,
                NULL, 'INTERNAL', 'STANDARD', 'NONE', 1);

            INSERT INTO payment_networks(payment_network_id, economy_scope_id, network_code,
                operator_party_id, accounting_book_id, liquid_asset_ledger_account_id, status,
                current_policy_version_id, version)
            VALUES({Blob(108)}, {Blob(1)}, 'CMRNET', {Blob(99)}, {Blob(100)}, {Blob(104)}, 'DRAFT',
                NULL, 1);

            INSERT INTO payment_network_policy_versions(payment_network_policy_version_id,
                payment_network_id, settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                per_bank_precredit_exposure_limit_minor, created_at, version)
            VALUES({Blob(109)}, {Blob(108)}, 'CLEARING', 'AFTER_FINAL_SETTLEMENT', NULL, 3600, 0,
                10000, 0, 1, 1);

            UPDATE payment_networks
            SET status = 'ACTIVE', current_policy_version_id = {Blob(109)}, version = version + 1
            WHERE payment_network_id = {Blob(108)};
            """);

        public async Task<DepositAccountId> OpenPartnerAccountAsync(
            ulong discordUserId,
            string handle)
        {
            Result<CustomerAccountView> registered = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, registered.Value.Id, PartnerInstitution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);
            return opened.Value.Id;
        }

        public async Task<DepositAccountId> OpenForeignAccountAsync(CustomerAccountId customerAccountId)
        {
            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(ForeignGuildId, customerAccountId, ForeignInstitution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);

            return opened.Value.Id;
        }

        public CustomerAccountId CustomerOf(DepositAccountId depositAccountId) =>
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(
                Convert.FromHexString(ReadText($"""
                    SELECT hex(customer_account_id) FROM deposit_accounts
                    WHERE deposit_account_id = x'{Convert.ToHexString(
                        depositAccountId.Value.ToByteArray())}';
                    """))));

        public void SeedCrossCurrency() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(70)}, '{ForeignGuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

            INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                base_money_supply_cap_minor, created_at, retired_at, version)
            VALUES({Blob(71)}, {Blob(70)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(72)}, 'BANK', '外貨銀行主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(73)}, {Blob(72)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(74)}, {Blob(73)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                current_fee_schedule_version_id, created_at, version)
            VALUES({Blob(75)}, {Blob(70)}, {Blob(72)}, '{ForeignInstitution}', '外貨銀行', 'NORMAL', NULL,
                'OPERATING', {Blob(73)}, NULL, NULL, 1, 1);

            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at,
                version)
            VALUES({Blob(76)}, {Blob(75)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES
                ({Blob(77)}, {Blob(73)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
                    {Blob(71)}, 0, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(78)}, {Blob(73)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                    {Blob(71)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                minimum_customer_account_age_days, minimum_initial_funding_minor, requires_manual_approval,
                reopen_closed_account_allowed, public_receiving_enabled_default, cash_card_enabled,
                debit_card_enabled, integrated_cash_debit_default, automatic_bank_card_issue_mode,
                cash_atm_enabled, cash_card_validity_months, debit_card_validity_months,
                per_transfer_limit_minor, daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                maximum_active_holds_minor, effective_from, effective_to, version)
            VALUES({Blob(79)}, {Blob(75)}, 1, 0, 0, 0, 1, 1, 1, 1, 1, 'NONE', 1, 12, 12,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES({Blob(80)}, {Blob(75)}, 1, NULL, 1);

            UPDATE banks
            SET current_policy_version_id = {Blob(79)},
                current_fee_schedule_version_id = {Blob(80)},
                version = version + 1
            WHERE bank_id = {Blob(75)};

            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES({Blob(81)}, {Blob(75)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', 1, 1);

            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES({Blob(82)}, {Blob(81)}, 1, 1, NULL, 1000000000, 'ACTUAL_365_FIXED', 0, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', 1);

            INSERT INTO fx_markets(market_id, base_currency_id, quote_currency_id, operator_party_id,
                current_policy_version_id, price_scale, tick_size_price_units, lot_size_base_minor,
                next_order_sequence_no, next_trade_sequence_no, status, version)
            VALUES({Blob(83)}, {Blob(2)}, {Blob(71)}, {Blob(3)}, {Blob(84)}, 100, 1, 100, 1, 1,
                'ACTIVE', 1);

            INSERT INTO fx_market_policy_versions(fx_market_policy_version_id, market_id, maker_fee_bps,
                taker_fee_bps, maximum_market_slippage_bps, effective_from, created_at, version)
            VALUES({Blob(84)}, {Blob(83)}, 0, 0, 1000, 1, 1, 1);

            INSERT INTO fx_market_summaries(market_id, last_trade_price_units, last_trade_sequence_no,
                summary_version, order_book_version, updated_at)
            VALUES({Blob(83)}, NULL, NULL, 1, 1, 1);

            INSERT INTO currency_trust_policy_versions(currency_trust_policy_version_id,
                economy_scope_id, established_min_age_seconds, established_min_trade_days,
                established_min_counterparties, trusted_min_age_seconds, trusted_min_trade_days,
                trusted_min_counterparties, reserve_min_age_seconds, reserve_min_trade_days,
                reserve_min_counterparties, status, created_at, published_at, retired_at, version)
            VALUES({Blob(85)}, {Blob(1)}, 604800, 3, 2, 2592000, 10, 3, 7776000, 30, 5,
                'PUBLISHED', 1, 1, NULL, 1);

            INSERT INTO authorization_decisions(authorization_decision_id, target_type, target_id,
                scope_guild_id, authority_kind, actor_discord_user_id, actor_customer_account_id,
                decision_kind, reason_code, occurred_at, supersedes_decision_id)
            VALUES
                ({Blob(86)}, 'CURRENCY_TRUST_DESIGNATION', {Blob(88)}, NULL, 'SYSTEM_OWNER', '1', NULL,
                    'APPROVE', NULL, 1, NULL),
                ({Blob(87)}, 'CURRENCY_TRUST_DESIGNATION', {Blob(89)}, NULL, 'SYSTEM_OWNER', '1', NULL,
                    'APPROVE', NULL, 1, NULL);

            INSERT INTO currency_trust_designations(currency_trust_designation_id, currency_id,
                currency_trust_policy_version_id, trust_tier, status, authorization_decision_id,
                qualified_age_seconds, qualified_trade_days, qualified_counterparties, effective_from,
                terminal_at, version)
            VALUES
                ({Blob(88)}, {Blob(2)}, {Blob(85)}, 'ESTABLISHED', 'ACTIVE', {Blob(86)},
                    604800, 3, 2, 1, NULL, 1),
                ({Blob(89)}, {Blob(71)}, {Blob(85)}, 'ESTABLISHED', 'ACTIVE', {Blob(87)},
                    604800, 3, 2, 1, NULL, 1);
            """);

        public void SeedStandaloneHold(DepositAccountId accountId, long amount, long expiresAt)
        {
            Execute($"""
                INSERT INTO business_operations(business_operation_id, operation_type, economy_scope_id,
                    actor_party_id, correlation_id, idempotency_scope, idempotency_key, status,
                    created_at, committed_at, version)
                VALUES({Blob(60)}, 'MANUAL_HOLD', {Blob(1)}, NULL, {Blob(61)}, 'MANUAL_HOLD',
                    'manual-hold-1', 'COMMITTED', 1, 1, 1);
                """);

            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO holds(hold_id, hold_scope_kind, deposit_account_id, ledger_account_id,
                    business_operation_id, amount_minor, remaining_minor, reason, status, created_at,
                    expires_at, terminal_at, version)
                VALUES({Blob(62)}, 'CUSTOMER_DEPOSIT', $account, NULL, {Blob(60)}, $amount, $amount,
                    'MANUAL', 'ACTIVE', 1, $expires, NULL, 1);

                UPDATE ledger_balance_projections
                SET held_minor = held_minor + $amount, version = version + 1
                WHERE ledger_account_id = (
                    SELECT ledger_account_id FROM deposit_accounts WHERE deposit_account_id = $account);
                """;
            command.Parameters.AddWithValue("$account", accountId.Value.ToByteArray());
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$expires", expiresAt);
            command.ExecuteNonQuery();
        }

        public void SeedAuthorization(
            MerchantProfileId profileId,
            DepositAccountId accountId,
            string status,
            long amount,
            long expiresAt)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO debit_card_authorizations(debit_card_authorization_id, debit_card_id,
                    deposit_account_id, merchant_profile_id, commerce_order_id,
                    merchant_destination_deposit_account_id, source_currency_id,
                    presentment_currency_id, hold_id, merchant_reference, authorization_amount_minor,
                    captured_amount_minor, refunded_amount_minor, presentment_authorized_minor,
                    presentment_captured_minor, presentment_refunded_minor, fee_schedule_version_id,
                    purchase_fee_assessed_minor, settlement_route, status, authorized_at, expires_at,
                    completed_at, version)
                SELECT {Blob(63)}, d.debit_card_id, $account, $merchant, NULL,
                    p.settlement_deposit_account_id, {Blob(2)}, {Blob(2)}, {Blob(62)}, 'MANUAL-1',
                    $amount, $captured, 0, $amount, $captured, 0, {Blob(31)}, 0,
                    'SAME_CURRENCY_PAYMENT', $status, 1, $expires, NULL, 1
                FROM debit_cards d
                INNER JOIN bank_cards c ON c.bank_card_id = d.bank_card_id
                INNER JOIN merchant_profiles p ON p.merchant_profile_id = $merchant
                WHERE c.deposit_account_id = $account;
                """;
            command.Parameters.AddWithValue("$account", accountId.Value.ToByteArray());
            command.Parameters.AddWithValue("$merchant", profileId.Value.ToByteArray());
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$captured", status == "PARTIALLY_CAPTURED" ? 600L : 0L);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$expires", expiresAt);
            command.ExecuteNonQuery();
        }

        public void SeedDormancy(DepositAccountId accountId, long nextDueAt)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE deposit_accounts
                SET status = 'DORMANT', next_dormancy_fee_at = $due, version = version + 1
                WHERE deposit_account_id = $id;
                """;
            command.Parameters.AddWithValue("$due", nextDueAt);
            command.Parameters.AddWithValue("$id", accountId.Value.ToByteArray());
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

    private static AuthorizationContext Merchant(ulong guildId = GuildId) =>
        new(AuthorizationLevel.Customer, MerchantUser, guildId);

    private static AuthorizationContext Buyer(ulong guildId = GuildId) =>
        new(AuthorizationLevel.Customer, BuyerUser, guildId);

    private static async Task<MerchantProfileView> CreateProfileAsync(
        Harness harness,
        string catalogScope = "GLOBAL",
        string paymentScope = "GLOBAL",
        bool settleAtPartner = false)
    {
        DepositAccountId settlement = settleAtPartner
            ? await harness.OpenPartnerAccountAsync(MerchantUser, "seller")
            : await harness.OpenAccountAsync(MerchantUser, "seller");

        Result<MerchantProfileView> created = await harness.Merchants.CreateAsync(
            new CreateMerchantProfileCommand(
                Merchant(), settlement, "ヌメラ商店", catalogScope, paymentScope, "DISABLED", 50, 3600, 7200, true),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        return created.Value;
    }

    private static async Task<MerchantProductView> CreateActiveProductAsync(
        Harness harness,
        MerchantProfileId profileId,
        string inventoryMode = "UNLIMITED",
        string saleScopeOverride = "INHERIT")
    {
        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Merchant(), profileId, "SKU-1", "記念コイン", "説明", inventoryMode, saleScopeOverride),
            CancellationToken.None);

        Assert.IsTrue(product.IsSuccess, product.Error?.Code);

        Result<MerchantProductPriceVersionView> price = await harness.Merchants.PublishPriceAsync(
            new PublishMerchantProductPriceCommand(Merchant(), product.Value.Id, 1_200),
            CancellationToken.None);

        Assert.IsTrue(price.IsSuccess, price.Error?.Code);

        if (inventoryMode == "FINITE")
        {
            Result<MerchantInventoryView> adjusted = await harness.Merchants.AdjustInventoryAsync(
                new AdjustMerchantInventoryCommand(Merchant(), product.Value.Id, 5), CancellationToken.None);

            Assert.IsTrue(adjusted.IsSuccess, adjusted.Error?.Code);
        }

        Result<MerchantProductView> activated = await harness.Merchants.SetProductStateAsync(
            new SetMerchantProductStateCommand(Merchant(), product.Value.Id, MerchantProductStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(activated.IsSuccess, activated.Error?.Code);
        return activated.Value;
    }

    [TestMethod]
    public async Task CreatingAProfilePublishesTheFirstAftercarePolicy()
    {
        await using Harness harness = Harness.Create();

        MerchantProfileView profile = await CreateProfileAsync(harness);

        Assert.AreEqual(MerchantProfileStatus.Active, profile.Status);
        Assert.AreEqual(1L, harness.Count("merchant_profiles"));
        Assert.AreEqual("PUBLISHED", harness.ReadText(
            "SELECT status FROM merchant_aftercare_policy_versions;"));
        Assert.AreEqual(1L, harness.Count(
            "merchant_profiles WHERE current_aftercare_policy_version_id IS NOT NULL"));
    }

    private static async Task<MerchantProfileView> CreateForeignProfileAsync(Harness harness)
    {
        DepositAccountId home = await harness.OpenAccountAsync(MerchantUser, "seller");
        DepositAccountId settlement = await harness.OpenForeignAccountAsync(harness.CustomerOf(home));

        Result<MerchantProfileView> created = await harness.Merchants.CreateAsync(
            new CreateMerchantProfileCommand(
                Merchant(), settlement, "ヌメラ商店", "GLOBAL", "GLOBAL", "FX_FOK", 50, 3600, 7200, true),
            CancellationToken.None);

        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        return created.Value;
    }

    private static async Task ProvideLiquidityAsync(Harness harness, long baseMinor, long priceUnits)
    {
        DepositAccountId home = await harness.OpenAccountAsync(LiquidityUser, "maker");
        CustomerAccountId customer = harness.CustomerOf(home);
        DepositAccountId foreign = await harness.OpenForeignAccountAsync(customer);

        harness.Fund(home, 1_000_000);
        harness.Fund(foreign, 1_000_000);

        Result<FxOrderView> resting = await harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                new AuthorizationContext(AuthorizationLevel.Customer, LiquidityUser, GuildId),
                FxMarketId.FromValue(EntityIdValue.FromBits(83)),
                customer,
                FxOrderSide.BuyBase,
                FxOrderType.Limit,
                baseMinor,
                priceUnits,
                null,
                foreign,
                home,
                IdempotencyKey.Create("commerce-fx", "liquidity-1")),
            CancellationToken.None);

        Assert.IsTrue(resting.IsSuccess, resting.Error?.Code);
    }

    private static async Task ProvideSellLiquidityAsync(Harness harness, long baseMinor, long priceUnits)
    {
        CustomerAccountId customer = harness.CustomerOf(
            await harness.OpenAccountAsync(LiquidityUser, "maker"));
        DepositAccountId home = harness.HomeAccountOf(customer);
        DepositAccountId foreign = harness.ForeignAccountOf(customer);

        Result<FxOrderView> resting = await harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                new AuthorizationContext(AuthorizationLevel.Customer, LiquidityUser, GuildId),
                FxMarketId.FromValue(EntityIdValue.FromBits(83)),
                customer,
                FxOrderSide.SellBase,
                FxOrderType.Limit,
                baseMinor,
                priceUnits,
                null,
                home,
                foreign,
                IdempotencyKey.Create("commerce-fx", "liquidity-2")),
            CancellationToken.None);

        Assert.IsTrue(resting.IsSuccess, resting.Error?.Code);
    }

    private static async Task<CommerceOrderId> ForeignOrderAsync(Harness harness, long unitPrice)
    {
        MerchantProfileView profile = await CreateForeignProfileAsync(harness);

        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Merchant(), profile.Id, "SKU-XC", "外貨商品", "説明", "UNLIMITED", "INHERIT"),
            CancellationToken.None);

        Assert.IsTrue(product.IsSuccess, product.Error?.Code);

        Result<MerchantProductPriceVersionView> price = await harness.Merchants.PublishPriceAsync(
            new PublishMerchantProductPriceCommand(Merchant(), product.Value.Id, unitPrice),
            CancellationToken.None);

        Assert.IsTrue(price.IsSuccess, price.Error?.Code);

        Result<MerchantProductView> activated = await harness.Merchants.SetProductStateAsync(
            new SetMerchantProductStateCommand(Merchant(), product.Value.Id, MerchantProductStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(activated.IsSuccess, activated.Error?.Code);

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Value.Id, 1, "checkout-xc"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        return checkout.Value.CommerceOrderId;
    }

    [TestMethod]
    public async Task ACrossCurrencyCheckoutQuotesTheSourceFromTheOrderBook()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc")),
            CancellationToken.None);

        Assert.IsTrue(card.IsSuccess, card.Error?.Code);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsTrue(confirmation.IsSuccess, confirmation.Error?.Code);
        Assert.AreNotEqual(
            confirmation.Value.SourceCurrencyId, confirmation.Value.PresentmentCurrencyId);
        Assert.AreEqual(1_000L, confirmation.Value.EstimatedSourcePrincipal.Value);
        Assert.AreEqual(0L, confirmation.Value.EstimatedFxFee.Value);
        Assert.AreEqual(1_005L, confirmation.Value.ConfirmedMaxSourceDebit.Value);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_checkout_confirmations
                WHERE fx_market_id IS NOT NULL AND fx_market_policy_version_id IS NOT NULL
                  AND order_book_version IS NOT NULL;
                """));
    }

    [TestMethod]
    public async Task ACrossCurrencyCaptureDeliversThePresentmentTotalToTheMerchant()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 100_000);

        Assert.IsTrue((await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc-capture")),
            CancellationToken.None)).IsSuccess);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsTrue(confirmation.IsSuccess, confirmation.Error?.Code);

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation.Value.Id),
            CancellationToken.None);

        Assert.IsTrue(captured.IsSuccess, captured.Error?.Code);
        Assert.AreEqual("FX_FOK_DEBIT", captured.Value.PaymentRoute);
        Assert.AreEqual(1_500L, captured.Value.PresentmentPaid.Value);
        Assert.AreEqual(
            "99000",
            harness.ReadText($"""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = x'{Convert.ToHexString(buyer.Value.ToByteArray())}';
                """));
        Assert.AreEqual(1L, harness.Count("fx_trades"));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM fx_settlement_endpoints
                WHERE endpoint_kind = 'MERCHANT_PURCHASE_DELIVERY';
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM debit_card_captures
                WHERE settlement_route = 'FX_FOK' AND payment_order_id IS NULL
                  AND fx_business_operation_id IS NOT NULL;
                """));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual(
            "1500",
            harness.ReadText("""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
                JOIN merchant_profiles AS m
                    ON m.settlement_deposit_account_id = d.deposit_account_id;
                """));
    }

    [TestMethod]
    public async Task ACrossCurrencyRefundReturnsTheAcquiredSourceNet()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);
        await ProvideSellLiquidityAsync(harness, 10_000, 250);

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 100_000);

        Assert.IsTrue((await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc-refund")),
            CancellationToken.None)).IsSuccess);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsTrue(confirmation.IsSuccess, confirmation.Error?.Code);

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation.Value.Id),
            CancellationToken.None);

        Assert.IsTrue(captured.IsSuccess, captured.Error?.Code);

        Result<CommerceRefundConfirmationView> review = await harness.Merchants.ReviewRefundAsync(
            new ReviewCommerceRefundCommand(Merchant(), captured.Value.Id, 1_500, 50),
            CancellationToken.None);

        Assert.IsTrue(review.IsSuccess, review.Error?.Code);
        Assert.AreEqual(600L, review.Value.EstimatedSourceRefundNet.Value);
        Assert.AreEqual(597L, review.Value.ConfirmedMinSourceRefundNet.Value);

        Result<CommercePaymentView> refunded = await harness.Merchants.RefundAsync(
            new RefundCommercePaymentCommand(Merchant(), null, null, review.Value.Id, "REF-XC"),
            CancellationToken.None);

        Assert.IsTrue(refunded.IsSuccess, refunded.Error?.Code);
        Assert.AreEqual(CommercePaymentStatus.Refunded, refunded.Value.Status);
        Assert.AreEqual(1_500L, refunded.Value.PresentmentRefunded.Value);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM debit_card_refunds
                WHERE settlement_route = 'FX_FOK' AND payment_order_id IS NULL
                  AND fx_business_operation_id IS NOT NULL AND source_refund_minor = 600;
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_refund_confirmations
                WHERE consumed_at IS NOT NULL;
                """));
        Assert.AreEqual(
            "99600",
            harness.ReadText($"""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = x'{Convert.ToHexString(buyer.Value.ToByteArray())}';
                """));
    }

    [TestMethod]
    public async Task ACrossCurrencyCheckoutWithoutLiquidityIsRejected()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc")),
            CancellationToken.None);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsFalse(confirmation.IsSuccess);
        Assert.AreEqual(
            BankingErrorCodes.CommerceFxLiquidityInsufficient, confirmation.Error!.Code);
        Assert.AreEqual(0L, harness.Count("commerce_checkout_confirmations"));
    }

    [TestMethod]
    public async Task ACrossCurrencyCheckoutWithoutCurrencyTrustIsRejected()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);
        harness.Execute("UPDATE currency_trust_designations SET status = 'SUPERSEDED';");

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc")),
            CancellationToken.None);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsFalse(confirmation.IsSuccess);
        Assert.AreEqual(
            BankingErrorCodes.CommerceCurrencyTrustInsufficient, confirmation.Error!.Code);
    }

    [TestMethod]
    public async Task ASlippageBeyondTheMarketCeilingIsRejected()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);
        harness.Execute("""
            UPDATE fx_market_policy_versions SET maximum_market_slippage_bps = 10;
            """);

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc")),
            CancellationToken.None);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsFalse(confirmation.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CommerceSlippageInvalid, confirmation.Error!.Code);
    }

    [TestMethod]
    public async Task AMerchantWithCrossCurrencyDisabledRejectsTheConfirmation()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCrossCurrency();
        await ProvideLiquidityAsync(harness, 10_000, 150);
        harness.Execute("UPDATE merchant_profiles SET cross_currency_mode = 'DISABLED';");

        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                harness.CustomerOf(buyer),
                buyer,
                BankCardForm.IntegratedCashDebit,
                IdempotencyKey.Create("commerce", "card-xc")),
            CancellationToken.None);

        CommerceOrderId orderId = await ForeignOrderAsync(harness, 1_500);
        harness.Execute("UPDATE merchant_profiles SET cross_currency_mode = 'DISABLED';");

        Result<CommerceCheckoutConfirmationView> confirmation = await harness.Commerce
            .ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(Buyer(), orderId, harness.DebitCardOf(buyer), 50),
                CancellationToken.None);

        Assert.IsFalse(confirmation.IsSuccess);
        Assert.AreEqual(
            BankingErrorCodes.CommerceCrossCurrencyDisabled, confirmation.Error!.Code);
    }

    [TestMethod]
    public async Task ASettlementAccountOwnedByAnotherPartyIsRejected()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId foreign = await harness.OpenAccountAsync(BuyerUser, "buyer");
        await harness.OpenAccountAsync(MerchantUser, "seller");

        Result<MerchantProfileView> created = await harness.Merchants.CreateAsync(
            new CreateMerchantProfileCommand(
                Merchant(), foreign, "ヌメラ商店", "GLOBAL", "GLOBAL", "DISABLED", 50, 3600, 7200, true),
            CancellationToken.None);

        Assert.IsFalse(created.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantSettlementAccountInvalid, created.Error!.Code);
    }

    [TestMethod]
    public async Task AProductWithoutAPublishedPriceCannotBecomeActive()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);

        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Merchant(), profile.Id, "SKU-9", "未価格品", "説明", "UNLIMITED", "INHERIT"),
            CancellationToken.None);

        Result<MerchantProductView> activated = await harness.Merchants.SetProductStateAsync(
            new SetMerchantProductStateCommand(Merchant(), product.Value.Id, MerchantProductStatus.Active),
            CancellationToken.None);

        Assert.IsFalse(activated.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantProductNotSellable, activated.Error!.Code);
    }

    [TestMethod]
    public async Task PublishingASecondPriceRetiresThePreviousOne()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantProductPriceVersionView> second = await harness.Merchants.PublishPriceAsync(
            new PublishMerchantProductPriceCommand(Merchant(), product.Id, 1_500),
            CancellationToken.None);

        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.AreEqual(2L, second.Value.Version);
        Assert.AreEqual(1L, harness.Count(
            "merchant_product_price_versions WHERE status = 'PUBLISHED'"));
        Assert.AreEqual(1L, harness.Count(
            "merchant_product_price_versions WHERE status = 'RETIRED'"));
    }

    [TestMethod]
    public async Task InventoryCannotBecomeNegative()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id, "FINITE");

        Result<MerchantInventoryView> adjusted = await harness.Merchants.AdjustInventoryAsync(
            new AdjustMerchantInventoryCommand(Merchant(), product.Id, -6), CancellationToken.None);

        Assert.IsFalse(adjusted.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantInventoryInsufficient, adjusted.Error!.Code);
        Assert.AreEqual(1L, harness.Count("merchant_inventory_movements"));
    }

    [TestMethod]
    public async Task AnUnauthorisedActorCannotChangeTheCatalog()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<MerchantProductView> product = await harness.Merchants.CreateProductAsync(
            new CreateMerchantProductCommand(
                Buyer(), profile.Id, "SKU-2", "他人の商品", "説明", "UNLIMITED", "INHERIT"),
            CancellationToken.None);

        Assert.IsFalse(product.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantOperationForbidden, product.Error!.Code);
    }

    [TestMethod]
    public async Task CheckoutFixesTheSnapshotAndAwaitsConfirmation()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 2, "checkout-1"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(CommerceOrderStatus.AwaitingConfirmation, checkout.Value.Status);
        Assert.AreEqual(2_400L, checkout.Value.OrderTotalPresentment.Value);
        Assert.AreEqual(1, checkout.Value.Lines.Count);
        Assert.AreEqual("PENDING", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual(
            checkout.Value.CommerceOrderId.Value.ToString(),
            harness.ReadText("SELECT NULL;") is null ? null : checkout.Value.CommerceOrderId.Value.ToString());
        Assert.AreEqual(0L, harness.Count("merchant_inventory_movements"));
    }

    [TestMethod]
    public async Task TheSameInteractionReturnsTheSameOrder()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> first = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-7"),
            CancellationToken.None);

        Result<CommerceCheckoutView> second = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-7"),
            CancellationToken.None);

        Assert.IsTrue(first.IsSuccess, first.Error?.Code);
        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.AreEqual(first.Value.CommerceOrderId, second.Value.CommerceOrderId);
        Assert.AreEqual(1, second.Value.Lines.Count);
        Assert.AreEqual(1L, harness.Count("commerce_orders"));
        Assert.AreEqual(1L, harness.Count("commerce_payments"));
        Assert.AreEqual(1L, harness.Count("operation_results"));
    }

    [TestMethod]
    public async Task CheckoutCreationEnqueuesOneOutboxEvent()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-8"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM outbox_events o
                INNER JOIN business_operations b
                    ON b.business_operation_id = o.business_operation_id
                WHERE o.event_type = 'COMMERCE_CHECKOUT_CREATED'
                  AND b.operation_type = 'COMMERCE_CHECKOUT_CREATE'
                  AND b.status = 'COMMITTED';
                """));
    }

    [TestMethod]
    public async Task AnExpiredCheckoutIsCancelledByMaintenance()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-9"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        harness.Clock.Advance(CommerceApplicationService.CheckoutLifetimeMilliseconds + 1);

        CommerceMaintenanceReport report = await harness.Maintenance.ExpireCheckoutsAsync(
            CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(1, report.Cancelled);
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM commerce_orders;"));
        Assert.AreEqual("CANCELLED", harness.ReadText("SELECT status FROM commerce_payments;"));
    }

    [TestMethod]
    public async Task AnUnexpiredCheckoutSurvivesMaintenance()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Assert.IsTrue((await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "interaction-10"),
            CancellationToken.None)).IsSuccess);

        CommerceMaintenanceReport report = await harness.Maintenance.ExpireCheckoutsAsync(
            CancellationToken.None);

        Assert.AreEqual(0, report.Examined);
        Assert.AreEqual("AWAITING_CONFIRMATION", harness.ReadText("SELECT status FROM commerce_orders;"));
    }

    private static async Task<DepositAccountId> PrepareBuyerAsync(
        Harness harness,
        string token,
        long funding = 100_000L)
    {
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, funding);

        Result<CustomerAccountStatusView> customer =
            await harness.Registration.GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(BuyerUser), CancellationToken.None);

        Assert.IsTrue(customer.IsSuccess, customer.Error?.Code);

        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                customer.Value.Id,
                buyer,
                BankCardForm.DebitOnly,
                IdempotencyKey.Create("BANK_CARD_ISSUE", token)),
            CancellationToken.None);

        Assert.IsTrue(card.IsSuccess, card.Error?.Code);
        return buyer;
    }

    private static async Task<Result<CommercePaymentView>> CaptureAsync(
        Harness harness,
        MerchantProductId productId,
        string token,
        int quantity = 1)
    {
        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), productId, quantity, token),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        DepositAccountId buyer = harness.SourceOf(checkout.Value.CommerceOrderId);

        Result<CommerceCheckoutConfirmationView> review =
            await harness.Commerce.ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(
                    Buyer(), checkout.Value.CommerceOrderId, harness.DebitCardOf(buyer), 0),
                CancellationToken.None);

        Assert.IsTrue(review.IsSuccess, review.Error?.Code + " " + review.Error?.Field);

        return await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), review.Value.Id), CancellationToken.None);
    }

    private static async Task<(CommerceCheckoutConfirmationId Confirmation, DepositAccountId Buyer)>
        ConfirmableAsync(
            Harness harness,
            string token,
            long funding = 100_000L,
            string inventoryMode = "UNLIMITED",
            bool settleAtPartner = false)
    {
        MerchantProfileView profile = await CreateProfileAsync(
            harness, settleAtPartner: settleAtPartner);
        MerchantProductView product =
            await CreateActiveProductAsync(harness, profile.Id, inventoryMode);
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, funding);

        Result<CustomerAccountStatusView> customer =
            await harness.Registration.GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(BuyerUser), CancellationToken.None);

        Assert.IsTrue(customer.IsSuccess, customer.Error?.Code);

        Result<BankCardView> card = await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                customer.Value.Id,
                buyer,
                BankCardForm.DebitOnly,
                IdempotencyKey.Create("BANK_CARD_ISSUE", token)),
            CancellationToken.None);

        Assert.IsTrue(card.IsSuccess, card.Error?.Code);

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, token),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        Result<CommerceCheckoutConfirmationView> review =
            await harness.Commerce.ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(
                    Buyer(), checkout.Value.CommerceOrderId, harness.DebitCardOf(buyer), 0),
                CancellationToken.None);

        Assert.IsTrue(review.IsSuccess, review.Error?.Code + " " + review.Error?.Field);

        return (review.Value.Id, buyer);
    }

    [TestMethod]
    public async Task ConfirmingCapturesTheFullAmountInOneWrite()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, DepositAccountId buyer) =
            await ConfirmableAsync(harness, "capture-1");

        Result<CommercePaymentView> paid = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(paid.IsSuccess, paid.Error?.Code);
        Assert.AreEqual(CommercePaymentStatus.Paid, paid.Value.Status);
        Assert.AreEqual(1_200L, paid.Value.PresentmentPaid.Value);
        Assert.AreEqual("PAID", harness.ReadText("SELECT status FROM commerce_orders;"));
        Assert.AreEqual("PAID", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM debit_card_authorizations;"));
        Assert.AreEqual(1L, harness.Count("debit_card_captures"));
        Assert.AreEqual(98_800L, harness.PostedBalance(buyer));
        Assert.AreEqual(
            "0",
            harness.ReadText("""
                SELECT CAST(COALESCE(SUM(held_minor), 0) AS TEXT) FROM ledger_balance_projections;
                """));
    }

    [TestMethod]
    public async Task CapturingSettlesTheMerchantAndConsumesTheConfirmation()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) =
            await ConfirmableAsync(harness, "capture-2", inventoryMode: "FINITE");

        Assert.IsTrue((await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation),
            CancellationToken.None)).IsSuccess);

        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_checkout_confirmations
                WHERE consumed_at IS NOT NULL;
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM outbox_events
                WHERE event_type = 'COMMERCE_PAYMENT_CAPTURED';
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM merchant_inventory_movements
                WHERE movement_kind = 'SALE' AND quantity_delta = -1;
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_orders
                WHERE refund_eligible_until IS NOT NULL AND return_request_eligible_until IS NOT NULL;
                """));
    }

    [TestMethod]
    public async Task ARoleProductCannotBePurchasedTwiceWithoutAReturn()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness, catalogScope: "LOCAL_GUILD");
        MerchantProductView product = await CreateActiveProductAsync(
            harness, profile.Id, saleScopeOverride: "LOCAL_GUILD");

        Assert.IsTrue((await harness.Merchants.PublishFulfillmentPolicyAsync(
            new PublishMerchantFulfillmentPolicyCommand(
                Merchant(), product.Id, "DISCORD_ROLE", "ON_CAPTURE", "555000111"),
            CancellationToken.None)).IsSuccess);

        DepositAccountId buyer = await PrepareBuyerAsync(harness, "role-1");

        Assert.IsTrue((await CaptureAsync(harness, product.Id, "role-1")).IsSuccess);

        Result<CommercePaymentView> second = await CaptureAsync(harness, product.Id, "role-2");

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantRoleAlreadyHeld, second.Error!.Code);
        Assert.AreEqual(1L, harness.Count("debit_card_captures"));
        Assert.AreEqual(buyer, buyer);
    }

    [TestMethod]
    public async Task APurchasePolicyPublishedAfterCheckoutInvalidatesTheOrder()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        DepositAccountId buyer = await PrepareBuyerAsync(harness, "policy-1");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "policy-1"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        Assert.IsTrue((await harness.Merchants.PublishPurchasePolicyAsync(
            new PublishMerchantProductPurchasePolicyCommand(
                Merchant(), product.Id, 1, null, null, null, null),
            CancellationToken.None)).IsSuccess);

        Result<CommerceCheckoutConfirmationView> review =
            await harness.Commerce.ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(
                    Buyer(), checkout.Value.CommerceOrderId, harness.DebitCardOf(buyer), 0),
                CancellationToken.None);

        Assert.IsTrue(review.IsSuccess, review.Error?.Code);

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), review.Value.Id), CancellationToken.None);

        Assert.IsFalse(captured.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CommerceSnapshotStale, captured.Error!.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_captures"));
    }

    [TestMethod]
    public async Task ARepricedProductRejectsAStaleOrderSnapshot()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        DepositAccountId buyer = await PrepareBuyerAsync(harness, "stale-1");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "stale-1"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);

        harness.Execute("""
            UPDATE merchant_product_price_versions SET unit_price_minor = 9_999,
                version = version + 1
            WHERE status = 'PUBLISHED';
            """);

        Result<CommerceCheckoutConfirmationView> review =
            await harness.Commerce.ReviewCommerceCheckoutAsync(
                new ReviewCommerceCheckoutCommand(
                    Buyer(), checkout.Value.CommerceOrderId, harness.DebitCardOf(buyer), 0),
                CancellationToken.None);

        Assert.IsTrue(review.IsSuccess, review.Error?.Code);

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), review.Value.Id), CancellationToken.None);

        Assert.IsFalse(captured.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CommerceSnapshotStale, captured.Error!.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_captures"));
    }

    [TestMethod]
    public async Task ASameCurrencyRefundReturnsThePrincipalToTheCardholder()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, DepositAccountId buyer) =
            await ConfirmableAsync(harness, "refund-1");

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(captured.IsSuccess, captured.Error?.Code);

        Result<CommercePaymentView> refunded = await harness.Merchants.RefundAsync(
            new RefundCommercePaymentCommand(
                Merchant(), captured.Value.Id, 1_200, null, "REF-1"),
            CancellationToken.None);

        Assert.IsTrue(refunded.IsSuccess, refunded.Error?.Code);
        Assert.AreEqual(CommercePaymentStatus.Refunded, refunded.Value.Status);
        Assert.AreEqual(1_200L, refunded.Value.PresentmentRefunded.Value);
        Assert.AreEqual(1L, harness.Count("debit_card_refunds"));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM debit_card_refunds
                WHERE settlement_route = 'SAME_CURRENCY_PAYMENT' AND payment_order_id IS NOT NULL
                  AND fx_business_operation_id IS NULL;
                """));
        Assert.AreEqual("REFUNDED", harness.ReadText("SELECT status FROM commerce_orders;"));
        Assert.AreEqual(
            "100000",
            harness.ReadText($"""
                SELECT CAST(p.posted_balance_minor AS TEXT) FROM ledger_balance_projections AS p
                JOIN deposit_accounts AS d ON d.ledger_account_id = p.ledger_account_id
                WHERE d.deposit_account_id = x'{Convert.ToHexString(buyer.Value.ToByteArray())}';
                """));
    }

    [TestMethod]
    public async Task ARefundBeyondThePaidAmountIsRejected()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) = await ConfirmableAsync(harness, "refund-2");

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(captured.IsSuccess, captured.Error?.Code);

        Result<CommercePaymentView> refunded = await harness.Merchants.RefundAsync(
            new RefundCommercePaymentCommand(
                Merchant(), captured.Value.Id, 1_201, null, "REF-2"),
            CancellationToken.None);

        Assert.IsFalse(refunded.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CommerceReturnQuantityExceeded, refunded.Error!.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_refunds"));
    }

    [TestMethod]
    public async Task ASameBankCaptureFinalizesTheMerchantSettlementAtCaptureTime()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) = await ConfirmableAsync(harness, "final-1");

        Assert.IsTrue((await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation),
            CancellationToken.None)).IsSuccess);

        CommerceSettlementFinalityReport report =
            await harness.Maintenance.FinalizeMerchantSettlementsAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(1, report.Finalized);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_payments
                WHERE merchant_settlement_finalized_at = capture_committed_at;
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM outbox_events
                WHERE event_type = 'COMMERCE_SETTLEMENT_FINALIZED';
                """));

        CommerceSettlementFinalityReport again =
            await harness.Maintenance.FinalizeMerchantSettlementsAsync(CancellationToken.None);

        Assert.AreEqual(0, again.Examined);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM outbox_events
                WHERE event_type = 'COMMERCE_SETTLEMENT_FINALIZED';
                """));
    }

    [TestMethod]
    public async Task AnInterbankCaptureIsNotFinalUntilTheClearingInstructionSettles()
    {
        await using Harness harness = Harness.Create();
        harness.SeedPartnerBank();

        (CommerceCheckoutConfirmationId confirmation, _) =
            await ConfirmableAsync(harness, "final-2", settleAtPartner: true);

        Assert.IsTrue((await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation),
            CancellationToken.None)).IsSuccess);

        CommerceSettlementFinalityReport report =
            await harness.Maintenance.FinalizeMerchantSettlementsAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Examined);
        Assert.AreEqual(0, report.Finalized);
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_payments
                WHERE merchant_settlement_finalized_at IS NULL;
                """));
    }

    [TestMethod]
    public async Task AnInterbankCaptureClearsInsteadOfCreditingTheMerchant()
    {
        await using Harness harness = Harness.Create();
        harness.SeedPartnerBank();

        (CommerceCheckoutConfirmationId confirmation, DepositAccountId buyer) =
            await ConfirmableAsync(harness, "capture-interbank", settleAtPartner: true);

        Result<CommercePaymentView> captured = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(captured.IsSuccess, captured.Error?.Code);
        Assert.AreEqual(1L, harness.Count("clearing_instructions"));
        Assert.AreEqual(1L, harness.Count("debit_card_captures"));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM payment_orders
                WHERE settlement_mode = 'CLEARING' AND method = 'DEBIT_CARD_MERCHANT';
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CLEARING_PAYABLE' AND p.posted_balance_minor > 0;
                """));
    }

    [TestMethod]
    public async Task AConsumedConfirmationReturnsTheSavedResult()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) = await ConfirmableAsync(harness, "capture-3");

        Result<CommercePaymentView> first = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(first.IsSuccess, first.Error?.Code);

        Result<CommercePaymentView> replay = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsTrue(replay.IsSuccess, replay.Error?.Code);
        Assert.AreEqual(first.Value.Id, replay.Value.Id);
        Assert.AreEqual(first.Value.PresentmentPaid, replay.Value.PresentmentPaid);
        Assert.AreEqual(CommercePaymentStatus.Paid, replay.Value.Status);
        Assert.AreEqual(1L, harness.Count("debit_card_captures"));
        Assert.AreEqual(1L, harness.Count("payment_orders"));
    }

    [TestMethod]
    public async Task ARejectedConfirmationReplaysTheSavedRejection()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) =
            await ConfirmableAsync(harness, "capture-4", funding: 0);

        Result<CommercePaymentView> first = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsFalse(first.IsSuccess);

        Result<CommercePaymentView> replay = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsFalse(replay.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CommerceCaptureRejected, replay.Error!.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_captures"));
    }

    [TestMethod]
    public async Task AnUnfundedBuyerConsumesTheConfirmationWithoutMoneyEffect()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) =
            await ConfirmableAsync(harness, "capture-4", funding: 100L);

        Result<CommercePaymentView> rejected = await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation), CancellationToken.None);

        Assert.IsFalse(rejected.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, rejected.Error!.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_authorizations"));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("payment_orders"));
        Assert.AreEqual("AWAITING_CONFIRMATION", harness.ReadText("SELECT status FROM commerce_orders;"));
        Assert.AreEqual("PENDING", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM commerce_checkout_confirmations
                WHERE consumed_at IS NOT NULL;
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM business_operations
                WHERE operation_type = 'COMMERCE_CAPTURE' AND status = 'FAILED';
                """));
    }

    [TestMethod]
    public async Task AnExpiredStandaloneHoldReleasesTheHeldAmount()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 5_000L);
        harness.SeedStandaloneHold(buyer, 1_500L, expiresAt: 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Holds);
        Assert.AreEqual("EXPIRED", harness.ReadText("SELECT status FROM holds;"));
        Assert.AreEqual(
            "0",
            harness.ReadText("""
                SELECT CAST(COALESCE(SUM(held_minor), 0) AS TEXT) FROM ledger_balance_projections;
                """));
    }

    [TestMethod]
    public async Task AnUnexpiredHoldSurvivesTheSweep()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 5_000L);
        harness.SeedStandaloneHold(buyer, 1_500L, expiresAt: 1_776_000_600_000L);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(0, report.Holds);
        Assert.AreEqual("ACTIVE", harness.ReadText("SELECT status FROM holds;"));
    }

    [TestMethod]
    public async Task ACapturedHoldIsNotTouchedByTheSweep()
    {
        await using Harness harness = Harness.Create();
        (CommerceCheckoutConfirmationId confirmation, _) = await ConfirmableAsync(harness, "expiry-1");

        Assert.IsTrue((await harness.Commerce.ConfirmCommerceCheckoutAsync(
            new ConfirmCommerceCheckoutCommand(Buyer(), confirmation),
            CancellationToken.None)).IsSuccess);

        harness.Clock.Advance(30L * 24 * 60 * 60 * 1000);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(0, report.Total);
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM debit_card_authorizations;"));
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM holds;"));
    }

    [TestMethod]
    public async Task AnExpiredAuthorizationReleasesItsHoldInTheSameSweep()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 5_000L);

        Result<CustomerAccountStatusView> customer =
            await harness.Registration.GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(BuyerUser), CancellationToken.None);

        Assert.IsTrue((await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                customer.Value.Id,
                buyer,
                BankCardForm.DebitOnly,
                IdempotencyKey.Create("BANK_CARD_ISSUE", "expiry-auth")),
            CancellationToken.None)).IsSuccess);

        harness.SeedStandaloneHold(buyer, 1_500L, expiresAt: 1_776_000_060_000L);
        harness.SeedAuthorization(profile.Id, buyer, "AUTHORIZED", 1_500L, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Authorizations);
        Assert.AreEqual(0, report.Holds);
        Assert.AreEqual("EXPIRED", harness.ReadText("SELECT status FROM debit_card_authorizations;"));
        Assert.AreEqual("RELEASED", harness.ReadText("SELECT status FROM holds;"));
        Assert.AreEqual(
            "0",
            harness.ReadText("""
                SELECT CAST(COALESCE(SUM(held_minor), 0) AS TEXT) FROM ledger_balance_projections;
                """));
    }

    [TestMethod]
    public async Task APartiallyCapturedAuthorizationFinalizesAsCaptured()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        DepositAccountId buyer = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(buyer, 5_000L);

        Result<CustomerAccountStatusView> customer =
            await harness.Registration.GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(BuyerUser), CancellationToken.None);

        Assert.IsTrue((await harness.Cards.IssueBankCardAsync(
            new IssueBankCardCommand(
                customer.Value.Id,
                buyer,
                BankCardForm.DebitOnly,
                IdempotencyKey.Create("BANK_CARD_ISSUE", "expiry-partial")),
            CancellationToken.None)).IsSuccess);

        harness.SeedStandaloneHold(buyer, 1_500L, expiresAt: 1_776_000_060_000L);
        harness.SeedAuthorization(profile.Id, buyer, "PARTIALLY_CAPTURED", 1_500L, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        ExpiryMaintenanceReport report = await harness.Expiries.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Authorizations);
        Assert.AreEqual("CAPTURED", harness.ReadText("SELECT status FROM debit_card_authorizations;"));
        Assert.AreEqual(
            "600",
            harness.ReadText("SELECT CAST(captured_amount_minor AS TEXT) FROM debit_card_authorizations;"));
    }

    [TestMethod]
    public async Task ADormantAccountIsChargedTheWeeklyFee()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(account, 5_000L);
        harness.SeedDormancy(account, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        DormancyMaintenanceReport report = await harness.Dormancy.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Assessed);
        Assert.AreEqual(0, report.Closed);
        Assert.AreEqual(4_999L, harness.PostedBalance(account));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM fee_assessments WHERE fee_type = 'DORMANCY_WEEKLY';
                """));
        Assert.AreEqual(
            "1",
            harness.ReadText("""
                SELECT CAST(COUNT(*) AS TEXT) FROM business_operations
                WHERE operation_type = 'DORMANCY_FEE' AND status = 'COMMITTED';
                """));
    }

    [TestMethod]
    public async Task TheNextDueAdvancesExactlySevenDays()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(account, 5_000L);
        harness.SeedDormancy(account, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        Assert.AreEqual(1, (await harness.Dormancy.ProcessDueAsync(CancellationToken.None)).Assessed);
        Assert.AreEqual(
            (1_776_000_060_000L + DormancyMaintenanceService.DormancyIntervalMilliseconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            harness.ReadText("SELECT CAST(next_dormancy_fee_at AS TEXT) FROM deposit_accounts;"));
    }

    [TestMethod]
    public async Task ADueIsNeverChargedTwice()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(account, 5_000L);
        harness.SeedDormancy(account, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        Assert.AreEqual(1, (await harness.Dormancy.ProcessDueAsync(CancellationToken.None)).Assessed);
        Assert.AreEqual(0, (await harness.Dormancy.ProcessDueAsync(CancellationToken.None)).Assessed);
        Assert.AreEqual(4_999L, harness.PostedBalance(account));
    }

    [TestMethod]
    public async Task DelayedDuesArePostedOldestFirst()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.Fund(account, 5_000L);
        harness.SeedDormancy(account, 1_776_000_060_000L);

        harness.Clock.Advance(3L * DormancyMaintenanceService.DormancyIntervalMilliseconds);

        DormancyMaintenanceReport report = await harness.Dormancy.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(3, report.Assessed);
        Assert.AreEqual(4_997L, harness.PostedBalance(account));
    }

    [TestMethod]
    public async Task AZeroBalanceDormantAccountEntersClosing()
    {
        await using Harness harness = Harness.Create();
        DepositAccountId account = await harness.OpenAccountAsync(BuyerUser, "buyer");
        harness.SeedDormancy(account, 1_776_000_060_000L);

        harness.Clock.Advance(120_000L);

        DormancyMaintenanceReport report = await harness.Dormancy.ProcessDueAsync(CancellationToken.None);

        Assert.AreEqual(0, report.Assessed);
        Assert.AreEqual(1, report.Closed);
        Assert.AreEqual("CLOSING", harness.ReadText("SELECT status FROM deposit_accounts;"));
        Assert.AreEqual("DORMANCY", harness.ReadText("SELECT closure_reason FROM deposit_accounts;"));
        Assert.AreEqual(0L, harness.Count("fee_assessments"));
    }

    [TestMethod]
    public async Task CheckoutOutsideThePaymentScopeIsRejected()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness, "GLOBAL", "LOCAL_GUILD");
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(OtherGuildId), product.Id, 1, "checkout-2"),
            CancellationToken.None);

        Assert.IsFalse(checkout.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantProductNotSellable, checkout.Error!.Code);
        Assert.AreEqual(0L, harness.Count("commerce_orders"));
    }

    [TestMethod]
    public async Task BrowsingHidesLocalStoresFromOtherGuilds()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness, "LOCAL_GUILD", "LOCAL_GUILD");
        await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantStorePageView> local = await harness.Commerce.BrowseMerchantStoresAsync(
            new BrowseMerchantStoresQuery(GuildId, null), CancellationToken.None);
        Result<MerchantStorePageView> foreign = await harness.Commerce.BrowseMerchantStoresAsync(
            new BrowseMerchantStoresQuery(OtherGuildId, null), CancellationToken.None);

        Assert.IsTrue(local.IsSuccess);
        Assert.AreEqual(1, local.Value.Items.Count);
        Assert.AreEqual(1, local.Value.Items[0].ActiveProductCount);
        Assert.IsTrue(foreign.IsSuccess);
        Assert.AreEqual(0, foreign.Value.Items.Count);
    }

    [TestMethod]
    public async Task ConfirmingACheckoutIsRejectedWhileCaptureIsUnavailable()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id, "FINITE");
        await harness.OpenAccountAsync(BuyerUser, "buyer");

        Result<CommerceCheckoutView> checkout = await harness.Commerce.CreateCommerceCheckoutAsync(
            new CreateCommerceCheckoutCommand(Buyer(), product.Id, 1, "checkout-3"),
            CancellationToken.None);

        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        Assert.AreEqual(0L, harness.Count("debit_card_authorizations"));
        Assert.AreEqual("PENDING", harness.ReadText("SELECT status FROM commerce_payments;"));
        Assert.AreEqual(
            "AWAITING_CONFIRMATION", harness.ReadText("SELECT status FROM commerce_orders;"));
    }

    [TestMethod]
    public async Task ListingProductsReturnsThePublishedPrice()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        await CreateActiveProductAsync(harness, profile.Id, "FINITE");

        Result<MerchantProductPageView> products = await harness.Commerce.ListMerchantProductsAsync(
            new ListMerchantProductsQuery(GuildId, profile.Id, null), CancellationToken.None);

        Assert.IsTrue(products.IsSuccess, products.Error?.Code);
        Assert.AreEqual(1, products.Value.Items.Count);
        Assert.AreEqual(1_200L, products.Value.Items[0].UnitPrice.Value);
        Assert.AreEqual(5L, products.Value.Items[0].OnHandQuantity);
    }

    [TestMethod]
    public async Task ADiscordRolePolicyRequiresALocalSaleScope()
    {
        await using Harness harness = Harness.Create();
        MerchantProfileView profile = await CreateProfileAsync(harness);
        MerchantProductView product = await CreateActiveProductAsync(harness, profile.Id);

        Result<MerchantFulfillmentPolicyVersionView> global =
            await harness.Merchants.PublishFulfillmentPolicyAsync(
                new PublishMerchantFulfillmentPolicyCommand(
                    Merchant(), product.Id, "DISCORD_ROLE", "ON_CAPTURE", "555000111"),
                CancellationToken.None);

        Assert.IsFalse(global.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.MerchantFulfillmentScopeInvalid, global.Error!.Code);
    }
}
