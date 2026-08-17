using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface ICurrencyRepository
{
    void Add(Currency currency);

    void Update(Currency currency);

    Currency? Find(CurrencyId id);

    Currency? FindCurrent(EconomyScopeId economyScopeId);

    bool EconomyIsActive(EconomyScopeId economyScopeId);

    bool AccountingBookIsOpen(AccountingBookId accountingBookId);

    void AddMetadataVersion(CurrencyMetadataVersion metadata);

    CurrencyMetadataVersion? FindCurrentMetadata(CurrencyId currencyId);

    void AddSupplyOperation(CurrencySupplyOperation operation);

    CurrencySupplyTotals SumSupply(CurrencyId currencyId);

    LedgerAccount? FindIssuanceLiabilityAccount(CurrencyId currencyId);
}

public partial interface IBankingUnitOfWork
{
    ICurrencyRepository Currencies { get; }
}
