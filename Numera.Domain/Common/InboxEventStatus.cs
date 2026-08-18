using Numera.Domain.Common;

namespace Numera.Domain.Common;

public enum InboxEventStatus
{
    Received = 1,
    Processed = 2,
    Failed = 3,
}

public static class InboxEventStatusCatalog
{
    private static readonly StateTransitionTable<InboxEventStatus> Transitions =
        StateTransitionTable<InboxEventStatus>
            .Create(InvariantViolationCode.InboxEventTransitionInvalid)
            .AllowCreation(InboxEventStatus.Received)
            .Allow(InboxEventStatus.Received, InboxEventStatus.Processed, InboxEventStatus.Failed)
            .Build();

    public static void EnsureCreatable(InboxEventStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(InboxEventStatus from, InboxEventStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(InboxEventStatus from, InboxEventStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this InboxEventStatus status) => status switch
    {
        InboxEventStatus.Received => "RECEIVED",
        InboxEventStatus.Processed => "PROCESSED",
        InboxEventStatus.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out InboxEventStatus status)
    {
        switch (token)
        {
            case "RECEIVED":
                status = InboxEventStatus.Received;
                return true;
            case "PROCESSED":
                status = InboxEventStatus.Processed;
                return true;
            case "FAILED":
                status = InboxEventStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static InboxEventStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out InboxEventStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.InboxEventStatusUnknown);
}
