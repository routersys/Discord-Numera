using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

public sealed class SqliteConnectionFactory
{
    private readonly SqliteDatabaseOptions options;
    private readonly string connectionString;

    public SqliteConnectionFactory(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.FullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = options.BusyTimeoutSeconds,
        }.ToString();
    }

    public SqliteConnection OpenRuntimeConnection()
    {
        SqliteConnection connection = OpenBare();

        try
        {
            SqlitePragmaGuard.ApplyConnectionLocal(connection, options);
            SqlitePragmaGuard.EnsureWriteAheadLogging(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public SqliteConnection OpenBootstrapConnection()
    {
        SqliteConnection connection = OpenBare();

        try
        {
            SqlitePragmaGuard.ApplyConnectionLocal(connection, options);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private SqliteConnection OpenBare()
    {
        SqliteConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    internal static string ReadScalarText(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();

        return value switch
        {
            null or DBNull => string.Empty,
            string text => text,
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
