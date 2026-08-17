using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class PaymentNetworkTests
{
    private static readonly PaymentNetworkId Identifier = PaymentNetworkId.FromValue(EntityIdValue.FromBits(1));
    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));
    private static readonly PartyId Operator = PartyId.FromValue(EntityIdValue.FromBits(3));
    private static readonly AccountingBookId Book = AccountingBookId.FromValue(EntityIdValue.FromBits(4));
    private static readonly LedgerAccountId LiquidAsset = LedgerAccountId.FromValue(EntityIdValue.FromBits(5));

    private static readonly PaymentNetworkPolicyVersionId FirstPolicy =
        PaymentNetworkPolicyVersionId.FromValue(EntityIdValue.FromBits(6));

    private static readonly PaymentNetworkPolicyVersionId SecondPolicy =
        PaymentNetworkPolicyVersionId.FromValue(EntityIdValue.FromBits(7));

    private static PaymentNetwork Draft() =>
        PaymentNetwork.Draft(Identifier, Scope, "ZENGIN", Operator, Book, LiquidAsset);

    [TestMethod]
    public void DraftNetworkDoesNotRoutePayments()
    {
        PaymentNetwork network = Draft();

        Assert.AreEqual(PaymentNetworkStatus.Draft, network.Status);
        Assert.IsFalse(network.RoutesPayments);
    }

    [TestMethod]
    public void FirstPolicyPublishActivatesNetwork()
    {
        PaymentNetwork network = Draft();

        network.PublishPolicy(FirstPolicy);

        Assert.AreEqual(PaymentNetworkStatus.Active, network.Status);
        Assert.AreEqual(FirstPolicy, network.CurrentPolicyVersionId);
        Assert.IsTrue(network.RoutesPayments);
    }

    [TestMethod]
    public void SubsequentPolicyPublishKeepsStatus()
    {
        PaymentNetwork network = Draft();
        network.PublishPolicy(FirstPolicy);

        network.PublishPolicy(SecondPolicy);

        Assert.AreEqual(PaymentNetworkStatus.Active, network.Status);
        Assert.AreEqual(SecondPolicy, network.CurrentPolicyVersionId);
    }

    [TestMethod]
    public void SuspendedNetworkDoesNotRoutePayments()
    {
        PaymentNetwork network = Draft();
        network.PublishPolicy(FirstPolicy);

        network.Suspend();

        Assert.AreEqual(PaymentNetworkStatus.Suspended, network.Status);
        Assert.IsFalse(network.RoutesPayments);
    }

    [TestMethod]
    public void SuspendedNetworkResumes()
    {
        PaymentNetwork network = Draft();
        network.PublishPolicy(FirstPolicy);
        network.Suspend();

        network.Resume();

        Assert.AreEqual(PaymentNetworkStatus.Active, network.Status);
    }

    [TestMethod]
    public void RetiredNetworkRejectsPolicyPublish()
    {
        PaymentNetwork network = Draft();
        network.Retire();

        Assert.ThrowsExactly<InvariantViolationException>(() => network.PublishPolicy(FirstPolicy));
    }

    [TestMethod]
    public void RetiredNetworkIsTerminal()
    {
        PaymentNetwork network = Draft();
        network.Retire();

        Assert.ThrowsExactly<InvariantViolationException>(network.Resume);
    }

    [TestMethod]
    public void DraftNetworkCannotSuspend()
    {
        PaymentNetwork network = Draft();

        Assert.ThrowsExactly<InvariantViolationException>(network.Suspend);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("zengin")]
    [DataRow("ZEN-GIN")]
    [DataRow("ZEN GIN")]
    public void NetworkCodeRejectsNonCanonicalToken(string code) =>
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            PaymentNetwork.Draft(Identifier, Scope, code, Operator, Book, LiquidAsset));

    [TestMethod]
    public void RehydrateRejectsActiveWithoutPolicy() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetwork.Rehydrate(
            Identifier,
            Scope,
            "ZENGIN",
            Operator,
            Book,
            LiquidAsset,
            PaymentNetworkStatus.Active,
            currentPolicyVersionId: null,
            2));

    [TestMethod]
    public void RehydrateRejectsDraftWithPolicy() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetwork.Rehydrate(
            Identifier,
            Scope,
            "ZENGIN",
            Operator,
            Book,
            LiquidAsset,
            PaymentNetworkStatus.Draft,
            FirstPolicy,
            1));
}

[TestClass]
public sealed class PaymentNetworkPolicyVersionTests
{
    private static readonly PaymentNetworkPolicyVersionId Identifier =
        PaymentNetworkPolicyVersionId.FromValue(EntityIdValue.FromBits(1));

    private static readonly PaymentNetworkId Network = PaymentNetworkId.FromValue(EntityIdValue.FromBits(2));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static PaymentNetworkPolicyVersion Clearing(
        MoneyMinor? rtgsThreshold = null,
        BeneficiaryPostingPolicy postingPolicy = BeneficiaryPostingPolicy.AfterFinalSettlement,
        bool precreditEnabled = false,
        int prefundRatioBasisPoints = 10000) =>
        PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Clearing,
            postingPolicy,
            rtgsThreshold,
            3600,
            precreditEnabled,
            prefundRatioBasisPoints,
            MoneyMinor.FromMinor(1_000_000),
            CreatedAt,
            1);

    [TestMethod]
    public void ClearingPolicyWithoutThresholdAlwaysClears() =>
        Assert.AreEqual(SettlementMode.Clearing, Clearing().ResolveSettlementMode(MoneyMinor.FromMinor(999_999)));

    [TestMethod]
    public void AmountAtThresholdRoutesToRealTimeSettlement()
    {
        PaymentNetworkPolicyVersion policy = Clearing(MoneyMinor.FromMinor(100_000));

        Assert.AreEqual(SettlementMode.Rtgs, policy.ResolveSettlementMode(MoneyMinor.FromMinor(100_000)));
    }

    [TestMethod]
    public void AmountBelowThresholdRoutesToClearing()
    {
        PaymentNetworkPolicyVersion policy = Clearing(MoneyMinor.FromMinor(100_000));

        Assert.AreEqual(SettlementMode.Clearing, policy.ResolveSettlementMode(MoneyMinor.FromMinor(99_999)));
    }

    [TestMethod]
    public void RealTimePolicyIgnoresThreshold()
    {
        PaymentNetworkPolicyVersion policy = PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Rtgs,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            MoneyMinor.FromMinor(100_000),
            clearingCycleIntervalSeconds: null,
            precreditEnabled: false,
            10000,
            MoneyMinor.Zero,
            CreatedAt,
            1);

        Assert.AreEqual(SettlementMode.Rtgs, policy.ResolveSettlementMode(MoneyMinor.FromMinor(1)));
    }

    [TestMethod]
    public void RequiredPrefundAtParEqualsAmount() =>
        Assert.AreEqual(
            MoneyMinor.FromMinor(12_345),
            Clearing(postingPolicy: BeneficiaryPostingPolicy.GuaranteedPreCredit, precreditEnabled: true)
                .RequiredPrefund(MoneyMinor.FromMinor(12_345)));

    [TestMethod]
    public void RequiredPrefundRoundsUp()
    {
        PaymentNetworkPolicyVersion policy = Clearing(
            postingPolicy: BeneficiaryPostingPolicy.GuaranteedPreCredit,
            precreditEnabled: true,
            prefundRatioBasisPoints: 12000);

        Assert.AreEqual(MoneyMinor.FromMinor(4), policy.RequiredPrefund(MoneyMinor.FromMinor(3)));
    }

    [TestMethod]
    public void PrefundRatioBelowParIsRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => Clearing(
            postingPolicy: BeneficiaryPostingPolicy.GuaranteedPreCredit,
            precreditEnabled: true,
            prefundRatioBasisPoints: 9999));

    [TestMethod]
    public void GuaranteedPreCreditRequiresPrecreditEnabled() =>
        Assert.ThrowsExactly<InvariantViolationException>(() =>
            Clearing(postingPolicy: BeneficiaryPostingPolicy.GuaranteedPreCredit));

    [TestMethod]
    public void AfterFinalSettlementRejectsPrecreditEnabled() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => Clearing(precreditEnabled: true));

    [TestMethod]
    public void RealTimeModeRejectsGuaranteedPreCredit() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Rtgs,
            BeneficiaryPostingPolicy.GuaranteedPreCredit,
            rtgsThreshold: null,
            clearingCycleIntervalSeconds: null,
            precreditEnabled: true,
            10000,
            MoneyMinor.Zero,
            CreatedAt,
            1));

    [TestMethod]
    public void ClearingModeRequiresCycleInterval() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Clearing,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            rtgsThreshold: null,
            clearingCycleIntervalSeconds: null,
            precreditEnabled: false,
            10000,
            MoneyMinor.Zero,
            CreatedAt,
            1));

    [TestMethod]
    [DataRow(59)]
    [DataRow(86401)]
    public void CycleIntervalOutsideCanonicalRangeIsRejected(int seconds) =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Clearing,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            rtgsThreshold: null,
            seconds,
            precreditEnabled: false,
            10000,
            MoneyMinor.Zero,
            CreatedAt,
            1));

    [TestMethod]
    public void InternalModeIsNotRoutableByNetwork() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => PaymentNetworkPolicyVersion.Create(
            Identifier,
            Network,
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.AfterFinalSettlement,
            rtgsThreshold: null,
            clearingCycleIntervalSeconds: null,
            precreditEnabled: false,
            10000,
            MoneyMinor.Zero,
            CreatedAt,
            1));
}
