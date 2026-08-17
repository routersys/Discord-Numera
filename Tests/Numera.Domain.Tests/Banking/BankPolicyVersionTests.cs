using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class BankPolicyVersionTests
{
    private static readonly BankPolicyVersionId PolicyId =
        BankPolicyVersionId.FromValue(EntityIdValue.FromBits(1));

    private static readonly BankId BankIdentifier = BankId.FromValue(EntityIdValue.FromBits(2));

    private static readonly PrudentialPolicyVersionId PrudentialId =
        PrudentialPolicyVersionId.FromValue(EntityIdValue.FromBits(3));

    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(4));
    private static readonly PartyId Party = PartyId.FromValue(EntityIdValue.FromBits(5));
    private static readonly AccountingBookId Book = AccountingBookId.FromValue(EntityIdValue.FromBits(6));

    private static readonly FeeScheduleVersionId FeeSchedule =
        FeeScheduleVersionId.FromValue(EntityIdValue.FromBits(7));

    private static readonly UtcTimestamp Now = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static BankPolicyVersion Policy(
        bool cashCardEnabled = false,
        bool debitCardEnabled = false,
        AutomaticBankCardIssueMode cardMode = AutomaticBankCardIssueMode.None,
        int minimumAgeDays = 0,
        long minimumInitialFunding = 0,
        bool requiresManualApproval = false) =>
        BankPolicyVersion.Create(
            PolicyId,
            BankIdentifier,
            openingEnabled: true,
            minimumAgeDays,
            MoneyMinor.FromMinor(minimumInitialFunding),
            requiresManualApproval,
            reopenClosedAccountAllowed: false,
            publicReceivingEnabledDefault: true,
            cashCardEnabled,
            debitCardEnabled,
            integratedCashDebitDefault: false,
            cardMode,
            cashAtmEnabled: false,
            cashCardValidityMonths: null,
            debitCardValidityMonths: 12,
            perTransferLimit: null,
            dailyOutgoingLimit: null,
            maximumActiveHolds: null,
            Now,
            effectiveTo: null,
            VersionedEntity.InitialVersion);

    private static Bank Established() => Bank.Establish(
        BankIdentifier,
        Scope,
        Party,
        InstitutionCode.Parse("NUM0001"),
        BankName.Parse("ヌメラ銀行"),
        Book,
        Now);

    [TestMethod]
    public void ManualApprovalMapsToTheManualDecisionMode()
    {
        Assert.AreEqual(AccountOpeningDecisionMode.Manual, Policy(requiresManualApproval: true).DecisionMode);
        Assert.AreEqual(AccountOpeningDecisionMode.Automatic, Policy().DecisionMode);
    }

    [TestMethod]
    public void AutomaticCardIssueRequiresTheMatchingCapability()
    {
        Assert.ThrowsExactly<InvariantViolationException>(
            static () => Policy(cardMode: AutomaticBankCardIssueMode.CashOnly));

        Assert.ThrowsExactly<InvariantViolationException>(
            static () => Policy(cashCardEnabled: true, cardMode: AutomaticBankCardIssueMode.IntegratedCashDebit));
    }

    [TestMethod]
    public void IntegratedIssueModeIssuesBothCapabilities()
    {
        BankPolicyVersion policy = Policy(
            cashCardEnabled: true,
            debitCardEnabled: true,
            cardMode: AutomaticBankCardIssueMode.IntegratedCashDebit);

        Assert.IsTrue(policy.IssuesCashCard);
        Assert.IsTrue(policy.IssuesDebitCard);
    }

    [TestMethod]
    public void NoneIssueModeIssuesNothing()
    {
        BankPolicyVersion policy = Policy();

        Assert.IsFalse(policy.IssuesCashCard);
        Assert.IsFalse(policy.IssuesDebitCard);
    }

    [TestMethod]
    public void NegativeMinimumAgeIsRejected() =>
        Assert.ThrowsExactly<InvariantViolationException>(static () => Policy(minimumAgeDays: -1));

    [TestMethod]
    public void PrudentialFloorsAreEnforced()
    {
        Assert.ThrowsExactly<InvariantViolationException>(static () => Prudential(minimumCet1: 449));
        Assert.ThrowsExactly<InvariantViolationException>(static () => Prudential(lendingCet1: 699));
        Assert.ThrowsExactly<InvariantViolationException>(static () => Prudential(leverage: 299));
        Assert.ThrowsExactly<InvariantViolationException>(static () => Prudential(liquidity: 9999));
        Assert.ThrowsExactly<InvariantViolationException>(static () => Prudential(minimumCapital: 0));
    }

    [TestMethod]
    public void PrudentialPolicyCarriesTheInitialCapitalFloor() =>
        Assert.AreEqual(1_000L, Prudential().MinimumInitialBankCapital.Value);

    [TestMethod]
    public void NormalBankNeedsPaidInCapitalToOperate()
    {
        Bank bank = Established();

        InvariantViolationException violation = Assert.ThrowsExactly<InvariantViolationException>(
            () => bank.Activate(PolicyId, FeeSchedule, MoneyMinor.FromMinor(999), MoneyMinor.FromMinor(1_000)));

        Assert.AreEqual(InvariantViolationCode.BankCapitalInsufficient, violation.Code);
        Assert.AreEqual(BankStatus.PendingActivation, bank.Status);
    }

    [TestMethod]
    public void NormalBankRejectsAnUnsetCapitalFloor()
    {
        Bank bank = Established();

        Assert.ThrowsExactly<InvariantViolationException>(
            () => bank.Activate(PolicyId, FeeSchedule, MoneyMinor.FromMinor(1_000), MoneyMinor.Zero));
    }

    [TestMethod]
    public void SufficientPaidInCapitalActivatesTheBank()
    {
        Bank bank = Established();

        bank.Activate(PolicyId, FeeSchedule, MoneyMinor.FromMinor(1_000), MoneyMinor.FromMinor(1_000));

        Assert.AreEqual(BankStatus.Operating, bank.Status);
        Assert.AreEqual(PolicyId, bank.CurrentPolicyVersionId);
        Assert.AreEqual(FeeSchedule, bank.CurrentFeeScheduleVersionId);
    }

    private static PrudentialPolicyVersion Prudential(
        int minimumCet1 = 450,
        int lendingCet1 = 700,
        int leverage = 300,
        int liquidity = 10000,
        long minimumCapital = 1_000) =>
        PrudentialPolicyVersion.Create(
            PrudentialId,
            Scope,
            minimumCet1,
            lendingCet1,
            leverage,
            leverage,
            liquidity,
            MoneyMinor.FromMinor(minimumCapital),
            VersionedEntity.InitialVersion);
}
