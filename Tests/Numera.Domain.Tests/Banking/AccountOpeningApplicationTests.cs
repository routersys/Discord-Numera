using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class AccountOpeningApplicationTests
{
    private static readonly AccountOpeningApplicationId Identifier =
        AccountOpeningApplicationId.FromValue(EntityIdValue.FromBits(1));

    private static readonly BankId Bank = BankId.FromValue(EntityIdValue.FromBits(2));
    private static readonly CustomerAccountId Customer = CustomerAccountId.FromValue(EntityIdValue.FromBits(3));

    private static readonly AccountProductVersionId ProductVersion =
        AccountProductVersionId.FromValue(EntityIdValue.FromBits(4));

    private static readonly BankPolicyVersionId PolicyVersion =
        BankPolicyVersionId.FromValue(EntityIdValue.FromBits(5));

    private static readonly FeeScheduleVersionId FeeSchedule =
        FeeScheduleVersionId.FromValue(EntityIdValue.FromBits(6));

    private static readonly DepositAccountId Account = DepositAccountId.FromValue(EntityIdValue.FromBits(7));
    private static readonly DepositAccountId Funding = DepositAccountId.FromValue(EntityIdValue.FromBits(8));
    private static readonly PaymentOrderId FundingPayment =
        PaymentOrderId.FromValue(EntityIdValue.FromBits(9));

    private static readonly UtcTimestamp Now = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static AccountOpeningApplication Submit(
        long minimumInitialFunding = 0,
        long openingFee = 0,
        long cashCardFee = 0,
        long debitCardFee = 0,
        AutomaticBankCardIssueMode cardMode = AutomaticBankCardIssueMode.None,
        AccountOpeningDecisionMode decisionMode = AccountOpeningDecisionMode.Automatic) =>
        AccountOpeningApplication.Submit(
            Identifier,
            Bank,
            Customer,
            ProductVersion,
            PolicyVersion,
            FeeSchedule,
            MoneyMinor.FromMinor(minimumInitialFunding),
            MoneyMinor.FromMinor(openingFee),
            MoneyMinor.FromMinor(cashCardFee),
            MoneyMinor.FromMinor(debitCardFee),
            cardMode,
            decisionMode,
            Now);

    [TestMethod]
    public void SubmissionStartsAtSubmitted()
    {
        AccountOpeningApplication application = Submit();

        Assert.AreEqual(AccountOpeningApplicationStatus.Submitted, application.Status);
        Assert.IsTrue(application.IsPending);
        Assert.IsNull(application.DepositAccountId);
        Assert.IsNull(application.DecidedAt);
    }

    [TestMethod]
    public void RequiredFundingSumsAllFourComponents()
    {
        AccountOpeningApplication application = Submit(
            minimumInitialFunding: 1000,
            openingFee: 300,
            cashCardFee: 20,
            debitCardFee: 5,
            cardMode: AutomaticBankCardIssueMode.IntegratedCashDebit);

        Assert.AreEqual(1325L, application.RequiredFunding.Value);
    }

    [TestMethod]
    public void RequiredFundingOverflowIsAnInvariantViolation()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            static () => AccountOpeningApplication.CalculateRequiredFunding(
                MoneyMinor.FromMinor(long.MaxValue),
                MoneyMinor.FromMinor(1),
                MoneyMinor.Zero,
                MoneyMinor.Zero));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, violation.Code);
    }

    [TestMethod]
    public void NegativeComponentIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            static () => AccountOpeningApplication.CalculateRequiredFunding(
                MoneyMinor.FromMinor(-1), MoneyMinor.Zero, MoneyMinor.Zero, MoneyMinor.Zero));

        Assert.AreEqual(InvariantViolationCode.AccountOpeningFundingInconsistent, violation.Code);
    }

    [TestMethod]
    public void CardIssueFeeWithoutAutomaticIssueIsRejected()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            static () => Submit(cashCardFee: 100));

        Assert.AreEqual(InvariantViolationCode.AccountOpeningCardFeeInconsistent, violation.Code);
    }

    [TestMethod]
    public void DebitCardFeeRequiresIntegratedIssueMode()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            static () => Submit(debitCardFee: 100, cardMode: AutomaticBankCardIssueMode.CashOnly));

        Assert.AreEqual(InvariantViolationCode.AccountOpeningCardFeeInconsistent, violation.Code);
    }

    [TestMethod]
    public void ZeroFundingReachesCompletionThroughReadyToActivate()
    {
        AccountOpeningApplication application = Submit();

        application.Approve(Now, "111");
        application.MarkReadyToActivate(Account);
        application.Complete(Now);

        Assert.AreEqual(AccountOpeningApplicationStatus.Completed, application.Status);
        Assert.AreEqual(Account, application.DepositAccountId);
        Assert.AreEqual(Now, application.CompletedAt);
        Assert.AreEqual("111", application.DecidedByDiscordUserId);
        Assert.IsFalse(application.IsPending);
    }

    [TestMethod]
    public void NonzeroFundingWaitsBeforeActivation()
    {
        AccountOpeningApplication application = Submit(minimumInitialFunding: 500);

        application.Approve(Now, null);
        application.AwaitFunding(Account, Funding);
        application.AttachFundingPayment(FundingPayment);

        Assert.AreEqual(AccountOpeningApplicationStatus.AwaitingFunding, application.Status);
        Assert.AreEqual(Funding, application.FundingSourceDepositAccountId);
        Assert.AreEqual(FundingPayment, application.FundingPaymentOrderId);

        application.MarkFunded();
        application.Complete(Now);

        Assert.AreEqual(AccountOpeningApplicationStatus.Completed, application.Status);
    }

    [TestMethod]
    public void FundingPaymentIsAttachedOnlyWhileAwaitingFunding()
    {
        AccountOpeningApplication application = Submit(minimumInitialFunding: 500);

        Assert.ThrowsExactly<InvariantViolationException>(
            () => application.AttachFundingPayment(FundingPayment));

        application.Approve(Now, null);
        application.AwaitFunding(Account, Funding);
        application.AttachFundingPayment(FundingPayment);

        Assert.ThrowsExactly<InvariantViolationException>(
            () => application.AttachFundingPayment(FundingPayment));
    }

    [TestMethod]
    public void FundingCannotBeMarkedWithoutAPayment()
    {
        AccountOpeningApplication application = Submit(minimumInitialFunding: 500);

        application.Approve(Now, null);
        application.AwaitFunding(Account, Funding);

        Assert.ThrowsExactly<InvariantViolationException>(application.MarkFunded);
    }

    [TestMethod]
    public void PostedFundingBlocksCancellation()
    {
        AccountOpeningApplication application = Submit(minimumInitialFunding: 500);

        application.Approve(Now, null);
        application.AwaitFunding(Account, Funding);
        application.AttachFundingPayment(FundingPayment);

        Assert.ThrowsExactly<InvariantViolationException>(() => application.Cancel(fundingPosted: true));

        application.Cancel(fundingPosted: false);

        Assert.AreEqual(AccountOpeningApplicationStatus.Cancelled, application.Status);
    }

    [TestMethod]
    public void FundingFailureIsTerminal()
    {
        AccountOpeningApplication application = Submit(minimumInitialFunding: 500);

        application.Approve(Now, null);
        application.AwaitFunding(Account, Funding);
        application.Fail();

        Assert.AreEqual(AccountOpeningApplicationStatus.Failed, application.Status);
        Assert.ThrowsExactly<InvariantViolationException>(() => application.Cancel(fundingPosted: false));
    }

    [TestMethod]
    public void RejectionIsTerminal()
    {
        AccountOpeningApplication application = Submit();

        application.Reject(Now, "111");

        Assert.AreEqual(AccountOpeningApplicationStatus.Rejected, application.Status);
        Assert.ThrowsExactly<InvariantViolationException>(() => application.Approve(Now, "111"));
    }

    [TestMethod]
    public void CompletedApplicationCannotBeCancelled()
    {
        AccountOpeningApplication application = Submit();

        application.Approve(Now, null);
        application.MarkReadyToActivate(Account);
        application.Complete(Now);

        Assert.ThrowsExactly<InvariantViolationException>(() => application.Cancel(fundingPosted: false));
    }

    [TestMethod]
    public void ReadyToActivateCannotSkipBackToApproved()
    {
        AccountOpeningApplication application = Submit();

        application.Approve(Now, null);
        application.MarkReadyToActivate(Account);

        Assert.ThrowsExactly<InvariantViolationException>(() => application.AwaitFunding(Account, Funding));
    }

    [TestMethod]
    public void SubmittedApplicationCannotCompleteDirectly()
    {
        AccountOpeningApplication application = Submit();

        Assert.ThrowsExactly<InvariantViolationException>(() => application.Complete(Now));
    }

    [TestMethod]
    public void CancellationBeforeApprovalIsAllowed()
    {
        AccountOpeningApplication application = Submit();

        application.Cancel(fundingPosted: false);

        Assert.AreEqual(AccountOpeningApplicationStatus.Cancelled, application.Status);
    }

    [TestMethod]
    public void RehydrationRejectsAMismatchedRequiredFunding()
    {
        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            static () => AccountOpeningApplication.Rehydrate(
                Identifier,
                Bank,
                Customer,
                ProductVersion,
                PolicyVersion,
                FeeSchedule,
                depositAccountId: null,
                fundingSourceDepositAccountId: null,
                fundingPaymentOrderId: null,
                MoneyMinor.FromMinor(100),
                MoneyMinor.FromMinor(10),
                MoneyMinor.Zero,
                MoneyMinor.Zero,
                MoneyMinor.FromMinor(999),
                AutomaticBankCardIssueMode.None,
                AccountOpeningDecisionMode.Automatic,
                AccountOpeningApplicationStatus.Submitted,
                Now,
                decidedAt: null,
                decidedByDiscordUserId: null,
                completedAt: null,
                VersionedEntity.InitialVersion));

        Assert.AreEqual(InvariantViolationCode.AccountOpeningFundingInconsistent, violation.Code);
    }

    [TestMethod]
    public void EveryStatusTokenRoundTrips()
    {
        foreach (AccountOpeningApplicationStatus status in Enum.GetValues<AccountOpeningApplicationStatus>())
        {
            Assert.AreEqual(
                status,
                AccountOpeningApplicationCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (AccountOpeningDecisionMode mode in Enum.GetValues<AccountOpeningDecisionMode>())
        {
            Assert.AreEqual(mode, AccountOpeningApplicationCatalog.ParseDecisionModeToken(mode.ToToken()));
        }

        foreach (AutomaticBankCardIssueMode mode in Enum.GetValues<AutomaticBankCardIssueMode>())
        {
            Assert.AreEqual(mode, AccountOpeningApplicationCatalog.ParseCardIssueModeToken(mode.ToToken()));
        }
    }

    [TestMethod]
    public void UnknownStatusTokenIsRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(
            static () => AccountOpeningApplicationCatalog.ParseStatusToken("UNKNOWN"));
}
