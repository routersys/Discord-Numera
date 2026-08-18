using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum CashCardStatus
{
    Active = 1,
    Locked = 2,
    Closed = 3,
}

public sealed class CashCard : VersionedEntity
{
    private static readonly StateTransitionTable<CashCardStatus> Transitions =
        StateTransitionTable<CashCardStatus>
            .Create(InvariantViolationCode.CashCardTransitionInvalid)
            .AllowCreation(CashCardStatus.Active)
            .Allow(CashCardStatus.Active, CashCardStatus.Locked, CashCardStatus.Closed)
            .Allow(CashCardStatus.Locked, CashCardStatus.Active, CashCardStatus.Closed)
            .Build();

    private CashCard(
        CashCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        CashCardStatus status,
        UtcTimestamp issuedAt,
        UtcTimestamp? closedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankCardId = bankCardId;
        DepositAccountId = depositAccountId;
        Status = status;
        IssuedAt = issuedAt;
        ClosedAt = closedAt;
    }

    public CashCardId Id { get; }

    public BankCardId BankCardId { get; }

    public DepositAccountId DepositAccountId { get; }

    public CashCardStatus Status { get; private set; }

    public UtcTimestamp IssuedAt { get; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public static CashCard Issue(
        CashCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        UtcTimestamp issuedAt) =>
        new(id, bankCardId, depositAccountId, CashCardStatus.Active, issuedAt, closedAt: null, InitialVersion);

    public static CashCard Rehydrate(
        CashCardId id,
        BankCardId bankCardId,
        DepositAccountId depositAccountId,
        CashCardStatus status,
        UtcTimestamp issuedAt,
        UtcTimestamp? closedAt,
        long version) =>
        new(id, bankCardId, depositAccountId, status, issuedAt, closedAt, version);

    public void Lock()
    {
        Transitions.EnsureAllowed(Status, CashCardStatus.Locked);

        Status = CashCardStatus.Locked;
        AdvanceVersion();
    }

    public void Unlock()
    {
        Transitions.EnsureAllowed(Status, CashCardStatus.Active);

        Status = CashCardStatus.Active;
        AdvanceVersion();
    }

    public void Close(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, CashCardStatus.Closed);

        Status = CashCardStatus.Closed;
        ClosedAt = now;
        AdvanceVersion();
    }
}

public static class CashCardCatalog
{
    public static string ToToken(this CashCardStatus status) => status switch
    {
        CashCardStatus.Active => "ACTIVE",
        CashCardStatus.Locked => "LOCKED",
        CashCardStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CashCardStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CashCardStatus.Active;
                return true;
            case "LOCKED":
                status = CashCardStatus.Locked;
                return true;
            case "CLOSED":
                status = CashCardStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CashCardStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CashCardStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CashCardStatusUnknown);
}
