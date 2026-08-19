using BenchmarkDotNet.Attributes;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Benchmarks;

[MemoryDiagnoser]
public class MoneyBenchmarks
{
    private MoneyMinor[] amounts = [];
    private LedgerBalance balance;

    [Params(16, 256, 4096)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Random random = new(20_260_819);
        amounts = new MoneyMinor[Length];

        for (int index = 0; index < Length; index++)
        {
            amounts[index] = MoneyMinor.FromMinor(random.NextInt64(1, 1_000_000_000L));
        }

        balance = LedgerBalance.Create(MoneyMinor.FromMinor(long.MaxValue / 4), MoneyMinor.Zero);
    }

    [Benchmark(Baseline = true)]
    public long SequentialFold()
    {
        MoneyMinor total = MoneyMinor.Zero;

        foreach (MoneyMinor amount in amounts)
        {
            total = total.Add(amount);
        }

        return total.Value;
    }

    [Benchmark]
    public long SpanSum() => MoneyMinor.Sum(amounts).Value;

    [Benchmark]
    public long RawInt64Fold()
    {
        long total = 0;

        foreach (MoneyMinor amount in amounts)
        {
            total = checked(total + amount.Value);
        }

        return total;
    }

    [Benchmark]
    public long HoldRoundTrip()
    {
        LedgerBalance current = balance;

        foreach (MoneyMinor amount in amounts)
        {
            current = current.IncreaseHold(amount).DecreaseHold(amount);
        }

        return current.AvailableBalance.Value;
    }
}

[MemoryDiagnoser]
public class FxPricingBenchmarks
{
    private long[] baseAmounts = [];
    private long[] prices = [];

    [Params(1_000, 100_000)]
    public long PriceScale { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Random random = new(20_260_820);
        baseAmounts = new long[1024];
        prices = new long[1024];

        for (int index = 0; index < baseAmounts.Length; index++)
        {
            baseAmounts[index] = random.NextInt64(1, 1_000_000L) * PriceScale;
            prices[index] = random.NextInt64(1, 1_000_000L);
        }
    }

    [Benchmark]
    public int ExactQuoteConversion()
    {
        int accepted = 0;

        for (int index = 0; index < baseAmounts.Length; index++)
        {
            if (FxPricing.TryQuoteMinor(baseAmounts[index], prices[index], PriceScale, out _))
            {
                accepted++;
            }
        }

        return accepted;
    }

    [Benchmark]
    public int LotAndTickValidation()
    {
        int accepted = 0;

        for (int index = 0; index < baseAmounts.Length; index++)
        {
            if (FxPricing.IsLotMultiple(baseAmounts[index], PriceScale)
                && FxPricing.IsTickMultiple(prices[index], 1))
            {
                accepted++;
            }
        }

        return accepted;
    }
}
