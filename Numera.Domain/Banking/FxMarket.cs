using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum FxMarketStatus
{
    Draft = 1,
    PendingApproval = 2,
    Active = 3,
    Suspended = 4,
    Retired = 5,
}

public enum FxOrderSide
{
    BuyBase = 1,
    SellBase = 2,
}

public enum FxOrderType
{
    Limit = 1,
    MarketIoc = 2,
    MarketFok = 3,
}

public enum FxTimeInForce
{
    GoodTilCancelled = 1,
    ImmediateOrCancel = 2,
    FillOrKill = 3,
}

public static class FxPricing
{
    public const int MaximumFokMakerOrders = 256;

    public static bool IsExactSettlementCapable(long lotSizeBaseMinor, long tickSizePriceUnits, long priceScale)
    {
        if (lotSizeBaseMinor <= 0 || tickSizePriceUnits <= 0 || priceScale <= 0)
        {
            return false;
        }

        return checked((Int128)lotSizeBaseMinor * tickSizePriceUnits) % priceScale == 0;
    }

    public static bool TryQuoteMinor(long baseMinor, long priceUnits, long priceScale, out long quoteMinor)
    {
        quoteMinor = 0;

        if (baseMinor <= 0 || priceUnits <= 0 || priceScale <= 0)
        {
            return false;
        }

        Int128 numerator = checked((Int128)baseMinor * priceUnits);

        if (numerator % priceScale != 0)
        {
            return false;
        }

        Int128 quote = numerator / priceScale;

        if (quote <= 0 || quote > long.MaxValue)
        {
            return false;
        }

        quoteMinor = (long)quote;
        return true;
    }

    public static bool IsLotMultiple(long baseMinor, long lotSizeBaseMinor) =>
        lotSizeBaseMinor > 0 && baseMinor > 0 && baseMinor % lotSizeBaseMinor == 0;

    public static bool IsTickMultiple(long priceUnits, long tickSizePriceUnits) =>
        tickSizePriceUnits > 0 && priceUnits > 0 && priceUnits % tickSizePriceUnits == 0;
}

public sealed class FxMarket : VersionedEntity
{
    private static readonly StateTransitionTable<FxMarketStatus> Transitions =
        StateTransitionTable<FxMarketStatus>
            .Create(InvariantViolationCode.FxMarketTransitionInvalid)
            .AllowCreation(FxMarketStatus.Draft)
            .Allow(FxMarketStatus.Draft, FxMarketStatus.PendingApproval, FxMarketStatus.Retired)
            .Allow(FxMarketStatus.PendingApproval, FxMarketStatus.Active, FxMarketStatus.Retired)
            .Allow(FxMarketStatus.Active, FxMarketStatus.Suspended)
            .Allow(FxMarketStatus.Suspended, FxMarketStatus.Active, FxMarketStatus.Retired)
            .Build();

    private FxMarket(
        FxMarketId id,
        CurrencyId baseCurrencyId,
        CurrencyId quoteCurrencyId,
        PartyId operatorPartyId,
        FxMarketPolicyVersionId? currentPolicyVersionId,
        long priceScale,
        long tickSizePriceUnits,
        long lotSizeBaseMinor,
        long nextOrderSequenceNo,
        long nextTradeSequenceNo,
        FxMarketStatus status,
        long version)
        : base(version)
    {
        Id = id;
        BaseCurrencyId = baseCurrencyId;
        QuoteCurrencyId = quoteCurrencyId;
        OperatorPartyId = operatorPartyId;
        CurrentPolicyVersionId = currentPolicyVersionId;
        PriceScale = priceScale;
        TickSizePriceUnits = tickSizePriceUnits;
        LotSizeBaseMinor = lotSizeBaseMinor;
        NextOrderSequenceNo = nextOrderSequenceNo;
        NextTradeSequenceNo = nextTradeSequenceNo;
        Status = status;
    }

    public FxMarketId Id { get; }

    public CurrencyId BaseCurrencyId { get; }

    public CurrencyId QuoteCurrencyId { get; }

    public PartyId OperatorPartyId { get; }

    public FxMarketPolicyVersionId? CurrentPolicyVersionId { get; private set; }

    public long PriceScale { get; }

    public long TickSizePriceUnits { get; }

    public long LotSizeBaseMinor { get; }

    public long NextOrderSequenceNo { get; private set; }

    public long NextTradeSequenceNo { get; private set; }

    public FxMarketStatus Status { get; private set; }

    public bool IsTradable => Status == FxMarketStatus.Active;

    public bool IsExactSettlementCapable =>
        FxPricing.IsExactSettlementCapable(LotSizeBaseMinor, TickSizePriceUnits, PriceScale);

    public static FxMarket CreateDraft(
        FxMarketId id,
        CurrencyId baseCurrencyId,
        CurrencyId quoteCurrencyId,
        PartyId operatorPartyId,
        long priceScale,
        long tickSizePriceUnits,
        long lotSizeBaseMinor)
    {
        if (baseCurrencyId.Value.CompareTo(quoteCurrencyId.Value) >= 0)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxMarketOrientationInvalid);
        }

        if (priceScale <= 0 || tickSizePriceUnits <= 0 || lotSizeBaseMinor <= 0)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxMarketParametersInvalid);
        }

        return new FxMarket(
            id,
            baseCurrencyId,
            quoteCurrencyId,
            operatorPartyId,
            currentPolicyVersionId: null,
            priceScale,
            tickSizePriceUnits,
            lotSizeBaseMinor,
            nextOrderSequenceNo: 1,
            nextTradeSequenceNo: 1,
            FxMarketStatus.Draft,
            InitialVersion);
    }

    public static FxMarket Rehydrate(
        FxMarketId id,
        CurrencyId baseCurrencyId,
        CurrencyId quoteCurrencyId,
        PartyId operatorPartyId,
        FxMarketPolicyVersionId? currentPolicyVersionId,
        long priceScale,
        long tickSizePriceUnits,
        long lotSizeBaseMinor,
        long nextOrderSequenceNo,
        long nextTradeSequenceNo,
        FxMarketStatus status,
        long version) =>
        new(
            id,
            baseCurrencyId,
            quoteCurrencyId,
            operatorPartyId,
            currentPolicyVersionId,
            priceScale,
            tickSizePriceUnits,
            lotSizeBaseMinor,
            nextOrderSequenceNo,
            nextTradeSequenceNo,
            status,
            version);

    public void ApplyPolicyVersion(FxMarketPolicyVersionId policyVersionId)
    {
        CurrentPolicyVersionId = policyVersionId;
        AdvanceVersion();
    }

    public void SubmitForApproval()
    {
        if (CurrentPolicyVersionId is null || !IsExactSettlementCapable)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxMarketNotApprovable);
        }

        Transitions.EnsureAllowed(Status, FxMarketStatus.PendingApproval);

        Status = FxMarketStatus.PendingApproval;
        AdvanceVersion();
    }

    public void Activate()
    {
        if (CurrentPolicyVersionId is null || !IsExactSettlementCapable)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxMarketNotApprovable);
        }

        Transitions.EnsureAllowed(Status, FxMarketStatus.Active);

        Status = FxMarketStatus.Active;
        AdvanceVersion();
    }

    public void Suspend()
    {
        Transitions.EnsureAllowed(Status, FxMarketStatus.Suspended);

        Status = FxMarketStatus.Suspended;
        AdvanceVersion();
    }

    public void Retire()
    {
        Transitions.EnsureAllowed(Status, FxMarketStatus.Retired);

        Status = FxMarketStatus.Retired;
        AdvanceVersion();
    }

    public long TakeOrderSequence()
    {
        long sequence = NextOrderSequenceNo;
        NextOrderSequenceNo = checked(sequence + 1);
        AdvanceVersion();

        return sequence;
    }

    public long TakeTradeSequence()
    {
        long sequence = NextTradeSequenceNo;
        NextTradeSequenceNo = checked(sequence + 1);

        return sequence;
    }
}

public static class FxMarketCatalog
{
    public static string ToToken(this FxMarketStatus status) => status switch
    {
        FxMarketStatus.Draft => "DRAFT",
        FxMarketStatus.PendingApproval => "PENDING_APPROVAL",
        FxMarketStatus.Active => "ACTIVE",
        FxMarketStatus.Suspended => "SUSPENDED",
        FxMarketStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this FxOrderSide side) => side switch
    {
        FxOrderSide.BuyBase => "BUY_BASE",
        FxOrderSide.SellBase => "SELL_BASE",
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    public static string ToToken(this FxOrderType type) => type switch
    {
        FxOrderType.Limit => "LIMIT",
        FxOrderType.MarketIoc => "MARKET_IOC",
        FxOrderType.MarketFok => "MARKET_FOK",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string ToToken(this FxTimeInForce timeInForce) => timeInForce switch
    {
        FxTimeInForce.GoodTilCancelled => "GTC",
        FxTimeInForce.ImmediateOrCancel => "IOC",
        FxTimeInForce.FillOrKill => "FOK",
        _ => throw new ArgumentOutOfRangeException(nameof(timeInForce)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out FxMarketStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = FxMarketStatus.Draft;
                return true;
            case "PENDING_APPROVAL":
                status = FxMarketStatus.PendingApproval;
                return true;
            case "ACTIVE":
                status = FxMarketStatus.Active;
                return true;
            case "SUSPENDED":
                status = FxMarketStatus.Suspended;
                return true;
            case "RETIRED":
                status = FxMarketStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static FxMarketStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out FxMarketStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.FxMarketStatusUnknown);

    public static FxOrderSide ParseSideToken(ReadOnlySpan<char> token) => token switch
    {
        "BUY_BASE" => FxOrderSide.BuyBase,
        "SELL_BASE" => FxOrderSide.SellBase,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FxOrderSideUnknown),
    };

    public static FxOrderType ParseOrderTypeToken(ReadOnlySpan<char> token) => token switch
    {
        "LIMIT" => FxOrderType.Limit,
        "MARKET_IOC" => FxOrderType.MarketIoc,
        "MARKET_FOK" => FxOrderType.MarketFok,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FxOrderTypeUnknown),
    };

    public static FxTimeInForce ParseTimeInForceToken(ReadOnlySpan<char> token) => token switch
    {
        "GTC" => FxTimeInForce.GoodTilCancelled,
        "IOC" => FxTimeInForce.ImmediateOrCancel,
        "FOK" => FxTimeInForce.FillOrKill,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FxTimeInForceUnknown),
    };
}
