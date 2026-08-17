namespace Numera.Domain.Banking;

public enum CurrencyStatus
{
    Active = 1,
    Suspended = 2,
    Retiring = 3,
    Retired = 4,
}

public sealed class Currency : VersionedEntity
{
    private static readonly StateTransitionTable<CurrencyStatus> Transitions =
        StateTransitionTable<CurrencyStatus>
            .Create(InvariantViolationCode.CurrencyTransitionInvalid)
            .AllowCreation(CurrencyStatus.Active)
            .Allow(CurrencyStatus.Active, CurrencyStatus.Suspended, CurrencyStatus.Retiring)
            .Allow(CurrencyStatus.Suspended, CurrencyStatus.Active, CurrencyStatus.Retiring)
            .Allow(CurrencyStatus.Retiring, CurrencyStatus.Retired)
            .Build();

    private Currency(
        CurrencyId id,
        EconomyScopeId economyScopeId,
        CurrencyStatus status,
        MinorUnitDigits minorUnitDigits,
        MoneyMinor? baseMoneySupplyCap,
        UtcTimestamp createdAt,
        UtcTimestamp? retiredAt,
        long version)
        : base(version)
    {
        Id = id;
        EconomyScopeId = economyScopeId;
        Status = status;
        MinorUnitDigits = minorUnitDigits;
        BaseMoneySupplyCap = baseMoneySupplyCap;
        CreatedAt = createdAt;
        RetiredAt = retiredAt;
    }

    public CurrencyId Id { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public CurrencyStatus Status { get; private set; }

    public MinorUnitDigits MinorUnitDigits { get; }

    public MoneyMinor? BaseMoneySupplyCap { get; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? RetiredAt { get; private set; }

    public bool IsCurrent => Status is CurrencyStatus.Active
        or CurrencyStatus.Suspended
        or CurrencyStatus.Retiring;

    public bool AcceptsSupplyChange => Status == CurrencyStatus.Active;

    public static Currency Create(
        CurrencyId id,
        EconomyScopeId economyScopeId,
        MinorUnitDigits minorUnitDigits,
        MoneyMinor? baseMoneySupplyCap,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(CurrencyStatus.Active);
        EnsureCapValid(baseMoneySupplyCap);

        return new Currency(
            id,
            economyScopeId,
            CurrencyStatus.Active,
            minorUnitDigits,
            baseMoneySupplyCap,
            createdAt,
            retiredAt: null,
            InitialVersion);
    }

    public static Currency Rehydrate(
        CurrencyId id,
        EconomyScopeId economyScopeId,
        CurrencyStatus status,
        MinorUnitDigits minorUnitDigits,
        MoneyMinor? baseMoneySupplyCap,
        UtcTimestamp createdAt,
        UtcTimestamp? retiredAt,
        long version)
    {
        EnsureCapValid(baseMoneySupplyCap);

        if ((status == CurrencyStatus.Retired) != retiredAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CurrencyTransitionInvalid);
        }

        return new Currency(
            id, economyScopeId, status, minorUnitDigits, baseMoneySupplyCap, createdAt, retiredAt, version);
    }

    public void Suspend() => Advance(CurrencyStatus.Suspended);

    public void Resume() => Advance(CurrencyStatus.Active);

    public void BeginRetiring() => Advance(CurrencyStatus.Retiring);

    public void Retire(UtcTimestamp at, MoneyMinor baseMoneySupply)
    {
        if (!baseMoneySupply.IsZero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CurrencyRetirementBlocked);
        }

        Advance(CurrencyStatus.Retired);
        RetiredAt = at;
    }

    public MoneyMinor ProjectSupplyAfterIssue(MoneyMinor baseMoneySupply, MoneyMinor amount) =>
        MoneyMinor.FromIntermediate(checked(baseMoneySupply.Intermediate + amount.Intermediate));

    public MoneyMinor ProjectSupplyAfterBurn(MoneyMinor baseMoneySupply, MoneyMinor amount)
    {
        Int128 projected = checked(baseMoneySupply.Intermediate - amount.Intermediate);

        return projected < Int128.Zero
            ? throw InvariantViolationException.Create(InvariantViolationCode.CurrencySupplyNegative)
            : MoneyMinor.FromIntermediate(projected);
    }

    public bool ExceedsSupplyCap(MoneyMinor projectedSupply) =>
        BaseMoneySupplyCap is { } cap && projectedSupply > cap;

    private void Advance(CurrencyStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private static void EnsureCapValid(MoneyMinor? baseMoneySupplyCap)
    {
        if (baseMoneySupplyCap is { IsNegative: true })
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CurrencySupplyCapInvalid);
        }
    }
}

public static class CurrencyCatalog
{
    public static string ToToken(this CurrencyStatus status) => status switch
    {
        CurrencyStatus.Active => "ACTIVE",
        CurrencyStatus.Suspended => "SUSPENDED",
        CurrencyStatus.Retiring => "RETIRING",
        CurrencyStatus.Retired => "RETIRED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CurrencyStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CurrencyStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CurrencyStatus.Active;
                return true;
            case "SUSPENDED":
                status = CurrencyStatus.Suspended;
                return true;
            case "RETIRING":
                status = CurrencyStatus.Retiring;
                return true;
            case "RETIRED":
                status = CurrencyStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CurrencyStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CurrencyStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CurrencyStatusUnknown);
}
