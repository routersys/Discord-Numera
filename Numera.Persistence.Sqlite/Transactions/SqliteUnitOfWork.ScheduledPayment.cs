using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IPaymentManagementRepository? paymentManagement;

    public IPaymentManagementRepository PaymentManagement =>
        paymentManagement ??= new SqlitePaymentManagementRepository(unitOfWork);
}
