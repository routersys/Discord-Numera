using Numera.Domain.Common;

namespace Numera.Domain.Tests.Common;

[TestClass]
public sealed class RateTests
{
    [TestMethod]
    public void OneHundredPercentPreservesPrincipal() =>
        Assert.AreEqual((Int128)12_345, Rate.FromPartsPerTrillion(Rate.OneHundredPercent).ApplyToIntermediate(12_345));

    [TestMethod]
    public void OnePercentIsExactWhenDivisible() =>
        Assert.AreEqual((Int128)100, Rate.FromPartsPerTrillion(Rate.OnePercent).ApplyToIntermediate(10_000));

    [TestMethod]
    public void FractionalResultTruncatesTowardZero()
    {
        Rate rate = Rate.FromPartsPerTrillion(Rate.OnePercent);

        Assert.AreEqual(Int128.Zero, rate.ApplyToIntermediate(99));
        Assert.AreEqual(Int128.Zero, rate.ApplyToIntermediate(-99));
    }

    [TestMethod]
    public void NegativeRateIsRejected()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => Rate.FromPartsPerTrillion(-1));

        Assert.AreEqual(InvariantViolationCode.RateOutOfRange, exception.Code);
    }

    [TestMethod]
    public void ZeroRateProducesZero() =>
        Assert.AreEqual(Int128.Zero, Rate.Zero.ApplyToIntermediate(long.MaxValue));

    [TestMethod]
    public void DayCountFractionAppliesBeforeDivision()
    {
        Rate rate = Rate.FromPartsPerTrillion(Rate.OnePercent * 365);

        Assert.AreEqual((Int128)10_000, rate.ApplyToIntermediate(1_000_000, 1, 365));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void NonPositiveDenominatorIsRejected(int denominator)
    {
        Rate rate = Rate.FromPartsPerTrillion(Rate.OnePercent);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => rate.ApplyToIntermediate(1_000, 1, denominator));

        Assert.AreEqual(InvariantViolationCode.RateOutOfRange, exception.Code);
    }

    [TestMethod]
    public void NegativeNumeratorIsRejected()
    {
        Rate rate = Rate.FromPartsPerTrillion(Rate.OnePercent);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => rate.ApplyToIntermediate(1_000, -1, 365));

        Assert.AreEqual(InvariantViolationCode.RateOutOfRange, exception.Code);
    }

    [TestMethod]
    public void LargePrincipalStaysWithinIntermediateRange()
    {
        Rate rate = Rate.FromPartsPerTrillion(Rate.OneHundredPercent);

        Assert.AreEqual((Int128)long.MaxValue, rate.ApplyToIntermediate(long.MaxValue));
    }
}

[TestClass]
public sealed class MinorUnitDigitsTests
{
    [TestMethod]
    [DataRow(0, 1L)]
    [DataRow(1, 10L)]
    [DataRow(2, 100L)]
    [DataRow(3, 1_000L)]
    [DataRow(4, 10_000L)]
    [DataRow(5, 100_000L)]
    [DataRow(6, 1_000_000L)]
    public void ScaleFactorMatchesDigits(int digits, long expected) =>
        Assert.AreEqual(expected, MinorUnitDigits.FromInt32(digits).ScaleFactor);

    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void OutOfRangeDigitsAreRejected(int digits)
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => MinorUnitDigits.FromInt32(digits));

        Assert.AreEqual(InvariantViolationCode.MinorUnitDigitsOutOfRange, exception.Code);
    }

    [TestMethod]
    public void OrderingFollowsDigitCount() =>
        Assert.IsTrue(MinorUnitDigits.FromInt32(0) < MinorUnitDigits.FromInt32(6));
}

[TestClass]
public sealed class UtcTimestampTests
{
    [TestMethod]
    public void NegativeUnixTimeIsRejected()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => UtcTimestamp.FromUnixMilliseconds(-1));

        Assert.AreEqual(InvariantViolationCode.TimestampOutOfRange, exception.Code);
    }

    [TestMethod]
    public void EpochIsZero() => Assert.AreEqual(0L, UtcTimestamp.Epoch.UnixMilliseconds);

    [TestMethod]
    public void RoundTripThroughDateTimeOffsetPreservesMilliseconds()
    {
        UtcTimestamp original = UtcTimestamp.FromUnixMilliseconds(1_776_000_123_456L);

        Assert.AreEqual(original, UtcTimestamp.FromDateTimeOffset(original.ToDateTimeOffset()));
    }

    [TestMethod]
    public void ForwardShiftBeyondRangeIsRejected()
    {
        UtcTimestamp maximum = UtcTimestamp.FromUnixMilliseconds(long.MaxValue);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => maximum.AddMilliseconds(1));

        Assert.AreEqual(InvariantViolationCode.TimestampOutOfRange, exception.Code);
    }

    [TestMethod]
    public void BackwardShiftBeforeEpochIsRejected()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => UtcTimestamp.Epoch.AddMilliseconds(-1));

        Assert.AreEqual(InvariantViolationCode.TimestampOutOfRange, exception.Code);
    }

    [TestMethod]
    public void TextFormatIsIso8601Utc() =>
        Assert.AreEqual("1970-01-01T00:00:00.000Z", UtcTimestamp.Epoch.ToString());
}

[TestClass]
public sealed class BusinessDateTests
{
    [TestMethod]
    public void LeapDayIsAcceptedInLeapYear() =>
        Assert.AreEqual("2024-02-29", BusinessDate.FromParts(2024, 2, 29).ToString());

    [TestMethod]
    public void LeapDayIsRejectedInCommonYear()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => BusinessDate.FromParts(2023, 2, 29));

        Assert.AreEqual(InvariantViolationCode.BusinessDateInvalid, exception.Code);
    }

    [TestMethod]
    [DataRow(2023, 2, 30)]
    [DataRow(2023, 4, 31)]
    [DataRow(2023, 13, 1)]
    [DataRow(2023, 0, 1)]
    [DataRow(2023, 1, 0)]
    [DataRow(0, 1, 1)]
    public void InvalidPartsAreRejected(int year, int month, int day) =>
        Assert.ThrowsExactly<InvariantViolationException>(() => BusinessDate.FromParts(year, month, day));

    [TestMethod]
    [DataRow("2024-02-29")]
    [DataRow("0001-01-01")]
    [DataRow("9999-12-31")]
    public void CanonicalTextRoundTrips(string source) =>
        Assert.AreEqual(source, BusinessDate.Parse(source).ToString());

    [TestMethod]
    [DataRow("")]
    [DataRow("2024-1-01")]
    [DataRow("2024/01/01")]
    [DataRow("2024-01-1")]
    [DataRow("2024-13-01")]
    [DataRow("2023-02-29")]
    [DataRow("2024-01-01 ")]
    [DataRow("20240101")]
    public void MalformedTextIsRejected(string source) =>
        Assert.IsFalse(BusinessDate.TryParse(source, out _));

    [TestMethod]
    public void AddDaysCrossesMonthAndYearBoundaries()
    {
        Assert.AreEqual("2024-03-01", BusinessDate.Parse("2024-02-29").AddDays(1).ToString());
        Assert.AreEqual("2025-01-01", BusinessDate.Parse("2024-12-31").AddDays(1).ToString());
        Assert.AreEqual("2024-02-29", BusinessDate.Parse("2024-03-01").AddDays(-1).ToString());
    }

    [TestMethod]
    public void AddDaysBeyondSupportedRangeIsRejected()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => BusinessDate.Parse("9999-12-31").AddDays(1));
        Assert.ThrowsExactly<InvariantViolationException>(() => BusinessDate.Parse("0001-01-01").AddDays(-1));
    }

    [TestMethod]
    public void OrderingFollowsCalendarOrder()
    {
        BusinessDate earlier = BusinessDate.Parse("2024-02-29");
        BusinessDate later = BusinessDate.Parse("2024-03-01");

        Assert.IsTrue(earlier < later);
        Assert.IsTrue(later > earlier);
        Assert.AreEqual(earlier, BusinessDate.FromDayNumber(earlier.DayNumber));
    }
}
