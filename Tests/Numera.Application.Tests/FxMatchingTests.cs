using Microsoft.Data.Sqlite;
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
public sealed class FxMatchingTests
{
    private const ulong BaseGuildId = 980UL;
    private const ulong QuoteGuildId = 981UL;
    private const string DisplayPattern = "{symbol}{amount}";

    private const string BaseInstitution = "NUM0001";
    private const string QuoteInstitution = "NUM0002";
    private const string ForeignInstitution = "NUM0003";
    private const ulong MakerUser = 780_000_000_000_000_001UL;
    private const ulong TakerUser = 780_000_000_000_000_002UL;
    private const ulong OwnerUser = 780_000_000_000_000_003UL;
    private const long PriceScale = 100;
    private const long LotSize = 100;

    private sealed record Trader(
        CustomerAccountId Customer,
        DepositAccountId BaseAccount,
        DepositAccountId QuoteAccount);

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

        public FxApplicationService Markets { get; private set; } = null!;

        public SettlementMaintenanceService Maintenance { get; private set; } = null!;

        public FxAdministrationApplicationService Administration { get; private set; } = null!;

        public CurrencyTrustAdministrationApplicationService Trust { get; private set; } = null!;

        public FxMarketId MarketId { get; } = FxMarketId.FromValue(EntityIdValue.FromBits(100));

        public static Harness Create(int makerFeeBps = 0, int takerFeeBps = 0)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-fx", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(makerFeeBps, takerFeeBps);

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            SqliteBankingWriteGateway gateway = new(new FinancialWriteCoordinator(harness.Coordinator));
            SequentialIdGenerator ids = new(9_000);

            PaymentApplicationService payments = new(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway, payments, harness.Clock, ids);
            harness.Markets = new FxApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Maintenance = new SettlementMaintenanceService(
                gateway, payments, harness.Clock, ids);
            harness.Administration = new FxAdministrationApplicationService(
                gateway, harness.Clock, ids);
            harness.Trust = new CurrencyTrustAdministrationApplicationService(
                gateway, harness.Clock, ids);
            harness.Authority = new MonetaryAuthorityAdministrationApplicationService(
                gateway, harness.Markets, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        public MonetaryAuthorityAdministrationApplicationService Authority { get; private set; } = null!;

        public FxInterventionMandateId MandateId { get; } =
            FxInterventionMandateId.FromValue(EntityIdValue.FromBits(196));

        public void SeedMonetaryAuthority() => Execute($"""
            INSERT OR IGNORE INTO system_owner_identities(discord_user_id, created_at)
            VALUES('{OwnerUser}', 1);

            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(180)}, 'GOVERNMENT', '通貨当局', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(181)}, {Blob(180)}, 'CENTRAL_BANK', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(182)}, {Blob(181)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES
                ({Blob(183)}, {Blob(181)}, NULL, '2000M', 'BASE_MONEY_ISSUANCE_LIABILITY', 'LIABILITY',
                    'CREDIT', {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(184)}, {Blob(181)}, NULL, '2550M', 'FX_CLEARING_PAYABLE', 'LIABILITY',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(185)}, {Blob(181)}, NULL, '2500M', 'FX_CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
                    {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(186)}, {Blob(181)}, NULL, '1600M', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET',
                    'DEBIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(187)}, {Blob(181)}, NULL, '2600M', 'CENTRAL_BANK_SETTLEMENT_LIABILITY',
                    'LIABILITY', 'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            VALUES({Blob(183)}, 1000000, 0, 1, 1);

            INSERT INTO monetary_authorities(monetary_authority_id, economy_scope_id, party_id,
                accounting_book_id, home_currency_id, home_fx_funding_ledger_account_id, status,
                version)
            VALUES({Blob(190)}, {Blob(5)}, {Blob(180)}, {Blob(181)}, {Blob(3)}, {Blob(183)}, 'ACTIVE',
                1);

            INSERT INTO official_reserve_portfolios(official_reserve_portfolio_id,
                monetary_authority_id, status, version)
            VALUES({Blob(191)}, {Blob(190)}, 'ACTIVE', 1);

            INSERT INTO official_reserve_positions(official_reserve_position_id,
                official_reserve_portfolio_id, currency_id, asset_ledger_account_id,
                custodian_monetary_authority_id, custodian_liability_ledger_account_id, status, version)
            VALUES({Blob(192)}, {Blob(191)}, {Blob(2)}, {Blob(186)}, {Blob(190)}, {Blob(187)}, 'ACTIVE',
                1);

            INSERT INTO currency_trust_policy_versions(currency_trust_policy_version_id,
                economy_scope_id, established_min_age_seconds, established_min_trade_days,
                established_min_counterparties, trusted_min_age_seconds, trusted_min_trade_days,
                trusted_min_counterparties, reserve_min_age_seconds, reserve_min_trade_days,
                reserve_min_counterparties, status, created_at, published_at, version)
            VALUES({Blob(193)}, {Blob(5)}, 604800, 3, 2, 2592000, 10, 3, 7776000, 30, 5, 'PUBLISHED',
                1, 1, 1);

            INSERT INTO authorization_decisions(authorization_decision_id, target_type, target_id,
                scope_guild_id, authority_kind, actor_discord_user_id, actor_customer_account_id,
                decision_kind, reason_code, occurred_at, supersedes_decision_id)
            VALUES({Blob(194)}, 'CURRENCY_TRUST_DESIGNATION', {Blob(2)}, NULL, 'SYSTEM_OWNER',
                '{OwnerUser}', NULL, 'APPROVE', NULL, 1, NULL);

            INSERT INTO currency_trust_designations(currency_trust_designation_id, currency_id,
                currency_trust_policy_version_id, trust_tier, status, authorization_decision_id,
                qualified_age_seconds, qualified_trade_days, qualified_counterparties, effective_from,
                terminal_at, version)
            VALUES({Blob(195)}, {Blob(2)}, {Blob(193)}, 'RESERVE_ELIGIBLE', 'ACTIVE', {Blob(194)},
                7776000, 30, 5, 1, NULL, 1);

            INSERT INTO fx_intervention_mandates(fx_intervention_mandate_id, monetary_authority_id,
                market_id, allowed_side, maximum_source_minor_per_order, maximum_source_minor_total,
                used_source_minor, maximum_slippage_bps, valid_from, valid_until, status, version)
            VALUES({Blob(196)}, {Blob(190)}, {Blob(100)}, 'BOTH', 3000, 9000, 0, 1000, 1,
                4102444800000, 'ACTIVE', 1);
            """);

        private void Seed(int makerFeeBps, int takerFeeBps)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '{BaseGuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(5)}, '{QuoteGuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(3)}, {Blob(5)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

                INSERT INTO currency_metadata_versions(currency_metadata_version_id, currency_id,
                    name, code, symbol, display_pattern, effective_from, effective_to, version)
                VALUES({Blob(200)}, {Blob(2)}, 'ベース通貨', 'BAS', 'B', '{DisplayPattern}', 1, NULL, 1);

                INSERT INTO currency_metadata_versions(currency_metadata_version_id, currency_id,
                    name, code, symbol, display_pattern, effective_from, effective_to, version)
                VALUES({Blob(201)}, {Blob(3)}, 'クォート通貨', 'QUO', 'Q', '{DisplayPattern}', 1, NULL, 1);
                """);

            SeedBank(16, BaseInstitution, scopeSeed: 1, currencySeed: 2);
            SeedBank(48, QuoteInstitution, scopeSeed: 5, currencySeed: 3);
            SeedBank(80, ForeignInstitution, scopeSeed: 5, currencySeed: 3);
            SeedPaymentNetwork(120, scopeSeed: 1, currencySeed: 2, networkCode: "BASENET");
            SeedPaymentNetwork(130, scopeSeed: 5, currencySeed: 3, networkCode: "QUOTENET");

            Execute($"""
                INSERT INTO fx_markets(market_id, base_currency_id, quote_currency_id, operator_party_id,
                    current_policy_version_id, price_scale, tick_size_price_units, lot_size_base_minor,
                    next_order_sequence_no, next_trade_sequence_no, status, version)
                VALUES({Blob(100)}, {Blob(2)}, {Blob(3)}, {Blob(16)}, {Blob(101)}, {PriceScale}, 1,
                    {LotSize}, 1, 1, 'ACTIVE', 1);

                INSERT INTO fx_market_policy_versions(fx_market_policy_version_id, market_id, maker_fee_bps,
                    taker_fee_bps, maximum_market_slippage_bps, effective_from, created_at, version)
                VALUES({Blob(101)}, {Blob(100)}, {makerFeeBps}, {takerFeeBps}, 1000, 1, 1, 1);

                INSERT INTO fx_market_summaries(market_id, last_trade_price_units, last_trade_sequence_no,
                    summary_version, order_book_version, updated_at)
                VALUES({Blob(100)}, NULL, NULL, 1, 1, 1);
                """);
        }

        public void SeedGovernance()
        {
            Execute($"""
                INSERT INTO system_owner_identities(discord_user_id, created_at)
                VALUES('{OwnerUser}', 1);

                INSERT INTO currency_trust_policy_versions(currency_trust_policy_version_id,
                    economy_scope_id, established_min_age_seconds, established_min_trade_days,
                    established_min_counterparties, trusted_min_age_seconds, trusted_min_trade_days,
                    trusted_min_counterparties, reserve_min_age_seconds, reserve_min_trade_days,
                    reserve_min_counterparties, status, created_at, published_at, retired_at, version)
                VALUES({Blob(160)}, {Blob(1)}, 604800, 3, 2, 2592000, 10, 3,
                    7776000, 30, 5, 'PUBLISHED', 1, 1, NULL, 1);
                """);
        }

        public void SeedUnresolvedIssue()
        {
            Execute($"""
                INSERT INTO reconciliation_runs(reconciliation_run_id, scope_type, scope_id, started_at,
                    completed_at, status, version)
                VALUES({Blob(161)}, 'ECONOMY_SCOPE', {Blob(1)}, 1, 2, 'ISSUES_FOUND', 1);

                INSERT INTO reconciliation_issues(reconciliation_issue_id, reconciliation_run_id,
                    issue_code, severity, target_type, target_id, detail, detected_at, resolved_at,
                    resolution_business_operation_id)
                VALUES({Blob(162)}, {Blob(161)}, 'LEDGER_IMBALANCE', 'CRITICAL', 'CURRENCY', NULL,
                    'ledger-imbalance', 2, NULL, NULL);
                """);
        }

        public void DraftMarket()
        {
            Execute($"""
                UPDATE fx_markets SET status = 'PENDING_APPROVAL', version = version + 1
                WHERE market_id = {Blob(100)};
                """);
        }

        public void SeedQuoteSettlement()
        {
            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(140)}, 'SYSTEM', '中央銀行', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(141)}, {Blob(140)}, 'CENTRAL_BANK', 'OPEN', 1, 1);

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(142)}, {Blob(141)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(143)}, {Blob(141)}, NULL, '2100-1', 'CENTRAL_BANK_SETTLEMENT_LIABILITY',
                        'LIABILITY', 'CREDIT', {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(144)}, {Blob(141)}, NULL, '2100-2', 'CENTRAL_BANK_SETTLEMENT_LIABILITY',
                        'LIABILITY', 'CREDIT', {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(145)}, {Blob(49)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(146)}, {Blob(81)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                        {Blob(3)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                    currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
                VALUES
                    ({Blob(147)}, {Blob(50)}, {Blob(3)}, {Blob(143)}, 'ACTIVE', 1, NULL, 1),
                    ({Blob(148)}, {Blob(82)}, {Blob(3)}, {Blob(144)}, 'ACTIVE', 1, NULL, 1);

                INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                    settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                    effective_to, version)
                VALUES
                    ({Blob(149)}, {Blob(50)}, 'DIRECT', NULL, {Blob(147)}, 'ACTIVE', 1, NULL, 1),
                    ({Blob(150)}, {Blob(82)}, 'DIRECT', NULL, {Blob(148)}, 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                    version, updated_at)
                VALUES({Blob(145)}, 100000, 0, 1, 1), ({Blob(143)}, 100000, 0, 1, 1);
                """);
        }

        private void SeedPaymentNetwork(
            int partySeed,
            int scopeSeed,
            int currencySeed,
            string networkCode)
        {
            int book = partySeed + 1;
            int liquid = partySeed + 2;
            int network = partySeed + 3;
            int policy = partySeed + 4;

            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(partySeed)}, 'SYSTEM', '決済網主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(book)}, {Blob(partySeed)}, 'SYSTEM', 'OPEN', 1, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(liquid)}, {Blob(book)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT',
                    {Blob(currencySeed)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO payment_networks(payment_network_id, economy_scope_id, network_code,
                    operator_party_id, accounting_book_id, liquid_asset_ledger_account_id, status,
                    current_policy_version_id, version)
                VALUES({Blob(network)}, {Blob(scopeSeed)}, '{networkCode}', {Blob(partySeed)}, {Blob(book)},
                    {Blob(liquid)}, 'DRAFT', NULL, 1);

                INSERT INTO payment_network_policy_versions(payment_network_policy_version_id,
                    payment_network_id, settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                    clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                    per_bank_precredit_exposure_limit_minor, created_at, version)
                VALUES({Blob(policy)}, {Blob(network)}, 'CLEARING', 'AFTER_FINAL_SETTLEMENT', NULL, 3600,
                    0, 10000, 0, 1, 1);

                UPDATE payment_networks
                SET status = 'ACTIVE', current_policy_version_id = {Blob(policy)}, version = version + 1
                WHERE payment_network_id = {Blob(network)};
                """);
        }

        private void SeedBank(int partySeed, string institutionCode, int scopeSeed, int currencySeed)
        {
            int book = partySeed + 1;
            int bank = partySeed + 2;
            int branch = partySeed + 3;
            int control = partySeed + 4;
            int product = partySeed + 5;
            int productVersion = partySeed + 6;
            int policy = partySeed + 7;
            int schedule = partySeed + 8;
            int period = partySeed + 9;
            int revenue = partySeed + 10;
            int payable = partySeed + 11;
            int receivable = partySeed + 12;

            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(partySeed)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(book)}, {Blob(partySeed)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(period)}, {Blob(book)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(bank)}, {Blob(scopeSeed)}, {Blob(partySeed)}, '{institutionCode}', 'ヌメラ銀行',
                    'NORMAL', NULL, 'OPERATING', {Blob(book)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at,
                    version)
                VALUES({Blob(branch)}, {Blob(bank)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES({Blob(control)}, {Blob(book)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY',
                    'CREDIT', {Blob(currencySeed)}, 0, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                    owner_reference_type, owner_reference_id, status, created_at, version)
                VALUES
                    ({Blob(revenue)}, {Blob(book)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                        {Blob(currencySeed)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(payable)}, {Blob(book)}, NULL, '2500', 'FX_CLEARING_PAYABLE', 'LIABILITY',
                        'CREDIT', {Blob(currencySeed)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(receivable)}, {Blob(book)}, NULL, '1500', 'FX_CLEARING_RECEIVABLE', 'ASSET',
                        'DEBIT', {Blob(currencySeed)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(product)}, {Blob(bank)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST',
                    'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                    effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor,
                    maximum_balance_minor, daily_outgoing_limit_minor, per_transaction_limit_minor,
                    transfer_capabilities, deposit_insurance_class_code, overdraft_policy, created_at)
                VALUES({Blob(productVersion)}, {Blob(product)}, 1, 1, NULL, 1000000000,
                    'ACTUAL_365_FIXED', 0, NULL, NULL, NULL, 'INTERNAL', 'STANDARD', 'NONE', 1);

                INSERT INTO bank_policy_versions(bank_policy_version_id, bank_id, opening_enabled,
                    minimum_customer_account_age_days, minimum_initial_funding_minor,
                    requires_manual_approval, reopen_closed_account_allowed,
                    public_receiving_enabled_default, cash_card_enabled, debit_card_enabled,
                    integrated_cash_debit_default, automatic_bank_card_issue_mode, cash_atm_enabled,
                    cash_card_validity_months, debit_card_validity_months, per_transfer_limit_minor,
                    daily_outgoing_limit_minor, per_atm_withdrawal_limit_minor,
                    daily_atm_withdrawal_limit_minor, daily_atm_transfer_limit_minor,
                    daily_debit_purchase_limit_minor, daily_fx_order_notional_limit_minor,
                    maximum_active_holds_minor, effective_from, effective_to, version)
                VALUES({Blob(policy)}, {Blob(bank)}, 1, 0, 0, 0, 1, 1, 1, 1, 0, 'NONE', 1, NULL, 12,
                    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

                INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                    effective_to, version)
                VALUES({Blob(schedule)}, {Blob(bank)}, 1, NULL, 1);

                UPDATE banks
                SET current_policy_version_id = {Blob(policy)},
                    current_fee_schedule_version_id = {Blob(schedule)},
                    version = version + 1
                WHERE bank_id = {Blob(bank)};
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

        public long Scalar(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() is long value ? value : 0L;
        }

        public string ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() as string ?? string.Empty;
        }

        public long Balance(DepositAccountId accountId) => Projection(accountId, "posted_balance_minor");

        public long Held(DepositAccountId accountId) => Projection(accountId, "held_minor");

        private long Projection(DepositAccountId accountId, string column) => Scalar($"""
            SELECT {column} FROM ledger_balance_projections
            WHERE ledger_account_id = (
                SELECT ledger_account_id FROM deposit_accounts
                WHERE deposit_account_id = x'{Convert.ToHexString(accountId.Value.ToByteArray())}');
            """);

        public void Fund(DepositAccountId accountId, long amount) => Execute($"""
            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            SELECT ledger_account_id, {amount}, 0, 1, 1 FROM deposit_accounts
            WHERE deposit_account_id = x'{Convert.ToHexString(accountId.Value.ToByteArray())}'
            ON CONFLICT(ledger_account_id) DO UPDATE
            SET posted_balance_minor = {amount}, version = version + 1;
            """);

        public async Task<Trader> TraderAsync(ulong discordUserId, string handle)
        {
            Result<CustomerAccountView> customer = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(BaseGuildId, discordUserId, handle, "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> baseAccount = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(BaseGuildId, customer.Value.Id, BaseInstitution),
                CancellationToken.None);

            Result<AccountOpeningView> quoteAccount = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(QuoteGuildId, customer.Value.Id, QuoteInstitution),
                CancellationToken.None);

            Assert.IsTrue(baseAccount.IsSuccess);
            Assert.IsTrue(quoteAccount.IsSuccess);

            return new Trader(customer.Value.Id, baseAccount.Value.Id, quoteAccount.Value.Id);
        }

        public async Task<DepositAccountId> ForeignQuoteAccountAsync(CustomerAccountId customer)
        {
            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(QuoteGuildId, customer, ForeignInstitution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess);

            return opened.Value.Id;
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

    private static Task<Result<FxOrderView>> SellAsync(
        Harness harness,
        Trader trader,
        long baseMinor,
        long? priceUnits,
        string key,
        FxOrderType orderType = FxOrderType.Limit,
        int? slippageBps = null) =>
        harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                Actor(),
                harness.MarketId,
                trader.Customer,
                FxOrderSide.SellBase,
                orderType,
                baseMinor,
                priceUnits,
                slippageBps,
                trader.BaseAccount,
                trader.QuoteAccount,
                IdempotencyKey.Create("fx-test", key)),
            CancellationToken.None);

    private static Task<Result<FxOrderView>> BuyAsync(
        Harness harness,
        Trader trader,
        long baseMinor,
        long? priceUnits,
        string key,
        FxOrderType orderType = FxOrderType.Limit,
        int? slippageBps = null,
        DepositAccountId? quoteAccount = null) =>
        harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                Actor(),
                harness.MarketId,
                trader.Customer,
                FxOrderSide.BuyBase,
                orderType,
                baseMinor,
                priceUnits,
                slippageBps,
                quoteAccount ?? trader.QuoteAccount,
                trader.BaseAccount,
                IdempotencyKey.Create("fx-test", key)),
            CancellationToken.None);

    private static AuthorizationContext Actor() =>
        new(AuthorizationLevel.Customer, MakerUser, BaseGuildId);

    private static async Task<(Trader Maker, Trader Taker)> TradersAsync(Harness harness)
    {
        Trader maker = await harness.TraderAsync(MakerUser, "maker");
        Trader taker = await harness.TraderAsync(TakerUser, "taker");

        harness.Fund(maker.BaseAccount, 10_000);
        harness.Fund(maker.QuoteAccount, 10_000);
        harness.Fund(taker.BaseAccount, 10_000);
        harness.Fund(taker.QuoteAccount, 10_000);

        return (maker, taker);
    }

    [TestMethod]
    public async Task ANonCrossingLimitOrderRestsAndHoldsTheSourceAmount()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);

        Result<FxOrderView> result = await SellAsync(harness, maker, 1_000, 150, "rest-1");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Open, result.Value.Status);
        Assert.AreEqual(1_000L, harness.Held(maker.BaseAccount));
        Assert.AreEqual(0L, harness.Count("fx_trades"));
        Assert.AreEqual(2L, harness.Scalar("SELECT order_book_version FROM fx_market_summaries;"));
    }

    [TestMethod]
    public async Task ACrossingLimitOrderSettlesBothCurrenciesInOneCommit()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 1_000, 150, "sell-1");
        Result<FxOrderView> result = await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Filled, result.Value.Status);
        Assert.AreEqual(1_000L, result.Value.FilledBaseMinor);

        Assert.AreEqual(1L, harness.Count("fx_trades"));
        Assert.AreEqual(2L, harness.Count("fx_settlement_legs"));
        Assert.AreEqual(2L, harness.Count("fx_settlement_leg_components"));
        Assert.AreEqual(2L, harness.Count("accounting_transactions"));

        Assert.AreEqual(9_000L, harness.Balance(maker.BaseAccount));
        Assert.AreEqual(11_500L, harness.Balance(maker.QuoteAccount));
        Assert.AreEqual(11_000L, harness.Balance(taker.BaseAccount));
        Assert.AreEqual(8_500L, harness.Balance(taker.QuoteAccount));

        Assert.AreEqual(0L, harness.Held(maker.BaseAccount));
        Assert.AreEqual(0L, harness.Held(taker.QuoteAccount));
        Assert.AreEqual(
            0L, harness.Scalar("SELECT COUNT(*) FROM holds WHERE status = 'ACTIVE';"));
    }

    [TestMethod]
    public async Task EveryTradeUpdatesTheLastTradeSummaryAndThreeBuckets()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 1_000, 150, "sell-1");
        await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.AreEqual(3L, harness.Count("fx_ohlc_buckets"));
        Assert.AreEqual(
            150L, harness.Scalar("SELECT last_trade_price_units FROM fx_market_summaries;"));
        Assert.AreEqual(
            1L, harness.Scalar("SELECT last_trade_sequence_no FROM fx_market_summaries;"));
        Assert.AreEqual(
            1_000L,
            harness.Scalar("SELECT base_volume_minor FROM fx_ohlc_buckets WHERE bucket_seconds = 60;"));
        Assert.AreEqual(
            1_500L,
            harness.Scalar("SELECT quote_volume_minor FROM fx_ohlc_buckets WHERE bucket_seconds = 3600;"));
    }

    [TestMethod]
    public async Task APartialFillLeavesTheRemainderRestingWithTheUnusedHold()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 400, 150, "sell-1");
        Result<FxOrderView> result = await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.PartiallyFilled, result.Value.Status);
        Assert.AreEqual(400L, result.Value.FilledBaseMinor);
        Assert.AreEqual(600L, result.Value.RemainingBaseMinor);
        Assert.AreEqual(900L, harness.Held(taker.QuoteAccount));
        Assert.AreEqual(
            1L, harness.Scalar("SELECT COUNT(*) FROM fx_orders WHERE status = 'FILLED';"));
    }

    [TestMethod]
    public async Task AMonetaryAuthorityInterventionBuysBaseAgainstTheBook()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);
        harness.SeedMonetaryAuthority();

        await SellAsync(harness, maker, 1_000, 150, "sell-mi");

        Result<FxOrderView> intervened = await harness.Authority.PlaceInterventionOrderAsync(
            new PlaceFxInterventionOrderCommand(
                new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, QuoteGuildId),
                harness.MandateId,
                FxOrderSide.BuyBase,
                1_000,
                150),
            CancellationToken.None);

        Assert.IsTrue(intervened.IsSuccess, intervened.Error?.Code);
        Assert.AreEqual(FxOrderStatus.Filled, intervened.Value.Status);
        Assert.AreEqual(1_000L, intervened.Value.FilledBaseMinor);
        Assert.AreEqual(
            "MONETARY_AUTHORITY",
            harness.ReadText("""
                SELECT participant_kind FROM fx_orders WHERE participant_kind = 'MONETARY_AUTHORITY';
                """));
        Assert.AreEqual(
            1_500L,
            harness.Scalar("""
                SELECT used_source_minor FROM fx_intervention_mandates;
                """));
        Assert.AreEqual(
            998_500L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'BASE_MONEY_ISSUANCE_LIABILITY';
                """));
        Assert.AreEqual(
            1_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN official_reserve_positions AS r
                    ON r.asset_ledger_account_id = p.ledger_account_id;
                """));
    }

    [TestMethod]
    public async Task AnInterventionBeyondTheMandateAllowanceIsRejected()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);
        harness.SeedMonetaryAuthority();

        await SellAsync(harness, maker, 4_000, 150, "sell-mi2");

        Result<FxOrderView> intervened = await harness.Authority.PlaceInterventionOrderAsync(
            new PlaceFxInterventionOrderCommand(
                new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, QuoteGuildId),
                harness.MandateId,
                FxOrderSide.BuyBase,
                4_000,
                150),
            CancellationToken.None);

        Assert.IsFalse(intervened.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.InterventionAllowanceExceeded, intervened.Error!.Code);
        Assert.AreEqual(0L, harness.Scalar(
            "SELECT COUNT(*) FROM fx_orders WHERE participant_kind = 'MONETARY_AUTHORITY';"));
        Assert.AreEqual(0L, harness.Scalar("SELECT used_source_minor FROM fx_intervention_mandates;"));
    }

    [TestMethod]
    public async Task AnOrderCrossingItsOwnPartyIsBlockedInsteadOfSkipped()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 400, 150, "self-1");
        await SellAsync(harness, taker, 400, 150, "self-2");

        Result<FxOrderView> result = await BuyAsync(harness, taker, 800, 150, "buy-self");

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(400L, result.Value.FilledBaseMinor);
        Assert.AreEqual(400L, result.Value.RemainingBaseMinor);
        Assert.AreEqual(
            1L, harness.Scalar("SELECT COUNT(*) FROM fx_trades;"));
    }

    [TestMethod]
    public async Task AnImmediateOrCancelOrderExpiresAndReleasesTheUnfilledRemainder()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 400, 150, "sell-1");

        Result<FxOrderView> result = await BuyAsync(
            harness, taker, 1_000, null, "buy-1", FxOrderType.MarketIoc, slippageBps: 0);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Expired, result.Value.Status);
        Assert.AreEqual(400L, result.Value.FilledBaseMinor);
        Assert.AreEqual(0L, harness.Held(taker.QuoteAccount));
        Assert.AreEqual(9_400L, harness.Balance(taker.QuoteAccount));
    }

    [TestMethod]
    public async Task AFillOrKillOrderThatCannotFillCompletelyIsRejectedWithoutTrades()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 400, 150, "sell-1");

        Result<FxOrderView> result = await BuyAsync(
            harness, taker, 1_000, null, "buy-1", FxOrderType.MarketFok, slippageBps: 0);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Rejected, result.Value.Status);
        Assert.AreEqual(0L, result.Value.FilledBaseMinor);
        Assert.AreEqual(0L, harness.Count("fx_trades"));
        Assert.AreEqual(0L, harness.Count("fx_settlement_legs"));
        Assert.AreEqual(10_000L, harness.Balance(taker.QuoteAccount));
        Assert.AreEqual(0L, harness.Held(taker.QuoteAccount));
    }

    [TestMethod]
    public async Task AFillOrKillOrderThatFillsCompletelyPostsEveryLeg()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 1_000, 150, "sell-1");

        Result<FxOrderView> result = await BuyAsync(
            harness, taker, 1_000, null, "buy-1", FxOrderType.MarketFok, slippageBps: 0);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Filled, result.Value.Status);
        Assert.AreEqual(1L, harness.Count("fx_trades"));
        Assert.AreEqual(8_500L, harness.Balance(taker.QuoteAccount));
    }

    [TestMethod]
    public async Task AMarketOrderWalksSeveralMakersInPriceTimeOrder()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 500, 160, "sell-high");
        await SellAsync(harness, maker, 500, 150, "sell-low");

        Result<FxOrderView> result = await BuyAsync(
            harness, taker, 1_000, null, "buy-1", FxOrderType.MarketIoc, slippageBps: 700);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(FxOrderStatus.Filled, result.Value.Status);
        Assert.AreEqual(2L, harness.Count("fx_trades"));
        Assert.AreEqual(
            150L, harness.Scalar("SELECT price_units FROM fx_trades WHERE sequence_no = 1;"));
        Assert.AreEqual(
            160L, harness.Scalar("SELECT price_units FROM fx_trades WHERE sequence_no = 2;"));
        Assert.AreEqual(8_450L, harness.Balance(taker.QuoteAccount));
        Assert.AreEqual(3L, harness.Scalar("SELECT summary_version FROM fx_market_summaries;"));
        Assert.AreEqual(4L, harness.Scalar("SELECT order_book_version FROM fx_market_summaries;"));
    }

    [TestMethod]
    public async Task TheOperatorFeeIsDeductedFromTheReceivedCurrency()
    {
        await using Harness harness = Harness.Create(makerFeeBps: 100);
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await BuyAsync(harness, maker, 1_000, 150, "buy-1");
        Result<FxOrderView> result = await SellAsync(harness, taker, 1_000, 150, "sell-1");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Filled, result.Value.Status);
        Assert.AreEqual(10_990L, harness.Balance(maker.BaseAccount));
        Assert.AreEqual(11_500L, harness.Balance(taker.QuoteAccount));
        Assert.AreEqual(3L, harness.Count("fx_settlement_leg_components"));
        Assert.AreEqual(
            10L, harness.Scalar("SELECT maker_fee_minor FROM fx_trades;"));
        Assert.AreEqual(
            10L,
            harness.Scalar("""
                SELECT amount_minor FROM fx_settlement_leg_components
                WHERE component_kind = 'OPERATOR_FEE';
                """));
    }

    [TestMethod]
    public async Task ALegCrossingBanksIsSettledThroughClearing()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        DepositAccountId foreignQuote = await harness.ForeignQuoteAccountAsync(maker.Customer);
        harness.Fund(foreignQuote, 10_000);

        Result<FxOrderView> resting = await harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                Actor(),
                harness.MarketId,
                maker.Customer,
                FxOrderSide.SellBase,
                FxOrderType.Limit,
                1_000,
                150,
                null,
                maker.BaseAccount,
                foreignQuote,
                IdempotencyKey.Create("fx-test", "sell-foreign")),
            CancellationToken.None);

        Assert.IsTrue(resting.IsSuccess, resting.Error?.Code);

        Result<FxOrderView> result = await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(FxOrderStatus.Filled, result.Value.Status);
        Assert.AreEqual(1L, harness.Count("fx_trades"));
        Assert.AreEqual(1L, harness.Count("clearing_instructions"));
        Assert.AreEqual(1L, harness.Count("clearing_cycles"));
        Assert.AreEqual(2L, harness.Count("clearing_positions"));

        Assert.AreEqual(
            1L,
            harness.Scalar("""
                SELECT COUNT(*) FROM fx_settlement_leg_components
                WHERE settlement_path = 'BANK_CLEARING' AND status = 'CLEARING';
                """));
        Assert.AreEqual(
            1L,
            harness.Scalar("""
                SELECT COUNT(*) FROM fx_settlement_leg_components
                WHERE settlement_path = 'INTERNAL_BOOK' AND status = 'INTERNAL_FINAL';
                """));
        Assert.AreEqual(
            1L, harness.Scalar("SELECT COUNT(*) FROM fx_settlement_legs WHERE status = 'CLEARING';"));
        Assert.AreEqual(
            1L, harness.Scalar("SELECT COUNT(*) FROM fx_settlement_legs WHERE status = 'SETTLED';"));

        Assert.AreEqual(11_000L, harness.Balance(taker.BaseAccount));
        Assert.AreEqual(8_500L, harness.Balance(taker.QuoteAccount));
        Assert.AreEqual(9_000L, harness.Balance(maker.BaseAccount));
        Assert.AreEqual(11_500L, harness.Balance(foreignQuote));
        Assert.AreEqual(1_500L, harness.Scalar(ClearingBalance("FX_CLEARING_PAYABLE")));
        Assert.AreEqual(1_500L, harness.Scalar(ClearingBalance("FX_CLEARING_RECEIVABLE")));
    }

    private static string ClearingBalance(string kind) => $"""
        SELECT COALESCE(SUM(p.posted_balance_minor), 0) FROM ledger_balance_projections AS p
        JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
        WHERE a.account_kind = '{kind}';
        """;

    [TestMethod]
    public async Task ClearingFinalitySettlesTheForeignLegAndUnwindsTheFxAccounts()
    {
        await using Harness harness = Harness.Create();
        harness.SeedQuoteSettlement();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        DepositAccountId foreignQuote = await harness.ForeignQuoteAccountAsync(maker.Customer);
        harness.Fund(foreignQuote, 10_000);

        await harness.Markets.PlaceFxOrderAsync(
            new PlaceFxOrderCommand(
                Actor(),
                harness.MarketId,
                maker.Customer,
                FxOrderSide.SellBase,
                FxOrderType.Limit,
                1_000,
                150,
                null,
                maker.BaseAccount,
                foreignQuote,
                IdempotencyKey.Create("fx-test", "sell-foreign")),
            CancellationToken.None);

        Result<FxOrderView> filled = await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.IsTrue(filled.IsSuccess, filled.Error?.Code);

        harness.Clock.Advance(7_200_000);

        SettlementMaintenanceReport report =
            await harness.Maintenance.ProcessClearingCyclesAsync(CancellationToken.None);

        Assert.AreEqual(1, report.Settled);
        Assert.AreEqual(
            1L,
            harness.Scalar("SELECT COUNT(*) FROM clearing_instructions WHERE status = 'SETTLED';"));
        Assert.AreEqual(
            0L,
            harness.Scalar("""
                SELECT COUNT(*) FROM fx_settlement_leg_components WHERE status = 'CLEARING';
                """));
        Assert.AreEqual(
            2L, harness.Scalar("SELECT COUNT(*) FROM fx_settlement_legs WHERE status = 'SETTLED';"));
        Assert.AreEqual(0L, harness.Scalar(ClearingBalance("FX_CLEARING_PAYABLE")));
        Assert.AreEqual(0L, harness.Scalar(ClearingBalance("FX_CLEARING_RECEIVABLE")));
    }

    [TestMethod]
    public async Task CancellingARestingOrderReleasesTheHoldAndBumpsTheBook()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);

        Result<FxOrderView> resting = await SellAsync(harness, maker, 1_000, 150, "sell-1");

        Result<FxOrderView> result = await harness.Markets.CancelFxOrderAsync(
            new CancelFxOrderCommand(
                Actor(),
                maker.Customer,
                resting.Value.Id,
                IdempotencyKey.Create("fx-test", "cancel-1")),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(FxOrderStatus.Cancelled, result.Value.Status);
        Assert.AreEqual(0L, harness.Held(maker.BaseAccount));
        Assert.AreEqual(3L, harness.Scalar("SELECT order_book_version FROM fx_market_summaries;"));
        Assert.AreEqual(
            1L,
            harness.Scalar(
                "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'FX_ORDER_CANCELLED';"));
    }

    [TestMethod]
    public async Task AnOrderBeyondTheAvailableBalanceIsRejectedWithoutSideEffects()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);
        harness.Fund(maker.BaseAccount, 500);

        Result<FxOrderView> result = await SellAsync(harness, maker, 1_000, 150, "sell-1");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("fx_orders"));
        Assert.AreEqual(0L, harness.Count("holds"));
        Assert.AreEqual(0L, harness.Count("fx_funding_endpoints"));
    }

    [TestMethod]
    public async Task AnAmountOffTheLotSizeIsRejected()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, _) = await TradersAsync(harness);

        Result<FxOrderView> result = await SellAsync(harness, maker, 150, 150, "sell-1");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.FxAmountNotRepresentable, result.Error!.Code);
    }

    [TestMethod]
    public async Task TheVisualSnapshotCarriesTheFourCacheKeyComponents()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 1_000, 150, "sell-1");
        await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        harness.Clock.Advance(120_000);

        Result<FxRateVisualView> rate = await harness.Markets.GetFxRateVisualAsync(
            new GetFxRateVisualQuery(harness.MarketId), CancellationToken.None);

        Result<FxBoardVisualView> board = await harness.Markets.GetFxBoardVisualAsync(
            new GetFxBoardVisualQuery(harness.MarketId), CancellationToken.None);

        Result<FxChartVisualView> chart = await harness.Markets.GetFxChartVisualAsync(
            new GetFxChartVisualQuery(harness.MarketId, 3600, 604_800L), CancellationToken.None);

        Assert.IsTrue(rate.IsSuccess);
        Assert.IsTrue(board.IsSuccess);
        Assert.IsTrue(chart.IsSuccess);

        Assert.AreEqual(rate.Value.CacheKey, board.Value.CacheKey);
        Assert.AreEqual(rate.Value.StatisticsAsOfMinute, rate.Value.CacheKey.StatisticsAsOfMinute);
        Assert.AreEqual(0L, rate.Value.StatisticsAsOfMinute % 60);
        Assert.AreEqual(2L, rate.Value.CacheKey.SummaryVersion);
        Assert.AreEqual(3L, rate.Value.CacheKey.OrderBookVersion);
        Assert.AreEqual(1L, rate.Value.CacheKey.ProjectionVersion);
        Assert.AreEqual(1L, chart.Value.CacheKey.ProjectionVersion);
        Assert.AreEqual("BAS/QUO", chart.Value.PairCode);
        Assert.AreEqual(100L, chart.Value.PriceScale);
        Assert.AreEqual(2, chart.Value.BaseMinorUnitDigits);
        Assert.AreEqual(150L, rate.Value.High24hPriceUnits);
        Assert.AreEqual(1_000L, rate.Value.Volume24hBaseMinor);
    }

    [TestMethod]
    public async Task ASingleGuildApprovalDoesNotActivateTheMarket()
    {
        await using Harness harness = Harness.Create();
        harness.SeedGovernance();
        harness.DraftMarket();

        Result<FxMarketView> first = await harness.Administration.SetMarketStateAsync(
            new SetFxMarketStateCommand(
                new AuthorizationContext(AuthorizationLevel.GuildOperator, MakerUser, BaseGuildId),
                harness.MarketId,
                FxMarketStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(first.IsSuccess, first.Error?.Code);
        Assert.AreEqual(FxMarketStatus.PendingApproval, first.Value.Status);
        Assert.AreEqual(1L, harness.Count("authorization_decisions"));

        Result<FxMarketView> second = await harness.Administration.SetMarketStateAsync(
            new SetFxMarketStateCommand(
                new AuthorizationContext(AuthorizationLevel.GuildOperator, TakerUser, QuoteGuildId),
                harness.MarketId,
                FxMarketStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(second.IsSuccess, second.Error?.Code);
        Assert.AreEqual(FxMarketStatus.Active, second.Value.Status);
        Assert.AreEqual(2L, harness.Count("authorization_decisions"));
        Assert.AreEqual(2L, harness.Count("audit_records"));
        Assert.AreEqual(
            2L,
            harness.Scalar("""
                SELECT COUNT(*) FROM outbox_events WHERE event_type = 'FX_MARKET_DECISION_RECORDED';
                """));
    }

    [TestMethod]
    public async Task ASystemOwnerOverrideActivatesTheMarketAlone()
    {
        await using Harness harness = Harness.Create();
        harness.SeedGovernance();
        harness.DraftMarket();

        Result<FxMarketView> result = await harness.Administration.SetMarketStateAsync(
            new SetFxMarketStateCommand(
                new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, BaseGuildId),
                harness.MarketId,
                FxMarketStatus.Active),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(FxMarketStatus.Active, result.Value.Status);
        Assert.AreEqual(
            "OVERRIDE",
            harness.ReadText("SELECT decision_kind FROM authorization_decisions;"));
    }

    private static async Task TradeOnThreeDaysAsync(Harness harness)
    {
        (Trader maker, Trader taker) = await TradersAsync(harness);

        for (int day = 0; day < 3; day++)
        {
            string suffix = day.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await SellAsync(harness, maker, 1_000, 150, "sell-" + suffix);
            Result<FxOrderView> filled = await BuyAsync(harness, taker, 1_000, 150, "buy-" + suffix);

            Assert.IsTrue(filled.IsSuccess, filled.Error?.Code);

            harness.Clock.Advance(24L * 60 * 60 * 1000);
        }
    }

    [TestMethod]
    public async Task TheTrustDesignationRecomputesTheObservationsFreshly()
    {
        await using Harness harness = Harness.Create();
        harness.SeedGovernance();
        await TradeOnThreeDaysAsync(harness);

        Result<CurrencyTrustDesignationView> result = await harness.Trust
            .PublishDesignationAsync(
                new PublishCurrencyTrustDesignationCommand(
                    new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, BaseGuildId),
                    CurrencyId.FromValue(EntityIdValue.FromBits(2)),
                    CurrencyTrustTier.Established),
                CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(CurrencyTrustTier.Established, result.Value.Tier);
        Assert.AreEqual(
            3L, harness.Scalar("SELECT qualified_trade_days FROM currency_trust_designations;"));
        Assert.AreEqual(
            2L, harness.Scalar("SELECT qualified_counterparties FROM currency_trust_designations;"));
        Assert.IsGreaterThan(
            604_800L, harness.Scalar("SELECT qualified_age_seconds FROM currency_trust_designations;"));
        Assert.AreEqual(
            1L,
            harness.Scalar("""
                SELECT COUNT(*) FROM authorization_decisions
                WHERE target_type = 'CURRENCY_TRUST_DESIGNATION';
                """));
        Assert.AreEqual(
            1L,
            harness.Scalar("""
                SELECT COUNT(*) FROM outbox_events WHERE event_type = 'CURRENCY_TRUST_DESIGNATED';
                """));
    }

    [TestMethod]
    public async Task ATierBeyondTheFreshObservationIsRejected()
    {
        await using Harness harness = Harness.Create();
        harness.SeedGovernance();
        await TradeOnThreeDaysAsync(harness);

        Result<CurrencyTrustDesignationView> result = await harness.Trust
            .PublishDesignationAsync(
                new PublishCurrencyTrustDesignationCommand(
                    new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, BaseGuildId),
                    CurrencyId.FromValue(EntityIdValue.FromBits(2)),
                    CurrencyTrustTier.Trusted),
                CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyTrustTierNotQualified, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("currency_trust_designations"));
    }

    [TestMethod]
    public async Task AnUnresolvedIntegrityIssueBlocksTheTrustDesignation()
    {
        await using Harness harness = Harness.Create();
        harness.SeedGovernance();
        harness.SeedUnresolvedIssue();
        await TradeOnThreeDaysAsync(harness);

        Result<CurrencyTrustDesignationView> result = await harness.Trust
            .PublishDesignationAsync(
                new PublishCurrencyTrustDesignationCommand(
                    new AuthorizationContext(AuthorizationLevel.SystemOwner, OwnerUser, BaseGuildId),
                    CurrencyId.FromValue(EntityIdValue.FromBits(2)),
                    CurrencyTrustTier.Established),
                CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.CurrencyTrustIntegrityBlocked, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("currency_trust_designations"));
    }

    [TestMethod]
    public async Task TheHistoryQueryReturnsTheCommittedTrades()
    {
        await using Harness harness = Harness.Create();
        (Trader maker, Trader taker) = await TradersAsync(harness);

        await SellAsync(harness, maker, 1_000, 150, "sell-1");
        await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Result<FxTradeHistoryPageView> history = await harness.Markets.GetFxHistoryAsync(
            new GetFxHistoryQuery(harness.MarketId, null), CancellationToken.None);

        Assert.IsTrue(history.IsSuccess);
        Assert.AreEqual(1, history.Value.Items.Count);
        Assert.AreEqual(150L, history.Value.Items[0].PriceUnits);
        Assert.AreEqual(1_000L, history.Value.Items[0].BaseMinor);
        Assert.AreEqual(1_500L, history.Value.Items[0].QuoteMinor);
    }
}
