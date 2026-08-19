using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class MoneyPropertyTests
{
    private const int Cases = 2_000;

    private const int Seed = 20_260_819;

    private static long NextAmount(Random random, long bound) =>
        (long)((random.NextDouble() - 0.5d) * 2d * bound);

    [TestMethod]
    public void AdditionIsAssociativeAcrossTheWholeRange()
    {
        Random random = new(Seed);

        for (int index = 0; index < Cases; index++)
        {
            MoneyMinor first = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 4));
            MoneyMinor second = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 4));
            MoneyMinor third = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 4));

            Assert.AreEqual(first.Add(second).Add(third), first.Add(second.Add(third)));
        }
    }

    [TestMethod]
    public void SubtractionIsTheInverseOfAddition()
    {
        Random random = new(Seed + 1);

        for (int index = 0; index < Cases; index++)
        {
            MoneyMinor left = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 2));
            MoneyMinor right = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 2));

            Assert.AreEqual(left, left.Add(right).Subtract(right));
        }
    }

    [TestMethod]
    public void OrderingAgreesWithTheUnderlyingIntegerOrdering()
    {
        Random random = new(Seed + 2);

        for (int index = 0; index < Cases; index++)
        {
            long left = NextAmount(random, long.MaxValue);
            long right = NextAmount(random, long.MaxValue);

            Assert.AreEqual(
                Math.Sign(left.CompareTo(right)),
                Math.Sign(MoneyMinor.FromMinor(left).CompareTo(MoneyMinor.FromMinor(right))));
        }
    }

    [TestMethod]
    public void SumMatchesTheSequentialFold()
    {
        Random random = new(Seed + 3);

        for (int index = 0; index < 200; index++)
        {
            int length = random.Next(1, 32);
            MoneyMinor[] values = new MoneyMinor[length];
            MoneyMinor folded = MoneyMinor.Zero;

            for (int item = 0; item < length; item++)
            {
                values[item] = MoneyMinor.FromMinor(NextAmount(random, long.MaxValue / 64));
                folded = folded.Add(values[item]);
            }

            Assert.AreEqual(folded, MoneyMinor.Sum(values));
        }
    }

    [TestMethod]
    public void TheIntermediateWidthAbsorbsSumsThatOverflowSixtyFourBits()
    {
        MoneyMinor maximum = MoneyMinor.FromMinor(long.MaxValue);

        Int128 doubled = checked(maximum.Intermediate + maximum.Intermediate);

        Assert.AreEqual((Int128)long.MaxValue * 2, doubled);

        InvariantViolationException failure =
            Assert.ThrowsExactly<InvariantViolationException>(() => MoneyMinor.FromIntermediate(doubled));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, failure.Code);
    }

    [TestMethod]
    public void EveryOutOfRangeIntermediateIsRejectedAsAnInvariantViolation()
    {
        Random random = new(Seed + 4);

        for (int index = 0; index < 500; index++)
        {
            Int128 beyond = (Int128)long.MaxValue + random.Next(1, int.MaxValue);

            Assert.ThrowsExactly<InvariantViolationException>(() => MoneyMinor.FromIntermediate(beyond));
            Assert.ThrowsExactly<InvariantViolationException>(() => MoneyMinor.FromIntermediate(-beyond));
        }
    }

    [TestMethod]
    public void AdditionThatLeavesTheSixtyFourBitRangeNeverReturnsAWrappedValue()
    {
        Random random = new(Seed + 5);

        for (int index = 0; index < 500; index++)
        {
            long offset = random.NextInt64(1, int.MaxValue);
            MoneyMinor high = MoneyMinor.FromMinor(long.MaxValue - offset + 1);
            MoneyMinor addend = MoneyMinor.FromMinor(offset);

            Assert.ThrowsExactly<InvariantViolationException>(() => high.Add(addend));
        }
    }

    [TestMethod]
    public void AvailableBalanceIsAlwaysPostedMinusHeld()
    {
        Random random = new(Seed + 6);

        for (int index = 0; index < Cases; index++)
        {
            long posted = random.NextInt64(0, 1_000_000_000L);
            long held = random.NextInt64(0, posted + 1);

            LedgerBalance balance = LedgerBalance.Create(
                MoneyMinor.FromMinor(posted), MoneyMinor.FromMinor(held));

            Assert.AreEqual(MoneyMinor.FromMinor(posted - held), balance.AvailableBalance);
            balance.EnsureDepositAccountInvariants();
        }
    }

    [TestMethod]
    public void HoldSequencesNeverDriveTheHeldAmountNegative()
    {
        Random random = new(Seed + 7);

        for (int run = 0; run < 200; run++)
        {
            LedgerBalance balance = LedgerBalance.Create(
                MoneyMinor.FromMinor(random.NextInt64(1_000, 1_000_000)), MoneyMinor.Zero);

            for (int step = 0; step < 32; step++)
            {
                long amount = random.NextInt64(1, 500);

                if (random.Next(2) == 0)
                {
                    balance = balance.IncreaseHold(MoneyMinor.FromMinor(amount));
                }
                else if (balance.HeldAmount.Value >= amount)
                {
                    balance = balance.DecreaseHold(MoneyMinor.FromMinor(amount));
                }

                Assert.IsFalse(balance.HeldAmount.IsNegative);
                Assert.AreEqual(
                    balance.PostedBalance.Subtract(balance.HeldAmount), balance.AvailableBalance);
            }
        }
    }

    [TestMethod]
    public void ReleasingMoreThanIsHeldIsAlwaysRejected()
    {
        Random random = new(Seed + 8);

        for (int index = 0; index < 500; index++)
        {
            long held = random.NextInt64(1, 1_000_000);
            LedgerBalance balance = LedgerBalance.Create(
                MoneyMinor.FromMinor(held + 1), MoneyMinor.FromMinor(held));

            MoneyMinor excess = MoneyMinor.FromMinor(held + random.NextInt64(1, 1_000));

            InvariantViolationException failure =
                Assert.ThrowsExactly<InvariantViolationException>(() => balance.DecreaseHold(excess));

            Assert.AreEqual(InvariantViolationCode.HeldAmountNegative, failure.Code);
        }
    }

    [TestMethod]
    public void PostingOnTheNormalSideIncreasesAndTheOppositeSideDecreases()
    {
        Random random = new(Seed + 9);

        for (int index = 0; index < Cases; index++)
        {
            long start = random.NextInt64(0, 1_000_000);
            long amount = random.NextInt64(1, 100_000);

            LedgerBalance balance = LedgerBalance.Create(
                MoneyMinor.FromMinor(start), MoneyMinor.Zero);

            LedgerBalance credited = balance.ApplyPosting(
                EntrySide.Credit, EntrySide.Credit, MoneyMinor.FromMinor(amount));
            LedgerBalance debited = balance.ApplyPosting(
                EntrySide.Debit, EntrySide.Credit, MoneyMinor.FromMinor(amount));

            Assert.AreEqual(MoneyMinor.FromMinor(start + amount), credited.PostedBalance);
            Assert.AreEqual(MoneyMinor.FromMinor(start - amount), debited.PostedBalance);
            Assert.AreEqual(balance.HeldAmount, credited.HeldAmount);
        }
    }

    [TestMethod]
    public void ReservationSucceedsExactlyWhenTheAvailableBalanceCovers()
    {
        Random random = new(Seed + 10);

        for (int index = 0; index < Cases; index++)
        {
            long posted = random.NextInt64(0, 100_000);
            long held = random.NextInt64(0, posted + 1);
            long request = random.NextInt64(0, 120_000);

            LedgerBalance balance = LedgerBalance.Create(
                MoneyMinor.FromMinor(posted), MoneyMinor.FromMinor(held));

            Assert.AreEqual(
                request >= 1 && posted - held >= request,
                balance.CanReserve(MoneyMinor.FromMinor(request)));
        }
    }
}
