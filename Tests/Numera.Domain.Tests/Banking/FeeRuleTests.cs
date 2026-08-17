using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class FeeRuleTests
{
    private static readonly FeeScheduleVersionId Schedule =
        FeeScheduleVersionId.FromValue(EntityIdValue.FromBits(1));

    private static readonly AccountProductId Product = AccountProductId.FromValue(EntityIdValue.FromBits(10));
    private static readonly BankId Counterparty = BankId.FromValue(EntityIdValue.FromBits(11));

    private static FeeRule Rule(
        int seed = 1,
        int priority = 100,
        FeeChannel channel = FeeChannel.Any,
        AccountProductId? product = null,
        BankId? counterparty = null,
        long amountMinimum = 0,
        long? amountMaximum = null,
        FeeRuleDayClass dayClass = FeeRuleDayClass.Any,
        int? startMinute = null,
        int? endMinute = null,
        long fixedAmount = 0,
        int basisPoints = 0,
        long minimumAmount = 0,
        long? maximumAmount = null,
        string? waiverCounterKey = null,
        int freeOccurrences = 0) =>
        FeeRule.Create(
            FeeRuleId.FromValue(EntityIdValue.FromBits((ulong)seed)),
            Schedule,
            FeeType.SameBankTransfer,
            priority,
            channel,
            product,
            atmNetworkId: null,
            counterparty,
            MoneyMinor.FromMinor(amountMinimum),
            amountMaximum is { } maximumBound ? MoneyMinor.FromMinor(maximumBound) : null,
            dayClass,
            startMinute,
            endMinute,
            MoneyMinor.FromMinor(fixedAmount),
            basisPoints,
            MoneyMinor.FromMinor(minimumAmount),
            maximumAmount is { } cap ? MoneyMinor.FromMinor(cap) : null,
            waiverCounterKey,
            freeOccurrences);

    private static FeeMatchContext Context(
        long amount = 10_000,
        FeeChannel channel = FeeChannel.Discord,
        AccountProductId? product = null,
        BankId? counterparty = null,
        BusinessDayClass dayClass = BusinessDayClass.BusinessDay,
        int localMinuteOfDay = 600) =>
        new(channel, product, null, counterparty, MoneyMinor.FromMinor(amount), dayClass, localMinuteOfDay);

    [TestMethod]
    public void FixedAndProportionalPartsAreSummedWithFlooring()
    {
        FeeRule rule = Rule(fixedAmount: 100, basisPoints: 55);

        Assert.AreEqual(155L, rule.Calculate(MoneyMinor.FromMinor(10_000)).Value);
        Assert.AreEqual(100L, rule.Calculate(MoneyMinor.FromMinor(181)).Value);
    }

    [TestMethod]
    public void MinimumIsAppliedBeforeMaximum()
    {
        FeeRule rule = Rule(basisPoints: 100, minimumAmount: 50, maximumAmount: 200);

        Assert.AreEqual(50L, rule.Calculate(MoneyMinor.FromMinor(1_000)).Value);
        Assert.AreEqual(100L, rule.Calculate(MoneyMinor.FromMinor(10_000)).Value);
        Assert.AreEqual(200L, rule.Calculate(MoneyMinor.FromMinor(100_000)).Value);
    }

    [TestMethod]
    public void ProportionalPartUsesWideIntermediateArithmetic()
    {
        FeeRule rule = Rule(basisPoints: FeeRule.MaximumBasisPoints);

        Assert.AreEqual(long.MaxValue / 10_000 * 10, rule.Calculate(MoneyMinor.FromMinor(long.MaxValue / 10_000)).Value);
    }

    [TestMethod]
    public void ChannelAnyMatchesEveryChannel()
    {
        Assert.IsTrue(Rule().Matches(Context(channel: FeeChannel.Atm)));
        Assert.IsFalse(Rule(channel: FeeChannel.Atm).Matches(Context(channel: FeeChannel.Discord)));
    }

    [TestMethod]
    public void AmountWindowIsHalfOpen()
    {
        FeeRule rule = Rule(amountMinimum: 1_000, amountMaximum: 2_000);

        Assert.IsFalse(rule.Matches(Context(amount: 999)));
        Assert.IsTrue(rule.Matches(Context(amount: 1_000)));
        Assert.IsTrue(rule.Matches(Context(amount: 1_999)));
        Assert.IsFalse(rule.Matches(Context(amount: 2_000)));
    }

    [TestMethod]
    public void TimeWindowIsHalfOpen()
    {
        FeeRule rule = Rule(startMinute: 540, endMinute: 900);

        Assert.IsFalse(rule.Matches(Context(localMinuteOfDay: 539)));
        Assert.IsTrue(rule.Matches(Context(localMinuteOfDay: 540)));
        Assert.IsTrue(rule.Matches(Context(localMinuteOfDay: 899)));
        Assert.IsFalse(rule.Matches(Context(localMinuteOfDay: 900)));
    }

    [TestMethod]
    public void DayClassQualifierIsRespected()
    {
        FeeRule rule = Rule(dayClass: FeeRuleDayClass.NonBusinessDay);

        Assert.IsFalse(rule.Matches(Context(dayClass: BusinessDayClass.BusinessDay)));
        Assert.IsTrue(rule.Matches(Context(dayClass: BusinessDayClass.NonBusinessDay)));
    }

    [TestMethod]
    public void QualifiersRequireExactCounterpartAndProduct()
    {
        FeeRule rule = Rule(product: Product, counterparty: Counterparty);

        Assert.IsFalse(rule.Matches(Context(product: Product)));
        Assert.IsFalse(rule.Matches(Context(counterparty: Counterparty)));
        Assert.IsTrue(rule.Matches(Context(product: Product, counterparty: Counterparty)));
    }

    [TestMethod]
    public void SelectionPrefersTheSmallestPriority()
    {
        FeeRule general = Rule(seed: 1, priority: 500, fixedAmount: 300);
        FeeRule specific = Rule(seed: 2, priority: 10, channel: FeeChannel.Discord, fixedAmount: 100);

        FeeRule? selected = FeeRuleSelection.Select([general, specific], Context());

        Assert.AreEqual(specific.Id, selected!.Id);
    }

    [TestMethod]
    public void SelectionSkipsRulesThatDoNotMatch()
    {
        FeeRule unmatched = Rule(seed: 1, priority: 10, channel: FeeChannel.Atm);
        FeeRule catchAll = Rule(seed: 2, priority: 500);

        FeeRule? selected = FeeRuleSelection.Select([unmatched, catchAll], Context());

        Assert.AreEqual(catchAll.Id, selected!.Id);
        Assert.IsTrue(catchAll.IsCatchAll);
    }

    [TestMethod]
    public void SelectionIsIndependentOfInputOrder()
    {
        FeeRule first = Rule(seed: 7, priority: 100);
        FeeRule second = Rule(seed: 3, priority: 100);

        Assert.AreEqual(second.Id, FeeRuleSelection.Select([first, second], Context())!.Id);
        Assert.AreEqual(second.Id, FeeRuleSelection.Select([second, first], Context())!.Id);
    }

    [TestMethod]
    public void NoMatchingRuleYieldsNothing() =>
        Assert.IsNull(FeeRuleSelection.Select([Rule(channel: FeeChannel.Atm)], Context()));

    [TestMethod]
    public void FreeOccurrencesRequireAWaiverCounterKey()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(freeOccurrences: 3));

        Assert.AreEqual(InvariantViolationCode.FeeRuleWaiverInvalid, exception.Code);
    }

    [TestMethod]
    public void PartialTimeWindowIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(startMinute: 540));

        Assert.AreEqual(InvariantViolationCode.FeeRuleTimeWindowInvalid, exception.Code);
    }

    [TestMethod]
    public void InvertedTimeWindowIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(startMinute: 900, endMinute: 540));

        Assert.AreEqual(InvariantViolationCode.FeeRuleTimeWindowInvalid, exception.Code);
    }

    [TestMethod]
    public void InvertedAmountWindowIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(amountMinimum: 2_000, amountMaximum: 1_000));

        Assert.AreEqual(InvariantViolationCode.FeeRuleAmountRangeInvalid, exception.Code);
    }

    [TestMethod]
    public void MaximumBelowMinimumIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(minimumAmount: 200, maximumAmount: 100));

        Assert.AreEqual(InvariantViolationCode.FeeRuleFormulaInvalid, exception.Code);
    }

    [TestMethod]
    public void PriorityAboveTheCanonicalCeilingIsRejected()
    {
        int excessive = FeeRule.MaximumPriority + 1;

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Rule(priority: excessive));

        Assert.AreEqual(InvariantViolationCode.FeeRulePriorityInvalid, exception.Code);
    }

    [TestMethod]
    public void EveryFeeTypeTokenRoundTrips()
    {
        foreach (FeeType feeType in Enum.GetValues<FeeType>())
        {
            Assert.AreEqual(feeType, FeeCatalog.ParseFeeTypeToken(feeType.ToToken()));
        }
    }

    [TestMethod]
    public void EveryChannelTokenRoundTrips()
    {
        foreach (FeeChannel channel in Enum.GetValues<FeeChannel>())
        {
            Assert.AreEqual(channel, FeeCatalog.ParseChannelToken(channel.ToToken()));
        }
    }

    [TestMethod]
    public void EveryDayClassTokenRoundTrips()
    {
        foreach (FeeRuleDayClass dayClass in Enum.GetValues<FeeRuleDayClass>())
        {
            Assert.AreEqual(dayClass, FeeCatalog.ParseDayClassToken(dayClass.ToToken()));
        }

        foreach (BusinessDayClass dayClass in Enum.GetValues<BusinessDayClass>())
        {
            Assert.AreEqual(dayClass, BusinessDayClassCatalog.ParseToken(dayClass.ToToken()));
        }
    }

    [TestMethod]
    public void WeekendsAreNonBusinessDaysByDefault()
    {
        Assert.AreEqual(
            BusinessDayClass.NonBusinessDay,
            BusinessDayClassCatalog.FromWeekday(BusinessDate.FromParts(2026, 8, 15)));
        Assert.AreEqual(
            BusinessDayClass.NonBusinessDay,
            BusinessDayClassCatalog.FromWeekday(BusinessDate.FromParts(2026, 8, 16)));
        Assert.AreEqual(
            BusinessDayClass.BusinessDay,
            BusinessDayClassCatalog.FromWeekday(BusinessDate.FromParts(2026, 8, 17)));
    }

    [TestMethod]
    public void BusinessMonthIsTheLocalYearMonthKey() =>
        Assert.AreEqual(202608, BusinessDate.FromParts(2026, 8, 17).BusinessMonth);
}
