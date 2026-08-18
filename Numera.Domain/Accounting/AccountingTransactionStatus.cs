using Numera.Domain.Common;

namespace Numera.Domain.Accounting;

public enum AccountingTransactionStatus
{
    Posted = 1,
}

public static class AccountingTransactionStatusCatalog
{
    private static readonly StateTransitionTable<AccountingTransactionStatus> Transitions =
        StateTransitionTable<AccountingTransactionStatus>
            .Create(InvariantViolationCode.AccountingTransactionTransitionInvalid)
            .AllowCreation(AccountingTransactionStatus.Posted)
            .Build();

    public static void EnsureCreatable(AccountingTransactionStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(AccountingTransactionStatus from, AccountingTransactionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(AccountingTransactionStatus from, AccountingTransactionStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this AccountingTransactionStatus status) => status switch
    {
        AccountingTransactionStatus.Posted => "POSTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountingTransactionStatus status)
    {
        switch (token)
        {
            case "POSTED":
                status = AccountingTransactionStatus.Posted;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountingTransactionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountingTransactionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountingTransactionStatusUnknown);
}
