using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

internal static class SqlitePragmaGuard
{
    internal const string WriteAheadLoggingMode = "wal";
    internal const string SynchronousFull = "2";
    internal const string ForeignKeysEnabled = "1";
    internal const int WalAutoCheckpointPages = 1_000;
    internal const string IntegrityOk = "ok";

    internal static void ApplyConnectionLocal(SqliteConnection connection, SqliteDatabaseOptions options)
    {
        SqliteConnectionFactory.Execute(connection, "PRAGMA synchronous = FULL;");
        SqliteConnectionFactory.Execute(connection, "PRAGMA foreign_keys = ON;");
        SqliteConnectionFactory.Execute(
            connection,
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {options.BusyTimeoutMilliseconds};"));

        Verify(connection, "PRAGMA synchronous;", SynchronousFull);
        Verify(connection, "PRAGMA foreign_keys;", ForeignKeysEnabled);
        Verify(
            connection,
            "PRAGMA busy_timeout;",
            options.BusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
    }

    internal static void ApplyDatabaseWide(SqliteConnection connection)
    {
        SqliteConnectionFactory.Execute(connection, "PRAGMA journal_mode = WAL;");
        SqliteConnectionFactory.Execute(
            connection,
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA wal_autocheckpoint = {WalAutoCheckpointPages};"));

        EnsureWriteAheadLogging(connection);
        Verify(
            connection,
            "PRAGMA wal_autocheckpoint;",
            WalAutoCheckpointPages.ToString(CultureInfo.InvariantCulture));
    }

    internal static void EnsureWriteAheadLogging(SqliteConnection connection)
    {
        string mode = SqliteConnectionFactory.ReadScalarText(connection, "PRAGMA journal_mode;");

        if (!string.Equals(mode, WriteAheadLoggingMode, StringComparison.OrdinalIgnoreCase))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.JournalModeNotWal);
        }
    }

    internal static void EnsureIntegrity(SqliteConnection connection)
    {
        string result = SqliteConnectionFactory.ReadScalarText(connection, "PRAGMA quick_check;");

        if (!string.Equals(result, IntegrityOk, StringComparison.Ordinal))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.IntegrityCheckFailed);
        }
    }

    private static void Verify(SqliteConnection connection, string query, string expected)
    {
        string actual = SqliteConnectionFactory.ReadScalarText(connection, query);

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.PragmaVerificationFailed);
        }
    }
}
