using System.Globalization;
using Numera.Application.Abstractions;
using SkiaSharp;

namespace Numera.Discord.Rendering;

internal sealed record FxChartPalette(
    int Background,
    int Panel,
    int PrimaryText,
    int SecondaryText,
    int Grid,
    int AxisLine,
    int Rising,
    int Falling,
    int Flat,
    IReadOnlyList<int> Averages)
{
    internal static FxChartPalette Dark { get; } = new(
        Background: 0x0F1115,
        Panel: 0x161A22,
        PrimaryText: 0xF3F4F6,
        SecondaryText: 0xB7BDC8,
        Grid: 0x2B3240,
        AxisLine: 0x3A4354,
        Rising: 0x16A34A,
        Falling: 0xDC2626,
        Flat: 0x60A5FA,
        Averages: [0x60A5FA, 0xF2C037, 0xDD5FE0]);

    internal static FxChartPalette Light { get; } = new(
        Background: 0xFFFFFF,
        Panel: 0xF7F8FA,
        PrimaryText: 0x1F2933,
        SecondaryText: 0x6B7280,
        Grid: 0xE5E7EB,
        AxisLine: 0x4B5563,
        Rising: 0xEF3B54,
        Falling: 0x3D4EC8,
        Flat: 0x8892A0,
        Averages: [0x21BA72, 0xF2C037, 0xDD5FE0]);

    internal static FxChartPalette Of(FxChartTheme theme) =>
        theme == FxChartTheme.Light ? Light : Dark;
}

internal static class FxChartCanvas
{
    internal const int Width = 1280;
    internal const int Height = 720;

    internal const int StripHeight = 104;
    internal const int MarginLeft = 56;
    internal const int MarginRight = 96;
    internal const int MarginTop = 144;
    internal const int MarginBottom = 60;

    internal const int PairFontPx = 30;
    internal const int PeriodFontPx = 15;
    internal const int LabelFontPx = 13;
    internal const int ValueFontPx = 19;
    internal const int AxisFontPx = 14;

    internal const int MetricLeft = 320;
    internal const int MetricWidth = 132;

    internal const int MaximumBodyWidth = 16;

    internal static readonly int[] AveragePeriods = [5, 25, 75];

    internal static int PlotWidth => Width - MarginLeft - MarginRight;

    internal static int PlotHeight => Height - MarginTop - MarginBottom;

    internal static int PlotRight => Width - MarginRight;

    internal static int PlotBottom => Height - MarginBottom;
}

internal sealed record FxChartTimeLabels(IReadOnlyList<int> Indexes, long Stride);

internal sealed record FxChartRenderRequest(
    string PairCode,
    string PeriodLabel,
    int BucketSeconds,
    IReadOnlyList<FxOhlcBucket> Buckets,
    long PriceScale,
    IReadOnlyList<FxChartMetric> Metrics,
    FxChartSeriesStyle Style,
    FxChartTheme Theme);

internal interface IFxChartRenderer
{
    byte[] Render(FxChartRenderRequest request);
}

internal sealed class FxChartRenderer : IFxChartRenderer
{
    private readonly ICardFontProvider fonts;

    public FxChartRenderer(ICardFontProvider fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);

        this.fonts = fonts;
    }

    public byte[] Render(FxChartRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<FxOhlcBucket> candles = Window(request.Buckets);
        FxChartPalette palette = FxChartPalette.Of(request.Theme);
        FxChartPriceAxis axis = FxChartScale.Axis(candles);

        SKImageInfo info = new(
            FxChartCanvas.Width, FxChartCanvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(Color(palette.Background));

        DrawStrip(canvas, request, palette);
        DrawPriceAxis(canvas, palette, axis, request.PriceScale);
        DrawTimeAxis(canvas, palette, candles);

        if (request.Style == FxChartSeriesStyle.Candle)
        {
            DrawCandles(canvas, palette, candles, axis);
        }
        else
        {
            DrawLine(canvas, palette, candles, axis);
        }

        DrawAverages(canvas, palette, candles, axis);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    internal static IReadOnlyList<FxOhlcBucket> Window(IReadOnlyList<FxOhlcBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        List<FxOhlcBucket> ordered = [.. buckets.OrderBy(static bucket => bucket.BucketStart)];

        return FxChartScale.Downsample(ordered);
    }

    internal static float PlotY(long value, FxChartPriceAxis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);

        double span = axis.High - axis.Low;
        double ratio = span <= 0d ? 0.5d : (value - axis.Low) / span;

        return (float)(FxChartCanvas.MarginTop + ((1d - ratio) * FxChartCanvas.PlotHeight));
    }

    internal static float PlotYOf(double value, FxChartPriceAxis axis)
    {
        ArgumentNullException.ThrowIfNull(axis);

        double span = axis.High - axis.Low;
        double ratio = span <= 0d ? 0.5d : (value - axis.Low) / span;

        return (float)(FxChartCanvas.MarginTop + ((1d - ratio) * FxChartCanvas.PlotHeight));
    }

    internal static float Inside(float center, float width)
    {
        float half = (width / 2f) + 4f;

        return Math.Clamp(center, half, FxChartCanvas.Width - half);
    }

    internal static float SlotCenter(int index, int count)
    {
        float slot = FxChartCanvas.PlotWidth / (float)Math.Max(1, count);

        return FxChartCanvas.MarginLeft + (slot * (index + 0.5f));
    }

    internal static string TimeFormat(long stride, long span)
    {
        if (span < 259200L)
        {
            return "H:mm";
        }

        return stride >= 86400L ? "M/d" : "M/d H:mm";
    }

    internal static string WidestTimeSample(long span) => span < 259200L ? "00:00" : "00/00 00:00";

    internal static string FormatTime(long unixSeconds, string format) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToString(format, CultureInfo.InvariantCulture);

    internal static FxChartTimeLabels LabelIndexes(
        IReadOnlyList<FxOhlcBucket> candles,
        int maximumLabels)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count == 0)
        {
            return new FxChartTimeLabels([], 0L);
        }

        long span = candles[^1].BucketStart - candles[0].BucketStart;

        if (candles.Count <= maximumLabels)
        {
            return new FxChartTimeLabels(
                [.. Enumerable.Range(0, candles.Count)], candles[0].BucketSeconds);
        }

        long stride = FxChartScale.TimeStride(span, maximumLabels);

        List<int> aligned = [];

        for (int index = 0; index < candles.Count; index++)
        {
            if (candles[index].BucketStart % stride == 0L)
            {
                aligned.Add(index);
            }
        }

        if (aligned.Count >= 2 && aligned.Count <= maximumLabels)
        {
            return new FxChartTimeLabels(aligned, stride);
        }

        int step = Math.Max(1, (int)Math.Ceiling(candles.Count / (double)maximumLabels));
        List<int> spaced = [];

        for (int index = 0; index < candles.Count; index += step)
        {
            spaced.Add(index);
        }

        if (spaced[^1] != candles.Count - 1)
        {
            spaced.Add(candles.Count - 1);
        }

        return new FxChartTimeLabels(spaced, step * (long)candles[0].BucketSeconds);
    }

    private void DrawStrip(SKCanvas canvas, FxChartRenderRequest request, FxChartPalette palette)
    {
        using SKPaint panel = new() { Color = Color(palette.Panel), IsAntialias = false };

        canvas.DrawRect(0, 0, FxChartCanvas.Width, FxChartCanvas.StripHeight, panel);

        using SKPaint divider = new() { Color = Color(palette.Grid), StrokeWidth = 1f };

        canvas.DrawLine(
            0, FxChartCanvas.StripHeight, FxChartCanvas.Width, FxChartCanvas.StripHeight, divider);

        using SKPaint primary = new() { Color = Color(palette.PrimaryText), IsAntialias = true };
        using SKPaint secondary = new() { Color = Color(palette.SecondaryText), IsAntialias = true };
        using SKFont pairFont = new(fonts.Resolve(CardFontRole.General), FxChartCanvas.PairFontPx);
        using SKFont periodFont = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.PeriodFontPx);
        using SKFont labelFont = new(fonts.Resolve(CardFontRole.General), FxChartCanvas.LabelFontPx);
        using SKFont valueFont = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.ValueFontPx);

        canvas.DrawText(
            request.PairCode, FxChartCanvas.MarginLeft, 52, SKTextAlign.Left, pairFont, primary);

        canvas.DrawText(
            request.PeriodLabel, FxChartCanvas.MarginLeft, 80, SKTextAlign.Left, periodFont, secondary);

        for (int index = 0; index < request.Metrics.Count; index++)
        {
            float left = FxChartCanvas.MetricLeft + (FxChartCanvas.MetricWidth * index);

            if (left + FxChartCanvas.MetricWidth > FxChartCanvas.Width - 16)
            {
                break;
            }

            canvas.DrawText(
                request.Metrics[index].Label, left, 42, SKTextAlign.Left, labelFont, secondary);

            canvas.DrawText(
                request.Metrics[index].Value, left, 72, SKTextAlign.Left, valueFont, primary);
        }
    }

    private void DrawPriceAxis(
        SKCanvas canvas,
        FxChartPalette palette,
        FxChartPriceAxis axis,
        long priceScale)
    {
        using SKPathEffect dash = SKPathEffect.CreateDash([4f, 4f], 0f);
        using SKPaint line = new()
        {
            Color = Color(palette.Grid),
            StrokeWidth = 1f,
            IsAntialias = false,
            PathEffect = dash,
        };

        using SKPaint text = new() { Color = Color(palette.SecondaryText), IsAntialias = true };
        using SKFont font = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.AxisFontPx);

        foreach (long tick in axis.Ticks)
        {
            float y = PlotY(tick, axis);

            canvas.DrawLine(FxChartCanvas.MarginLeft, y, FxChartCanvas.PlotRight, y, line);

            canvas.DrawText(
                FxChartScale.FormatPrice(tick, priceScale),
                FxChartCanvas.PlotRight + 12,
                y + (FxChartCanvas.AxisFontPx / 2f) - 2f,
                SKTextAlign.Left,
                font,
                text);
        }
    }

    private void DrawTimeAxis(
        SKCanvas canvas,
        FxChartPalette palette,
        IReadOnlyList<FxOhlcBucket> candles)
    {
        using SKPaint axisPaint = new()
        {
            Color = Color(palette.AxisLine),
            StrokeWidth = 1.5f,
            IsAntialias = false,
        };

        canvas.DrawLine(
            FxChartCanvas.MarginLeft,
            FxChartCanvas.PlotBottom,
            FxChartCanvas.PlotRight,
            FxChartCanvas.PlotBottom,
            axisPaint);

        if (candles.Count == 0)
        {
            return;
        }

        using SKPaint text = new() { Color = Color(palette.SecondaryText), IsAntialias = true };
        using SKFont font = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.AxisFontPx);

        long span = candles[^1].BucketStart - candles[0].BucketStart;
        float sample = font.MeasureText(WidestTimeSample(span));
        int maximumLabels = Math.Max(2, (int)(FxChartCanvas.PlotWidth / (sample + 32f)));

        FxChartTimeLabels labels = LabelIndexes(candles, maximumLabels);
        string format = TimeFormat(labels.Stride, span);

        using SKPaint tick = new()
        {
            Color = Color(palette.AxisLine),
            StrokeWidth = 1f,
            IsAntialias = false,
        };

        foreach (int index in labels.Indexes)
        {
            float center = SlotCenter(index, candles.Count);
            string caption = FormatTime(candles[index].BucketStart, format);

            canvas.DrawLine(
                center, FxChartCanvas.PlotBottom, center, FxChartCanvas.PlotBottom + 6, tick);

            canvas.DrawText(
                caption,
                Inside(center, font.MeasureText(caption)),
                FxChartCanvas.PlotBottom + 26,
                SKTextAlign.Center,
                font,
                text);
        }
    }

    private static void DrawCandles(
        SKCanvas canvas,
        FxChartPalette palette,
        IReadOnlyList<FxOhlcBucket> candles,
        FxChartPriceAxis axis)
    {
        if (candles.Count == 0)
        {
            return;
        }

        float slot = FxChartCanvas.PlotWidth / (float)candles.Count;
        float body = Math.Max(2f, Math.Min(slot * 0.55f, FxChartCanvas.MaximumBodyWidth));

        foreach ((FxOhlcBucket candle, int index) in candles.Select(static (item, i) => (item, i)))
        {
            float center = SlotCenter(index, candles.Count);

            using SKPaint wick = new()
            {
                Color = Color(Trend(palette, candle)),
                IsAntialias = true,
                StrokeWidth = Math.Max(1f, body / 8f),
            };

            canvas.DrawLine(
                center,
                PlotY(candle.HighPriceUnits, axis),
                center,
                PlotY(candle.LowPriceUnits, axis),
                wick);

            float open = PlotY(candle.OpenPriceUnits, axis);
            float close = PlotY(candle.ClosePriceUnits, axis);
            float top = Math.Min(open, close);
            float bottom = Math.Max(open, close);

            using SKPaint fill = new() { Color = Color(Trend(palette, candle)), IsAntialias = true };

            canvas.DrawRect(
                center - (body / 2f), top, body, Math.Max(1.5f, bottom - top), fill);
        }
    }

    private static void DrawLine(
        SKCanvas canvas,
        FxChartPalette palette,
        IReadOnlyList<FxOhlcBucket> candles,
        FxChartPriceAxis axis)
    {
        if (candles.Count == 0)
        {
            return;
        }

        int direction = Direction(candles);
        int rgb = direction > 0 ? palette.Rising : direction < 0 ? palette.Falling : palette.Flat;

        using SKPathBuilder builder = new();

        for (int index = 0; index < candles.Count; index++)
        {
            float x = SlotCenter(index, candles.Count);
            float y = PlotY(candles[index].ClosePriceUnits, axis);

            if (index == 0)
            {
                builder.MoveTo(x, y);
            }
            else
            {
                builder.LineTo(x, y);
            }
        }

        using SKPath path = builder.Detach();

        using SKPaint stroke = new()
        {
            Color = Color(rgb),
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 2.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.DrawPath(path, stroke);

        if (candles.Count > 60)
        {
            return;
        }

        using SKPaint dot = new() { Color = Color(rgb), IsAntialias = true };

        for (int index = 0; index < candles.Count; index++)
        {
            canvas.DrawCircle(
                SlotCenter(index, candles.Count),
                PlotY(candles[index].ClosePriceUnits, axis),
                3.5f,
                dot);
        }
    }

    private static void DrawAverages(
        SKCanvas canvas,
        FxChartPalette palette,
        IReadOnlyList<FxOhlcBucket> candles,
        FxChartPriceAxis axis)
    {
        for (int slot = 0; slot < FxChartCanvas.AveragePeriods.Length; slot++)
        {
            IReadOnlyList<double?> values =
                FxChartScale.MovingAverage(candles, FxChartCanvas.AveragePeriods[slot]);

            using SKPathBuilder builder = new();
            bool started = false;

            for (int index = 0; index < values.Count; index++)
            {
                if (values[index] is not { } value)
                {
                    continue;
                }

                float x = SlotCenter(index, candles.Count);
                float y = PlotYOf(value, axis);

                if (started)
                {
                    builder.LineTo(x, y);
                }
                else
                {
                    builder.MoveTo(x, y);
                    started = true;
                }
            }

            if (!started)
            {
                continue;
            }

            using SKPath path = builder.Detach();

            using SKPaint stroke = new()
            {
                Color = Color(palette.Averages[slot % palette.Averages.Count]),
                IsAntialias = true,
                IsStroke = true,
                StrokeWidth = 1.8f,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            canvas.DrawPath(path, stroke);
        }
    }

    internal static int Direction(IReadOnlyList<FxOhlcBucket> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count == 0)
        {
            return 0;
        }

        long from = candles[0].OpenPriceUnits;
        long to = candles[^1].ClosePriceUnits;

        return to > from ? 1 : to < from ? -1 : 0;
    }

    internal static int Trend(FxChartPalette palette, FxOhlcBucket candle)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(candle);

        if (candle.ClosePriceUnits > candle.OpenPriceUnits)
        {
            return palette.Rising;
        }

        return candle.ClosePriceUnits < candle.OpenPriceUnits ? palette.Falling : palette.Flat;
    }

    private static SKColor Color(int rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
