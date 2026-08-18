using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum FxOrderStatus
{
    Open = 1,
    PartiallyFilled = 2,
    Filled = 3,
    Cancelled = 4,
    Expired = 5,
    Rejected = 6,
}

public enum FxParticipantKind
{
    Customer = 1,
    BankTreasury = 2,
    MonetaryAuthority = 3,
}

public sealed class FxOrder : VersionedEntity
{
    private static readonly StateTransitionTable<FxOrderStatus> Transitions =
        StateTransitionTable<FxOrderStatus>
            .Create(InvariantViolationCode.FxOrderTransitionInvalid)
            .AllowCreation(FxOrderStatus.Open)
            .Allow(
                FxOrderStatus.Open,
                FxOrderStatus.PartiallyFilled,
                FxOrderStatus.Filled,
                FxOrderStatus.Cancelled,
                FxOrderStatus.Expired,
                FxOrderStatus.Rejected)
            .Allow(
                FxOrderStatus.PartiallyFilled,
                FxOrderStatus.Filled,
                FxOrderStatus.Cancelled,
                FxOrderStatus.Expired)
            .Build();

    private FxOrder(
        FxOrderId id,
        FxMarketId marketId,
        FxParticipantKind participantKind,
        PartyId participantPartyId,
        CustomerAccountId? customerAccountId,
        FxOrderSide side,
        FxOrderType orderType,
        FxTimeInForce timeInForce,
        long? priceUnits,
        int? maximumSlippageBps,
        long originalBaseMinor,
        long filledBaseMinor,
        long sequenceNo,
        FxOrderStatus status,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        HoldId sourceHoldId,
        FxMarketPolicyVersionId feePolicyVersionId,
        UtcTimestamp createdAt,
        UtcTimestamp? terminalAt,
        long version)
        : base(version)
    {
        Id = id;
        MarketId = marketId;
        ParticipantKind = participantKind;
        ParticipantPartyId = participantPartyId;
        CustomerAccountId = customerAccountId;
        Side = side;
        OrderType = orderType;
        TimeInForce = timeInForce;
        PriceUnits = priceUnits;
        MaximumSlippageBps = maximumSlippageBps;
        OriginalBaseMinor = originalBaseMinor;
        FilledBaseMinor = filledBaseMinor;
        SequenceNo = sequenceNo;
        Status = status;
        SourceFundingEndpointId = sourceFundingEndpointId;
        DestinationSettlementEndpointId = destinationSettlementEndpointId;
        SourceHoldId = sourceHoldId;
        FeePolicyVersionId = feePolicyVersionId;
        CreatedAt = createdAt;
        TerminalAt = terminalAt;
    }

    public FxOrderId Id { get; }

    public FxMarketId MarketId { get; }

    public FxParticipantKind ParticipantKind { get; }

    public PartyId ParticipantPartyId { get; }

    public CustomerAccountId? CustomerAccountId { get; }

    public FxOrderSide Side { get; }

    public FxOrderType OrderType { get; }

    public FxTimeInForce TimeInForce { get; }

    public long? PriceUnits { get; }

    public int? MaximumSlippageBps { get; }

    public long OriginalBaseMinor { get; }

    public long FilledBaseMinor { get; private set; }

    public long SequenceNo { get; }

    public FxOrderStatus Status { get; private set; }

    public FxFundingEndpointId SourceFundingEndpointId { get; }

    public FxSettlementEndpointId DestinationSettlementEndpointId { get; }

    public HoldId SourceHoldId { get; }

    public FxMarketPolicyVersionId FeePolicyVersionId { get; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? TerminalAt { get; private set; }

    public long RemainingBaseMinor => checked(OriginalBaseMinor - FilledBaseMinor);

    public bool IsTerminal => Status is FxOrderStatus.Filled
        or FxOrderStatus.Cancelled
        or FxOrderStatus.Expired
        or FxOrderStatus.Rejected;

    public bool IsResting => Status is FxOrderStatus.Open or FxOrderStatus.PartiallyFilled;

    public static FxOrder Place(
        FxOrderId id,
        FxMarketId marketId,
        FxParticipantKind participantKind,
        PartyId participantPartyId,
        CustomerAccountId? customerAccountId,
        FxOrderSide side,
        FxOrderType orderType,
        FxTimeInForce timeInForce,
        long? priceUnits,
        int? maximumSlippageBps,
        long originalBaseMinor,
        long sequenceNo,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        HoldId sourceHoldId,
        FxMarketPolicyVersionId feePolicyVersionId,
        UtcTimestamp createdAt)
    {
        if (originalBaseMinor <= 0)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderAmountInvalid);
        }

        if (orderType == FxOrderType.Limit
            ? priceUnits is not > 0 || timeInForce != FxTimeInForce.GoodTilCancelled
            : priceUnits is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderPriceInvalid);
        }

        if (orderType != FxOrderType.Limit && maximumSlippageBps is not >= 0)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderSlippageInvalid);
        }

        if (participantKind == FxParticipantKind.Customer
            ? customerAccountId is null
            : customerAccountId is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderParticipantInvalid);
        }

        return new FxOrder(
            id,
            marketId,
            participantKind,
            participantPartyId,
            customerAccountId,
            side,
            orderType,
            timeInForce,
            priceUnits,
            maximumSlippageBps,
            originalBaseMinor,
            filledBaseMinor: 0,
            sequenceNo,
            FxOrderStatus.Open,
            sourceFundingEndpointId,
            destinationSettlementEndpointId,
            sourceHoldId,
            feePolicyVersionId,
            createdAt,
            terminalAt: null,
            InitialVersion);
    }

    public static FxOrder Rehydrate(
        FxOrderId id,
        FxMarketId marketId,
        FxParticipantKind participantKind,
        PartyId participantPartyId,
        CustomerAccountId? customerAccountId,
        FxOrderSide side,
        FxOrderType orderType,
        FxTimeInForce timeInForce,
        long? priceUnits,
        int? maximumSlippageBps,
        long originalBaseMinor,
        long filledBaseMinor,
        long sequenceNo,
        FxOrderStatus status,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        HoldId sourceHoldId,
        FxMarketPolicyVersionId feePolicyVersionId,
        UtcTimestamp createdAt,
        UtcTimestamp? terminalAt,
        long version) =>
        new(
            id,
            marketId,
            participantKind,
            participantPartyId,
            customerAccountId,
            side,
            orderType,
            timeInForce,
            priceUnits,
            maximumSlippageBps,
            originalBaseMinor,
            filledBaseMinor,
            sequenceNo,
            status,
            sourceFundingEndpointId,
            destinationSettlementEndpointId,
            sourceHoldId,
            feePolicyVersionId,
            createdAt,
            terminalAt,
            version);

    public void Fill(long baseMinor, UtcTimestamp now)
    {
        if (baseMinor <= 0 || baseMinor > RemainingBaseMinor)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderFillInvalid);
        }

        FilledBaseMinor = checked(FilledBaseMinor + baseMinor);

        FxOrderStatus next = FilledBaseMinor == OriginalBaseMinor
            ? FxOrderStatus.Filled
            : FxOrderStatus.PartiallyFilled;

        if (next != Status)
        {
            Transitions.EnsureAllowed(Status, next);
            Status = next;
        }

        if (next == FxOrderStatus.Filled)
        {
            TerminalAt = now;
        }

        AdvanceVersion();
    }

    public void Cancel(UtcTimestamp now) => Terminate(FxOrderStatus.Cancelled, now);

    public void Expire(UtcTimestamp now) => Terminate(FxOrderStatus.Expired, now);

    public void Reject(UtcTimestamp now)
    {
        if (FilledBaseMinor != 0)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxOrderRejectInvalid);
        }

        Terminate(FxOrderStatus.Rejected, now);
    }

    private void Terminate(FxOrderStatus status, UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, status);

        Status = status;
        TerminalAt = now;
        AdvanceVersion();
    }
}

public static class FxOrderCatalog
{
    public static string ToToken(this FxOrderStatus status) => status switch
    {
        FxOrderStatus.Open => "OPEN",
        FxOrderStatus.PartiallyFilled => "PARTIALLY_FILLED",
        FxOrderStatus.Filled => "FILLED",
        FxOrderStatus.Cancelled => "CANCELLED",
        FxOrderStatus.Expired => "EXPIRED",
        FxOrderStatus.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this FxParticipantKind kind) => kind switch
    {
        FxParticipantKind.Customer => "CUSTOMER",
        FxParticipantKind.BankTreasury => "BANK_TREASURY",
        FxParticipantKind.MonetaryAuthority => "MONETARY_AUTHORITY",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out FxOrderStatus status)
    {
        switch (token)
        {
            case "OPEN":
                status = FxOrderStatus.Open;
                return true;
            case "PARTIALLY_FILLED":
                status = FxOrderStatus.PartiallyFilled;
                return true;
            case "FILLED":
                status = FxOrderStatus.Filled;
                return true;
            case "CANCELLED":
                status = FxOrderStatus.Cancelled;
                return true;
            case "EXPIRED":
                status = FxOrderStatus.Expired;
                return true;
            case "REJECTED":
                status = FxOrderStatus.Rejected;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static FxOrderStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out FxOrderStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.FxOrderStatusUnknown);

    public static FxParticipantKind ParseParticipantToken(ReadOnlySpan<char> token) => token switch
    {
        "CUSTOMER" => FxParticipantKind.Customer,
        "BANK_TREASURY" => FxParticipantKind.BankTreasury,
        "MONETARY_AUTHORITY" => FxParticipantKind.MonetaryAuthority,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.FxParticipantKindUnknown),
    };
}
