using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record DepositInsuranceFundRecord(
    DepositInsuranceFundId Id,
    EconomyScopeId EconomyScopeId,
    CurrencyId CurrencyId,
    PartyId OwnerPartyId,
    AccountingBookId AccountingBookId,
    LedgerAccountId CentralBankSettlementLiabilityLedgerAccountId,
    LedgerAccountId LiquidAssetLedgerAccountId,
    LedgerAccountId PremiumRevenueLedgerAccountId,
    LedgerAccountId ClaimExpenseLedgerAccountId,
    DepositInsuranceFundStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record DepositInsuranceSchemeRecord(
    DepositInsuranceSchemeId Id,
    EconomyScopeId EconomyScopeId,
    CurrencyId CurrencyId,
    string ProtectionClassCode,
    DepositInsuranceSchemeStatus Status,
    DepositInsuranceSchemeVersionId? CurrentVersionId,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record DepositInsuranceSchemeVersionRecord(
    DepositInsuranceSchemeVersionId Id,
    DepositInsuranceSchemeId SchemeId,
    DepositInsuranceFundId FundId,
    MoneyMinor CoverageLimit,
    MoneyMinor EnrollmentFee,
    UtcTimestamp EffectiveFrom,
    long Version);

public sealed record DepositInsuranceEnrollmentRecord(
    DepositInsuranceEnrollmentId Id,
    DepositAccountId DepositAccountId,
    CustomerAccountId CustomerAccountId,
    BankId BankId,
    string ProtectionClassCode,
    DepositInsuranceSchemeVersionId SchemeVersionId,
    MoneyMinor CoverageLimitSnapshot,
    MoneyMinor EnrollmentFeeSnapshot,
    DepositInsurancePremiumPaymentId? PremiumPaymentId,
    DepositInsuranceEnrollmentStatus Status,
    UtcTimestamp EnrolledAt,
    UtcTimestamp? TerminalAt,
    long Version);

public sealed record DepositInsuranceReservationRecord(
    DepositInsuranceReservationId Id,
    DepositInsuranceEnrollmentId EnrollmentId,
    DepositInsuranceFundId FundId,
    MoneyMinor Reserved,
    MoneyMinor Consumed,
    MoneyMinor Released,
    DepositInsuranceReservationStatus Status,
    UtcTimestamp CreatedAt,
    UtcTimestamp? TerminalAt,
    long Version);

public sealed record InsuranceSettlementWalletRecord(
    InsuranceSettlementWalletId Id,
    DepositInsuranceFundId FundId,
    CustomerAccountId CustomerAccountId,
    CurrencyId CurrencyId,
    LedgerAccountId LiabilityLedgerAccountId,
    InsuranceSettlementWalletStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record DepositInsuranceClaimRecord(
    DepositInsuranceClaimId Id,
    ResolutionCaseId ResolutionCaseId,
    DepositInsuranceSchemeVersionId SchemeVersionId,
    DepositInsuranceEnrollmentId EnrollmentId,
    PartyId PartyId,
    CustomerAccountId CustomerAccountId,
    BankId BankId,
    CurrencyId CurrencyId,
    string ProtectionClassCode,
    InsuranceSettlementWalletId SettlementWalletId,
    MoneyMinor Eligible,
    MoneyMinor Insured,
    MoneyMinor Paid,
    DepositInsuranceClaimStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public interface IDepositInsuranceRepository
{
    void AddFund(DepositInsuranceFundRecord fund);

    void UpdateFund(DepositInsuranceFundRecord fund);

    DepositInsuranceFundRecord? FindFund(DepositInsuranceFundId id);

    DepositInsuranceFundRecord? FindFundByCurrency(EconomyScopeId economyScopeId, CurrencyId currencyId);

    void AddScheme(DepositInsuranceSchemeRecord scheme);

    void UpdateScheme(DepositInsuranceSchemeRecord scheme);

    DepositInsuranceSchemeRecord? FindScheme(DepositInsuranceSchemeId id);

    DepositInsuranceSchemeRecord? FindSchemeByClass(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string protectionClassCode);

    IReadOnlyList<DepositInsuranceSchemeRecord> ListSchemes(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        int limit);

    void AddSchemeVersion(DepositInsuranceSchemeVersionRecord version);

    DepositInsuranceSchemeVersionRecord? FindSchemeVersion(DepositInsuranceSchemeVersionId id);

    DepositInsuranceSchemeVersionRecord? FindSchemeVersionByNumber(
        DepositInsuranceSchemeId schemeId,
        long version);

    long NextSchemeVersion(DepositInsuranceSchemeId schemeId);

    void AddEnrollment(DepositInsuranceEnrollmentRecord enrollment);

    void UpdateEnrollment(DepositInsuranceEnrollmentRecord enrollment);

    DepositInsuranceEnrollmentRecord? FindEnrollment(DepositInsuranceEnrollmentId id);

    DepositInsuranceEnrollmentRecord? FindActiveEnrollment(DepositAccountId depositAccountId);

    void AddReservation(DepositInsuranceReservationRecord reservation);

    void UpdateReservation(DepositInsuranceReservationRecord reservation);

    DepositInsuranceReservationRecord? FindReservation(DepositInsuranceEnrollmentId enrollmentId);

    void AddSettlementWallet(InsuranceSettlementWalletRecord wallet);

    InsuranceSettlementWalletRecord? FindSettlementWallet(
        CustomerAccountId customerAccountId,
        CurrencyId currencyId);

    IReadOnlyList<DepositInsuranceClaimRecord> ListClaims(
        CustomerAccountId customerAccountId,
        DepositInsuranceClaimId? after,
        int limit);
}

public partial interface IBankingUnitOfWork
{
    IDepositInsuranceRepository DepositInsurance { get; }
}
