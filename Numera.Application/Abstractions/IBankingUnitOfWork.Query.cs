using Numera.Application.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IBankQueryReadRepository
{
    IReadOnlyList<BankListItem> ListBanks(EconomyScopeId economyScopeId, string? afterInstitutionCode, int limit);

    BankDetailView? FindBankDetail(EconomyScopeId economyScopeId, string institutionCode);

    BankId? FindBankId(EconomyScopeId economyScopeId, string institutionCode);

    IReadOnlyList<BankProductItem> ListProducts(BankId bankId, string? afterProductCode, int limit);

    IReadOnlyList<BankAccountItem> ListCustomerAccounts(
        CustomerAccountId customerAccountId,
        string? afterAccountNumber,
        int limit);

    DepositAccountDetailView? FindAccountDetail(
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId);

    IReadOnlyList<AccountStatementItem> ListStatement(
        DepositAccountId depositAccountId,
        long? beforePostedAt,
        int limit);
}

public partial interface IBankingReadContext
{
    IBankQueryReadRepository BankQueries { get; }
}
