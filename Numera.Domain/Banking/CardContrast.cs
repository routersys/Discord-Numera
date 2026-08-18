namespace Numera.Domain.Banking;

public static class CardContrast
{
    public const int White = 0xFFFFFF;

    public const int NearBlack = 0x111111;

    public const double NormalTextMinimum = 4.5;

    public const double LargeTextMinimum = 3.0;

    public const double ScrimOpacityStep = 0.05;

    public const double ScrimOpacityMaximum = 0.80;

    public static double RelativeLuminance(int rgb)
    {
        double red = Channel((rgb >> 16) & 0xFF);
        double green = Channel((rgb >> 8) & 0xFF);
        double blue = Channel(rgb & 0xFF);

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    public static double Ratio(int firstRgb, int secondRgb)
    {
        double first = RelativeLuminance(firstRgb);
        double second = RelativeLuminance(secondRgb);

        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    public static double Minimum(bool largeText) => largeText ? LargeTextMinimum : NormalTextMinimum;

    public static int ChooseTextColor(IReadOnlyList<int> backgroundPixels)
    {
        ArgumentNullException.ThrowIfNull(backgroundPixels);

        double white = double.PositiveInfinity;
        double nearBlack = double.PositiveInfinity;

        foreach (int pixel in backgroundPixels)
        {
            white = Math.Min(white, Ratio(White, pixel));
            nearBlack = Math.Min(nearBlack, Ratio(NearBlack, pixel));
        }

        return white > nearBlack ? White : NearBlack;
    }

    public static bool Satisfies(IReadOnlyList<int> backgroundPixels, int textRgb, bool largeText)
    {
        ArgumentNullException.ThrowIfNull(backgroundPixels);

        double minimum = Minimum(largeText);

        foreach (int pixel in backgroundPixels)
        {
            if (Ratio(textRgb, pixel) < minimum)
            {
                return false;
            }
        }

        return true;
    }

    public static int Blend(int backgroundRgb, int scrimRgb, double opacity)
    {
        double weight = Math.Clamp(opacity, 0.0, 1.0);

        int red = BlendChannel((backgroundRgb >> 16) & 0xFF, (scrimRgb >> 16) & 0xFF, weight);
        int green = BlendChannel((backgroundRgb >> 8) & 0xFF, (scrimRgb >> 8) & 0xFF, weight);
        int blue = BlendChannel(backgroundRgb & 0xFF, scrimRgb & 0xFF, weight);

        return (red << 16) | (green << 8) | blue;
    }

    public static bool TryResolveScrimOpacity(
        IReadOnlyList<int> backgroundPixels,
        int scrimRgb,
        int textRgb,
        bool largeText,
        out double opacity)
    {
        ArgumentNullException.ThrowIfNull(backgroundPixels);

        for (int step = 0; step * ScrimOpacityStep <= ScrimOpacityMaximum + double.Epsilon; step++)
        {
            double candidate = step * ScrimOpacityStep;

            if (candidate > ScrimOpacityMaximum)
            {
                break;
            }

            int[] blended = [.. backgroundPixels.Select(pixel => Blend(pixel, scrimRgb, candidate))];

            if (Satisfies(blended, textRgb, largeText))
            {
                opacity = candidate;
                return true;
            }
        }

        opacity = default;
        return false;
    }

    private static double Channel(int component)
    {
        double value = component / 255.0;

        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static int BlendChannel(int background, int scrim, double opacity) =>
        (int)Math.Round((background * (1.0 - opacity)) + (scrim * opacity), MidpointRounding.ToEven);
}
