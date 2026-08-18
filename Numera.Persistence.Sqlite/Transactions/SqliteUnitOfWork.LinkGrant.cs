using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IAccountLinkGrantRepository? accountLinkGrants;

    public IAccountLinkGrantRepository AccountLinkGrants =>
        accountLinkGrants ??= new SqliteAccountLinkGrantRepository(unitOfWork);
}
