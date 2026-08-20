using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

internal sealed record FxChartState(
    FxMarketId MarketId,
    FxChartPeriod Period,
    FxChartSeriesStyle Style,
    FxChartTheme Theme)
{
    internal const int MarketLength = 32;
    internal const int TokenLength = MarketLength + 3;

    internal string ToToken() =>
        Convert.ToHexString(MarketId.Value.ToByteArray()).ToLowerInvariant()
        + PeriodCode(Period)
        + (Style == FxChartSeriesStyle.Candle ? "C" : "L")
        + (Theme == FxChartTheme.Dark ? "D" : "L");

    internal static bool TryParse(string? token, out FxChartState state)
    {
        state = default!;

        if (token is null || token.Length != TokenLength)
        {
            return false;
        }

        byte[] raw;

        try
        {
            raw = Convert.FromHexString(token[..MarketLength]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (ResolvePeriod(token[MarketLength]) is not { } period)
        {
            return false;
        }

        state = new FxChartState(
            FxMarketId.FromValue(EntityIdValue.FromBytes(raw)),
            period,
            token[MarketLength + 1] == 'C' ? FxChartSeriesStyle.Candle : FxChartSeriesStyle.Line,
            token[MarketLength + 2] == 'D' ? FxChartTheme.Dark : FxChartTheme.Light);

        return true;
    }

    internal static string PeriodCode(FxChartPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return period.Token switch
        {
            "24H" => "D",
            "7D" => "W",
            "30D" => "M",
            _ => "H",
        };
    }

    private static FxChartPeriod? ResolvePeriod(char code) => code switch
    {
        'H' => FxChartPeriod.Hour,
        'D' => FxChartPeriod.Day,
        'W' => FxChartPeriod.Week,
        'M' => FxChartPeriod.Month,
        _ => null,
    };
}

public sealed partial class FxEndpoints
{
    internal const string ChartPeriodAction = "fx-chart-period";
    internal const string ChartStyleAction = "fx-chart-style";
    internal const string ChartThemeAction = "fx-chart-theme";

    [EconomyComponent(EconomyComponentKind.Select, ChartPeriodAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> SelectChartPeriodAsync(
        DiscordEndpointContext context,
        DiscordComponentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (!FxChartState.TryParse(input.SessionToken, out FxChartState state))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        FxChartPeriod chosen = FxChartPeriod.Resolve(
            input.Values.Count > 0 ? input.Values[0] : state.Period.Token);

        return await RenderChartAsync(state with { Period = chosen }, update: true, cancellationToken)
            .ConfigureAwait(false);
    }

    [EconomyComponent(EconomyComponentKind.Button, ChartStyleAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> ToggleChartStyleAsync(
        DiscordEndpointContext context,
        DiscordComponentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (!FxChartState.TryParse(input.SessionToken, out FxChartState state))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        FxChartSeriesStyle flipped = state.Style == FxChartSeriesStyle.Candle
            ? FxChartSeriesStyle.Line
            : FxChartSeriesStyle.Candle;

        return await RenderChartAsync(state with { Style = flipped }, update: true, cancellationToken)
            .ConfigureAwait(false);
    }

    [EconomyComponent(EconomyComponentKind.Button, ChartThemeAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> ToggleChartThemeAsync(
        DiscordEndpointContext context,
        DiscordComponentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (!FxChartState.TryParse(input.SessionToken, out FxChartState state))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        FxChartTheme flipped = state.Theme == FxChartTheme.Dark
            ? FxChartTheme.Light
            : FxChartTheme.Dark;

        return await RenderChartAsync(state with { Theme = flipped }, update: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DiscordEndpointResponse> RenderChartAsync(
        FxChartState state,
        bool update,
        CancellationToken cancellationToken)
    {
        Result<FxChartVisualView> result = await markets
            .GetFxChartVisualAsync(
                new GetFxChartVisualQuery(
                    state.MarketId, state.Period.BucketSeconds, state.Period.WindowSeconds),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        FxChartVisualView view = result.Value;
        DiscordResponseComponents components = ChartComponents(state);

        if (view.Buckets.Count == 0)
        {
            Dictionary<string, string> empty = new(StringComparer.Ordinal)
            {
                ["pair"] = view.PairCode,
                ["period"] = state.Period.Token,
            };

            return Compose(
                update,
                ViewKeys.FxChartEmpty,
                empty,
                new DiscordResponseBody([], components));
        }

        Dictionary<string, string> data = new(StringComparer.Ordinal)
        {
            ["pair"] = view.PairCode,
            ["period"] = state.Period.Token,
            ["count"] = view.Buckets.Count.ToString(CultureInfo.InvariantCulture),
            ["change"] = FxChartScale.FormatChange(
                view.Buckets[0].OpenPriceUnits, view.Buckets[^1].ClosePriceUnits),
        };

        FxChartImage? image = charts.TryRender(new FxChartRenderModel(
            view.PairCode,
            state.Period.Token,
            state.Period.BucketSeconds,
            view.Buckets,
            view.PriceScale,
            ChartMetrics(view),
            state.Style,
            state.Theme));

        DiscordResponseBody body = image is { } rendered
            ? new DiscordResponseBody(
                [],
                components,
                new DiscordResponseAttachment(rendered.FileName, rendered.Content))
            : new DiscordResponseBody([], components);

        return Compose(update, ViewKeys.FxChart, data, body);
    }

    private static DiscordEndpointResponse Compose(
        bool update,
        string viewKey,
        IReadOnlyDictionary<string, string> data,
        DiscordResponseBody body) =>
        update
            ? DiscordEndpointResponse.UpdateMessage(viewKey, data, body)
            : DiscordEndpointResponse.Message(viewKey, data, body);

    private DiscordResponseComponents ChartComponents(FxChartState state)
    {
        string token = state.ToToken();

        DiscordResponseSelect select = new(
            DiscordCustomId.Select(ChartPeriodAction, token),
            ViewKeys.FxChartPeriodPlaceholder,
            [
                new DiscordResponseSelectOption(
                    catalog.Resolve(ViewKeys.FxChartPeriodHour), PeriodHour),
                new DiscordResponseSelectOption(
                    catalog.Resolve(ViewKeys.FxChartPeriodDay), PeriodDay),
                new DiscordResponseSelectOption(
                    catalog.Resolve(ViewKeys.FxChartPeriodWeek), PeriodWeek),
                new DiscordResponseSelectOption(
                    catalog.Resolve(ViewKeys.FxChartPeriodMonth), PeriodMonth),
            ]);

        return new DiscordResponseComponents(
            select,
            [
                new DiscordResponseButton(
                    DiscordCustomId.Button(ChartStyleAction, token),
                    state.Style == FxChartSeriesStyle.Candle
                        ? ViewKeys.FxChartToLine
                        : ViewKeys.FxChartToCandle,
                    DiscordButtonStyle.Secondary),
                new DiscordResponseButton(
                    DiscordCustomId.Button(ChartThemeAction, token),
                    state.Theme == FxChartTheme.Dark
                        ? ViewKeys.FxChartToLight
                        : ViewKeys.FxChartToDark,
                    DiscordButtonStyle.Secondary),
            ]);
    }

    private IReadOnlyList<FxChartMetric> ChartMetrics(FxChartVisualView view)
    {
        long high = view.Buckets[0].HighPriceUnits;
        long low = view.Buckets[0].LowPriceUnits;
        long volume = 0L;

        foreach (FxOhlcBucket bucket in view.Buckets)
        {
            high = Math.Max(high, bucket.HighPriceUnits);
            low = Math.Min(low, bucket.LowPriceUnits);
            volume = checked(volume + bucket.BaseVolumeMinor);
        }

        long open = view.Buckets[0].OpenPriceUnits;
        long close = view.Buckets[^1].ClosePriceUnits;

        return
        [
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartStart),
                FxChartScale.FormatPrice(open, view.PriceScale)),
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartEnd),
                FxChartScale.FormatPrice(close, view.PriceScale)),
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartHigh),
                FxChartScale.FormatPrice(high, view.PriceScale)),
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartLow),
                FxChartScale.FormatPrice(low, view.PriceScale)),
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartChange),
                FxChartScale.FormatChange(open, close)),
            new FxChartMetric(
                catalog.Resolve(ViewKeys.FxChartVolume),
                FxChartScale.FormatAmount(volume, view.BaseMinorUnitDigits)),
        ];
    }
}
