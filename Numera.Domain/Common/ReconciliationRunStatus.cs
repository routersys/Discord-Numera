using Numera.Domain.Common;

namespace Numera.Domain.Common;

public enum ReconciliationRunStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    IssuesFound = 4,
}

public static class ReconciliationRunStatusCatalog
{
    private static readonly StateTransitionTable<ReconciliationRunStatus> Transitions =
        StateTransitionTable<ReconciliationRunStatus>
            .Create(InvariantViolationCode.ReconciliationRunTransitionInvalid)
            .AllowCreation(ReconciliationRunStatus.Running)
            .Allow(
                ReconciliationRunStatus.Running,
                ReconciliationRunStatus.Succeeded,
                ReconciliationRunStatus.Failed,
                ReconciliationRunStatus.IssuesFound)
            .Build();

    public static void EnsureCreatable(ReconciliationRunStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(ReconciliationRunStatus from, ReconciliationRunStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(ReconciliationRunStatus from, ReconciliationRunStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this ReconciliationRunStatus status) => status switch
    {
        ReconciliationRunStatus.Running => "RUNNING",
        ReconciliationRunStatus.Succeeded => "SUCCEEDED",
        ReconciliationRunStatus.Failed => "FAILED",
        ReconciliationRunStatus.IssuesFound => "ISSUES_FOUND",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out ReconciliationRunStatus status)
    {
        switch (token)
        {
            case "RUNNING":
                status = ReconciliationRunStatus.Running;
                return true;
            case "SUCCEEDED":
                status = ReconciliationRunStatus.Succeeded;
                return true;
            case "FAILED":
                status = ReconciliationRunStatus.Failed;
                return true;
            case "ISSUES_FOUND":
                status = ReconciliationRunStatus.IssuesFound;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static ReconciliationRunStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out ReconciliationRunStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.ReconciliationRunStatusUnknown);
}
