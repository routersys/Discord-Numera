using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class FxPricingTests
{
    [TestMethod]
    public void ExactSettlementRequiresTheProductToDivideTheScale()
    {
        Assert.IsTrue(FxPricing.IsExactSettlementCapable(100, 10, 100));
        Assert.IsFalse(FxPricing.IsExactSettlementCapable(100, 3, 1000));
    }

    [TestMethod]
    public void ExactSettlementRejectsNonPositiveParameters()
    {
        Assert.IsFalse(FxPricing.IsExactSettlementCapable(0, 10, 100));
        Assert.IsFalse(FxPricing.IsExactSettlementCapable(100, 0, 100));
        Assert.IsFalse(FxPricing.IsExactSettlementCapable(100, 10, 0));
    }

    [TestMethod]
    public void TheQuoteIsTheExactIntegerQuotient()
    {
        Assert.IsTrue(FxPricing.TryQuoteMinor(1_000, 150, 100, out long quote));
        Assert.AreEqual(1_500L, quote);
    }

    [TestMethod]
    public void ANonZeroRemainderIsRejected()
    {
        Assert.IsFalse(FxPricing.TryQuoteMinor(3, 1, 2, out _));
    }

    [TestMethod]
    public void TheQuoteIsComputedInWideArithmeticWithoutOverflow()
    {
        Assert.IsTrue(FxPricing.TryQuoteMinor(long.MaxValue / 2, 2, 2, out long quote));
        Assert.AreEqual(long.MaxValue / 2, quote);
    }

    [TestMethod]
    public void AQuoteBeyondTheLongRangeIsRejected()
    {
        Assert.IsFalse(FxPricing.TryQuoteMinor(long.MaxValue, 4, 1, out _));
    }

    [TestMethod]
    public void LotAndTickMultiplesAreEnforced()
    {
        Assert.IsTrue(FxPricing.IsLotMultiple(500, 100));
        Assert.IsFalse(FxPricing.IsLotMultiple(550, 100));
        Assert.IsTrue(FxPricing.IsTickMultiple(150, 50));
        Assert.IsFalse(FxPricing.IsTickMultiple(155, 50));
    }
}

[TestClass]
public sealed class FxMarketTests
{
    private static CurrencyId Currency(int seed) =>
        CurrencyId.FromValue(EntityIdValue.FromBits((ulong)seed));

    private static FxMarket Draft() =>
        FxMarket.CreateDraft(
            FxMarketId.FromValue(EntityIdValue.FromBits(1)),
            Currency(10),
            Currency(20),
            PartyId.FromValue(EntityIdValue.FromBits(2)),
            priceScale: 100,
            tickSizePriceUnits: 10,
            lotSizeBaseMinor: 100);

    [TestMethod]
    public void AMarketIsCreatedAsADraft()
    {
        Assert.AreEqual(FxMarketStatus.Draft, Draft().Status);
    }

    [TestMethod]
    public void TheReverseOrientationIsRejected()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxMarket.CreateDraft(
            FxMarketId.FromValue(EntityIdValue.FromBits(1)),
            Currency(20),
            Currency(10),
            PartyId.FromValue(EntityIdValue.FromBits(2)),
            100,
            10,
            100));
    }

    [TestMethod]
    public void ADraftWithoutAPolicyCannotBeSubmitted()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => Draft().SubmitForApproval());
    }

    [TestMethod]
    public void ADraftReachesActiveThroughPendingApproval()
    {
        FxMarket market = Draft();
        market.ApplyPolicyVersion(FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(3)));

        market.SubmitForApproval();
        Assert.AreEqual(FxMarketStatus.PendingApproval, market.Status);

        market.Activate();
        Assert.AreEqual(FxMarketStatus.Active, market.Status);
    }

    [TestMethod]
    public void AnActiveMarketCannotRetireDirectly()
    {
        FxMarket market = Draft();
        market.ApplyPolicyVersion(FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(3)));
        market.SubmitForApproval();
        market.Activate();

        Assert.ThrowsExactly<InvariantViolationException>(market.Retire);
    }

    [TestMethod]
    public void ASuspendedMarketMayRetire()
    {
        FxMarket market = Draft();
        market.ApplyPolicyVersion(FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(3)));
        market.SubmitForApproval();
        market.Activate();
        market.Suspend();
        market.Retire();

        Assert.AreEqual(FxMarketStatus.Retired, market.Status);
    }

    [TestMethod]
    public void AMarketThatCannotSettleExactlyIsNotApprovable()
    {
        FxMarket market = FxMarket.CreateDraft(
            FxMarketId.FromValue(EntityIdValue.FromBits(1)),
            Currency(10),
            Currency(20),
            PartyId.FromValue(EntityIdValue.FromBits(2)),
            priceScale: 1000,
            tickSizePriceUnits: 3,
            lotSizeBaseMinor: 100);

        market.ApplyPolicyVersion(FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(3)));

        Assert.IsFalse(market.IsExactSettlementCapable);
        Assert.ThrowsExactly<InvariantViolationException>(market.SubmitForApproval);
    }

    [TestMethod]
    public void OrderSequencesAreMonotonic()
    {
        FxMarket market = Draft();

        Assert.AreEqual(1L, market.TakeOrderSequence());
        Assert.AreEqual(2L, market.TakeOrderSequence());
        Assert.AreEqual(3L, market.NextOrderSequenceNo);
    }
}

[TestClass]
public sealed class FxOrderTests
{
    private static FxOrder Limit(long baseMinor = 1_000) =>
        FxOrder.Place(
            FxOrderId.FromValue(EntityIdValue.FromBits(1)),
            FxMarketId.FromValue(EntityIdValue.FromBits(2)),
            FxParticipantKind.Customer,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(4)),
            FxOrderSide.BuyBase,
            FxOrderType.Limit,
            FxTimeInForce.GoodTilCancelled,
            priceUnits: 150,
            maximumSlippageBps: null,
            baseMinor,
            sequenceNo: 1,
            FxFundingEndpointId.FromValue(EntityIdValue.FromBits(5)),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(6)),
            HoldId.FromValue(EntityIdValue.FromBits(7)),
            FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(8)),
            UtcTimestamp.FromUnixMilliseconds(1));

    [TestMethod]
    public void ANewOrderIsOpenWithNothingFilled()
    {
        FxOrder order = Limit();

        Assert.AreEqual(FxOrderStatus.Open, order.Status);
        Assert.AreEqual(0L, order.FilledBaseMinor);
        Assert.AreEqual(1_000L, order.RemainingBaseMinor);
    }

    [TestMethod]
    public void APartialFillMovesToPartiallyFilled()
    {
        FxOrder order = Limit();
        order.Fill(400, UtcTimestamp.FromUnixMilliseconds(2));

        Assert.AreEqual(FxOrderStatus.PartiallyFilled, order.Status);
        Assert.AreEqual(600L, order.RemainingBaseMinor);
        Assert.IsFalse(order.IsTerminal);
    }

    [TestMethod]
    public void FillingTheRemainderTerminatesTheOrder()
    {
        FxOrder order = Limit();
        order.Fill(400, UtcTimestamp.FromUnixMilliseconds(2));
        order.Fill(600, UtcTimestamp.FromUnixMilliseconds(3));

        Assert.AreEqual(FxOrderStatus.Filled, order.Status);
        Assert.AreEqual(0L, order.RemainingBaseMinor);
        Assert.IsTrue(order.IsTerminal);
        Assert.IsNotNull(order.TerminalAt);
    }

    [TestMethod]
    public void ASecondPartialFillStaysPartiallyFilled()
    {
        FxOrder order = Limit();
        order.Fill(400, UtcTimestamp.FromUnixMilliseconds(2));
        order.Fill(300, UtcTimestamp.FromUnixMilliseconds(3));

        Assert.AreEqual(FxOrderStatus.PartiallyFilled, order.Status);
        Assert.AreEqual(700L, order.FilledBaseMinor);
        Assert.AreEqual(300L, order.RemainingBaseMinor);
        Assert.IsFalse(order.IsTerminal);
    }

    [TestMethod]
    public void OverfillingIsRejected()
    {
        FxOrder order = Limit();

        Assert.ThrowsExactly<InvariantViolationException>(
            () => order.Fill(1_001, UtcTimestamp.FromUnixMilliseconds(2)));
    }

    [TestMethod]
    public void ATerminalOrderCannotBeCancelledAgain()
    {
        FxOrder order = Limit();
        order.Cancel(UtcTimestamp.FromUnixMilliseconds(2));

        Assert.ThrowsExactly<InvariantViolationException>(
            () => order.Cancel(UtcTimestamp.FromUnixMilliseconds(3)));
    }

    [TestMethod]
    public void APartiallyFilledOrderCannotBeRejected()
    {
        FxOrder order = Limit();
        order.Fill(400, UtcTimestamp.FromUnixMilliseconds(2));

        Assert.ThrowsExactly<InvariantViolationException>(
            () => order.Reject(UtcTimestamp.FromUnixMilliseconds(3)));
    }

    [TestMethod]
    public void ALimitOrderRequiresAPrice()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxOrder.Place(
            FxOrderId.FromValue(EntityIdValue.FromBits(1)),
            FxMarketId.FromValue(EntityIdValue.FromBits(2)),
            FxParticipantKind.Customer,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(4)),
            FxOrderSide.BuyBase,
            FxOrderType.Limit,
            FxTimeInForce.GoodTilCancelled,
            priceUnits: null,
            maximumSlippageBps: null,
            1_000,
            1,
            FxFundingEndpointId.FromValue(EntityIdValue.FromBits(5)),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(6)),
            HoldId.FromValue(EntityIdValue.FromBits(7)),
            FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(8)),
            UtcTimestamp.FromUnixMilliseconds(1)));
    }

    [TestMethod]
    public void ACustomerOrderRequiresACustomerAccount()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxOrder.Place(
            FxOrderId.FromValue(EntityIdValue.FromBits(1)),
            FxMarketId.FromValue(EntityIdValue.FromBits(2)),
            FxParticipantKind.Customer,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            customerAccountId: null,
            FxOrderSide.BuyBase,
            FxOrderType.Limit,
            FxTimeInForce.GoodTilCancelled,
            150,
            null,
            1_000,
            1,
            FxFundingEndpointId.FromValue(EntityIdValue.FromBits(5)),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(6)),
            HoldId.FromValue(EntityIdValue.FromBits(7)),
            FxMarketPolicyVersionId.FromValue(EntityIdValue.FromBits(8)),
            UtcTimestamp.FromUnixMilliseconds(1)));
    }

    [TestMethod]
    public void TheFeeTotalDoesNotDependOnHowTheFillsAreSplit()
    {
        FxOrder single = Limit();
        FxOrder split = Limit();

        long once = single.AccrueFee(asMaker: false, 1_000, 125);
        long first = split.AccrueFee(asMaker: false, 333, 125);
        long second = split.AccrueFee(asMaker: false, 333, 125);
        long third = split.AccrueFee(asMaker: false, 334, 125);

        Assert.AreEqual(12L, once);
        Assert.AreEqual(once, first + second + third);
        Assert.AreEqual(single.TakerFeeChargedMinor, split.TakerFeeChargedMinor);
        Assert.AreEqual(1_000L, split.TakerReceivedGrossMinor);
    }

    [TestMethod]
    public void MakerAndTakerAccumulateSeparately()
    {
        FxOrder order = Limit();

        order.AccrueFee(asMaker: false, 1_000, 200);
        order.AccrueFee(asMaker: true, 4_000, 100);

        Assert.AreEqual(1_000L, order.TakerReceivedGrossMinor);
        Assert.AreEqual(20L, order.TakerFeeChargedMinor);
        Assert.AreEqual(4_000L, order.MakerReceivedGrossMinor);
        Assert.AreEqual(40L, order.MakerFeeChargedMinor);
    }

    [TestMethod]
    public void AZeroRateChargesNothing()
    {
        FxOrder order = Limit();

        Assert.AreEqual(0L, order.AccrueFee(asMaker: true, 9_999, 0));
        Assert.AreEqual(0L, order.MakerFeeChargedMinor);
    }

    [TestMethod]
    public void ANonPositiveReceiptIsRejected()
    {
        FxOrder order = Limit();

        Assert.ThrowsExactly<InvariantViolationException>(() => order.AccrueFee(asMaker: true, 0, 100));
    }

    [TestMethod]
    public void ARateBeyondTheAllowedRangeIsRejected()
    {
        FxOrder order = Limit();

        Assert.ThrowsExactly<InvariantViolationException>(
            () => order.AccrueFee(asMaker: true, 1_000, FxPricing.BasisPointScale));
    }
}

[TestClass]
public sealed class FxSettlementTests
{
    private static FxSettlementLeg Leg(bool external, long fee = 0)
    {
        return FxSettlementLeg.Create(
            FxSettlementLegId.FromValue(EntityIdValue.FromBits(1)),
            FxTradeId.FromValue(EntityIdValue.FromBits(2)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(3)),
            FxSettlementLegKind.Base,
            CurrencyId.FromValue(EntityIdValue.FromBits(4)),
            FxFundingEndpointId.FromValue(EntityIdValue.FromBits(5)),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(6)),
            MoneyMinor.FromMinor(1_000),
            MoneyMinor.FromMinor(fee),
            fee > 0 ? LedgerAccountId.FromValue(EntityIdValue.FromBits(7)) : null,
            external,
            UtcTimestamp.FromUnixMilliseconds(1));
    }

    [TestMethod]
    public void AnInternalLegIsSettledOnCreation()
    {
        Assert.AreEqual(FxSettlementLegStatus.Settled, Leg(external: false).Status);
    }

    [TestMethod]
    public void ALegWithAnExternalComponentStartsClearing()
    {
        Assert.AreEqual(FxSettlementLegStatus.Clearing, Leg(external: true).Status);
    }

    [TestMethod]
    public void TheNetIsGrossMinusTheOperatorFee()
    {
        FxSettlementLeg leg = Leg(external: false, fee: 250);

        Assert.AreEqual(1_000L, leg.Gross.Value);
        Assert.AreEqual(750L, leg.RecipientNet.Value);
        Assert.AreEqual(250L, leg.OperatorFee.Value);
    }

    [TestMethod]
    public void AFeeWithoutATreasuryAccountIsRejected()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxSettlementLeg.Create(
            FxSettlementLegId.FromValue(EntityIdValue.FromBits(1)),
            FxTradeId.FromValue(EntityIdValue.FromBits(2)),
            BusinessOperationId.FromValue(EntityIdValue.FromBits(3)),
            FxSettlementLegKind.Base,
            CurrencyId.FromValue(EntityIdValue.FromBits(4)),
            FxFundingEndpointId.FromValue(EntityIdValue.FromBits(5)),
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(6)),
            MoneyMinor.FromMinor(1_000),
            MoneyMinor.FromMinor(250),
            operatorFeeTreasuryLedgerAccountId: null,
            hasExternalComponent: false,
            UtcTimestamp.FromUnixMilliseconds(1)));
    }

    [TestMethod]
    public void AClearingLegSettles()
    {
        FxSettlementLeg leg = Leg(external: true);
        leg.Settle();

        Assert.AreEqual(FxSettlementLegStatus.Settled, leg.Status);
    }

    [TestMethod]
    public void AnInternalComponentIsFinalOnCreation()
    {
        FxSettlementLegComponent component = FxSettlementLegComponent.Create(
            FxSettlementLegComponentId.FromValue(EntityIdValue.FromBits(1)),
            FxSettlementLegId.FromValue(EntityIdValue.FromBits(2)),
            FxSettlementComponentKind.RecipientNet,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            PartyId.FromValue(EntityIdValue.FromBits(4)),
            sourceBankId: null,
            destinationBankId: null,
            FxSettlementPath.InternalBook,
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(5)),
            destinationLedgerAccountId: null,
            MoneyMinor.FromMinor(750),
            clearingInstructionId: null,
            UtcTimestamp.FromUnixMilliseconds(1));

        Assert.AreEqual(FxSettlementLegComponentStatus.InternalFinal, component.Status);
    }

    [TestMethod]
    public void ABankClearingComponentRequiresAClearingInstruction()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxSettlementLegComponent.Create(
            FxSettlementLegComponentId.FromValue(EntityIdValue.FromBits(1)),
            FxSettlementLegId.FromValue(EntityIdValue.FromBits(2)),
            FxSettlementComponentKind.RecipientNet,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            PartyId.FromValue(EntityIdValue.FromBits(4)),
            BankId.FromValue(EntityIdValue.FromBits(8)),
            BankId.FromValue(EntityIdValue.FromBits(9)),
            FxSettlementPath.BankClearing,
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(5)),
            destinationLedgerAccountId: null,
            MoneyMinor.FromMinor(750),
            clearingInstructionId: null,
            UtcTimestamp.FromUnixMilliseconds(1)));
    }

    [TestMethod]
    public void AnOperatorFeeComponentTargetsALedgerAccount()
    {
        Assert.ThrowsExactly<InvariantViolationException>(() => FxSettlementLegComponent.Create(
            FxSettlementLegComponentId.FromValue(EntityIdValue.FromBits(1)),
            FxSettlementLegId.FromValue(EntityIdValue.FromBits(2)),
            FxSettlementComponentKind.OperatorFee,
            PartyId.FromValue(EntityIdValue.FromBits(3)),
            PartyId.FromValue(EntityIdValue.FromBits(4)),
            sourceBankId: null,
            destinationBankId: null,
            FxSettlementPath.InternalBook,
            FxSettlementEndpointId.FromValue(EntityIdValue.FromBits(5)),
            destinationLedgerAccountId: null,
            MoneyMinor.FromMinor(250),
            clearingInstructionId: null,
            UtcTimestamp.FromUnixMilliseconds(1)));
    }
}
