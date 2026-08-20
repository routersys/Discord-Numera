using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record LedgerExposure(
    LedgerAccountKind Kind,
    AccountingType AccountingType,
    MoneyMinor PostedBalance,
    bool DefaultedLoan);

public partial interface ILedgerAccountRepository
{
    IReadOnlyList<LedgerExposure> ListPostedExposures(AccountingBookId bookId, CurrencyId currencyId);
}
