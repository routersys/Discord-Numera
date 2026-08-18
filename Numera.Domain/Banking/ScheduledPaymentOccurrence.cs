using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum ScheduledPaymentOccurrenceStatus
{
    Pending = 1,
    Executing = 2,
    Succeeded = 3,
    FailedFunds = 4,
    FailedRestricted = 5,
    FailedDestination = 6,
    Cancelled = 7,
}

public sealed class ScheduledPaymentOccurrence : VersionedEntity
{
    private static readonly StateTransitionTable<ScheduledPaymentOccurrenceStatus> Transitions =
        StateTransitionTable<ScheduledPaymentOccurrenceStatus>
            .Create(InvariantViolationCode.ScheduledPaymentOccurrenceTransitionInvalid)
            .AllowCreation(ScheduledPaymentOccurrenceStatus.Pending)
            .Allow(
                ScheduledPaymentOccurrenceStatus.Pending,
                ScheduledPaymentOccurrenceStatus.Executing,
                ScheduledPaymentOccurrenceStatus.Cancelled)
            .Allow(
                ScheduledPaymentOccurrenceStatus.Executing,
                ScheduledPaymentOccurrenceStatus.Succeeded,
                ScheduledPaymentOccurrenceStatus.FailedFunds,
                ScheduledPaymentOccurrenceStatus.FailedRestricted,
                ScheduledPaymentOccurrenceStatus.FailedDestination)
            .Build();

    private ScheduledPaymentOccurrence(
        ScheduledPaymentOccurrenceId id,
        ScheduledPaymentPlanId planId,
        PaymentOrderId? paymentOrderId,
        UtcTimestamp scheduledFor,
        ScheduledPaymentOccurrenceStatus status,
        UtcTimestamp? attemptedAt,
        UtcTimestamp? completedAt,
        long version)
        : base(version)
    {
        Id = id;
        PlanId = planId;
        PaymentOrderId = paymentOrderId;
        ScheduledFor = scheduledFor;
        Status = status;
        AttemptedAt = attemptedAt;
        CompletedAt = completedAt;
    }

    public ScheduledPaymentOccurrenceId Id { get; }

    public ScheduledPaymentPlanId PlanId { get; }

    public PaymentOrderId? PaymentOrderId { get; private set; }

    public UtcTimestamp ScheduledFor { get; }

    public ScheduledPaymentOccurrenceStatus Status { get; private set; }

    public UtcTimestamp? AttemptedAt { get; private set; }

    public UtcTimestamp? CompletedAt { get; private set; }

    public static ScheduledPaymentOccurrence Schedule(
        ScheduledPaymentOccurrenceId id,
        ScheduledPaymentPlanId planId,
        UtcTimestamp scheduledFor) =>
        new(
            id,
            planId,
            paymentOrderId: null,
            scheduledFor,
            ScheduledPaymentOccurrenceStatus.Pending,
            attemptedAt: null,
            completedAt: null,
            InitialVersion);

    public static ScheduledPaymentOccurrence Rehydrate(
        ScheduledPaymentOccurrenceId id,
        ScheduledPaymentPlanId planId,
        PaymentOrderId? paymentOrderId,
        UtcTimestamp scheduledFor,
        ScheduledPaymentOccurrenceStatus status,
        UtcTimestamp? attemptedAt,
        UtcTimestamp? completedAt,
        long version) =>
        new(id, planId, paymentOrderId, scheduledFor, status, attemptedAt, completedAt, version);

    public void Claim(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentOccurrenceStatus.Executing);

        Status = ScheduledPaymentOccurrenceStatus.Executing;
        AttemptedAt = now;
        AdvanceVersion();
    }

    public void Succeed(PaymentOrderId paymentOrderId, UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentOccurrenceStatus.Succeeded);

        Status = ScheduledPaymentOccurrenceStatus.Succeeded;
        PaymentOrderId = paymentOrderId;
        CompletedAt = now;
        AdvanceVersion();
    }

    public void Fail(ScheduledPaymentOccurrenceStatus status, UtcTimestamp now)
    {
        if (status is not (ScheduledPaymentOccurrenceStatus.FailedFunds
            or ScheduledPaymentOccurrenceStatus.FailedRestricted
            or ScheduledPaymentOccurrenceStatus.FailedDestination))
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.ScheduledPaymentOccurrenceTransitionInvalid);
        }

        Transitions.EnsureAllowed(Status, status);

        Status = status;
        CompletedAt = now;
        AdvanceVersion();
    }

    public void Cancel(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentOccurrenceStatus.Cancelled);

        Status = ScheduledPaymentOccurrenceStatus.Cancelled;
        CompletedAt = now;
        AdvanceVersion();
    }
}

public static class ScheduledPaymentOccurrenceCatalog
{
    public static string ToToken(this ScheduledPaymentOccurrenceStatus status) => status switch
    {
        ScheduledPaymentOccurrenceStatus.Pending => "PENDING",
        ScheduledPaymentOccurrenceStatus.Executing => "EXECUTING",
        ScheduledPaymentOccurrenceStatus.Succeeded => "SUCCEEDED",
        ScheduledPaymentOccurrenceStatus.FailedFunds => "FAILED_FUNDS",
        ScheduledPaymentOccurrenceStatus.FailedRestricted => "FAILED_RESTRICTED",
        ScheduledPaymentOccurrenceStatus.FailedDestination => "FAILED_DESTINATION",
        ScheduledPaymentOccurrenceStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ScheduledPaymentOccurrenceStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = ScheduledPaymentOccurrenceStatus.Pending;
                return true;
            case "EXECUTING":
                status = ScheduledPaymentOccurrenceStatus.Executing;
                return true;
            case "SUCCEEDED":
                status = ScheduledPaymentOccurrenceStatus.Succeeded;
                return true;
            case "FAILED_FUNDS":
                status = ScheduledPaymentOccurrenceStatus.FailedFunds;
                return true;
            case "FAILED_RESTRICTED":
                status = ScheduledPaymentOccurrenceStatus.FailedRestricted;
                return true;
            case "FAILED_DESTINATION":
                status = ScheduledPaymentOccurrenceStatus.FailedDestination;
                return true;
            case "CANCELLED":
                status = ScheduledPaymentOccurrenceStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static ScheduledPaymentOccurrenceStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ScheduledPaymentOccurrenceStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.ScheduledPaymentOccurrenceStatusUnknown);
}
