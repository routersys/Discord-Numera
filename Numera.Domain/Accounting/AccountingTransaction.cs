namespace Numera.Domain.Accounting;

public readonly record struct JournalEntryDraft(
    JournalEntryId Id,
    LedgerAccountId LedgerAccountId,
    EntrySide Side,
    MoneyMinor Amount);

public sealed class JournalEntry
{
    internal JournalEntry(
        JournalEntryId id,
        AccountingTransactionId accountingTransactionId,
        LedgerAccountId ledgerAccountId,
        EntrySide side,
        MoneyMinor amount,
        int sequence)
    {
        Id = id;
        AccountingTransactionId = accountingTransactionId;
        LedgerAccountId = ledgerAccountId;
        Side = side;
        Amount = amount;
        Sequence = sequence;
    }

    public JournalEntryId Id { get; }

    public AccountingTransactionId AccountingTransactionId { get; }

    public LedgerAccountId LedgerAccountId { get; }

    public EntrySide Side { get; }

    public MoneyMinor Amount { get; }

    public int Sequence { get; }
}

public readonly struct LedgerAccountSet
{
    private readonly IReadOnlyDictionary<LedgerAccountId, LedgerAccount> accounts;

    private LedgerAccountSet(IReadOnlyDictionary<LedgerAccountId, LedgerAccount> accounts) =>
        this.accounts = accounts;

    public static LedgerAccountSet From(IReadOnlyDictionary<LedgerAccountId, LedgerAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return new LedgerAccountSet(accounts);
    }

    public static LedgerAccountSet From(IEnumerable<LedgerAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        Dictionary<LedgerAccountId, LedgerAccount> map = [];
        foreach (LedgerAccount account in accounts)
        {
            map[account.Id] = account;
        }

        return new LedgerAccountSet(map);
    }

    public LedgerAccount Resolve(LedgerAccountId id) =>
        accounts is not null && accounts.TryGetValue(id, out LedgerAccount? account)
            ? account
            : throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountUnknown);
}

public sealed class AccountingTransaction
{
    private readonly JournalEntry[] entries;

    private AccountingTransaction(
        AccountingTransactionId id,
        AccountingBookId bookId,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp occurredAt,
        UtcTimestamp postedAt,
        string transactionType,
        string descriptionCode,
        AccountingTransactionId? reversesTransactionId,
        JournalEntry[] entries)
    {
        Id = id;
        BookId = bookId;
        BusinessOperationId = businessOperationId;
        CurrencyId = currencyId;
        BusinessDate = businessDate;
        OccurredAt = occurredAt;
        PostedAt = postedAt;
        TransactionType = transactionType;
        DescriptionCode = descriptionCode;
        ReversesTransactionId = reversesTransactionId;
        this.entries = entries;
    }

    public AccountingTransactionId Id { get; }

    public AccountingBookId BookId { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public CurrencyId CurrencyId { get; }

    public BusinessDate BusinessDate { get; }

    public UtcTimestamp OccurredAt { get; }

    public UtcTimestamp PostedAt { get; }

    public string TransactionType { get; }

    public string DescriptionCode { get; }

    public AccountingTransactionId? ReversesTransactionId { get; }

    public IReadOnlyList<JournalEntry> Entries => entries;

    public MoneyMinor DebitTotal => Total(EntrySide.Debit);

    public MoneyMinor CreditTotal => Total(EntrySide.Credit);

    public static AccountingTransaction Post(
        AccountingTransactionId id,
        AccountingBookId bookId,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp occurredAt,
        UtcTimestamp postedAt,
        string transactionType,
        string descriptionCode,
        IReadOnlyList<JournalEntryDraft> drafts,
        LedgerAccountSet accounts) =>
        Create(
            id,
            bookId,
            businessOperationId,
            currencyId,
            businessDate,
            occurredAt,
            postedAt,
            transactionType,
            descriptionCode,
            reversesTransactionId: null,
            drafts,
            accounts);

    public AccountingTransaction Reverse(
        AccountingTransactionId id,
        BusinessOperationId businessOperationId,
        BusinessDate businessDate,
        UtcTimestamp occurredAt,
        UtcTimestamp postedAt,
        string descriptionCode,
        IReadOnlyList<JournalEntryId> entryIds,
        LedgerAccountSet accounts)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count != entries.Length)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ReversalShapeMismatch);
        }

        JournalEntryDraft[] drafts = new JournalEntryDraft[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            JournalEntry source = entries[index];
            drafts[index] = new JournalEntryDraft(
                entryIds[index],
                source.LedgerAccountId,
                source.Side.Opposite(),
                source.Amount);
        }

        return Create(
            id,
            BookId,
            businessOperationId,
            CurrencyId,
            businessDate,
            occurredAt,
            postedAt,
            TransactionType,
            descriptionCode,
            reversesTransactionId: Id,
            drafts,
            accounts);
    }

    private static AccountingTransaction Create(
        AccountingTransactionId id,
        AccountingBookId bookId,
        BusinessOperationId businessOperationId,
        CurrencyId currencyId,
        BusinessDate businessDate,
        UtcTimestamp occurredAt,
        UtcTimestamp postedAt,
        string transactionType,
        string descriptionCode,
        AccountingTransactionId? reversesTransactionId,
        IReadOnlyList<JournalEntryDraft> drafts,
        LedgerAccountSet accounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionCode);
        ArgumentNullException.ThrowIfNull(drafts);

        if (drafts.Count < 2)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.JournalEntryCountInsufficient);
        }

        Int128 debitTotal = Int128.Zero;
        Int128 creditTotal = Int128.Zero;
        JournalEntry[] entries = new JournalEntry[drafts.Count];
        HashSet<JournalEntryId> seenEntryIds = [];

        for (int index = 0; index < drafts.Count; index++)
        {
            JournalEntryDraft draft = drafts[index];

            if (draft.Amount.Value < 1)
            {
                throw InvariantViolationException.Create(InvariantViolationCode.JournalEntryAmountInvalid);
            }

            if (!seenEntryIds.Add(draft.Id))
            {
                throw InvariantViolationException.Create(InvariantViolationCode.JournalEntrySequenceInvalid);
            }

            LedgerAccount account = accounts.Resolve(draft.LedgerAccountId);

            if (!account.AcceptsPosting)
            {
                throw InvariantViolationException.Create(InvariantViolationCode.PostingNotAllowed);
            }

            if (account.BookId != bookId)
            {
                throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountBookMismatch);
            }

            if (account.CurrencyId != currencyId)
            {
                throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountCurrencyMismatch);
            }

            if (draft.Side == EntrySide.Debit)
            {
                debitTotal = checked(debitTotal + draft.Amount.Intermediate);
            }
            else
            {
                creditTotal = checked(creditTotal + draft.Amount.Intermediate);
            }

            entries[index] = new JournalEntry(
                draft.Id,
                id,
                draft.LedgerAccountId,
                draft.Side,
                draft.Amount,
                index + 1);
        }

        if (debitTotal != creditTotal)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.JournalUnbalanced);
        }

        if (debitTotal == Int128.Zero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.JournalEntryAmountInvalid);
        }

        MoneyMinor.FromIntermediate(debitTotal);

        return new AccountingTransaction(
            id,
            bookId,
            businessOperationId,
            currencyId,
            businessDate,
            occurredAt,
            postedAt,
            transactionType,
            descriptionCode,
            reversesTransactionId,
            entries);
    }

    private MoneyMinor Total(EntrySide side)
    {
        Int128 total = Int128.Zero;
        foreach (JournalEntry entry in entries)
        {
            if (entry.Side == side)
            {
                total = checked(total + entry.Amount.Intermediate);
            }
        }

        return MoneyMinor.FromIntermediate(total);
    }
}
