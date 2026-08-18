using Numera.Domain.Common;

namespace Numera.Domain.Common;

public enum IdempotencyRecordStatus
{
    InProgress = 1,
    Completed = 2,
    Failed = 3,
}

public static class IdempotencyRecordStatusCatalog
{
    private static readonly StateTransitionTable<IdempotencyRecordStatus> Transitions =
        StateTransitionTable<IdempotencyRecordStatus>
            .Create(InvariantViolationCode.IdempotencyRecordTransitionInvalid)
            .AllowCreation(IdempotencyRecordStatus.InProgress)
            .Allow(
                IdempotencyRecordStatus.InProgress,
                IdempotencyRecordStatus.Completed,
                IdempotencyRecordStatus.Failed)
            .Build();

    public static void EnsureCreatable(IdempotencyRecordStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(IdempotencyRecordStatus from, IdempotencyRecordStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(IdempotencyRecordStatus from, IdempotencyRecordStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this IdempotencyRecordStatus status) => status switch
    {
        IdempotencyRecordStatus.InProgress => "IN_PROGRESS",
        IdempotencyRecordStatus.Completed => "COMPLETED",
        IdempotencyRecordStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out IdempotencyRecordStatus status)
    {
        switch (token)
        {
            case "IN_PROGRESS":
                status = IdempotencyRecordStatus.InProgress;
                return true;
            case "COMPLETED":
                status = IdempotencyRecordStatus.Completed;
                return true;
            case "FAILED":
                status = IdempotencyRecordStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static IdempotencyRecordStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out IdempotencyRecordStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.IdempotencyRecordStatusUnknown);
}
