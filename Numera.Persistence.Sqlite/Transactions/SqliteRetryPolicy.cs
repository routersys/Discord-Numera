using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed class SqliteRetryPolicy
{
    public const int DefaultMaximumAttempts = 3;
    public const int DefaultBaseDelayMilliseconds = 20;
    public const int DefaultJitterMilliseconds = 20;

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    private readonly Func<int> jitterMillisecondsProvider;

    public SqliteRetryPolicy(
        int maximumAttempts = DefaultMaximumAttempts,
        int baseDelayMilliseconds = DefaultBaseDelayMilliseconds,
        Func<int>? jitterMillisecondsProvider = null)
    {
        if (maximumAttempts < 1 || baseDelayMilliseconds < 0)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.RetryPolicyInvalid);
        }

        MaximumAttempts = maximumAttempts;
        BaseDelayMilliseconds = baseDelayMilliseconds;
        this.jitterMillisecondsProvider =
            jitterMillisecondsProvider ?? (static () => Random.Shared.Next(0, DefaultJitterMilliseconds + 1));
    }

    public int MaximumAttempts { get; }

    public int BaseDelayMilliseconds { get; }

    public static bool IsTransientContention(Exception exception) =>
        exception is SqliteException sqlite && sqlite.SqliteErrorCode is SqliteBusy or SqliteLocked;

    public TimeSpan DelayForAttempt(int attempt)
    {
        if (attempt < 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.RetryPolicyInvalid);
        }

        int scaled = BaseDelayMilliseconds * (1 << (attempt - 1));
        return TimeSpan.FromMilliseconds(scaled + jitterMillisecondsProvider());
    }
}
