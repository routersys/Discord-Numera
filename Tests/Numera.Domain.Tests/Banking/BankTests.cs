using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class BankTests
{
    private static readonly BankId Identifier = BankId.FromValue(EntityIdValue.FromBits(1));
    private static readonly EconomyScopeId Scope = EconomyScopeId.FromValue(EntityIdValue.FromBits(2));
    private static readonly PartyId Party = PartyId.FromValue(EntityIdValue.FromBits(3));
    private static readonly AccountingBookId Book = AccountingBookId.FromValue(EntityIdValue.FromBits(4));
    private static readonly ResolutionCaseId Resolution = ResolutionCaseId.FromValue(EntityIdValue.FromBits(5));
    private static readonly BankPolicyVersionId Policy = BankPolicyVersionId.FromValue(EntityIdValue.FromBits(6));
    private static readonly FeeScheduleVersionId Fees = FeeScheduleVersionId.FromValue(EntityIdValue.FromBits(7));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static Bank Establish() => Bank.Establish(
        Identifier, Scope, Party, InstitutionCode.Parse("NUM0001"), BankName.Parse("ヌメラ銀行"), Book, CreatedAt);

    private static Bank Operating()
    {
        Bank bank = Establish();
        bank.Activate(Policy, Fees);
        return bank;
    }

    [TestMethod]
    public void EstablishedBankStartsPendingActivation()
    {
        Bank bank = Establish();

        Assert.AreEqual(BankStatus.PendingActivation, bank.Status);
        Assert.AreEqual(BankKind.Normal, bank.Kind);
        Assert.IsNull(bank.ResolutionCaseId);
        Assert.IsNull(bank.CurrentPolicyVersionId);
        Assert.IsFalse(bank.AcceptsAccountOpening);
        Assert.IsFalse(bank.AcceptsInternalTransfer);
    }

    [TestMethod]
    public void ActivationPublishesPolicyAndFeeSchedule()
    {
        Bank bank = Operating();

        Assert.AreEqual(BankStatus.Operating, bank.Status);
        Assert.AreEqual(Policy, bank.CurrentPolicyVersionId);
        Assert.AreEqual(Fees, bank.CurrentFeeScheduleVersionId);
        Assert.IsTrue(bank.AcceptsAccountOpening);
        Assert.IsTrue(bank.AcceptsInterbankSettlement);
    }

    [TestMethod]
    public void SettlementSuspensionStopsInterbankButKeepsInternalTransfer()
    {
        Bank bank = Operating();

        bank.SuspendSettlement();

        Assert.IsFalse(bank.AcceptsInterbankSettlement);
        Assert.IsTrue(bank.AcceptsInternalTransfer);
        Assert.IsFalse(bank.AcceptsAccountOpening);
    }

    [TestMethod]
    public void SuspendedSettlementReturnsOnlyThroughRestricted()
    {
        Bank bank = Operating();
        bank.SuspendSettlement();

        Assert.ThrowsExactly<InvariantViolationException>(bank.Resume);

        bank.Restrict();
        bank.Resume();

        Assert.AreEqual(BankStatus.Operating, bank.Status);
    }

    [TestMethod]
    public void OperatingBankCannotCloseWithoutRestrictionOrResolution()
    {
        Bank bank = Operating();

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(bank.BeginClosing);

        Assert.AreEqual(InvariantViolationCode.BankTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void ResolutionLeadsToClosingThenClosed()
    {
        Bank bank = Operating();

        bank.EnterResolution();
        bank.BeginClosing();
        bank.CompleteClosing();

        Assert.AreEqual(BankStatus.Closed, bank.Status);
    }

    [TestMethod]
    public void ClosedBankIsTerminalAndNotConfigurable()
    {
        Bank bank = Operating();
        bank.Restrict();
        bank.BeginClosing();
        bank.CompleteClosing();

        Assert.ThrowsExactly<InvariantViolationException>(bank.Resume);
        Assert.ThrowsExactly<InvariantViolationException>(bank.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(bank.EnterResolution);
        Assert.ThrowsExactly<InvariantViolationException>(() => bank.Rename(BankName.Parse("別名")));
        Assert.ThrowsExactly<InvariantViolationException>(() => bank.ApplyPolicyVersion(Policy));
    }

    [TestMethod]
    public void BridgeBankStartsOperatingWithResolutionCase()
    {
        Bank bridge = Bank.EstablishBridge(
            Identifier, Scope, Party, InstitutionCode.Parse("BRDG01"), BankName.Parse("承継銀行"),
            Book, Resolution, Policy, Fees, CreatedAt);

        Assert.AreEqual(BankStatus.Operating, bridge.Status);
        Assert.AreEqual(BankKind.Bridge, bridge.Kind);
        Assert.AreEqual(Resolution, bridge.ResolutionCaseId);
    }

    [TestMethod]
    public void RehydrationRejectsBridgeWithoutResolutionCase()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Bank.Rehydrate(
                Identifier, Scope, Party, InstitutionCode.Parse("NUM0001"), BankName.Parse("ヌメラ銀行"),
                BankKind.Bridge, null, BankStatus.Operating, Book, Policy, Fees, CreatedAt,
                VersionedEntity.InitialVersion));

        Assert.AreEqual(InvariantViolationCode.BankKindInconsistent, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsNormalBankWithResolutionCase() =>
        Assert.ThrowsExactly<InvariantViolationException>(
            () => Bank.Rehydrate(
                Identifier, Scope, Party, InstitutionCode.Parse("NUM0001"), BankName.Parse("ヌメラ銀行"),
                BankKind.Normal, Resolution, BankStatus.Operating, Book, Policy, Fees, CreatedAt,
                VersionedEntity.InitialVersion));

    [TestMethod]
    public void StatusAndKindTokensRoundTrip()
    {
        foreach (BankStatus status in Enum.GetValues<BankStatus>())
        {
            Assert.AreEqual(status, BankCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (BankKind kind in Enum.GetValues<BankKind>())
        {
            Assert.AreEqual(kind, BankCatalog.ParseKindToken(kind.ToToken()));
        }

        Assert.IsFalse(BankCatalog.TryParseStatusToken("operating", out _));
    }
}

[TestClass]
public sealed class InstitutionCodeTests
{
    [TestMethod]
    [DataRow("ABCD")]
    [DataRow("NUM0001")]
    [DataRow("0123456789ABCDEF")]
    public void CanonicalCodesAreAccepted(string candidate) =>
        Assert.AreEqual(candidate, InstitutionCode.Parse(candidate).Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("ABC")]
    [DataRow("0123456789ABCDEFG")]
    [DataRow("abcd")]
    [DataRow("AB-D")]
    [DataRow("AB D")]
    [DataRow("AB_D")]
    [DataRow("あいうえ")]
    public void NonCanonicalCodesAreRejected(string candidate)
    {
        Assert.IsFalse(InstitutionCode.IsValid(candidate));
        Assert.IsFalse(InstitutionCode.TryParse(candidate, out _));
    }

    [TestMethod]
    public void RejectionRaisesCanonicalCode() =>
        Assert.AreEqual(
            InvariantViolationCode.InstitutionCodeInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => InstitutionCode.Parse("abc")).Code);
}

[TestClass]
public sealed class BankNameTests
{
    [TestMethod]
    public void EightyCodePointsAreAccepted() =>
        Assert.AreEqual(80, BankName.Parse(new string('a', 80)).Value.Length);

    [TestMethod]
    public void EightyOneCodePointsAreRejected() =>
        Assert.IsFalse(BankName.TryParse(new string('a', 81), out _));

    [TestMethod]
    public void BankNameAllowsMoreThanDisplayNameLimit()
    {
        string seventyCharacters = new('a', 70);

        Assert.IsTrue(BankName.TryParse(seventyCharacters, out _));
        Assert.IsFalse(DisplayName.TryParse(seventyCharacters, out _));
    }

    [TestMethod]
    public void RejectionRaisesCanonicalCode() =>
        Assert.AreEqual(
            InvariantViolationCode.BankNameInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => BankName.Parse(" ")).Code);
}

[TestClass]
public sealed class PartyTests
{
    private static readonly PartyId Identifier = PartyId.FromValue(EntityIdValue.FromBits(1));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);

    private static Party Create() =>
        Party.Create(Identifier, PartyType.Customer, DisplayName.Parse("山田太郎"), CreatedAt);

    [TestMethod]
    public void CreatedPartyIsActive()
    {
        Party party = Create();

        Assert.AreEqual(PartyStatus.Active, party.Status);
        Assert.AreEqual(PartyType.Customer, party.Type);
        Assert.AreEqual(VersionedEntity.InitialVersion, party.Version);
    }

    [TestMethod]
    public void RestrictionIsReversible()
    {
        Party party = Create();

        party.Restrict();
        Assert.AreEqual(PartyStatus.Restricted, party.Status);

        party.ClearRestriction();
        Assert.AreEqual(PartyStatus.Active, party.Status);
    }

    [TestMethod]
    public void ClosedPartyIsTerminal()
    {
        Party party = Create();
        party.Close();

        Assert.ThrowsExactly<InvariantViolationException>(party.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(party.ClearRestriction);
        Assert.ThrowsExactly<InvariantViolationException>(party.Close);
        Assert.ThrowsExactly<InvariantViolationException>(() => party.Rename(DisplayName.Parse("別名")));
    }

    [TestMethod]
    public void TypeAndStatusTokensRoundTrip()
    {
        foreach (PartyType type in Enum.GetValues<PartyType>())
        {
            Assert.AreEqual(type, PartyCatalog.ParseTypeToken(type.ToToken()));
        }

        foreach (PartyStatus status in Enum.GetValues<PartyStatus>())
        {
            Assert.AreEqual(status, PartyCatalog.ParseStatusToken(status.ToToken()));
        }

        Assert.IsFalse(PartyCatalog.TryParseTypeToken("customer", out _));
    }
}
