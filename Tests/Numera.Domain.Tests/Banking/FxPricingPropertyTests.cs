using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class FxPricingPropertyTests
{
    private const int Cases = 2_000;

    private const int Seed = 20_260_820;

    [TestMethod]
    public void AnAcceptedQuoteAlwaysReproducesTheExactProduct()
    {
        Random random = new(Seed);
        int accepted = 0;

        for (int index = 0; index < Cases; index++)
        {
            long baseMinor = random.NextInt64(1, 1_000_000_000L);
            long priceUnits = random.NextInt64(1, 1_000_000L);
            long priceScale = 1L << random.Next(0, 20);

            if (!FxPricing.TryQuoteMinor(baseMinor, priceUnits, priceScale, out long quote))
            {
                continue;
            }

            accepted++;
            Assert.AreEqual((Int128)baseMinor * priceUnits, (Int128)quote * priceScale);
        }

        Assert.IsGreaterThan(0, accepted);
    }

    [TestMethod]
    public void AQuoteThatWouldRoundIsRefusedInsteadOfTruncated()
    {
        Random random = new(Seed + 1);
        int refused = 0;

        for (int index = 0; index < Cases; index++)
        {
            long baseMinor = random.NextInt64(1, 100_000L);
            long priceUnits = random.NextInt64(1, 100_000L);
            long priceScale = random.NextInt64(2, 100_000L);

            bool exact = ((Int128)baseMinor * priceUnits) % priceScale == 0;
            bool quoted = FxPricing.TryQuoteMinor(baseMinor, priceUnits, priceScale, out _);

            if (!exact)
            {
                Assert.IsFalse(quoted);
                refused++;
            }
        }

        Assert.IsGreaterThan(0, refused);
    }

    [TestMethod]
    public void NonPositiveInputsNeverProduceAQuote()
    {
        Random random = new(Seed + 2);

        for (int index = 0; index < Cases; index++)
        {
            long negative = -random.NextInt64(0, 1_000_000L);

            Assert.IsFalse(FxPricing.TryQuoteMinor(negative, 10, 100, out _));
            Assert.IsFalse(FxPricing.TryQuoteMinor(10, negative, 100, out _));
            Assert.IsFalse(FxPricing.TryQuoteMinor(10, 100, negative, out _));
        }
    }

    [TestMethod]
    public void AQuoteBeyondSixtyFourBitsIsRefusedRatherThanWrapped()
    {
        Assert.IsFalse(FxPricing.TryQuoteMinor(long.MaxValue, 4, 1, out long overflowed));
        Assert.AreEqual(0L, overflowed);

        Assert.IsTrue(FxPricing.TryQuoteMinor(long.MaxValue, 1, 1, out long boundary));
        Assert.AreEqual(long.MaxValue, boundary);
    }

    [TestMethod]
    public void LotAndTickMultiplesAgreeWithTheRemainder()
    {
        Random random = new(Seed + 3);

        for (int index = 0; index < Cases; index++)
        {
            long amount = random.NextInt64(1, 1_000_000L);
            long lot = random.NextInt64(1, 1_000L);

            Assert.AreEqual(amount % lot == 0, FxPricing.IsLotMultiple(amount, lot));
            Assert.AreEqual(amount % lot == 0, FxPricing.IsTickMultiple(amount, lot));
            Assert.IsFalse(FxPricing.IsLotMultiple(amount, 0));
            Assert.IsFalse(FxPricing.IsTickMultiple(0, lot));
        }
    }

    [TestMethod]
    public void ExactSettlementCapabilityMatchesTheDivisibilityOfOneLotAtOneTick()
    {
        Random random = new(Seed + 4);

        for (int index = 0; index < Cases; index++)
        {
            long lot = random.NextInt64(1, 100_000L);
            long tick = random.NextInt64(1, 100_000L);
            long scale = random.NextInt64(1, 100_000L);

            Assert.AreEqual(
                ((Int128)lot * tick) % scale == 0,
                FxPricing.IsExactSettlementCapable(lot, tick, scale));
        }
    }

    [TestMethod]
    public void AMarketThatIsExactSettlementCapableAlwaysQuotesOneLotAtOneTick()
    {
        Random random = new(Seed + 5);
        int checkedMarkets = 0;

        for (int index = 0; index < Cases; index++)
        {
            long lot = random.NextInt64(1, 10_000L);
            long tick = random.NextInt64(1, 10_000L);
            long scale = random.NextInt64(1, 10_000L);

            if (!FxPricing.IsExactSettlementCapable(lot, tick, scale))
            {
                continue;
            }

            checkedMarkets++;
            Assert.IsTrue(FxPricing.TryQuoteMinor(lot, tick, scale, out long quote));
            Assert.IsGreaterThan(0L, quote);
        }

        Assert.IsGreaterThan(0, checkedMarkets);
    }
}

[TestClass]
public sealed class StateTransitionPropertyTests
{
    private enum Sample
    {
        Created = 1,
        Working = 2,
        Done = 3,
        Failed = 4,
    }

    private static readonly StateTransitionTable<Sample> Table =
        StateTransitionTable<Sample>
            .Create("SAMPLE_TRANSITION_INVALID")
            .AllowCreation(Sample.Created)
            .Allow(Sample.Created, Sample.Working, Sample.Failed)
            .Allow(Sample.Working, Sample.Done, Sample.Failed)
            .Build();

    [TestMethod]
    public void EveryPairIsEitherDeclaredOrRejectedAndNeverBoth()
    {
        foreach (Sample from in Enum.GetValues<Sample>())
        {
            foreach (Sample to in Enum.GetValues<Sample>())
            {
                bool allowed = Table.IsAllowed(from, to);

                if (allowed)
                {
                    Assert.AreEqual(to, Table.EnsureAllowed(from, to));
                    continue;
                }

                Assert.ThrowsExactly<InvariantViolationException>(() => Table.EnsureAllowed(from, to));
            }
        }
    }

    [TestMethod]
    public void NoStateEverTransitionsToItself()
    {
        foreach (Sample state in Enum.GetValues<Sample>())
        {
            Assert.IsFalse(Table.IsAllowed(state, state));
        }
    }

    [TestMethod]
    public void TerminalStatesHaveNoOutgoingTransition()
    {
        foreach (Sample state in Enum.GetValues<Sample>())
        {
            if (!Table.IsTerminal(state))
            {
                continue;
            }

            foreach (Sample target in Enum.GetValues<Sample>())
            {
                Assert.IsFalse(Table.IsAllowed(state, target));
            }
        }

        Assert.IsTrue(Table.IsTerminal(Sample.Done));
        Assert.IsTrue(Table.IsTerminal(Sample.Failed));
    }

    [TestMethod]
    public void ARandomWalkNeverLeavesTheDeclaredGraph()
    {
        Random random = new(20_260_821);

        for (int run = 0; run < 500; run++)
        {
            Sample state = Sample.Created;
            Table.EnsureCreatable(state);

            for (int step = 0; step < 8 && !Table.IsTerminal(state); step++)
            {
                Sample target = (Sample)random.Next(1, 5);

                if (!Table.IsAllowed(state, target))
                {
                    continue;
                }

                state = Table.EnsureAllowed(state, target);
            }

            Assert.IsTrue(state is Sample.Created or Sample.Working or Sample.Done or Sample.Failed);
        }
    }
}
