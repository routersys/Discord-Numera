using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private ICurrencyRepository? currencies;

    public ICurrencyRepository Currencies => currencies ??= new SqliteCurrencyRepository(unitOfWork);
}
