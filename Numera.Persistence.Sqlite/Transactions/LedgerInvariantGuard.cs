using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite.Transactions;

internal static class LedgerInvariantGuard
{
    private const string UnbalancedTransactionQuery = """
        SELECT COUNT(*)
        FROM (
            SELECT accounting_transaction_id
            FROM journal_entries
            GROUP BY accounting_transaction_id
            HAVING SUM(CASE WHEN side = 'DEBIT' THEN amount_minor ELSE 0 END)
                <> SUM(CASE WHEN side = 'CREDIT' THEN amount_minor ELSE 0 END)
        );
        """;

    private const string NegativeProjectionQuery = """
        SELECT COUNT(*)
        FROM ledger_balance_projections
        WHERE held_minor < 0;
        """;

    private const string TableExistsQuery = """
        SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;
        """;

    internal static void EnsureSatisfied(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (TableExists(unitOfWork, "journal_entries") && Count(unitOfWork, UnbalancedTransactionQuery) > 0)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.LedgerUnbalanced);
        }

        if (TableExists(unitOfWork, "ledger_balance_projections") &&
            Count(unitOfWork, NegativeProjectionQuery) > 0)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.LedgerProjectionInvalid);
        }
    }

    private static bool TableExists(SqliteUnitOfWork unitOfWork, string table)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(TableExistsQuery);
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static long Count(SqliteUnitOfWork unitOfWork, string query)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(query);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
