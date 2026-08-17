using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class CurrencyTests
{
    private static readonly CurrencyId Identifier = CurrencyId.FromValue(EntityIdValue.FromBits(1));
    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp RetiredAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_100_000);

    private static Currency Create(long? capMinor = null) => Currency.Create(
        Identifier,
        Scope,
        MinorUnitDigits.FromInt32(2),
        capMinor is { } cap ? MoneyMinor.FromMinor(cap) : null,
        CreatedAt);

    [TestMethod]
    public void CreatedCurrencyIsActiveAndCurrent()
    {
        Currency currency = Create();

        Assert.AreEqual(CurrencyStatus.Active, currency.Status);
        Assert.IsTrue(currency.IsCurrent);
        Assert.IsTrue(currency.AcceptsSupplyChange);
        Assert.IsNull(currency.RetiredAt);
    }

    [TestMethod]
    public void SuspendAndResumeMoveBetweenActiveAndSuspended()
    {
        Currency currency = Create();

        currency.Suspend();

        Assert.AreEqual(CurrencyStatus.Suspended, currency.Status);
        Assert.IsTrue(currency.IsCurrent);
        Assert.IsFalse(currency.AcceptsSupplyChange);

        currency.Resume();

        Assert.AreEqual(CurrencyStatus.Active, currency.Status);
    }

    [TestMethod]
    public void SuspendedCurrencyCanEnterRetiring()
    {
        Currency currency = Create();
        currency.Suspend();

        currency.BeginRetiring();

        Assert.AreEqual(CurrencyStatus.Retiring, currency.Status);
        Assert.IsTrue(currency.IsCurrent);
    }

    [TestMethod]
    public void RetiredCurrencyIsNoLongerCurrent()
    {
        Currency currency = Create();
        currency.BeginRetiring();

        currency.Retire(RetiredAt, MoneyMinor.Zero);

        Assert.AreEqual(CurrencyStatus.Retired, currency.Status);
        Assert.IsFalse(currency.IsCurrent);
        Assert.AreEqual(RetiredAt, currency.RetiredAt);
    }

    [TestMethod]
    public void RetireIsRejectedWhileSupplyRemains()
    {
        Currency currency = Create();
        currency.BeginRetiring();

        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => currency.Retire(RetiredAt, MoneyMinor.FromMinor(1)));

        Assert.AreEqual(InvariantViolationCode.CurrencyRetirementBlocked, violation.Code);
        Assert.AreEqual(CurrencyStatus.Retiring, currency.Status);
    }

    [TestMethod]
    public void ActiveCurrencyCannotBecomeRetiredDirectly()
    {
        Currency currency = Create();

        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => currency.Retire(RetiredAt, MoneyMinor.Zero));

        Assert.AreEqual(InvariantViolationCode.CurrencyTransitionInvalid, violation.Code);
    }

    [TestMethod]
    public void RetiredCurrencyIsTerminal()
    {
        Currency currency = Create();
        currency.BeginRetiring();
        currency.Retire(RetiredAt, MoneyMinor.Zero);

        Assert.ThrowsExactly<InvariantViolationException>(currency.Resume);
        Assert.ThrowsExactly<InvariantViolationException>(currency.Suspend);
    }

    [TestMethod]
    public void SupplyCapRejectsProjectionAboveTheCeiling()
    {
        Currency currency = Create(1_000);

        MoneyMinor projected = currency.ProjectSupplyAfterIssue(
            MoneyMinor.FromMinor(900), MoneyMinor.FromMinor(101));

        Assert.AreEqual(1_001L, projected.Value);
        Assert.IsTrue(currency.ExceedsSupplyCap(projected));
        Assert.IsFalse(currency.ExceedsSupplyCap(MoneyMinor.FromMinor(1_000)));
    }

    [TestMethod]
    public void AbsentSupplyCapNeverBlocksIssue()
    {
        Currency currency = Create();

        Assert.IsFalse(currency.ExceedsSupplyCap(MoneyMinor.FromMinor(long.MaxValue)));
    }

    [TestMethod]
    public void BurnBelowZeroSupplyIsAnInvariantViolation()
    {
        Currency currency = Create();

        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => currency.ProjectSupplyAfterBurn(MoneyMinor.FromMinor(10), MoneyMinor.FromMinor(11)));

        Assert.AreEqual(InvariantViolationCode.CurrencySupplyNegative, violation.Code);
    }

    [TestMethod]
    public void RehydrateRejectsRetiredWithoutTimestamp()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => Currency.Rehydrate(
                Identifier,
                Scope,
                CurrencyStatus.Retired,
                MinorUnitDigits.FromInt32(2),
                null,
                CreatedAt,
                retiredAt: null,
                version: 3));

        Assert.AreEqual(InvariantViolationCode.CurrencyTransitionInvalid, violation.Code);
    }

    [TestMethod]
    public void NegativeSupplyCapIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => Currency.Create(
                Identifier,
                Scope,
                MinorUnitDigits.FromInt32(2),
                MoneyMinor.FromMinor(-1),
                CreatedAt));

        Assert.AreEqual(InvariantViolationCode.CurrencySupplyCapInvalid, violation.Code);
    }

    [TestMethod]
    public void EveryStatusRoundTripsThroughItsToken()
    {
        foreach (CurrencyStatus status in Enum.GetValues<CurrencyStatus>())
        {
            Assert.AreEqual(status, CurrencyCatalog.ParseToken(status.ToToken()));
        }

        Assert.IsFalse(CurrencyCatalog.TryParseToken("NOT_A_STATUS", out _));
    }
}
