using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

public enum BackupKind
{
    Automatic = 1,
    Manual = 2,
    PreMigration = 3,
}

internal sealed record BackupManifest(
    [property: JsonPropertyName("format_version")] int FormatVersion,
    [property: JsonPropertyName("backup_id")] string BackupId,
    [property: JsonPropertyName("backup_kind")] string BackupKind,
    [property: JsonPropertyName("created_at_utc")] string CreatedAtUtc,
    [property: JsonPropertyName("source_database_schema_version")] int SourceDatabaseSchemaVersion,
    [property: JsonPropertyName("application_version")] string ApplicationVersion,
    [property: JsonPropertyName("database_length_bytes")] long DatabaseLengthBytes,
    [property: JsonPropertyName("database_sha256")] string DatabaseSha256,
    [property: JsonPropertyName("quick_check")] string QuickCheck,
    [property: JsonPropertyName("foreign_key_check_count")] int ForeignKeyCheckCount,
    [property: JsonPropertyName("integrity_check")] string IntegrityCheck,
    [property: JsonPropertyName("verified_at_utc")] string VerifiedAtUtc);

[JsonSerializable(typeof(BackupManifest))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class BackupManifestContext : JsonSerializerContext;

public sealed record BackupCreationResult(bool IsSuccess, string DatabasePath, string ManifestPath, string Detail)
{
    public static BackupCreationResult Failed(string detail) =>
        new(false, string.Empty, string.Empty, detail);
}

public sealed record BackupVerificationResult(bool IsSuccess, string Detail)
{
    public static BackupVerificationResult Passed { get; } = new(true, string.Empty);

    public static BackupVerificationResult Failed(string detail) => new(false, detail);
}

internal sealed record BackupEntry(string DatabasePath, string ManifestPath, BackupManifest Manifest);

public sealed record BackupSummary(
    int AutomaticCount,
    int ManualCount,
    int PreMigrationCount,
    long TotalBytes,
    string OldestCreatedAtUtc,
    string NewestCreatedAtUtc)
{
    public static BackupSummary Empty { get; } = new(0, 0, 0, 0L, string.Empty, string.Empty);

    public int Count => AutomaticCount + ManualCount + PreMigrationCount;
}

public interface IDatabaseBackupService
{
    BackupCreationResult Create(BackupKind kind);

    BackupVerificationResult VerifyAt(string databasePath);

    BackupSummary Summarize();

    string? FindLatestVerified();

    int PruneAutomatic();
}

internal static class BackupFailure
{
    internal const string ManifestMissing = "MANIFEST_MISSING";
    internal const string FormatVersionUnsupported = "FORMAT_VERSION_UNSUPPORTED";
    internal const string BackupIdMismatch = "BACKUP_ID_MISMATCH";
    internal const string LengthMismatch = "LENGTH_MISMATCH";
    internal const string DigestMismatch = "DIGEST_MISMATCH";
    internal const string SchemaVersionNewer = "SCHEMA_VERSION_NEWER";
    internal const string QuickCheckFailed = "QUICK_CHECK_FAILED";
    internal const string ForeignKeyCheckFailed = "FOREIGN_KEY_CHECK_FAILED";
    internal const string IntegrityCheckFailed = "INTEGRITY_CHECK_FAILED";
    internal const string DatabaseMissing = "DATABASE_MISSING";
}

public sealed class SqliteDatabaseBackupService : IDatabaseBackupService
{
    public const int ManifestFormatVersion = 1;
    public const int AutomaticRetentionCount = 28;
    public const string DatabaseExtension = ".db";
    public const string ManifestExtension = ".manifest.json";
    public const string PartialExtension = ".partial";
    public const string FileNamePrefix = "economy-";
    public const string TimestampFormat = "yyyyMMddTHHmmssfffZ";

    private readonly SqliteDatabaseOptions options;
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly TimeProvider timeProvider;
    private readonly string applicationVersion;

    public SqliteDatabaseBackupService(
        SqliteDatabaseOptions options,
        SqliteConnectionFactory connectionFactory,
        TimeProvider timeProvider,
        string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(applicationVersion);

        this.options = options;
        this.connectionFactory = connectionFactory;
        this.timeProvider = timeProvider;
        this.applicationVersion = applicationVersion;
    }

    public BackupCreationResult Create(BackupKind kind)
    {
        Directory.CreateDirectory(options.BackupDirectoryPath);

        Guid backupId = Guid.CreateVersion7();
        DateTimeOffset createdAt = timeProvider.GetUtcNow();
        string stem = FileNamePrefix
            + createdAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)
            + "-" + backupId.ToString("N", CultureInfo.InvariantCulture);

        string databasePath = Path.Combine(options.BackupDirectoryPath, stem + DatabaseExtension);
        string manifestPath = Path.Combine(options.BackupDirectoryPath, stem + ManifestExtension);
        string databaseTemp = databasePath + PartialExtension;
        string manifestTemp = manifestPath + PartialExtension;

        try
        {
            CopyOnline(databaseTemp);

            DatabaseProbeResult quick;
            DatabaseProbeResult foreignKeys;
            DatabaseProbeResult integrity;
            int schemaVersion;

            using (SqliteConnection destination = OpenBackup(databaseTemp))
            {
                quick = Scalar(destination, SqliteDatabaseIntegrityProbe.QuickCheckStatement);
                foreignKeys = ForeignKeys(destination);
                integrity = Scalar(destination, SqliteDatabaseIntegrityProbe.IntegrityCheckStatement);
                schemaVersion = ReadSchemaVersion(destination);
            }

            SqliteConnection.ClearAllPools();

            if (!quick.IsOk)
            {
                return Discard(databaseTemp, BackupFailure.QuickCheckFailed);
            }

            if (!foreignKeys.IsOk)
            {
                return Discard(databaseTemp, BackupFailure.ForeignKeyCheckFailed);
            }

            if (!integrity.IsOk)
            {
                return Discard(databaseTemp, BackupFailure.IntegrityCheckFailed);
            }

            BackupManifest manifest = new(
                ManifestFormatVersion,
                backupId.ToString("N", CultureInfo.InvariantCulture),
                Token(kind),
                createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                schemaVersion,
                applicationVersion,
                new FileInfo(databaseTemp).Length,
                Digest(databaseTemp),
                SqlitePragmaGuard.IntegrityOk,
                0,
                SqlitePragmaGuard.IntegrityOk,
                timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

            File.WriteAllText(
                manifestTemp,
                JsonSerializer.Serialize(manifest, BackupManifestContext.Default.BackupManifest));

            File.Move(databaseTemp, databasePath, overwrite: false);
            File.Move(manifestTemp, manifestPath, overwrite: false);

            BackupVerificationResult verified = Verify(new BackupEntry(databasePath, manifestPath, manifest));

            if (!verified.IsSuccess)
            {
                Delete(databasePath);
                Delete(manifestPath);

                return BackupCreationResult.Failed(verified.Detail);
            }

            return new BackupCreationResult(true, databasePath, manifestPath, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or SqliteException or UnauthorizedAccessException)
        {
            Delete(databaseTemp);
            Delete(manifestTemp);

            return BackupCreationResult.Failed(exception.GetType().Name);
        }
    }

    internal BackupVerificationResult Verify(BackupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!File.Exists(entry.DatabasePath))
        {
            return BackupVerificationResult.Failed(BackupFailure.DatabaseMissing);
        }

        if (entry.Manifest.FormatVersion != ManifestFormatVersion)
        {
            return BackupVerificationResult.Failed(BackupFailure.FormatVersionUnsupported);
        }

        if (!Path.GetFileName(entry.DatabasePath).Contains(entry.Manifest.BackupId, StringComparison.Ordinal))
        {
            return BackupVerificationResult.Failed(BackupFailure.BackupIdMismatch);
        }

        if (new FileInfo(entry.DatabasePath).Length != entry.Manifest.DatabaseLengthBytes)
        {
            return BackupVerificationResult.Failed(BackupFailure.LengthMismatch);
        }

        if (!string.Equals(Digest(entry.DatabasePath), entry.Manifest.DatabaseSha256, StringComparison.Ordinal))
        {
            return BackupVerificationResult.Failed(BackupFailure.DigestMismatch);
        }

        using SqliteConnection connection = OpenBackup(entry.DatabasePath);

        if (ReadSchemaVersion(connection) > entry.Manifest.SourceDatabaseSchemaVersion)
        {
            return BackupVerificationResult.Failed(BackupFailure.SchemaVersionNewer);
        }

        if (!Scalar(connection, SqliteDatabaseIntegrityProbe.QuickCheckStatement).IsOk)
        {
            return BackupVerificationResult.Failed(BackupFailure.QuickCheckFailed);
        }

        if (!ForeignKeys(connection).IsOk)
        {
            return BackupVerificationResult.Failed(BackupFailure.ForeignKeyCheckFailed);
        }

        return Scalar(connection, SqliteDatabaseIntegrityProbe.IntegrityCheckStatement).IsOk
            ? BackupVerificationResult.Passed
            : BackupVerificationResult.Failed(BackupFailure.IntegrityCheckFailed);
    }

    internal IReadOnlyList<BackupEntry> List()
    {
        if (!Directory.Exists(options.BackupDirectoryPath))
        {
            return [];
        }

        List<BackupEntry> entries = [];

        foreach (string databasePath in Directory
            .EnumerateFiles(options.BackupDirectoryPath, FileNamePrefix + "*" + DatabaseExtension)
            .Order(StringComparer.Ordinal))
        {
            string manifestPath = databasePath[..^DatabaseExtension.Length] + ManifestExtension;

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            BackupManifest? manifest = Read(manifestPath);

            if (manifest is not null)
            {
                entries.Add(new BackupEntry(databasePath, manifestPath, manifest));
            }
        }

        return entries;
    }

    public BackupVerificationResult VerifyAt(string databasePath)
    {
        ArgumentNullException.ThrowIfNull(databasePath);

        string fullPath = Path.GetFullPath(databasePath);

        BackupEntry? entry = List().FirstOrDefault(candidate => string.Equals(
            Path.GetFullPath(candidate.DatabasePath), fullPath, StringComparison.OrdinalIgnoreCase));

        return entry is null ? BackupVerificationResult.Failed(BackupFailure.ManifestMissing) : Verify(entry);
    }

    public string? FindLatestVerified()
    {
        foreach (BackupEntry entry in List()
            .OrderByDescending(static candidate => candidate.Manifest.CreatedAtUtc, StringComparer.Ordinal)
            .ThenByDescending(static candidate => candidate.Manifest.BackupId, StringComparer.Ordinal))
        {
            if (Verify(entry).IsSuccess)
            {
                return entry.DatabasePath;
            }
        }

        return null;
    }

    public BackupSummary Summarize()
    {
        BackupEntry[] entries = [.. List()];

        if (entries.Length == 0)
        {
            return BackupSummary.Empty;
        }

        string[] created = [.. entries.Select(static entry => entry.Manifest.CreatedAtUtc).Order(StringComparer.Ordinal)];

        return new BackupSummary(
            entries.Count(static entry => IsKind(entry, BackupKind.Automatic)),
            entries.Count(static entry => IsKind(entry, BackupKind.Manual)),
            entries.Count(static entry => IsKind(entry, BackupKind.PreMigration)),
            entries.Sum(static entry => entry.Manifest.DatabaseLengthBytes),
            created[0],
            created[^1]);
    }

    private static bool IsKind(BackupEntry entry, BackupKind kind) =>
        string.Equals(entry.Manifest.BackupKind, Token(kind), StringComparison.Ordinal);

    public int PruneAutomatic()
    {
        BackupEntry[] automatic =
        [
            .. List()
                .Where(static entry => string.Equals(
                    entry.Manifest.BackupKind, Token(BackupKind.Automatic), StringComparison.Ordinal))
                .OrderBy(static entry => entry.Manifest.CreatedAtUtc, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Manifest.BackupId, StringComparer.Ordinal),
        ];

        int removed = 0;

        for (int index = 0; index < automatic.Length - AutomaticRetentionCount; index++)
        {
            Delete(automatic[index].DatabasePath);
            Delete(automatic[index].ManifestPath);
            removed++;
        }

        return removed;
    }

    internal static string Token(BackupKind kind) => kind switch
    {
        BackupKind.Manual => "MANUAL",
        BackupKind.PreMigration => "PRE_MIGRATION",
        _ => "AUTOMATIC",
    };

    private static BackupManifest? Read(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath), BackupManifestContext.Default.BackupManifest);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private void CopyOnline(string destinationPath)
    {
        using SqliteConnection source = connectionFactory.OpenRuntimeConnection();
        using SqliteConnection destination = OpenBackup(destinationPath);

        source.BackupDatabase(destination);
    }

    private static SqliteConnection OpenBackup(string path)
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

    private static DatabaseProbeResult Scalar(SqliteConnection connection, string statement)
    {
        string result = SqliteConnectionFactory.ReadScalarText(connection, statement);

        return string.Equals(result, SqlitePragmaGuard.IntegrityOk, StringComparison.Ordinal)
            ? DatabaseProbeResult.Ok
            : DatabaseProbeResult.Failed(result);
    }

    private static DatabaseProbeResult ForeignKeys(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SqliteDatabaseIntegrityProbe.ForeignKeyCheckStatement;

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? DatabaseProbeResult.Failed(BackupFailure.ForeignKeyCheckFailed)
            : DatabaseProbeResult.Ok;
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(version), 0) FROM schema_migrations;
            """;

        try
        {
            return command.ExecuteScalar() is long version ? (int)version : 0;
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static string Digest(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static BackupCreationResult Discard(string databaseTemp, string detail)
    {
        Delete(databaseTemp);

        return BackupCreationResult.Failed(detail);
    }

    private static void Delete(string path)
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
