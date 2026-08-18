using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IGovernanceRepository? governance;

    public IGovernanceRepository Governance =>
        governance ??= new SqliteGovernanceRepository(unitOfWork);
}
