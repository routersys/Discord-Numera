namespace Numera.Domain.Accounting;

public enum LedgerAccountStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public enum LedgerOwnerReferenceType
{
    None = 0,
    Bank = 1,
    DepositAccount = 2,
    LoanContract = 3,
    CentralBankSettlementAccount = 4,
}

public sealed class LedgerAccount
{
    private LedgerAccount(
        LedgerAccountId id,
        AccountingBookId bookId,
        LedgerAccountId? parentAccountId,
        string accountCode,
        LedgerAccountKind kind,
        CurrencyId currencyId,
        bool postingAllowed,
        LedgerAccountStatus status,
        LedgerOwnerReferenceType ownerReferenceType,
        EntityIdValue ownerReferenceId)
    {
        Id = id;
        BookId = bookId;
        ParentAccountId = parentAccountId;
        AccountCode = accountCode;
        Kind = kind;
        CurrencyId = currencyId;
        PostingAllowed = postingAllowed;
        Status = status;
        OwnerReferenceType = ownerReferenceType;
        OwnerReferenceId = ownerReferenceId;
    }

    public LedgerAccountId Id { get; }

    public AccountingBookId BookId { get; }

    public LedgerAccountId? ParentAccountId { get; }

    public string AccountCode { get; }

    public LedgerAccountKind Kind { get; }

    public CurrencyId CurrencyId { get; }

    public bool PostingAllowed { get; }

    public LedgerAccountStatus Status { get; private set; }

    public LedgerOwnerReferenceType OwnerReferenceType { get; }

    public EntityIdValue OwnerReferenceId { get; }

    public AccountingType AccountingType => Kind.ToAccountingType();

    public EntrySide NormalSide => AccountingType.NormalSide();

    public static LedgerAccount CreateControl(
        LedgerAccountId id,
        AccountingBookId bookId,
        LedgerAccountId? parentAccountId,
        string accountCode,
        LedgerAccountKind kind,
        CurrencyId currencyId) =>
        new(
            id,
            bookId,
            parentAccountId,
            accountCode,
            kind,
            currencyId,
            postingAllowed: false,
            LedgerAccountStatus.Active,
            LedgerOwnerReferenceType.None,
            EntityIdValue.Empty);

    public static LedgerAccount CreatePosting(
        LedgerAccountId id,
        AccountingBookId bookId,
        LedgerAccountId parentAccountId,
        string accountCode,
        LedgerAccountKind kind,
        CurrencyId currencyId,
        LedgerOwnerReferenceType ownerReferenceType,
        EntityIdValue ownerReferenceId) =>
        new(
            id,
            bookId,
            parentAccountId,
            accountCode,
            kind,
            currencyId,
            postingAllowed: true,
            LedgerAccountStatus.Active,
            ownerReferenceType,
            ownerReferenceId);

    public static LedgerAccount Rehydrate(
        LedgerAccountId id,
        AccountingBookId bookId,
        LedgerAccountId? parentAccountId,
        string accountCode,
        LedgerAccountKind kind,
        CurrencyId currencyId,
        bool postingAllowed,
        LedgerAccountStatus status,
        LedgerOwnerReferenceType ownerReferenceType,
        EntityIdValue ownerReferenceId) =>
        new(
            id,
            bookId,
            parentAccountId,
            accountCode,
            kind,
            currencyId,
            postingAllowed,
            status,
            ownerReferenceType,
            ownerReferenceId);

    public bool AcceptsPosting => PostingAllowed && Status == LedgerAccountStatus.Active;

    public void Restrict()
    {
        if (Status != LedgerAccountStatus.Active)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountTransitionInvalid);
        }

        Status = LedgerAccountStatus.Restricted;
    }

    public void Reactivate()
    {
        if (Status != LedgerAccountStatus.Restricted)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountTransitionInvalid);
        }

        Status = LedgerAccountStatus.Active;
    }

    public void Close(MoneyMinor postedBalance, MoneyMinor heldAmount)
    {
        if (Status == LedgerAccountStatus.Closed)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountTransitionInvalid);
        }

        if (!postedBalance.IsZero || !heldAmount.IsZero)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountNotEmpty);
        }

        Status = LedgerAccountStatus.Closed;
    }

    public MoneyMinor SignedDelta(EntrySide side, MoneyMinor amount) =>
        side == NormalSide ? amount : amount.Negate();
}

public static class LedgerAccountStatusCatalog
{
    public static string ToToken(this LedgerAccountStatus status) => status switch
    {
        LedgerAccountStatus.Active => "ACTIVE",
        LedgerAccountStatus.Restricted => "RESTRICTED",
        LedgerAccountStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out LedgerAccountStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = LedgerAccountStatus.Active;
                return true;
            case "RESTRICTED":
                status = LedgerAccountStatus.Restricted;
                return true;
            case "CLOSED":
                status = LedgerAccountStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static LedgerAccountStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out LedgerAccountStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.LedgerAccountStatusUnknown);
}
