using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite;

public sealed record RestoreResult(bool IsSuccess, string Detail, string RecoveryCopyPath)
{
    public static RestoreResult Failed(string detail) => new(false, detail, string.Empty);
}

public interface IDatabaseRestoreService
{
    RestoreResult Restore(string backupDatabasePath, long restoredAtUnixMilliseconds);
}

internal static class RestoreFailure
{
    internal const string BackupNotVerified = "BACKUP_NOT_VERIFIED";
    internal const string TempQuickCheckFailed = "TEMP_QUICK_CHECK_FAILED";
    internal const string TempForeignKeyCheckFailed = "TEMP_FOREIGN_KEY_CHECK_FAILED";
    internal const string TempIntegrityCheckFailed = "TEMP_INTEGRITY_CHECK_FAILED";
    internal const string RestoredQuickCheckFailed = "RESTORED_QUICK_CHECK_FAILED";
    internal const string RestoredForeignKeyCheckFailed = "RESTORED_FOREIGN_KEY_CHECK_FAILED";
    internal const string MaintenanceFailed = "MAINTENANCE_FAILED";
}

public sealed class SqliteDatabaseRestoreService : IDatabaseRestoreService
{
    public const string TempSuffix = ".restore.partial";
    public const string RecoveryCopySuffix = ".recovery";
    public const string WriteAheadLogSuffix = "-wal";
    public const string SharedMemorySuffix = "-shm";

    private readonly SqliteDatabaseOptions options;
    private readonly IDatabaseBackupService backups;
    private readonly MigrationRunner migrations;

    public SqliteDatabaseRestoreService(
        SqliteDatabaseOptions options,
        IDatabaseBackupService backups,
        MigrationRunner migrations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(migrations);

        this.options = options;
        this.backups = backups;
        this.migrations = migrations;
    }

    public RestoreResult Restore(string backupDatabasePath, long restoredAtUnixMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(backupDatabasePath);

        if (!backups.VerifyAt(backupDatabasePath).IsSuccess)
        {
            return RestoreResult.Failed(RestoreFailure.BackupNotVerified);
        }

        string current = options.FullPath;
        string temp = current + TempSuffix;
        string recoveryCopy = current + RecoveryCopySuffix;

        Discard(temp);

        try
        {
            if (PrepareTemp(backupDatabasePath, temp, restoredAtUnixMilliseconds) is { } prepared)
            {
                Discard(temp);

                return RestoreResult.Failed(prepared);
            }
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            Discard(temp);

            return RestoreResult.Failed(exception.GetType().Name);
        }

        SqliteConnection.ClearAllPools();

        try
        {
            Discard(recoveryCopy);
            File.Move(current, recoveryCopy, overwrite: false);
            Discard(current + WriteAheadLogSuffix);
            Discard(current + SharedMemorySuffix);
            File.Move(temp, current, overwrite: false);
        }
        catch (IOException exception)
        {
            Rollback(current, recoveryCopy);
            Discard(temp);

            return RestoreResult.Failed(exception.GetType().Name);
        }

        if (Inspect(current) is { } restored)
        {
            Rollback(current, recoveryCopy);

            return RestoreResult.Failed(restored);
        }

        return new RestoreResult(true, string.Empty, recoveryCopy);
    }

    private string? PrepareTemp(string backupDatabasePath, string temp, long restoredAtUnixMilliseconds)
    {
        using (SqliteConnection source = Open(backupDatabasePath))
        using (SqliteConnection destination = Open(temp))
        {
            source.BackupDatabase(destination);
        }

        using (SqliteConnection connection = Open(temp))
        {
            migrations.Apply(connection, restoredAtUnixMilliseconds);

            if (!Quick(connection))
            {
                return RestoreFailure.TempQuickCheckFailed;
            }

            if (!ForeignKeys(connection))
            {
                return RestoreFailure.TempForeignKeyCheckFailed;
            }

            if (!Integrity(connection))
            {
                return RestoreFailure.TempIntegrityCheckFailed;
            }
        }

        return null;
    }

    private static string? Inspect(string databasePath)
    {
        try
        {
            using SqliteConnection connection = Open(databasePath);

            if (!Quick(connection))
            {
                return RestoreFailure.RestoredQuickCheckFailed;
            }

            return ForeignKeys(connection) ? null : RestoreFailure.RestoredForeignKeyCheckFailed;
        }
        catch (SqliteException exception)
        {
            return exception.GetType().Name;
        }
    }

    private static void Rollback(string current, string recoveryCopy)
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Discard(current);

            if (File.Exists(recoveryCopy))
            {
                File.Move(recoveryCopy, current, overwrite: false);
            }
        }
        catch (IOException)
        {
        }
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        connection.Open();

        return connection;
    }

    private static bool Quick(SqliteConnection connection) => IsOk(
        SqliteConnectionFactory.ReadScalarText(connection, SqliteDatabaseIntegrityProbe.QuickCheckStatement));

    private static bool Integrity(SqliteConnection connection) => IsOk(
        SqliteConnectionFactory.ReadScalarText(connection, SqliteDatabaseIntegrityProbe.IntegrityCheckStatement));

    private static bool ForeignKeys(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SqliteDatabaseIntegrityProbe.ForeignKeyCheckStatement;

        using SqliteDataReader reader = command.ExecuteReader();

        return !reader.Read();
    }

    private static bool IsOk(string value) =>
        string.Equals(value, SqlitePragmaGuard.IntegrityOk, StringComparison.Ordinal);

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
