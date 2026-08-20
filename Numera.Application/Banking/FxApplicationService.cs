using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record GetFxMarketQuery(CurrencyId BaseCurrencyId, CurrencyId QuoteCurrencyId);

public sealed record GetFxRateVisualQuery(FxMarketId MarketId);

public sealed record GetFxBoardVisualQuery(FxMarketId MarketId);

public sealed record GetFxChartVisualQuery(FxMarketId MarketId, int BucketSeconds, long WindowSeconds);

public sealed record ListFxOrdersQuery(CustomerAccountId CustomerAccountId, string? Cursor);

public sealed record GetFxHistoryQuery(FxMarketId MarketId, string? Cursor);

public sealed record PlaceFxOrderCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId,
    CustomerAccountId CustomerAccountId,
    FxOrderSide Side,
    FxOrderType OrderType,
    long BaseMinor,
    long? PriceUnits,
    int? MaximumSlippageBps,
    DepositAccountId SourceDepositAccountId,
    DepositAccountId DestinationDepositAccountId,
    IdempotencyKey IdempotencyKey);

public sealed record CancelFxOrderCommand(
    AuthorizationContext Actor,
    CustomerAccountId CustomerAccountId,
    FxOrderId FxOrderId,
    IdempotencyKey IdempotencyKey);

public sealed record FxOrderView(
    FxOrderId Id,
    FxMarketId MarketId,
    FxOrderSide Side,
    FxOrderType OrderType,
    FxOrderStatus Status,
    long OriginalBaseMinor,
    long FilledBaseMinor,
    long RemainingBaseMinor,
    long? PriceUnits);

public sealed record FxOrderPageView(IReadOnlyList<FxOrderView> Items, string? NextCursor);

public sealed record FxTradeHistoryItem(
    long SequenceNo,
    long PriceUnits,
    long BaseMinor,
    long QuoteMinor,
    long ExecutedAt);

public sealed record FxTradeHistoryPageView(
    IReadOnlyList<FxTradeHistoryItem> Items,
    string? NextCursor);

public sealed record FxVisualCacheKey(
    long StatisticsAsOfMinute,
    long SummaryVersion,
    long OrderBookVersion,
    long ProjectionVersion);

public sealed record FxRateVisualView(
    FxMarketId MarketId,
    long StatisticsAsOfMinute,
    long? LastTradePriceUnits,
    long? BestBidPriceUnits,
    long? BestAskPriceUnits,
    long? SpreadPriceUnits,
    long High24hPriceUnits,
    long Low24hPriceUnits,
    long Volume24hBaseMinor,
    long SummaryVersion,
    long OrderBookVersion,
    FxVisualCacheKey CacheKey);

public sealed record FxBoardVisualView(
    FxMarketId MarketId,
    long StatisticsAsOfMinute,
    IReadOnlyList<FxDepthLevel> Bids,
    IReadOnlyList<FxDepthLevel> Asks,
    long OrderBookVersion,
    FxVisualCacheKey CacheKey);

public sealed record FxChartVisualView(
    FxMarketId MarketId,
    string PairCode,
    long PriceScale,
    int BaseMinorUnitDigits,
    int BucketSeconds,
    long StatisticsAsOfMinute,
    IReadOnlyList<FxOhlcBucket> Buckets,
    long SummaryVersion,
    FxVisualCacheKey CacheKey);

public interface IFxApplicationService
{
    Task<Result<FxMarketView>> GetFxMarketAsync(GetFxMarketQuery query, CancellationToken cancellationToken);

    Task<Result<FxRateVisualView>> GetFxRateVisualAsync(
        GetFxRateVisualQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxBoardVisualView>> GetFxBoardVisualAsync(
        GetFxBoardVisualQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxChartVisualView>> GetFxChartVisualAsync(
        GetFxChartVisualQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxOrderPageView>> ListFxOrdersAsync(
        ListFxOrdersQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxTradeHistoryPageView>> GetFxHistoryAsync(
        GetFxHistoryQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxOrderView>> PlaceFxOrderAsync(
        PlaceFxOrderCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxOrderView>> CancelFxOrderAsync(
        CancelFxOrderCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class FxApplicationService : IFxApplicationService
{
    public const int DepthLevels = 10;

    public const int MinuteBucketSeconds = 60;

    public const long RollingWindowSeconds = 86400;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IBankingReadGateway readGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public FxApplicationService(
        IBankingWriteGateway writeGateway,
        IBankingReadGateway readGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(readGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.readGateway = readGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<FxMarketView>> GetFxMarketAsync(
        GetFxMarketQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                (CurrencyId first, CurrencyId second) =
                    FxAdministrationApplicationService.Orient(query.BaseCurrencyId, query.QuoteCurrencyId);

                return unitOfWork.Fx.FindMarketByPair(first, second) is { } market
                    ? Result<FxMarketView>.Success(
                        FxAdministrationApplicationService.ToView(unitOfWork, market))
                    : Result<FxMarketView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
            },
            cancellationToken);
    }

    public Task<Result<FxRateVisualView>> GetFxRateVisualAsync(
        GetFxRateVisualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context => RateVisual(context, query)));
    }

    public Task<Result<FxBoardVisualView>> GetFxBoardVisualAsync(
        GetFxBoardVisualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context => BoardVisual(context, query)));
    }

    public Task<Result<FxChartVisualView>> GetFxChartVisualAsync(
        GetFxChartVisualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context => ChartVisual(context, query)));
    }

    public Task<Result<FxOrderPageView>> ListFxOrdersAsync(
        ListFxOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                if (unitOfWork.CustomerAccounts.Find(query.CustomerAccountId) is not { } customer)
                {
                    return Result<FxOrderPageView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
                }

                IReadOnlyList<FxOrder> fetched = unitOfWork.Fx.ListParticipantOrders(
                    customer.PartyId,
                    Cursor(query.Cursor),
                    PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

                IReadOnlyList<FxOrder> page = fetched.Count <= PaginationBudget.ListPageSize
                    ? fetched
                    : [.. fetched.Take(PaginationBudget.ListPageSize)];

                return Result<FxOrderPageView>.Success(new FxOrderPageView(
                    [.. page.Select(ToView)],
                    fetched.Count > PaginationBudget.ListPageSize
                        ? fetched[PaginationBudget.ListPageSize - 1].CreatedAt.UnixMilliseconds
                            .ToString(CultureInfo.InvariantCulture)
                        : null));
            },
            cancellationToken);
    }

    public Task<Result<FxTradeHistoryPageView>> GetFxHistoryAsync(
        GetFxHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => History(unitOfWork, query),
            cancellationToken);
    }

    public Task<Result<FxOrderView>> PlaceFxOrderAsync(
        PlaceFxOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Place(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxOrderView>> CancelFxOrderAsync(
        CancelFxOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Cancel(unitOfWork, command), cancellationToken);
    }

    private Result<FxTradeHistoryPageView> History(
        IBankingUnitOfWork unitOfWork,
        GetFxHistoryQuery query)
    {
        if (unitOfWork.Fx.FindMarket(query.MarketId) is null)
        {
            return Result<FxTradeHistoryPageView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        IReadOnlyList<FxTradeRecord> fetched = unitOfWork.Fx.ListTrades(
            query.MarketId,
            Cursor(query.Cursor),
            PaginationBudget.HistoryPageSize + PaginationBudget.QueryLookAhead);

        IReadOnlyList<FxTradeRecord> page = fetched.Count <= PaginationBudget.HistoryPageSize
            ? fetched
            : [.. fetched.Take(PaginationBudget.HistoryPageSize)];

        return Result<FxTradeHistoryPageView>.Success(new FxTradeHistoryPageView(
            [.. page.Select(static trade => new FxTradeHistoryItem(
                trade.SequenceNo,
                trade.PriceUnits,
                trade.BaseMinor,
                trade.QuoteMinor,
                trade.ExecutedAt.UnixMilliseconds))],
            fetched.Count > PaginationBudget.HistoryPageSize
                ? page[^1].SequenceNo.ToString(CultureInfo.InvariantCulture)
                : null));
    }

    private Result<FxRateVisualView> RateVisual(IBankingReadContext context, GetFxRateVisualQuery query)
    {
        long asOf = StatisticsAsOfMinute(clock.Now());

        if (Snapshot(context, query.MarketId, MinuteBucketSeconds, asOf) is not { } snapshot)
        {
            return Result<FxRateVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        long? bid = snapshot.Bids.Count > 0 ? snapshot.Bids[0].PriceUnits : null;
        long? ask = snapshot.Asks.Count > 0 ? snapshot.Asks[0].PriceUnits : null;

        return Result<FxRateVisualView>.Success(new FxRateVisualView(
            snapshot.MarketId,
            asOf,
            snapshot.LastTradePriceUnits,
            bid,
            ask,
            bid is { } b && ask is { } a ? checked(a - b) : null,
            snapshot.Buckets.Count == 0 ? 0 : snapshot.Buckets.Max(static bucket => bucket.HighPriceUnits),
            snapshot.Buckets.Count == 0 ? 0 : snapshot.Buckets.Min(static bucket => bucket.LowPriceUnits),
            snapshot.Buckets.Sum(static bucket => bucket.BaseVolumeMinor),
            snapshot.SummaryVersion,
            snapshot.OrderBookVersion,
            CacheKeyOf(asOf, snapshot)));
    }

    private Result<FxBoardVisualView> BoardVisual(
        IBankingReadContext context,
        GetFxBoardVisualQuery query)
    {
        long asOf = StatisticsAsOfMinute(clock.Now());

        return Snapshot(context, query.MarketId, MinuteBucketSeconds, asOf) is not { } snapshot
            ? Result<FxBoardVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound)
            : Result<FxBoardVisualView>.Success(new FxBoardVisualView(
                snapshot.MarketId,
                asOf,
                snapshot.Bids,
                snapshot.Asks,
                snapshot.OrderBookVersion,
                CacheKeyOf(asOf, snapshot)));
    }

    private Result<FxChartVisualView> ChartVisual(
        IBankingReadContext context,
        GetFxChartVisualQuery query)
    {
        if (query.BucketSeconds is not (60 or 300 or 3600))
        {
            return Result<FxChartVisualView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxBucketInvalid);
        }

        if (query.WindowSeconds <= 0L)
        {
            return Result<FxChartVisualView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxBucketInvalid);
        }

        long asOf = StatisticsAsOfMinute(clock.Now());

        return Snapshot(context, query.MarketId, query.BucketSeconds, asOf, query.WindowSeconds)
            is not { } snapshot
            ? Result<FxChartVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound)
            : Result<FxChartVisualView>.Success(new FxChartVisualView(
                snapshot.MarketId,
                snapshot.PairCode,
                snapshot.PriceScale,
                snapshot.BaseMinorUnitDigits,
                query.BucketSeconds,
                asOf,
                snapshot.Buckets,
                snapshot.SummaryVersion,
                CacheKeyOf(asOf, snapshot)));
    }

    private static FxVisualSnapshot? Snapshot(
        IBankingReadContext context,
        FxMarketId marketId,
        int bucketSeconds,
        long statisticsAsOfMinute,
        long windowSeconds = RollingWindowSeconds) =>
        context.FxVisuals.Read(
            marketId,
            bucketSeconds,
            statisticsAsOfMinute - windowSeconds,
            statisticsAsOfMinute,
            DepthLevels);

    private static FxVisualCacheKey CacheKeyOf(long statisticsAsOfMinute, FxVisualSnapshot snapshot) =>
        new(
            statisticsAsOfMinute,
            snapshot.SummaryVersion,
            snapshot.OrderBookVersion,
            snapshot.ProjectionVersion);

    internal static long StatisticsAsOfMinute(UtcTimestamp now) =>
        now.UnixMilliseconds / 60_000 * 60;

    private static long? Cursor(string? cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    internal static FxOrderView ToView(FxOrder order) =>
        new(
            order.Id,
            order.MarketId,
            order.Side,
            order.OrderType,
            order.Status,
            order.OriginalBaseMinor,
            order.FilledBaseMinor,
            order.RemainingBaseMinor,
            order.PriceUnits);
}
