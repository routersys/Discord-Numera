namespace Numera.Persistence.Sqlite;

public sealed class SqliteDatabaseOptions
{
    public const string DefaultPath = "data/economy.db";
    public const int DefaultBusyTimeoutSeconds = 5;
    public const int MinimumBusyTimeoutSeconds = 1;
    public const int MaximumBusyTimeoutSeconds = 60;
    public const string LockFileSuffix = ".lock";
    public const string BackupDirectoryName = "backups";

    private SqliteDatabaseOptions(string path, int busyTimeoutSeconds)
    {
        Path = path;
        BusyTimeoutSeconds = busyTimeoutSeconds;
    }

    public string Path { get; }

    public int BusyTimeoutSeconds { get; }

    public int BusyTimeoutMilliseconds => BusyTimeoutSeconds * 1_000;

    public string FullPath => System.IO.Path.GetFullPath(Path);

    public string LockFilePath => FullPath + LockFileSuffix;

    public string? DirectoryPath => System.IO.Path.GetDirectoryName(FullPath);

    public string BackupDirectoryPath =>
        System.IO.Path.Combine(DirectoryPath ?? ".", BackupDirectoryName);

    public static SqliteDatabaseOptions Create(string path, int busyTimeoutSeconds)
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

        return new SqliteDatabaseOptions(path, busyTimeoutSeconds);
    }

    public static SqliteDatabaseOptions CreateDefault() => Create(DefaultPath, DefaultBusyTimeoutSeconds);
}
