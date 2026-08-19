using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite;

public sealed class SqliteDatabaseInitializer
{
    private readonly SqliteDatabaseOptions options;
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly MigrationRunner migrationRunner;

    public SqliteDatabaseInitializer(
        SqliteDatabaseOptions options,
        SqliteConnectionFactory connectionFactory,
        MigrationRunner migrationRunner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(migrationRunner);

        this.options = options;
        this.connectionFactory = connectionFactory;
        this.migrationRunner = migrationRunner;
    }

    public void EnsureDirectory()
    {
        string? directory = options.DirectoryPath;

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public MigrationOutcome Initialize(long startedAtUnixMilliseconds)
    {
        EnsureDirectory();

        using SqliteConnection connection = connectionFactory.OpenBootstrapConnection();

        SqlitePragmaGuard.ApplyDatabaseWide(connection);
        MigrationOutcome outcome = migrationRunner.Apply(connection, startedAtUnixMilliseconds);
        SqlitePragmaGuard.EnsureIntegrity(connection);

        return outcome;
    }

    public bool IsFreshDatabase =>
        !File.Exists(options.FullPath) || new FileInfo(options.FullPath).Length == 0;

    public void EnsureWriteAheadLogging()
    {
        EnsureDirectory();

        using SqliteConnection bootstrap = connectionFactory.OpenBootstrapConnection();

        if (SqlitePragmaGuard.IsWriteAheadLogging(bootstrap))
        {
            return;
        }

        SqlitePragmaGuard.ApplyDatabaseWide(bootstrap);
    }

    public void VerifyRuntimeReadiness()
    {
        EnsureWriteAheadLogging();

        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        SqlitePragmaGuard.EnsureIntegrity(connection);
    }
}
