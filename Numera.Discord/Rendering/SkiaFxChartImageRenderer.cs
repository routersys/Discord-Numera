using Numera.Application.Abstractions;

namespace Numera.Discord.Rendering;

internal sealed class SkiaFxChartImageRenderer : IFxChartImageRenderer
{
    internal const string FileName = "fx-chart.png";

    private readonly IFxChartRenderer renderer;
    private readonly ICardRenderDiagnostics diagnostics;

    public SkiaFxChartImageRenderer(IFxChartRenderer renderer, ICardRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.renderer = renderer;
        this.diagnostics = diagnostics;
    }

    public FxChartImage? TryRender(FxChartRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Buckets.Count == 0)
        {
            return null;
        }

        try
        {
            byte[] content = renderer.Render(new FxChartRenderRequest(
                model.Title,
                IntervalLabel(model.BucketSeconds),
                model.Buckets,
                model.MinorUnitDigits));

            return new FxChartImage(FileName, FxChartCanvas.Width, FxChartCanvas.Height, content);
        }
        catch (CardFontManifestException failure)
        {
            diagnostics.RendererUnavailable(failure.Message);

            return null;
        }
    }

    internal static string IntervalLabel(int bucketSeconds) => bucketSeconds switch
    {
        300 => "5m",
        3600 => "1h",
        _ => "1m",
    };
}
