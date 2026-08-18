using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum DebitCardStatus
{
    Active = 1,
    Locked = 2,
    Closed = 3,
}

public sealed class DebitCard : VersionedEntity
{
    private static readonly StateTransitionTable<DebitCardStatus> Transitions =
        StateTransitionTable<DebitCardStatus>
            .Create(InvariantViolationCode.DebitCardTransitionInvalid)
            .AllowCreation(DebitCardStatus.Active)
            .Allow(DebitCardStatus.Active, DebitCardStatus.Locked, DebitCardStatus.Closed)
            .Allow(DebitCardStatus.Locked, DebitCardStatus.Active, DebitCardStatus.Closed)
            .Build();

    private DebitCard(
        DebitCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        DebitCardStatus status,
        string displayNumber,
        UtcTimestamp issuedAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? closedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankCardId = bankCardId;
        DepositAccountId = depositAccountId;
        Status = status;
        DisplayNumber = displayNumber;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        ClosedAt = closedAt;
    }

    public DebitCardId Id { get; }

    public BankCardId BankCardId { get; }

    public DepositAccountId DepositAccountId { get; }

    public DebitCardStatus Status { get; private set; }

    public string DisplayNumber { get; }

    public UtcTimestamp IssuedAt { get; }

    public UtcTimestamp ExpiresAt { get; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public static DebitCard Issue(
        DebitCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        string displayNumber,
        UtcTimestamp issuedAt,
        UtcTimestamp expiresAt)
    {
        if (string.IsNullOrWhiteSpace(displayNumber))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DebitCardDisplayNumberInvalid);
        }

        if (expiresAt.UnixMilliseconds <= issuedAt.UnixMilliseconds)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DebitCardExpiryInconsistent);
        }

        return new DebitCard(
            id,
            bankCardId,
            depositAccountId,
            DebitCardStatus.Active,
            displayNumber,
            issuedAt,
            expiresAt,
            closedAt: null,
            InitialVersion);
    }

    public static DebitCard Rehydrate(
        DebitCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        DebitCardStatus status,
        string displayNumber,
        UtcTimestamp issuedAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? closedAt,
        long version) =>
        new(id, bankCardId, depositAccountId, status, displayNumber, issuedAt, expiresAt, closedAt, version);

    public void Lock()
    {
        Transitions.EnsureAllowed(Status, DebitCardStatus.Locked);

        Status = DebitCardStatus.Locked;
        AdvanceVersion();
    }

    public void Unlock()
    {
        Transitions.EnsureAllowed(Status, DebitCardStatus.Active);

        Status = DebitCardStatus.Active;
        AdvanceVersion();
    }

    public void Close(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DebitCardStatus.Closed);

        Status = DebitCardStatus.Closed;
        ClosedAt = now;
        AdvanceVersion();
    }
}

public static class DebitCardCatalog
{
    public static string ToToken(this DebitCardStatus status) => status switch
    {
        DebitCardStatus.Active => "ACTIVE",
        DebitCardStatus.Locked => "LOCKED",
        DebitCardStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DebitCardStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = DebitCardStatus.Active;
                return true;
            case "LOCKED":
                status = DebitCardStatus.Locked;
                return true;
            case "CLOSED":
                status = DebitCardStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DebitCardStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DebitCardStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DebitCardStatusUnknown);
}
