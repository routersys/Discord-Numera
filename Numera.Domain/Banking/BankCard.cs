using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum BankCardForm
{
    CashOnly = 1,
    DebitOnly = 2,
    IntegratedCashDebit = 3,
}

public enum BankCardStatus
{
    Active = 1,
    Locked = 2,
    Replaced = 3,
    Expired = 4,
    Closed = 5,
}

public sealed class BankCard : VersionedEntity
{
    private static readonly StateTransitionTable<BankCardStatus> Transitions =
        StateTransitionTable<BankCardStatus>
            .Create(InvariantViolationCode.BankCardTransitionInvalid)
            .AllowCreation(BankCardStatus.Active)
            .Allow(
                BankCardStatus.Active,
                BankCardStatus.Locked,
                BankCardStatus.Replaced,
                BankCardStatus.Expired,
                BankCardStatus.Closed)
            .Allow(
                BankCardStatus.Locked,
                BankCardStatus.Active,
                BankCardStatus.Replaced,
                BankCardStatus.Expired,
                BankCardStatus.Closed)
            .Allow(BankCardStatus.Expired, BankCardStatus.Replaced, BankCardStatus.Closed)
            .Build();

    private BankCard(
        BankCardId id,
        BankId bankId,
        DepositAccountId depositAccountId,
        BankCardForm form,
        BankCardStatus status,
        string displayIdentifier,
        UtcTimestamp issuedAt,
        UtcTimestamp? expiresAt,
        BankCardId? replacedBy,
        UtcTimestamp? closedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        DepositAccountId = depositAccountId;
        Form = form;
        Status = status;
        DisplayIdentifier = displayIdentifier;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        ReplacedBy = replacedBy;
        ClosedAt = closedAt;
    }

    public BankCardId Id { get; }

    public BankId BankId { get; }

    public DepositAccountId DepositAccountId { get; }

    public BankCardForm Form { get; }

    public BankCardStatus Status { get; private set; }

    public string DisplayIdentifier { get; }

    public UtcTimestamp IssuedAt { get; }

    public UtcTimestamp? ExpiresAt { get; }

    public BankCardId? ReplacedBy { get; private set; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public bool HasCashCapability => Form is BankCardForm.CashOnly or BankCardForm.IntegratedCashDebit;

    public bool HasDebitCapability => Form is BankCardForm.DebitOnly or BankCardForm.IntegratedCashDebit;

    public bool IsUsable => Status == BankCardStatus.Active;

    public static BankCard Issue(
        BankCardId id,
        BankId bankId,
        DepositAccountId depositAccountId,
        BankCardForm form,
        string displayIdentifier,
        UtcTimestamp issuedAt,
        UtcTimestamp? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(displayIdentifier))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankCardDisplayIdentifierInvalid);
        }

        if (form != BankCardForm.CashOnly && expiresAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankCardExpiryInconsistent);
        }

        if (expiresAt is { } expiry && expiry.UnixMilliseconds <= issuedAt.UnixMilliseconds)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankCardExpiryInconsistent);
        }

        return new BankCard(
            id,
            bankId,
            depositAccountId,
            form,
            BankCardStatus.Active,
            displayIdentifier,
            issuedAt,
            expiresAt,
            replacedBy: null,
            closedAt: null,
            InitialVersion);
    }

    public static BankCard Rehydrate(
        BankCardId id,
        BankId bankId,
        DepositAccountId depositAccountId,
        BankCardForm form,
        BankCardStatus status,
        string displayIdentifier,
        UtcTimestamp issuedAt,
        UtcTimestamp? expiresAt,
        BankCardId? replacedBy,
        UtcTimestamp? closedAt,
        long version) =>
        new(
            id,
            bankId,
            depositAccountId,
            form,
            status,
            displayIdentifier,
            issuedAt,
            expiresAt,
            replacedBy,
            closedAt,
            version);

    public void Lock()
    {
        Transitions.EnsureAllowed(Status, BankCardStatus.Locked);

        Status = BankCardStatus.Locked;
        AdvanceVersion();
    }

    public void Unlock()
    {
        Transitions.EnsureAllowed(Status, BankCardStatus.Active);

        Status = BankCardStatus.Active;
        AdvanceVersion();
    }

    public void Replace(BankCardId replacement)
    {
        Transitions.EnsureAllowed(Status, BankCardStatus.Replaced);

        Status = BankCardStatus.Replaced;
        ReplacedBy = replacement;
        AdvanceVersion();
    }

    public void Expire()
    {
        Transitions.EnsureAllowed(Status, BankCardStatus.Expired);

        Status = BankCardStatus.Expired;
        AdvanceVersion();
    }

    public void Close(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, BankCardStatus.Closed);

        Status = BankCardStatus.Closed;
        ClosedAt = now;
        AdvanceVersion();
    }
}

public static class BankCardCatalog
{
    public static string ToToken(this BankCardStatus status) => status switch
    {
        BankCardStatus.Active => "ACTIVE",
        BankCardStatus.Locked => "LOCKED",
        BankCardStatus.Replaced => "REPLACED",
        BankCardStatus.Expired => "EXPIRED",
        BankCardStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this BankCardForm form) => form switch
    {
        BankCardForm.CashOnly => "CASH_ONLY",
        BankCardForm.DebitOnly => "DEBIT_ONLY",
        BankCardForm.IntegratedCashDebit => "INTEGRATED_CASH_DEBIT",
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BankCardStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = BankCardStatus.Active;
                return true;
            case "LOCKED":
                status = BankCardStatus.Locked;
                return true;
            case "REPLACED":
                status = BankCardStatus.Replaced;
                return true;
            case "EXPIRED":
                status = BankCardStatus.Expired;
                return true;
            case "CLOSED":
                status = BankCardStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static bool TryParseFormToken(ReadOnlySpan<char> token, out BankCardForm form)
    {
        switch (token)
        {
            case "CASH_ONLY":
                form = BankCardForm.CashOnly;
                return true;
            case "DEBIT_ONLY":
                form = BankCardForm.DebitOnly;
                return true;
            case "INTEGRATED_CASH_DEBIT":
                form = BankCardForm.IntegratedCashDebit;
                return true;
            default:
                form = default;
                return false;
        }
    }

    public static BankCardStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BankCardStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BankCardStatusUnknown);

    public static BankCardForm ParseFormToken(ReadOnlySpan<char> token) =>
        TryParseFormToken(token, out BankCardForm form)
            ? form
            : throw InvariantViolationException.Create(InvariantViolationCode.BankCardFormUnknown);
}
