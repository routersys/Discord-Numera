namespace Numera.Application.Abstractions;

public enum FxChartSeriesStyle
{
    Line = 1,
    Candle = 2,
}

public enum FxChartTheme
{
    Dark = 1,
    Light = 2,
}

public sealed record FxChartMetric(string Label, string Value);

public sealed record FxChartRenderModel(
    string PairCode,
    string PeriodLabel,
    int BucketSeconds,
    IReadOnlyList<FxOhlcBucket> Buckets,
    long PriceScale,
    IReadOnlyList<FxChartMetric> Metrics,
    FxChartSeriesStyle Style,
    FxChartTheme Theme);

public sealed record FxChartImage(string FileName, int Width, int Height, byte[] Content);

public interface IFxChartImageRenderer
{
    FxChartImage? TryRender(FxChartRenderModel model);
}
