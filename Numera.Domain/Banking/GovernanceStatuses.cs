using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum PresentationProfileVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class PresentationProfileStatusCatalog
{
    private static readonly StateTransitionTable<PresentationProfileVersionStatus> Transitions =
        StateTransitionTable<PresentationProfileVersionStatus>
            .Create(InvariantViolationCode.PresentationProfileTransitionInvalid)
            .AllowCreation(PresentationProfileVersionStatus.Draft)
            .Allow(
                PresentationProfileVersionStatus.Draft,
                PresentationProfileVersionStatus.Published,
                PresentationProfileVersionStatus.Retired)
            .Allow(
                PresentationProfileVersionStatus.Published,
                PresentationProfileVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(PresentationProfileVersionStatus from, PresentationProfileVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(PresentationProfileVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this PresentationProfileVersionStatus status) => status switch
    {
        PresentationProfileVersionStatus.Draft => "DRAFT",
        PresentationProfileVersionStatus.Published => "PUBLISHED",
        PresentationProfileVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out PresentationProfileVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = PresentationProfileVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = PresentationProfileVersionStatus.Published;
                return true;
            case "RETIRED":
                status = PresentationProfileVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static PresentationProfileVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out PresentationProfileVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.PresentationProfileStatusUnknown);
}

public enum CurrencyTrustPolicyVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class CurrencyTrustPolicyStatusCatalog
{
    private static readonly StateTransitionTable<CurrencyTrustPolicyVersionStatus> Transitions =
        StateTransitionTable<CurrencyTrustPolicyVersionStatus>
            .Create(InvariantViolationCode.CurrencyTrustPolicyTransitionInvalid)
            .AllowCreation(CurrencyTrustPolicyVersionStatus.Draft)
            .Allow(
                CurrencyTrustPolicyVersionStatus.Draft,
                CurrencyTrustPolicyVersionStatus.Published,
                CurrencyTrustPolicyVersionStatus.Retired)
            .Allow(
                CurrencyTrustPolicyVersionStatus.Published,
                CurrencyTrustPolicyVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(CurrencyTrustPolicyVersionStatus from, CurrencyTrustPolicyVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CurrencyTrustPolicyVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CurrencyTrustPolicyVersionStatus status) => status switch
    {
        CurrencyTrustPolicyVersionStatus.Draft => "DRAFT",
        CurrencyTrustPolicyVersionStatus.Published => "PUBLISHED",
        CurrencyTrustPolicyVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CurrencyTrustPolicyVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = CurrencyTrustPolicyVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = CurrencyTrustPolicyVersionStatus.Published;
                return true;
            case "RETIRED":
                status = CurrencyTrustPolicyVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CurrencyTrustPolicyVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CurrencyTrustPolicyVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CurrencyTrustPolicyStatusUnknown);
}

public enum CurrencyTrustDesignationStatus
{
    Active = 1,
    Suspended = 2,
    Superseded = 3,
}

public static class CurrencyTrustDesignationStatusCatalog
{
    private static readonly StateTransitionTable<CurrencyTrustDesignationStatus> Transitions =
        StateTransitionTable<CurrencyTrustDesignationStatus>
            .Create(InvariantViolationCode.CurrencyTrustDesignationTransitionInvalid)
            .AllowCreation(CurrencyTrustDesignationStatus.Active)
            .Allow(
                CurrencyTrustDesignationStatus.Active,
                CurrencyTrustDesignationStatus.Suspended,
                CurrencyTrustDesignationStatus.Superseded)
            .Allow(
                CurrencyTrustDesignationStatus.Suspended,
                CurrencyTrustDesignationStatus.Active,
                CurrencyTrustDesignationStatus.Superseded)
            .Build();

    public static void EnsureTransition(CurrencyTrustDesignationStatus from, CurrencyTrustDesignationStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CurrencyTrustDesignationStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CurrencyTrustDesignationStatus status) => status switch
    {
        CurrencyTrustDesignationStatus.Active => "ACTIVE",
        CurrencyTrustDesignationStatus.Suspended => "SUSPENDED",
        CurrencyTrustDesignationStatus.Superseded => "SUPERSEDED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CurrencyTrustDesignationStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CurrencyTrustDesignationStatus.Active;
                return true;
            case "SUSPENDED":
                status = CurrencyTrustDesignationStatus.Suspended;
                return true;
            case "SUPERSEDED":
                status = CurrencyTrustDesignationStatus.Superseded;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CurrencyTrustDesignationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CurrencyTrustDesignationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CurrencyTrustDesignationStatusUnknown);
}

public enum MonetaryAuthorityStatus
{
    Active = 1,
    Suspended = 2,
    Retired = 3,
}

public static class MonetaryAuthorityStatusCatalog
{
    private static readonly StateTransitionTable<MonetaryAuthorityStatus> Transitions =
        StateTransitionTable<MonetaryAuthorityStatus>
            .Create(InvariantViolationCode.MonetaryAuthorityTransitionInvalid)
            .AllowCreation(MonetaryAuthorityStatus.Active)
            .Allow(
                MonetaryAuthorityStatus.Active,
                MonetaryAuthorityStatus.Suspended,
                MonetaryAuthorityStatus.Retired)
            .Allow(
                MonetaryAuthorityStatus.Suspended,
                MonetaryAuthorityStatus.Active,
                MonetaryAuthorityStatus.Retired)
            .Build();

    public static void EnsureTransition(MonetaryAuthorityStatus from, MonetaryAuthorityStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MonetaryAuthorityStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MonetaryAuthorityStatus status) => status switch
    {
        MonetaryAuthorityStatus.Active => "ACTIVE",
        MonetaryAuthorityStatus.Suspended => "SUSPENDED",
        MonetaryAuthorityStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MonetaryAuthorityStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = MonetaryAuthorityStatus.Active;
                return true;
            case "SUSPENDED":
                status = MonetaryAuthorityStatus.Suspended;
                return true;
            case "RETIRED":
                status = MonetaryAuthorityStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MonetaryAuthorityStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MonetaryAuthorityStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MonetaryAuthorityStatusUnknown);
}

public enum OfficialReservePortfolioStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public static class OfficialReservePortfolioStatusCatalog
{
    private static readonly StateTransitionTable<OfficialReservePortfolioStatus> Transitions =
        StateTransitionTable<OfficialReservePortfolioStatus>
            .Create(InvariantViolationCode.OfficialReservePortfolioTransitionInvalid)
            .AllowCreation(OfficialReservePortfolioStatus.Active)
            .Allow(
                OfficialReservePortfolioStatus.Active,
                OfficialReservePortfolioStatus.Restricted,
                OfficialReservePortfolioStatus.Closed)
            .Allow(
                OfficialReservePortfolioStatus.Restricted,
                OfficialReservePortfolioStatus.Active,
                OfficialReservePortfolioStatus.Closed)
            .Build();

    public static void EnsureTransition(OfficialReservePortfolioStatus from, OfficialReservePortfolioStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(OfficialReservePortfolioStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this OfficialReservePortfolioStatus status) => status switch
    {
        OfficialReservePortfolioStatus.Active => "ACTIVE",
        OfficialReservePortfolioStatus.Restricted => "RESTRICTED",
        OfficialReservePortfolioStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out OfficialReservePortfolioStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = OfficialReservePortfolioStatus.Active;
                return true;
            case "RESTRICTED":
                status = OfficialReservePortfolioStatus.Restricted;
                return true;
            case "CLOSED":
                status = OfficialReservePortfolioStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static OfficialReservePortfolioStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out OfficialReservePortfolioStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.OfficialReservePortfolioStatusUnknown);
}

public enum OfficialReservePositionStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public static class OfficialReservePositionStatusCatalog
{
    private static readonly StateTransitionTable<OfficialReservePositionStatus> Transitions =
        StateTransitionTable<OfficialReservePositionStatus>
            .Create(InvariantViolationCode.OfficialReservePositionTransitionInvalid)
            .AllowCreation(OfficialReservePositionStatus.Active)
            .Allow(
                OfficialReservePositionStatus.Active,
                OfficialReservePositionStatus.Restricted,
                OfficialReservePositionStatus.Closed)
            .Allow(
                OfficialReservePositionStatus.Restricted,
                OfficialReservePositionStatus.Active,
                OfficialReservePositionStatus.Closed)
            .Build();

    public static void EnsureTransition(OfficialReservePositionStatus from, OfficialReservePositionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(OfficialReservePositionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this OfficialReservePositionStatus status) => status switch
    {
        OfficialReservePositionStatus.Active => "ACTIVE",
        OfficialReservePositionStatus.Restricted => "RESTRICTED",
        OfficialReservePositionStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out OfficialReservePositionStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = OfficialReservePositionStatus.Active;
                return true;
            case "RESTRICTED":
                status = OfficialReservePositionStatus.Restricted;
                return true;
            case "CLOSED":
                status = OfficialReservePositionStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static OfficialReservePositionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out OfficialReservePositionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.OfficialReservePositionStatusUnknown);
}

public enum FxInterventionMandateStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Expired = 4,
    Cancelled = 5,
}

public static class FxInterventionMandateStatusCatalog
{
    private static readonly StateTransitionTable<FxInterventionMandateStatus> Transitions =
        StateTransitionTable<FxInterventionMandateStatus>
            .Create(InvariantViolationCode.FxInterventionMandateTransitionInvalid)
            .AllowCreation(FxInterventionMandateStatus.Draft)
            .Allow(
                FxInterventionMandateStatus.Draft,
                FxInterventionMandateStatus.Active,
                FxInterventionMandateStatus.Expired,
                FxInterventionMandateStatus.Cancelled)
            .Allow(
                FxInterventionMandateStatus.Active,
                FxInterventionMandateStatus.Suspended,
                FxInterventionMandateStatus.Expired,
                FxInterventionMandateStatus.Cancelled)
            .Allow(
                FxInterventionMandateStatus.Suspended,
                FxInterventionMandateStatus.Active,
                FxInterventionMandateStatus.Expired,
                FxInterventionMandateStatus.Cancelled)
            .Build();

    public static void EnsureTransition(FxInterventionMandateStatus from, FxInterventionMandateStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(FxInterventionMandateStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this FxInterventionMandateStatus status) => status switch
    {
        FxInterventionMandateStatus.Draft => "DRAFT",
        FxInterventionMandateStatus.Active => "ACTIVE",
        FxInterventionMandateStatus.Suspended => "SUSPENDED",
        FxInterventionMandateStatus.Expired => "EXPIRED",
        FxInterventionMandateStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out FxInterventionMandateStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = FxInterventionMandateStatus.Draft;
                return true;
            case "ACTIVE":
                status = FxInterventionMandateStatus.Active;
                return true;
            case "SUSPENDED":
                status = FxInterventionMandateStatus.Suspended;
                return true;
            case "EXPIRED":
                status = FxInterventionMandateStatus.Expired;
                return true;
            case "CANCELLED":
                status = FxInterventionMandateStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static FxInterventionMandateStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out FxInterventionMandateStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.FxInterventionMandateStatusUnknown);
}

public enum ResolutionCaseStatus
{
    Open = 1,
    Restricted = 2,
    TransferInProgress = 3,
    Resolved = 4,
    Liquidated = 5,
}

public static class ResolutionCaseStatusCatalog
{
    private static readonly StateTransitionTable<ResolutionCaseStatus> Transitions =
        StateTransitionTable<ResolutionCaseStatus>
            .Create(InvariantViolationCode.ResolutionCaseTransitionInvalid)
            .AllowCreation(ResolutionCaseStatus.Open)
            .Allow(
                ResolutionCaseStatus.Open,
                ResolutionCaseStatus.Restricted)
            .Allow(
                ResolutionCaseStatus.Restricted,
                ResolutionCaseStatus.TransferInProgress,
                ResolutionCaseStatus.Liquidated)
            .Allow(
                ResolutionCaseStatus.TransferInProgress,
                ResolutionCaseStatus.Resolved,
                ResolutionCaseStatus.Liquidated)
            .Build();

    public static void EnsureTransition(ResolutionCaseStatus from, ResolutionCaseStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(ResolutionCaseStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this ResolutionCaseStatus status) => status switch
    {
        ResolutionCaseStatus.Open => "OPEN",
        ResolutionCaseStatus.Restricted => "RESTRICTED",
        ResolutionCaseStatus.TransferInProgress => "TRANSFER_IN_PROGRESS",
        ResolutionCaseStatus.Resolved => "RESOLVED",
        ResolutionCaseStatus.Liquidated => "LIQUIDATED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ResolutionCaseStatus status)
    {
        switch (token)
        {
            case "OPEN":
                status = ResolutionCaseStatus.Open;
                return true;
            case "RESTRICTED":
                status = ResolutionCaseStatus.Restricted;
                return true;
            case "TRANSFER_IN_PROGRESS":
                status = ResolutionCaseStatus.TransferInProgress;
                return true;
            case "RESOLVED":
                status = ResolutionCaseStatus.Resolved;
                return true;
            case "LIQUIDATED":
                status = ResolutionCaseStatus.Liquidated;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static ResolutionCaseStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ResolutionCaseStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.ResolutionCaseStatusUnknown);
}

public enum MerchantProfileStatus
{
    Active = 1,
    Suspended = 2,
    Closing = 3,
    Closed = 4,
}

public static class MerchantProfileStatusCatalog
{
    private static readonly StateTransitionTable<MerchantProfileStatus> Transitions =
        StateTransitionTable<MerchantProfileStatus>
            .Create(InvariantViolationCode.MerchantProfileTransitionInvalid)
            .AllowCreation(MerchantProfileStatus.Active)
            .Allow(
                MerchantProfileStatus.Active,
                MerchantProfileStatus.Suspended,
                MerchantProfileStatus.Closing)
            .Allow(
                MerchantProfileStatus.Suspended,
                MerchantProfileStatus.Active,
                MerchantProfileStatus.Closing)
            .Allow(
                MerchantProfileStatus.Closing,
                MerchantProfileStatus.Suspended,
                MerchantProfileStatus.Closed)
            .Build();

    public static bool IsAllowed(MerchantProfileStatus from, MerchantProfileStatus to) =>
        Transitions.IsAllowed(from, to);

    public static void EnsureTransition(MerchantProfileStatus from, MerchantProfileStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantProfileStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantProfileStatus status) => status switch
    {
        MerchantProfileStatus.Active => "ACTIVE",
        MerchantProfileStatus.Suspended => "SUSPENDED",
        MerchantProfileStatus.Closing => "CLOSING",
        MerchantProfileStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantProfileStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = MerchantProfileStatus.Active;
                return true;
            case "SUSPENDED":
                status = MerchantProfileStatus.Suspended;
                return true;
            case "CLOSING":
                status = MerchantProfileStatus.Closing;
                return true;
            case "CLOSED":
                status = MerchantProfileStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantProfileStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantProfileStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantProfileStatusUnknown);
}

public enum MerchantOperatorGrantStatus
{
    Active = 1,
    Revoked = 2,
}

public static class MerchantOperatorGrantStatusCatalog
{
    private static readonly StateTransitionTable<MerchantOperatorGrantStatus> Transitions =
        StateTransitionTable<MerchantOperatorGrantStatus>
            .Create(InvariantViolationCode.MerchantOperatorGrantTransitionInvalid)
            .AllowCreation(MerchantOperatorGrantStatus.Active)
            .Allow(
                MerchantOperatorGrantStatus.Active,
                MerchantOperatorGrantStatus.Revoked)
            .Build();

    public static void EnsureTransition(MerchantOperatorGrantStatus from, MerchantOperatorGrantStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantOperatorGrantStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantOperatorGrantStatus status) => status switch
    {
        MerchantOperatorGrantStatus.Active => "ACTIVE",
        MerchantOperatorGrantStatus.Revoked => "REVOKED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantOperatorGrantStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = MerchantOperatorGrantStatus.Active;
                return true;
            case "REVOKED":
                status = MerchantOperatorGrantStatus.Revoked;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantOperatorGrantStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantOperatorGrantStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantOperatorGrantStatusUnknown);
}

public enum LoanContractStatus
{
    Approved = 1,
    Active = 2,
    Delinquent = 3,
    Defaulted = 4,
    Paid = 5,
    WrittenOff = 6,
    Cancelled = 7,
}

public static class LoanContractStatusCatalog
{
    private static readonly StateTransitionTable<LoanContractStatus> Transitions =
        StateTransitionTable<LoanContractStatus>
            .Create(InvariantViolationCode.LoanContractTransitionInvalid)
            .AllowCreation(LoanContractStatus.Approved)
            .Allow(
                LoanContractStatus.Approved,
                LoanContractStatus.Active,
                LoanContractStatus.Cancelled)
            .Allow(
                LoanContractStatus.Active,
                LoanContractStatus.Delinquent,
                LoanContractStatus.Paid)
            .Allow(
                LoanContractStatus.Delinquent,
                LoanContractStatus.Active,
                LoanContractStatus.Defaulted,
                LoanContractStatus.Paid)
            .Allow(
                LoanContractStatus.Defaulted,
                LoanContractStatus.WrittenOff,
                LoanContractStatus.Paid)
            .Build();

    public static void EnsureTransition(LoanContractStatus from, LoanContractStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(LoanContractStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this LoanContractStatus status) => status switch
    {
        LoanContractStatus.Approved => "APPROVED",
        LoanContractStatus.Active => "ACTIVE",
        LoanContractStatus.Delinquent => "DELINQUENT",
        LoanContractStatus.Defaulted => "DEFAULTED",
        LoanContractStatus.Paid => "PAID",
        LoanContractStatus.WrittenOff => "WRITTEN_OFF",
        LoanContractStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out LoanContractStatus status)
    {
        switch (token)
        {
            case "APPROVED":
                status = LoanContractStatus.Approved;
                return true;
            case "ACTIVE":
                status = LoanContractStatus.Active;
                return true;
            case "DELINQUENT":
                status = LoanContractStatus.Delinquent;
                return true;
            case "DEFAULTED":
                status = LoanContractStatus.Defaulted;
                return true;
            case "PAID":
                status = LoanContractStatus.Paid;
                return true;
            case "WRITTEN_OFF":
                status = LoanContractStatus.WrittenOff;
                return true;
            case "CANCELLED":
                status = LoanContractStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static LoanContractStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out LoanContractStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.LoanContractStatusUnknown);
}

public enum LoanScheduleStatus
{
    Scheduled = 1,
    Due = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5,
    Waived = 6,
}

public static class LoanScheduleStatusCatalog
{
    private static readonly StateTransitionTable<LoanScheduleStatus> Transitions =
        StateTransitionTable<LoanScheduleStatus>
            .Create(InvariantViolationCode.LoanScheduleTransitionInvalid)
            .AllowCreation(LoanScheduleStatus.Scheduled)
            .Allow(
                LoanScheduleStatus.Scheduled,
                LoanScheduleStatus.Due,
                LoanScheduleStatus.Waived)
            .Allow(
                LoanScheduleStatus.Due,
                LoanScheduleStatus.PartiallyPaid,
                LoanScheduleStatus.Paid,
                LoanScheduleStatus.Overdue,
                LoanScheduleStatus.Waived)
            .Allow(
                LoanScheduleStatus.PartiallyPaid,
                LoanScheduleStatus.Paid,
                LoanScheduleStatus.Overdue,
                LoanScheduleStatus.Waived)
            .Allow(
                LoanScheduleStatus.Overdue,
                LoanScheduleStatus.PartiallyPaid,
                LoanScheduleStatus.Paid,
                LoanScheduleStatus.Waived)
            .Build();

    public static void EnsureTransition(LoanScheduleStatus from, LoanScheduleStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(LoanScheduleStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this LoanScheduleStatus status) => status switch
    {
        LoanScheduleStatus.Scheduled => "SCHEDULED",
        LoanScheduleStatus.Due => "DUE",
        LoanScheduleStatus.PartiallyPaid => "PARTIALLY_PAID",
        LoanScheduleStatus.Paid => "PAID",
        LoanScheduleStatus.Overdue => "OVERDUE",
        LoanScheduleStatus.Waived => "WAIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out LoanScheduleStatus status)
    {
        switch (token)
        {
            case "SCHEDULED":
                status = LoanScheduleStatus.Scheduled;
                return true;
            case "DUE":
                status = LoanScheduleStatus.Due;
                return true;
            case "PARTIALLY_PAID":
                status = LoanScheduleStatus.PartiallyPaid;
                return true;
            case "PAID":
                status = LoanScheduleStatus.Paid;
                return true;
            case "OVERDUE":
                status = LoanScheduleStatus.Overdue;
                return true;
            case "WAIVED":
                status = LoanScheduleStatus.Waived;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static LoanScheduleStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out LoanScheduleStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.LoanScheduleStatusUnknown);
}
