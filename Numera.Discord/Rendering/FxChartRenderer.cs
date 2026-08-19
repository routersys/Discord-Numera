using System.Globalization;
using Numera.Application.Abstractions;
using SkiaSharp;

namespace Numera.Discord.Rendering;

internal static class FxChartCanvas
{
    internal const int Width = 960;
    internal const int Height = 480;
    internal const int MarginLeft = 24;
    internal const int MarginRight = 96;
    internal const int MarginTop = 40;
    internal const int MarginBottom = 32;
    internal const int MaximumCandles = 96;
    internal const int GridRows = 4;

    internal const int BackgroundRgb = 0x11151C;
    internal const int GridRgb = 0x232A34;
    internal const int AxisTextRgb = 0x9AA4B2;
    internal const int TitleTextRgb = 0xE6EAF0;
    internal const int RisingRgb = 0x2FA968;
    internal const int FallingRgb = 0xD24B5A;
    internal const int FlatRgb = 0x8892A0;

    internal const int TitleFontPx = 20;
    internal const int AxisFontPx = 14;

    internal static int PlotWidth => Width - MarginLeft - MarginRight;

    internal static int PlotHeight => Height - MarginTop - MarginBottom;
}

internal sealed record FxChartRenderRequest(
    string Title,
    string IntervalLabel,
    IReadOnlyList<FxOhlcBucket> Buckets,
    int MinorUnitDigits);

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

        SKImageInfo info = new(
            FxChartCanvas.Width, FxChartCanvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(Color(FxChartCanvas.BackgroundRgb));

        (long low, long high) = Range(candles);

        DrawGrid(canvas, low, high, request.MinorUnitDigits);
        DrawCandles(canvas, candles, low, high);
        DrawTitle(canvas, request);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    internal static IReadOnlyList<FxOhlcBucket> Window(IReadOnlyList<FxOhlcBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        List<FxOhlcBucket> ordered = [.. buckets.OrderBy(static bucket => bucket.BucketStart)];

        return ordered.Count <= FxChartCanvas.MaximumCandles
            ? ordered
            : ordered.GetRange(
                ordered.Count - FxChartCanvas.MaximumCandles, FxChartCanvas.MaximumCandles);
    }

    internal static (long Low, long High) Range(IReadOnlyList<FxOhlcBucket> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);

        if (candles.Count == 0)
        {
            return (0L, 1L);
        }

        long low = candles[0].LowPriceUnits;
        long high = candles[0].HighPriceUnits;

        foreach (FxOhlcBucket candle in candles)
        {
            low = Math.Min(low, candle.LowPriceUnits);
            high = Math.Max(high, candle.HighPriceUnits);
        }

        return high == low ? (low, low + 1L) : (low, high);
    }

    internal static float PlotY(long value, long low, long high)
    {
        double ratio = (double)(value - low) / (high - low);

        return (float)(FxChartCanvas.MarginTop + ((1d - ratio) * FxChartCanvas.PlotHeight));
    }

    internal static string FormatPrice(long units, int minorUnitDigits)
    {
        if (minorUnitDigits <= 0)
        {
            return units.ToString(CultureInfo.InvariantCulture);
        }

        long scale = 1L;

        for (int index = 0; index < minorUnitDigits; index++)
        {
            scale *= 10L;
        }

        long whole = units / scale;
        long fraction = Math.Abs(units % scale);

        return whole.ToString(CultureInfo.InvariantCulture)
            + "."
            + fraction.ToString(CultureInfo.InvariantCulture).PadLeft(minorUnitDigits, '0');
    }

    private void DrawGrid(SKCanvas canvas, long low, long high, int minorUnitDigits)
    {
        using SKPaint line = new() { Color = Color(FxChartCanvas.GridRgb), StrokeWidth = 1f, IsAntialias = false };
        using SKPaint text = new() { Color = Color(FxChartCanvas.AxisTextRgb), IsAntialias = true };
        using SKFont font = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.AxisFontPx);

        for (int row = 0; row <= FxChartCanvas.GridRows; row++)
        {
            float y = FxChartCanvas.MarginTop
                + (FxChartCanvas.PlotHeight * row / (float)FxChartCanvas.GridRows);

            canvas.DrawLine(
                FxChartCanvas.MarginLeft,
                y,
                FxChartCanvas.MarginLeft + FxChartCanvas.PlotWidth,
                y,
                line);

            long value = high - ((high - low) * row / FxChartCanvas.GridRows);

            canvas.DrawText(
                FormatPrice(value, minorUnitDigits),
                FxChartCanvas.MarginLeft + FxChartCanvas.PlotWidth + 8,
                y + (FxChartCanvas.AxisFontPx / 2f) - 2f,
                SKTextAlign.Left,
                font,
                text);
        }
    }

    private static void DrawCandles(
        SKCanvas canvas,
        IReadOnlyList<FxOhlcBucket> candles,
        long low,
        long high)
    {
        if (candles.Count == 0)
        {
            return;
        }

        float slot = FxChartCanvas.PlotWidth / (float)candles.Count;
        float body = Math.Max(1f, Math.Min(slot - 2f, 14f));

        for (int index = 0; index < candles.Count; index++)
        {
            FxOhlcBucket candle = candles[index];
            float center = FxChartCanvas.MarginLeft + (slot * (index + 0.5f));

            using SKPaint paint = new()
            {
                Color = Color(Trend(candle)),
                IsAntialias = true,
                StrokeWidth = 1.5f,
            };

            canvas.DrawLine(
                center,
                PlotY(candle.HighPriceUnits, low, high),
                center,
                PlotY(candle.LowPriceUnits, low, high),
                paint);

            float open = PlotY(candle.OpenPriceUnits, low, high);
            float close = PlotY(candle.ClosePriceUnits, low, high);
            float top = Math.Min(open, close);
            float bottom = Math.Max(open, close);

            canvas.DrawRect(
                center - (body / 2f),
                top,
                body,
                Math.Max(1f, bottom - top),
                paint);
        }
    }

    private void DrawTitle(SKCanvas canvas, FxChartRenderRequest request)
    {
        using SKPaint paint = new() { Color = Color(FxChartCanvas.TitleTextRgb), IsAntialias = true };
        using SKFont font = new(fonts.Resolve(CardFontRole.General), FxChartCanvas.TitleFontPx);

        canvas.DrawText(
            request.Title,
            FxChartCanvas.MarginLeft,
            FxChartCanvas.MarginTop - 12,
            SKTextAlign.Left,
            font,
            paint);

        using SKPaint interval = new() { Color = Color(FxChartCanvas.AxisTextRgb), IsAntialias = true };
        using SKFont intervalFont = new(fonts.Resolve(CardFontRole.Mono), FxChartCanvas.AxisFontPx);

        canvas.DrawText(
            request.IntervalLabel,
            FxChartCanvas.MarginLeft + FxChartCanvas.PlotWidth,
            FxChartCanvas.MarginTop - 12,
            SKTextAlign.Right,
            intervalFont,
            interval);
    }

    internal static int Trend(FxOhlcBucket candle)
    {
        ArgumentNullException.ThrowIfNull(candle);

        if (candle.ClosePriceUnits > candle.OpenPriceUnits)
        {
            return FxChartCanvas.RisingRgb;
        }

        return candle.ClosePriceUnits < candle.OpenPriceUnits
            ? FxChartCanvas.FallingRgb
            : FxChartCanvas.FlatRgb;
    }

    private static SKColor Color(int rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
