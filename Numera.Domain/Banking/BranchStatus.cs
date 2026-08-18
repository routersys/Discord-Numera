using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum BranchStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public static class BranchStatusCatalog
{
    private static readonly StateTransitionTable<BranchStatus> Transitions =
        StateTransitionTable<BranchStatus>
            .Create(InvariantViolationCode.BranchTransitionInvalid)
            .AllowCreation(BranchStatus.Active)
            .Allow(BranchStatus.Active, BranchStatus.Restricted, BranchStatus.Closed)
            .Allow(BranchStatus.Restricted, BranchStatus.Active, BranchStatus.Closed)
            .Build();

    public static void EnsureCreatable(BranchStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(BranchStatus from, BranchStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(BranchStatus from, BranchStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this BranchStatus status) => status switch
    {
        BranchStatus.Active => "ACTIVE",
        BranchStatus.Restricted => "RESTRICTED",
        BranchStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BranchStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = BranchStatus.Active;
                return true;
            case "RESTRICTED":
                status = BranchStatus.Restricted;
                return true;
            case "CLOSED":
                status = BranchStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BranchStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BranchStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BranchStatusUnknown);
}
