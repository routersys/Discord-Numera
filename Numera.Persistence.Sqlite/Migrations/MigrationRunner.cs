using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite.Migrations;

public sealed class MigrationRunner
{
    private const string CreateHistoryTable = """
        CREATE TABLE IF NOT EXISTS schema_migrations(
            version INTEGER NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            checksum TEXT NOT NULL,
            applied_at INTEGER NOT NULL
        ) STRICT;
        """;

    private readonly IReadOnlyList<SqlMigration> migrations;

    public MigrationRunner(IReadOnlyList<SqlMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        EnsureContiguousSequence(migrations);
        this.migrations = migrations;
    }

    public MigrationOutcome Apply(SqliteConnection connection, long appliedAtUnixMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(connection);

        SqliteConnectionFactory.Execute(connection, CreateHistoryTable);

        IReadOnlyDictionary<int, AppliedMigration> applied = ReadHistory(connection);
        EnsureNoUnknownAppliedMigration(applied);

        int appliedCount = 0;

        foreach (SqlMigration migration in migrations)
        {
            if (applied.TryGetValue(migration.Version, out AppliedMigration record))
            {
                EnsureUnchanged(migration, record);
                continue;
            }

            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            ExecuteScript(connection, transaction, migration.Script);
            RecordApplication(connection, transaction, migration, appliedAtUnixMilliseconds);
            transaction.Commit();
            appliedCount++;
        }

        return new MigrationOutcome(migrations.Count, appliedCount, CurrentVersion(migrations));
    }

    private static int CurrentVersion(IReadOnlyList<SqlMigration> migrations) =>
        migrations.Count == 0 ? 0 : migrations[^1].Version;

    private static void EnsureContiguousSequence(IReadOnlyList<SqlMigration> migrations)
    {
        for (int index = 0; index < migrations.Count; index++)
        {
            if (migrations[index].Version != index + 1)
            {
                throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationSequenceInvalid);
            }
        }
    }

    private static void EnsureUnchanged(SqlMigration migration, AppliedMigration record)
    {
        if (!string.Equals(migration.Checksum, record.Checksum, StringComparison.Ordinal))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationChecksumMismatch);
        }

        if (!string.Equals(migration.Name, record.Name, StringComparison.Ordinal))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationNameMismatch);
        }
    }

    private void EnsureNoUnknownAppliedMigration(IReadOnlyDictionary<int, AppliedMigration> applied)
    {
        int highestKnown = CurrentVersion(migrations);

        foreach (int version in applied.Keys)
        {
            if (version > highestKnown)
            {
                throw PersistenceFailureException.Create(PersistenceFailureCode.MigrationMissing);
            }
        }
    }

    private static Dictionary<int, AppliedMigration> ReadHistory(SqliteConnection connection)
    {
        Dictionary<int, AppliedMigration> applied = [];

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, name, checksum FROM schema_migrations ORDER BY version;";
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            applied[(int)reader.GetInt64(0)] = new AppliedMigration(reader.GetString(1), reader.GetString(2));
        }

        return applied;
    }

    private static void ExecuteScript(SqliteConnection connection, SqliteTransaction transaction, string script)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = script;
        command.ExecuteNonQuery();
    }

    private static void RecordApplication(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqlMigration migration,
        long appliedAtUnixMilliseconds)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_migrations(version, name, checksum, applied_at)
            VALUES($version, $name, $checksum, $applied_at);
            """;
        command.Parameters.AddWithValue("$version", migration.Version);
        command.Parameters.AddWithValue("$name", migration.Name);
        command.Parameters.AddWithValue("$checksum", migration.Checksum);
        command.Parameters.AddWithValue("$applied_at", appliedAtUnixMilliseconds);
        command.ExecuteNonQuery();
    }

    private readonly record struct AppliedMigration(string Name, string Checksum);
}

public readonly record struct MigrationOutcome(int KnownCount, int AppliedCount, int CurrentVersion);
