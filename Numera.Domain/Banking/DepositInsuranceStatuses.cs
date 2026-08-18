using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum DepositInsuranceFundStatus
{
    Active = 1,
    Suspended = 2,
    Retired = 3,
}

public static class DepositInsuranceFundStatusCatalog
{
    private static readonly StateTransitionTable<DepositInsuranceFundStatus> Transitions =
        StateTransitionTable<DepositInsuranceFundStatus>
            .Create(InvariantViolationCode.DepositInsuranceFundTransitionInvalid)
            .AllowCreation(DepositInsuranceFundStatus.Active)
            .Allow(
                DepositInsuranceFundStatus.Active,
                DepositInsuranceFundStatus.Suspended,
                DepositInsuranceFundStatus.Retired)
            .Allow(
                DepositInsuranceFundStatus.Suspended,
                DepositInsuranceFundStatus.Active,
                DepositInsuranceFundStatus.Retired)
            .Build();

    public static bool IsAllowed(DepositInsuranceFundStatus from, DepositInsuranceFundStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(DepositInsuranceFundStatus from, DepositInsuranceFundStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DepositInsuranceFundStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DepositInsuranceFundStatus status) => status switch
    {
        DepositInsuranceFundStatus.Active => "ACTIVE",
        DepositInsuranceFundStatus.Suspended => "SUSPENDED",
        DepositInsuranceFundStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DepositInsuranceFundStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = DepositInsuranceFundStatus.Active;
                return true;
            case "SUSPENDED":
                status = DepositInsuranceFundStatus.Suspended;
                return true;
            case "RETIRED":
                status = DepositInsuranceFundStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositInsuranceFundStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DepositInsuranceFundStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositInsuranceFundStatusUnknown);
}

public enum DepositInsuranceSchemeStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Retired = 4,
}

public static class DepositInsuranceSchemeStatusCatalog
{
    private static readonly StateTransitionTable<DepositInsuranceSchemeStatus> Transitions =
        StateTransitionTable<DepositInsuranceSchemeStatus>
            .Create(InvariantViolationCode.DepositInsuranceSchemeTransitionInvalid)
            .AllowCreation(DepositInsuranceSchemeStatus.Draft)
            .Allow(
                DepositInsuranceSchemeStatus.Draft,
                DepositInsuranceSchemeStatus.Active,
                DepositInsuranceSchemeStatus.Retired)
            .Allow(
                DepositInsuranceSchemeStatus.Active,
                DepositInsuranceSchemeStatus.Suspended,
                DepositInsuranceSchemeStatus.Retired)
            .Allow(
                DepositInsuranceSchemeStatus.Suspended,
                DepositInsuranceSchemeStatus.Active,
                DepositInsuranceSchemeStatus.Retired)
            .Build();

    public static bool IsAllowed(DepositInsuranceSchemeStatus from, DepositInsuranceSchemeStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(DepositInsuranceSchemeStatus from, DepositInsuranceSchemeStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DepositInsuranceSchemeStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DepositInsuranceSchemeStatus status) => status switch
    {
        DepositInsuranceSchemeStatus.Draft => "DRAFT",
        DepositInsuranceSchemeStatus.Active => "ACTIVE",
        DepositInsuranceSchemeStatus.Suspended => "SUSPENDED",
        DepositInsuranceSchemeStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DepositInsuranceSchemeStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = DepositInsuranceSchemeStatus.Draft;
                return true;
            case "ACTIVE":
                status = DepositInsuranceSchemeStatus.Active;
                return true;
            case "SUSPENDED":
                status = DepositInsuranceSchemeStatus.Suspended;
                return true;
            case "RETIRED":
                status = DepositInsuranceSchemeStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositInsuranceSchemeStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DepositInsuranceSchemeStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositInsuranceSchemeStatusUnknown);
}

public enum DepositInsuranceEnrollmentStatus
{
    Active = 1,
    Cancelled = 2,
    Claimed = 3,
}

public static class DepositInsuranceEnrollmentStatusCatalog
{
    private static readonly StateTransitionTable<DepositInsuranceEnrollmentStatus> Transitions =
        StateTransitionTable<DepositInsuranceEnrollmentStatus>
            .Create(InvariantViolationCode.DepositInsuranceEnrollmentTransitionInvalid)
            .AllowCreation(DepositInsuranceEnrollmentStatus.Active)
            .Allow(
                DepositInsuranceEnrollmentStatus.Active,
                DepositInsuranceEnrollmentStatus.Cancelled,
                DepositInsuranceEnrollmentStatus.Claimed)
            .Build();

    public static bool IsAllowed(DepositInsuranceEnrollmentStatus from, DepositInsuranceEnrollmentStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(DepositInsuranceEnrollmentStatus from, DepositInsuranceEnrollmentStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DepositInsuranceEnrollmentStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DepositInsuranceEnrollmentStatus status) => status switch
    {
        DepositInsuranceEnrollmentStatus.Active => "ACTIVE",
        DepositInsuranceEnrollmentStatus.Cancelled => "CANCELLED",
        DepositInsuranceEnrollmentStatus.Claimed => "CLAIMED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DepositInsuranceEnrollmentStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = DepositInsuranceEnrollmentStatus.Active;
                return true;
            case "CANCELLED":
                status = DepositInsuranceEnrollmentStatus.Cancelled;
                return true;
            case "CLAIMED":
                status = DepositInsuranceEnrollmentStatus.Claimed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositInsuranceEnrollmentStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DepositInsuranceEnrollmentStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositInsuranceEnrollmentStatusUnknown);
}

public enum DepositInsuranceReservationStatus
{
    Active = 1,
    Settled = 2,
}

public static class DepositInsuranceReservationStatusCatalog
{
    private static readonly StateTransitionTable<DepositInsuranceReservationStatus> Transitions =
        StateTransitionTable<DepositInsuranceReservationStatus>
            .Create(InvariantViolationCode.DepositInsuranceReservationTransitionInvalid)
            .AllowCreation(DepositInsuranceReservationStatus.Active)
            .Allow(
                DepositInsuranceReservationStatus.Active,
                DepositInsuranceReservationStatus.Settled)
            .Build();

    public static bool IsAllowed(DepositInsuranceReservationStatus from, DepositInsuranceReservationStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(DepositInsuranceReservationStatus from, DepositInsuranceReservationStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DepositInsuranceReservationStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DepositInsuranceReservationStatus status) => status switch
    {
        DepositInsuranceReservationStatus.Active => "ACTIVE",
        DepositInsuranceReservationStatus.Settled => "SETTLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DepositInsuranceReservationStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = DepositInsuranceReservationStatus.Active;
                return true;
            case "SETTLED":
                status = DepositInsuranceReservationStatus.Settled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositInsuranceReservationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DepositInsuranceReservationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositInsuranceReservationStatusUnknown);
}

public enum InsuranceSettlementWalletStatus
{
    Active = 1,
    Closed = 2,
}

public static class InsuranceSettlementWalletStatusCatalog
{
    private static readonly StateTransitionTable<InsuranceSettlementWalletStatus> Transitions =
        StateTransitionTable<InsuranceSettlementWalletStatus>
            .Create(InvariantViolationCode.InsuranceSettlementWalletTransitionInvalid)
            .AllowCreation(InsuranceSettlementWalletStatus.Active)
            .Allow(
                InsuranceSettlementWalletStatus.Active,
                InsuranceSettlementWalletStatus.Closed)
            .Allow(
                InsuranceSettlementWalletStatus.Closed,
                InsuranceSettlementWalletStatus.Active)
            .Build();

    public static bool IsAllowed(InsuranceSettlementWalletStatus from, InsuranceSettlementWalletStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(InsuranceSettlementWalletStatus from, InsuranceSettlementWalletStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(InsuranceSettlementWalletStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this InsuranceSettlementWalletStatus status) => status switch
    {
        InsuranceSettlementWalletStatus.Active => "ACTIVE",
        InsuranceSettlementWalletStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out InsuranceSettlementWalletStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = InsuranceSettlementWalletStatus.Active;
                return true;
            case "CLOSED":
                status = InsuranceSettlementWalletStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static InsuranceSettlementWalletStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out InsuranceSettlementWalletStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.InsuranceSettlementWalletStatusUnknown);
}

public enum DepositInsuranceClaimStatus
{
    Calculated = 1,
    Approved = 2,
    Paid = 3,
    Rejected = 4,
}

public static class DepositInsuranceClaimStatusCatalog
{
    private static readonly StateTransitionTable<DepositInsuranceClaimStatus> Transitions =
        StateTransitionTable<DepositInsuranceClaimStatus>
            .Create(InvariantViolationCode.DepositInsuranceClaimTransitionInvalid)
            .AllowCreation(DepositInsuranceClaimStatus.Calculated)
            .Allow(
                DepositInsuranceClaimStatus.Calculated,
                DepositInsuranceClaimStatus.Approved,
                DepositInsuranceClaimStatus.Rejected)
            .Allow(
                DepositInsuranceClaimStatus.Approved,
                DepositInsuranceClaimStatus.Paid)
            .Build();

    public static bool IsAllowed(DepositInsuranceClaimStatus from, DepositInsuranceClaimStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(DepositInsuranceClaimStatus from, DepositInsuranceClaimStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DepositInsuranceClaimStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DepositInsuranceClaimStatus status) => status switch
    {
        DepositInsuranceClaimStatus.Calculated => "CALCULATED",
        DepositInsuranceClaimStatus.Approved => "APPROVED",
        DepositInsuranceClaimStatus.Paid => "PAID",
        DepositInsuranceClaimStatus.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DepositInsuranceClaimStatus status)
    {
        switch (token)
        {
            case "CALCULATED":
                status = DepositInsuranceClaimStatus.Calculated;
                return true;
            case "APPROVED":
                status = DepositInsuranceClaimStatus.Approved;
                return true;
            case "PAID":
                status = DepositInsuranceClaimStatus.Paid;
                return true;
            case "REJECTED":
                status = DepositInsuranceClaimStatus.Rejected;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositInsuranceClaimStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DepositInsuranceClaimStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositInsuranceClaimStatusUnknown);
}
