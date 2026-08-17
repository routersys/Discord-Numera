namespace Numera.Domain.Banking;

public enum SettlementInstructionStatus
{
    Created = 1,
    Queued = 2,
    LockedForSettlement = 3,
    Settled = 4,
    Cancelled = 5,
    Failed = 6,
}

public sealed class SettlementInstruction : VersionedEntity
{
    private static readonly StateTransitionTable<SettlementInstructionStatus> Transitions =
        StateTransitionTable<SettlementInstructionStatus>
            .Create(InvariantViolationCode.SettlementInstructionTransitionInvalid)
            .AllowCreation(SettlementInstructionStatus.Created)
            .Allow(
                SettlementInstructionStatus.Created,
                SettlementInstructionStatus.Queued,
                SettlementInstructionStatus.LockedForSettlement,
                SettlementInstructionStatus.Cancelled,
                SettlementInstructionStatus.Failed)
            .Allow(
                SettlementInstructionStatus.Queued,
                SettlementInstructionStatus.LockedForSettlement,
                SettlementInstructionStatus.Cancelled,
                SettlementInstructionStatus.Failed)
            .Allow(
                SettlementInstructionStatus.LockedForSettlement,
                SettlementInstructionStatus.Settled,
                SettlementInstructionStatus.Failed)
            .Build();

    private SettlementInstruction(
        SettlementInstructionId id,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        SettlementInstructionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? lockedAt,
        UtcTimestamp? settledAt,
        long version)
        : base(version)
    {
        Id = id;
        BusinessOperationId = businessOperationId;
        CurrencyId = currencyId;
        SourceBankId = sourceBankId;
        DestinationBankId = destinationBankId;
        Amount = amount;
        Status = status;
        CreatedAt = createdAt;
        LockedAt = lockedAt;
        SettledAt = settledAt;
    }

    public SettlementInstructionId Id { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public CurrencyId CurrencyId { get; }

    public BankId SourceBankId { get; }

    public BankId DestinationBankId { get; }

    public MoneyMinor Amount { get; }

    public SettlementInstructionStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? LockedAt { get; private set; }

    public UtcTimestamp? SettledAt { get; private set; }

    public bool IsFinal => Status == SettlementInstructionStatus.Settled;

    public static SettlementInstruction Create(
        SettlementInstructionId id,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(SettlementInstructionStatus.Created);

        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.SettlementInstructionAmountInvalid);
        }

        if (sourceBankId == destinationBankId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.SettlementInstructionEndpointsInvalid);
        }

        return new SettlementInstruction(
            id,
            businessOperationId,
            currencyId,
            sourceBankId,
            destinationBankId,
            amount,
            SettlementInstructionStatus.Created,
            createdAt,
            lockedAt: null,
            settledAt: null,
            InitialVersion);
    }

    public static SettlementInstruction Rehydrate(
        SettlementInstructionId id,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        SettlementInstructionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? lockedAt,
        UtcTimestamp? settledAt,
        long version)
    {
        EnsureFinalityConsistent(status, lockedAt, settledAt);

        return new SettlementInstruction(
            id,
            businessOperationId,
            currencyId,
            sourceBankId,
            destinationBankId,
            amount,
            status,
            createdAt,
            lockedAt,
            settledAt,
            version);
    }

    public void Queue() => Advance(SettlementInstructionStatus.Queued);

    public void LockForSettlement(UtcTimestamp at)
    {
        Advance(SettlementInstructionStatus.LockedForSettlement);
        LockedAt = at;
    }

    public void Settle(UtcTimestamp at)
    {
        Advance(SettlementInstructionStatus.Settled);
        SettledAt = at;
    }

    public void Cancel() => Advance(SettlementInstructionStatus.Cancelled);

    public void Fail() => Advance(SettlementInstructionStatus.Failed);

    private void Advance(SettlementInstructionStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private static void EnsureFinalityConsistent(
        SettlementInstructionStatus status,
        UtcTimestamp? lockedAt,
        UtcTimestamp? settledAt)
    {
        bool settled = status == SettlementInstructionStatus.Settled;

        if (settled != settledAt.HasValue)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementInstructionFinalityInconsistent);
        }

        bool requiresLock = status is SettlementInstructionStatus.LockedForSettlement
            or SettlementInstructionStatus.Settled;

        if (requiresLock && !lockedAt.HasValue)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementInstructionFinalityInconsistent);
        }
    }
}

public static class SettlementInstructionCatalog
{
    public static string ToToken(this SettlementInstructionStatus status) => status switch
    {
        SettlementInstructionStatus.Created => "CREATED",
        SettlementInstructionStatus.Queued => "QUEUED",
        SettlementInstructionStatus.LockedForSettlement => "LOCKED_FOR_SETTLEMENT",
        SettlementInstructionStatus.Settled => "SETTLED",
        SettlementInstructionStatus.Cancelled => "CANCELLED",
        SettlementInstructionStatus.Failed => "FAILED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.SettlementInstructionStatusUnknown),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out SettlementInstructionStatus status)
    {
        switch (token)
        {
            case "CREATED":
                status = SettlementInstructionStatus.Created;
                return true;
            case "QUEUED":
                status = SettlementInstructionStatus.Queued;
                return true;
            case "LOCKED_FOR_SETTLEMENT":
                status = SettlementInstructionStatus.LockedForSettlement;
                return true;
            case "SETTLED":
                status = SettlementInstructionStatus.Settled;
                return true;
            case "CANCELLED":
                status = SettlementInstructionStatus.Cancelled;
                return true;
            case "FAILED":
                status = SettlementInstructionStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static SettlementInstructionStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out SettlementInstructionStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementInstructionStatusUnknown);
}
