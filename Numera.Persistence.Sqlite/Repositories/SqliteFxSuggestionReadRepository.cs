using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteFxSuggestionReadRepository : IFxSuggestionReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteFxSuggestionReadRepository(SqliteConnection connection) =>
        this.connection = connection;

    public IReadOnlyList<FxMarketSuggestion> ListMarkets(EconomyScopeId economyScopeId, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.market_id, b.code, q.code, m.status
            FROM fx_markets AS m
            INNER JOIN currencies AS bc ON bc.currency_id = m.base_currency_id
            INNER JOIN currencies AS qc ON qc.currency_id = m.quote_currency_id
            INNER JOIN currency_metadata_versions AS b
                ON b.currency_id = m.base_currency_id AND b.effective_to IS NULL
            INNER JOIN currency_metadata_versions AS q
                ON q.currency_id = m.quote_currency_id AND q.effective_to IS NULL
            WHERE (bc.economy_scope_id = $scope OR qc.economy_scope_id = $scope)
              AND m.status <> 'RETIRED'
            ORDER BY b.code ASC, q.code ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$limit", limit);

        List<FxMarketSuggestion> markets = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            markets.Add(new FxMarketSuggestion(
                FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.GetString(1) + "/" + reader.GetString(2),
                FxMarketCatalog.ParseToken(reader.GetString(3))));
        }

        return markets;
    }

    public IReadOnlyList<FxOrderSuggestion> ListRestingOrders(
        CustomerAccountId customerAccountId,
        int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.fx_order_id, b.code, q.code, o.side,
                   o.original_base_minor - o.filled_base_minor
            FROM fx_orders AS o
            INNER JOIN fx_markets AS m ON m.market_id = o.market_id
            INNER JOIN currency_metadata_versions AS b
                ON b.currency_id = m.base_currency_id AND b.effective_to IS NULL
            INNER JOIN currency_metadata_versions AS q
                ON q.currency_id = m.quote_currency_id AND q.effective_to IS NULL
            WHERE o.customer_account_id = $customer
              AND o.status IN ('OPEN', 'PARTIALLY_FILLED')
            ORDER BY o.sequence_no ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$customer", customerAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$limit", limit);

        List<FxOrderSuggestion> orders = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            orders.Add(new FxOrderSuggestion(
                FxOrderId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.GetString(1) + "/" + reader.GetString(2),
                FxMarketCatalog.ParseSideToken(reader.GetString(3)),
                reader.GetInt64(4)));
        }

        return orders;
    }
}
