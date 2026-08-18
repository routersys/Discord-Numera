using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IFeeAdministrationRepository? feeAdministration;

    public IFeeAdministrationRepository FeeAdministration =>
        feeAdministration ??= new SqliteFeeAdministrationRepository(unitOfWork);
}
