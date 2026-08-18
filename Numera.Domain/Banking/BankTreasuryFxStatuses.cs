using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum BankTreasuryFxAccountStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public static class BankTreasuryFxAccountStatusCatalog
{
    private static readonly StateTransitionTable<BankTreasuryFxAccountStatus> Transitions =
        StateTransitionTable<BankTreasuryFxAccountStatus>
            .Create(InvariantViolationCode.BankTreasuryFxAccountTransitionInvalid)
            .AllowCreation(BankTreasuryFxAccountStatus.Active)
            .Allow(
                BankTreasuryFxAccountStatus.Active,
                BankTreasuryFxAccountStatus.Restricted,
                BankTreasuryFxAccountStatus.Closed)
            .Allow(
                BankTreasuryFxAccountStatus.Restricted,
                BankTreasuryFxAccountStatus.Active,
                BankTreasuryFxAccountStatus.Closed)
            .Build();

    public static bool IsAllowed(BankTreasuryFxAccountStatus from, BankTreasuryFxAccountStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(BankTreasuryFxAccountStatus from, BankTreasuryFxAccountStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(BankTreasuryFxAccountStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this BankTreasuryFxAccountStatus status) => status switch
    {
        BankTreasuryFxAccountStatus.Active => "ACTIVE",
        BankTreasuryFxAccountStatus.Restricted => "RESTRICTED",
        BankTreasuryFxAccountStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BankTreasuryFxAccountStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = BankTreasuryFxAccountStatus.Active;
                return true;
            case "RESTRICTED":
                status = BankTreasuryFxAccountStatus.Restricted;
                return true;
            case "CLOSED":
                status = BankTreasuryFxAccountStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BankTreasuryFxAccountStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BankTreasuryFxAccountStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BankTreasuryFxAccountStatusUnknown);
}
