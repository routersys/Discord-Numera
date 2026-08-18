using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Accounting;

[TestClass]
public sealed class LedgerLifecycleStatusTests
{
    [TestMethod]
    public void ABookReturnsFromReconciliationRequired()
    {
        AccountingBookStatusCatalog.EnsureCreatable(AccountingBookStatus.Open);
        AccountingBookStatusCatalog.EnsureTransition(
            AccountingBookStatus.Open, AccountingBookStatus.ReconciliationRequired);
        AccountingBookStatusCatalog.EnsureTransition(
            AccountingBookStatus.ReconciliationRequired, AccountingBookStatus.Open);
        AccountingBookStatusCatalog.EnsureTransition(
            AccountingBookStatus.ReconciliationRequired, AccountingBookStatus.Closed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AccountingBookStatusCatalog.EnsureTransition(
                AccountingBookStatus.Closed, AccountingBookStatus.Open));
    }

    [TestMethod]
    public void AClosingPeriodCanAbortBackToOpen()
    {
        AccountingPeriodStatusCatalog.EnsureTransition(
            AccountingPeriodStatus.Open, AccountingPeriodStatus.Closing);
        AccountingPeriodStatusCatalog.EnsureTransition(
            AccountingPeriodStatus.Closing, AccountingPeriodStatus.Open);
        AccountingPeriodStatusCatalog.EnsureTransition(
            AccountingPeriodStatus.Closing, AccountingPeriodStatus.Closed);

        Assert.IsFalse(AccountingPeriodStatusCatalog.IsAllowed(
            AccountingPeriodStatus.Open, AccountingPeriodStatus.Closed));
    }

    [TestMethod]
    public void APostedTransactionHasNoTransition()
    {
        AccountingTransactionStatusCatalog.EnsureCreatable(AccountingTransactionStatus.Posted);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AccountingTransactionStatusCatalog.EnsureTransition(
                AccountingTransactionStatus.Posted, AccountingTransactionStatus.Posted));
    }

    [TestMethod]
    public void ARestrictedBranchReopensOrCloses()
    {
        BranchStatusCatalog.EnsureTransition(BranchStatus.Active, BranchStatus.Restricted);
        BranchStatusCatalog.EnsureTransition(BranchStatus.Restricted, BranchStatus.Active);
        BranchStatusCatalog.EnsureTransition(BranchStatus.Restricted, BranchStatus.Closed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            BranchStatusCatalog.EnsureTransition(BranchStatus.Closed, BranchStatus.Active));
    }

    [TestMethod]
    public void ADisabledEconomyCanBeReactivated()
    {
        GuildEconomyStatusCatalog.EnsureTransition(GuildEconomyStatus.Active, GuildEconomyStatus.Disabled);
        GuildEconomyStatusCatalog.EnsureTransition(GuildEconomyStatus.Disabled, GuildEconomyStatus.Active);

        Assert.IsFalse(GuildEconomyStatusCatalog.IsAllowed(
            GuildEconomyStatus.Disabled, GuildEconomyStatus.Suspended));
    }

    [TestMethod]
    public void APublishedPrudentialPolicyOnlyRetires()
    {
        PrudentialPolicyVersionStatusCatalog.EnsureTransition(
            PrudentialPolicyVersionStatus.Draft, PrudentialPolicyVersionStatus.Published);
        PrudentialPolicyVersionStatusCatalog.EnsureTransition(
            PrudentialPolicyVersionStatus.Published, PrudentialPolicyVersionStatus.Retired);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            PrudentialPolicyVersionStatusCatalog.EnsureTransition(
                PrudentialPolicyVersionStatus.Published, PrudentialPolicyVersionStatus.Draft));
    }

    [TestMethod]
    public void AnIdempotencyRecordLeavesInProgressOnce()
    {
        IdempotencyRecordStatusCatalog.EnsureCreatable(IdempotencyRecordStatus.InProgress);
        IdempotencyRecordStatusCatalog.EnsureTransition(
            IdempotencyRecordStatus.InProgress, IdempotencyRecordStatus.Completed);
        IdempotencyRecordStatusCatalog.EnsureTransition(
            IdempotencyRecordStatus.InProgress, IdempotencyRecordStatus.Failed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            IdempotencyRecordStatusCatalog.EnsureTransition(
                IdempotencyRecordStatus.Failed, IdempotencyRecordStatus.Completed));
    }

    [TestMethod]
    public void AnInboxEventIsProcessedOrFailedOnce()
    {
        InboxEventStatusCatalog.EnsureCreatable(InboxEventStatus.Received);
        InboxEventStatusCatalog.EnsureTransition(InboxEventStatus.Received, InboxEventStatus.Processed);
        InboxEventStatusCatalog.EnsureTransition(InboxEventStatus.Received, InboxEventStatus.Failed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            InboxEventStatusCatalog.EnsureTransition(
                InboxEventStatus.Failed, InboxEventStatus.Processed));
    }

    [TestMethod]
    public void AFailedInterestBatchIsTerminal()
    {
        InterestPostingBatchStatusCatalog.EnsureTransition(
            InterestPostingBatchStatus.Pending, InterestPostingBatchStatus.Posted);
        InterestPostingBatchStatusCatalog.EnsureTransition(
            InterestPostingBatchStatus.Pending, InterestPostingBatchStatus.Failed);

        Assert.IsFalse(InterestPostingBatchStatusCatalog.IsAllowed(
            InterestPostingBatchStatus.Failed, InterestPostingBatchStatus.Pending));
    }

    [TestMethod]
    public void AReconciliationRunEndsInOneOfThreeStates()
    {
        ReconciliationRunStatusCatalog.EnsureTransition(
            ReconciliationRunStatus.Running, ReconciliationRunStatus.Succeeded);
        ReconciliationRunStatusCatalog.EnsureTransition(
            ReconciliationRunStatus.Running, ReconciliationRunStatus.IssuesFound);
        ReconciliationRunStatusCatalog.EnsureTransition(
            ReconciliationRunStatus.Running, ReconciliationRunStatus.Failed);

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            ReconciliationRunStatusCatalog.EnsureTransition(
                ReconciliationRunStatus.IssuesFound, ReconciliationRunStatus.Succeeded));
    }

    [TestMethod]
    public void EveryTokenRoundTrips()
    {
        Assert.AreEqual(
            AccountingBookStatus.ReconciliationRequired,
            AccountingBookStatusCatalog.ParseToken("RECONCILIATION_REQUIRED"));
        Assert.AreEqual(
            AccountingPeriodStatus.Closing, AccountingPeriodStatusCatalog.ParseToken("CLOSING"));
        Assert.AreEqual(BranchStatus.Restricted, BranchStatusCatalog.ParseToken("RESTRICTED"));
        Assert.AreEqual(GuildEconomyStatus.Disabled, GuildEconomyStatusCatalog.ParseToken("DISABLED"));
        Assert.AreEqual(
            PrudentialPolicyVersionStatus.Retired,
            PrudentialPolicyVersionStatusCatalog.ParseToken("RETIRED"));
        Assert.AreEqual(
            IdempotencyRecordStatus.InProgress,
            IdempotencyRecordStatusCatalog.ParseToken("IN_PROGRESS"));

        Assert.AreEqual(InboxEventStatus.Failed, InboxEventStatusCatalog.ParseToken("FAILED"));
        Assert.AreEqual(
            InterestPostingBatchStatus.Posted, InterestPostingBatchStatusCatalog.ParseToken("POSTED"));
        Assert.AreEqual(
            ReconciliationRunStatus.IssuesFound,
            ReconciliationRunStatusCatalog.ParseToken("ISSUES_FOUND"));

        Assert.ThrowsExactly<InvariantViolationException>(() =>
            AccountingTransactionStatusCatalog.ParseToken("REVERSED"));
    }
}
