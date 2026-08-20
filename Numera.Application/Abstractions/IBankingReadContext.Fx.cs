using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record FxVisualSnapshot(
    FxMarketId MarketId,
    string PairCode,
    long PriceScale,
    int BaseMinorUnitDigits,
    long? LastTradePriceUnits,
    long SummaryVersion,
    long OrderBookVersion,
    long ProjectionVersion,
    IReadOnlyList<FxOhlcBucket> Buckets,
    IReadOnlyList<FxDepthLevel> Bids,
    IReadOnlyList<FxDepthLevel> Asks);

public interface IFxVisualReadRepository
{
    FxVisualSnapshot? Read(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd,
        int depthLevels);
}

public partial interface IBankingReadContext
{
    IFxVisualReadRepository FxVisuals { get; }
}
