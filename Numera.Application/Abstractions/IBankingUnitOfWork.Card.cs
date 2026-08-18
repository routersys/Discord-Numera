using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IBankCardRepository
{
    void Add(BankCard card);

    void Update(BankCard card);

    BankCard? Find(BankCardId id);

    BankCard? FindUsableByAccount(DepositAccountId depositAccountId);

    bool DisplayIdentifierExists(BankId bankId, string displayIdentifier);

    void AddCashCard(CashCard card);

    void UpdateCashCard(CashCard card);

    CashCard? FindCashCardByBankCard(BankCardId bankCardId);

    void AddDebitCard(DebitCard card);

    void UpdateDebitCard(DebitCard card);

    DebitCard? FindDebitCardByBankCard(BankCardId bankCardId);
}

public partial interface IBankingUnitOfWork
{
    IBankCardRepository BankCards { get; }
}
