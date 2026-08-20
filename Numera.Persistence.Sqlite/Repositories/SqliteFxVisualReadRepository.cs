using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteFxVisualReadRepository : IFxVisualReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteFxVisualReadRepository(SqliteConnection connection) => this.connection = connection;

    public FxVisualSnapshot? Read(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd,
        int depthLevels)
    {
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);

        if (ReadMarket(marketId, transaction) is not { } market)
        {
            transaction.Commit();
            return null;
        }

        (long? lastTrade, long summaryVersion, long orderBookVersion) = ReadSummary(marketId, transaction);
        (IReadOnlyList<FxOhlcBucket> buckets, long projectionVersion) =
            ReadBuckets(marketId, bucketSeconds, windowStart, windowEnd, transaction);

        FxVisualSnapshot snapshot = new(
            marketId,
            market.PairCode,
            market.PriceScale,
            market.BaseMinorUnitDigits,
            lastTrade,
            summaryVersion,
            orderBookVersion,
            projectionVersion,
            buckets,
            ReadDepth(marketId, FxOrderSide.BuyBase, depthLevels, transaction),
            ReadDepth(marketId, FxOrderSide.SellBase, depthLevels, transaction));

        transaction.Commit();

        return snapshot;
    }

    private (string PairCode, long PriceScale, int BaseMinorUnitDigits)? ReadMarket(
        FxMarketId marketId,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = Command(
            """
            SELECT b.code, q.code, m.price_scale, bc.minor_unit_digits
            FROM fx_markets AS m
            INNER JOIN currencies AS bc ON bc.currency_id = m.base_currency_id
            LEFT JOIN currency_metadata_versions AS b
                ON b.currency_id = m.base_currency_id AND b.effective_to IS NULL
            LEFT JOIN currency_metadata_versions AS q
                ON q.currency_id = m.quote_currency_id AND q.effective_to IS NULL
            WHERE m.market_id = $market;
            """,
            transaction);

        command.Parameters.AddWithValue("$market", marketId.Value.ToByteArray());

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        string baseCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
        string quoteCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);

        return (baseCode + "/" + quoteCode, reader.GetInt64(2), reader.GetInt32(3));
    }

    private (long? LastTrade, long SummaryVersion, long OrderBookVersion) ReadSummary(
        FxMarketId marketId,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = Command(
            """
            SELECT last_trade_price_units, summary_version, order_book_version
            FROM fx_market_summaries WHERE market_id = $market;
            """,
            transaction);

        command.Parameters.AddWithValue("$market", marketId.Value.ToByteArray());

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? (reader.IsDBNull(0) ? null : reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : (null, 1L, 1L);
    }

    private (IReadOnlyList<FxOhlcBucket> Buckets, long ProjectionVersion) ReadBuckets(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = Command(
            """
            SELECT bucket_start, open_price_units, high_price_units, low_price_units,
                   close_price_units, base_volume_minor, quote_volume_minor, last_trade_sequence_no,
                   projection_version
            FROM fx_ohlc_buckets
            WHERE market_id = $market AND bucket_seconds = $seconds
              AND bucket_start >= $start AND bucket_start < $end
            ORDER BY bucket_start ASC;
            """,
            transaction);

        command.Parameters.AddWithValue("$market", marketId.Value.ToByteArray());
        command.Parameters.AddWithValue("$seconds", bucketSeconds);
        command.Parameters.AddWithValue("$start", windowStart);
        command.Parameters.AddWithValue("$end", windowEnd);

        List<FxOhlcBucket> buckets = [];
        long projectionVersion = 0;
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            buckets.Add(new FxOhlcBucket(
                marketId,
                bucketSeconds,
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8)));

            projectionVersion = checked(projectionVersion + reader.GetInt64(8));
        }

        return (buckets, projectionVersion);
    }

    private IReadOnlyList<FxDepthLevel> ReadDepth(
        FxMarketId marketId,
        FxOrderSide side,
        int depthLevels,
        SqliteTransaction transaction)
    {
        string order = side == FxOrderSide.BuyBase ? "DESC" : "ASC";

        using SqliteCommand command = Command(
            $"""
            SELECT price_units, SUM(original_base_minor - filled_base_minor)
            FROM fx_orders
            WHERE market_id = $market AND side = $side
              AND status IN ('OPEN','PARTIALLY_FILLED') AND price_units IS NOT NULL
            GROUP BY price_units
            HAVING SUM(original_base_minor - filled_base_minor) > 0
            ORDER BY price_units {order}
            LIMIT $limit;
            """,
            transaction);

        command.Parameters.AddWithValue("$market", marketId.Value.ToByteArray());
        command.Parameters.AddWithValue("$side", side.ToToken());
        command.Parameters.AddWithValue("$limit", depthLevels);

        List<FxDepthLevel> levels = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            levels.Add(new FxDepthLevel(reader.GetInt64(0), reader.GetInt64(1)));
        }

        return levels;
    }

    private SqliteCommand Command(string sql, SqliteTransaction transaction)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }
}
