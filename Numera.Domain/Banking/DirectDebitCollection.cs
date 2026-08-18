using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum DirectDebitCollectionStatus
{
    Pending = 1,
    Executing = 2,
    Settled = 3,
    FailedFunds = 4,
    FailedMandate = 5,
    FailedAccount = 6,
    Cancelled = 7,
}

public sealed class DirectDebitCollection : VersionedEntity
{
    public const int MaximumReferenceLength = 64;

    private static readonly StateTransitionTable<DirectDebitCollectionStatus> Transitions =
        StateTransitionTable<DirectDebitCollectionStatus>
            .Create(InvariantViolationCode.DirectDebitCollectionTransitionInvalid)
            .AllowCreation(DirectDebitCollectionStatus.Pending)
            .Allow(
                DirectDebitCollectionStatus.Pending,
                DirectDebitCollectionStatus.Executing,
                DirectDebitCollectionStatus.Cancelled)
            .Allow(
                DirectDebitCollectionStatus.Executing,
                DirectDebitCollectionStatus.Settled,
                DirectDebitCollectionStatus.FailedFunds,
                DirectDebitCollectionStatus.FailedMandate,
                DirectDebitCollectionStatus.FailedAccount)
            .Build();

    private DirectDebitCollection(
        DirectDebitCollectionId id,
        DirectDebitMandateId mandateId,
        PaymentOrderId? paymentOrderId,
        string creditorCollectionReference,
        MoneyMinor amount,
        DirectDebitCollectionStatus status,
        UtcTimestamp scheduledFor,
        UtcTimestamp? completedAt,
        long version)
        : base(version)
    {
        Id = id;
        MandateId = mandateId;
        PaymentOrderId = paymentOrderId;
        CreditorCollectionReference = creditorCollectionReference;
        Amount = amount;
        Status = status;
        ScheduledFor = scheduledFor;
        CompletedAt = completedAt;
    }

    public DirectDebitCollectionId Id { get; }

    public DirectDebitMandateId MandateId { get; }

    public PaymentOrderId? PaymentOrderId { get; private set; }

    public string CreditorCollectionReference { get; }

    public MoneyMinor Amount { get; }

    public DirectDebitCollectionStatus Status { get; private set; }

    public UtcTimestamp ScheduledFor { get; }

    public UtcTimestamp? CompletedAt { get; private set; }

    public static DirectDebitCollection Request(
        DirectDebitCollectionId id,
        DirectDebitMandateId mandateId,
        string creditorCollectionReference,
        MoneyMinor amount,
        UtcTimestamp scheduledFor)
    {
        if (string.IsNullOrWhiteSpace(creditorCollectionReference)
            || creditorCollectionReference.Length > MaximumReferenceLength)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.DirectDebitCollectionReferenceInvalid);
        }

        if (!amount.IsPositive)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DirectDebitCollectionAmountInvalid);
        }

        return new DirectDebitCollection(
            id,
            mandateId,
            paymentOrderId: null,
            creditorCollectionReference,
            amount,
            DirectDebitCollectionStatus.Pending,
            scheduledFor,
            completedAt: null,
            InitialVersion);
    }

    public static DirectDebitCollection Rehydrate(
        DirectDebitCollectionId id,
        DirectDebitMandateId mandateId,
        PaymentOrderId? paymentOrderId,
        string creditorCollectionReference,
        MoneyMinor amount,
        DirectDebitCollectionStatus status,
        UtcTimestamp scheduledFor,
        UtcTimestamp? completedAt,
        long version) =>
        new(
            id,
            mandateId,
            paymentOrderId,
            creditorCollectionReference,
            amount,
            status,
            scheduledFor,
            completedAt,
            version);

    public void Claim()
    {
        Transitions.EnsureAllowed(Status, DirectDebitCollectionStatus.Executing);

        Status = DirectDebitCollectionStatus.Executing;
        AdvanceVersion();
    }

    public void Settle(PaymentOrderId paymentOrderId, UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DirectDebitCollectionStatus.Settled);

        Status = DirectDebitCollectionStatus.Settled;
        PaymentOrderId = paymentOrderId;
        CompletedAt = now;
        AdvanceVersion();
    }

    public void Fail(DirectDebitCollectionStatus status, UtcTimestamp now)
    {
        if (status is not (DirectDebitCollectionStatus.FailedFunds
            or DirectDebitCollectionStatus.FailedMandate
            or DirectDebitCollectionStatus.FailedAccount))
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.DirectDebitCollectionTransitionInvalid);
        }

        Transitions.EnsureAllowed(Status, status);

        Status = status;
        CompletedAt = now;
        AdvanceVersion();
    }

    public void Cancel(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DirectDebitCollectionStatus.Cancelled);

        Status = DirectDebitCollectionStatus.Cancelled;
        CompletedAt = now;
        AdvanceVersion();
    }
}

public static class DirectDebitCollectionCatalog
{
    public static string ToToken(this DirectDebitCollectionStatus status) => status switch
    {
        DirectDebitCollectionStatus.Pending => "PENDING",
        DirectDebitCollectionStatus.Executing => "EXECUTING",
        DirectDebitCollectionStatus.Settled => "SETTLED",
        DirectDebitCollectionStatus.FailedFunds => "FAILED_FUNDS",
        DirectDebitCollectionStatus.FailedMandate => "FAILED_MANDATE",
        DirectDebitCollectionStatus.FailedAccount => "FAILED_ACCOUNT",
        DirectDebitCollectionStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DirectDebitCollectionStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = DirectDebitCollectionStatus.Pending;
                return true;
            case "EXECUTING":
                status = DirectDebitCollectionStatus.Executing;
                return true;
            case "SETTLED":
                status = DirectDebitCollectionStatus.Settled;
                return true;
            case "FAILED_FUNDS":
                status = DirectDebitCollectionStatus.FailedFunds;
                return true;
            case "FAILED_MANDATE":
                status = DirectDebitCollectionStatus.FailedMandate;
                return true;
            case "FAILED_ACCOUNT":
                status = DirectDebitCollectionStatus.FailedAccount;
                return true;
            case "CANCELLED":
                status = DirectDebitCollectionStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DirectDebitCollectionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DirectDebitCollectionStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.DirectDebitCollectionStatusUnknown);
}
