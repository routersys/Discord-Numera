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

    private static IReadOnlyList<FxChartMetric> Metrics() =>
    [
        new FxChartMetric("start", "1.00"),
        new FxChartMetric("end", "1.10"),
    ];

    private static FxChartRenderRequest Request(
        IReadOnlyList<FxOhlcBucket> buckets,
        FxChartSeriesStyle style = FxChartSeriesStyle.Line,
        FxChartTheme theme = FxChartTheme.Light) =>
        new("JPY/USD", "1H", 60, buckets, 100L, Metrics(), style, theme);

    private static FxChartRenderer Renderer() => new(new StubFontProvider());

    [TestMethod]
    public void TheRenderedChartIsAPortableNetworkGraphic()
    {
        byte[] content = Renderer().Render(Request(Series(24)));

        Assert.IsGreaterThan(0, content.Length);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, content[..4]);
    }

    [TestMethod]
    public void TheCanvasKeepsTheSizeSection27Ag3Declares()
    {
        byte[] content = Renderer().Render(Request(Series(8)));

        using SKBitmap decoded = SKBitmap.Decode(content);

        Assert.AreEqual(1280, decoded.Width);
        Assert.AreEqual(720, decoded.Height);
        Assert.AreEqual(FxChartCanvas.Width, decoded.Width);
        Assert.AreEqual(FxChartCanvas.Height, decoded.Height);
    }

    [TestMethod]
    public void BothSeriesStylesRender()
    {
        byte[] line = Renderer().Render(Request(Series(24), FxChartSeriesStyle.Line));
        byte[] candle = Renderer().Render(Request(Series(24), FxChartSeriesStyle.Candle));

        Assert.IsGreaterThan(0, line.Length);
        Assert.IsGreaterThan(0, candle.Length);
        Assert.IsFalse(line.SequenceEqual(candle));
    }

    [TestMethod]
    public void BothThemesRender()
    {
        byte[] dark = Renderer().Render(Request(Series(24), theme: FxChartTheme.Dark));
        byte[] light = Renderer().Render(Request(Series(24), theme: FxChartTheme.Light));

        Assert.IsFalse(dark.SequenceEqual(light));
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
    public void ASeriesLongerThanTheDrawingBudgetIsDownsampled()
    {
        IReadOnlyList<FxOhlcBucket> windowed =
            FxChartRenderer.Window(Series(FxChartScale.MaximumPoints + 220));

        Assert.AreEqual(FxChartScale.MaximumPoints, windowed.Count);
        Assert.AreEqual(0L, windowed[0].BucketStart);
        Assert.AreEqual(
            (FxChartScale.MaximumPoints + 219) * 60L, windowed[^1].BucketStart);
    }

    [TestMethod]
    public void AFlatSeriesIsCentredInsteadOfPinnedToTheFloor()
    {
        FxChartPriceAxis axis = FxChartScale.Axis([Bucket(60, 150, 150, 150, 150)]);

        float centre = FxChartRenderer.PlotY(150, axis);
        float middle = FxChartCanvas.MarginTop + (FxChartCanvas.PlotHeight / 2f);

        Assert.AreEqual(middle, centre, 1f);
        Assert.IsLessThan(150L, axis.Low);
        Assert.IsGreaterThan(150L, axis.High);
    }

    [TestMethod]
    public void EveryPriceLabelOnAFlatSeriesIsDistinct()
    {
        FxChartPriceAxis axis = FxChartScale.Axis([Bucket(60, 150, 150, 150, 150)]);

        string[] labels =
            [.. axis.Ticks.Select(static tick => FxChartScale.FormatPrice(tick, 100L))];

        Assert.AreEqual(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void ThePriceAxisKeepsTheLabelCountSection27Ag7Allows()
    {
        foreach (int count in new[] { 1, 2, 5, 24, 120 })
        {
            FxChartPriceAxis axis = FxChartScale.Axis(Series(count));

            Assert.IsGreaterThanOrEqualTo(FxChartScale.MinimumTicks, axis.Ticks.Count);
            Assert.IsLessThanOrEqualTo(FxChartScale.MaximumTicks, axis.Ticks.Count);
        }
    }

    [TestMethod]
    public void ThePriceAxisCoversEveryTradedPrice()
    {
        IReadOnlyList<FxOhlcBucket> series = Series(40);
        FxChartPriceAxis axis = FxChartScale.Axis(series);

        Assert.IsLessThanOrEqualTo(series.Min(static bucket => bucket.LowPriceUnits), axis.Low);
        Assert.IsGreaterThanOrEqualTo(series.Max(static bucket => bucket.HighPriceUnits), axis.High);
    }

    [TestMethod]
    public void TheHighestPriceSitsAtTheTopOfThePlot()
    {
        FxChartPriceAxis axis = new(100, 200, 25, [100, 125, 150, 175, 200]);

        Assert.AreEqual(FxChartCanvas.MarginTop, FxChartRenderer.PlotY(200, axis), 0.01f);
        Assert.AreEqual(
            FxChartCanvas.MarginTop + FxChartCanvas.PlotHeight,
            FxChartRenderer.PlotY(100, axis),
            0.01f);
    }

    [TestMethod]
    public void RisingAndFallingCandlesUseDistinctColours()
    {
        FxChartPalette palette = FxChartPalette.Light;

        Assert.AreEqual(palette.Rising, FxChartRenderer.Trend(palette, Bucket(60, 10, 12, 9, 11)));
        Assert.AreEqual(palette.Falling, FxChartRenderer.Trend(palette, Bucket(60, 11, 12, 9, 10)));
        Assert.AreEqual(palette.Flat, FxChartRenderer.Trend(palette, Bucket(60, 10, 12, 9, 10)));
        Assert.AreNotEqual(palette.Rising, palette.Falling);
    }

    [TestMethod]
    public void PricesArePrintedAgainstTheMarketPriceScale()
    {
        Assert.AreEqual("1.50", FxChartScale.FormatPrice(150, 100L));
        Assert.AreEqual("1.2345", FxChartScale.FormatPrice(12_345, 10_000L));
        Assert.AreEqual("0.0001", FxChartScale.FormatPrice(1, 10_000L));
        Assert.AreEqual("12345", FxChartScale.FormatPrice(12_345, 1L));
    }

    [TestMethod]
    public void TheChangeMetricCarriesItsSign()
    {
        Assert.AreEqual("+10.00%", FxChartScale.FormatChange(100, 110));
        Assert.AreEqual("-10.00%", FxChartScale.FormatChange(100, 90));
        Assert.AreEqual("0.00%", FxChartScale.FormatChange(100, 100));
    }

    [TestMethod]
    public void TheTimeAxisNeverLabelsEveryCandleOnALongSeries()
    {
        IReadOnlyList<FxOhlcBucket> series = Series(400);
        FxChartTimeLabels labels = FxChartRenderer.LabelIndexes(series, 10);

        Assert.IsGreaterThanOrEqualTo(2, labels.Indexes.Count);
        Assert.IsLessThanOrEqualTo(12, labels.Indexes.Count);
    }

    [TestMethod]
    public void EveryTimeLabelOnALongSeriesIsDistinct()
    {
        List<FxOhlcBucket> series = [];

        for (int index = 0; index < 168; index++)
        {
            series.Add(new FxOhlcBucket(
                FxMarketId.FromValue(EntityIdValue.FromBits(1)),
                3600,
                1_786_924_800L + (index * 3600L),
                150,
                150,
                150,
                150,
                BaseVolumeMinor: 10,
                QuoteVolumeMinor: 10,
                LastTradeSequenceNo: index,
                ProjectionVersion: 1));
        }

        long span = series[^1].BucketStart - series[0].BucketStart;
        FxChartTimeLabels labels = FxChartRenderer.LabelIndexes(series, 9);
        string format = FxChartRenderer.TimeFormat(labels.Stride, span);

        string[] rendered =
        [
            .. labels.Indexes.Select(index =>
                FxChartRenderer.FormatTime(series[index].BucketStart, format)),
        ];

        Assert.IsGreaterThanOrEqualTo(2, rendered.Length);
        Assert.AreEqual(rendered.Length, rendered.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void TheTimeAxisStillLabelsCandlesThatMissEveryRoundBoundary()
    {
        IReadOnlyList<FxOhlcBucket> series =
            [Bucket(1_787_208_540, 150, 150, 150, 150), Bucket(1_787_209_140, 150, 150, 150, 150)];

        Assert.AreEqual(2, FxChartRenderer.LabelIndexes(series, 8).Indexes.Count);
    }

    [TestMethod]
    public void AMovingAverageOnlyStartsOnceItsPeriodIsCovered()
    {
        IReadOnlyList<double?> average = FxChartScale.MovingAverage(Series(10), 5);

        Assert.IsNull(average[3]);
        Assert.IsNotNull(average[4]);
        Assert.IsEmpty(FxChartScale.MovingAverage(Series(3), 5).Where(static value => value is not null));
    }

    [TestMethod]
    public void TheImageRendererReportsTheCanonicalFileNameAndSize()
    {
        SkiaFxChartImageRenderer renderer = new(Renderer(), new RecordingDiagnostics());

        FxChartImage? image = renderer.TryRender(new FxChartRenderModel(
            "JPY/USD", "1H", 60, Series(10), 100L, Metrics(), FxChartSeriesStyle.Line, FxChartTheme.Light));

        Assert.IsNotNull(image);
        Assert.AreEqual("fx-chart.png", image.FileName);
        Assert.AreEqual(FxChartCanvas.Width, image.Width);
        Assert.AreEqual(FxChartCanvas.Height, image.Height);
    }

    [TestMethod]
    public void AnEmptySeriesRendersNoImage()
    {
        SkiaFxChartImageRenderer renderer = new(Renderer(), new RecordingDiagnostics());

        Assert.IsNull(renderer.TryRender(new FxChartRenderModel(
            "JPY/USD", "1H", 60, [], 100L, Metrics(), FxChartSeriesStyle.Line, FxChartTheme.Light)));
    }
}
