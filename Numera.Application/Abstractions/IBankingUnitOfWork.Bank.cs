using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IBankAdministrationRepository
{
    bool InstitutionCodeExists(string institutionCode);

    void AddBank(Bank bank);

    void UpdateBank(Bank bank);

    void AddAccountingBook(AccountingBookId id, PartyId ownerPartyId, UtcTimestamp createdAt);

    void AddBranch(BranchId id, BankId bankId, string branchCode, string name, UtcTimestamp createdAt);

    void AddAccountProduct(
        AccountProductId id,
        BankId bankId,
        string productCode,
        string name,
        UtcTimestamp createdAt);

    void AddAccountProductVersion(
        AccountProductVersionId id,
        AccountProductId productId,
        MoneyMinor minimumBalance,
        UtcTimestamp effectiveFrom);

    void AddBankPolicyVersion(BankPolicyVersion policy);

    BankPolicyVersion? FindBankPolicyVersion(BankPolicyVersionId id);

    void AddFeeScheduleVersion(
        FeeScheduleVersionId id,
        BankId bankId,
        UtcTimestamp effectiveFrom,
        long version);

    void AddFeeRule(FeeRule rule);

    void AddSettlementParticipation(SettlementParticipation participation);

    void AddCentralBankSettlementAccount(
        CentralBankSettlementAccountId id,
        BankId bankId,
        CurrencyId currencyId,
        LedgerAccountId centralBankLedgerAccountId,
        UtcTimestamp openedAt);

    PrudentialPolicyVersion? FindPublishedPrudentialPolicy(EconomyScopeId economyScopeId);

    CurrencyId? FindActiveCurrency(EconomyScopeId economyScopeId);

    bool HasOperatingBank(EconomyScopeId economyScopeId);

    void AddAuditRecord(
        AuditRecordId id,
        BusinessOperationId businessOperationId,
        string? actorDiscordUserId,
        string action,
        string targetType,
        EntityIdValue targetId,
        string? reason,
        UtcTimestamp occurredAt);

    void AddOpeningApplication(AccountOpeningApplication application);

    void UpdateOpeningApplication(AccountOpeningApplication application);

    AccountOpeningApplication? FindOpeningApplication(AccountOpeningApplicationId id);

    AccountOpeningApplication? FindPendingOpeningApplication(BankId bankId, CustomerAccountId customerAccountId);

    DepositAccountId? FindOutgoingCapableAccount(
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        BankId excludedBankId);
}

public partial interface IBankingUnitOfWork
{
    IBankAdministrationRepository BankAdministration { get; }
}
