namespace Numera.Domain.Banking;

public enum ClearingInstructionStatus
{
    Created = 1,
    Accepted = 2,
    Locked = 3,
    Settled = 4,
    Cancelled = 5,
    Failed = 6,
}

public sealed class ClearingInstruction : VersionedEntity
{
    private static readonly StateTransitionTable<ClearingInstructionStatus> Transitions =
        StateTransitionTable<ClearingInstructionStatus>
            .Create(InvariantViolationCode.ClearingInstructionTransitionInvalid)
            .AllowCreation(ClearingInstructionStatus.Created)
            .Allow(
                ClearingInstructionStatus.Created,
                ClearingInstructionStatus.Accepted,
                ClearingInstructionStatus.Cancelled,
                ClearingInstructionStatus.Failed)
            .Allow(
                ClearingInstructionStatus.Accepted,
                ClearingInstructionStatus.Locked,
                ClearingInstructionStatus.Cancelled,
                ClearingInstructionStatus.Failed)
            .Allow(
                ClearingInstructionStatus.Locked,
                ClearingInstructionStatus.Settled,
                ClearingInstructionStatus.Failed)
            .Build();

    private ClearingInstruction(
        ClearingInstructionId id,
        BusinessOperationId businessOperationId,
        PaymentOrderId? paymentOrderId,
        ClearingCycleId? clearingCycleId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        string instructionKind,
        ClearingInstructionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? settledAt,
        long version)
        : base(version)
    {
        Id = id;
        BusinessOperationId = businessOperationId;
        PaymentOrderId = paymentOrderId;
        ClearingCycleId = clearingCycleId;
        CurrencyId = currencyId;
        SourceBankId = sourceBankId;
        DestinationBankId = destinationBankId;
        Amount = amount;
        InstructionKind = instructionKind;
        Status = status;
        CreatedAt = createdAt;
        SettledAt = settledAt;
    }

    public ClearingInstructionId Id { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public PaymentOrderId? PaymentOrderId { get; }

    public ClearingCycleId? ClearingCycleId { get; private set; }

    public CurrencyId CurrencyId { get; }

    public BankId SourceBankId { get; }

    public BankId DestinationBankId { get; }

    public MoneyMinor Amount { get; }

    public string InstructionKind { get; }

    public ClearingInstructionStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? SettledAt { get; private set; }

    public bool IsFinal => Status == ClearingInstructionStatus.Settled;

    public static ClearingInstruction Create(
        ClearingInstructionId id,
        BusinessOperationId businessOperationId,
        PaymentOrderId? paymentOrderId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        string instructionKind,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(ClearingInstructionStatus.Created);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructionKind);

        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingInstructionAmountInvalid);
        }

        if (sourceBankId == destinationBankId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingInstructionEndpointsInvalid);
        }

        return new ClearingInstruction(
            id,
            businessOperationId,
            paymentOrderId,
            clearingCycleId: null,
            currencyId,
            sourceBankId,
            destinationBankId,
            amount,
            instructionKind,
            ClearingInstructionStatus.Created,
            createdAt,
            settledAt: null,
            InitialVersion);
    }

    public static ClearingInstruction Rehydrate(
        ClearingInstructionId id,
        BusinessOperationId businessOperationId,
        PaymentOrderId? paymentOrderId,
        ClearingCycleId? clearingCycleId,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        string instructionKind,
        ClearingInstructionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? settledAt,
        long version)
    {
        if ((status == ClearingInstructionStatus.Settled) != settledAt.HasValue)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.ClearingInstructionTransitionInvalid);
        }

        if (status is ClearingInstructionStatus.Locked or ClearingInstructionStatus.Settled &&
            clearingCycleId is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingInstructionCycleMissing);
        }

        return new ClearingInstruction(
            id,
            businessOperationId,
            paymentOrderId,
            clearingCycleId,
            currencyId,
            sourceBankId,
            destinationBankId,
            amount,
            instructionKind,
            status,
            createdAt,
            settledAt,
            version);
    }

    public void Accept(ClearingCycleId clearingCycleId)
    {
        Advance(ClearingInstructionStatus.Accepted);
        ClearingCycleId = clearingCycleId;
    }

    public void Lock()
    {
        if (ClearingCycleId is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingInstructionCycleMissing);
        }

        Advance(ClearingInstructionStatus.Locked);
    }

    public void Settle(UtcTimestamp at)
    {
        Advance(ClearingInstructionStatus.Settled);
        SettledAt = at;
    }

    public void Cancel() => Advance(ClearingInstructionStatus.Cancelled);

    public void Fail() => Advance(ClearingInstructionStatus.Failed);

    private void Advance(ClearingInstructionStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }
}

public static class ClearingInstructionCatalog
{
    public static string ToToken(this ClearingInstructionStatus status) => status switch
    {
        ClearingInstructionStatus.Created => "CREATED",
        ClearingInstructionStatus.Accepted => "ACCEPTED",
        ClearingInstructionStatus.Locked => "LOCKED",
        ClearingInstructionStatus.Settled => "SETTLED",
        ClearingInstructionStatus.Cancelled => "CANCELLED",
        ClearingInstructionStatus.Failed => "FAILED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.ClearingInstructionStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ClearingInstructionStatus status)
    {
        switch (token)
        {
            case "CREATED":
                status = ClearingInstructionStatus.Created;
                return true;
            case "ACCEPTED":
                status = ClearingInstructionStatus.Accepted;
                return true;
            case "LOCKED":
                status = ClearingInstructionStatus.Locked;
                return true;
            case "SETTLED":
                status = ClearingInstructionStatus.Settled;
                return true;
            case "CANCELLED":
                status = ClearingInstructionStatus.Cancelled;
                return true;
            case "FAILED":
                status = ClearingInstructionStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static ClearingInstructionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ClearingInstructionStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.ClearingInstructionStatusUnknown);
}
