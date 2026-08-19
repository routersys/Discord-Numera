using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record PresentationProfileRecord(
    PresentationProfileVersionId Id,
    EconomyScopeId EconomyScopeId,
    BankId? BankId,
    int? InformationRgb,
    int? SuccessRgb,
    int? WarningRgb,
    int? ErrorRgb,
    int? NeutralRgb,
    PresentationProfileVersionStatus Status,
    long Version);

public sealed record CurrencyTrustPolicyRecord(
    CurrencyTrustPolicyVersionId Id,
    EconomyScopeId EconomyScopeId,
    long EstablishedMinAgeSeconds,
    int EstablishedMinTradeDays,
    int EstablishedMinCounterparties,
    long TrustedMinAgeSeconds,
    int TrustedMinTradeDays,
    int TrustedMinCounterparties,
    long ReserveMinAgeSeconds,
    int ReserveMinTradeDays,
    int ReserveMinCounterparties,
    CurrencyTrustPolicyVersionStatus Status,
    long Version);

public sealed record CurrencyTrustDesignationRecord(
    CurrencyTrustDesignationId Id,
    CurrencyId CurrencyId,
    CurrencyTrustPolicyVersionId PolicyVersionId,
    CurrencyTrustTier Tier,
    CurrencyTrustDesignationStatus Status,
    long QualifiedAgeSeconds,
    int QualifiedTradeDays,
    int QualifiedCounterparties,
    AuthorizationDecisionId? AuthorizationDecisionId,
    UtcTimestamp EffectiveFrom,
    long Version);

public sealed record MonetaryAuthorityRecord(
    MonetaryAuthorityId Id,
    EconomyScopeId EconomyScopeId,
    PartyId PartyId,
    AccountingBookId AccountingBookId,
    CurrencyId HomeCurrencyId,
    MonetaryAuthorityStatus Status,
    long Version);

public sealed record OfficialReservePositionRecord(
    OfficialReservePositionId Id,
    CurrencyId CurrencyId,
    OfficialReservePositionStatus Status);

public sealed record OfficialReservePortfolioRecord(
    OfficialReservePortfolioId Id,
    MonetaryAuthorityId MonetaryAuthorityId,
    OfficialReservePortfolioStatus Status,
    IReadOnlyList<OfficialReservePositionRecord> Positions,
    long Version);

public sealed record FxInterventionMandateRecord(
    FxInterventionMandateId Id,
    MonetaryAuthorityId MonetaryAuthorityId,
    FxMarketId MarketId,
    string AllowedSide,
    long MaximumSourceMinorPerOrder,
    long MaximumSourceMinorTotal,
    long UsedSourceMinor,
    int MaximumSlippageBps,
    UtcTimestamp ValidFrom,
    UtcTimestamp ValidUntil,
    FxInterventionMandateStatus Status,
    long Version);

public sealed record ResolutionCaseRecord(
    ResolutionCaseId Id,
    BankId BankId,
    ResolutionCaseStatus Status,
    UtcTimestamp OpenedAt,
    BankId? SelectedSuccessorBankId,
    BankId? BridgeBankId,
    long Version);

public sealed record LoanProductRecord(
    AccountProductId ProductId,
    BankId BankId,
    string ProductCode,
    string Name,
    int AnnualRatePpt);

public sealed record LoanContractRecord(
    LoanContractId Id,
    BankId BankId,
    CustomerAccountId CustomerAccountId,
    CurrencyId CurrencyId,
    MoneyMinor PrincipalOriginal,
    MoneyMinor PrincipalOutstanding,
    int AnnualRatePpt,
    LoanContractStatus Status,
    UtcTimestamp OriginatedAt,
    long Version);

public sealed record MerchantOperatorGrantRecord(
    MerchantOperatorGrantId Id,
    MerchantProfileId MerchantProfileId,
    string DiscordUserId,
    bool ManageCatalog,
    bool ManagePaymentPolicy,
    bool ManageRefunds,
    bool ManageReturns,
    bool ManageSettlementAccount,
    MerchantOperatorGrantStatus Status,
    long Version);

public interface IGovernanceRepository
{
    void AddPresentationProfile(PresentationProfileRecord profile, UtcTimestamp createdAt);

    void UpdatePresentationProfile(PresentationProfileRecord profile, UtcTimestamp occurredAt);

    PresentationProfileRecord? FindPresentationProfile(PresentationProfileVersionId id);

    PresentationProfileRecord? FindPublishedPresentationProfile(
        EconomyScopeId economyScopeId,
        BankId? bankId);

    void AddTrustPolicy(CurrencyTrustPolicyRecord policy);

    void UpdateTrustPolicy(CurrencyTrustPolicyRecord policy);

    CurrencyTrustPolicyRecord? FindTrustPolicy(CurrencyTrustPolicyVersionId id);

    CurrencyTrustPolicyRecord? FindPublishedTrustPolicy(EconomyScopeId economyScopeId);

    long NextTrustPolicyVersion(EconomyScopeId economyScopeId);

    void AddTrustDesignation(CurrencyTrustDesignationRecord designation);

    void UpdateTrustDesignation(CurrencyTrustDesignationRecord designation);

    CurrencyTrustDesignationRecord? FindCurrentTrustDesignation(CurrencyId currencyId);

    CurrencyTrustDesignationRecord? FindTrustDesignation(CurrencyTrustDesignationId id);

    MonetaryAuthorityRecord? FindMonetaryAuthority(EconomyScopeId economyScopeId);

    OfficialReservePortfolioRecord? FindReservePortfolio(MonetaryAuthorityId monetaryAuthorityId);

    void AddInterventionMandate(FxInterventionMandateRecord mandate);

    void UpdateInterventionMandate(FxInterventionMandateRecord mandate);

    FxInterventionMandateRecord? FindInterventionMandate(FxInterventionMandateId id);

    ResolutionCaseRecord? FindResolutionCase(ResolutionCaseId id);

    void UpdateResolutionCase(ResolutionCaseRecord resolutionCase);

    IReadOnlyList<LoanProductRecord> ListLoanProducts(BankId bankId, int limit);

    void AddLoanContract(LoanContractRecord contract);

    void AddMerchantOperatorGrant(MerchantOperatorGrantRecord grant);

    void UpdateMerchantOperatorGrant(MerchantOperatorGrantRecord grant);

    MerchantOperatorGrantRecord? FindActiveMerchantOperatorGrant(
        MerchantProfileId merchantProfileId,
        string discordUserId);

    MerchantProfileStatus? FindMerchantProfileStatus(MerchantProfileId merchantProfileId);
}

public partial interface IBankingUnitOfWork
{
    IGovernanceRepository Governance { get; }
}
