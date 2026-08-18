using Numera.Application.Abstractions;
using Numera.Persistence.Sqlite.Repositories;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed partial class SqliteBankingUnitOfWork
{
    private IBankCardRepository? bankCards;

    public IBankCardRepository BankCards =>
        bankCards ??= new SqliteBankCardRepository(unitOfWork);
}
