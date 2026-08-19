namespace Numera.Application.Abstractions;

public sealed record FxChartRenderModel(
    string Title,
    int BucketSeconds,
    IReadOnlyList<FxOhlcBucket> Buckets,
    int MinorUnitDigits);

public sealed record FxChartImage(string FileName, int Width, int Height, byte[] Content);

public interface IFxChartImageRenderer
{
    FxChartImage? TryRender(FxChartRenderModel model);
}
