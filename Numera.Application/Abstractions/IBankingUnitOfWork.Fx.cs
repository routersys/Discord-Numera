using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record FxMarketPolicyVersion(
    FxMarketPolicyVersionId Id,
    FxMarketId MarketId,
    int MakerFeeBps,
    int TakerFeeBps,
    int MaximumMarketSlippageBps,
    UtcTimestamp EffectiveFrom,
    long Version);

public sealed record FxMarketSummary(
    FxMarketId MarketId,
    long? LastTradePriceUnits,
    long? LastTradeSequenceNo,
    long SummaryVersion,
    long OrderBookVersion,
    UtcTimestamp UpdatedAt);

public sealed record FxOhlcBucket(
    FxMarketId MarketId,
    int BucketSeconds,
    long BucketStart,
    long OpenPriceUnits,
    long HighPriceUnits,
    long LowPriceUnits,
    long ClosePriceUnits,
    long BaseVolumeMinor,
    long QuoteVolumeMinor,
    long LastTradeSequenceNo,
    long ProjectionVersion);

public sealed record FxDepthLevel(long PriceUnits, long BaseMinor);

public interface IFxRepository
{
    void AddMarket(FxMarket market);

    void UpdateMarket(FxMarket market);

    FxMarket? FindMarket(FxMarketId id);

    FxMarket? FindMarketByPair(CurrencyId baseCurrencyId, CurrencyId quoteCurrencyId);

    IReadOnlyList<FxMarket> ListMarkets(EconomyScopeId economyScopeId, int limit);

    void AddPolicyVersion(FxMarketPolicyVersion policy);

    FxMarketPolicyVersion? FindPolicyVersion(FxMarketPolicyVersionId id);

    long NextPolicyVersion(FxMarketId marketId);

    void AddOrder(FxOrder order);

    void UpdateOrder(FxOrder order);

    FxOrder? FindOrder(FxOrderId id);

    IReadOnlyList<FxOrder> ListRestingOrders(FxMarketId marketId, FxOrderSide side, int limit);

    IReadOnlyList<FxOrder> ListParticipantOrders(PartyId participantPartyId, long? afterCreatedAt, int limit);

    IReadOnlyList<FxDepthLevel> ReadDepth(FxMarketId marketId, FxOrderSide side, int limit);

    FxMarketSummary? FindSummary(FxMarketId marketId);

    void UpsertSummary(FxMarketSummary summary);

    IReadOnlyList<FxOhlcBucket> ListBuckets(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd);
}

public partial interface IBankingUnitOfWork
{
    IFxRepository Fx { get; }
}
