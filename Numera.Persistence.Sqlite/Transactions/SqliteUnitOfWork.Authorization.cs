using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IAuthorizationDecisionRepository? authorizationDecisions;

    public IAuthorizationDecisionRepository AuthorizationDecisions =>
        authorizationDecisions ??= new SqliteAuthorizationDecisionRepository(unitOfWork);
}
