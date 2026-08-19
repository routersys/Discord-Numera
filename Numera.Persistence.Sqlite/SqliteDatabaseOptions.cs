namespace Numera.Persistence.Sqlite;

public sealed class SqliteDatabaseOptions
{
    public const string DefaultPath = "data/economy.db";
    public const int DefaultBusyTimeoutSeconds = 5;
    public const int MinimumBusyTimeoutSeconds = 1;
    public const int MaximumBusyTimeoutSeconds = 60;
    public const string LockFileSuffix = ".lock";
    public const string BackupDirectoryName = "backups";

    private SqliteDatabaseOptions(string path, int busyTimeoutSeconds, string? secondaryBackupDirectory)
    {
        Path = path;
        BusyTimeoutSeconds = busyTimeoutSeconds;
        SecondaryBackupDirectory = secondaryBackupDirectory;
    }

    public string Path { get; }

    public string? SecondaryBackupDirectory { get; }

    public bool HasSecondaryBackupTarget => SecondaryBackupDirectory is not null;

    public int BusyTimeoutSeconds { get; }

    public int BusyTimeoutMilliseconds => BusyTimeoutSeconds * 1_000;

    public string FullPath => System.IO.Path.GetFullPath(Path);

    public string LockFilePath => FullPath + LockFileSuffix;

    public string? DirectoryPath => System.IO.Path.GetDirectoryName(FullPath);

    public string BackupDirectoryPath =>
        System.IO.Path.Combine(DirectoryPath ?? ".", BackupDirectoryName);

    public static SqliteDatabaseOptions Create(
        string path,
        int busyTimeoutSeconds,
        string? secondaryBackupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.DatabasePathInvalid);
        }

        if (path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.DatabasePathInvalid);
        }

        if (busyTimeoutSeconds is < MinimumBusyTimeoutSeconds or > MaximumBusyTimeoutSeconds)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.BusyTimeoutInvalid);
        }

        string? secondary = Normalize(secondaryBackupDirectory);

        if (secondary is not null)
        {
            string primary = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".",
                    BackupDirectoryName));

            if (string.Equals(secondary, primary, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    secondary,
                    System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw PersistenceFailureException.Create(
                    PersistenceFailureCode.SecondaryBackupDirectoryInvalid);
            }
        }

        return new SqliteDatabaseOptions(path, busyTimeoutSeconds, secondary);
    }

    private static string? Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (candidate.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
        {
            throw PersistenceFailureException.Create(
                PersistenceFailureCode.SecondaryBackupDirectoryInvalid);
        }

        return System.IO.Path.GetFullPath(candidate);
    }

    public static SqliteDatabaseOptions CreateDefault() => Create(DefaultPath, DefaultBusyTimeoutSeconds);
}
