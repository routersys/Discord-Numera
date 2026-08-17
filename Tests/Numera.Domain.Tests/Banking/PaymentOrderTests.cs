using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class PaymentOrderTests
{
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp Later = UtcTimestamp.FromUnixMilliseconds(1_776_900_000_000);
    private static readonly MoneyMinor Amount = MoneyMinor.FromMinor(100);

    private static PaymentOrder Internal(string? memo = null) => PaymentOrder.Create(
        PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
        BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
        CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
        DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
        DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
        CurrencyId.FromValue(EntityIdValue.FromBits(6)),
        Amount,
        "INTERNAL_TRANSFER",
        SettlementMode.Internal,
        BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
        paymentNetworkPolicyVersionId: null,
        memo,
        CreatedAt);

    private static PaymentOrder Rtgs() => PaymentOrder.Create(
        PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
        BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
        CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
        DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
        DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
        CurrencyId.FromValue(EntityIdValue.FromBits(6)),
        Amount,
        "RTGS_TRANSFER",
        SettlementMode.Rtgs,
        BeneficiaryPostingPolicy.AfterFinalSettlement,
        paymentNetworkPolicyVersionId: null,
        memo: null,
        CreatedAt);

    private static PaymentOrder HeldInternal()
    {
        PaymentOrder order = Internal();
        order.Authorize();
        order.HoldFunds();
        return order;
    }

    [TestMethod]
    public void NewOrderIsCreated()
    {
        PaymentOrder order = Internal();

        Assert.AreEqual(PaymentOrderStatus.Created, order.Status);
        Assert.IsNull(order.BeneficiaryPostedAt);
        Assert.IsNull(order.SettlementFinalizedAt);
        Assert.IsNull(order.CompletedAt);
        Assert.IsFalse(order.RequiresInterbankSettlement);
    }

    [TestMethod]
    public void SameAccountEndpointsAreRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            CreatedAt));

    [TestMethod]
    public void ZeroAmountIsRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            MoneyMinor.Zero,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            CreatedAt));

    [TestMethod]
    public void InternalModeRequiresImmediatePostingPolicy() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            CreatedAt));

    [TestMethod]
    public void InternalModeRejectsNetworkPolicySnapshot() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            PaymentNetworkPolicyVersionId.FromValue(EntityIdValue.FromBits(9)),
            memo: null,
            CreatedAt));

    [TestMethod]
    public void ClearingModeRequiresNetworkPolicySnapshot() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "CLEARING_TRANSFER",
            SettlementMode.Clearing,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            CreatedAt));

    [TestMethod]
    public void GuaranteedPreCreditRequiresClearing() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Create(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "RTGS_TRANSFER",
            SettlementMode.Rtgs,
            BeneficiaryPostingPolicy.GuaranteedPreCredit,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            CreatedAt));

    [TestMethod]
    public void OverlongMemoIsRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(
            () => Internal(new string('あ', PaymentOrder.MaximumMemoLength + 1)));

    [TestMethod]
    public void MemoAtTheLimitIsAccepted() =>
        Assert.AreEqual(
            PaymentOrder.MaximumMemoLength,
            Internal(new string('あ', PaymentOrder.MaximumMemoLength)).Memo!.Length);

    [TestMethod]
    public void CanonicalInternalTransferWalksEveryDeclaredTransition()
    {
        PaymentOrder order = Internal();

        order.Authorize();
        Assert.AreEqual(PaymentOrderStatus.Authorized, order.Status);

        order.HoldFunds();
        Assert.AreEqual(PaymentOrderStatus.FundsHeld, order.Status);

        order.CompleteInternalTransfer(Later);

        Assert.AreEqual(PaymentOrderStatus.Completed, order.Status);
        Assert.AreEqual(Later, order.BeneficiaryPostedAt);
        Assert.AreEqual(Later, order.CompletedAt);
        Assert.IsNull(order.SettlementFinalizedAt);
        Assert.IsTrue(order.IsTerminal);
    }

    [TestMethod]
    public void FundsHeldCannotJumpStraightToCompleted()
    {
        PaymentOrder order = HeldInternal();

        Assert.ThrowsExactly<InvariantViolationException>(() => order.Complete(Later));
    }

    [TestMethod]
    public void InternalTransferRejectsSettlementFinality()
    {
        PaymentOrder order = HeldInternal();

        Assert.ThrowsExactly<InvariantViolationException>(() => order.RecordSettlementFinality(Later));
    }

    [TestMethod]
    public void InterbankSettleRequiresFinalityFirst()
    {
        PaymentOrder order = Rtgs();
        order.Authorize();
        order.HoldFunds();
        order.Accept();
        order.BeginSettling();

        Assert.ThrowsExactly<InvariantViolationException>(order.Settle);

        order.RecordSettlementFinality(Later);
        order.Settle();

        Assert.AreEqual(PaymentOrderStatus.Settled, order.Status);
    }

    [TestMethod]
    public void InterbankCompleteRequiresBothFacts()
    {
        PaymentOrder order = Rtgs();
        order.Authorize();
        order.HoldFunds();
        order.Accept();
        order.BeginSettling();
        order.RecordSettlementFinality(Later);
        order.Settle();

        Assert.ThrowsExactly<InvariantViolationException>(() => order.Complete(Later));

        order.RecordBeneficiaryPosting(Later);
        order.Complete(Later);

        Assert.AreEqual(PaymentOrderStatus.Completed, order.Status);
    }

    [TestMethod]
    public void BeneficiaryPostingIsIdempotent()
    {
        PaymentOrder order = HeldInternal();

        order.RecordBeneficiaryPosting(CreatedAt);
        order.RecordBeneficiaryPosting(Later);

        Assert.AreEqual(CreatedAt, order.BeneficiaryPostedAt);
    }

    [TestMethod]
    public void SettlementFinalityIsIdempotent()
    {
        PaymentOrder order = Rtgs();
        order.Authorize();
        order.HoldFunds();

        order.RecordSettlementFinality(CreatedAt);
        order.RecordSettlementFinality(Later);

        Assert.AreEqual(CreatedAt, order.SettlementFinalizedAt);
    }

    [TestMethod]
    public void PostedBeneficiaryBlocksFailure()
    {
        PaymentOrder order = HeldInternal();
        order.RecordBeneficiaryPosting(Later);

        Assert.ThrowsExactly<InvariantViolationException>(order.Fail);
        Assert.ThrowsExactly<InvariantViolationException>(order.Cancel);
    }

    [TestMethod]
    public void SettlingCannotBeCancelled()
    {
        PaymentOrder order = Rtgs();
        order.Authorize();
        order.HoldFunds();
        order.Accept();
        order.BeginSettling();

        Assert.ThrowsExactly<InvariantViolationException>(order.Cancel);
    }

    [TestMethod]
    public void TerminalStatesAreFinal()
    {
        PaymentOrder failed = Internal();
        failed.Fail();

        Assert.ThrowsExactly<InvariantViolationException>(failed.Authorize);
        Assert.ThrowsExactly<InvariantViolationException>(failed.Cancel);

        PaymentOrder cancelled = Internal();
        cancelled.Cancel();

        Assert.ThrowsExactly<InvariantViolationException>(cancelled.Authorize);
    }

    [TestMethod]
    public void RehydrateRejectsCompletedWithoutBeneficiaryPosting() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Rehydrate(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            PaymentOrderStatus.Completed,
            beneficiaryPostedAt: null,
            settlementFinalizedAt: null,
            CreatedAt,
            Later,
            version: 1));

    [TestMethod]
    public void RehydrateRejectsInternalWithSettlementFinality() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Rehydrate(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            PaymentOrderStatus.FundsHeld,
            beneficiaryPostedAt: null,
            settlementFinalizedAt: Later,
            CreatedAt,
            completedAt: null,
            version: 1));

    [TestMethod]
    public void RehydrateRejectsSettledInterbankWithoutFinality() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Rehydrate(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "RTGS_TRANSFER",
            SettlementMode.Rtgs,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            PaymentOrderStatus.Settled,
            beneficiaryPostedAt: null,
            settlementFinalizedAt: null,
            CreatedAt,
            completedAt: null,
            version: 1));

    [TestMethod]
    public void RehydrateRejectsPostedBeneficiaryOnFailedOrder() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentOrder.Rehydrate(
            PaymentOrderId.FromValue(EntityIdValue.FromBits(1)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(2)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(3)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(4)),
            DepositAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            Amount,
            "INTERNAL_TRANSFER",
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            memo: null,
            PaymentOrderStatus.Failed,
            beneficiaryPostedAt: Later,
            settlementFinalizedAt: null,
            CreatedAt,
            completedAt: null,
            version: 1));

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (PaymentOrderStatus status in Enum.GetValues<PaymentOrderStatus>())
        {
            Assert.AreEqual(status, PaymentOrderCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (SettlementMode mode in Enum.GetValues<SettlementMode>())
        {
            Assert.AreEqual(mode, PaymentOrderCatalog.ParseSettlementModeToken(mode.ToToken()));
        }

        foreach (BeneficiaryPostingPolicy policy in Enum.GetValues<BeneficiaryPostingPolicy>())
        {
            Assert.AreEqual(policy, PaymentOrderCatalog.ParsePostingPolicyToken(policy.ToToken()));
        }
    }

    [TestMethod]
    public void UnknownTokensAreRejected()
    {
        Assert.ThrowsExactly<InvariantViolationException>(
            () => PaymentOrderCatalog.ParseStatusToken("UNKNOWN"));
        Assert.ThrowsExactly<InvariantViolationException>(
            () => PaymentOrderCatalog.ParseSettlementModeToken("UNKNOWN"));
        Assert.ThrowsExactly<InvariantViolationException>(
            () => PaymentOrderCatalog.ParsePostingPolicyToken("UNKNOWN"));
    }
}
