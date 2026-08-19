using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IReconciliationRepository? reconciliation;

    public IReconciliationRepository Reconciliation =>
        reconciliation ??= new SqliteReconciliationRepository(unitOfWork);
}
