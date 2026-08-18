using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class CommerceAndCashStatusTests
{
    [TestMethod]
    public void ACommerceOrderReachesPaidOnlyThroughProcessing()
    {
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.Created, CommerceOrderStatus.AwaitingConfirmation);
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.AwaitingConfirmation, CommerceOrderStatus.Processing);
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.Processing, CommerceOrderStatus.Paid);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CommerceOrderStatusCatalog.EnsureTransition(
                CommerceOrderStatus.AwaitingConfirmation, CommerceOrderStatus.Paid));
    }

    [TestMethod]
    public void ARefundedOrderIsTerminal()
    {
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.Paid, CommerceOrderStatus.PartiallyRefunded);
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.PartiallyRefunded, CommerceOrderStatus.Refunded);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CommerceOrderStatusCatalog.EnsureTransition(
                CommerceOrderStatus.Refunded, CommerceOrderStatus.PartiallyRefunded));
    }

    [TestMethod]
    public void ACommercePaymentCannotFailAfterCapture()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CommercePaymentStatusCatalog.EnsureTransition(
                CommercePaymentStatus.Paid, CommercePaymentStatus.Failed));
    }

    [TestMethod]
    public void ADeclinedAuthorizationIsCreatableAndTerminal()
    {
        DebitCardAuthorizationStatusCatalog.EnsureCreatable(DebitCardAuthorizationStatus.Declined);
        DebitCardAuthorizationStatusCatalog.EnsureCreatable(DebitCardAuthorizationStatus.Authorized);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            DebitCardAuthorizationStatusCatalog.EnsureTransition(
                DebitCardAuthorizationStatus.Declined, DebitCardAuthorizationStatus.Authorized));
    }

    [TestMethod]
    public void ARefundStartsOnlyFromCapturedStates()
    {
        DebitCardAuthorizationStatusCatalog.EnsureTransition(
            DebitCardAuthorizationStatus.Captured, DebitCardAuthorizationStatus.PartiallyRefunded);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            DebitCardAuthorizationStatusCatalog.EnsureTransition(
                DebitCardAuthorizationStatus.Authorized, DebitCardAuthorizationStatus.PartiallyRefunded));
    }

    [TestMethod]
    public void AFailedFulfillmentReturnsToPendingForRetry()
    {
        CommerceFulfillmentStatusCatalog.EnsureTransition(
            CommerceFulfillmentStatus.FailedRetryable, CommerceFulfillmentStatus.Pending);
        CommerceFulfillmentStatusCatalog.EnsureTransition(
            CommerceFulfillmentStatus.FailedManual, CommerceFulfillmentStatus.Pending);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CommerceFulfillmentStatusCatalog.EnsureTransition(
                CommerceFulfillmentStatus.Succeeded, CommerceFulfillmentStatus.Pending));
    }

    [TestMethod]
    public void AReturnCompletesOnlyAfterApproval()
    {
        CommerceReturnStatusCatalog.EnsureTransition(
            CommerceReturnStatus.Pending, CommerceReturnStatus.Approved);
        CommerceReturnStatusCatalog.EnsureTransition(
            CommerceReturnStatus.Approved, CommerceReturnStatus.Completed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CommerceReturnStatusCatalog.EnsureTransition(
                CommerceReturnStatus.Pending, CommerceReturnStatus.Completed));
    }

    [TestMethod]
    public void AnAtmTerminalMayStartOutOfService()
    {
        AtmTerminalStatusCatalog.EnsureCreatable(AtmTerminalStatus.OutOfService);
        AtmTerminalStatusCatalog.EnsureCreatable(AtmTerminalStatus.Operating);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AtmTerminalStatusCatalog.EnsureCreatable(AtmTerminalStatus.Retired));
    }

    [TestMethod]
    public void AnAtmTransactionCannotReturnToPendingAfterCustomerPosting()
    {
        AtmTransactionStatusCatalog.EnsureTransition(
            AtmTransactionStatus.Pending, AtmTransactionStatus.CustomerPosted);
        AtmTransactionStatusCatalog.EnsureTransition(
            AtmTransactionStatus.CustomerPosted, AtmTransactionStatus.InterbankPending);
        AtmTransactionStatusCatalog.EnsureTransition(
            AtmTransactionStatus.InterbankPending, AtmTransactionStatus.Settled);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AtmTransactionStatusCatalog.EnsureTransition(
                AtmTransactionStatus.CustomerPosted, AtmTransactionStatus.Pending));
    }

    [TestMethod]
    public void APlacementAgreementBecomesActiveOnlyFromPendingOrSuspended()
    {
        AtmPlacementAgreementStatusCatalog.EnsureTransition(
            AtmPlacementAgreementStatus.Pending, AtmPlacementAgreementStatus.Active);
        AtmPlacementAgreementStatusCatalog.EnsureTransition(
            AtmPlacementAgreementStatus.Suspended, AtmPlacementAgreementStatus.Active);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AtmPlacementAgreementStatusCatalog.EnsureTransition(
                AtmPlacementAgreementStatus.Ended, AtmPlacementAgreementStatus.Active));
    }

    [TestMethod]
    public void ARetiredDenominationStaysRetired()
    {
        CurrencyDenominationStatusCatalog.EnsureTransition(
            CurrencyDenominationStatus.Active, CurrencyDenominationStatus.Retired);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CurrencyDenominationStatusCatalog.EnsureTransition(
                CurrencyDenominationStatus.Retired, CurrencyDenominationStatus.Active));
    }

    [TestMethod]
    public void ABrokenInstallationCanBeRepaired()
    {
        AtmDiscordInstallationStatusCatalog.EnsureTransition(
            AtmDiscordInstallationStatus.Active, AtmDiscordInstallationStatus.Broken);
        AtmDiscordInstallationStatusCatalog.EnsureTransition(
            AtmDiscordInstallationStatus.Broken, AtmDiscordInstallationStatus.Active);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AtmDiscordInstallationStatusCatalog.EnsureTransition(
                AtmDiscordInstallationStatus.Removed, AtmDiscordInstallationStatus.Active));
    }

    [TestMethod]
    public void EveryCommerceTokenRoundTrips()
    {
        foreach (CommerceOrderStatus status in Enum.GetValues<CommerceOrderStatus>())
        {
            Assert.AreEqual(status, CommerceOrderStatusCatalog.ParseToken(status.ToToken()));
        }

        foreach (AtmTerminalStatus status in Enum.GetValues<AtmTerminalStatus>())
        {
            Assert.AreEqual(status, AtmTerminalStatusCatalog.ParseToken(status.ToToken()));
        }
    }
}
