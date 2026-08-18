using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IDebitCardAuthorizationRepository? debitCardAuthorizations;

    public IDebitCardAuthorizationRepository DebitCardAuthorizations =>
        debitCardAuthorizations ??= new SqliteDebitCardAuthorizationRepository(unitOfWork);
}
