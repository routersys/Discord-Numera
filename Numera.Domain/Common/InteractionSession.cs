using System.Text;

namespace Numera.Domain.Common;

public enum InteractionSessionStatus
{
    Active = 1,
    Completed = 2,
    Cancelled = 3,
    Expired = 4,
    Superseded = 5,
}

public sealed class InteractionSession
{
    public const int TokenHashLength = 32;
    public const int MaximumPayloadBytes = 32_768;
    public const int MaximumActivePerUser = 8;
    public const int DefaultLifetimeMinutes = 15;

    private static readonly StateTransitionTable<InteractionSessionStatus> Transitions =
        StateTransitionTable<InteractionSessionStatus>.Create(InvariantViolationCode.InteractionSessionTransitionInvalid)
            .AllowCreation(InteractionSessionStatus.Active)
            .Allow(
                InteractionSessionStatus.Active,
                InteractionSessionStatus.Completed,
                InteractionSessionStatus.Cancelled,
                InteractionSessionStatus.Expired,
                InteractionSessionStatus.Superseded)
            .Build();

    private readonly byte[] tokenHash;

    private InteractionSession(
        InteractionSessionId id,
        string discordUserId,
        string guildId,
        EconomyScopeId economyScopeId,
        string flowType,
        string state,
        byte[] tokenHash,
        string payloadJson,
        long stateVersion,
        InteractionSessionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? completedAt)
    {
        Id = id;
        DiscordUserId = discordUserId;
        GuildId = guildId;
        EconomyScopeId = economyScopeId;
        FlowType = flowType;
        State = state;
        this.tokenHash = tokenHash;
        PayloadJson = payloadJson;
        StateVersion = stateVersion;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CompletedAt = completedAt;
    }

    public InteractionSessionId Id { get; }

    public string DiscordUserId { get; }

    public string GuildId { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public string FlowType { get; }

    public string State { get; private set; }

    public string PayloadJson { get; private set; }

    public long StateVersion { get; private set; }

    public InteractionSessionStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp ExpiresAt { get; }

    public UtcTimestamp? CompletedAt { get; private set; }

    public bool IsActive => Status == InteractionSessionStatus.Active;

    public ReadOnlySpan<byte> TokenHash => tokenHash;

    public byte[] TokenHashCopy() => [.. tokenHash];

    public static InteractionSession Open(
        InteractionSessionId id,
        string discordUserId,
        string guildId,
        EconomyScopeId economyScopeId,
        string flowType,
        string state,
        ReadOnlySpan<byte> tokenHash,
        string payloadJson,
        UtcTimestamp createdAt,
        UtcTimestamp expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowType);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(payloadJson);

        if (tokenHash.Length != TokenHashLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionTokenInvalid);
        }

        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionPayloadInvalid);
        }

        if (expiresAt <= createdAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionExpiryInvalid);
        }

        Transitions.EnsureCreatable(InteractionSessionStatus.Active);

        return new InteractionSession(
            id, discordUserId, guildId, economyScopeId, flowType, state, [.. tokenHash], payloadJson,
            stateVersion: 0, InteractionSessionStatus.Active, createdAt, expiresAt, completedAt: null);
    }

    public static InteractionSession Rehydrate(
        InteractionSessionId id,
        string discordUserId,
        string guildId,
        EconomyScopeId economyScopeId,
        string flowType,
        string state,
        ReadOnlySpan<byte> tokenHash,
        string payloadJson,
        long stateVersion,
        InteractionSessionStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? completedAt)
    {
        if (tokenHash.Length != TokenHashLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionTokenInvalid);
        }

        if ((status == InteractionSessionStatus.Active) == completedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionTransitionInvalid);
        }

        return new InteractionSession(
            id, discordUserId, guildId, economyScopeId, flowType, state, [.. tokenHash], payloadJson,
            stateVersion, status, createdAt, expiresAt, completedAt);
    }

    public bool HasExpired(UtcTimestamp now) => now >= ExpiresAt;

    public bool Matches(
        string discordUserId,
        string guildId,
        EconomyScopeId economyScopeId,
        string state,
        long stateVersion) =>
        string.Equals(DiscordUserId, discordUserId, StringComparison.Ordinal)
        && string.Equals(GuildId, guildId, StringComparison.Ordinal)
        && EconomyScopeId == economyScopeId
        && string.Equals(State, state, StringComparison.Ordinal)
        && StateVersion == stateVersion;

    public void Advance(string state, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(payloadJson);

        if (!IsActive)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionTransitionInvalid);
        }

        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionPayloadInvalid);
        }

        State = state;
        PayloadJson = payloadJson;
        StateVersion = checked(StateVersion + 1);
    }

    public void Complete(UtcTimestamp at) => Terminate(InteractionSessionStatus.Completed, at);

    public void Cancel(UtcTimestamp at) => Terminate(InteractionSessionStatus.Cancelled, at);

    public void Expire(UtcTimestamp at) => Terminate(InteractionSessionStatus.Expired, at);

    public void Supersede(UtcTimestamp at) => Terminate(InteractionSessionStatus.Superseded, at);

    private void Terminate(InteractionSessionStatus target, UtcTimestamp at)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        CompletedAt = at;
    }
}

public static class InteractionSessionStatusCatalog
{
    public static string ToToken(this InteractionSessionStatus status) => status switch
    {
        InteractionSessionStatus.Active => "ACTIVE",
        InteractionSessionStatus.Completed => "COMPLETED",
        InteractionSessionStatus.Cancelled => "CANCELLED",
        InteractionSessionStatus.Expired => "EXPIRED",
        InteractionSessionStatus.Superseded => "SUPERSEDED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out InteractionSessionStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = InteractionSessionStatus.Active;
                return true;
            case "COMPLETED":
                status = InteractionSessionStatus.Completed;
                return true;
            case "CANCELLED":
                status = InteractionSessionStatus.Cancelled;
                return true;
            case "EXPIRED":
                status = InteractionSessionStatus.Expired;
                return true;
            case "SUPERSEDED":
                status = InteractionSessionStatus.Superseded;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static InteractionSessionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out InteractionSessionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.InteractionSessionStatusUnknown);
}
