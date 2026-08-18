using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum ScheduledPaymentKind
{
    Once = 1,
    Weekly = 2,
    Monthly = 3,
}

public enum ScheduledPaymentPlanStatus
{
    Active = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
}

public sealed class ScheduledPaymentPlan : VersionedEntity
{
    private static readonly StateTransitionTable<ScheduledPaymentPlanStatus> Transitions =
        StateTransitionTable<ScheduledPaymentPlanStatus>
            .Create(InvariantViolationCode.ScheduledPaymentPlanTransitionInvalid)
            .AllowCreation(ScheduledPaymentPlanStatus.Active)
            .Allow(
                ScheduledPaymentPlanStatus.Active,
                ScheduledPaymentPlanStatus.Paused,
                ScheduledPaymentPlanStatus.Completed,
                ScheduledPaymentPlanStatus.Cancelled)
            .Allow(
                ScheduledPaymentPlanStatus.Paused,
                ScheduledPaymentPlanStatus.Active,
                ScheduledPaymentPlanStatus.Cancelled)
            .Build();

    private ScheduledPaymentPlan(
        ScheduledPaymentPlanId id,
        CustomerAccountId customerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        SavedBeneficiaryId? savedBeneficiaryId,
        CurrencyId currencyId,
        ScheduledPaymentKind kind,
        ScheduledPaymentPlanStatus status,
        MoneyMinor amount,
        int? anchorDayOfMonth,
        string canonicalTimezone,
        UtcTimestamp? nextDueAt,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        CustomerAccountId = customerAccountId;
        SourceDepositAccountId = sourceDepositAccountId;
        DestinationDepositAccountId = destinationDepositAccountId;
        SavedBeneficiaryId = savedBeneficiaryId;
        CurrencyId = currencyId;
        Kind = kind;
        Status = status;
        Amount = amount;
        AnchorDayOfMonth = anchorDayOfMonth;
        CanonicalTimezone = canonicalTimezone;
        NextDueAt = nextDueAt;
        CreatedAt = createdAt;
    }

    public ScheduledPaymentPlanId Id { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public DepositAccountId SourceDepositAccountId { get; }

    public DepositAccountId DestinationDepositAccountId { get; }

    public SavedBeneficiaryId? SavedBeneficiaryId { get; }

    public CurrencyId CurrencyId { get; }

    public ScheduledPaymentKind Kind { get; }

    public ScheduledPaymentPlanStatus Status { get; private set; }

    public MoneyMinor Amount { get; }

    public int? AnchorDayOfMonth { get; }

    public string CanonicalTimezone { get; }

    public UtcTimestamp? NextDueAt { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public static ScheduledPaymentPlan Create(
        ScheduledPaymentPlanId id,
        CustomerAccountId customerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        SavedBeneficiaryId? savedBeneficiaryId,
        CurrencyId currencyId,
        ScheduledPaymentKind kind,
        MoneyMinor amount,
        int? anchorDayOfMonth,
        string canonicalTimezone,
        UtcTimestamp nextDueAt,
        UtcTimestamp createdAt)
    {
        if (!amount.IsPositive)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ScheduledPaymentAmountInvalid);
        }

        if (sourceDepositAccountId == destinationDepositAccountId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ScheduledPaymentRouteInvalid);
        }

        if (kind == ScheduledPaymentKind.Monthly
            ? anchorDayOfMonth is not (>= 1 and <= 31)
            : anchorDayOfMonth is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ScheduledPaymentAnchorInvalid);
        }

        if (string.IsNullOrWhiteSpace(canonicalTimezone))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ScheduledPaymentTimezoneInvalid);
        }

        return new ScheduledPaymentPlan(
            id,
            customerAccountId,
            sourceDepositAccountId,
            destinationDepositAccountId,
            savedBeneficiaryId,
            currencyId,
            kind,
            ScheduledPaymentPlanStatus.Active,
            amount,
            anchorDayOfMonth,
            canonicalTimezone,
            nextDueAt,
            createdAt,
            InitialVersion);
    }

    public static ScheduledPaymentPlan Rehydrate(
        ScheduledPaymentPlanId id,
        CustomerAccountId customerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        SavedBeneficiaryId? savedBeneficiaryId,
        CurrencyId currencyId,
        ScheduledPaymentKind kind,
        ScheduledPaymentPlanStatus status,
        MoneyMinor amount,
        int? anchorDayOfMonth,
        string canonicalTimezone,
        UtcTimestamp? nextDueAt,
        UtcTimestamp createdAt,
        long version) =>
        new(
            id,
            customerAccountId,
            sourceDepositAccountId,
            destinationDepositAccountId,
            savedBeneficiaryId,
            currencyId,
            kind,
            status,
            amount,
            anchorDayOfMonth,
            canonicalTimezone,
            nextDueAt,
            createdAt,
            version);

    public void Pause()
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentPlanStatus.Paused);

        Status = ScheduledPaymentPlanStatus.Paused;
        AdvanceVersion();
    }

    public void Resume(UtcTimestamp nextDueAt)
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentPlanStatus.Active);

        Status = ScheduledPaymentPlanStatus.Active;
        NextDueAt = nextDueAt;
        AdvanceVersion();
    }

    public void Cancel()
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentPlanStatus.Cancelled);

        Status = ScheduledPaymentPlanStatus.Cancelled;
        NextDueAt = null;
        AdvanceVersion();
    }

    public void Complete()
    {
        Transitions.EnsureAllowed(Status, ScheduledPaymentPlanStatus.Completed);

        Status = ScheduledPaymentPlanStatus.Completed;
        NextDueAt = null;
        AdvanceVersion();
    }

    public void Advance(UtcTimestamp nextDueAt)
    {
        if (Status != ScheduledPaymentPlanStatus.Active)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.ScheduledPaymentPlanTransitionInvalid);
        }

        NextDueAt = nextDueAt;
        AdvanceVersion();
    }
}

public static class ScheduledPaymentPlanCatalog
{
    public static string ToToken(this ScheduledPaymentPlanStatus status) => status switch
    {
        ScheduledPaymentPlanStatus.Active => "ACTIVE",
        ScheduledPaymentPlanStatus.Paused => "PAUSED",
        ScheduledPaymentPlanStatus.Completed => "COMPLETED",
        ScheduledPaymentPlanStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this ScheduledPaymentKind kind) => kind switch
    {
        ScheduledPaymentKind.Once => "ONCE",
        ScheduledPaymentKind.Weekly => "WEEKLY",
        ScheduledPaymentKind.Monthly => "MONTHLY",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ScheduledPaymentPlanStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = ScheduledPaymentPlanStatus.Active;
                return true;
            case "PAUSED":
                status = ScheduledPaymentPlanStatus.Paused;
                return true;
            case "COMPLETED":
                status = ScheduledPaymentPlanStatus.Completed;
                return true;
            case "CANCELLED":
                status = ScheduledPaymentPlanStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static bool TryParseKindToken(ReadOnlySpan<char> token, out ScheduledPaymentKind kind)
    {
        switch (token)
        {
            case "ONCE":
                kind = ScheduledPaymentKind.Once;
                return true;
            case "WEEKLY":
                kind = ScheduledPaymentKind.Weekly;
                return true;
            case "MONTHLY":
                kind = ScheduledPaymentKind.Monthly;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static ScheduledPaymentPlanStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ScheduledPaymentPlanStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.ScheduledPaymentPlanStatusUnknown);

    public static ScheduledPaymentKind ParseKindToken(ReadOnlySpan<char> token) =>
        TryParseKindToken(token, out ScheduledPaymentKind kind)
            ? kind
            : throw InvariantViolationException.Create(InvariantViolationCode.ScheduledPaymentKindUnknown);
}
