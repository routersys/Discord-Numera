using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteFxRepository : IFxRepository
{
    private const string MarketColumns =
        "market_id, base_currency_id, quote_currency_id, operator_party_id, current_policy_version_id, " +
        "price_scale, tick_size_price_units, lot_size_base_minor, next_order_sequence_no, " +
        "next_trade_sequence_no, status, version";

    private const string OrderColumns =
        "fx_order_id, market_id, participant_kind, participant_party_id, customer_account_id, side, " +
        "order_type, time_in_force, price_units, maximum_slippage_bps, original_base_minor, " +
        "filled_base_minor, sequence_no, status, source_funding_endpoint_id, " +
        "destination_settlement_endpoint_id, source_hold_id, fee_policy_version_id, " +
        "maker_received_gross_minor, maker_fee_charged_minor, taker_received_gross_minor, " +
        "taker_fee_charged_minor, created_at, terminal_at, version";

    private const string TradeColumns =
        "fx_trade_id, market_id, maker_order_id, taker_order_id, maker_fee_policy_version_id, " +
        "taker_fee_policy_version_id, business_operation_id, price_units, base_minor, quote_minor, " +
        "maker_fee_currency_id, maker_fee_minor, taker_fee_currency_id, taker_fee_minor, " +
        "sequence_no, executed_at";

    private const string LegColumns =
        "fx_settlement_leg_id, fx_trade_id, business_operation_id, leg_kind, currency_id, " +
        "source_funding_endpoint_id, destination_settlement_endpoint_id, gross_minor, " +
        "recipient_net_minor, operator_fee_minor, operator_fee_treasury_ledger_account_id, status, " +
        "created_at, version";

    private const string ComponentColumns =
        "fx_settlement_leg_component_id, fx_settlement_leg_id, component_kind, source_party_id, " +
        "destination_party_id, source_bank_id, destination_bank_id, settlement_path, " +
        "destination_settlement_endpoint_id, destination_ledger_account_id, amount_minor, " +
        "clearing_instruction_id, status, created_at, settled_at, version";

    private const string BucketColumns =
        "market_id, bucket_seconds, bucket_start, open_price_units, high_price_units, " +
        "low_price_units, close_price_units, base_volume_minor, quote_volume_minor, " +
        "last_trade_sequence_no, projection_version";

    private const string PolicyColumns =
        "fx_market_policy_version_id, market_id, maker_fee_bps, taker_fee_bps, " +
        "maximum_market_slippage_bps, effective_from, created_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteFxRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddMarket(FxMarket market)
    {
        ArgumentNullException.ThrowIfNull(market);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_markets({MarketColumns})
            VALUES($id, $base, $quote, $operator, $policy, $scale, $tick, $lot, $orderSeq,
                $tradeSeq, $status, $version);
            """);

        Bind(command, market);
        command.ExecuteNonQuery();
    }

    public void UpdateMarket(FxMarket market)
    {
        ArgumentNullException.ThrowIfNull(market);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE fx_markets
            SET current_policy_version_id = $policy,
                next_order_sequence_no = $orderSeq,
                next_trade_sequence_no = $tradeSeq,
                status = $status,
                version = $version
            WHERE market_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(market.Id.Value));
        command.Parameters.AddWithValue(
            "$policy",
            market.CurrentPolicyVersionId is { } policy
                ? SqliteValueMapper.ToBlob(policy.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$orderSeq", market.NextOrderSequenceNo);
        command.Parameters.AddWithValue("$tradeSeq", market.NextTradeSequenceNo);
        command.Parameters.AddWithValue("$status", market.Status.ToToken());
        command.Parameters.AddWithValue("$version", market.Version);
        command.Parameters.AddWithValue("$expected", market.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public FxMarket? FindMarket(FxMarketId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MarketColumns} FROM fx_markets WHERE market_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadMarket(reader) : null;
    }

    public FxMarket? FindMarketByPair(CurrencyId baseCurrencyId, CurrencyId quoteCurrencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MarketColumns} FROM fx_markets
            WHERE base_currency_id = $base AND quote_currency_id = $quote;
            """);

        command.Parameters.AddWithValue("$base", SqliteValueMapper.ToBlob(baseCurrencyId.Value));
        command.Parameters.AddWithValue("$quote", SqliteValueMapper.ToBlob(quoteCurrencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadMarket(reader) : null;
    }

    public IReadOnlyList<FxMarket> ListMarkets(EconomyScopeId economyScopeId, int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MarketColumns} FROM fx_markets AS m
            WHERE EXISTS(
                SELECT 1 FROM currencies AS c
                WHERE c.currency_id IN (m.base_currency_id, m.quote_currency_id)
                  AND c.economy_scope_id = $scope)
            ORDER BY m.market_id ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$limit", limit);

        List<FxMarket> markets = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            markets.Add(ReadMarket(reader));
        }

        return markets;
    }

    public void AddPolicyVersion(FxMarketPolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_market_policy_versions({PolicyColumns})
            VALUES($id, $market, $maker, $taker, $slippage, $from, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(policy.MarketId.Value));
        command.Parameters.AddWithValue("$maker", policy.MakerFeeBps);
        command.Parameters.AddWithValue("$taker", policy.TakerFeeBps);
        command.Parameters.AddWithValue("$slippage", policy.MaximumMarketSlippageBps);
        command.Parameters.AddWithValue("$from", policy.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$created", policy.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", policy.Version);

        command.ExecuteNonQuery();
    }

    public FxMarketPolicyVersion? FindPolicyVersion(FxMarketPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PolicyColumns} FROM fx_market_policy_versions
            WHERE fx_market_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxMarketPolicyVersion(
                FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
                reader.GetInt64(7))
            : null;
    }

    public long NextPolicyVersion(FxMarketId marketId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM fx_market_policy_versions WHERE market_id = $market;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));

        return (long)command.ExecuteScalar()!;
    }

    public void AddOrder(FxOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_orders({OrderColumns})
            VALUES($id, $market, $participantKind, $participantParty, $customer, $side, $type, $tif,
                $price, $slippage, $original, $filled, $sequence, $status, $funding, $settlement,
                $hold, $policy, $makerGross, $makerFee, $takerGross, $takerFee, $created, NULL,
                $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(order.Id.Value));
        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(order.MarketId.Value));
        command.Parameters.AddWithValue("$participantKind", order.ParticipantKind.ToToken());
        command.Parameters.AddWithValue(
            "$participantParty", SqliteValueMapper.ToBlob(order.ParticipantPartyId.Value));
        command.Parameters.AddWithValue(
            "$customer",
            order.CustomerAccountId is { } customer
                ? SqliteValueMapper.ToBlob(customer.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$side", order.Side.ToToken());
        command.Parameters.AddWithValue("$type", order.OrderType.ToToken());
        command.Parameters.AddWithValue("$tif", order.TimeInForce.ToToken());
        command.Parameters.AddWithValue("$price", (object?)order.PriceUnits ?? DBNull.Value);
        command.Parameters.AddWithValue("$slippage", (object?)order.MaximumSlippageBps ?? DBNull.Value);
        command.Parameters.AddWithValue("$original", order.OriginalBaseMinor);
        command.Parameters.AddWithValue("$filled", order.FilledBaseMinor);
        command.Parameters.AddWithValue("$sequence", order.SequenceNo);
        command.Parameters.AddWithValue("$status", order.Status.ToToken());
        command.Parameters.AddWithValue(
            "$funding", SqliteValueMapper.ToBlob(order.SourceFundingEndpointId.Value));
        command.Parameters.AddWithValue(
            "$settlement", SqliteValueMapper.ToBlob(order.DestinationSettlementEndpointId.Value));
        command.Parameters.AddWithValue("$hold", SqliteValueMapper.ToBlob(order.SourceHoldId.Value));
        command.Parameters.AddWithValue(
            "$policy", SqliteValueMapper.ToBlob(order.FeePolicyVersionId.Value));
        command.Parameters.AddWithValue("$makerGross", order.MakerReceivedGrossMinor);
        command.Parameters.AddWithValue("$makerFee", order.MakerFeeChargedMinor);
        command.Parameters.AddWithValue("$takerGross", order.TakerReceivedGrossMinor);
        command.Parameters.AddWithValue("$takerFee", order.TakerFeeChargedMinor);
        command.Parameters.AddWithValue("$created", order.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", order.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateOrder(FxOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE fx_orders
            SET filled_base_minor = $filled,
                status = $status,
                maker_received_gross_minor = $makerGross,
                maker_fee_charged_minor = $makerFee,
                taker_received_gross_minor = $takerGross,
                taker_fee_charged_minor = $takerFee,
                terminal_at = $terminal,
                version = $version
            WHERE fx_order_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$filled", order.FilledBaseMinor);
        command.Parameters.AddWithValue("$makerGross", order.MakerReceivedGrossMinor);
        command.Parameters.AddWithValue("$makerFee", order.MakerFeeChargedMinor);
        command.Parameters.AddWithValue("$takerGross", order.TakerReceivedGrossMinor);
        command.Parameters.AddWithValue("$takerFee", order.TakerFeeChargedMinor);
        command.Parameters.AddWithValue("$status", order.Status.ToToken());
        command.Parameters.AddWithValue(
            "$terminal", (object?)order.TerminalAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", order.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(order.Id.Value));
        command.Parameters.AddWithValue("$expected", order.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public FxOrder? FindOrder(FxOrderId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM fx_orders WHERE fx_order_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadOrder(reader) : null;
    }

    public IReadOnlyList<FxOrder> ListRestingOrders(FxMarketId marketId, FxOrderSide side, int limit)
    {
        string order = side == FxOrderSide.BuyBase ? "DESC" : "ASC";

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM fx_orders
            WHERE market_id = $market AND side = $side AND status IN ('OPEN','PARTIALLY_FILLED')
            ORDER BY price_units {order}, sequence_no ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));
        command.Parameters.AddWithValue("$side", side.ToToken());
        command.Parameters.AddWithValue("$limit", limit);

        List<FxOrder> orders = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            orders.Add(ReadOrder(reader));
        }

        return orders;
    }

    public IReadOnlyList<FxOrder> ListParticipantOrders(
        PartyId participantPartyId,
        long? afterCreatedAt,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM fx_orders
            WHERE participant_party_id = $party AND ($after IS NULL OR created_at > $after)
            ORDER BY created_at ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(participantPartyId.Value));
        command.Parameters.AddWithValue("$after", (object?)afterCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<FxOrder> orders = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            orders.Add(ReadOrder(reader));
        }

        return orders;
    }

    public IReadOnlyList<FxDepthLevel> ReadDepth(FxMarketId marketId, FxOrderSide side, int limit)
    {
        string order = side == FxOrderSide.BuyBase ? "DESC" : "ASC";

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT price_units, SUM(original_base_minor - filled_base_minor)
            FROM fx_orders
            WHERE market_id = $market AND side = $side
              AND status IN ('OPEN','PARTIALLY_FILLED') AND price_units IS NOT NULL
            GROUP BY price_units
            HAVING SUM(original_base_minor - filled_base_minor) > 0
            ORDER BY price_units {order}
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));
        command.Parameters.AddWithValue("$side", side.ToToken());
        command.Parameters.AddWithValue("$limit", limit);

        List<FxDepthLevel> levels = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            levels.Add(new FxDepthLevel(reader.GetInt64(0), reader.GetInt64(1)));
        }

        return levels;
    }

    public FxMarketSummary? FindSummary(FxMarketId marketId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT market_id, last_trade_price_units, last_trade_sequence_no, summary_version,
                   order_book_version, updated_at
            FROM fx_market_summaries WHERE market_id = $market;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxMarketSummary(
                FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)))
            : null;
    }

    public void UpsertSummary(FxMarketSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fx_market_summaries(
                market_id, last_trade_price_units, last_trade_sequence_no, summary_version,
                order_book_version, updated_at)
            VALUES($market, $price, $sequence, $summary, $book, $updated)
            ON CONFLICT(market_id) DO UPDATE
            SET last_trade_price_units = excluded.last_trade_price_units,
                last_trade_sequence_no = excluded.last_trade_sequence_no,
                summary_version = excluded.summary_version,
                order_book_version = excluded.order_book_version,
                updated_at = excluded.updated_at;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(summary.MarketId.Value));
        command.Parameters.AddWithValue(
            "$price", (object?)summary.LastTradePriceUnits ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$sequence", (object?)summary.LastTradeSequenceNo ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", summary.SummaryVersion);
        command.Parameters.AddWithValue("$book", summary.OrderBookVersion);
        command.Parameters.AddWithValue("$updated", summary.UpdatedAt.UnixMilliseconds);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<FxOhlcBucket> ListBuckets(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {BucketColumns} FROM fx_ohlc_buckets
            WHERE market_id = $market AND bucket_seconds = $seconds
              AND bucket_start >= $start AND bucket_start < $end
            ORDER BY bucket_start ASC;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));
        command.Parameters.AddWithValue("$seconds", bucketSeconds);
        command.Parameters.AddWithValue("$start", windowStart);
        command.Parameters.AddWithValue("$end", windowEnd);

        List<FxOhlcBucket> buckets = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            buckets.Add(ReadBucket(reader));
        }

        return buckets;
    }

    private static void Bind(SqliteCommand command, FxMarket market)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(market.Id.Value));
        command.Parameters.AddWithValue("$base", SqliteValueMapper.ToBlob(market.BaseCurrencyId.Value));
        command.Parameters.AddWithValue("$quote", SqliteValueMapper.ToBlob(market.QuoteCurrencyId.Value));
        command.Parameters.AddWithValue(
            "$operator", SqliteValueMapper.ToBlob(market.OperatorPartyId.Value));
        command.Parameters.AddWithValue(
            "$policy",
            market.CurrentPolicyVersionId is { } policy
                ? SqliteValueMapper.ToBlob(policy.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$scale", market.PriceScale);
        command.Parameters.AddWithValue("$tick", market.TickSizePriceUnits);
        command.Parameters.AddWithValue("$lot", market.LotSizeBaseMinor);
        command.Parameters.AddWithValue("$orderSeq", market.NextOrderSequenceNo);
        command.Parameters.AddWithValue("$tradeSeq", market.NextTradeSequenceNo);
        command.Parameters.AddWithValue("$status", market.Status.ToToken());
        command.Parameters.AddWithValue("$version", market.Version);
    }

    private static FxMarket ReadMarket(SqliteDataReader reader) =>
        FxMarket.Rehydrate(
            FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            PartyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(3))),
            reader.IsDBNull(4)
                ? null
                : FxMarketPolicyVersionId.FromValue(
                    EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            FxMarketCatalog.ParseToken(reader.GetString(10)),
            reader.GetInt64(11));

    private static FxOrder ReadOrder(SqliteDataReader reader) =>
        FxOrder.Rehydrate(
            FxOrderId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            FxOrderCatalog.ParseParticipantToken(reader.GetString(2)),
            PartyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(3))),
            reader.IsDBNull(4)
                ? null
                : CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
            FxMarketCatalog.ParseSideToken(reader.GetString(5)),
            FxMarketCatalog.ParseOrderTypeToken(reader.GetString(6)),
            FxMarketCatalog.ParseTimeInForceToken(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.GetInt64(10),
            reader.GetInt64(11),
            reader.GetInt64(12),
            FxOrderCatalog.ParseToken(reader.GetString(13)),
            FxFundingEndpointId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(14))),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(15))),
            HoldId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(16))),
            FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(17))),
            reader.GetInt64(18),
            reader.GetInt64(19),
            reader.GetInt64(20),
            reader.GetInt64(21),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(22)),
            reader.IsDBNull(23) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(23)),
            reader.GetInt64(24));

    public void AddTreasuryAccount(BankTreasuryFxAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO bank_treasury_fx_accounts(bank_treasury_fx_account_id, bank_id, currency_id,
                asset_ledger_account_id, status, version)
            VALUES($id, $bank, $currency, $ledger, $status, $version);
            """);

        BindTreasuryAccount(command, account);
        command.ExecuteNonQuery();
    }

    public void UpdateTreasuryAccount(BankTreasuryFxAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE bank_treasury_fx_accounts SET status = $status, version = $version
            WHERE bank_treasury_fx_account_id = $id;
            """);

        BindTreasuryAccount(command, account);
        command.ExecuteNonQuery();
    }

    public BankTreasuryFxAccountRecord? FindTreasuryAccount(BankId bankId, CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT bank_treasury_fx_account_id, bank_id, currency_id, asset_ledger_account_id,
                status, version
            FROM bank_treasury_fx_accounts WHERE bank_id = $bank AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new BankTreasuryFxAccountRecord(
                BankTreasuryFxAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                BankTreasuryFxAccountStatusCatalog.ParseToken(reader.GetString(4)),
                reader.GetInt64(5))
            : null;
    }

    public void AddFundingEndpoint(FxFundingEndpointRecord endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fx_funding_endpoints(fx_funding_endpoint_id, currency_id, endpoint_kind,
                owner_party_id, deposit_account_id, ledger_account_id, bank_id, monetary_authority_id,
                created_at)
            VALUES($id, $currency, $kind, $owner, $deposit, $ledger, $bank, NULL, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(endpoint.Id.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(endpoint.CurrencyId.Value));
        command.Parameters.AddWithValue("$kind", endpoint.EndpointKind);
        command.Parameters.AddWithValue("$owner", SqliteValueMapper.ToBlob(endpoint.OwnerPartyId.Value));
        command.Parameters.AddWithValue("$deposit", Blob(endpoint.DepositAccountId?.Value));
        command.Parameters.AddWithValue("$ledger", Blob(endpoint.LedgerAccountId?.Value));
        command.Parameters.AddWithValue("$bank", Blob(endpoint.BankId?.Value));
        command.Parameters.AddWithValue("$created", endpoint.CreatedAt.UnixMilliseconds);

        command.ExecuteNonQuery();
    }

    public FxFundingEndpointRecord? FindFundingEndpoint(FxFundingEndpointId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT fx_funding_endpoint_id, currency_id, endpoint_kind, owner_party_id,
                   deposit_account_id, ledger_account_id, bank_id, created_at
            FROM fx_funding_endpoints WHERE fx_funding_endpoint_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxFundingEndpointRecord(
                FxFundingEndpointId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                reader.IsDBNull(4)
                    ? null
                    : DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                reader.IsDBNull(5)
                    ? null
                    : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                reader.IsDBNull(6) ? null : BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)))
            : null;
    }

    public void AddSettlementEndpoint(FxSettlementEndpointRecord endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fx_settlement_endpoints(fx_settlement_endpoint_id, currency_id, endpoint_kind,
                deposit_account_id, atm_terminal_id, customer_cash_holder_id, business_operation_id,
                destination_ledger_account_id, destination_party_id, merchant_profile_id,
                commerce_order_id, created_at)
            VALUES($id, $currency, $kind, $deposit, NULL, NULL, $operation, $ledger, $party, NULL,
                NULL, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(endpoint.Id.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(endpoint.CurrencyId.Value));
        command.Parameters.AddWithValue("$kind", endpoint.EndpointKind);
        command.Parameters.AddWithValue("$deposit", Blob(endpoint.DepositAccountId?.Value));
        command.Parameters.AddWithValue("$operation", Blob(endpoint.BusinessOperationId?.Value));
        command.Parameters.AddWithValue("$ledger", Blob(endpoint.DestinationLedgerAccountId?.Value));
        command.Parameters.AddWithValue("$party", Blob(endpoint.DestinationPartyId?.Value));
        command.Parameters.AddWithValue("$created", endpoint.CreatedAt.UnixMilliseconds);

        command.ExecuteNonQuery();
    }

    public FxSettlementEndpointRecord? FindSettlementEndpoint(FxSettlementEndpointId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT fx_settlement_endpoint_id, currency_id, endpoint_kind, deposit_account_id,
                   business_operation_id, destination_ledger_account_id, destination_party_id, created_at
            FROM fx_settlement_endpoints WHERE fx_settlement_endpoint_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxSettlementEndpointRecord(
                FxSettlementEndpointId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                reader.IsDBNull(3)
                    ? null
                    : DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                reader.IsDBNull(4)
                    ? null
                    : BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                reader.IsDBNull(5)
                    ? null
                    : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                reader.IsDBNull(6) ? null : PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)))
            : null;
    }

    public void AddTrade(FxTradeRecord trade)
    {
        ArgumentNullException.ThrowIfNull(trade);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_trades({TradeColumns})
            VALUES($id, $market, $maker, $taker, $makerPolicy, $takerPolicy, $operation, $price,
                $base, $quote, $makerFeeCurrency, $makerFee, $takerFeeCurrency, $takerFee, $sequence,
                $executed);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(trade.Id.Value));
        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(trade.MarketId.Value));
        command.Parameters.AddWithValue("$maker", SqliteValueMapper.ToBlob(trade.MakerOrderId.Value));
        command.Parameters.AddWithValue("$taker", SqliteValueMapper.ToBlob(trade.TakerOrderId.Value));
        command.Parameters.AddWithValue(
            "$makerPolicy", SqliteValueMapper.ToBlob(trade.MakerFeePolicyVersionId.Value));
        command.Parameters.AddWithValue(
            "$takerPolicy", SqliteValueMapper.ToBlob(trade.TakerFeePolicyVersionId.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(trade.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$price", trade.PriceUnits);
        command.Parameters.AddWithValue("$base", trade.BaseMinor);
        command.Parameters.AddWithValue("$quote", trade.QuoteMinor);
        command.Parameters.AddWithValue(
            "$makerFeeCurrency", SqliteValueMapper.ToBlob(trade.MakerFeeCurrencyId.Value));
        command.Parameters.AddWithValue("$makerFee", trade.MakerFee.Value);
        command.Parameters.AddWithValue(
            "$takerFeeCurrency", SqliteValueMapper.ToBlob(trade.TakerFeeCurrencyId.Value));
        command.Parameters.AddWithValue("$takerFee", trade.TakerFee.Value);
        command.Parameters.AddWithValue("$sequence", trade.SequenceNo);
        command.Parameters.AddWithValue("$executed", trade.ExecutedAt.UnixMilliseconds);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<FxTradeRecord> ListTrades(
        FxMarketId marketId,
        long? beforeSequenceNo,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {TradeColumns} FROM fx_trades
            WHERE market_id = $market AND ($before IS NULL OR sequence_no < $before)
            ORDER BY sequence_no DESC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));
        command.Parameters.AddWithValue("$before", (object?)beforeSequenceNo ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<FxTradeRecord> trades = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            trades.Add(new FxTradeRecord(
                FxTradeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                FxMarketId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                FxOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                FxOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                FxMarketPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                FxMarketPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
                MoneyMinor.FromMinor(reader.GetInt64(11)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 12)),
                MoneyMinor.FromMinor(reader.GetInt64(13)),
                reader.GetInt64(14),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(15))));
        }

        return trades;
    }

    public void AddSettlementLeg(FxSettlementLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fx_settlement_legs(fx_settlement_leg_id, fx_trade_id, business_operation_id,
                leg_kind, currency_id, source_funding_endpoint_id, destination_settlement_endpoint_id,
                gross_minor, recipient_net_minor, operator_fee_minor,
                operator_fee_treasury_ledger_account_id, status, created_at, version)
            VALUES($id, $trade, $operation, $kind, $currency, $funding, $settlement, $gross, $net,
                $fee, $treasury, $status, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(leg.Id.Value));
        command.Parameters.AddWithValue("$trade", SqliteValueMapper.ToBlob(leg.TradeId.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(leg.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$kind", leg.LegKind.ToToken());
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(leg.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$funding", SqliteValueMapper.ToBlob(leg.SourceFundingEndpointId.Value));
        command.Parameters.AddWithValue(
            "$settlement", SqliteValueMapper.ToBlob(leg.DestinationSettlementEndpointId.Value));
        command.Parameters.AddWithValue("$gross", leg.Gross.Value);
        command.Parameters.AddWithValue("$net", leg.RecipientNet.Value);
        command.Parameters.AddWithValue("$fee", leg.OperatorFee.Value);
        command.Parameters.AddWithValue(
            "$treasury", Blob(leg.OperatorFeeTreasuryLedgerAccountId?.Value));
        command.Parameters.AddWithValue("$status", leg.Status.ToToken());
        command.Parameters.AddWithValue("$created", leg.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", leg.Version);

        command.ExecuteNonQuery();
    }

    public void AddSettlementLegComponent(FxSettlementLegComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fx_settlement_leg_components(fx_settlement_leg_component_id,
                fx_settlement_leg_id, component_kind, source_party_id, destination_party_id,
                source_bank_id, destination_bank_id, settlement_path,
                destination_settlement_endpoint_id, destination_ledger_account_id, amount_minor,
                clearing_instruction_id, status, created_at, settled_at, version)
            VALUES($id, $leg, $kind, $sourceParty, $destinationParty, $sourceBank, $destinationBank,
                $path, $settlement, $ledger, $amount, $clearing, $status, $created, $settled,
                $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(component.Id.Value));
        command.Parameters.AddWithValue("$leg", SqliteValueMapper.ToBlob(component.LegId.Value));
        command.Parameters.AddWithValue("$kind", component.ComponentKind.ToToken());
        command.Parameters.AddWithValue(
            "$sourceParty", SqliteValueMapper.ToBlob(component.SourcePartyId.Value));
        command.Parameters.AddWithValue(
            "$destinationParty", SqliteValueMapper.ToBlob(component.DestinationPartyId.Value));
        command.Parameters.AddWithValue("$sourceBank", Blob(component.SourceBankId?.Value));
        command.Parameters.AddWithValue("$destinationBank", Blob(component.DestinationBankId?.Value));
        command.Parameters.AddWithValue("$path", component.SettlementPath.ToToken());
        command.Parameters.AddWithValue(
            "$settlement", Blob(component.DestinationSettlementEndpointId?.Value));
        command.Parameters.AddWithValue("$ledger", Blob(component.DestinationLedgerAccountId?.Value));
        command.Parameters.AddWithValue("$amount", component.Amount.Value);
        command.Parameters.AddWithValue("$clearing", Blob(component.ClearingInstructionId?.Value));
        command.Parameters.AddWithValue("$status", component.Status.ToToken());
        command.Parameters.AddWithValue("$created", component.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$settled", (object?)component.SettledAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", component.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateSettlementLeg(FxSettlementLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE fx_settlement_legs SET status = $status, version = $version
            WHERE fx_settlement_leg_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", leg.Status.ToToken());
        command.Parameters.AddWithValue("$version", leg.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(leg.Id.Value));
        command.Parameters.AddWithValue("$expected", leg.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void UpdateSettlementLegComponent(FxSettlementLegComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE fx_settlement_leg_components
            SET status = $status, settled_at = $settled, version = $version
            WHERE fx_settlement_leg_component_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", component.Status.ToToken());
        command.Parameters.AddWithValue(
            "$settled", (object?)component.SettledAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", component.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(component.Id.Value));
        command.Parameters.AddWithValue("$expected", component.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public FxSettlementLeg? FindSettlementLeg(FxSettlementLegId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {LegColumns} FROM fx_settlement_legs WHERE fx_settlement_leg_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadLeg(reader) : null;
    }

    public IReadOnlyList<FxSettlementLegComponent> ListSettlementLegComponents(FxSettlementLegId legId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ComponentColumns} FROM fx_settlement_leg_components
            WHERE fx_settlement_leg_id = $leg ORDER BY component_kind ASC;
            """);

        command.Parameters.AddWithValue("$leg", SqliteValueMapper.ToBlob(legId.Value));

        return ReadComponents(command);
    }

    public IReadOnlyList<FxSettlementLegComponent> ListClearingComponents(
        ClearingInstructionId clearingInstructionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ComponentColumns} FROM fx_settlement_leg_components
            WHERE clearing_instruction_id = $instruction ORDER BY component_kind ASC;
            """);

        command.Parameters.AddWithValue(
            "$instruction", SqliteValueMapper.ToBlob(clearingInstructionId.Value));

        return ReadComponents(command);
    }

    private static IReadOnlyList<FxSettlementLegComponent> ReadComponents(SqliteCommand command)
    {
        List<FxSettlementLegComponent> components = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            components.Add(ReadComponent(reader));
        }

        return components;
    }

    private static FxSettlementLeg ReadLeg(SqliteDataReader reader) =>
        FxSettlementLeg.Rehydrate(
            FxSettlementLegId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            FxTradeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
            reader.GetString(3) == "BASE" ? FxSettlementLegKind.Base : FxSettlementLegKind.Quote,
            CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
            FxFundingEndpointId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
            FxSettlementEndpointId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
            MoneyMinor.FromMinor(reader.GetInt64(7)),
            MoneyMinor.FromMinor(reader.GetInt64(8)),
            MoneyMinor.FromMinor(reader.GetInt64(9)),
            reader.IsDBNull(10)
                ? null
                : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
            FxSettlementCatalog.ParseToken(reader.GetString(11)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(12)),
            reader.GetInt64(13));

    private static FxSettlementLegComponent ReadComponent(SqliteDataReader reader) =>
        FxSettlementLegComponent.Rehydrate(
            FxSettlementLegComponentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            FxSettlementLegId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            reader.GetString(2) == "RECIPIENT_NET"
                ? FxSettlementComponentKind.RecipientNet
                : FxSettlementComponentKind.OperatorFee,
            PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
            PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
            reader.IsDBNull(5) ? null : BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
            reader.IsDBNull(6) ? null : BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
            FxSettlementCatalog.ParsePathToken(reader.GetString(7)),
            reader.IsDBNull(8)
                ? null
                : FxSettlementEndpointId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
            reader.IsDBNull(9)
                ? null
                : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
            MoneyMinor.FromMinor(reader.GetInt64(10)),
            reader.IsDBNull(11)
                ? null
                : ClearingInstructionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 11)),
            FxSettlementCatalog.ParseComponentToken(reader.GetString(12)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(13)),
            reader.IsDBNull(14) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(14)),
            reader.GetInt64(15));

    public FxTradingObservation ObserveTrading(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(DISTINCT t.executed_at / 86400000),
                   COUNT(DISTINCT o.participant_party_id)
            FROM fx_trades AS t
            JOIN fx_markets AS m ON m.market_id = t.market_id
            JOIN fx_orders AS o ON o.fx_order_id IN (t.maker_order_id, t.taker_order_id)
            WHERE m.base_currency_id = $currency OR m.quote_currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxTradingObservation(reader.GetInt32(0), reader.GetInt32(1))
            : new FxTradingObservation(0, 0);
    }

    public FxOhlcBucket? FindBucket(FxMarketId marketId, int bucketSeconds, long bucketStart)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {BucketColumns} FROM fx_ohlc_buckets
            WHERE market_id = $market AND bucket_seconds = $seconds AND bucket_start = $start;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(marketId.Value));
        command.Parameters.AddWithValue("$seconds", bucketSeconds);
        command.Parameters.AddWithValue("$start", bucketStart);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadBucket(reader) : null;
    }

    public void UpsertBucket(FxOhlcBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_ohlc_buckets({BucketColumns})
            VALUES($market, $seconds, $start, $open, $high, $low, $close, $baseVolume, $quoteVolume,
                $sequence, $projection)
            ON CONFLICT(market_id, bucket_seconds, bucket_start) DO UPDATE
            SET open_price_units = excluded.open_price_units,
                high_price_units = excluded.high_price_units,
                low_price_units = excluded.low_price_units,
                close_price_units = excluded.close_price_units,
                base_volume_minor = excluded.base_volume_minor,
                quote_volume_minor = excluded.quote_volume_minor,
                last_trade_sequence_no = excluded.last_trade_sequence_no,
                projection_version = excluded.projection_version;
            """);

        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(bucket.MarketId.Value));
        command.Parameters.AddWithValue("$seconds", bucket.BucketSeconds);
        command.Parameters.AddWithValue("$start", bucket.BucketStart);
        command.Parameters.AddWithValue("$open", bucket.OpenPriceUnits);
        command.Parameters.AddWithValue("$high", bucket.HighPriceUnits);
        command.Parameters.AddWithValue("$low", bucket.LowPriceUnits);
        command.Parameters.AddWithValue("$close", bucket.ClosePriceUnits);
        command.Parameters.AddWithValue("$baseVolume", bucket.BaseVolumeMinor);
        command.Parameters.AddWithValue("$quoteVolume", bucket.QuoteVolumeMinor);
        command.Parameters.AddWithValue("$sequence", bucket.LastTradeSequenceNo);
        command.Parameters.AddWithValue("$projection", bucket.ProjectionVersion);

        command.ExecuteNonQuery();
    }

    private static FxOhlcBucket ReadBucket(SqliteDataReader reader) =>
        new(
            FxMarketId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10));

    private static object Blob(EntityIdValue? value) =>
        value is { } id ? SqliteValueMapper.ToBlob(id) : DBNull.Value;

    private static void BindTreasuryAccount(
        SqliteCommand command,
        BankTreasuryFxAccountRecord account)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(account.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(account.BankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(account.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$ledger", SqliteValueMapper.ToBlob(account.AssetLedgerAccountId.Value));
        command.Parameters.AddWithValue("$status", account.Status.ToToken());
        command.Parameters.AddWithValue("$version", account.Version);
    }
}
