namespace Numera.Domain.Banking;

public enum HoldStatus
{
    Active = 1,
    Captured = 2,
    Released = 3,
    Expired = 4,
}

public enum HoldScopeKind
{
    CustomerDeposit = 1,
    LedgerAsset = 2,
}

public sealed class Hold : VersionedEntity
{
    private Hold(
        HoldId id,
        HoldScopeKind scopeKind,
        DepositAccountId? depositAccountId,
        LedgerAccountId? ledgerAccountId,
        BusinessOperationId businessOperationId,
        MoneyMinor amount,
        MoneyMinor remaining,
        string reason,
        HoldStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? expiresAt,
        UtcTimestamp? terminalAt,
        long version)
        : base(version)
    {
        Id = id;
        ScopeKind = scopeKind;
        DepositAccountId = depositAccountId;
        LedgerAccountId = ledgerAccountId;
        BusinessOperationId = businessOperationId;
        Amount = amount;
        Remaining = remaining;
        Reason = reason;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        TerminalAt = terminalAt;
    }

    public HoldId Id { get; }

    public HoldScopeKind ScopeKind { get; }

    public DepositAccountId? DepositAccountId { get; }

    public LedgerAccountId? LedgerAccountId { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public MoneyMinor Amount { get; }

    public MoneyMinor Remaining { get; private set; }

    public string Reason { get; }

    public HoldStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? ExpiresAt { get; }

    public UtcTimestamp? TerminalAt { get; private set; }

    public bool IsTerminal => Status != HoldStatus.Active;

    public MoneyMinor CapturedAmount => Amount.Subtract(Remaining);

    public static Hold ReserveOnDeposit(
        HoldId id,
        DepositAccountId depositAccountId,
        BusinessOperationId businessOperationId,
        MoneyMinor amount,
        string reason,
        UtcTimestamp createdAt,
        UtcTimestamp? expiresAt) =>
        Reserve(
            id,
            HoldScopeKind.CustomerDeposit,
            depositAccountId,
            null,
            businessOperationId,
            amount,
            reason,
            createdAt,
            expiresAt);

    public static Hold ReserveOnLedgerAsset(
        HoldId id,
        LedgerAccountId ledgerAccountId,
        BusinessOperationId businessOperationId,
        MoneyMinor amount,
        string reason,
        UtcTimestamp createdAt,
        UtcTimestamp? expiresAt) =>
        Reserve(
            id,
            HoldScopeKind.LedgerAsset,
            null,
            ledgerAccountId,
            businessOperationId,
            amount,
            reason,
            createdAt,
            expiresAt);

    public static Hold Rehydrate(
        HoldId id,
        HoldScopeKind scopeKind,
        DepositAccountId? depositAccountId,
        LedgerAccountId? ledgerAccountId,
        BusinessOperationId businessOperationId,
        MoneyMinor amount,
        MoneyMinor remaining,
        string reason,
        HoldStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? expiresAt,
        UtcTimestamp? terminalAt,
        long version)
    {
        EnsureScopeConsistency(scopeKind, depositAccountId, ledgerAccountId);

        if (amount.Value < 1 || remaining.IsNegative || remaining > amount)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldAmountInvalid);
        }

        bool terminal = status != HoldStatus.Active;
        if (terminal != remaining.IsZero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldRemainingInconsistent);
        }

        if (terminal != terminalAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldRemainingInconsistent);
        }

        return new Hold(
            id,
            scopeKind,
            depositAccountId,
            ledgerAccountId,
            businessOperationId,
            amount,
            remaining,
            reason,
            status,
            createdAt,
            expiresAt,
            terminalAt,
            version);
    }

    public void Capture(MoneyMinor amount, UtcTimestamp at)
    {
        EnsureActive();

        if (amount.Value < 1 || amount > Remaining)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldCaptureAmountInvalid);
        }

        Remaining = Remaining.Subtract(amount);

        if (Remaining.IsZero)
        {
            Status = HoldStatus.Captured;
            TerminalAt = at;
        }

        AdvanceVersion();
    }

    public void Release(UtcTimestamp at) => Terminate(HoldStatus.Released, at);

    public void Expire(UtcTimestamp at)
    {
        if (ExpiresAt is not { } expiresAt || at < expiresAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldNotExpirable);
        }

        Terminate(HoldStatus.Expired, at);
    }

    private void Terminate(HoldStatus status, UtcTimestamp at)
    {
        EnsureActive();
        Remaining = MoneyMinor.Zero;
        Status = status;
        TerminalAt = at;
        AdvanceVersion();
    }

    private void EnsureActive()
    {
        if (Status != HoldStatus.Active)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldTransitionInvalid);
        }
    }

    private static Hold Reserve(
        HoldId id,
        HoldScopeKind scopeKind,
        DepositAccountId? depositAccountId,
        LedgerAccountId? ledgerAccountId,
        BusinessOperationId businessOperationId,
        MoneyMinor amount,
        string reason,
        UtcTimestamp createdAt,
        UtcTimestamp? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureScopeConsistency(scopeKind, depositAccountId, ledgerAccountId);

        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldAmountInvalid);
        }

        if (expiresAt is { } expiry && expiry <= createdAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldExpiryInvalid);
        }

        return new Hold(
            id,
            scopeKind,
            depositAccountId,
            ledgerAccountId,
            businessOperationId,
            amount,
            amount,
            reason,
            HoldStatus.Active,
            createdAt,
            expiresAt,
            terminalAt: null,
            InitialVersion);
    }

    private static void EnsureScopeConsistency(
        HoldScopeKind scopeKind,
        DepositAccountId? depositAccountId,
        LedgerAccountId? ledgerAccountId)
    {
        bool consistent = scopeKind switch
        {
            HoldScopeKind.CustomerDeposit => depositAccountId.HasValue && !ledgerAccountId.HasValue,
            HoldScopeKind.LedgerAsset => !depositAccountId.HasValue && ledgerAccountId.HasValue,
            _ => false,
        };

        if (!consistent)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.HoldScopeInconsistent);
        }
    }
}

public static class HoldCatalog
{
    public static string ToToken(this HoldStatus status) => status switch
    {
        HoldStatus.Active => "ACTIVE",
        HoldStatus.Captured => "CAPTURED",
        HoldStatus.Released => "RELEASED",
        HoldStatus.Expired => "EXPIRED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.HoldStatusUnknown),
    };

    public static string ToToken(this HoldScopeKind scopeKind) => scopeKind switch
    {
        HoldScopeKind.CustomerDeposit => "CUSTOMER_DEPOSIT",
        HoldScopeKind.LedgerAsset => "LEDGER_ASSET",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.HoldScopeInconsistent),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out HoldStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = HoldStatus.Active;
                return true;
            case "CAPTURED":
                status = HoldStatus.Captured;
                return true;
            case "RELEASED":
                status = HoldStatus.Released;
                return true;
            case "EXPIRED":
                status = HoldStatus.Expired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static HoldStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out HoldStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.HoldStatusUnknown);

    public static bool TryParseScopeToken(ReadOnlySpan<char> token, out HoldScopeKind scopeKind)
    {
        switch (token)
        {
            case "CUSTOMER_DEPOSIT":
                scopeKind = HoldScopeKind.CustomerDeposit;
                return true;
            case "LEDGER_ASSET":
                scopeKind = HoldScopeKind.LedgerAsset;
                return true;
            default:
                scopeKind = default;
                return false;
        }
    }

    public static HoldScopeKind ParseScopeToken(ReadOnlySpan<char> token) =>
        TryParseScopeToken(token, out HoldScopeKind scopeKind)
            ? scopeKind
            : throw InvariantViolationException.Create(InvariantViolationCode.HoldScopeInconsistent);
}
