using System.Text;

namespace Numera.Domain.Common;

public enum OutboxEventStatus
{
    Pending = 1,
    Claimed = 2,
    Published = 3,
    RetryDue = 4,
    DeadLetter = 5,
}

public sealed class OutboxEvent : VersionedEntity
{
    public const int MaximumPayloadBytes = 32_768;
    public const int MaximumAttemptCount = 5;

    private static readonly StateTransitionTable<OutboxEventStatus> Transitions =
        StateTransitionTable<OutboxEventStatus>.Create(InvariantViolationCode.OutboxTransitionInvalid)
            .AllowCreation(OutboxEventStatus.Pending)
            .Allow(OutboxEventStatus.Pending, OutboxEventStatus.Claimed)
            .Allow(OutboxEventStatus.Claimed, OutboxEventStatus.Published, OutboxEventStatus.RetryDue, OutboxEventStatus.DeadLetter)
            .Allow(OutboxEventStatus.RetryDue, OutboxEventStatus.Claimed)
            .Build();

    private OutboxEvent(
        OutboxEventId id,
        BusinessOperationId? businessOperationId,
        string eventType,
        string payloadJson,
        OutboxEventStatus status,
        EntityIdValue? claimToken,
        UtcTimestamp? claimedAt,
        UtcTimestamp? claimExpiresAt,
        UtcTimestamp? nextAttemptAt,
        UtcTimestamp createdAt,
        UtcTimestamp? publishedAt,
        int attemptCount,
        string? lastErrorCode,
        long version)
        : base(version)
    {
        Id = id;
        BusinessOperationId = businessOperationId;
        EventType = eventType;
        PayloadJson = payloadJson;
        Status = status;
        ClaimToken = claimToken;
        ClaimedAt = claimedAt;
        ClaimExpiresAt = claimExpiresAt;
        NextAttemptAt = nextAttemptAt;
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        AttemptCount = attemptCount;
        LastErrorCode = lastErrorCode;
    }

    public OutboxEventId Id { get; }

    public BusinessOperationId? BusinessOperationId { get; }

    public string EventType { get; }

    public string PayloadJson { get; }

    public OutboxEventStatus Status { get; private set; }

    public EntityIdValue? ClaimToken { get; private set; }

    public UtcTimestamp? ClaimedAt { get; private set; }

    public UtcTimestamp? ClaimExpiresAt { get; private set; }

    public UtcTimestamp? NextAttemptAt { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? PublishedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastErrorCode { get; private set; }

    public static OutboxEvent Enqueue(
        OutboxEventId id,
        BusinessOperationId? businessOperationId,
        string eventType,
        string payloadJson,
        UtcTimestamp createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxPayloadInvalid);
        }

        Transitions.EnsureCreatable(OutboxEventStatus.Pending);

        return new OutboxEvent(
            id, businessOperationId, eventType, payloadJson, OutboxEventStatus.Pending,
            claimToken: null, claimedAt: null, claimExpiresAt: null, nextAttemptAt: null,
            createdAt, publishedAt: null, attemptCount: 0, lastErrorCode: null, InitialVersion);
    }

    public static OutboxEvent Rehydrate(
        OutboxEventId id,
        BusinessOperationId? businessOperationId,
        string eventType,
        string payloadJson,
        OutboxEventStatus status,
        EntityIdValue? claimToken,
        UtcTimestamp? claimedAt,
        UtcTimestamp? claimExpiresAt,
        UtcTimestamp? nextAttemptAt,
        UtcTimestamp createdAt,
        UtcTimestamp? publishedAt,
        int attemptCount,
        string? lastErrorCode,
        long version)
    {
        bool claimed = status == OutboxEventStatus.Claimed;
        if (claimed != (claimToken.HasValue && claimedAt.HasValue && claimExpiresAt.HasValue))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxTransitionInvalid);
        }

        if ((status == OutboxEventStatus.RetryDue) != nextAttemptAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxTransitionInvalid);
        }

        if ((status == OutboxEventStatus.Published) != publishedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxTransitionInvalid);
        }

        if (attemptCount is < 0 or > MaximumAttemptCount)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxAttemptExhausted);
        }

        return new OutboxEvent(
            id, businessOperationId, eventType, payloadJson, status, claimToken, claimedAt,
            claimExpiresAt, nextAttemptAt, createdAt, publishedAt, attemptCount, lastErrorCode, version);
    }

    public void Claim(EntityIdValue claimToken, UtcTimestamp claimedAt, UtcTimestamp claimExpiresAt)
    {
        if (claimExpiresAt <= claimedAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxTransitionInvalid);
        }

        if (AttemptCount >= MaximumAttemptCount)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxAttemptExhausted);
        }

        Status = Transitions.EnsureAllowed(Status, OutboxEventStatus.Claimed);
        ClaimToken = claimToken;
        ClaimedAt = claimedAt;
        ClaimExpiresAt = claimExpiresAt;
        NextAttemptAt = null;
        AttemptCount++;
        AdvanceVersion();
    }

    public void MarkPublished(UtcTimestamp publishedAt)
    {
        Status = Transitions.EnsureAllowed(Status, OutboxEventStatus.Published);
        ClearClaim();
        PublishedAt = publishedAt;
        LastErrorCode = null;
        AdvanceVersion();
    }

    public void ScheduleRetry(UtcTimestamp nextAttemptAt, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        if (AttemptCount >= MaximumAttemptCount)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.OutboxAttemptExhausted);
        }

        Status = Transitions.EnsureAllowed(Status, OutboxEventStatus.RetryDue);
        ClearClaim();
        NextAttemptAt = nextAttemptAt;
        LastErrorCode = errorCode;
        AdvanceVersion();
    }

    public void MarkDeadLetter(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Status = Transitions.EnsureAllowed(Status, OutboxEventStatus.DeadLetter);
        ClearClaim();
        NextAttemptAt = null;
        LastErrorCode = errorCode;
        AdvanceVersion();
    }

    private void ClearClaim()
    {
        ClaimToken = null;
        ClaimedAt = null;
        ClaimExpiresAt = null;
        NextAttemptAt = null;
    }
}

public static class OutboxEventStatusCatalog
{
    public static string ToToken(this OutboxEventStatus status) => status switch
    {
        OutboxEventStatus.Pending => "PENDING",
        OutboxEventStatus.Claimed => "CLAIMED",
        OutboxEventStatus.Published => "PUBLISHED",
        OutboxEventStatus.RetryDue => "RETRY_DUE",
        OutboxEventStatus.DeadLetter => "DEAD_LETTER",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.OutboxStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out OutboxEventStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = OutboxEventStatus.Pending;
                return true;
            case "CLAIMED":
                status = OutboxEventStatus.Claimed;
                return true;
            case "PUBLISHED":
                status = OutboxEventStatus.Published;
                return true;
            case "RETRY_DUE":
                status = OutboxEventStatus.RetryDue;
                return true;
            case "DEAD_LETTER":
                status = OutboxEventStatus.DeadLetter;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static OutboxEventStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out OutboxEventStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.OutboxStatusUnknown);
}
