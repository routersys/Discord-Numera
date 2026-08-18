using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum AccountProductStatus
{
    Draft = 1,
    Active = 2,
    Suspended = 3,
    Retired = 4,
}

public static class AccountProductStatusCatalog
{
    private static readonly StateTransitionTable<AccountProductStatus> Transitions =
        StateTransitionTable<AccountProductStatus>
            .Create(InvariantViolationCode.AccountProductTransitionInvalid)
            .AllowCreation(AccountProductStatus.Draft)
            .Allow(AccountProductStatus.Draft, AccountProductStatus.Active, AccountProductStatus.Retired)
            .Allow(AccountProductStatus.Active, AccountProductStatus.Suspended, AccountProductStatus.Retired)
            .Allow(AccountProductStatus.Suspended, AccountProductStatus.Active, AccountProductStatus.Retired)
            .Build();

    public static void EnsureTransition(AccountProductStatus from, AccountProductStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsOfferable(this AccountProductStatus status) =>
        status == AccountProductStatus.Active;

    public static string ToToken(this AccountProductStatus status) => status switch
    {
        AccountProductStatus.Draft => "DRAFT",
        AccountProductStatus.Active => "ACTIVE",
        AccountProductStatus.Suspended => "SUSPENDED",
        AccountProductStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountProductStatus status)
    {
        switch (token)
        {
            case "DRAFT":
                status = AccountProductStatus.Draft;
                return true;
            case "ACTIVE":
                status = AccountProductStatus.Active;
                return true;
            case "SUSPENDED":
                status = AccountProductStatus.Suspended;
                return true;
            case "RETIRED":
                status = AccountProductStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountProductStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountProductStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AccountProductStatusUnknown);
}
