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
public sealed class AtmCashTests
{
    private const ulong GuildId = 990UL;
    private const string HomeInstitution = "NUM0090";
    private const string PartnerInstitution = "NUM0091";
    private const ulong CustomerUser = 790_000_000_000_000_001UL;

    private sealed class StubAtmCardImageRenderer : IBankCardImageRenderer
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

        public BankCardApplicationService Cards { get; private set; } = null!;

        public AtmApplicationService Atm { get; private set; } = null!;

        public CashAdministrationApplicationService Cash { get; private set; } = null!;

        public AtmTerminalId TerminalId { get; } = AtmTerminalId.FromValue(EntityIdValue.FromBits(60));

        public CurrencyId CurrencyId { get; } = CurrencyId.FromValue(EntityIdValue.FromBits(2));

        public static Harness Create(bool partnerTerminal = false, long ownFee = 0, long partnerFee = 0)
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-atm", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed(partnerTerminal, ownFee, partnerFee);

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
            harness.Cards = new BankCardApplicationService(
                gateway, harness.Clock, ids, new StubAtmCardImageRenderer());
            harness.Atm = new AtmApplicationService(gateway, harness.Clock, ids);
            harness.Cash = new CashAdministrationApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed(bool partnerTerminal, long ownFee, long partnerFee)
        {
            Execute($"""
                INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
                VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);

                INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
                    base_money_supply_cap_minor, created_at, retired_at, version)
                VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 0, NULL, 1, NULL, 1);

                INSERT INTO currency_denominations(currency_denomination_id, currency_id, value_minor,
                    kind, atm_dispense_enabled, atm_deposit_enabled, status, version)
                VALUES
                    ({Blob(10)}, {Blob(2)}, 1000, 'NOTE', 1, 1, 'ACTIVE', 1),
                    ({Blob(11)}, {Blob(2)}, 100, 'NOTE', 1, 1, 'ACTIVE', 1);

                INSERT INTO atm_networks(atm_network_id, name, status, version)
                VALUES({Blob(50)}, 'NUMERANET', 'ACTIVE', 1);
                """);

            SeedBank(20, HomeInstitution, ownFee, partnerFee);
            SeedBank(30, PartnerInstitution, ownFee, partnerFee);

            int ownerSeed = partnerTerminal ? 30 : 20;

            Execute($"""
                INSERT INTO atm_network_participations(atm_network_id, bank_id, issuer_enabled,
                    acquirer_enabled, withdrawal_enabled, deposit_enabled, balance_inquiry_enabled,
                    transfer_enabled, effective_from, effective_to, version)
                VALUES
                    ({Blob(50)}, {Blob(22)}, 1, 1, 1, 1, 1, 1, 0, NULL, 1),
                    ({Blob(50)}, {Blob(32)}, 1, 1, 1, 1, 1, 1, 0, NULL, 1);

                INSERT INTO atm_terminals(atm_terminal_id, owner_bank_id, placement_guild_id, branch_id,
                    atm_network_id, display_name, status, withdrawal_enabled, deposit_enabled,
                    balance_inquiry_enabled, transfer_enabled, version)
                VALUES({Blob(60)}, {Blob(ownerSeed + 2)}, '{GuildId}', NULL, {Blob(50)}, '本店ATM',
                    'OPERATING', 1, 1, 1, 1, 1);

                INSERT INTO atm_terminal_currency_services(atm_terminal_id, currency_id,
                    withdrawal_enabled, deposit_enabled, cross_currency_withdrawal_enabled, status,
                    version)
                VALUES({Blob(60)}, {Blob(2)}, 1, 1, 0, 'ACTIVE', 1);

                INSERT INTO cash_holders(cash_holder_id, currency_id, holder_type, owner_reference_id,
                    created_at)
                VALUES
                    ({Blob(61)}, {Blob(2)}, 'ATM_CASSETTE', {Blob(63)}, 1),
                    ({Blob(62)}, {Blob(2)}, 'ATM_CASSETTE', {Blob(64)}, 1);

                INSERT INTO atm_cash_cassettes(atm_cash_cassette_id, atm_terminal_id, cash_holder_id,
                    currency_denomination_id, cassette_role, cassette_priority, capacity_count, status,
                    version)
                VALUES
                    ({Blob(63)}, {Blob(60)}, {Blob(61)}, {Blob(10)}, 'RECYCLE', 0, 500, 'ACTIVE', 1),
                    ({Blob(64)}, {Blob(60)}, {Blob(62)}, {Blob(11)}, 'RECYCLE', 1, 500, 'ACTIVE', 1);

                INSERT INTO cash_positions(cash_holder_id, currency_denomination_id, on_hand_count,
                    reserved_count, version)
                VALUES
                    ({Blob(61)}, {Blob(10)}, 100, 0, 1),
                    ({Blob(62)}, {Blob(11)}, 100, 0, 1);
                """);
        }

        private void SeedBank(int partySeed, string institutionCode, long ownFee, long partnerFee)
        {
            int book = partySeed + 1;
            int bank = partySeed + 2;
            int branch = partySeed + 3;
            int control = partySeed + 4;
            int product = partySeed + 5;
            int productVersion = partySeed + 6;
            int policy = partySeed + 7;
            int schedule = partySeed + 8;

            Execute($"""
                INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
                VALUES({Blob(partySeed)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

                INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                    created_at, version)
                VALUES({Blob(book)}, {Blob(partySeed)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

                INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                    starts_on, ends_on, status, closed_at, version)
                VALUES({Blob(partySeed + 9)}, {Blob(book)}, '2026', '2000-01-01', '2100-12-31',
                    'OPEN', NULL, 1);

                INSERT INTO banks(bank_id, economy_scope_id, party_id, institution_code, name, bank_kind,
                    resolution_case_id, status, general_ledger_book_id, current_policy_version_id,
                    current_fee_schedule_version_id, created_at, version)
                VALUES({Blob(bank)}, {Blob(1)}, {Blob(partySeed)}, '{institutionCode}', 'ヌメラ銀行',
                    'NORMAL', NULL, 'OPERATING', {Blob(book)}, NULL, NULL, 1, 1);

                INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at,
                    closed_at, version)
                VALUES({Blob(branch)}, {Blob(bank)}, '001', '本店', 'ACTIVE', 1, NULL, 1);

                INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                    account_code, account_kind, accounting_type, normal_side, currency_id,
                    posting_allowed, owner_reference_type, owner_reference_id, status, created_at,
                    version)
                VALUES
                    ({Blob(control)}, {Blob(book)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY',
                        'CREDIT', {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(partySeed + 10)}, {Blob(book)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE',
                        'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(partySeed + 11)}, {Blob(book)}, NULL, '1000', 'CASH_ASSET', 'ASSET',
                        'DEBIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(partySeed + 12)}, {Blob(book)}, NULL, '2600', 'ATM_NETWORK_PAYABLE',
                        'LIABILITY', 'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                    ({Blob(partySeed + 13)}, {Blob(book)}, NULL, '1600', 'ATM_NETWORK_RECEIVABLE',
                        'ASSET', 'DEBIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

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
                VALUES({Blob(policy)}, {Blob(bank)}, 1, 0, 0, 0, 1, 1, 1, 1, 1, 'NONE', 1, 12, 12,
                    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, 1);

                INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                    effective_to, version)
                VALUES({Blob(schedule)}, {Blob(bank)}, 1, NULL, 1);

                INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                    account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                    amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                    basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                    free_occurrences_per_business_month)
                VALUES
                    ({Blob(partySeed + 14)}, {Blob(schedule)}, 'ATM_OWN_WITHDRAWAL', 0, 'ANY', NULL,
                        NULL, NULL, 0, NULL, 'ANY', NULL, NULL, {ownFee}, 0, 0, NULL, NULL, 0),
                    ({Blob(partySeed + 15)}, {Blob(schedule)}, 'ATM_PARTNER_WITHDRAWAL', 0, 'ANY', NULL,
                        NULL, NULL, 0, NULL, 'ANY', NULL, NULL, {partnerFee}, 0, 0, NULL, NULL, 0),
                    ({Blob(partySeed + 16)}, {Blob(schedule)}, 'ATM_OWN_DEPOSIT', 0, 'ANY', NULL,
                        NULL, NULL, 0, NULL, 'ANY', NULL, NULL, {ownFee}, 0, 0, NULL, NULL, 0),
                    ({Blob(partySeed + 17)}, {Blob(schedule)}, 'ATM_PARTNER_DEPOSIT', 0, 'ANY', NULL,
                        NULL, NULL, 0, NULL, 'ANY', NULL, NULL, {partnerFee}, 0, 0, NULL, NULL, 0);

                UPDATE banks
                SET current_policy_version_id = {Blob(policy)},
                    current_fee_schedule_version_id = {Blob(schedule)},
                    version = version + 1
                WHERE bank_id = {Blob(bank)};

                INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                    version_application_policy, status, created_at, version)
                VALUES({Blob(product)}, {Blob(bank)}, 'DEMAND01', '普通預金', 'DEMAND', 'FOLLOW_LATEST',
                    'ACTIVE', 1, 1);

                INSERT INTO account_product_versions(product_version_id, product_id, version,
                    effective_from, effective_to, annual_rate_ppt, day_count_basis,
                    minimum_balance_minor, maximum_balance_minor, daily_outgoing_limit_minor,
                    per_transaction_limit_minor, transfer_capabilities, deposit_insurance_class_code,
                    overdraft_policy, created_at)
                VALUES({Blob(productVersion)}, {Blob(product)}, 1, 1, NULL, 1000000000,
                    'ACTUAL_365_FIXED', 0, NULL, NULL, NULL, 'INTERNAL', 'STANDARD', 'NONE', 1);
                """);
        }

        public void SeedPaymentNetwork() => Execute($"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(70)}, 'SYSTEM', '決済網主体', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(71)}, {Blob(70)}, 'SYSTEM', 'OPEN', 1, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES({Blob(72)}, {Blob(71)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT',
                {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO payment_networks(payment_network_id, economy_scope_id, network_code,
                operator_party_id, accounting_book_id, liquid_asset_ledger_account_id, status,
                current_policy_version_id, version)
            VALUES({Blob(73)}, {Blob(1)}, 'ATMNET', {Blob(70)}, {Blob(71)}, {Blob(72)}, 'DRAFT',
                NULL, 1);

            INSERT INTO payment_network_policy_versions(payment_network_policy_version_id,
                payment_network_id, settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                per_bank_precredit_exposure_limit_minor, created_at, version)
            VALUES({Blob(74)}, {Blob(73)}, 'CLEARING', 'AFTER_FINAL_SETTLEMENT', NULL, 3600, 0, 10000,
                0, 1, 1);

            UPDATE payment_networks
            SET status = 'ACTIVE', current_policy_version_id = {Blob(74)}, version = version + 1
            WHERE payment_network_id = {Blob(73)};
            """);

        public void SeedCentralBank() => Execute($"""
            INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
            VALUES({Blob(90)}, 'SYSTEM', '中央銀行', 'ACTIVE', 1, 1);

            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES({Blob(91)}, {Blob(90)}, 'CENTRAL_BANK', 'OPEN', 1, 1);

            INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
                starts_on, ends_on, status, closed_at, version)
            VALUES({Blob(92)}, {Blob(91)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

            INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
                account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
                owner_reference_type, owner_reference_id, status, created_at, version)
            VALUES
                ({Blob(93)}, {Blob(91)}, NULL, '2100', 'CENTRAL_BANK_SETTLEMENT_LIABILITY', 'LIABILITY',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(94)}, {Blob(91)}, NULL, '2900', 'CASH_OUTSTANDING_LIABILITY', 'LIABILITY',
                    'CREDIT', {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1),
                ({Blob(95)}, {Blob(21)}, NULL, '1100', 'CENTRAL_BANK_RESERVE_ASSET', 'ASSET', 'DEBIT',
                    {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

            INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
            VALUES({Blob(96)}, {Blob(22)}, {Blob(2)}, {Blob(93)}, 'ACTIVE', 1, NULL, 1);

            INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                effective_to, version)
            VALUES({Blob(97)}, {Blob(22)}, 'DIRECT', NULL, {Blob(96)}, 'ACTIVE', 1, NULL, 1);

            INSERT INTO cash_holders(cash_holder_id, currency_id, holder_type, owner_reference_id,
                created_at)
            VALUES({Blob(98)}, {Blob(2)}, 'BANK_VAULT', {Blob(22)}, 1);

            INSERT INTO bank_cash_vaults(bank_cash_vault_id, bank_id, currency_id, cash_holder_id,
                status, version)
            VALUES({Blob(99)}, {Blob(22)}, {Blob(2)}, {Blob(98)}, 'ACTIVE', 1);

            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            VALUES({Blob(95)}, 100000, 0, 1, 1), ({Blob(93)}, 100000, 0, 1, 1);
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long Count(string table) => Scalar($"SELECT COUNT(*) FROM {table};");

        public long Scalar(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar() is long value ? value : 0L;
        }

        public long Balance(DepositAccountId accountId) => Scalar($"""
            SELECT posted_balance_minor FROM ledger_balance_projections
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

        public void StockWallet(CustomerAccountId customerAccountId, long thousands, long hundreds)
        {
            Execute($"""
                INSERT INTO cash_holders(cash_holder_id, currency_id, holder_type, owner_reference_id,
                    created_at)
                VALUES({Blob(80)}, {Blob(2)}, 'CUSTOMER_WALLET',
                    x'{Convert.ToHexString(customerAccountId.Value.ToByteArray())}', 1);

                INSERT INTO cash_wallets(cash_wallet_id, customer_account_id, currency_id,
                    cash_holder_id, created_at, version)
                VALUES({Blob(81)},
                    x'{Convert.ToHexString(customerAccountId.Value.ToByteArray())}', {Blob(2)},
                    {Blob(80)}, 1, 1);

                INSERT INTO cash_positions(cash_holder_id, currency_denomination_id, on_hand_count,
                    reserved_count, version)
                VALUES({Blob(80)}, {Blob(10)}, {thousands}, 0, 1),
                    ({Blob(80)}, {Blob(11)}, {hundreds}, 0, 1);
                """);
        }

        public async Task<(CustomerAccountId Customer, DepositAccountId Account)> OpenAsync()
        {
            Result<CustomerAccountView> customer = await Registration.RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(GuildId, CustomerUser, "taro", "利用者"),
                CancellationToken.None);

            Result<AccountOpeningView> opened = await Accounts.OpenDepositAccountAsync(
                new OpenDepositAccountCommand(GuildId, customer.Value.Id, HomeInstitution),
                CancellationToken.None);

            Assert.IsTrue(opened.IsSuccess, opened.Error?.Code);

            Result<BankCardView> card = await Cards.IssueBankCardAsync(
                new IssueBankCardCommand(
                    customer.Value.Id,
                    opened.Value.Id,
                    BankCardForm.IntegratedCashDebit,
                    IdempotencyKey.Create("atm", "card-1")),
                CancellationToken.None);

            Assert.IsTrue(card.IsSuccess, card.Error?.Code);

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

    private static AuthorizationContext Actor() =>
        new(AuthorizationLevel.Customer, CustomerUser, GuildId);

    private static AuthorizationContext Owner() =>
        new(AuthorizationLevel.GuildOperator, CustomerUser, GuildId);

    [TestMethod]
    public async Task AnOwnBankWithdrawalDebitsTheDepositAndMovesTheCash()
    {
        await using Harness harness = Harness.Create(ownFee: 110);
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(AtmTransactionStatus.Settled, result.Value.Status);
        Assert.AreEqual(46_490L, harness.Balance(account));
        Assert.AreEqual(1L, harness.Count("atm_transactions"));
        Assert.AreEqual(2L, harness.Count("cash_movements"));
        Assert.AreEqual(1L, harness.Count("fee_assessments"));
        Assert.AreEqual(
            97L,
            harness.Scalar($"""
                SELECT on_hand_count FROM cash_positions
                WHERE currency_denomination_id = x'{new string('0', 30)}0a';
                """));
        Assert.AreEqual(
            96L,
            harness.Scalar($"""
                SELECT on_hand_count FROM cash_positions AS p
                JOIN cash_holders AS h ON h.cash_holder_id = p.cash_holder_id
                WHERE h.holder_type = 'ATM_CASSETTE'
                  AND p.currency_denomination_id = x'{new string('0', 30)}0b';
                """));
        Assert.AreEqual(
            3_400L,
            harness.Scalar("""
                SELECT SUM(p.on_hand_count * d.value_minor) FROM cash_positions AS p
                JOIN cash_holders AS h ON h.cash_holder_id = p.cash_holder_id
                JOIN currency_denominations AS d
                    ON d.currency_denomination_id = p.currency_denomination_id
                WHERE h.holder_type = 'CUSTOMER_WALLET';
                """));
        Assert.AreEqual(
            110L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'FEE_REVENUE';
                """));
    }

    [TestMethod]
    public async Task AWithdrawalBeyondTheAvailableBalanceIsRejectedWithoutEffect()
    {
        await using Harness harness = Harness.Create(ownFee: 110);
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 3_400);

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AvailableBalanceInsufficient, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("atm_transactions"));
        Assert.AreEqual(0L, harness.Count("cash_movements"));
    }

    [TestMethod]
    public async Task AnAmountTheCassettesCannotComposeIsRejected()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_450, "atm-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmCashUnavailable, result.Error!.Code);
    }

    [TestMethod]
    public async Task AnOwnBankDepositCreditsTheDepositNetOfTheFee()
    {
        await using Harness harness = Harness.Create(ownFee: 50);
        (CustomerAccountId customer, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 1_000);
        harness.StockWallet(customer, thousands: 5, hundreds: 5);

        Result<AtmTransactionView> result = await harness.Atm.AtmDepositAsync(
            new AtmDepositCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 2_000, "atm-1"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(AtmTransactionStatus.Settled, result.Value.Status);
        Assert.AreEqual(2_950L, harness.Balance(account));
        Assert.AreEqual(
            3_500L,
            harness.Scalar("""
                SELECT SUM(p.on_hand_count * d.value_minor) FROM cash_positions AS p
                JOIN cash_holders AS h ON h.cash_holder_id = p.cash_holder_id
                JOIN currency_denominations AS d
                    ON d.currency_denomination_id = p.currency_denomination_id
                WHERE h.holder_type = 'CUSTOMER_WALLET';
                """));
        Assert.AreEqual(
            50L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'FEE_REVENUE';
                """));
    }

    [TestMethod]
    public async Task AWithdrawalBeyondThePerTransactionLimitIsRejected()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);
        harness.Execute("UPDATE bank_policy_versions SET per_atm_withdrawal_limit_minor = 2000;");

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AmountLimitExceeded, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("atm_transactions"));
    }

    [TestMethod]
    public async Task ASecondWithdrawalBeyondTheDailyLimitIsRejected()
    {
        await using Harness harness = Harness.Create();
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);
        harness.Execute("UPDATE bank_policy_versions SET daily_atm_withdrawal_limit_minor = 5000;");

        Result<AtmTransactionView> first = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsTrue(first.IsSuccess, first.Error?.Code);

        Result<AtmTransactionView> second = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-2"),
            CancellationToken.None);

        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.DailyOutgoingLimitExceeded, second.Error!.Code);
        Assert.AreEqual(1L, harness.Count("atm_transactions"));
    }

    [TestMethod]
    public async Task ADepositBeyondTheCustomerCashIsRejected()
    {
        await using Harness harness = Harness.Create();
        (CustomerAccountId customer, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 1_000);
        harness.StockWallet(customer, thousands: 1, hundreds: 0);

        Result<AtmTransactionView> result = await harness.Atm.AtmDepositAsync(
            new AtmDepositCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 2_000, "atm-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmAcceptCapacityExceeded, result.Error!.Code);
        Assert.AreEqual(1_000L, harness.Balance(account));
        Assert.AreEqual(0L, harness.Count("cash_movements"));
    }

    [TestMethod]
    public async Task APartnerWithdrawalRaisesNetworkClaimsAndAClearingInstruction()
    {
        await using Harness harness = Harness.Create(partnerTerminal: true, partnerFee: 220);
        harness.SeedPaymentNetwork();
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(AtmTransactionStatus.InterbankPending, result.Value.Status);
        Assert.AreEqual(46_160L, harness.Balance(account));
        Assert.AreEqual(1L, harness.Count("clearing_instructions"));
        Assert.AreEqual(2L, harness.Count("clearing_positions"));
        Assert.AreEqual(
            3_620L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'ATM_NETWORK_PAYABLE';
                """));
        Assert.AreEqual(
            3_620L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'ATM_NETWORK_RECEIVABLE';
                """));
        Assert.AreEqual(
            3_620L,
            harness.Scalar("SELECT amount_minor FROM clearing_instructions;"));
    }

    [TestMethod]
    public async Task ReserveConvertsToVaultCashAgainstTheCentralBankLegs()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCentralBank();

        Result<CashConversionView> result = await harness.Cash.ConvertReserveToCashAsync(
            new ConvertReserveToCashCommand(
                Owner(),
                BankId.FromValue(EntityIdValue.FromBits(22)),
                CurrencyDenominationId.FromValue(EntityIdValue.FromBits(10)),
                5,
                "convert-1"),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(5_000L, result.Value.Amount.Value);
        Assert.AreEqual(2L, harness.Count("accounting_transactions"));
        Assert.AreEqual(
            1L,
            harness.Scalar("""
                SELECT COUNT(*) FROM cash_movements
                WHERE movement_kind = 'CENTRAL_BANK_CONVERSION_IN';
                """));
        Assert.AreEqual(
            5L,
            harness.Scalar("""
                SELECT p.on_hand_count FROM cash_positions AS p
                JOIN cash_holders AS h ON h.cash_holder_id = p.cash_holder_id
                WHERE h.holder_type = 'BANK_VAULT';
                """));
        Assert.AreEqual(
            5_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CASH_ASSET'
                  AND a.accounting_book_id = (
                    SELECT general_ledger_book_id FROM banks WHERE institution_code = 'NUM0090');
                """));
        Assert.AreEqual(
            95_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CENTRAL_BANK_RESERVE_ASSET';
                """));
        Assert.AreEqual(
            5_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CASH_OUTSTANDING_LIABILITY';
                """));
    }

    [TestMethod]
    public async Task VaultCashConvertsBackToReserve()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCentralBank();

        await harness.Cash.ConvertReserveToCashAsync(
            new ConvertReserveToCashCommand(
                Owner(),
                BankId.FromValue(EntityIdValue.FromBits(22)),
                CurrencyDenominationId.FromValue(EntityIdValue.FromBits(10)),
                5,
                "convert-1"),
            CancellationToken.None);

        Result<CashConversionView> back = await harness.Cash.ConvertCashToReserveAsync(
            new ConvertCashToReserveCommand(
                Owner(),
                BankId.FromValue(EntityIdValue.FromBits(22)),
                CurrencyDenominationId.FromValue(EntityIdValue.FromBits(10)),
                5,
                "convert-2"),
            CancellationToken.None);

        Assert.IsTrue(back.IsSuccess, back.Error?.Code);
        Assert.AreEqual(
            0L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CASH_OUTSTANDING_LIABILITY';
                """));
        Assert.AreEqual(
            100_000L,
            harness.Scalar("""
                SELECT p.posted_balance_minor FROM ledger_balance_projections AS p
                JOIN ledger_accounts AS a ON a.ledger_account_id = p.ledger_account_id
                WHERE a.account_kind = 'CENTRAL_BANK_RESERVE_ASSET';
                """));
    }

    [TestMethod]
    public async Task ConvertingMoreCashThanTheVaultHoldsIsRejected()
    {
        await using Harness harness = Harness.Create();
        harness.SeedCentralBank();

        Result<CashConversionView> result = await harness.Cash.ConvertCashToReserveAsync(
            new ConvertCashToReserveCommand(
                Owner(),
                BankId.FromValue(EntityIdValue.FromBits(22)),
                CurrencyDenominationId.FromValue(EntityIdValue.FromBits(10)),
                5,
                "convert-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.BankCashInsufficient, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("cash_movements"));
    }

    [TestMethod]
    public async Task ATerminalOutsideTheNetworkIsRejected()
    {
        await using Harness harness = Harness.Create(partnerTerminal: true, partnerFee: 220);
        harness.SeedPaymentNetwork();
        harness.Execute("DELETE FROM atm_network_participations;");
        (_, DepositAccountId account) = await harness.OpenAsync();
        harness.Fund(account, 50_000);

        Result<AtmTransactionView> result = await harness.Atm.AtmWithdrawAsync(
            new AtmWithdrawCommand(
                Actor(), harness.TerminalId, account, harness.CurrencyId, 3_400, "atm-1"),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(BankingErrorCodes.AtmNetworkParticipationInvalid, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("atm_transactions"));
    }
}
