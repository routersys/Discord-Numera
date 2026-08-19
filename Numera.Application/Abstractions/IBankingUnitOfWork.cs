using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface IPartyRepository
{
    void Add(Party party);

    Party? Find(PartyId id);
}

public interface ICustomerAccountRepository
{
    void Add(CustomerAccount account);

    void Update(CustomerAccount account);

    CustomerAccount? Find(CustomerAccountId id);

    bool HandleExists(PublicHandle handle);
}

public interface IDiscordIdentityLinkRepository
{
    void Add(DiscordIdentityLink link);

    void Update(DiscordIdentityLink link);

    DiscordIdentityLink? FindActive(DiscordUserId discordUserId);
}

public interface IBusinessOperationRepository
{
    void Add(BusinessOperation operation);

    void Update(BusinessOperation operation);

    BusinessOperation? Find(IdempotencyKey idempotencyKey);

    BusinessOperation? FindById(BusinessOperationId id);
}

public interface IOutboxRepository
{
    void Add(OutboxEvent outboxEvent);
}

public interface IInteractionSessionRepository
{
    void Add(InteractionSession session);

    void Update(InteractionSession session);

    InteractionSession? FindByTokenHash(byte[] tokenHash);

    IReadOnlyList<InteractionSession> ListActiveByUser(string discordUserId);

    IReadOnlyList<InteractionSession> ListExpired(UtcTimestamp now, int batchSize);

    int PurgeTerminal(UtcTimestamp completedBefore, int batchSize);
}

public partial interface IBankingUnitOfWork
{
    IPartyRepository Parties { get; }

    ICustomerAccountRepository CustomerAccounts { get; }

    IDiscordIdentityLinkRepository DiscordIdentityLinks { get; }

    IBusinessOperationRepository BusinessOperations { get; }

    IOutboxRepository Outbox { get; }

    IInteractionSessionRepository InteractionSessions { get; }

    IBankRepository Banks { get; }

    IBankCustomerRelationshipRepository Relationships { get; }

    ILedgerAccountRepository LedgerAccounts { get; }

    IDepositAccountRepository DepositAccounts { get; }

    IAccountProductRepository AccountProducts { get; }

    IBranchRepository Branches { get; }

    IAccountingPeriodRepository AccountingPeriods { get; }

    IAccountingTransactionRepository AccountingTransactions { get; }

    IHoldRepository Holds { get; }

    IPaymentOrderRepository PaymentOrders { get; }

    IEconomyCalendarRepository EconomyCalendars { get; }

    IFeeScheduleRepository FeeSchedules { get; }

    IFeeWaiverCounterRepository FeeWaiverCounters { get; }

    IFeeAssessmentRepository FeeAssessments { get; }

    IBankPolicyRepository BankPolicies { get; }

    IAccountLimitPreferenceRepository AccountLimitPreferences { get; }

    ISettlementInstructionRepository SettlementInstructions { get; }

    ISettlementParticipationRepository SettlementParticipations { get; }

    IPaymentNetworkRepository PaymentNetworks { get; }

    IClearingRepository Clearing { get; }

    ISystemOwnerRepository SystemOwners { get; }

    IGuildEconomyRepository GuildEconomies { get; }

    ICentralBankSettlementAccountRepository CentralBankSettlementAccounts { get; }

    IPaymentPreferenceRepository PaymentPreferences { get; }
}

public interface IBankingWriteGateway
{
    Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IBankingUnitOfWork, Result<TValue>> operation,
        CancellationToken cancellationToken);
}

public interface IBankReadRepository
{
    IReadOnlyList<Numera.Application.Banking.BankSuggestion> ListSuggestible(
        EconomyScopeId economyScopeId,
        IReadOnlyList<Numera.Domain.Banking.BankStatus> selectableStatuses,
        ulong? operatorDiscordUserId,
        int limit);
}

public interface ICurrencyReadRepository
{
    IReadOnlyList<Numera.Application.Banking.CurrencySuggestion> ListSuggestible(
        EconomyScopeId economyScopeId,
        int limit);
}

public sealed record TransferSourceView(DepositAccountId Id, CurrencyId CurrencyId);

public interface ITransferPreparationReadRepository
{
    TransferSourceView? FindOwnedSource(
        CustomerAccountId payerCustomerAccountId,
        DepositAccountId sourceDepositAccountId);

    CustomerAccountId? FindCustomerByDiscordUser(EconomyScopeId economyScopeId, string discordUserId);

    IReadOnlyList<Numera.Application.Banking.TransferDestinationCandidate> ListPublicReceivingAccounts(
        CustomerAccountId beneficiaryCustomerAccountId,
        CurrencyId currencyId,
        DepositAccountId excludedDepositAccountId,
        int limit);
}

public partial interface IBankingReadContext
{
    IBankReadRepository Banks { get; }

    ICurrencyReadRepository Currencies { get; }

    ITransferPreparationReadRepository TransferPreparation { get; }
}

public interface IBankingReadGateway
{
    TResult Execute<TResult>(Func<IBankingReadContext, TResult> query);
}

public interface IBankRepository
{
    Numera.Domain.Banking.Bank? FindByInstitutionCode(EconomyScopeId economyScopeId, string institutionCode);

    Numera.Domain.Banking.Bank? Find(BankId id);

    Numera.Domain.Banking.Bank? FindByParty(PartyId partyId);
}

public interface IBankCustomerRelationshipRepository
{
    void Add(Numera.Domain.Banking.BankCustomerRelationship relationship);

    void Update(Numera.Domain.Banking.BankCustomerRelationship relationship);

    Numera.Domain.Banking.BankCustomerRelationship? Find(BankId bankId, PartyId partyId);

    long CountByBank(BankId bankId);
}

public interface ILedgerAccountRepository
{
    void Add(Numera.Domain.Accounting.LedgerAccount account);

    Numera.Domain.Accounting.LedgerAccount? Find(LedgerAccountId id);

    Numera.Domain.Accounting.LedgerAccount? FindByCode(AccountingBookId bookId, string accountCode);

    Numera.Domain.Accounting.LedgerAccount? FindPostingByKind(
        AccountingBookId bookId,
        Numera.Domain.Accounting.LedgerAccountKind kind,
        CurrencyId currencyId);

    Numera.Domain.Accounting.LedgerAccount? FindPostingByKindAndOwner(
        AccountingBookId bookId,
        Numera.Domain.Accounting.LedgerAccountKind kind,
        CurrencyId currencyId,
        EntityIdValue ownerReferenceId);

    void UpsertProjection(LedgerAccountId id, Numera.Domain.Accounting.LedgerBalance balance, UtcTimestamp updatedAt);

    Numera.Domain.Accounting.LedgerBalance? FindProjection(LedgerAccountId id);
}

public interface IDepositAccountRepository
{
    void Add(Numera.Domain.Banking.DepositAccount account);

    IReadOnlyList<Numera.Domain.Banking.DepositAccount> ListDueDormant(UtcTimestamp now, int limit);

    IReadOnlyList<Numera.Domain.Banking.DepositAccount> ListDormancyCandidates(
        UtcTimestamp inactiveSince,
        int limit);

    void Update(Numera.Domain.Banking.DepositAccount account);

    Numera.Domain.Banking.DepositAccount? Find(DepositAccountId id);

    Numera.Domain.Banking.DepositAccount? FindByCustomer(BankId bankId, CustomerAccountId customerAccountId);

    Numera.Domain.Banking.DepositAccount? FindByRouting(
        BankId bankId,
        BranchId branchId,
        Numera.Domain.Banking.AccountNumber accountNumber);

    long CountByBranch(BankId bankId, BranchId branchId);
}

public interface IBranchRepository
{
    BranchId? FindIdByCode(BankId bankId, string branchCode);

    string? FindCodeById(BranchId branchId);
}

public interface IAccountingPeriodRepository
{
    AccountingPeriodId? FindOpen(AccountingBookId bookId, BusinessDate businessDate);
}

public interface IAccountingTransactionRepository
{
    void Add(Numera.Domain.Accounting.AccountingTransaction transaction, AccountingPeriodId periodId);
}

public interface IHoldRepository
{
    void Add(Numera.Domain.Banking.Hold hold);

    void Update(Numera.Domain.Banking.Hold hold);

    Numera.Domain.Banking.Hold? Find(HoldId id);

    Numera.Domain.Banking.Hold? FindActiveByBusinessOperation(BusinessOperationId businessOperationId);

    Numera.Domain.Banking.Hold? FindByBusinessOperation(BusinessOperationId businessOperationId);

    IReadOnlyList<Numera.Domain.Banking.Hold> ListExpiredStandalone(UtcTimestamp now, int limit);
}

public interface IPaymentOrderRepository
{
    void Add(Numera.Domain.Banking.PaymentOrder order);

    void Update(Numera.Domain.Banking.PaymentOrder order);

    Numera.Domain.Banking.PaymentOrder? Find(PaymentOrderId id);

    Numera.Domain.Banking.PaymentOrder? FindByBusinessOperation(BusinessOperationId businessOperationId);

    MoneyMinor SumOutgoingAmount(
        DepositAccountId sourceDepositAccountId,
        UtcTimestamp fromInclusive,
        UtcTimestamp toExclusive);

    MoneyMinor SumUnfinalisedPreCreditExposure(BankId sourceBankId);
}

public sealed record TransferLimitSet(MoneyMinor? PerTransfer, MoneyMinor? DailyOutgoing);

public interface IBankPolicyRepository
{
    TransferLimitSet? FindTransferLimits(BankPolicyVersionId bankPolicyVersionId);

    MoneyMinor? FindMaximumActiveHolds(BankPolicyVersionId bankPolicyVersionId);
}

public partial interface IAccountLimitPreferenceRepository
{
    TransferLimitSet? FindTransferLimits(DepositAccountId depositAccountId);
}

public interface ISettlementInstructionRepository
{
    void Add(Numera.Domain.Banking.SettlementInstruction instruction);

    void Update(Numera.Domain.Banking.SettlementInstruction instruction);

    Numera.Domain.Banking.SettlementInstruction? FindByBusinessOperation(
        BusinessOperationId businessOperationId);

    IReadOnlyList<BusinessOperationId> ListQueued(EntityIdValue? afterId, int limit);
}

public interface IPaymentPreferenceRepository
{
    void Add(Numera.Domain.Banking.PaymentPreference preference);

    void Update(Numera.Domain.Banking.PaymentPreference preference);

    Numera.Domain.Banking.PaymentPreference? Find(
        CustomerAccountId customerAccountId,
        Numera.Domain.Banking.PaymentPreferenceKind kind);
}

public interface IClearingRepository
{
    Numera.Domain.Banking.ClearingCycle? FindCycle(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string cycleKey);

    Numera.Domain.Banking.ClearingCycle? FindCycleById(ClearingCycleId clearingCycleId);

    IReadOnlyList<Numera.Domain.Banking.ClearingCycle> ListUnclosedCycles(int limit);

    void AddCycle(Numera.Domain.Banking.ClearingCycle cycle);

    void UpdateCycle(Numera.Domain.Banking.ClearingCycle cycle);

    void AddInstruction(Numera.Domain.Banking.ClearingInstruction instruction);

    void UpdateInstruction(Numera.Domain.Banking.ClearingInstruction instruction);

    Numera.Domain.Banking.ClearingInstruction? FindInstructionByBusinessOperation(
        BusinessOperationId businessOperationId);

    IReadOnlyList<Numera.Domain.Banking.ClearingInstruction> ListInstructions(ClearingCycleId clearingCycleId);

    IReadOnlyList<Numera.Domain.Banking.ClearingPosition> ListPositions(ClearingCycleId clearingCycleId);

    void AccumulatePosition(
        ClearingPositionId identity,
        ClearingCycleId clearingCycleId,
        BankId bankId,
        CurrencyId currencyId,
        MoneyMinor receivableDelta,
        MoneyMinor payableDelta);
}

public interface IPaymentNetworkRepository
{
    Numera.Domain.Banking.PaymentNetwork? FindRouting(EconomyScopeId economyScopeId);

    Numera.Domain.Banking.PaymentNetworkPolicyVersion? FindPolicy(
        PaymentNetworkPolicyVersionId paymentNetworkPolicyVersionId);

    Numera.Domain.Banking.PaymentNetworkPrefund? FindPrefund(
        PaymentNetworkId paymentNetworkId,
        BankId bankId,
        CurrencyId currencyId);

    Numera.Domain.Banking.PaymentNetwork? Find(PaymentNetworkId paymentNetworkId);

    Numera.Domain.Banking.PaymentNetwork? FindByCode(EconomyScopeId economyScopeId, string networkCode);

    void Add(Numera.Domain.Banking.PaymentNetwork network);

    void Update(Numera.Domain.Banking.PaymentNetwork network);

    void AddPolicy(Numera.Domain.Banking.PaymentNetworkPolicyVersion policy);

    long NextPolicyVersion(PaymentNetworkId paymentNetworkId);
}

public interface ISystemOwnerRepository
{
    bool Contains(string discordUserId);
}

public partial interface IGuildEconomyRepository
{
    string? FindGuildId(EconomyScopeId economyScopeId);
}

public interface ISettlementParticipationRepository
{
    Numera.Domain.Banking.SettlementParticipation? FindLive(BankId bankId);
}

public sealed record CentralBankSettlementAccountView(
    LedgerAccountId CentralBankLedgerAccountId,
    CurrencyId CurrencyId,
    Numera.Domain.Banking.CentralBankSettlementAccountStatus Status);

public interface ICentralBankSettlementAccountRepository
{
    CentralBankSettlementAccountView? Find(CentralBankSettlementAccountId centralBankSettlementAccountId);
}

public interface IAccountProductRepository
{
    AccountProductSelection? FindDefault(BankId bankId);
}

public interface IEconomyCalendarRepository
{
    string? FindCanonicalTimezone(EconomyScopeId economyScopeId);

    BusinessDayClass? FindDayClassOverride(EconomyScopeId economyScopeId, BusinessDate localDate);

    void UpsertDayClassOverride(
        EconomyScopeId economyScopeId,
        BusinessDate localDate,
        BusinessDayClass dayClass,
        string? description);

    bool DeleteDayClassOverride(EconomyScopeId economyScopeId, BusinessDate localDate);
}

public interface IFeeScheduleRepository
{
    IReadOnlyList<Numera.Domain.Banking.FeeRule> ListRules(
        FeeScheduleVersionId feeScheduleVersionId,
        Numera.Domain.Banking.FeeType feeType);
}

public interface IFeeWaiverCounterRepository
{
    long FindUsedCount(DepositAccountId depositAccountId, string waiverCounterKey, int businessMonth);

    void Consume(DepositAccountId depositAccountId, string waiverCounterKey, int businessMonth);
}

public interface IFeeAssessmentRepository
{
    void Add(Numera.Domain.Accounting.FeeAssessment assessment);
}

public sealed record AccountProductSelection(
    AccountProductId ProductId,
    AccountProductVersionId ProductVersionId,
    BranchId BranchId);
