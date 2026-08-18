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
        "destination_settlement_endpoint_id, source_hold_id, fee_policy_version_id, created_at, " +
        "terminal_at, version";

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
                $hold, $policy, $created, NULL, $version);
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
                terminal_at = $terminal,
                version = $version
            WHERE fx_order_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$filled", order.FilledBaseMinor);
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
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT market_id, bucket_seconds, bucket_start, open_price_units, high_price_units,
                   low_price_units, close_price_units, base_volume_minor, quote_volume_minor,
                   last_trade_sequence_no, projection_version
            FROM fx_ohlc_buckets
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
            buckets.Add(new FxOhlcBucket(
                FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10)));
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
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(18)),
            reader.IsDBNull(19) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(19)),
            reader.GetInt64(20));

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
