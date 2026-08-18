using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Tests;

[TestClass]
public sealed class CurrencyTrustQualificationTests
{
    private static CurrencyTrustPolicyRecord Canonical() =>
        new(
            CurrencyTrustPolicyVersionId.FromValue(EntityIdValue.FromBits(1)),
            EconomyScopeId.FromValue(EntityIdValue.FromBits(2)),
            EstablishedMinAgeSeconds: 604_800,
            EstablishedMinTradeDays: 3,
            EstablishedMinCounterparties: 2,
            TrustedMinAgeSeconds: 2_592_000,
            TrustedMinTradeDays: 10,
            TrustedMinCounterparties: 3,
            ReserveMinAgeSeconds: 7_776_000,
            ReserveMinTradeDays: 30,
            ReserveMinCounterparties: 5,
            CurrencyTrustPolicyVersionStatus.Published,
            1);

    private static CurrencyTrustTier Qualify(long age, int days, int parties) =>
        CurrencyTrustAdministrationApplicationService.Qualify(Canonical(), age, days, parties);

    [TestMethod]
    public void ANewCurrencyIsExperimental()
    {
        Assert.AreEqual(CurrencyTrustTier.Experimental, Qualify(0, 0, 0));
    }

    [TestMethod]
    public void TheCanonicalEstablishedThresholdQualifies()
    {
        Assert.AreEqual(CurrencyTrustTier.Established, Qualify(604_800, 3, 2));
    }

    [TestMethod]
    public void MissingAnySingleEstablishedThresholdFallsBack()
    {
        Assert.AreEqual(CurrencyTrustTier.Experimental, Qualify(604_799, 3, 2));
        Assert.AreEqual(CurrencyTrustTier.Experimental, Qualify(604_800, 2, 2));
        Assert.AreEqual(CurrencyTrustTier.Experimental, Qualify(604_800, 3, 1));
    }

    [TestMethod]
    public void TheCanonicalTrustedThresholdQualifies()
    {
        Assert.AreEqual(CurrencyTrustTier.Trusted, Qualify(2_592_000, 10, 3));
    }

    [TestMethod]
    public void TheCanonicalReserveThresholdQualifies()
    {
        Assert.AreEqual(CurrencyTrustTier.ReserveEligible, Qualify(7_776_000, 30, 5));
    }

    [TestMethod]
    public void ShortCounterpartiesCapsTheTierAtTrusted()
    {
        Assert.AreEqual(CurrencyTrustTier.Trusted, Qualify(7_776_000, 30, 4));
    }

    [TestMethod]
    public void ThePolicyFloorsMatchTheCanonicalTable()
    {
        CurrencyTrustPolicyInput canonical = new(
            new CurrencyTrustTierThresholds(604_800, 3, 2),
            new CurrencyTrustTierThresholds(2_592_000, 10, 3),
            new CurrencyTrustTierThresholds(7_776_000, 30, 5));

        Assert.IsTrue(
            CurrencyTrustAdministrationApplicationService.IsWithinCanonicalFloors(canonical));
    }

    [TestMethod]
    public void APolicyBelowAnyCanonicalFloorIsRejected()
    {
        Assert.IsFalse(CurrencyTrustAdministrationApplicationService.IsWithinCanonicalFloors(
            new CurrencyTrustPolicyInput(
                new CurrencyTrustTierThresholds(604_799, 3, 2),
                new CurrencyTrustTierThresholds(2_592_000, 10, 3),
                new CurrencyTrustTierThresholds(7_776_000, 30, 5))));

        Assert.IsFalse(CurrencyTrustAdministrationApplicationService.IsWithinCanonicalFloors(
            new CurrencyTrustPolicyInput(
                new CurrencyTrustTierThresholds(604_800, 3, 2),
                new CurrencyTrustTierThresholds(2_592_000, 9, 3),
                new CurrencyTrustTierThresholds(7_776_000, 30, 5))));

        Assert.IsFalse(CurrencyTrustAdministrationApplicationService.IsWithinCanonicalFloors(
            new CurrencyTrustPolicyInput(
                new CurrencyTrustTierThresholds(604_800, 3, 2),
                new CurrencyTrustTierThresholds(2_592_000, 10, 3),
                new CurrencyTrustTierThresholds(7_776_000, 30, 4))));
    }
}
