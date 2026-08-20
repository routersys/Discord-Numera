using System.Globalization;
using Numera.Application.Abstractions;

namespace Numera.Discord.Rendering;

internal sealed record FxChartPriceAxis(long Low, long High, long Step, IReadOnlyList<long> Ticks);

internal static class FxChartScale
{
    internal const int MinimumTicks = 3;
    internal const int MaximumTicks = 7;
    internal const int PreferredTicks = 5;
    internal const int MaximumPoints = 500;

    private static readonly double[] NiceMultiples = [1d, 2d, 2.5d, 5d, 10d];

    private static readonly long[] TimeLadder =
    [
        60L, 300L, 900L, 1800L, 3600L, 7200L, 14400L, 21600L, 43200L,
        86400L, 172800L, 604800L, 2592000L,
    ];

    internal static (long Low, long High) Bounds(IReadOnlyList<FxOhlcBucket> candles)
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

        if (high > low)
        {
            return (low, high);
        }

        long pad = Math.Max(1L, Math.Abs(low) / 100L);

        return (low - pad, high + pad);
    }

    internal static FxChartPriceAxis Axis(IReadOnlyList<FxOhlcBucket> candles)
    {
        (long low, long high) = Bounds(candles);

        long step = NiceStep((high - low) / (double)(PreferredTicks - 1));

        while (TickCount(low, high, step) > MaximumTicks)
        {
            step = NiceStep(step * 1.5d);
        }

        long axisLow = FloorTo(low, step);
        long axisHigh = CeilingTo(high, step);

        while (TickCount(axisLow, axisHigh, step) < MinimumTicks)
        {
            axisHigh += step;
        }

        List<long> ticks = [];

        for (long value = axisLow; value <= axisHigh; value += step)
        {
            ticks.Add(value);
        }

        return new FxChartPriceAxis(axisLow, axisHigh, step, ticks);
    }

    internal static long NiceStep(double raw)
    {
        if (raw <= 1d || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            return 1L;
        }

        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;

        foreach (double multiple in NiceMultiples)
        {
            if (normalized <= multiple)
            {
                return Math.Max(1L, (long)Math.Round(multiple * magnitude));
            }
        }

        return Math.Max(1L, (long)Math.Round(10d * magnitude));
    }

    internal static string FormatPrice(long units, long priceScale)
    {
        if (priceScale <= 1L)
        {
            return units.ToString(CultureInfo.InvariantCulture);
        }

        int digits = Digits(priceScale);
        decimal value = units / (decimal)priceScale;

        return value.ToString(
            "F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    internal static string FormatAmount(long minor, int minorUnitDigits)
    {
        if (minorUnitDigits <= 0)
        {
            return minor.ToString("N0", CultureInfo.InvariantCulture);
        }

        long scale = 1L;

        for (int index = 0; index < minorUnitDigits; index++)
        {
            scale *= 10L;
        }

        decimal value = minor / (decimal)scale;

        return value.ToString(
            "N" + minorUnitDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    internal static string FormatChange(long from, long to)
    {
        if (from == 0L)
        {
            return "0.00%";
        }

        decimal ratio = (to - from) / (decimal)Math.Abs(from) * 100m;
        string sign = ratio > 0m ? "+" : string.Empty;

        return sign + ratio.ToString("F2", CultureInfo.InvariantCulture) + "%";
    }

    internal static int Digits(long priceScale)
    {
        int digits = 0;
        long remaining = priceScale;

        while (remaining > 1L && digits < 9)
        {
            remaining /= 10L;
            digits++;
        }

        return digits;
    }

    internal static long TimeStride(long span, int maximumLabels)
    {
        if (span <= 0L || maximumLabels <= 1)
        {
            return TimeLadder[0];
        }

        foreach (long candidate in TimeLadder)
        {
            long intervals = ((span + candidate) - 1L) / candidate;

            if (intervals + 1L <= maximumLabels)
            {
                return candidate;
            }
        }

        return TimeLadder[^1];
    }

    internal static IReadOnlyList<FxOhlcBucket> Downsample(IReadOnlyList<FxOhlcBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        if (buckets.Count <= MaximumPoints)
        {
            return buckets;
        }

        List<FxOhlcBucket> sampled = [buckets[0]];
        double every = (buckets.Count - 2) / (double)(MaximumPoints - 2);
        int previous = 0;

        for (int index = 0; index < MaximumPoints - 2; index++)
        {
            int rangeStart = (int)Math.Floor((index + 1) * every) + 1;
            int rangeEnd = Math.Min((int)Math.Floor((index + 2) * every) + 1, buckets.Count);
            int nextStart = rangeEnd;
            int nextEnd = Math.Min((int)Math.Floor((index + 3) * every) + 1, buckets.Count);

            double averageX = 0d;
            double averageY = 0d;
            int counted = 0;

            for (int next = nextStart; next < nextEnd; next++)
            {
                averageX += buckets[next].BucketStart;
                averageY += buckets[next].ClosePriceUnits;
                counted++;
            }

            if (counted == 0)
            {
                averageX = buckets[^1].BucketStart;
                averageY = buckets[^1].ClosePriceUnits;
            }
            else
            {
                averageX /= counted;
                averageY /= counted;
            }

            double anchorX = buckets[previous].BucketStart;
            double anchorY = buckets[previous].ClosePriceUnits;

            double best = -1d;
            int chosen = rangeStart;

            for (int candidate = rangeStart; candidate < rangeEnd; candidate++)
            {
                double area = Math.Abs(
                    ((anchorX - averageX) * (buckets[candidate].ClosePriceUnits - anchorY))
                    - ((anchorX - buckets[candidate].BucketStart) * (averageY - anchorY)));

                if (area > best)
                {
                    best = area;
                    chosen = candidate;
                }
            }

            sampled.Add(buckets[chosen]);
            previous = chosen;
        }

        sampled.Add(buckets[^1]);

        return sampled;
    }

    internal static IReadOnlyList<double?> MovingAverage(
        IReadOnlyList<FxOhlcBucket> candles,
        int period)
    {
        ArgumentNullException.ThrowIfNull(candles);

        double?[] values = new double?[candles.Count];

        if (period <= 0 || candles.Count < period)
        {
            return values;
        }

        double running = 0d;

        for (int index = 0; index < candles.Count; index++)
        {
            running += candles[index].ClosePriceUnits;

            if (index >= period)
            {
                running -= candles[index - period].ClosePriceUnits;
            }

            if (index >= period - 1)
            {
                values[index] = running / period;
            }
        }

        return values;
    }

    private static int TickCount(long low, long high, long step) =>
        step <= 0L ? MaximumTicks + 1 : (int)((CeilingTo(high, step) - FloorTo(low, step)) / step) + 1;

    private static long FloorTo(long value, long step) =>
        (long)(Math.Floor(value / (double)step) * step);

    private static long CeilingTo(long value, long step) =>
        (long)(Math.Ceiling(value / (double)step) * step);
}
