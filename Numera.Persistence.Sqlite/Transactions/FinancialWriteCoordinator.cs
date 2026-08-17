namespace Numera.Persistence.Sqlite.Transactions;

public sealed class FinancialWriteCoordinator
{
    private readonly SqliteWriteCoordinator inner;

    public FinancialWriteCoordinator(SqliteWriteCoordinator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    public Task<WriteOutcome<TResult>> ExecuteAsync<TResult>(
        Func<SqliteUnitOfWork, TResult> operation,
        CancellationToken cancellationToken) =>
        inner.ExecuteAsync(WriteLane.Foreground, WrapWithInvariantCheck(operation), cancellationToken);

    public Task<WriteOutcome<TResult>> ExecuteMaintenanceAsync<TResult>(
        Func<SqliteUnitOfWork, TResult> operation,
        CancellationToken cancellationToken) =>
        inner.ExecuteAsync(WriteLane.Background, WrapWithInvariantCheck(operation), cancellationToken);

    public Task<WriteOutcome<TResult>> ExecuteWithDecisionAsync<TResult>(
        Func<SqliteUnitOfWork, WriteDecision<TResult>> operation,
        CancellationToken cancellationToken) =>
        inner.ExecuteWithDecisionAsync(WriteLane.Foreground, WrapWithInvariantCheck(operation), cancellationToken);

    private static Func<SqliteUnitOfWork, TResult> WrapWithInvariantCheck<TResult>(
        Func<SqliteUnitOfWork, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return unitOfWork =>
        {
            TResult result = operation(unitOfWork);
            LedgerInvariantGuard.EnsureSatisfied(unitOfWork);
            return result;
        };
    }

    private static Func<SqliteUnitOfWork, WriteDecision<TResult>> WrapWithInvariantCheck<TResult>(
        Func<SqliteUnitOfWork, WriteDecision<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return unitOfWork =>
        {
            WriteDecision<TResult> decision = operation(unitOfWork);

            if (decision.ShouldCommit)
            {
                LedgerInvariantGuard.EnsureSatisfied(unitOfWork);
            }

            return decision;
        };
    }
}
