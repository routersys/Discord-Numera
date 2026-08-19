using Numera.Application.Abstractions;
using Numera.Discord.Rendering;
using Numera.Domain.Common;
using SkiaSharp;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class FxChartRendererTests
{
    private sealed class StubFontProvider : ICardFontProvider
    {
        public SKTypeface Resolve(CardFontRole role) => SKTypeface.Default;

        public bool TryResolveFallback(out SKTypeface typeface)
        {
            typeface = SKTypeface.Default;
            return true;
        }
    }

    private sealed class RecordingDiagnostics : ICardRenderDiagnostics
    {
        public List<string> Unavailable { get; } = [];

        public void MissingGlyph(int codePoint)
        {
        }

        public void RendererUnavailable(string reason) => Unavailable.Add(reason);
    }

    private static FxOhlcBucket Bucket(long start, long open, long high, long low, long close) =>
        new(
            FxMarketId.FromValue(EntityIdValue.FromBits(1)),
            60,
            start,
            open,
            high,
            low,
            close,
            BaseVolumeMinor: 100,
            QuoteVolumeMinor: 200,
            LastTradeSequenceNo: start,
            ProjectionVersion: 1);

    private static IReadOnlyList<FxOhlcBucket> Series(int count)
    {
        List<FxOhlcBucket> buckets = [];

        for (int index = 0; index < count; index++)
        {
            long baseline = 10_000 + (index * 7);

            buckets.Add(Bucket(
                index * 60L, baseline, baseline + 40, baseline - 30, baseline + ((index % 2 == 0) ? 20 : -20)));
        }

        return buckets;
    }

    private static FxChartRenderer Renderer() => new(new StubFontProvider());

    [TestMethod]
    public void TheRenderedChartIsAPortableNetworkGraphic()
    {
        byte[] content = Renderer().Render(new FxChartRenderRequest("JPY/USD", "1m", Series(24), 4));

        Assert.IsGreaterThan(0, content.Length);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, content[..4]);
    }

    [TestMethod]
    public void TheCanvasKeepsTheDeclaredSize()
    {
        byte[] content = Renderer().Render(new FxChartRenderRequest("JPY/USD", "1m", Series(8), 4));

        using SKBitmap decoded = SKBitmap.Decode(content);

        Assert.AreEqual(FxChartCanvas.Width, decoded.Width);
        Assert.AreEqual(FxChartCanvas.Height, decoded.Height);
    }

    [TestMethod]
    public void OnlyTheNewestCandlesAreDrawn()
    {
        IReadOnlyList<FxOhlcBucket> windowed =
            FxChartRenderer.Window(Series(FxChartCanvas.MaximumCandles + 40));

        Assert.AreEqual(FxChartCanvas.MaximumCandles, windowed.Count);
        Assert.AreEqual(40L * 60L, windowed[0].BucketStart);
    }

    [TestMethod]
    public void AnUnorderedSeriesIsSortedBeforeDrawing()
    {
        IReadOnlyList<FxOhlcBucket> windowed = FxChartRenderer.Window(
        [
            Bucket(180, 10, 12, 9, 11),
            Bucket(60, 10, 12, 9, 11),
            Bucket(120, 10, 12, 9, 11),
        ]);

        CollectionAssert.AreEqual(
            new[] { 60L, 120L, 180L },
            windowed.Select(static bucket => bucket.BucketStart).ToArray());
    }

    [TestMethod]
    public void AFlatSeriesStillProducesANonZeroRange()
    {
        (long low, long high) = FxChartRenderer.Range([Bucket(60, 500, 500, 500, 500)]);

        Assert.AreEqual(500L, low);
        Assert.AreEqual(501L, high);
    }

    [TestMethod]
    public void AnEmptySeriesFallsBackToTheUnitRange()
    {
        (long low, long high) = FxChartRenderer.Range([]);

        Assert.AreEqual(0L, low);
        Assert.AreEqual(1L, high);
    }

    [TestMethod]
    public void TheHighestPriceSitsAtTheTopOfThePlot()
    {
        float top = FxChartRenderer.PlotY(200, 100, 200);
        float bottom = FxChartRenderer.PlotY(100, 100, 200);

        Assert.AreEqual(FxChartCanvas.MarginTop, top, 0.01f);
        Assert.AreEqual(FxChartCanvas.MarginTop + FxChartCanvas.PlotHeight, bottom, 0.01f);
    }

    [TestMethod]
    public void RisingAndFallingCandlesUseDistinctColours()
    {
        Assert.AreEqual(FxChartCanvas.RisingRgb, FxChartRenderer.Trend(Bucket(60, 10, 12, 9, 11)));
        Assert.AreEqual(FxChartCanvas.FallingRgb, FxChartRenderer.Trend(Bucket(60, 11, 12, 9, 10)));
        Assert.AreEqual(FxChartCanvas.FlatRgb, FxChartRenderer.Trend(Bucket(60, 10, 12, 9, 10)));
    }

    [TestMethod]
    public void PricesArePrintedWithTheCurrencyMinorUnits()
    {
        Assert.AreEqual("1.2345", FxChartRenderer.FormatPrice(12_345, 4));
        Assert.AreEqual("0.0001", FxChartRenderer.FormatPrice(1, 4));
        Assert.AreEqual("12345", FxChartRenderer.FormatPrice(12_345, 0));
    }

    [TestMethod]
    public void TheImageRendererReportsTheCanonicalFileNameAndSize()
    {
        SkiaFxChartImageRenderer renderer = new(Renderer(), new RecordingDiagnostics());

        FxChartImage? image = renderer.TryRender(
            new FxChartRenderModel("JPY/USD", 60, Series(10), 4));

        Assert.IsNotNull(image);
        Assert.AreEqual("fx-chart.png", image.FileName);
        Assert.AreEqual(FxChartCanvas.Width, image.Width);
        Assert.AreEqual(FxChartCanvas.Height, image.Height);
    }

    [TestMethod]
    public void AnEmptySeriesRendersNoImage()
    {
        SkiaFxChartImageRenderer renderer = new(Renderer(), new RecordingDiagnostics());

        Assert.IsNull(renderer.TryRender(new FxChartRenderModel("JPY/USD", 60, [], 4)));
    }

    [TestMethod]
    public void TheIntervalLabelFollowsTheBucketLength()
    {
        Assert.AreEqual("1m", SkiaFxChartImageRenderer.IntervalLabel(60));
        Assert.AreEqual("5m", SkiaFxChartImageRenderer.IntervalLabel(300));
        Assert.AreEqual("1h", SkiaFxChartImageRenderer.IntervalLabel(3600));
    }
}
