using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Banking;

[TestClass]
public sealed class HoldTests
{
    private static readonly HoldId Identifier = HoldId.FromValue(EntityIdValue.FromBits(1));
    private static readonly DepositAccountId Deposit = DepositAccountId.FromValue(EntityIdValue.FromBits(2));
    private static readonly LedgerAccountId Ledger = LedgerAccountId.FromValue(EntityIdValue.FromBits(3));
    private static readonly BusinessOperationId Operation = BusinessOperationId.FromValue(EntityIdValue.FromBits(4));
    private static readonly UtcTimestamp CreatedAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000);
    private static readonly UtcTimestamp ExpiresAt = UtcTimestamp.FromUnixMilliseconds(1_776_000_900_000);

    private static Hold Reserve(long amount = 1_000, UtcTimestamp? expiresAt = null) =>
        Hold.ReserveOnDeposit(
            Identifier,
            Deposit,
            Operation,
            MoneyMinor.FromMinor(amount),
            "TRANSFER",
            CreatedAt,
            expiresAt);

    [TestMethod]
    public void ReservationStartsActiveWithFullRemaining()
    {
        Hold hold = Reserve();

        Assert.AreEqual(HoldStatus.Active, hold.Status);
        Assert.AreEqual(MoneyMinor.FromMinor(1_000), hold.Amount);
        Assert.AreEqual(MoneyMinor.FromMinor(1_000), hold.Remaining);
        Assert.AreEqual(MoneyMinor.Zero, hold.CapturedAmount);
        Assert.IsFalse(hold.IsTerminal);
        Assert.IsNull(hold.TerminalAt);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositiveReservationIsRejected(long amount)
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => Reserve(amount));

        Assert.AreEqual(InvariantViolationCode.HoldAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void ExpiryAtOrBeforeCreationIsRejected()
    {
        Assert.AreEqual(
            InvariantViolationCode.HoldExpiryInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => Reserve(expiresAt: CreatedAt)).Code);
        Assert.AreEqual(
            InvariantViolationCode.HoldExpiryInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(
                () => Reserve(expiresAt: UtcTimestamp.FromUnixMilliseconds(CreatedAt.UnixMilliseconds - 1))).Code);
    }

    [TestMethod]
    public void BlankReasonIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => Hold.ReserveOnDeposit(
            Identifier, Deposit, Operation, MoneyMinor.FromMinor(100), "  ", CreatedAt, null));

    [TestMethod]
    public void FullCaptureMovesToCapturedState()
    {
        Hold hold = Reserve();

        hold.Capture(MoneyMinor.FromMinor(1_000), ExpiresAt);

        Assert.AreEqual(HoldStatus.Captured, hold.Status);
        Assert.AreEqual(MoneyMinor.Zero, hold.Remaining);
        Assert.AreEqual(MoneyMinor.FromMinor(1_000), hold.CapturedAmount);
        Assert.AreEqual(ExpiresAt, hold.TerminalAt);
    }

    [TestMethod]
    public void PartialCaptureKeepsHoldActiveAndOriginalAmount()
    {
        Hold hold = Reserve();

        hold.Capture(MoneyMinor.FromMinor(400), ExpiresAt);

        Assert.AreEqual(HoldStatus.Active, hold.Status);
        Assert.AreEqual(MoneyMinor.FromMinor(1_000), hold.Amount);
        Assert.AreEqual(MoneyMinor.FromMinor(600), hold.Remaining);
        Assert.IsNull(hold.TerminalAt);
    }

    [TestMethod]
    public void RemainingDecreasesMonotonicallyUntilTerminal()
    {
        Hold hold = Reserve();

        hold.Capture(MoneyMinor.FromMinor(300), ExpiresAt);
        hold.Capture(MoneyMinor.FromMinor(300), ExpiresAt);
        Assert.AreEqual(MoneyMinor.FromMinor(400), hold.Remaining);

        hold.Capture(MoneyMinor.FromMinor(400), ExpiresAt);
        Assert.AreEqual(HoldStatus.Captured, hold.Status);
        Assert.AreEqual(MoneyMinor.Zero, hold.Remaining);
    }

    [TestMethod]
    public void CaptureBeyondRemainingIsRejected()
    {
        Hold hold = Reserve();
        hold.Capture(MoneyMinor.FromMinor(600), ExpiresAt);

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => hold.Capture(MoneyMinor.FromMinor(401), ExpiresAt));

        Assert.AreEqual(InvariantViolationCode.HoldCaptureAmountInvalid, exception.Code);
        Assert.AreEqual(MoneyMinor.FromMinor(400), hold.Remaining);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositiveCaptureIsRejected(long amount)
    {
        Hold hold = Reserve();

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => hold.Capture(MoneyMinor.FromMinor(amount), ExpiresAt));

        Assert.AreEqual(InvariantViolationCode.HoldCaptureAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void ReleaseZeroesRemainingAndTerminates()
    {
        Hold hold = Reserve();
        hold.Capture(MoneyMinor.FromMinor(250), ExpiresAt);

        hold.Release(ExpiresAt);

        Assert.AreEqual(HoldStatus.Released, hold.Status);
        Assert.AreEqual(MoneyMinor.Zero, hold.Remaining);
        Assert.AreEqual(MoneyMinor.FromMinor(1_000), hold.Amount);
        Assert.AreEqual(ExpiresAt, hold.TerminalAt);
    }

    [TestMethod]
    public void ExpiryRequiresExpiryTimestampAndElapsedTime()
    {
        Assert.AreEqual(
            InvariantViolationCode.HoldNotExpirable,
            Assert.ThrowsExactly<InvariantViolationException>(() => Reserve().Expire(ExpiresAt)).Code);

        Hold expiring = Reserve(expiresAt: ExpiresAt);
        Assert.AreEqual(
            InvariantViolationCode.HoldNotExpirable,
            Assert.ThrowsExactly<InvariantViolationException>(
                () => expiring.Expire(UtcTimestamp.FromUnixMilliseconds(ExpiresAt.UnixMilliseconds - 1))).Code);

        expiring.Expire(ExpiresAt);
        Assert.AreEqual(HoldStatus.Expired, expiring.Status);
        Assert.AreEqual(MoneyMinor.Zero, expiring.Remaining);
    }

    [TestMethod]
    public void TerminalHoldRejectsFurtherTransitions()
    {
        Hold hold = Reserve();
        hold.Release(ExpiresAt);

        Assert.AreEqual(
            InvariantViolationCode.HoldTransitionInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(
                () => hold.Capture(MoneyMinor.FromMinor(1), ExpiresAt)).Code);
        Assert.AreEqual(
            InvariantViolationCode.HoldTransitionInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(() => hold.Release(ExpiresAt)).Code);
    }

    [TestMethod]
    public void LedgerAssetScopeUsesLedgerAccountOnly()
    {
        Hold hold = Hold.ReserveOnLedgerAsset(
            Identifier, Ledger, Operation, MoneyMinor.FromMinor(500), "COLLATERAL", CreatedAt, null);

        Assert.AreEqual(HoldScopeKind.LedgerAsset, hold.ScopeKind);
        Assert.IsNull(hold.DepositAccountId);
        Assert.AreEqual(Ledger, hold.LedgerAccountId);
    }

    [TestMethod]
    public void RehydrationRejectsScopeWithBothReferences()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Hold.Rehydrate(
                Identifier, HoldScopeKind.CustomerDeposit, Deposit, Ledger, Operation,
                MoneyMinor.FromMinor(100), MoneyMinor.FromMinor(100), "TRANSFER",
                HoldStatus.Active, CreatedAt, null, null));

        Assert.AreEqual(InvariantViolationCode.HoldScopeInconsistent, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsRemainingAboveOriginalAmount()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Hold.Rehydrate(
                Identifier, HoldScopeKind.CustomerDeposit, Deposit, null, Operation,
                MoneyMinor.FromMinor(100), MoneyMinor.FromMinor(101), "TRANSFER",
                HoldStatus.Active, CreatedAt, null, null));

        Assert.AreEqual(InvariantViolationCode.HoldAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsActiveHoldWithZeroRemaining()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Hold.Rehydrate(
                Identifier, HoldScopeKind.CustomerDeposit, Deposit, null, Operation,
                MoneyMinor.FromMinor(100), MoneyMinor.Zero, "TRANSFER",
                HoldStatus.Active, CreatedAt, null, null));

        Assert.AreEqual(InvariantViolationCode.HoldRemainingInconsistent, exception.Code);
    }

    [TestMethod]
    public void RehydrationRejectsTerminalHoldWithRemainingAmount()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Hold.Rehydrate(
                Identifier, HoldScopeKind.CustomerDeposit, Deposit, null, Operation,
                MoneyMinor.FromMinor(100), MoneyMinor.FromMinor(100), "TRANSFER",
                HoldStatus.Captured, CreatedAt, null, ExpiresAt));

        Assert.AreEqual(InvariantViolationCode.HoldRemainingInconsistent, exception.Code);
    }

    [TestMethod]
    public void RehydrationRequiresTerminalTimestampForTerminalStatus()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Hold.Rehydrate(
                Identifier, HoldScopeKind.CustomerDeposit, Deposit, null, Operation,
                MoneyMinor.FromMinor(100), MoneyMinor.Zero, "TRANSFER",
                HoldStatus.Captured, CreatedAt, null, null));

        Assert.AreEqual(InvariantViolationCode.HoldRemainingInconsistent, exception.Code);
    }

    [TestMethod]
    public void StatusAndScopeTokensRoundTrip()
    {
        foreach (HoldStatus status in Enum.GetValues<HoldStatus>())
        {
            Assert.AreEqual(status, HoldCatalog.ParseStatusToken(status.ToToken()));
        }

        foreach (HoldScopeKind scopeKind in Enum.GetValues<HoldScopeKind>())
        {
            Assert.AreEqual(scopeKind, HoldCatalog.ParseScopeToken(scopeKind.ToToken()));
        }

        Assert.IsFalse(HoldCatalog.TryParseStatusToken("active", out _));
        Assert.IsFalse(HoldCatalog.TryParseScopeToken("customer_deposit", out _));
    }
}
