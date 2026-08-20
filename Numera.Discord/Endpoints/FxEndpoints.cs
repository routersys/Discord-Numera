using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("fx", "外国為替市場を操作します。")]
public sealed class FxEndpoints : IEconomyEndpoint
{
    private const int MinuteBucket = 60;
    private const int FiveMinuteBucket = 300;
    private const int HourBucket = 3600;

    private const int ChartMinorUnitDigits = 4;

    private const string TypeLimit = "LIMIT";

    private const string TypeMarketIoc = "MARKET_IOC";

    private const string TypeMarketFok = "MARKET_FOK";

    private readonly IFxApplicationService markets;
    private readonly ICustomerAccountApplicationService customers;
    private readonly ITextCatalog catalog;
    private readonly IFxChartImageRenderer charts;

    public FxEndpoints(
        IFxApplicationService markets,
        ICustomerAccountApplicationService customers,
        ITextCatalog catalog,
        IFxChartImageRenderer charts)
    {
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(charts);

        this.markets = markets;
        this.customers = customers;
        this.catalog = catalog;
        this.charts = charts;
    }

    [EconomySlashCommand("market", "為替市場の概要を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> MarketAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxBoardVisualView> result = await markets
            .GetFxBoardVisualAsync(new GetFxBoardVisualQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxMarket,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["bestBid"] = Price(result.Value.Bids.Count > 0 ? result.Value.Bids[0].PriceUnits : null),
                    ["bestAsk"] = Price(result.Value.Asks.Count > 0 ? result.Value.Asks[0].PriceUnits : null),
                    ["orderBookVersion"] =
                        result.Value.OrderBookVersion.ToString(CultureInfo.InvariantCulture),
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("rate", "為替レートを表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> RateAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxRateVisualView> result = await markets
            .GetFxRateVisualAsync(new GetFxRateVisualQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxRate,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["lastTrade"] = Price(result.Value.LastTradePriceUnits),
                    ["bestBid"] = Price(result.Value.BestBidPriceUnits),
                    ["bestAsk"] = Price(result.Value.BestAskPriceUnits),
                    ["spread"] = Price(result.Value.SpreadPriceUnits),
                    ["high"] = result.Value.High24hPriceUnits.ToString(CultureInfo.InvariantCulture),
                    ["low"] = result.Value.Low24hPriceUnits.ToString(CultureInfo.InvariantCulture),
                    ["volume"] = result.Value.Volume24hBaseMinor.ToString(CultureInfo.InvariantCulture),
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("board", "板情報を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> BoardAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxBoardVisualView> result = await markets
            .GetFxBoardVisualAsync(new GetFxBoardVisualQuery(id), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Bids.Count == 0 && result.Value.Asks.Count == 0
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxBoardEmpty, new Dictionary<string, string>(StringComparer.Ordinal))
            : DiscordEndpointResponse.Message(
                ViewKeys.FxBoard,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["bids"] = Depth(result.Value.Bids),
                    ["asks"] = Depth(result.Value.Asks),
                });
    }

    [EconomySlashCommand("chart", "為替チャートを表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> ChartAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        [EconomyOption("interval", "足の長さを選びます。", false)]
        [EconomyChoice("1分足", "60")]
        [EconomyChoice("5分足", "300")]
        [EconomyChoice("1時間足", "3600")]
        string? interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        int bucket = interval switch
        {
            "300" => FiveMinuteBucket,
            "3600" => HourBucket,
            _ => MinuteBucket,
        };

        Result<FxChartVisualView> result = await markets
            .GetFxChartVisualAsync(new GetFxChartVisualQuery(id, bucket), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        if (result.Value.Buckets.Count == 0)
        {
            return DiscordEndpointResponse.Message(
                ViewKeys.FxChartEmpty, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        Dictionary<string, string> data = new(StringComparer.Ordinal)
        {
            ["count"] = result.Value.Buckets.Count.ToString(CultureInfo.InvariantCulture),
            ["interval"] = bucket.ToString(CultureInfo.InvariantCulture),
        };

        FxChartImage? image = charts.TryRender(new FxChartRenderModel(
            market, bucket, result.Value.Buckets, ChartMinorUnitDigits));

        return image is { } rendered
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxChart,
                data,
                DiscordResponseBody.WithAttachment(
                    new DiscordResponseAttachment(rendered.FileName, rendered.Content)))
            : DiscordEndpointResponse.Message(ViewKeys.FxChart, data);
    }

    [EconomySlashCommand("orders", "自分の為替注文を一覧します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> OrdersAsync(
        DiscordEndpointContext context,
        [EconomyOption("cursor", "次のページの位置を指定します。", false)] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<FxOrderPageView> result = await markets
            .ListFxOrdersAsync(
                new ListFxOrdersQuery(customer.Value.Id, cursor),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Items.Count == 0
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxOrdersEmpty, new Dictionary<string, string>(StringComparer.Ordinal))
            : DiscordEndpointResponse.Message(
                ViewKeys.FxOrders,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["count"] = result.Value.Items.Count.ToString(CultureInfo.InvariantCulture),
                    ["items"] = string.Join(
                        '\n',
                        result.Value.Items.Select(item =>
                            $"{Side(item.Side)} {item.RemainingBaseMinor} {Status(item.Status.ToToken())}")),
                });
    }

    [EconomySlashCommand("history", "約定履歴を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> HistoryAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        [EconomyOption("cursor", "次のページの位置を指定します。", false)] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        Result<FxTradeHistoryPageView> result = await markets
            .GetFxHistoryAsync(new GetFxHistoryQuery(id, cursor), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Items.Count == 0
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxHistoryEmpty, new Dictionary<string, string>(StringComparer.Ordinal))
            : DiscordEndpointResponse.Message(
                ViewKeys.FxHistory,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["count"] = result.Value.Items.Count.ToString(CultureInfo.InvariantCulture),
                });
    }

    [EconomySlashCommand("order", "為替注文を出します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> OrderAsync(
        DiscordEndpointContext context,
        [EconomyOption("market", "市場を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxMarketProviderKey)]
        string market,
        [EconomyOption("side", "売買の向きを選びます。", true)]
        [EconomyChoice("基軸通貨を買う", "BUY_BASE")]
        [EconomyChoice("基軸通貨を売る", "SELL_BASE")]
        string side,
        [EconomyOption("type", "注文の種類を選びます。", true)]
        [EconomyChoice("指値", TypeLimit)]
        [EconomyChoice("成行（残数量は失効）", TypeMarketIoc)]
        [EconomyChoice("成行（全量約定のみ）", TypeMarketFok)]
        string type,
        [EconomyOption("amount", "基軸通貨の数量を入力します。", true)] long amount,
        [EconomyOption("source", "支払う通貨の口座を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.DepositAccountProviderKey)]
        string source,
        [EconomyOption("destination", "受け取る通貨の口座を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.DepositAccountProviderKey)]
        string destination,
        [EconomyOption("price", "指値の価格を入力します。", false)] string? price,
        [EconomyOption("slippage", "成行の許容スリッページをbpsで入力します。", false)] string? slippage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!FxMarketReference.TryParse(market, out FxMarketId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        if (!DepositAccountReference.TryParse(source, out DepositAccountId sourceAccountId) ||
            !DepositAccountReference.TryParse(destination, out DepositAccountId destinationAccountId))
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        FxOrderType orderType = type switch
        {
            TypeMarketIoc => FxOrderType.MarketIoc,
            TypeMarketFok => FxOrderType.MarketFok,
            _ => FxOrderType.Limit,
        };

        long? priceUnits = orderType == FxOrderType.Limit ? Number(price) : null;
        int? slippageBps = orderType == FxOrderType.Limit ? null : (int?)Number(slippage);

        if (orderType == FxOrderType.Limit && priceUnits is null)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.FxPriceNotOnTick);
        }

        if (orderType != FxOrderType.Limit && slippageBps is null)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.FxSlippageInvalid);
        }

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<FxOrderView> result = await markets
            .PlaceFxOrderAsync(
                new PlaceFxOrderCommand(
                    EndpointAuthorization.ToActor(context),
                    id,
                    customer.Value.Id,
                    side == "SELL_BASE" ? FxOrderSide.SellBase : FxOrderSide.BuyBase,
                    orderType,
                    amount,
                    priceUnits,
                    slippageBps,
                    sourceAccountId,
                    destinationAccountId,
                    IdempotencyKey.Create(
                        "fx-order", context.InteractionId.ToString(CultureInfo.InvariantCulture))),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxOrderPlaced,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["status"] = Status(result.Value.Status.ToToken()),
                    ["filled"] = result.Value.FilledBaseMinor.ToString(CultureInfo.InvariantCulture),
                    ["remaining"] =
                        result.Value.RemainingBaseMinor.ToString(CultureInfo.InvariantCulture),
                })
            : EndpointFailures.From(result.Error!);
    }

    private static long? Number(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    [EconomySlashCommand("cancel", "為替注文を取り消します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> CancelAsync(
        DiscordEndpointContext context,
        [EconomyOption("order", "取り消す注文を指定します。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.FxOrderProviderKey)]
        string order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        if (!FxOrderReference.TryParse(order, out FxOrderId id))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxOrderNotFound);
        }

        Result<FxOrderView> result = await markets
            .CancelFxOrderAsync(
                new CancelFxOrderCommand(
                    EndpointAuthorization.ToActor(context),
                    customer.Value.Id,
                    id,
                    IdempotencyKey.Create(
                        "fx-cancel", context.InteractionId.ToString(CultureInfo.InvariantCulture))),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.FxOrderCancelled,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["status"] = Status(result.Value.Status.ToToken()),
                })
            : EndpointFailures.From(result.Error!);
    }

    private Task<Result<CustomerAccountStatusView>> ResolveAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken) =>
        customers.GetCustomerAccountStatusAsync(
            new GetCustomerAccountStatusQuery(context.UserId), cancellationToken);

    private string Status(string token) => catalog.Resolve(ViewKeys.StatusOf(token));

    private string Side(FxOrderSide side) => catalog.Resolve(ViewKeys.FxSideOf(side.ToToken()));

    private static string Price(long? priceUnits) =>
        priceUnits?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Depth(IReadOnlyList<FxDepthLevel> levels) =>
        string.Join('\n', levels.Select(static level => $"{level.PriceUnits} {level.BaseMinor}"));
}

internal static class FxMarketReference
{
    internal static string Format(FxMarketId id) =>
        new Guid(id.Value.ToByteArray(), bigEndian: true).ToString();

    internal static bool TryParse(string text, out FxMarketId id)
    {
        if (Guid.TryParse(text, out Guid parsed))
        {
            id = FxMarketId.FromValue(EntityIdValue.FromBytes(parsed.ToByteArray(bigEndian: true)));
            return true;
        }

        id = default;
        return false;
    }
}

internal static class FxOrderReference
{
    internal static string Format(FxOrderId id) =>
        new Guid(id.Value.ToByteArray(), bigEndian: true).ToString();

    internal static bool TryParse(string text, out FxOrderId id)
    {
        if (Guid.TryParse(text, out Guid parsed))
        {
            id = FxOrderId.FromValue(EntityIdValue.FromBytes(parsed.ToByteArray(bigEndian: true)));
            return true;
        }

        id = default;
        return false;
    }
}
