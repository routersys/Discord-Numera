using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Accounting;

[TestClass]
public sealed class LedgerAccountCatalogTests
{
    private static LedgerAccountKind[] AllKinds() => Enum.GetValues<LedgerAccountKind>();

    [TestMethod]
    public void EveryKindHasCanonicalToken()
    {
        foreach (LedgerAccountKind kind in AllKinds())
        {
            string token = kind.ToToken();

            Assert.IsFalse(string.IsNullOrWhiteSpace(token));
            foreach (char character in token)
            {
                Assert.IsTrue(character is (>= 'A' and <= 'Z') or '_', $"{token} は大文字ASCIIではありません。");
            }
        }
    }

    [TestMethod]
    public void TokensAreUniqueAcrossKinds()
    {
        HashSet<string> tokens = [];

        foreach (LedgerAccountKind kind in AllKinds())
        {
            Assert.IsTrue(tokens.Add(kind.ToToken()), $"{kind} のトークンが重複しています。");
        }

        Assert.AreEqual(AllKinds().Length, tokens.Count);
    }

    [TestMethod]
    public void TokenRoundTripsThroughParse()
    {
        foreach (LedgerAccountKind kind in AllKinds())
        {
            Assert.AreEqual(kind, LedgerAccountKindCatalog.ParseToken(kind.ToToken()));
        }
    }

    [TestMethod]
    public void EveryKindResolvesToAccountingType()
    {
        foreach (LedgerAccountKind kind in AllKinds())
        {
            AccountingType type = kind.ToAccountingType();

            Assert.IsTrue(Enum.IsDefined(type));
        }
    }

    [TestMethod]
    public void UndefinedKindIsRejected()
    {
        LedgerAccountKind undefined = (LedgerAccountKind)9_999;

        Assert.ThrowsExactly<InvariantViolationException>(() => undefined.ToToken());
        Assert.ThrowsExactly<InvariantViolationException>(() => undefined.ToAccountingType());
    }

    [TestMethod]
    public void UnknownTokenIsRejected()
    {
        Assert.IsFalse(LedgerAccountKindCatalog.TryParseToken("NOT_A_KIND", out _));
        Assert.IsFalse(LedgerAccountKindCatalog.TryParseToken("cash_asset", out _));
        Assert.ThrowsExactly<InvariantViolationException>(() => LedgerAccountKindCatalog.ParseToken("NOT_A_KIND"));
    }

    [TestMethod]
    public void CreditLossAllowanceIsContraAsset() =>
        Assert.AreEqual(AccountingType.ContraAsset, LedgerAccountKind.CreditLossAllowance.ToAccountingType());

    [TestMethod]
    [DataRow(AccountingType.Asset, EntrySide.Debit)]
    [DataRow(AccountingType.Expense, EntrySide.Debit)]
    [DataRow(AccountingType.Liability, EntrySide.Credit)]
    [DataRow(AccountingType.Equity, EntrySide.Credit)]
    [DataRow(AccountingType.Revenue, EntrySide.Credit)]
    [DataRow(AccountingType.ContraAsset, EntrySide.Credit)]
    public void NormalSideFollowsAccountingElement(AccountingType type, EntrySide expected) =>
        Assert.AreEqual(expected, type.NormalSide());

    [TestMethod]
    public void AccountingTypeTokenRoundTrips()
    {
        foreach (AccountingType type in Enum.GetValues<AccountingType>())
        {
            Assert.AreEqual(type, AccountingTypeCatalog.ParseToken(type.ToToken()));
        }
    }

    [TestMethod]
    public void EntrySideTokenRoundTripsAndInverts()
    {
        Assert.AreEqual("DEBIT", EntrySide.Debit.ToToken());
        Assert.AreEqual("CREDIT", EntrySide.Credit.ToToken());
        Assert.AreEqual(EntrySide.Credit, EntrySide.Debit.Opposite());
        Assert.AreEqual(EntrySide.Debit, EntrySide.Credit.Opposite());
        Assert.AreEqual(EntrySide.Debit, EntrySideCatalog.ParseToken("DEBIT"));
        Assert.IsFalse(EntrySideCatalog.TryParseToken("debit", out _));
    }

    [TestMethod]
    public void SignedDeltaFollowsNormalSide()
    {
        LedgerAccount asset = LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(EntityIdValue.FromBits(1)),
            AccountingBookId.FromValue(EntityIdValue.FromBits(2)),
            LedgerAccountId.FromValue(EntityIdValue.FromBits(3)),
            "1000",
            LedgerAccountKind.CashAsset,
            CurrencyId.FromValue(EntityIdValue.FromBits(4)),
            LedgerOwnerReferenceType.Bank,
            EntityIdValue.FromBits(5));

        Assert.AreEqual(MoneyMinor.FromMinor(100), asset.SignedDelta(EntrySide.Debit, MoneyMinor.FromMinor(100)));
        Assert.AreEqual(MoneyMinor.FromMinor(-100), asset.SignedDelta(EntrySide.Credit, MoneyMinor.FromMinor(100)));
    }

    [TestMethod]
    public void ControlAccountRejectsPosting()
    {
        LedgerAccount control = LedgerAccount.CreateControl(
            LedgerAccountId.FromValue(EntityIdValue.FromBits(1)),
            AccountingBookId.FromValue(EntityIdValue.FromBits(2)),
            null,
            "2000C",
            LedgerAccountKind.DemandDepositControl,
            CurrencyId.FromValue(EntityIdValue.FromBits(4)));

        Assert.IsFalse(control.PostingAllowed);
        Assert.IsFalse(control.AcceptsPosting);
    }

    [TestMethod]
    public void StatusTransitionsFollowCanonicalTable()
    {
        LedgerAccount account = Posting();

        Assert.AreEqual(LedgerAccountStatus.Active, account.Status);
        account.Restrict();
        Assert.AreEqual(LedgerAccountStatus.Restricted, account.Status);
        account.Reactivate();
        Assert.AreEqual(LedgerAccountStatus.Active, account.Status);
        account.Close(MoneyMinor.Zero, MoneyMinor.Zero);
        Assert.AreEqual(LedgerAccountStatus.Closed, account.Status);
    }

    [TestMethod]
    public void UndefinedTransitionsAreRejected()
    {
        LedgerAccount account = Posting();

        Assert.ThrowsExactly<InvariantViolationException>(account.Reactivate);

        account.Restrict();
        Assert.ThrowsExactly<InvariantViolationException>(account.Restrict);

        account.Close(MoneyMinor.Zero, MoneyMinor.Zero);
        Assert.ThrowsExactly<InvariantViolationException>(account.Restrict);
        Assert.ThrowsExactly<InvariantViolationException>(account.Reactivate);
        Assert.ThrowsExactly<InvariantViolationException>(() => account.Close(MoneyMinor.Zero, MoneyMinor.Zero));
    }

    [TestMethod]
    public void AccountWithRemainingBalanceOrHoldCannotClose()
    {
        InvariantViolationException balanceFailure = Assert.ThrowsExactly<InvariantViolationException>(
            () => Posting().Close(MoneyMinor.FromMinor(1), MoneyMinor.Zero));
        InvariantViolationException holdFailure = Assert.ThrowsExactly<InvariantViolationException>(
            () => Posting().Close(MoneyMinor.Zero, MoneyMinor.FromMinor(1)));

        Assert.AreEqual(InvariantViolationCode.LedgerAccountNotEmpty, balanceFailure.Code);
        Assert.AreEqual(InvariantViolationCode.LedgerAccountNotEmpty, holdFailure.Code);
    }

    [TestMethod]
    public void StatusTokenRoundTrips()
    {
        foreach (LedgerAccountStatus status in Enum.GetValues<LedgerAccountStatus>())
        {
            Assert.AreEqual(status, LedgerAccountStatusCatalog.ParseToken(status.ToToken()));
        }
    }

    private static LedgerAccount Posting() => LedgerAccount.CreatePosting(
        LedgerAccountId.FromValue(EntityIdValue.FromBits(1)),
        AccountingBookId.FromValue(EntityIdValue.FromBits(2)),
        LedgerAccountId.FromValue(EntityIdValue.FromBits(3)),
        "2001",
        LedgerAccountKind.DemandDepositControl,
        CurrencyId.FromValue(EntityIdValue.FromBits(4)),
        LedgerOwnerReferenceType.DepositAccount,
        EntityIdValue.FromBits(5));
}
