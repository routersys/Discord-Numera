using System.Globalization;
using SkiaSharp;
using Numera.Domain.Banking;

namespace Numera.Discord.Rendering;

internal sealed record CardTextElement(
    string Text,
    int X,
    int Y,
    int Width,
    int Height,
    CardTextAlignment Alignment,
    CardFontRole Role,
    int FontSizePx,
    int MinimumFontSizePx,
    bool LargeText,
    int? FixedTextRgb);

internal sealed record BankCardRenderRequest(
    string BankName,
    string CapabilityLabel,
    string CustomerDisplayName,
    string CardIdentifier,
    string? Expiry,
    int BackgroundRgb,
    byte[]? BackgroundImage,
    CardFaceMode FaceMode);

internal interface IBankCardRenderer
{
    byte[] Render(BankCardRenderRequest request);
}

internal sealed class BankCardRenderer : IBankCardRenderer
{
    internal const string MissingGlyph = "□";
    internal const string Ellipsis = "…";

    private readonly ICardFontProvider fonts;
    private readonly ICardRenderDiagnostics diagnostics;

    public BankCardRenderer(ICardFontProvider fonts, ICardRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.fonts = fonts;
        this.diagnostics = diagnostics;
    }

    public byte[] Render(BankCardRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        SKImageInfo info = new(
            CardCanvas.Width, CardCanvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(SKColors.Transparent);

        using SKRoundRect clip = new(
            new SKRect(0, 0, CardCanvas.Width, CardCanvas.Height),
            CardCanvas.CornerRadius,
            CardCanvas.CornerRadius);

        canvas.Save();
        canvas.ClipRoundRect(clip, SKClipOperation.Intersect, antialias: true);

        DrawBackground(canvas, request);

        HashSet<int> reported = [];

        foreach (CardTextElement element in Layout(request))
        {
            DrawElement(canvas, bitmap, element, reported);
        }

        canvas.Restore();

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    internal static IReadOnlyList<CardTextElement> Layout(BankCardRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<CardTextElement> elements =
        [
            new(
                request.BankName, 72, 64, 710, 80,
                CardTextAlignment.Left, CardFontRole.General, 42, 30, LargeText: true, null),
            new(
                request.CapabilityLabel, 760, 72, 194, 56,
                CardTextAlignment.Right, CardFontRole.General, 30, 30, LargeText: true, null),
            new(
                request.CustomerDisplayName, 72, 478, 580, 70,
                CardTextAlignment.Left, CardFontRole.General, 28, 22, LargeText: false, null),
        ];

        if (request.FaceMode == CardFaceMode.Numbered)
        {
            elements.Insert(
                2,
                new CardTextElement(
                    request.CardIdentifier, 72, 274, 882, 88,
                    CardTextAlignment.Left, CardFontRole.Mono, 46, 36, LargeText: true, null));
        }

        if (request.Expiry is { Length: > 0 } expiry)
        {
            elements.Add(new CardTextElement(
                expiry, 710, 478, 244, 70,
                CardTextAlignment.Right, CardFontRole.Mono, 28, 22, LargeText: false, null));
        }

        return elements;
    }

    internal static string GroupDigits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        List<string> groups = [];

        for (int index = 0; index < value.Length; index += 4)
        {
            groups.Add(value.Substring(index, Math.Min(4, value.Length - index)));
        }

        return string.Join(' ', groups);
    }

    private void DrawBackground(SKCanvas canvas, BankCardRenderRequest request)
    {
        if (request.BackgroundImage is { Length: > 0 } bytes
            && SKBitmap.Decode(bytes) is { } source)
        {
            using (source)
            {
                float scale = Math.Max(
                    (float)CardCanvas.Width / source.Width, (float)CardCanvas.Height / source.Height);
                float width = source.Width * scale;
                float height = source.Height * scale;
                float left = (CardCanvas.Width - width) / 2f;
                float top = (CardCanvas.Height - height) / 2f;

                using SKImage background = SKImage.FromBitmap(source);

                canvas.DrawImage(
                    background,
                    new SKRect(left, top, left + width, top + height),
                    new SKSamplingOptions(SKFilterMode.Linear),
                    paint: null);

                return;
            }
        }

        using SKPaint paint = new()
        {
            Color = ToColor(request.BackgroundRgb),
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawRect(new SKRect(0, 0, CardCanvas.Width, CardCanvas.Height), paint);
    }

    private void DrawElement(
        SKCanvas canvas,
        SKBitmap bitmap,
        CardTextElement element,
        HashSet<int> reported)
    {
        if (element.Text.Length == 0)
        {
            return;
        }

        int[] zone = SampleZone(bitmap, element);
        int textRgb = element.FixedTextRgb ?? CardContrast.ChooseTextColor(zone);

        if (!CardContrast.Satisfies(zone, textRgb, element.LargeText))
        {
            int scrimRgb = textRgb == CardContrast.White ? 0x000000 : CardContrast.White;

            if (!CardContrast.TryResolveScrimOpacity(
                    zone, scrimRgb, textRgb, element.LargeText, out double opacity))
            {
                throw new CardContrastException(element.Text.Length);
            }

            DrawScrim(canvas, element, scrimRgb, opacity);
        }

        DrawText(canvas, element, fonts.Resolve(element.Role), textRgb, reported);
    }

    private void DrawScrim(SKCanvas canvas, CardTextElement element, int scrimRgb, double opacity)
    {
        if (opacity <= 0.0)
        {
            return;
        }

        SKRect bounds = new(
            element.X - CardCanvas.ScrimPadding,
            element.Y - CardCanvas.ScrimPadding,
            element.X + element.Width + CardCanvas.ScrimPadding,
            element.Y + element.Height + CardCanvas.ScrimPadding);

        using SKPaint paint = new()
        {
            Color = ToColor(scrimRgb).WithAlpha((byte)Math.Round(opacity * 255.0)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        canvas.DrawRoundRect(bounds, CardCanvas.ScrimCornerRadius, CardCanvas.ScrimCornerRadius, paint);
    }

    private void DrawText(
        SKCanvas canvas,
        CardTextElement element,
        SKTypeface typeface,
        int textRgb,
        HashSet<int> reported)
    {
        int size = element.FontSizePx;
        using SKFont font = new(typeface, size);
        string text = Substitute(element.Text, font, reported);

        while (size > element.MinimumFontSizePx && font.MeasureText(text) > element.Width)
        {
            size--;
            font.Size = size;
        }

        if (font.MeasureText(text) > element.Width)
        {
            text = Truncate(text, font, element.Width);
        }

        using SKPaint paint = new()
        {
            Color = ToColor(textRgb),
            IsAntialias = true,
        };

        font.GetFontMetrics(out SKFontMetrics metrics);

        float baseline = element.Y + ((element.Height - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
        float width = font.MeasureText(text);

        float x = element.Alignment switch
        {
            CardTextAlignment.Right => element.X + element.Width - width,
            CardTextAlignment.Center => element.X + ((element.Width - width) / 2f),
            _ => element.X,
        };

        canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
    }

    private string Substitute(string text, SKFont font, HashSet<int> reported)
    {
        System.Text.StringBuilder builder = new(text.Length);
        bool probed = false;
        SKFont? fallback = null;

        try
        {
            foreach (System.Text.Rune rune in text.EnumerateRunes())
            {
                if (font.ContainsGlyph(rune.Value))
                {
                    builder.Append(rune);
                    continue;
                }

                if (!probed)
                {
                    probed = true;
                    fallback = fonts.TryResolveFallback(out SKTypeface resolved)
                        ? new SKFont(resolved, font.Size)
                        : null;
                }

                if (fallback is not null && fallback.ContainsGlyph(rune.Value))
                {
                    builder.Append(rune);
                    continue;
                }

                if (reported.Add(rune.Value))
                {
                    diagnostics.MissingGlyph(rune.Value);
                }

                builder.Append(MissingGlyph);
            }
        }
        finally
        {
            fallback?.Dispose();
        }

        return builder.ToString();
    }

    private static string Truncate(string text, SKFont font, float limit)
    {
        List<string> clusters = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            clusters.Add((string)enumerator.Current);
        }

        for (int count = clusters.Count - 1; count > 0; count--)
        {
            string candidate = string.Concat(clusters.Take(count)) + Ellipsis;

            if (font.MeasureText(candidate) <= limit)
            {
                return candidate;
            }
        }

        return Ellipsis;
    }

    private static int[] SampleZone(SKBitmap bitmap, CardTextElement element)
    {
        int left = Math.Max(0, element.X - CardCanvas.ScrimPadding);
        int top = Math.Max(0, element.Y - CardCanvas.ScrimPadding);
        int right = Math.Min(bitmap.Width, element.X + element.Width + CardCanvas.ScrimPadding);
        int bottom = Math.Min(bitmap.Height, element.Y + element.Height + CardCanvas.ScrimPadding);

        List<int> pixels = [];

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                pixels.Add((color.Red << 16) | (color.Green << 8) | color.Blue);
            }
        }

        return [.. pixels.Distinct()];
    }

    private static SKColor ToColor(int rgb) =>
        new((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}

internal sealed class CardContrastException : Exception
{
    internal CardContrastException(int length) => Length = length;

    internal int Length { get; }
}

internal interface ICardRenderDiagnostics
{
    void MissingGlyph(int codePoint);

    void RendererUnavailable(string reason);
}
