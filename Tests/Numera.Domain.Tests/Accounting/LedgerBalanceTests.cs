using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Accounting;

[TestClass]
public sealed class LedgerBalanceTests
{
    private static LedgerBalance Balance(long posted, long held) =>
        LedgerBalance.Create(MoneyMinor.FromMinor(posted), MoneyMinor.FromMinor(held));

    [TestMethod]
    public void AvailableBalanceIsPostedMinusHeld() =>
        Assert.AreEqual(MoneyMinor.FromMinor(700), Balance(1_000, 300).AvailableBalance);

    [TestMethod]
    public void NegativeHeldAmountIsRejected()
    {
        InvariantViolationException exception =
            Assert.ThrowsExactly<InvariantViolationException>(() => Balance(1_000, -1));

        Assert.AreEqual(InvariantViolationCode.HeldAmountNegative, exception.Code);
    }

    [TestMethod]
    public void CreditToLiabilityAccountIncreasesPostedBalance()
    {
        LedgerBalance balance = Balance(1_000, 0)
            .ApplyPosting(EntrySide.Credit, EntrySide.Credit, MoneyMinor.FromMinor(500));

        Assert.AreEqual(MoneyMinor.FromMinor(1_500), balance.PostedBalance);
    }

    [TestMethod]
    public void DebitToLiabilityAccountDecreasesPostedBalance()
    {
        LedgerBalance balance = Balance(1_000, 0)
            .ApplyPosting(EntrySide.Debit, EntrySide.Credit, MoneyMinor.FromMinor(500));

        Assert.AreEqual(MoneyMinor.FromMinor(500), balance.PostedBalance);
    }

    [TestMethod]
    public void DebitToAssetAccountIncreasesPostedBalance()
    {
        LedgerBalance balance = Balance(1_000, 0)
            .ApplyPosting(EntrySide.Debit, EntrySide.Debit, MoneyMinor.FromMinor(500));

        Assert.AreEqual(MoneyMinor.FromMinor(1_500), balance.PostedBalance);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositivePostingIsRejected(long amount)
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Balance(1_000, 0).ApplyPosting(EntrySide.Credit, EntrySide.Credit, MoneyMinor.FromMinor(amount)));

        Assert.AreEqual(InvariantViolationCode.JournalEntryAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void PostingDoesNotChangeHeldAmount()
    {
        LedgerBalance balance = Balance(1_000, 300)
            .ApplyPosting(EntrySide.Credit, EntrySide.Credit, MoneyMinor.FromMinor(500));

        Assert.AreEqual(MoneyMinor.FromMinor(300), balance.HeldAmount);
    }

    [TestMethod]
    public void HoldIncreaseAndDecreaseAreSymmetric()
    {
        LedgerBalance reserved = Balance(1_000, 0).IncreaseHold(MoneyMinor.FromMinor(400));
        Assert.AreEqual(MoneyMinor.FromMinor(400), reserved.HeldAmount);
        Assert.AreEqual(MoneyMinor.FromMinor(600), reserved.AvailableBalance);

        LedgerBalance released = reserved.DecreaseHold(MoneyMinor.FromMinor(400));
        Assert.AreEqual(MoneyMinor.Zero, released.HeldAmount);
        Assert.AreEqual(Balance(1_000, 0), released);
    }

    [TestMethod]
    public void HoldDecreaseBelowZeroIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Balance(1_000, 300).DecreaseHold(MoneyMinor.FromMinor(301)));

        Assert.AreEqual(InvariantViolationCode.HeldAmountNegative, exception.Code);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void NonPositiveHoldChangeIsRejected(long amount)
    {
        Assert.AreEqual(
            InvariantViolationCode.HoldAmountInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(
                () => Balance(1_000, 0).IncreaseHold(MoneyMinor.FromMinor(amount))).Code);
        Assert.AreEqual(
            InvariantViolationCode.HoldAmountInvalid,
            Assert.ThrowsExactly<InvariantViolationException>(
                () => Balance(1_000, 500).DecreaseHold(MoneyMinor.FromMinor(amount))).Code);
    }

    [TestMethod]
    public void ReservationIsAllowedExactlyUpToAvailableBalance()
    {
        LedgerBalance balance = Balance(1_000, 300);

        Assert.IsTrue(balance.CanReserve(MoneyMinor.FromMinor(700)));
        Assert.IsFalse(balance.CanReserve(MoneyMinor.FromMinor(701)));
        Assert.IsFalse(balance.CanReserve(MoneyMinor.Zero));
    }

    [TestMethod]
    public void DepositInvariantRejectsNegativePostedBalance()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Balance(-1, 0).EnsureDepositAccountInvariants());

        Assert.AreEqual(InvariantViolationCode.PostedBalanceNegative, exception.Code);
    }

    [TestMethod]
    public void DepositInvariantRejectsNegativeAvailableBalance()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Balance(300, 301).EnsureDepositAccountInvariants());

        Assert.AreEqual(InvariantViolationCode.AvailableBalanceNegative, exception.Code);
    }

    [TestMethod]
    public void DepositInvariantAcceptsFullyReservedBalance() =>
        Assert.AreEqual(MoneyMinor.Zero, Balance(300, 300).EnsureDepositAccountInvariants().AvailableBalance);

    [TestMethod]
    public void PostingOverflowIsRejected()
    {
        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(
            () => Balance(long.MaxValue, 0)
                .ApplyPosting(EntrySide.Credit, EntrySide.Credit, MoneyMinor.FromMinor(1)));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void EqualityComparesBothComponents()
    {
        Assert.AreEqual(Balance(1_000, 300), Balance(1_000, 300));
        Assert.AreNotEqual(Balance(1_000, 300), Balance(1_000, 301));
        Assert.IsTrue(Balance(1_000, 300) == Balance(1_000, 300));
        Assert.IsTrue(Balance(1_000, 300) != Balance(999, 300));
        Assert.AreEqual(LedgerBalance.Empty, Balance(0, 0));
    }
}
