using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed class SqliteUnitOfWork
{
    internal SqliteUnitOfWork(SqliteConnection connection, SqliteTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public SqliteConnection Connection { get; }

    public SqliteTransaction Transaction { get; }

    public SqliteCommand CreateCommand(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);

        SqliteCommand command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = commandText;
        return command;
    }
}

public enum WriteLane
{
    Foreground = 1,
    Background = 2,
}

public enum WriteOutcomeStatus
{
    Committed = 1,
    RejectedSystemBusy = 2,
    CancelledBeforeExecution = 3,
}

public readonly struct WriteOutcome<TResult>
{
    private readonly TResult? value;

    private WriteOutcome(WriteOutcomeStatus status, TResult? value)
    {
        Status = status;
        this.value = value;
    }

    public WriteOutcomeStatus Status { get; }

    public bool IsCommitted => Status == WriteOutcomeStatus.Committed;

    public TResult Value => IsCommitted
        ? value!
        : throw PersistenceFailureException.Create(PersistenceFailureCode.WriteOutcomeNotCommitted);

    public static WriteOutcome<TResult> Committed(TResult value) =>
        new(WriteOutcomeStatus.Committed, value);

    public static WriteOutcome<TResult> RejectedSystemBusy() =>
        new(WriteOutcomeStatus.RejectedSystemBusy, default);

    public static WriteOutcome<TResult> CancelledBeforeExecution() =>
        new(WriteOutcomeStatus.CancelledBeforeExecution, default);
}
