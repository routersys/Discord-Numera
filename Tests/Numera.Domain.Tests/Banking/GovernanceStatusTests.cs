using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class GovernanceStatusTests
{
    [TestMethod]
    public void AVersionRowPublishesThenRetires()
    {
        PresentationProfileStatusCatalog.EnsureTransition(
            PresentationProfileVersionStatus.Draft, PresentationProfileVersionStatus.Published);
        PresentationProfileStatusCatalog.EnsureTransition(
            PresentationProfileVersionStatus.Published, PresentationProfileVersionStatus.Retired);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            PresentationProfileStatusCatalog.EnsureTransition(
                PresentationProfileVersionStatus.Retired, PresentationProfileVersionStatus.Published));
    }

    [TestMethod]
    public void ARetiredPolicyCannotReturnToDraft()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CurrencyTrustPolicyStatusCatalog.EnsureTransition(
                CurrencyTrustPolicyVersionStatus.Retired, CurrencyTrustPolicyVersionStatus.Draft));
    }

    [TestMethod]
    public void ASupersededDesignationIsTerminal()
    {
        CurrencyTrustDesignationStatusCatalog.EnsureTransition(
            CurrencyTrustDesignationStatus.Active, CurrencyTrustDesignationStatus.Suspended);
        CurrencyTrustDesignationStatusCatalog.EnsureTransition(
            CurrencyTrustDesignationStatus.Suspended, CurrencyTrustDesignationStatus.Active);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            CurrencyTrustDesignationStatusCatalog.EnsureTransition(
                CurrencyTrustDesignationStatus.Superseded, CurrencyTrustDesignationStatus.Active));
    }

    [TestMethod]
    public void AResolutionCaseCannotSkipRestricted()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            ResolutionCaseStatusCatalog.EnsureTransition(
                ResolutionCaseStatus.Open, ResolutionCaseStatus.TransferInProgress));

        ResolutionCaseStatusCatalog.EnsureTransition(
            ResolutionCaseStatus.Open, ResolutionCaseStatus.Restricted);
        ResolutionCaseStatusCatalog.EnsureTransition(
            ResolutionCaseStatus.Restricted, ResolutionCaseStatus.TransferInProgress);
        ResolutionCaseStatusCatalog.EnsureTransition(
            ResolutionCaseStatus.TransferInProgress, ResolutionCaseStatus.Resolved);
    }

    [TestMethod]
    public void AResolvedCaseIsTerminal()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            ResolutionCaseStatusCatalog.EnsureTransition(
                ResolutionCaseStatus.Resolved, ResolutionCaseStatus.Liquidated));
    }

    [TestMethod]
    public void AMandateReachesTerminalFromEveryLiveState()
    {
        FxInterventionMandateStatusCatalog.EnsureTransition(
            FxInterventionMandateStatus.Draft, FxInterventionMandateStatus.Active);
        FxInterventionMandateStatusCatalog.EnsureTransition(
            FxInterventionMandateStatus.Active, FxInterventionMandateStatus.Suspended);
        FxInterventionMandateStatusCatalog.EnsureTransition(
            FxInterventionMandateStatus.Suspended, FxInterventionMandateStatus.Expired);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            FxInterventionMandateStatusCatalog.EnsureTransition(
                FxInterventionMandateStatus.Expired, FxInterventionMandateStatus.Active));
    }

    [TestMethod]
    public void AClosingMerchantMayReturnToSuspendedButNotActive()
    {
        MerchantProfileStatusCatalog.EnsureTransition(
            MerchantProfileStatus.Closing, MerchantProfileStatus.Suspended);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            MerchantProfileStatusCatalog.EnsureTransition(
                MerchantProfileStatus.Closing, MerchantProfileStatus.Active));
    }

    [TestMethod]
    public void ALoanReachesPaidFromEveryLiveState()
    {
        LoanContractStatusCatalog.EnsureTransition(
            LoanContractStatus.Active, LoanContractStatus.Paid);
        LoanContractStatusCatalog.EnsureTransition(
            LoanContractStatus.Delinquent, LoanContractStatus.Paid);
        LoanContractStatusCatalog.EnsureTransition(
            LoanContractStatus.Defaulted, LoanContractStatus.Paid);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            LoanContractStatusCatalog.EnsureTransition(
                LoanContractStatus.Approved, LoanContractStatus.Paid));
    }

    [TestMethod]
    public void AWaivedInstalmentIsTerminal()
    {
        LoanScheduleStatusCatalog.EnsureTransition(
            LoanScheduleStatus.Due, LoanScheduleStatus.PartiallyPaid);
        LoanScheduleStatusCatalog.EnsureTransition(
            LoanScheduleStatus.PartiallyPaid, LoanScheduleStatus.Overdue);
        LoanScheduleStatusCatalog.EnsureTransition(
            LoanScheduleStatus.Overdue, LoanScheduleStatus.Paid);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            LoanScheduleStatusCatalog.EnsureTransition(
                LoanScheduleStatus.Waived, LoanScheduleStatus.Due));
    }

    [TestMethod]
    public void EveryGovernanceTokenRoundTrips()
    {
        foreach (MonetaryAuthorityStatus status in Enum.GetValues<MonetaryAuthorityStatus>())
        {
            Assert.AreEqual(status, MonetaryAuthorityStatusCatalog.ParseToken(status.ToToken()));
        }

        foreach (OfficialReservePortfolioStatus status in
                 Enum.GetValues<OfficialReservePortfolioStatus>())
        {
            Assert.AreEqual(
                status, OfficialReservePortfolioStatusCatalog.ParseToken(status.ToToken()));
        }

        foreach (CurrencyTrustTier tier in Enum.GetValues<CurrencyTrustTier>())
        {
            Assert.AreEqual(tier, CurrencyTrustTierCatalog.ParseToken(tier.ToToken()));
        }
    }

    [TestMethod]
    public void OnlyExperimentalSkipsSystemOwnerApproval()
    {
        Assert.IsFalse(CurrencyTrustTier.Experimental.RequiresSystemOwnerApproval());
        Assert.IsTrue(CurrencyTrustTier.Established.RequiresSystemOwnerApproval());
        Assert.IsTrue(CurrencyTrustTier.Trusted.RequiresSystemOwnerApproval());
        Assert.IsTrue(CurrencyTrustTier.ReserveEligible.RequiresSystemOwnerApproval());
    }
}
