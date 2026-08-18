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

public sealed record GetFxChartVisualQuery(FxMarketId MarketId, int BucketSeconds);

public sealed record ListFxOrdersQuery(PartyId ParticipantPartyId, string? Cursor);

public sealed record GetFxHistoryQuery(FxMarketId MarketId, string? Cursor);

public sealed record PlaceFxOrderCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId,
    CustomerAccountId CustomerAccountId,
    PartyId ParticipantPartyId,
    FxOrderSide Side,
    FxOrderType OrderType,
    long BaseMinor,
    long? PriceUnits,
    int? MaximumSlippageBps,
    FxFundingEndpointId SourceFundingEndpointId,
    FxSettlementEndpointId DestinationSettlementEndpointId,
    HoldId SourceHoldId,
    IdempotencyKey IdempotencyKey);

public sealed record CancelFxOrderCommand(
    AuthorizationContext Actor,
    PartyId ParticipantPartyId,
    FxOrderId FxOrderId);

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
    long OrderBookVersion);

public sealed record FxBoardVisualView(
    FxMarketId MarketId,
    long StatisticsAsOfMinute,
    IReadOnlyList<FxDepthLevel> Bids,
    IReadOnlyList<FxDepthLevel> Asks,
    long OrderBookVersion);

public sealed record FxChartVisualView(
    FxMarketId MarketId,
    int BucketSeconds,
    long StatisticsAsOfMinute,
    IReadOnlyList<FxOhlcBucket> Buckets,
    long SummaryVersion);

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

public sealed class FxApplicationService : IFxApplicationService
{
    public const int DepthLevels = 10;

    public const long RollingWindowSeconds = 86400;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public FxApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
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

        return writeGateway.ExecuteAsync(unitOfWork => RateVisual(unitOfWork, query), cancellationToken);
    }

    public Task<Result<FxBoardVisualView>> GetFxBoardVisualAsync(
        GetFxBoardVisualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => BoardVisual(unitOfWork, query), cancellationToken);
    }

    public Task<Result<FxChartVisualView>> GetFxChartVisualAsync(
        GetFxChartVisualQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => ChartVisual(unitOfWork, query), cancellationToken);
    }

    public Task<Result<FxOrderPageView>> ListFxOrdersAsync(
        ListFxOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                IReadOnlyList<FxOrder> fetched = unitOfWork.Fx.ListParticipantOrders(
                    query.ParticipantPartyId,
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
            unitOfWork => unitOfWork.Fx.FindMarket(query.MarketId) is null
                ? Result<FxTradeHistoryPageView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound)
                : Result<FxTradeHistoryPageView>.Success(
                    new FxTradeHistoryPageView([], null)),
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

    private Result<FxOrderView> Place(IBankingUnitOfWork unitOfWork, PlaceFxOrderCommand command)
    {
        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<FxOrderView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        if (!market.IsTradable)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        if (!FxPricing.IsLotMultiple(command.BaseMinor, market.LotSizeBaseMinor))
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
        }

        if (command.OrderType == FxOrderType.Limit
            && !FxPricing.IsTickMultiple(command.PriceUnits ?? 0, market.TickSizePriceUnits))
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxPriceNotOnTick);
        }

        if (market.CurrentPolicyVersionId is not { } policyVersionId)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketPolicyMissing);
        }

        if (Crosses(unitOfWork, market, command))
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
        }

        if (command.OrderType != FxOrderType.Limit)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
        }

        FxOrder order;

        try
        {
            order = FxOrder.Place(
                FxOrderId.FromValue(idGenerator.NextId()),
                market.Id,
                FxParticipantKind.Customer,
                command.ParticipantPartyId,
                command.CustomerAccountId,
                command.Side,
                command.OrderType,
                FxTimeInForce.GoodTilCancelled,
                command.PriceUnits,
                command.MaximumSlippageBps,
                command.BaseMinor,
                market.TakeOrderSequence(),
                command.SourceFundingEndpointId,
                command.DestinationSettlementEndpointId,
                command.SourceHoldId,
                policyVersionId,
                clock.Now());
        }
        catch (InvariantViolationException)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxOrderInvalid);
        }

        unitOfWork.Fx.AddOrder(order);
        unitOfWork.Fx.UpdateMarket(market);
        BumpOrderBook(unitOfWork, market.Id);

        return Result<FxOrderView>.Success(ToView(order));
    }

    private Result<FxOrderView> Cancel(IBankingUnitOfWork unitOfWork, CancelFxOrderCommand command)
    {
        if (unitOfWork.Fx.FindOrder(command.FxOrderId) is not { } order
            || order.ParticipantPartyId != command.ParticipantPartyId)
        {
            return Result<FxOrderView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.FxOrderNotFound);
        }

        if (order.IsTerminal)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxOrderAlreadyTerminal);
        }

        UtcTimestamp now = clock.Now();

        order.Cancel(now);
        unitOfWork.Fx.UpdateOrder(order);

        if (unitOfWork.Holds.Find(order.SourceHoldId) is { } hold && hold.Status == HoldStatus.Active)
        {
            hold.Release(now);
            unitOfWork.Holds.Update(hold);
        }

        BumpOrderBook(unitOfWork, order.MarketId);

        return Result<FxOrderView>.Success(ToView(order));
    }

    private static bool Crosses(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        PlaceFxOrderCommand command)
    {
        if (command.OrderType != FxOrderType.Limit || command.PriceUnits is not { } price)
        {
            return true;
        }

        FxOrderSide opposite = command.Side == FxOrderSide.BuyBase
            ? FxOrderSide.SellBase
            : FxOrderSide.BuyBase;

        IReadOnlyList<FxDepthLevel> best = unitOfWork.Fx.ReadDepth(market.Id, opposite, 1);

        if (best.Count == 0)
        {
            return false;
        }

        return command.Side == FxOrderSide.BuyBase
            ? price >= best[0].PriceUnits
            : price <= best[0].PriceUnits;
    }

    private void BumpOrderBook(IBankingUnitOfWork unitOfWork, FxMarketId marketId)
    {
        FxMarketSummary current = unitOfWork.Fx.FindSummary(marketId)
            ?? new FxMarketSummary(marketId, null, null, 1, 1, clock.Now());

        unitOfWork.Fx.UpsertSummary(current with
        {
            OrderBookVersion = checked(current.OrderBookVersion + 1),
            UpdatedAt = clock.Now(),
        });
    }

    private Result<FxRateVisualView> RateVisual(IBankingUnitOfWork unitOfWork, GetFxRateVisualQuery query)
    {
        if (unitOfWork.Fx.FindMarket(query.MarketId) is not { } market)
        {
            return Result<FxRateVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        long asOf = StatisticsAsOfMinute(clock.Now());
        long windowStart = asOf - RollingWindowSeconds;

        IReadOnlyList<FxOhlcBucket> buckets =
            unitOfWork.Fx.ListBuckets(market.Id, 60, windowStart, asOf);

        IReadOnlyList<FxDepthLevel> bids = unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.BuyBase, 1);
        IReadOnlyList<FxDepthLevel> asks = unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.SellBase, 1);
        FxMarketSummary? summary = unitOfWork.Fx.FindSummary(market.Id);

        long? bid = bids.Count > 0 ? bids[0].PriceUnits : null;
        long? ask = asks.Count > 0 ? asks[0].PriceUnits : null;

        return Result<FxRateVisualView>.Success(new FxRateVisualView(
            market.Id,
            asOf,
            summary?.LastTradePriceUnits,
            bid,
            ask,
            bid is { } b && ask is { } a ? checked(a - b) : null,
            buckets.Count == 0 ? 0 : buckets.Max(static bucket => bucket.HighPriceUnits),
            buckets.Count == 0 ? 0 : buckets.Min(static bucket => bucket.LowPriceUnits),
            buckets.Sum(static bucket => bucket.BaseVolumeMinor),
            summary?.SummaryVersion ?? 1,
            summary?.OrderBookVersion ?? 1));
    }

    private Result<FxBoardVisualView> BoardVisual(
        IBankingUnitOfWork unitOfWork,
        GetFxBoardVisualQuery query)
    {
        if (unitOfWork.Fx.FindMarket(query.MarketId) is not { } market)
        {
            return Result<FxBoardVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        return Result<FxBoardVisualView>.Success(new FxBoardVisualView(
            market.Id,
            StatisticsAsOfMinute(clock.Now()),
            unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.BuyBase, DepthLevels),
            unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.SellBase, DepthLevels),
            unitOfWork.Fx.FindSummary(market.Id)?.OrderBookVersion ?? 1));
    }

    private Result<FxChartVisualView> ChartVisual(
        IBankingUnitOfWork unitOfWork,
        GetFxChartVisualQuery query)
    {
        if (query.BucketSeconds is not (60 or 300 or 3600))
        {
            return Result<FxChartVisualView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxBucketInvalid);
        }

        if (unitOfWork.Fx.FindMarket(query.MarketId) is not { } market)
        {
            return Result<FxChartVisualView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        long asOf = StatisticsAsOfMinute(clock.Now());

        return Result<FxChartVisualView>.Success(new FxChartVisualView(
            market.Id,
            query.BucketSeconds,
            asOf,
            unitOfWork.Fx.ListBuckets(market.Id, query.BucketSeconds, asOf - RollingWindowSeconds, asOf),
            unitOfWork.Fx.FindSummary(market.Id)?.SummaryVersion ?? 1));
    }

    internal static long StatisticsAsOfMinute(UtcTimestamp now) =>
        now.UnixMilliseconds / 60_000 * 60;

    private static long? Cursor(string? cursor) =>
        long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    private static FxOrderView ToView(FxOrder order) =>
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
