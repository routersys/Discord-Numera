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
                model.PairCode,
                model.PeriodLabel,
                model.BucketSeconds,
                model.Buckets,
                model.PriceScale,
                model.Metrics,
                model.Style,
                model.Theme));

            return new FxChartImage(FileName, FxChartCanvas.Width, FxChartCanvas.Height, content);
        }
        catch (CardFontManifestException failure)
        {
            diagnostics.RendererUnavailable(failure.Message);

            return null;
        }
    }
}
