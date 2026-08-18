using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IPrudentialPolicyRepository? prudentialPolicies;

    public IPrudentialPolicyRepository PrudentialPolicies =>
        prudentialPolicies ??= new SqlitePrudentialPolicyRepository(unitOfWork);
}
