using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IBankOperatorGrantRepository? bankOperatorGrants;

    public IBankOperatorGrantRepository BankOperatorGrants =>
        bankOperatorGrants ??= new SqliteBankOperatorGrantRepository(unitOfWork);
}
