namespace Numera.Domain.Identity;

public enum DiscordIdentityLinkStatus
{
    Active = 1,
    Unlinked = 2,
}

public sealed class DiscordIdentityLink : VersionedEntity
{
    private static readonly StateTransitionTable<DiscordIdentityLinkStatus> Transitions =
        StateTransitionTable<DiscordIdentityLinkStatus>.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid)
            .AllowCreation(DiscordIdentityLinkStatus.Active)
            .Allow(DiscordIdentityLinkStatus.Active, DiscordIdentityLinkStatus.Unlinked)
            .Build();

    private DiscordIdentityLink(
        DiscordIdentityLinkId id,
        CustomerAccountId customerAccountId,
        DiscordUserId discordUserId,
        bool isPrimary,
        DiscordIdentityLinkStatus status,
        UtcTimestamp linkedAt,
        UtcTimestamp? unlinkedAt,
        UtcTimestamp lastAuthenticatedAt,
        long version)
        : base(version)
    {
        Id = id;
        CustomerAccountId = customerAccountId;
        DiscordUserId = discordUserId;
        IsPrimary = isPrimary;
        Status = status;
        LinkedAt = linkedAt;
        UnlinkedAt = unlinkedAt;
        LastAuthenticatedAt = lastAuthenticatedAt;
    }

    public DiscordIdentityLinkId Id { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public DiscordUserId DiscordUserId { get; }

    public bool IsPrimary { get; private set; }

    public DiscordIdentityLinkStatus Status { get; private set; }

    public UtcTimestamp LinkedAt { get; }

    public UtcTimestamp? UnlinkedAt { get; private set; }

    public UtcTimestamp LastAuthenticatedAt { get; private set; }

    public bool IsActive => Status == DiscordIdentityLinkStatus.Active;

    public static DiscordIdentityLink Link(
        DiscordIdentityLinkId id,
        CustomerAccountId customerAccountId,
        DiscordUserId discordUserId,
        bool isPrimary,
        UtcTimestamp linkedAt)
    {
        Transitions.EnsureCreatable(DiscordIdentityLinkStatus.Active);

        return new DiscordIdentityLink(
            id,
            customerAccountId,
            discordUserId,
            isPrimary,
            DiscordIdentityLinkStatus.Active,
            linkedAt,
            unlinkedAt: null,
            linkedAt,
            InitialVersion);
    }

    public static DiscordIdentityLink Rehydrate(
        DiscordIdentityLinkId id,
        CustomerAccountId customerAccountId,
        DiscordUserId discordUserId,
        bool isPrimary,
        DiscordIdentityLinkStatus status,
        UtcTimestamp linkedAt,
        UtcTimestamp? unlinkedAt,
        UtcTimestamp lastAuthenticatedAt,
        long version)
    {
        bool unlinked = status == DiscordIdentityLinkStatus.Unlinked;

        if (unlinked != unlinkedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }

        if (unlinked && isPrimary)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }

        return new DiscordIdentityLink(
            id,
            customerAccountId,
            discordUserId,
            isPrimary,
            status,
            linkedAt,
            unlinkedAt,
            lastAuthenticatedAt,
            version);
    }

    public void PromoteToPrimary()
    {
        EnsureActive();

        if (IsPrimary)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }

        IsPrimary = true;
        AdvanceVersion();
    }

    public void DemoteFromPrimary()
    {
        EnsureActive();

        if (!IsPrimary)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }

        IsPrimary = false;
        AdvanceVersion();
    }

    public void Unlink(UtcTimestamp unlinkedAt)
    {
        if (IsPrimary)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }

        Status = Transitions.EnsureAllowed(Status, DiscordIdentityLinkStatus.Unlinked);
        UnlinkedAt = unlinkedAt;
        AdvanceVersion();
    }

    public void RecordAuthentication(UtcTimestamp authenticatedAt)
    {
        EnsureActive();

        if (authenticatedAt < LastAuthenticatedAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.TimestampOutOfRange);
        }

        LastAuthenticatedAt = authenticatedAt;
        AdvanceVersion();
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkTransitionInvalid);
        }
    }
}

public static class DiscordIdentityLinkStatusCatalog
{
    public static string ToToken(this DiscordIdentityLinkStatus status) => status switch
    {
        DiscordIdentityLinkStatus.Active => "ACTIVE",
        DiscordIdentityLinkStatus.Unlinked => "UNLINKED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DiscordIdentityLinkStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = DiscordIdentityLinkStatus.Active;
                return true;
            case "UNLINKED":
                status = DiscordIdentityLinkStatus.Unlinked;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DiscordIdentityLinkStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DiscordIdentityLinkStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DiscordIdentityLinkStatusUnknown);
}
