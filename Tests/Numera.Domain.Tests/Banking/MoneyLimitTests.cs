using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class MoneyLimitTests
{
    private static MoneyMinor? Amount(long? value) => value is { } minor ? MoneyMinor.FromMinor(minor) : null;

    private static MoneyLimit Resolve(long? bankCeiling, long? customerPreference) =>
        MoneyLimit.Resolve(Amount(bankCeiling), Amount(customerPreference));

    [TestMethod]
    public void BothUnsetMeansUnlimited() => Assert.IsNull(Resolve(null, null).Ceiling);

    [TestMethod]
    public void OnlyOneSideSetBecomesTheEffectiveCeiling()
    {
        Assert.AreEqual(500L, Resolve(500, null).Ceiling!.Value.Value);
        Assert.AreEqual(300L, Resolve(null, 300).Ceiling!.Value.Value);
    }

    [TestMethod]
    public void TheStricterSideWins()
    {
        Assert.AreEqual(300L, Resolve(500, 300).Ceiling!.Value.Value);
        Assert.AreEqual(200L, Resolve(200, 900).Ceiling!.Value.Value);
    }

    [TestMethod]
    public void CustomerPreferenceCannotRaiseTheBankCeiling() =>
        Assert.AreEqual(200L, Resolve(200, long.MaxValue).Ceiling!.Value.Value);

    [TestMethod]
    public void UnlimitedAllowsAnyAmount() =>
        Assert.AreEqual(
            LimitOutcome.Allowed,
            MoneyLimit.Unlimited.Evaluate(MoneyMinor.Zero, MoneyMinor.FromMinor(long.MaxValue)));

    [TestMethod]
    public void ZeroCeilingDisablesTheOperation()
    {
        MoneyLimit limit = Resolve(1_000, 0);

        Assert.IsTrue(limit.IsDisabled);
        Assert.AreEqual(LimitOutcome.Disabled, limit.Evaluate(MoneyMinor.Zero, MoneyMinor.FromMinor(1)));
    }

    [TestMethod]
    public void UsageIsAddedBeforeComparison()
    {
        MoneyLimit limit = Resolve(1_000, null);

        Assert.AreEqual(
            LimitOutcome.Allowed, limit.Evaluate(MoneyMinor.FromMinor(400), MoneyMinor.FromMinor(600)));
        Assert.AreEqual(
            LimitOutcome.Exceeded, limit.Evaluate(MoneyMinor.FromMinor(400), MoneyMinor.FromMinor(601)));
    }

    [TestMethod]
    public void TheCeilingItselfIsAllowed() =>
        Assert.AreEqual(
            LimitOutcome.Allowed,
            Resolve(1_000, null).Evaluate(MoneyMinor.Zero, MoneyMinor.FromMinor(1_000)));

    [TestMethod]
    public void NegativeCeilingIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Resolve(-1, null));

        Assert.AreEqual(InvariantViolationCode.LimitValueNegative, exception.Code);
    }

    [TestMethod]
    public void NegativeUsageIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Resolve(1_000, null).Evaluate(MoneyMinor.FromMinor(-1), MoneyMinor.FromMinor(1)));

        Assert.AreEqual(InvariantViolationCode.LimitUsageInvalid, exception.Code);
    }

    [TestMethod]
    public void AccumulationNearTheLongBoundaryDoesNotWrap()
    {
        MoneyLimit limit = Resolve(long.MaxValue, null);

        Assert.AreEqual(
            LimitOutcome.Allowed,
            limit.Evaluate(MoneyMinor.FromMinor(long.MaxValue - 1), MoneyMinor.FromMinor(1)));
        Assert.ThrowsExactly<InvariantViolationException>(
            () => limit.Evaluate(MoneyMinor.FromMinor(long.MaxValue), MoneyMinor.FromMinor(1)));
    }
}
