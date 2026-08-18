using Numera.Domain.Common;

namespace Numera.Domain.Accounting;

public enum InterestPostingBatchStatus
{
    Pending = 1,
    Posted = 2,
    Failed = 3,
}

public static class InterestPostingBatchStatusCatalog
{
    private static readonly StateTransitionTable<InterestPostingBatchStatus> Transitions =
        StateTransitionTable<InterestPostingBatchStatus>
            .Create(InvariantViolationCode.InterestPostingBatchTransitionInvalid)
            .AllowCreation(InterestPostingBatchStatus.Pending)
            .Allow(
                InterestPostingBatchStatus.Pending,
                InterestPostingBatchStatus.Posted,
                InterestPostingBatchStatus.Failed)
            .Build();

    public static void EnsureCreatable(InterestPostingBatchStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(InterestPostingBatchStatus from, InterestPostingBatchStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(InterestPostingBatchStatus from, InterestPostingBatchStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this InterestPostingBatchStatus status) => status switch
    {
        InterestPostingBatchStatus.Pending => "PENDING",
        InterestPostingBatchStatus.Posted => "POSTED",
        InterestPostingBatchStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out InterestPostingBatchStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = InterestPostingBatchStatus.Pending;
                return true;
            case "POSTED":
                status = InterestPostingBatchStatus.Posted;
                return true;
            case "FAILED":
                status = InterestPostingBatchStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static InterestPostingBatchStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out InterestPostingBatchStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.InterestPostingBatchStatusUnknown);
}
