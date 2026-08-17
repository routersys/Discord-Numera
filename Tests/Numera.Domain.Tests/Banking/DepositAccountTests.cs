using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class DepositAccountTests
{
    private static readonly UtcTimestamp OpenedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp Later = UtcTimestamp.FromUnixMilliseconds(1_776_900_000_000);

    private static DepositAccount Pending() => DepositAccount.OpenPending(
        DepositAccountId.FromValue(EntityIdValue.FromBits(1)),
        BankId.FromValue(EntityIdValue.FromBits(2)),
        BranchId.FromValue(EntityIdValue.FromBits(3)),
        BankCustomerRelationshipId.FromValue(EntityIdValue.FromBits(4)),
        CustomerAccountId.FromValue(EntityIdValue.FromBits(5)),
        CurrencyId.FromValue(EntityIdValue.FromBits(6)),
        AccountProductId.FromValue(EntityIdValue.FromBits(7)),
        AccountProductVersionId.FromValue(EntityIdValue.FromBits(8)),
        LedgerAccountId.FromValue(EntityIdValue.FromBits(9)),
        AccountNumber.Parse("0012345678"),
        publicReceivingEnabled: true,
        OpenedAt);

    private static DepositAccount Active()
    {
        DepositAccount account = Pending();
        account.FinalizeOpening();
        return account;
    }

    [TestMethod]
    public void NewAccountIsPendingAndCannotMoveMoney()
    {
        DepositAccount account = Pending();

        Assert.AreEqual(DepositAccountStatus.Pending, account.Status);
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.Withdrawal));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.OutgoingTransfer));
        Assert.AreEqual(StatusPermission.Allowed, account.Permits(AccountOperation.BalanceInquiry));
    }

    [TestMethod]
    public void ActiveAccountPermitsEveryOperation()
    {
        DepositAccount account = Active();

        foreach (AccountOperation operation in Enum.GetValues<AccountOperation>())
        {
            Assert.AreEqual(StatusPermission.Allowed, account.Permits(operation));
        }
    }

    [TestMethod]
    public void RestrictedAccountDefersToRestrictionRules()
    {
        DepositAccount account = Active();
        account.Restrict();

        Assert.AreEqual(StatusPermission.RestrictionDependent, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(StatusPermission.RestrictionDependent, account.Permits(AccountOperation.Withdrawal));
        Assert.AreEqual(StatusPermission.Allowed, account.Permits(AccountOperation.BalanceInquiry));
    }

    [TestMethod]
    public void FrozenAccountRoutesIncomingFundsToSuspense()
    {
        DepositAccount account = Active();
        account.Freeze();

        Assert.AreEqual(StatusPermission.SuspenseOnly, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.Withdrawal));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.OutgoingTransfer));
    }

    [TestMethod]
    public void DormantAccountReceivesByPolicyOnly()
    {
        DepositAccount account = Active();
        account.MarkDormant(Later);

        Assert.AreEqual(StatusPermission.ReceivePolicyDependent, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.Withdrawal));
        Assert.AreEqual(Later, account.NextDormancyFeeAt);
    }

    [TestMethod]
    public void ClosingAccountAllowsSettlementWithdrawalOnly()
    {
        DepositAccount account = Active();
        account.RequestClosure(ClosureReason.User, Later);

        Assert.AreEqual(StatusPermission.SettlementOnly, account.Permits(AccountOperation.Withdrawal));
        Assert.AreEqual(StatusPermission.SuspenseOnly, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.OutgoingTransfer));
    }

    [TestMethod]
    public void ClosedAccountExposesHistoryOnly()
    {
        DepositAccount account = Active();
        account.RequestClosure(ClosureReason.User, Later);
        account.FinalizeClosure(Later);

        Assert.AreEqual(DepositAccountStatus.ClosedUser, account.Status);
        Assert.AreEqual(StatusPermission.HistoryOnly, account.Permits(AccountOperation.BalanceInquiry));
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.ExternalCredit));
        Assert.AreEqual(Later, account.ClosedAt);
    }

    [TestMethod]
    public void ClosureReasonSelectsTerminalStatus()
    {
        DepositAccount dormancy = Active();
        dormancy.MarkDormant(null);
        dormancy.RequestClosure(ClosureReason.Dormancy, Later);
        dormancy.FinalizeClosure(Later);
        Assert.AreEqual(DepositAccountStatus.ClosedDormancy, dormancy.Status);

        DepositAccount resolution = Active();
        resolution.RequestClosure(ClosureReason.Resolution, Later);
        resolution.FinalizeClosure(Later);
        Assert.AreEqual(DepositAccountStatus.ClosedResolution, resolution.Status);
    }

    [TestMethod]
    public void DormancyClosureRequiresDormantAccount()
    {
        DepositAccount account = Active();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => account.RequestClosure(ClosureReason.Dormancy, Later));

        Assert.AreEqual(InvariantViolationCode.ClosureReasonInconsistent, exception.Code);
    }

    [TestMethod]
    public void FrozenAccountCannotBeClosedByUserRequest()
    {
        DepositAccount account = Active();
        account.Freeze();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => account.RequestClosure(ClosureReason.User, Later));

        Assert.AreEqual(InvariantViolationCode.ClosureReasonInconsistent, exception.Code);
    }

    [TestMethod]
    public void FrozenAccountCanBeClosedByResolution()
    {
        DepositAccount account = Active();
        account.Freeze();

        account.RequestClosure(ClosureReason.Resolution, Later);

        Assert.AreEqual(DepositAccountStatus.Closing, account.Status);
    }

    [TestMethod]
    public void ResolutionClosedAccountCannotReopen()
    {
        DepositAccount account = Active();
        account.RequestClosure(ClosureReason.Resolution, Later);
        account.FinalizeClosure(Later);

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(account.BeginReopening);

        Assert.AreEqual(InvariantViolationCode.DepositAccountTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void UserClosedAccountReopensThroughReopeningState()
    {
        DepositAccount account = Active();
        account.RequestClosure(ClosureReason.User, Later);
        account.FinalizeClosure(Later);

        account.BeginReopening();
        Assert.AreEqual(DepositAccountStatus.Reopening, account.Status);
        Assert.AreEqual(StatusPermission.Denied, account.Permits(AccountOperation.ExternalCredit));

        account.FinalizeReopening(AccountProductVersionId.FromValue(EntityIdValue.FromBits(99)), Later);

        Assert.AreEqual(DepositAccountStatus.Active, account.Status);
        Assert.IsNull(account.ClosedAt);
        Assert.IsNull(account.ClosureReason);
        Assert.AreEqual(AccountProductVersionId.FromValue(EntityIdValue.FromBits(99)), account.CurrentProductVersionId);
    }

    [TestMethod]
    public void UnfreezeMustTargetActiveOrRestricted()
    {
        DepositAccount account = Active();
        account.Freeze();

        Assert.ThrowsExactly<InvariantViolationException>(
            () => account.Unfreeze(DepositAccountStatus.Dormant));

        account.Unfreeze(DepositAccountStatus.Restricted);
        Assert.AreEqual(DepositAccountStatus.Restricted, account.Status);
    }

    [TestMethod]
    public void FinalizeClosureRequiresRecordedReason()
    {
        DepositAccount account = Active();

        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => account.FinalizeClosure(Later));

        Assert.AreEqual(InvariantViolationCode.ClosureReasonInconsistent, exception.Code);
    }

    [TestMethod]
    public void UndeclaredTransitionsAreRejected()
    {
        DepositAccount pending = Pending();
        Assert.ThrowsExactly<InvariantViolationException>(pending.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(pending.Freeze);

        DepositAccount dormant = Active();
        dormant.MarkDormant(null);
        Assert.ThrowsExactly<InvariantViolationException>(dormant.Freeze);
        Assert.ThrowsExactly<InvariantViolationException>(dormant.Restrict);
    }

    [TestMethod]
    public void ActivityTimestampNeverMovesBackwards()
    {
        DepositAccount account = Active();
        account.RecordCustomerActivity(Later);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => account.RecordCustomerActivity(OpenedAt));

        Assert.AreEqual(InvariantViolationCode.TimestampOutOfRange, exception.Code);
    }

    [TestMethod]
    public void ClosingAccountRejectsSettingChanges()
    {
        DepositAccount account = Active();
        account.RequestClosure(ClosureReason.User, Later);

        Assert.ThrowsExactly<InvariantViolationException>(() => account.SetPublicReceiving(false));
        Assert.ThrowsExactly<InvariantViolationException>(() => account.RecordCustomerActivity(Later));
    }

    [TestMethod]
    public void RehydrationRejectsClosedStatusWithoutClosureReason() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => Rehydrate(
            DepositAccountStatus.ClosedUser, closureReason: null, closedAt: Later));

    [TestMethod]
    public void RehydrationRejectsClosureReasonMismatch() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => Rehydrate(
            DepositAccountStatus.ClosedUser, ClosureReason.Dormancy, Later));

    [TestMethod]
    public void RehydrationRejectsOpenAccountCarryingClosureReason() =>
        Assert.ThrowsExactly<InvariantViolationException>(() => Rehydrate(
            DepositAccountStatus.Active, ClosureReason.User, closedAt: null));

    [TestMethod]
    public void RehydrationAcceptsClosingAccountCarryingClosureReason() =>
        Assert.AreEqual(
            DepositAccountStatus.Closing,
            Rehydrate(DepositAccountStatus.Closing, ClosureReason.User, closedAt: null).Status);

    [TestMethod]
    public void StatusAndClosureReasonTokensRoundTrip()
    {
        foreach (DepositAccountStatus status in Enum.GetValues<DepositAccountStatus>())
        {
            Assert.AreEqual(status, DepositAccountCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (ClosureReason reason in Enum.GetValues<ClosureReason>())
        {
            Assert.AreEqual(reason, DepositAccountCatalog.ParseClosureReasonToken(reason.ToToken()));
        }

        Assert.IsFalse(DepositAccountCatalog.TryParseStatusToken("closed", out _));
    }

    private static DepositAccount Rehydrate(
        DepositAccountStatus status,
        ClosureReason? closureReason,
        UtcTimestamp? closedAt) =>
        DepositAccount.Rehydrate(
            DepositAccountId.FromValue(EntityIdValue.FromBits(1)),
            BankId.FromValue(EntityIdValue.FromBits(2)),
            BranchId.FromValue(EntityIdValue.FromBits(3)),
            BankCustomerRelationshipId.FromValue(EntityIdValue.FromBits(4)),
            CustomerAccountId.FromValue(EntityIdValue.FromBits(5)),
            CurrencyId.FromValue(EntityIdValue.FromBits(6)),
            AccountProductId.FromValue(EntityIdValue.FromBits(7)),
            AccountProductVersionId.FromValue(EntityIdValue.FromBits(8)),
            LedgerAccountId.FromValue(EntityIdValue.FromBits(9)),
            AccountNumber.Parse("0012345678"),
            publicReceivingEnabled: true,
            OpenedAt,
            nextDormancyFeeAt: null,
            status,
            OpenedAt,
            closingRequestedAt: null,
            closureReason,
            closedAt,
            VersionedEntity.InitialVersion);
}

[TestClass]
public sealed class BankCustomerRelationshipTests
{
    private static readonly UtcTimestamp OpenedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp ClosedAt = UtcTimestamp.FromUnixMilliseconds(1_776_900_000_000);

    private static BankCustomerRelationship Pending() => BankCustomerRelationship.Open(
        BankCustomerRelationshipId.FromValue(EntityIdValue.FromBits(1)),
        BankId.FromValue(EntityIdValue.FromBits(2)),
        PartyId.FromValue(EntityIdValue.FromBits(3)),
        CustomerNumber.Parse("000123456"),
        OpenedAt);

    [TestMethod]
    public void NewRelationshipIsPendingAndBlocksAccountOpening()
    {
        BankCustomerRelationship relationship = Pending();

        Assert.AreEqual(RelationshipStatus.Pending, relationship.Status);
        Assert.IsFalse(relationship.AllowsNewAccount);
    }

    [TestMethod]
    public void ActivatedRelationshipAllowsAccountOpening()
    {
        BankCustomerRelationship relationship = Pending();
        relationship.Activate();

        Assert.IsTrue(relationship.AllowsNewAccount);
    }

    [TestMethod]
    public void ActiveRelationshipClosesOnlyThroughTermination()
    {
        BankCustomerRelationship relationship = Pending();
        relationship.Activate();

        Assert.ThrowsExactly<InvariantViolationException>(() => relationship.Close(ClosedAt));

        relationship.BeginTermination();
        relationship.Close(ClosedAt);

        Assert.AreEqual(RelationshipStatus.Closed, relationship.Status);
        Assert.AreEqual(ClosedAt, relationship.ClosedAt);
    }

    [TestMethod]
    public void PendingRelationshipCanBeClosedDirectly()
    {
        BankCustomerRelationship relationship = Pending();

        relationship.Close(ClosedAt);

        Assert.AreEqual(RelationshipStatus.Closed, relationship.Status);
    }

    [TestMethod]
    public void ClosedRelationshipIsTerminal()
    {
        BankCustomerRelationship relationship = Pending();
        relationship.Close(ClosedAt);

        Assert.ThrowsExactly<InvariantViolationException>(relationship.Activate);
        Assert.ThrowsExactly<InvariantViolationException>(relationship.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(() => relationship.ClassifyRisk("HIGH"));
    }

    [TestMethod]
    public void RehydrationRequiresClosedTimestampForClosedStatus()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => BankCustomerRelationship.Rehydrate(
                BankCustomerRelationshipId.FromValue(EntityIdValue.FromBits(1)),
                BankId.FromValue(EntityIdValue.FromBits(2)),
                PartyId.FromValue(EntityIdValue.FromBits(3)),
                CustomerNumber.Parse("000123456"),
                RelationshipStatus.Closed,
                OpenedAt,
                closedAt: null,
                riskClassification: null,
                VersionedEntity.InitialVersion));

        Assert.AreEqual(InvariantViolationCode.RelationshipTransitionInvalid, exception.Code);
    }

    [TestMethod]
    public void StatusTokensRoundTrip()
    {
        foreach (RelationshipStatus status in Enum.GetValues<RelationshipStatus>())
        {
            Assert.AreEqual(status, RelationshipStatusCatalog.ParseToken(status.ToToken()));
        }
    }
}

[TestClass]
public sealed class RoutingCodeTests
{
    [TestMethod]
    [DataRow("001")]
    [DataRow("00123456")]
    public void CanonicalBranchCodesAreAccepted(string candidate) =>
        Assert.AreEqual(candidate, BranchCode.Parse(candidate).Value);

    [TestMethod]
    [DataRow("00")]
    [DataRow("001234567")]
    [DataRow("00a")]
    [DataRow("")]
    public void NonCanonicalBranchCodesAreRejected(string candidate) =>
        Assert.IsFalse(BranchCode.TryParse(candidate, out _));

    [TestMethod]
    [DataRow("000123")]
    [DataRow("0001234567890123")]
    public void CanonicalCustomerNumbersAreAccepted(string candidate) =>
        Assert.AreEqual(candidate, CustomerNumber.Parse(candidate).Value);

    [TestMethod]
    [DataRow("00012")]
    [DataRow("00012345678901234")]
    [DataRow("00012a")]
    [DataRow("-000123")]
    public void NonCanonicalCustomerNumbersAreRejected(string candidate) =>
        Assert.IsFalse(CustomerNumber.TryParse(candidate, out _));

    [TestMethod]
    public void LeadingZeroesAreSignificant()
    {
        Assert.AreNotEqual(AccountNumber.Parse("0012345678"), AccountNumber.Parse("12345678"));
        Assert.AreEqual("0012345678", AccountNumber.Parse("0012345678").Value);
    }

    [TestMethod]
    public void RejectionRaisesCanonicalCodes()
    {
        Assert.AreEqual(
            InvariantViolationCode.BranchCodeInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => BranchCode.Parse("x")).Code);
        Assert.AreEqual(
            InvariantViolationCode.CustomerNumberInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => CustomerNumber.Parse("x")).Code);
        Assert.AreEqual(
            InvariantViolationCode.AccountNumberInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => AccountNumber.Parse("x")).Code);
    }
}
