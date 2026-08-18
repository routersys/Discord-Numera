using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IFxRepository? fx;

    public IFxRepository Fx => fx ??= new SqliteFxRepository(unitOfWork);
}
