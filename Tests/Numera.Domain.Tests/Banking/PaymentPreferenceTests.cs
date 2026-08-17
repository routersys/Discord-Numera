using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class PaymentPreferenceTests
{
    private static readonly PaymentPreferenceId Identifier =
        PaymentPreferenceId.FromValue(EntityIdValue.FromBits(1));

    private static readonly CustomerAccountId Customer = CustomerAccountId.FromValue(EntityIdValue.FromBits(2));
    private static readonly DepositAccountId First = DepositAccountId.FromValue(EntityIdValue.FromBits(3));
    private static readonly DepositAccountId Second = DepositAccountId.FromValue(EntityIdValue.FromBits(4));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp LaterAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_600_000);

    private static PaymentPreference Select() => PaymentPreference.Select(
        Identifier, Customer, PaymentPreferenceKind.DefaultPayment, First, CreatedAt);

    [TestMethod]
    public void SelectionStartsEffective()
    {
        PaymentPreference preference = Select();

        Assert.IsTrue(preference.IsEffective);
        Assert.IsNull(preference.DisabledAt);
        Assert.AreEqual(First, preference.DepositAccountId);
    }

    [TestMethod]
    public void DisablingKeepsTheChosenAccountForTheCustomerToReview()
    {
        PaymentPreference preference = Select();

        preference.Disable(LaterAt);

        Assert.IsFalse(preference.IsEffective);
        Assert.AreEqual(LaterAt, preference.DisabledAt);
        Assert.AreEqual(First, preference.DepositAccountId);
    }

    [TestMethod]
    public void ReselectionClearsTheDisabledMarker()
    {
        PaymentPreference preference = Select();
        preference.Disable(LaterAt);

        preference.Reselect(Second);

        Assert.IsTrue(preference.IsEffective);
        Assert.AreEqual(Second, preference.DepositAccountId);
    }

    [TestMethod]
    public void DisablingTwiceIsRejected()
    {
        PaymentPreference preference = Select();
        preference.Disable(LaterAt);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => preference.Disable(LaterAt));

        Assert.AreEqual(InvariantViolationCode.PaymentPreferenceAlreadyDisabled, exception.Code);
    }

    [TestMethod]
    public void EachChangeAdvancesTheOptimisticVersion()
    {
        PaymentPreference preference = Select();
        long initial = preference.Version;

        preference.Disable(LaterAt);
        preference.Reselect(Second);

        Assert.AreEqual(initial + 2, preference.Version);
        Assert.AreEqual(initial, preference.PersistedVersion);
    }

    [TestMethod]
    public void EveryKindTokenRoundTrips()
    {
        foreach (PaymentPreferenceKind kind in Enum.GetValues<PaymentPreferenceKind>())
        {
            Assert.AreEqual(kind, PaymentPreferenceCatalog.ParseToken(kind.ToToken()));
        }
    }

    [TestMethod]
    public void SchemaCarriesFiveKindsEvenThoughTheScreenOffersFour() =>
        Assert.AreEqual(5, Enum.GetValues<PaymentPreferenceKind>().Length);
}
