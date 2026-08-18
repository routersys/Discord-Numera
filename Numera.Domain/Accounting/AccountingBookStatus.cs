using Numera.Domain.Common;

namespace Numera.Domain.Accounting;

public enum AccountingBookStatus
{
    Open = 1,
    ReconciliationRequired = 2,
    Closed = 3,
}

public static class AccountingBookStatusCatalog
{
    private static readonly StateTransitionTable<AccountingBookStatus> Transitions =
        StateTransitionTable<AccountingBookStatus>
            .Create(InvariantViolationCode.AccountingBookTransitionInvalid)
            .AllowCreation(AccountingBookStatus.Open)
            .Allow(
                AccountingBookStatus.Open,
                AccountingBookStatus.ReconciliationRequired,
                AccountingBookStatus.Closed)
            .Allow(
                AccountingBookStatus.ReconciliationRequired,
                AccountingBookStatus.Open,
                AccountingBookStatus.Closed)
            .Build();

    public static void EnsureCreatable(AccountingBookStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(AccountingBookStatus from, AccountingBookStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(AccountingBookStatus from, AccountingBookStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this AccountingBookStatus status) => status switch
    {
        AccountingBookStatus.Open => "OPEN",
        AccountingBookStatus.ReconciliationRequired => "RECONCILIATION_REQUIRED",
        AccountingBookStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountingBookStatus status)
    {
        switch (token)
        {
            case "OPEN":
                status = AccountingBookStatus.Open;
                return true;
            case "RECONCILIATION_REQUIRED":
                status = AccountingBookStatus.ReconciliationRequired;
                return true;
            case "CLOSED":
                status = AccountingBookStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountingBookStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountingBookStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountingBookStatusUnknown);
}
