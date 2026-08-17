using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class MoneyMinorTests
{
    [TestMethod]
    public void ZeroIsNeutral()
    {
        Assert.AreEqual(0L, MoneyMinor.Zero.Value);
        Assert.IsTrue(MoneyMinor.Zero.IsZero);
        Assert.IsFalse(MoneyMinor.Zero.IsPositive);
        Assert.IsFalse(MoneyMinor.Zero.IsNegative);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(long.MinValue)]
    public void FromPositiveMinorRejectsNonPositive(long value)
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => MoneyMinor.FromPositiveMinor(value));

        Assert.AreEqual(InvariantViolationCode.MoneyNotPositive, exception.Code);
    }

    [TestMethod]
    public void FromPositiveMinorAcceptsMaximum() =>
        Assert.AreEqual(long.MaxValue, MoneyMinor.FromPositiveMinor(long.MaxValue).Value);

    [TestMethod]
    public void FromIntermediateAcceptsExactBoundaries()
    {
        Assert.AreEqual(long.MaxValue, MoneyMinor.FromIntermediate(long.MaxValue).Value);
        Assert.AreEqual(long.MinValue, MoneyMinor.FromIntermediate(long.MinValue).Value);
    }

    [TestMethod]
    public void FromIntermediateRejectsAboveMaximum()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => MoneyMinor.FromIntermediate((Int128)long.MaxValue + 1));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void FromIntermediateRejectsBelowMinimum()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => MoneyMinor.FromIntermediate((Int128)long.MinValue - 1));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void AdditionOverflowRaisesInvariantViolation()
    {
        MoneyMinor maximum = MoneyMinor.FromMinor(long.MaxValue);
        MoneyMinor one = MoneyMinor.FromMinor(1);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => maximum.Add(one));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void SubtractionOverflowRaisesInvariantViolation()
    {
        MoneyMinor minimum = MoneyMinor.FromMinor(long.MinValue);
        MoneyMinor one = MoneyMinor.FromMinor(1);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => minimum.Subtract(one));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void NegationOfMinimumRaisesInvariantViolation()
    {
        MoneyMinor minimum = MoneyMinor.FromMinor(long.MinValue);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => minimum.Negate());

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void NegationOfMaximumSucceeds() =>
        Assert.AreEqual(-long.MaxValue, MoneyMinor.FromMinor(long.MaxValue).Negate().Value);

    [TestMethod]
    public void SumOfEmptySpanIsZero() =>
        Assert.AreEqual(MoneyMinor.Zero, MoneyMinor.Sum([]));

    [TestMethod]
    public void SumAccumulatesWithoutIntermediateOverflow()
    {
        MoneyMinor[] values =
        [
            MoneyMinor.FromMinor(long.MaxValue),
            MoneyMinor.FromMinor(long.MaxValue),
            MoneyMinor.FromMinor(long.MinValue),
            MoneyMinor.FromMinor(long.MinValue),
        ];

        Assert.AreEqual(MoneyMinor.FromMinor(-2), MoneyMinor.Sum(values));
    }

    [TestMethod]
    public void SumRejectsResultOutOfRange()
    {
        MoneyMinor[] values =
        [
            MoneyMinor.FromMinor(long.MaxValue),
            MoneyMinor.FromMinor(long.MaxValue),
        ];

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => MoneyMinor.Sum(values));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void ComparisonFollowsSignedOrder()
    {
        MoneyMinor negative = MoneyMinor.FromMinor(-1);
        MoneyMinor zero = MoneyMinor.Zero;
        MoneyMinor positive = MoneyMinor.FromMinor(1);

        Assert.IsTrue(negative < zero);
        Assert.IsTrue(zero < positive);
        Assert.IsTrue(positive > negative);
        Assert.IsTrue(negative <= MoneyMinor.FromMinor(-1));
        Assert.IsTrue(positive >= MoneyMinor.FromMinor(1));
        Assert.AreNotEqual(negative, positive);
    }

    [TestMethod]
    public void OperatorsAgreeWithNamedMethods()
    {
        MoneyMinor left = MoneyMinor.FromMinor(700);
        MoneyMinor right = MoneyMinor.FromMinor(300);

        Assert.AreEqual(left.Add(right), left + right);
        Assert.AreEqual(left.Subtract(right), left - right);
        Assert.AreEqual(left.Negate(), -left);
    }
}
