using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum MerchantProductStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Retired = 4,
}

public static class MerchantProductStatusCatalog
{
    private static readonly StateTransitionTable<MerchantProductStatus> Transitions =
        StateTransitionTable<MerchantProductStatus>
            .Create(InvariantViolationCode.MerchantProductTransitionInvalid)
            .AllowCreation(MerchantProductStatus.Draft)
            .Allow(
                MerchantProductStatus.Draft,
                MerchantProductStatus.Active,
                MerchantProductStatus.Retired)
            .Allow(
                MerchantProductStatus.Active,
                MerchantProductStatus.Suspended,
                MerchantProductStatus.Retired)
            .Allow(
                MerchantProductStatus.Suspended,
                MerchantProductStatus.Active,
                MerchantProductStatus.Retired)
            .Build();

    public static void EnsureTransition(MerchantProductStatus from, MerchantProductStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantProductStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantProductStatus status) => status switch
    {
        MerchantProductStatus.Draft => "DRAFT",
        MerchantProductStatus.Active => "ACTIVE",
        MerchantProductStatus.Suspended => "SUSPENDED",
        MerchantProductStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantProductStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = MerchantProductStatus.Draft;
                return true;
            case "ACTIVE":
                status = MerchantProductStatus.Active;
                return true;
            case "SUSPENDED":
                status = MerchantProductStatus.Suspended;
                return true;
            case "RETIRED":
                status = MerchantProductStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantProductStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantProductStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantProductStatusUnknown);
}

public enum MerchantProductPriceVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class MerchantProductPriceVersionStatusCatalog
{
    private static readonly StateTransitionTable<MerchantProductPriceVersionStatus> Transitions =
        StateTransitionTable<MerchantProductPriceVersionStatus>
            .Create(InvariantViolationCode.MerchantProductPriceVersionTransitionInvalid)
            .AllowCreation(MerchantProductPriceVersionStatus.Draft)
            .Allow(
                MerchantProductPriceVersionStatus.Draft,
                MerchantProductPriceVersionStatus.Published,
                MerchantProductPriceVersionStatus.Retired)
            .Allow(
                MerchantProductPriceVersionStatus.Published,
                MerchantProductPriceVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(MerchantProductPriceVersionStatus from, MerchantProductPriceVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantProductPriceVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantProductPriceVersionStatus status) => status switch
    {
        MerchantProductPriceVersionStatus.Draft => "DRAFT",
        MerchantProductPriceVersionStatus.Published => "PUBLISHED",
        MerchantProductPriceVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantProductPriceVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = MerchantProductPriceVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = MerchantProductPriceVersionStatus.Published;
                return true;
            case "RETIRED":
                status = MerchantProductPriceVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantProductPriceVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantProductPriceVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantProductPriceVersionStatusUnknown);
}

public enum MerchantProductPurchasePolicyVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class MerchantProductPurchasePolicyVersionStatusCatalog
{
    private static readonly StateTransitionTable<MerchantProductPurchasePolicyVersionStatus> Transitions =
        StateTransitionTable<MerchantProductPurchasePolicyVersionStatus>
            .Create(InvariantViolationCode.MerchantProductPurchasePolicyVersionTransitionInvalid)
            .AllowCreation(MerchantProductPurchasePolicyVersionStatus.Draft)
            .Allow(
                MerchantProductPurchasePolicyVersionStatus.Draft,
                MerchantProductPurchasePolicyVersionStatus.Published,
                MerchantProductPurchasePolicyVersionStatus.Retired)
            .Allow(
                MerchantProductPurchasePolicyVersionStatus.Published,
                MerchantProductPurchasePolicyVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(MerchantProductPurchasePolicyVersionStatus from, MerchantProductPurchasePolicyVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantProductPurchasePolicyVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantProductPurchasePolicyVersionStatus status) => status switch
    {
        MerchantProductPurchasePolicyVersionStatus.Draft => "DRAFT",
        MerchantProductPurchasePolicyVersionStatus.Published => "PUBLISHED",
        MerchantProductPurchasePolicyVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantProductPurchasePolicyVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = MerchantProductPurchasePolicyVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = MerchantProductPurchasePolicyVersionStatus.Published;
                return true;
            case "RETIRED":
                status = MerchantProductPurchasePolicyVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantProductPurchasePolicyVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantProductPurchasePolicyVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantProductPurchasePolicyVersionStatusUnknown);
}

public enum MerchantFulfillmentPolicyVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class MerchantFulfillmentPolicyVersionStatusCatalog
{
    private static readonly StateTransitionTable<MerchantFulfillmentPolicyVersionStatus> Transitions =
        StateTransitionTable<MerchantFulfillmentPolicyVersionStatus>
            .Create(InvariantViolationCode.MerchantFulfillmentPolicyVersionTransitionInvalid)
            .AllowCreation(MerchantFulfillmentPolicyVersionStatus.Draft)
            .Allow(
                MerchantFulfillmentPolicyVersionStatus.Draft,
                MerchantFulfillmentPolicyVersionStatus.Published,
                MerchantFulfillmentPolicyVersionStatus.Retired)
            .Allow(
                MerchantFulfillmentPolicyVersionStatus.Published,
                MerchantFulfillmentPolicyVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(MerchantFulfillmentPolicyVersionStatus from, MerchantFulfillmentPolicyVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantFulfillmentPolicyVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantFulfillmentPolicyVersionStatus status) => status switch
    {
        MerchantFulfillmentPolicyVersionStatus.Draft => "DRAFT",
        MerchantFulfillmentPolicyVersionStatus.Published => "PUBLISHED",
        MerchantFulfillmentPolicyVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantFulfillmentPolicyVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = MerchantFulfillmentPolicyVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = MerchantFulfillmentPolicyVersionStatus.Published;
                return true;
            case "RETIRED":
                status = MerchantFulfillmentPolicyVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantFulfillmentPolicyVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantFulfillmentPolicyVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantFulfillmentPolicyVersionStatusUnknown);
}

public enum MerchantAftercarePolicyVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

public static class MerchantAftercarePolicyVersionStatusCatalog
{
    private static readonly StateTransitionTable<MerchantAftercarePolicyVersionStatus> Transitions =
        StateTransitionTable<MerchantAftercarePolicyVersionStatus>
            .Create(InvariantViolationCode.MerchantAftercarePolicyVersionTransitionInvalid)
            .AllowCreation(MerchantAftercarePolicyVersionStatus.Draft)
            .Allow(
                MerchantAftercarePolicyVersionStatus.Draft,
                MerchantAftercarePolicyVersionStatus.Published,
                MerchantAftercarePolicyVersionStatus.Retired)
            .Allow(
                MerchantAftercarePolicyVersionStatus.Published,
                MerchantAftercarePolicyVersionStatus.Retired)
            .Build();

    public static void EnsureTransition(MerchantAftercarePolicyVersionStatus from, MerchantAftercarePolicyVersionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(MerchantAftercarePolicyVersionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this MerchantAftercarePolicyVersionStatus status) => status switch
    {
        MerchantAftercarePolicyVersionStatus.Draft => "DRAFT",
        MerchantAftercarePolicyVersionStatus.Published => "PUBLISHED",
        MerchantAftercarePolicyVersionStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out MerchantAftercarePolicyVersionStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = MerchantAftercarePolicyVersionStatus.Draft;
                return true;
            case "PUBLISHED":
                status = MerchantAftercarePolicyVersionStatus.Published;
                return true;
            case "RETIRED":
                status = MerchantAftercarePolicyVersionStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static MerchantAftercarePolicyVersionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out MerchantAftercarePolicyVersionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.MerchantAftercarePolicyVersionStatusUnknown);
}

public enum CommerceOrderStatus
{
    Created = 1,
    AwaitingConfirmation = 2,
    Processing = 3,
    Paid = 4,
    PartiallyRefunded = 5,
    Refunded = 6,
    Cancelled = 7,
    Failed = 8,
}

public static class CommerceOrderStatusCatalog
{
    private static readonly StateTransitionTable<CommerceOrderStatus> Transitions =
        StateTransitionTable<CommerceOrderStatus>
            .Create(InvariantViolationCode.CommerceOrderTransitionInvalid)
            .AllowCreation(CommerceOrderStatus.Created)
            .Allow(
                CommerceOrderStatus.Created,
                CommerceOrderStatus.AwaitingConfirmation,
                CommerceOrderStatus.Cancelled)
            .Allow(
                CommerceOrderStatus.AwaitingConfirmation,
                CommerceOrderStatus.Processing,
                CommerceOrderStatus.Cancelled,
                CommerceOrderStatus.Failed)
            .Allow(
                CommerceOrderStatus.Processing,
                CommerceOrderStatus.Paid)
            .Allow(
                CommerceOrderStatus.Paid,
                CommerceOrderStatus.PartiallyRefunded,
                CommerceOrderStatus.Refunded)
            .Allow(
                CommerceOrderStatus.PartiallyRefunded,
                CommerceOrderStatus.Refunded)
            .Build();

    public static void EnsureTransition(CommerceOrderStatus from, CommerceOrderStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CommerceOrderStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CommerceOrderStatus status) => status switch
    {
        CommerceOrderStatus.Created => "CREATED",
        CommerceOrderStatus.AwaitingConfirmation => "AWAITING_CONFIRMATION",
        CommerceOrderStatus.Processing => "PROCESSING",
        CommerceOrderStatus.Paid => "PAID",
        CommerceOrderStatus.PartiallyRefunded => "PARTIALLY_REFUNDED",
        CommerceOrderStatus.Refunded => "REFUNDED",
        CommerceOrderStatus.Cancelled => "CANCELLED",
        CommerceOrderStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CommerceOrderStatus status)
    {
        switch (token)
        {
            case "CREATED":
                status = CommerceOrderStatus.Created;
                return true;
            case "AWAITING_CONFIRMATION":
                status = CommerceOrderStatus.AwaitingConfirmation;
                return true;
            case "PROCESSING":
                status = CommerceOrderStatus.Processing;
                return true;
            case "PAID":
                status = CommerceOrderStatus.Paid;
                return true;
            case "PARTIALLY_REFUNDED":
                status = CommerceOrderStatus.PartiallyRefunded;
                return true;
            case "REFUNDED":
                status = CommerceOrderStatus.Refunded;
                return true;
            case "CANCELLED":
                status = CommerceOrderStatus.Cancelled;
                return true;
            case "FAILED":
                status = CommerceOrderStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CommerceOrderStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CommerceOrderStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CommerceOrderStatusUnknown);
}

public enum CommercePaymentStatus
{
    Pending = 1,
    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
    Cancelled = 5,
    Failed = 6,
}

public static class CommercePaymentStatusCatalog
{
    private static readonly StateTransitionTable<CommercePaymentStatus> Transitions =
        StateTransitionTable<CommercePaymentStatus>
            .Create(InvariantViolationCode.CommercePaymentTransitionInvalid)
            .AllowCreation(CommercePaymentStatus.Pending)
            .Allow(
                CommercePaymentStatus.Pending,
                CommercePaymentStatus.Paid,
                CommercePaymentStatus.Cancelled,
                CommercePaymentStatus.Failed)
            .Allow(
                CommercePaymentStatus.Paid,
                CommercePaymentStatus.PartiallyRefunded,
                CommercePaymentStatus.Refunded)
            .Allow(
                CommercePaymentStatus.PartiallyRefunded,
                CommercePaymentStatus.Refunded)
            .Build();

    public static void EnsureTransition(CommercePaymentStatus from, CommercePaymentStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CommercePaymentStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CommercePaymentStatus status) => status switch
    {
        CommercePaymentStatus.Pending => "PENDING",
        CommercePaymentStatus.Paid => "PAID",
        CommercePaymentStatus.PartiallyRefunded => "PARTIALLY_REFUNDED",
        CommercePaymentStatus.Refunded => "REFUNDED",
        CommercePaymentStatus.Cancelled => "CANCELLED",
        CommercePaymentStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CommercePaymentStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = CommercePaymentStatus.Pending;
                return true;
            case "PAID":
                status = CommercePaymentStatus.Paid;
                return true;
            case "PARTIALLY_REFUNDED":
                status = CommercePaymentStatus.PartiallyRefunded;
                return true;
            case "REFUNDED":
                status = CommercePaymentStatus.Refunded;
                return true;
            case "CANCELLED":
                status = CommercePaymentStatus.Cancelled;
                return true;
            case "FAILED":
                status = CommercePaymentStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CommercePaymentStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CommercePaymentStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CommercePaymentStatusUnknown);
}

public enum CommerceReturnStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Completed = 5,
}

public static class CommerceReturnStatusCatalog
{
    private static readonly StateTransitionTable<CommerceReturnStatus> Transitions =
        StateTransitionTable<CommerceReturnStatus>
            .Create(InvariantViolationCode.CommerceReturnTransitionInvalid)
            .AllowCreation(CommerceReturnStatus.Pending)
            .Allow(
                CommerceReturnStatus.Pending,
                CommerceReturnStatus.Approved,
                CommerceReturnStatus.Rejected,
                CommerceReturnStatus.Cancelled)
            .Allow(
                CommerceReturnStatus.Approved,
                CommerceReturnStatus.Cancelled,
                CommerceReturnStatus.Completed)
            .Build();

    public static void EnsureTransition(CommerceReturnStatus from, CommerceReturnStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CommerceReturnStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CommerceReturnStatus status) => status switch
    {
        CommerceReturnStatus.Pending => "PENDING",
        CommerceReturnStatus.Approved => "APPROVED",
        CommerceReturnStatus.Rejected => "REJECTED",
        CommerceReturnStatus.Cancelled => "CANCELLED",
        CommerceReturnStatus.Completed => "COMPLETED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CommerceReturnStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = CommerceReturnStatus.Pending;
                return true;
            case "APPROVED":
                status = CommerceReturnStatus.Approved;
                return true;
            case "REJECTED":
                status = CommerceReturnStatus.Rejected;
                return true;
            case "CANCELLED":
                status = CommerceReturnStatus.Cancelled;
                return true;
            case "COMPLETED":
                status = CommerceReturnStatus.Completed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CommerceReturnStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CommerceReturnStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CommerceReturnStatusUnknown);
}

public enum CommerceFulfillmentStatus
{
    Pending = 1,
    Succeeded = 2,
    CancelledReturned = 3,
    FailedRetryable = 4,
    FailedManual = 5,
}

public static class CommerceFulfillmentStatusCatalog
{
    private static readonly StateTransitionTable<CommerceFulfillmentStatus> Transitions =
        StateTransitionTable<CommerceFulfillmentStatus>
            .Create(InvariantViolationCode.CommerceFulfillmentTransitionInvalid)
            .AllowCreation(CommerceFulfillmentStatus.Pending)
            .Allow(
                CommerceFulfillmentStatus.Pending,
                CommerceFulfillmentStatus.Succeeded,
                CommerceFulfillmentStatus.CancelledReturned,
                CommerceFulfillmentStatus.FailedRetryable,
                CommerceFulfillmentStatus.FailedManual)
            .Allow(
                CommerceFulfillmentStatus.FailedRetryable,
                CommerceFulfillmentStatus.Pending)
            .Allow(
                CommerceFulfillmentStatus.FailedManual,
                CommerceFulfillmentStatus.Pending)
            .Build();

    public static void EnsureTransition(CommerceFulfillmentStatus from, CommerceFulfillmentStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CommerceFulfillmentStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CommerceFulfillmentStatus status) => status switch
    {
        CommerceFulfillmentStatus.Pending => "PENDING",
        CommerceFulfillmentStatus.Succeeded => "SUCCEEDED",
        CommerceFulfillmentStatus.CancelledReturned => "CANCELLED_RETURNED",
        CommerceFulfillmentStatus.FailedRetryable => "FAILED_RETRYABLE",
        CommerceFulfillmentStatus.FailedManual => "FAILED_MANUAL",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CommerceFulfillmentStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = CommerceFulfillmentStatus.Pending;
                return true;
            case "SUCCEEDED":
                status = CommerceFulfillmentStatus.Succeeded;
                return true;
            case "CANCELLED_RETURNED":
                status = CommerceFulfillmentStatus.CancelledReturned;
                return true;
            case "FAILED_RETRYABLE":
                status = CommerceFulfillmentStatus.FailedRetryable;
                return true;
            case "FAILED_MANUAL":
                status = CommerceFulfillmentStatus.FailedManual;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CommerceFulfillmentStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CommerceFulfillmentStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CommerceFulfillmentStatusUnknown);
}

public enum CommerceFulfillmentReversalStatus
{
    Pending = 1,
    Succeeded = 2,
    FailedRetryable = 3,
    FailedManual = 4,
}

public static class CommerceFulfillmentReversalStatusCatalog
{
    private static readonly StateTransitionTable<CommerceFulfillmentReversalStatus> Transitions =
        StateTransitionTable<CommerceFulfillmentReversalStatus>
            .Create(InvariantViolationCode.CommerceFulfillmentReversalTransitionInvalid)
            .AllowCreation(CommerceFulfillmentReversalStatus.Pending)
            .Allow(
                CommerceFulfillmentReversalStatus.Pending,
                CommerceFulfillmentReversalStatus.Succeeded,
                CommerceFulfillmentReversalStatus.FailedRetryable,
                CommerceFulfillmentReversalStatus.FailedManual)
            .Allow(
                CommerceFulfillmentReversalStatus.FailedRetryable,
                CommerceFulfillmentReversalStatus.Pending)
            .Allow(
                CommerceFulfillmentReversalStatus.FailedManual,
                CommerceFulfillmentReversalStatus.Pending)
            .Build();

    public static void EnsureTransition(CommerceFulfillmentReversalStatus from, CommerceFulfillmentReversalStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CommerceFulfillmentReversalStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CommerceFulfillmentReversalStatus status) => status switch
    {
        CommerceFulfillmentReversalStatus.Pending => "PENDING",
        CommerceFulfillmentReversalStatus.Succeeded => "SUCCEEDED",
        CommerceFulfillmentReversalStatus.FailedRetryable => "FAILED_RETRYABLE",
        CommerceFulfillmentReversalStatus.FailedManual => "FAILED_MANUAL",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CommerceFulfillmentReversalStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = CommerceFulfillmentReversalStatus.Pending;
                return true;
            case "SUCCEEDED":
                status = CommerceFulfillmentReversalStatus.Succeeded;
                return true;
            case "FAILED_RETRYABLE":
                status = CommerceFulfillmentReversalStatus.FailedRetryable;
                return true;
            case "FAILED_MANUAL":
                status = CommerceFulfillmentReversalStatus.FailedManual;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CommerceFulfillmentReversalStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CommerceFulfillmentReversalStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CommerceFulfillmentReversalStatusUnknown);
}

public enum DebitCardAuthorizationStatus
{
    Authorized = 1,
    PartiallyCaptured = 2,
    Captured = 3,
    PartiallyRefunded = 4,
    Refunded = 5,
    Reversed = 6,
    Expired = 7,
    Declined = 8,
}

public static class DebitCardAuthorizationStatusCatalog
{
    private static readonly StateTransitionTable<DebitCardAuthorizationStatus> Transitions =
        StateTransitionTable<DebitCardAuthorizationStatus>
            .Create(InvariantViolationCode.DebitCardAuthorizationTransitionInvalid)
            .AllowCreation(
                DebitCardAuthorizationStatus.Authorized,
                DebitCardAuthorizationStatus.Declined)
            .Allow(
                DebitCardAuthorizationStatus.Authorized,
                DebitCardAuthorizationStatus.PartiallyCaptured,
                DebitCardAuthorizationStatus.Captured,
                DebitCardAuthorizationStatus.Reversed,
                DebitCardAuthorizationStatus.Expired)
            .Allow(
                DebitCardAuthorizationStatus.PartiallyCaptured,
                DebitCardAuthorizationStatus.Captured)
            .Allow(
                DebitCardAuthorizationStatus.Captured,
                DebitCardAuthorizationStatus.PartiallyRefunded,
                DebitCardAuthorizationStatus.Refunded)
            .Allow(
                DebitCardAuthorizationStatus.PartiallyRefunded,
                DebitCardAuthorizationStatus.Refunded)
            .Build();

    public static void EnsureTransition(DebitCardAuthorizationStatus from, DebitCardAuthorizationStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(DebitCardAuthorizationStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this DebitCardAuthorizationStatus status) => status switch
    {
        DebitCardAuthorizationStatus.Authorized => "AUTHORIZED",
        DebitCardAuthorizationStatus.PartiallyCaptured => "PARTIALLY_CAPTURED",
        DebitCardAuthorizationStatus.Captured => "CAPTURED",
        DebitCardAuthorizationStatus.PartiallyRefunded => "PARTIALLY_REFUNDED",
        DebitCardAuthorizationStatus.Refunded => "REFUNDED",
        DebitCardAuthorizationStatus.Reversed => "REVERSED",
        DebitCardAuthorizationStatus.Expired => "EXPIRED",
        DebitCardAuthorizationStatus.Declined => "DECLINED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DebitCardAuthorizationStatus status)
    {
        switch (token)
        {
            case "AUTHORIZED":
                status = DebitCardAuthorizationStatus.Authorized;
                return true;
            case "PARTIALLY_CAPTURED":
                status = DebitCardAuthorizationStatus.PartiallyCaptured;
                return true;
            case "CAPTURED":
                status = DebitCardAuthorizationStatus.Captured;
                return true;
            case "PARTIALLY_REFUNDED":
                status = DebitCardAuthorizationStatus.PartiallyRefunded;
                return true;
            case "REFUNDED":
                status = DebitCardAuthorizationStatus.Refunded;
                return true;
            case "REVERSED":
                status = DebitCardAuthorizationStatus.Reversed;
                return true;
            case "EXPIRED":
                status = DebitCardAuthorizationStatus.Expired;
                return true;
            case "DECLINED":
                status = DebitCardAuthorizationStatus.Declined;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DebitCardAuthorizationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DebitCardAuthorizationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DebitCardAuthorizationStatusUnknown);
}
