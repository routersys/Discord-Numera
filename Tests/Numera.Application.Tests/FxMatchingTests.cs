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
    private const string BaseInstitution = "NUM0001";
    private const string QuoteInstitution = "NUM0002";
    private const string ForeignInstitution = "NUM0003";
    private const ulong MakerUser = 780_000_000_000_000_001UL;
    private const ulong TakerUser = 780_000_000_000_000_002UL;
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

            harness.Registration = new CustomerAccountApplicationService(
                gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids);
            harness.Accounts = new BankAccountApplicationService(
                gateway,
                new PaymentApplicationService(
                    gateway, new SqliteBankingReadGateway(harness.ConnectionFactory), harness.Clock, ids),
                harness.Clock,
                ids);
            harness.Markets = new FxApplicationService(gateway, harness.Clock, ids);

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

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
                """);

            SeedBank(16, BaseInstitution, scopeSeed: 1, currencySeed: 2);
            SeedBank(48, QuoteInstitution, scopeSeed: 5, currencySeed: 3);
            SeedBank(80, ForeignInstitution, scopeSeed: 5, currencySeed: 3);

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
                VALUES({Blob(revenue)}, {Blob(book)}, NULL, '4300', 'FEE_REVENUE', 'REVENUE', 'CREDIT',
                    {Blob(currencySeed)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

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
    public async Task AMakerSettlingAtAnotherBankRejectsTheWholeOrder()
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

        Assert.IsTrue(resting.IsSuccess);

        Result<FxOrderView> result = await BuyAsync(harness, taker, 1_000, 150, "buy-1");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            BankingErrorCodes.FxInterbankSettlementUnavailable, result.Error!.Code);
        Assert.AreEqual(0L, harness.Count("fx_trades"));
        Assert.AreEqual(
            1L, harness.Scalar("SELECT COUNT(*) FROM fx_orders;"));
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
