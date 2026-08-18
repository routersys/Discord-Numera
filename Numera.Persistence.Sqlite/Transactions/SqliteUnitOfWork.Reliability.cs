using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IOperationResultRepository? operationResults;

    public IOperationResultRepository OperationResults =>
        operationResults ??= new SqliteOperationResultRepository(unitOfWork);
}
