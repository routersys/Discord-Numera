using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteBankReadRepository : IBankReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteBankReadRepository(SqliteConnection connection) => this.connection = connection;

    public IReadOnlyList<BankSuggestion> ListSuggestible(
        EconomyScopeId economyScopeId,
        IReadOnlyList<BankStatus> selectableStatuses,
        ulong? operatorDiscordUserId,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(selectableStatuses);

        if (selectableStatuses.Count == 0 || limit <= 0)
        {
            return [];
        }

        StringBuilder sql = new("""
            SELECT b.institution_code, b.name, b.status
            FROM banks AS b
            """);

        if (operatorDiscordUserId is not null)
        {
            sql.AppendLine().Append("""
                INNER JOIN bank_operator_grants AS g
                    ON g.bank_id = b.bank_id AND g.status = 'ACTIVE' AND g.discord_user_id = $operator
                """);
        }

        sql.AppendLine().Append("WHERE b.economy_scope_id = $scope AND b.status IN (");

        for (int index = 0; index < selectableStatuses.Count; index++)
        {
            if (index > 0)
            {
                sql.Append(", ");
            }

            sql.Append('$').Append("status").Append(index.ToString(CultureInfo.InvariantCulture));
        }

        sql.AppendLine(")").Append("ORDER BY b.name ASC, b.institution_code ASC LIMIT $limit;");

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$limit", limit);

        for (int index = 0; index < selectableStatuses.Count; index++)
        {
            command.Parameters.AddWithValue(
                $"$status{index.ToString(CultureInfo.InvariantCulture)}",
                selectableStatuses[index].ToToken());
        }

        if (operatorDiscordUserId is { } discordUserId)
        {
            command.Parameters.AddWithValue(
                "$operator", discordUserId.ToString(CultureInfo.InvariantCulture));
        }

        List<BankSuggestion> suggestions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            suggestions.Add(new BankSuggestion(
                reader.GetString(0),
                reader.GetString(1),
                BankCatalog.ParseStatusToken(reader.GetString(2))));
        }

        return suggestions;
    }
}

public sealed class SqliteCurrencyReadRepository : ICurrencyReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteCurrencyReadRepository(SqliteConnection connection) => this.connection = connection;

    public IReadOnlyList<CurrencySuggestion> ListSuggestible(EconomyScopeId economyScopeId, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.code, m.name
            FROM currencies AS c
            INNER JOIN currency_metadata_versions AS m ON m.currency_id = c.currency_id
            WHERE c.economy_scope_id = $scope
              AND c.status IN ('ACTIVE', 'SUSPENDED', 'RETIRING')
              AND m.effective_to IS NULL
            ORDER BY m.name ASC, m.code ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$limit", limit);

        List<CurrencySuggestion> suggestions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            suggestions.Add(new CurrencySuggestion(reader.GetString(0), reader.GetString(1)));
        }

        return suggestions;
    }
}

public sealed class SqliteTransferPreparationReadRepository : ITransferPreparationReadRepository
{
    private readonly SqliteConnection connection;

    internal SqliteTransferPreparationReadRepository(SqliteConnection connection) =>
        this.connection = connection;

    public TransferSourceView? FindOwnedSource(
        CustomerAccountId payerCustomerAccountId,
        DepositAccountId sourceDepositAccountId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT deposit_account_id, currency_id FROM deposit_accounts
            WHERE deposit_account_id = $account AND customer_account_id = $customer;
            """;
        command.Parameters.AddWithValue("$account", sourceDepositAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$customer", payerCustomerAccountId.Value.ToByteArray());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new TransferSourceView(
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))))
            : null;
    }

    public CustomerAccountId? FindCustomerByDiscordUser(EconomyScopeId economyScopeId, string discordUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.customer_account_id
            FROM discord_identity_links AS l
            INNER JOIN customer_accounts AS c ON c.customer_account_id = l.customer_account_id
            WHERE l.discord_user_id = $user AND l.status = 'ACTIVE' AND c.status <> 'CLOSED';
            """;
        command.Parameters.AddWithValue("$user", discordUserId);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0)))
            : null;
    }

    public IReadOnlyList<TransferDestinationCandidate> ListPublicReceivingAccounts(
        CustomerAccountId beneficiaryCustomerAccountId,
        CurrencyId currencyId,
        DepositAccountId excludedDepositAccountId,
        int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.deposit_account_id, b.institution_code, r.branch_code, d.account_number, b.name
            FROM deposit_accounts AS d
            INNER JOIN banks AS b ON b.bank_id = d.bank_id
            INNER JOIN branches AS r ON r.branch_id = d.branch_id
            WHERE d.customer_account_id = $customer
              AND d.currency_id = $currency
              AND d.deposit_account_id <> $excluded
              AND d.public_receiving_enabled = 1
              AND d.status IN ('ACTIVE', 'RESTRICTED', 'DORMANT')
              AND b.status IN ('OPERATING', 'RESTRICTED')
            ORDER BY b.name ASC, d.account_number ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$customer", beneficiaryCustomerAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$currency", currencyId.Value.ToByteArray());
        command.Parameters.AddWithValue("$excluded", excludedDepositAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$limit", limit);

        List<TransferDestinationCandidate> candidates = [];

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            candidates.Add(new TransferDestinationCandidate(
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return candidates;
    }
}

public sealed class SqliteBankingReadContext : IBankingReadContext
{
    internal SqliteBankingReadContext(SqliteConnection connection)
    {
        Banks = new SqliteBankReadRepository(connection);
        Currencies = new SqliteCurrencyReadRepository(connection);
        TransferPreparation = new SqliteTransferPreparationReadRepository(connection);
    }

    public IBankReadRepository Banks { get; }

    public ICurrencyReadRepository Currencies { get; }

    public ITransferPreparationReadRepository TransferPreparation { get; }
}

public sealed class SqliteBankingReadGateway : IBankingReadGateway
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteBankingReadGateway(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        this.connectionFactory = connectionFactory;
    }

    public TResult Execute<TResult>(Func<IBankingReadContext, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        return query(new SqliteBankingReadContext(connection));
    }
}
