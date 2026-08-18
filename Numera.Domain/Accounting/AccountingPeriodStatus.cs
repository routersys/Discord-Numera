using Numera.Domain.Common;

namespace Numera.Domain.Accounting;

public enum AccountingPeriodStatus
{
    Open = 1,
    Closing = 2,
    Closed = 3,
}

public static class AccountingPeriodStatusCatalog
{
    private static readonly StateTransitionTable<AccountingPeriodStatus> Transitions =
        StateTransitionTable<AccountingPeriodStatus>
            .Create(InvariantViolationCode.AccountingPeriodTransitionInvalid)
            .AllowCreation(AccountingPeriodStatus.Open)
            .Allow(AccountingPeriodStatus.Open, AccountingPeriodStatus.Closing)
            .Allow(AccountingPeriodStatus.Closing, AccountingPeriodStatus.Open, AccountingPeriodStatus.Closed)
            .Build();

    public static void EnsureCreatable(AccountingPeriodStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(AccountingPeriodStatus from, AccountingPeriodStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(AccountingPeriodStatus from, AccountingPeriodStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this AccountingPeriodStatus status) => status switch
    {
        AccountingPeriodStatus.Open => "OPEN",
        AccountingPeriodStatus.Closing => "CLOSING",
        AccountingPeriodStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountingPeriodStatus status)
    {
        switch (token)
        {
            case "OPEN":
                status = AccountingPeriodStatus.Open;
                return true;
            case "CLOSING":
                status = AccountingPeriodStatus.Closing;
                return true;
            case "CLOSED":
                status = AccountingPeriodStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountingPeriodStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountingPeriodStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountingPeriodStatusUnknown);
}
