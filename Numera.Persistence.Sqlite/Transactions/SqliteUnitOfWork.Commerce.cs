using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private ICommerceRepository? commerce;

    public ICommerceRepository Commerce =>
        commerce ??= new SqliteCommerceRepository(unitOfWork);
}
