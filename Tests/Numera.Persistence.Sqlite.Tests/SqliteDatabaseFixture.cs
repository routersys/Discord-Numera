using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

internal sealed class SqliteDatabaseFixture : IDisposable
{
    private readonly string root;

    private SqliteDatabaseFixture(string root, SqliteDatabaseOptions options)
    {
        this.root = root;
        Options = options;
        ConnectionFactory = new SqliteConnectionFactory(options);
    }

    public SqliteDatabaseOptions Options { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public static SqliteDatabaseFixture Create(int busyTimeoutSeconds = SqliteDatabaseOptions.DefaultBusyTimeoutSeconds)
    {
        string root = Path.Combine(Path.GetTempPath(), "numera-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
            Path.Combine(root, "data", "economy.db"),
            busyTimeoutSeconds);

        return new SqliteDatabaseFixture(root, options);
    }

    public SqliteDatabaseInitializer CreateInitializer(params SqlMigration[] migrations) =>
        new(Options, ConnectionFactory, new MigrationRunner(migrations));

    public MigrationOutcome Initialize(params SqlMigration[] migrations) =>
        CreateInitializer(migrations).Initialize(1_776_000_000_000);

    public long CountRows(string table)
    {
        using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public bool TableExists(string table)
    {
        using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return (long)(command.ExecuteScalar() ?? 0L) > 0;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
