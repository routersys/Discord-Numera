using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private ICashRepository? cash;

    public ICashRepository Cash => cash ??= new SqliteCashRepository(unitOfWork);
}
