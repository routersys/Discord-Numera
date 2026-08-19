using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record OperationResultRecord(
    OperationResultId Id,
    BusinessOperationId BusinessOperationId,
    string ResultKind,
    string ResultJson,
    UtcTimestamp CreatedAt);

public interface IOperationResultRepository
{
    void Add(OperationResultRecord result);

    OperationResultRecord? Find(BusinessOperationId businessOperationId);
}

public interface IReconciliationRepository
{
    long CountUnresolvedIssues(EconomyScopeId economyScopeId);
}

public partial interface IBankingUnitOfWork
{
    IOperationResultRepository OperationResults { get; }

    IReconciliationRepository Reconciliation { get; }
}
