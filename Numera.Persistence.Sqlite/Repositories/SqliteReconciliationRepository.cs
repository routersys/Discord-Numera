using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteReconciliationRepository : IReconciliationRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteReconciliationRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public long CountUnresolvedIssues(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(*) FROM reconciliation_issues AS i
            JOIN reconciliation_runs AS r ON r.reconciliation_run_id = i.reconciliation_run_id
            WHERE r.scope_id = $scope AND i.resolved_at IS NULL
              AND i.severity IN ('ERROR','CRITICAL');
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return (long)(command.ExecuteScalar() ?? 0L);
    }
}
