using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IBankAdministrationRepository? bankAdministration;

    public IBankAdministrationRepository BankAdministration =>
        bankAdministration ??= new SqliteBankAdministrationRepository(unitOfWork);
}
