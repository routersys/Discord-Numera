namespace Numera.Domain.Banking;

public enum ClearingCycleStatus
{
    Open = 1,
    Locked = 2,
    Settling = 3,
    Closed = 4,
}

public sealed class ClearingCycle : VersionedEntity
{
    public const int MaximumCycleKeyLength = 32;

    private static readonly StateTransitionTable<ClearingCycleStatus> Transitions =
        StateTransitionTable<ClearingCycleStatus>
            .Create(InvariantViolationCode.ClearingCycleTransitionInvalid)
            .AllowCreation(ClearingCycleStatus.Open)
            .Allow(ClearingCycleStatus.Open, ClearingCycleStatus.Locked)
            .Allow(ClearingCycleStatus.Locked, ClearingCycleStatus.Settling)
            .Allow(ClearingCycleStatus.Settling, ClearingCycleStatus.Closed)
            .Build();

    private ClearingCycle(
        ClearingCycleId id,
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string cycleKey,
        ClearingCycleStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? lockedAt,
        UtcTimestamp? closedAt,
        long version)
        : base(version)
    {
        Id = id;
        EconomyScopeId = economyScopeId;
        CurrencyId = currencyId;
        CycleKey = cycleKey;
        Status = status;
        OpenedAt = openedAt;
        LockedAt = lockedAt;
        ClosedAt = closedAt;
    }

    public ClearingCycleId Id { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public CurrencyId CurrencyId { get; }

    public string CycleKey { get; }

    public ClearingCycleStatus Status { get; private set; }

    public UtcTimestamp OpenedAt { get; }

    public UtcTimestamp? LockedAt { get; private set; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public bool AcceptsNewInstructions => Status == ClearingCycleStatus.Open;

    public static ClearingCycle Open(
        ClearingCycleId id,
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string cycleKey,
        UtcTimestamp openedAt)
    {
        Transitions.EnsureCreatable(ClearingCycleStatus.Open);

        if (string.IsNullOrEmpty(cycleKey) || cycleKey.Length > MaximumCycleKeyLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingCycleKeyInvalid);
        }

        return new ClearingCycle(
            id,
            economyScopeId,
            currencyId,
            cycleKey,
            ClearingCycleStatus.Open,
            openedAt,
            lockedAt: null,
            closedAt: null,
            InitialVersion);
    }

    public static ClearingCycle Rehydrate(
        ClearingCycleId id,
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string cycleKey,
        ClearingCycleStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? lockedAt,
        UtcTimestamp? closedAt,
        long version)
    {
        bool requiresLock = status is ClearingCycleStatus.Locked
            or ClearingCycleStatus.Settling
            or ClearingCycleStatus.Closed;

        if (requiresLock != lockedAt.HasValue ||
            (status == ClearingCycleStatus.Closed) != closedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingCycleTransitionInvalid);
        }

        return new ClearingCycle(
            id, economyScopeId, currencyId, cycleKey, status, openedAt, lockedAt, closedAt, version);
    }

    public void Lock(UtcTimestamp at)
    {
        Advance(ClearingCycleStatus.Locked);
        LockedAt = at;
    }

    public void BeginSettling() => Advance(ClearingCycleStatus.Settling);

    public void Close(UtcTimestamp at)
    {
        Advance(ClearingCycleStatus.Closed);
        ClosedAt = at;
    }

    private void Advance(ClearingCycleStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }
}

public readonly record struct ClearingPosition(
    ClearingPositionId Id,
    ClearingCycleId ClearingCycleId,
    BankId BankId,
    CurrencyId CurrencyId,
    MoneyMinor GrossReceivable,
    MoneyMinor GrossPayable)
{
    public MoneyMinor Net => GrossReceivable.Subtract(GrossPayable);

    public static ClearingPosition Create(
        ClearingPositionId id,
        ClearingCycleId clearingCycleId,
        BankId bankId,
        CurrencyId currencyId,
        MoneyMinor grossReceivable,
        MoneyMinor grossPayable) =>
        grossReceivable.IsNegative || grossPayable.IsNegative
            ? throw InvariantViolationException.Create(InvariantViolationCode.ClearingPositionInconsistent)
            : new ClearingPosition(id, clearingCycleId, bankId, currencyId, grossReceivable, grossPayable);

    public static MoneyMinor NetTotal(ReadOnlySpan<ClearingPosition> positions)
    {
        Int128 total = Int128.Zero;

        foreach (ClearingPosition position in positions)
        {
            total = checked(total + position.Net.Intermediate);
        }

        return MoneyMinor.FromIntermediate(total);
    }

    public static void EnsureBalanced(ReadOnlySpan<ClearingPosition> positions)
    {
        if (!NetTotal(positions).IsZero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClearingPositionInconsistent);
        }
    }
}

public static class ClearingCycleCatalog
{
    public static string ToToken(this ClearingCycleStatus status) => status switch
    {
        ClearingCycleStatus.Open => "OPEN",
        ClearingCycleStatus.Locked => "LOCKED",
        ClearingCycleStatus.Settling => "SETTLING",
        ClearingCycleStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.ClearingCycleStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ClearingCycleStatus status)
    {
        switch (token)
        {
            case "OPEN":
                status = ClearingCycleStatus.Open;
                return true;
            case "LOCKED":
                status = ClearingCycleStatus.Locked;
                return true;
            case "SETTLING":
                status = ClearingCycleStatus.Settling;
                return true;
            case "CLOSED":
                status = ClearingCycleStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static ClearingCycleStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ClearingCycleStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.ClearingCycleStatusUnknown);
}
