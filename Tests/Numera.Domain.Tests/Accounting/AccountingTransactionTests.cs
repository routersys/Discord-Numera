using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Domain.Tests.Accounting;

[TestClass]
public sealed class AccountingTransactionTests
{
    private static readonly AccountingBookId Book = AccountingBookId.FromValue(Id(1));
    private static readonly AccountingBookId OtherBook = AccountingBookId.FromValue(Id(2));
    private static readonly CurrencyId Currency = CurrencyId.FromValue(Id(3));
    private static readonly CurrencyId OtherCurrency = CurrencyId.FromValue(Id(4));
    private static readonly LedgerAccountId CashId = LedgerAccountId.FromValue(Id(10));
    private static readonly LedgerAccountId DepositId = LedgerAccountId.FromValue(Id(11));
    private static readonly LedgerAccountId ControlId = LedgerAccountId.FromValue(Id(12));

    private static EntityIdValue Id(ulong seed) => EntityIdValue.FromBits(seed);

    private static LedgerAccount Cash() => LedgerAccount.CreatePosting(
        CashId, Book, ControlId, "1000", LedgerAccountKind.CashAsset, Currency,
        LedgerOwnerReferenceType.Bank, Id(100));

    private static LedgerAccount Deposit() => LedgerAccount.CreatePosting(
        DepositId, Book, ControlId, "2000", LedgerAccountKind.DemandDepositControl, Currency,
        LedgerOwnerReferenceType.DepositAccount, Id(101));

    private static LedgerAccount Control() => LedgerAccount.CreateControl(
        ControlId, Book, null, "2000C", LedgerAccountKind.DemandDepositControl, Currency);

    private static LedgerAccountSet SetOf(params LedgerAccount[] accounts) => LedgerAccountSet.From(accounts);

    private static AccountingTransaction Post(
        IReadOnlyList<JournalEntryDraft> drafts,
        LedgerAccountSet accounts,
        AccountingBookId? book = null,
        CurrencyId? currency = null) =>
        AccountingTransaction.Post(
            AccountingTransactionId.FromValue(Id(900)),
            book ?? Book,
            BusinessOperationId.FromValue(Id(901)),
            currency ?? Currency,
            BusinessDate.Parse("2026-08-17"),
            UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000),
            UtcTimestamp.FromUnixMilliseconds(1_776_000_000_500),
            "INTERNAL_TRANSFER",
            "TRANSFER_POSTED",
            drafts,
            accounts);

    private static JournalEntryDraft Debit(LedgerAccountId account, long amount, ulong seed) =>
        new(JournalEntryId.FromValue(Id(seed)), account, EntrySide.Debit, MoneyMinor.FromMinor(amount));

    private static JournalEntryDraft Credit(LedgerAccountId account, long amount, ulong seed) =>
        new(JournalEntryId.FromValue(Id(seed)), account, EntrySide.Credit, MoneyMinor.FromMinor(amount));

    private static InvariantViolationException Rejects(Func<AccountingTransaction> action) =>
        Assert.ThrowsExactly<InvariantViolationException>(() => action());

    [TestMethod]
    public void BalancedTransactionPosts()
    {
        AccountingTransaction transaction = Post(
            [Debit(CashId, 5_000, 500), Credit(DepositId, 5_000, 501)],
            SetOf(Cash(), Deposit()));

        Assert.AreEqual(MoneyMinor.FromMinor(5_000), transaction.DebitTotal);
        Assert.AreEqual(MoneyMinor.FromMinor(5_000), transaction.CreditTotal);
        Assert.AreEqual(2, transaction.Entries.Count);
        Assert.IsNull(transaction.ReversesTransactionId);
    }

    [TestMethod]
    public void SequenceIsAssignedContiguouslyFromOne()
    {
        AccountingTransaction transaction = Post(
            [Debit(CashId, 300, 500), Credit(DepositId, 100, 501), Credit(DepositId, 200, 502)],
            SetOf(Cash(), Deposit()));

        CollectionAssert.AreEqual(
            new[] { 1, 2, 3 },
            transaction.Entries.Select(entry => entry.Sequence).ToArray());
    }

    [TestMethod]
    public void UnbalancedTransactionIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 5_000, 500), Credit(DepositId, 4_999, 501)],
            SetOf(Cash(), Deposit())));

        Assert.AreEqual(InvariantViolationCode.JournalUnbalanced, exception.Code);
    }

    [TestMethod]
    public void SingleEntryTransactionIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 5_000, 500)],
            SetOf(Cash())));

        Assert.AreEqual(InvariantViolationCode.JournalEntryCountInsufficient, exception.Code);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(long.MinValue)]
    public void NonPositiveEntryAmountIsRejected(long amount)
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, amount, 500), Credit(DepositId, amount, 501)],
            SetOf(Cash(), Deposit())));

        Assert.AreEqual(InvariantViolationCode.JournalEntryAmountInvalid, exception.Code);
    }

    [TestMethod]
    public void PostingToControlAccountIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(ControlId, 100, 501)],
            SetOf(Cash(), Control())));

        Assert.AreEqual(InvariantViolationCode.PostingNotAllowed, exception.Code);
    }

    [TestMethod]
    public void PostingToRestrictedAccountIsRejected()
    {
        LedgerAccount deposit = Deposit();
        deposit.Restrict();

        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash(), deposit)));

        Assert.AreEqual(InvariantViolationCode.PostingNotAllowed, exception.Code);
    }

    [TestMethod]
    public void PostingToClosedAccountIsRejected()
    {
        LedgerAccount deposit = Deposit();
        deposit.Close(MoneyMinor.Zero, MoneyMinor.Zero);

        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash(), deposit)));

        Assert.AreEqual(InvariantViolationCode.PostingNotAllowed, exception.Code);
    }

    [TestMethod]
    public void AccountFromAnotherBookIsRejected()
    {
        LedgerAccount foreignAccount = LedgerAccount.CreatePosting(
            DepositId, OtherBook, ControlId, "2000", LedgerAccountKind.DemandDepositControl, Currency,
            LedgerOwnerReferenceType.DepositAccount, Id(101));

        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash(), foreignAccount)));

        Assert.AreEqual(InvariantViolationCode.LedgerAccountBookMismatch, exception.Code);
    }

    [TestMethod]
    public void AccountWithAnotherCurrencyIsRejected()
    {
        LedgerAccount foreignCurrencyAccount = LedgerAccount.CreatePosting(
            DepositId, Book, ControlId, "2000", LedgerAccountKind.DemandDepositControl, OtherCurrency,
            LedgerOwnerReferenceType.DepositAccount, Id(101));

        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash(), foreignCurrencyAccount)));

        Assert.AreEqual(InvariantViolationCode.LedgerAccountCurrencyMismatch, exception.Code);
    }

    [TestMethod]
    public void UnknownAccountIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash())));

        Assert.AreEqual(InvariantViolationCode.LedgerAccountUnknown, exception.Code);
    }

    [TestMethod]
    public void DuplicateEntryIdentifierIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 500)],
            SetOf(Cash(), Deposit())));

        Assert.AreEqual(InvariantViolationCode.JournalEntrySequenceInvalid, exception.Code);
    }

    [TestMethod]
    public void TotalExceedingPersistableRangeIsRejected()
    {
        InvariantViolationException exception = Rejects(() => Post(
            [
                Debit(CashId, long.MaxValue, 500),
                Debit(CashId, long.MaxValue, 501),
                Credit(DepositId, long.MaxValue, 502),
                Credit(DepositId, long.MaxValue, 503),
            ],
            SetOf(Cash(), Deposit())));

        Assert.AreEqual(InvariantViolationCode.MoneyOutOfRange, exception.Code);
    }

    [TestMethod]
    public void ReversalMirrorsEverySideAndKeepsAmounts()
    {
        AccountingTransaction original = Post(
            [Debit(CashId, 5_000, 500), Credit(DepositId, 5_000, 501)],
            SetOf(Cash(), Deposit()));

        AccountingTransaction reversal = original.Reverse(
            AccountingTransactionId.FromValue(Id(910)),
            BusinessOperationId.FromValue(Id(911)),
            BusinessDate.Parse("2026-08-18"),
            UtcTimestamp.FromUnixMilliseconds(1_776_100_000_000),
            UtcTimestamp.FromUnixMilliseconds(1_776_100_000_500),
            "TRANSFER_REVERSED",
            [JournalEntryId.FromValue(Id(600)), JournalEntryId.FromValue(Id(601))],
            SetOf(Cash(), Deposit()));

        Assert.AreEqual(original.Id, reversal.ReversesTransactionId);
        Assert.AreEqual(original.DebitTotal, reversal.CreditTotal);
        Assert.AreEqual(original.CreditTotal, reversal.DebitTotal);
        Assert.AreEqual(EntrySide.Credit, reversal.Entries[0].Side);
        Assert.AreEqual(EntrySide.Debit, reversal.Entries[1].Side);
        Assert.AreEqual(original.TransactionType, reversal.TransactionType);
    }

    [TestMethod]
    public void ReversalWithMismatchedEntryIdentifierCountIsRejected()
    {
        AccountingTransaction original = Post(
            [Debit(CashId, 5_000, 500), Credit(DepositId, 5_000, 501)],
            SetOf(Cash(), Deposit()));

        InvariantViolationException exception = Assert.ThrowsExactly<InvariantViolationException>(() =>
            original.Reverse(
                AccountingTransactionId.FromValue(Id(910)),
                BusinessOperationId.FromValue(Id(911)),
                BusinessDate.Parse("2026-08-18"),
                UtcTimestamp.FromUnixMilliseconds(1_776_100_000_000),
                UtcTimestamp.FromUnixMilliseconds(1_776_100_000_500),
                "TRANSFER_REVERSED",
                [JournalEntryId.FromValue(Id(600))],
                SetOf(Cash(), Deposit())));

        Assert.AreEqual(InvariantViolationCode.ReversalShapeMismatch, exception.Code);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void BlankTransactionTypeIsRejected(string transactionType) =>
        Assert.ThrowsExactly<ArgumentException>(() => AccountingTransaction.Post(
            AccountingTransactionId.FromValue(Id(900)),
            Book,
            BusinessOperationId.FromValue(Id(901)),
            Currency,
            BusinessDate.Parse("2026-08-17"),
            UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000),
            UtcTimestamp.FromUnixMilliseconds(1_776_000_000_500),
            transactionType,
            "TRANSFER_POSTED",
            [Debit(CashId, 100, 500), Credit(DepositId, 100, 501)],
            SetOf(Cash(), Deposit())));

    [TestMethod]
    public void MultiLegTransactionBalancesAcrossSides()
    {
        AccountingTransaction transaction = Post(
            [
                Debit(CashId, 1_000, 500),
                Credit(DepositId, 970, 501),
                Credit(DepositId, 30, 502),
            ],
            SetOf(Cash(), Deposit()));

        Assert.AreEqual(transaction.DebitTotal, transaction.CreditTotal);
    }
}
