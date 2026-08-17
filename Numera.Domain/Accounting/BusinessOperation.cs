namespace Numera.Domain.Accounting;

public enum BusinessOperationStatus
{
    Started = 1,
    Committed = 2,
    Failed = 3,
}

public sealed class BusinessOperation : VersionedEntity
{
    private static readonly StateTransitionTable<BusinessOperationStatus> Transitions =
        StateTransitionTable<BusinessOperationStatus>.Create(InvariantViolationCode.BusinessOperationTransitionInvalid)
            .AllowCreation(BusinessOperationStatus.Started)
            .Allow(BusinessOperationStatus.Started, BusinessOperationStatus.Committed, BusinessOperationStatus.Failed)
            .Build();

    private BusinessOperation(
        BusinessOperationId id,
        string operationType,
        EconomyScopeId economyScopeId,
        PartyId? actorPartyId,
        EntityIdValue correlationId,
        IdempotencyKey idempotencyKey,
        BusinessOperationStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? committedAt,
        long version)
        : base(version)
    {
        Id = id;
        OperationType = operationType;
        EconomyScopeId = economyScopeId;
        ActorPartyId = actorPartyId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = createdAt;
        CommittedAt = committedAt;
    }

    public BusinessOperationId Id { get; }

    public string OperationType { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public PartyId? ActorPartyId { get; }

    public EntityIdValue CorrelationId { get; }

    public IdempotencyKey IdempotencyKey { get; }

    public BusinessOperationStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? CommittedAt { get; private set; }

    public static BusinessOperation Start(
        BusinessOperationId id,
        string operationType,
        EconomyScopeId economyScopeId,
        PartyId? actorPartyId,
        EntityIdValue correlationId,
        IdempotencyKey idempotencyKey,
        UtcTimestamp createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        Transitions.EnsureCreatable(BusinessOperationStatus.Started);

        return new BusinessOperation(
            id,
            operationType,
            economyScopeId,
            actorPartyId,
            correlationId,
            idempotencyKey,
            BusinessOperationStatus.Started,
            createdAt,
            committedAt: null,
            InitialVersion);
    }

    public static BusinessOperation Rehydrate(
        BusinessOperationId id,
        string operationType,
        EconomyScopeId economyScopeId,
        PartyId? actorPartyId,
        EntityIdValue correlationId,
        IdempotencyKey idempotencyKey,
        BusinessOperationStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? committedAt,
        long version)
    {
        if ((status == BusinessOperationStatus.Committed) != committedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BusinessOperationTransitionInvalid);
        }

        return new BusinessOperation(
            id, operationType, economyScopeId, actorPartyId, correlationId, idempotencyKey,
            status, createdAt, committedAt, version);
    }

    public void Commit(UtcTimestamp committedAt)
    {
        Status = Transitions.EnsureAllowed(Status, BusinessOperationStatus.Committed);
        CommittedAt = committedAt;
        AdvanceVersion();
    }

    public void Fail()
    {
        Status = Transitions.EnsureAllowed(Status, BusinessOperationStatus.Failed);
        AdvanceVersion();
    }
}

public readonly struct IdempotencyKey : IEquatable<IdempotencyKey>
{
    public const int MaximumScopeLength = 64;
    public const int MaximumKeyLength = 128;

    private readonly string scope;
    private readonly string key;

    private IdempotencyKey(string scope, string key)
    {
        this.scope = scope;
        this.key = key;
    }

    public string Scope => scope ?? string.Empty;

    public string Key => key ?? string.Empty;

    public static bool TryCreate(ReadOnlySpan<char> scope, ReadOnlySpan<char> key, out IdempotencyKey result)
    {
        result = default;

        if (!IsAcceptable(scope, MaximumScopeLength) || !IsAcceptable(key, MaximumKeyLength))
        {
            return false;
        }

        result = new IdempotencyKey(scope.ToString(), key.ToString());
        return true;
    }

    public static IdempotencyKey Create(ReadOnlySpan<char> scope, ReadOnlySpan<char> key) =>
        TryCreate(scope, key, out IdempotencyKey result)
            ? result
            : throw InvariantViolationException.Create(InvariantViolationCode.IdempotencyKeyInvalid);

    public bool Equals(IdempotencyKey other) =>
        string.Equals(Scope, other.Scope, StringComparison.Ordinal) &&
        string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is IdempotencyKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Scope, Key);

    public override string ToString() => $"{Scope}/{Key}";

    public static bool operator ==(IdempotencyKey left, IdempotencyKey right) => left.Equals(right);

    public static bool operator !=(IdempotencyKey left, IdempotencyKey right) => !left.Equals(right);

    private static bool IsAcceptable(ReadOnlySpan<char> candidate, int maximumLength)
    {
        if (candidate.Length is 0 || candidate.Length > maximumLength)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            bool permitted = character is (>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '-' or '_' or '.' or ':';

            if (!permitted)
            {
                return false;
            }
        }

        return true;
    }
}

public static class BusinessOperationStatusCatalog
{
    public static string ToToken(this BusinessOperationStatus status) => status switch
    {
        BusinessOperationStatus.Started => "STARTED",
        BusinessOperationStatus.Committed => "COMMITTED",
        BusinessOperationStatus.Failed => "FAILED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.BusinessOperationTransitionInvalid),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BusinessOperationStatus status)
    {
        switch (token)
        {
            case "STARTED":
                status = BusinessOperationStatus.Started;
                return true;
            case "COMMITTED":
                status = BusinessOperationStatus.Committed;
                return true;
            case "FAILED":
                status = BusinessOperationStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BusinessOperationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BusinessOperationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BusinessOperationTransitionInvalid);
}
