namespace Numera.Persistence.Sqlite;

public sealed class SingleInstanceLock : IDisposable
{
    private FileStream? stream;

    private SingleInstanceLock(FileStream stream) => this.stream = stream;

    public static SingleInstanceLock Acquire(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            FileStream stream = new(
                options.LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);

            return new SingleInstanceLock(stream);
        }
        catch (IOException exception)
        {
            throw PersistenceFailureException.Create(
                PersistenceFailureCode.SingleInstanceLockUnavailable, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw PersistenceFailureException.Create(
                PersistenceFailureCode.SingleInstanceLockUnavailable, exception);
        }
    }

    public void Dispose()
    {
        stream?.Dispose();
        stream = null;
    }
}
