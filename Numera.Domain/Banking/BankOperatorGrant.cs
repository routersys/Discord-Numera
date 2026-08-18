using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Domain.Banking;

public enum BankOperatorGrantStatus
{
    Active = 1,
    Revoked = 2,
}

public sealed class BankOperatorGrant : VersionedEntity
{
    private static readonly StateTransitionTable<BankOperatorGrantStatus> Transitions =
        StateTransitionTable<BankOperatorGrantStatus>
            .Create(InvariantViolationCode.BankOperatorGrantTransitionInvalid)
            .AllowCreation(BankOperatorGrantStatus.Active)
            .Allow(BankOperatorGrantStatus.Active, BankOperatorGrantStatus.Revoked)
            .Build();

    private BankOperatorGrant(
        BankOperatorGrantId id,
        BankId bankId,
        DiscordUserId discordUserId,
        BankOperatorGrantStatus status,
        DiscordUserId grantedBy,
        UtcTimestamp grantedAt,
        UtcTimestamp? revokedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        DiscordUserId = discordUserId;
        Status = status;
        GrantedBy = grantedBy;
        GrantedAt = grantedAt;
        RevokedAt = revokedAt;
    }

    public BankOperatorGrantId Id { get; }

    public BankId BankId { get; }

    public DiscordUserId DiscordUserId { get; }

    public BankOperatorGrantStatus Status { get; private set; }

    public DiscordUserId GrantedBy { get; }

    public UtcTimestamp GrantedAt { get; }

    public UtcTimestamp? RevokedAt { get; private set; }

    public bool IsActive => Status == BankOperatorGrantStatus.Active;

    public static BankOperatorGrant Grant(
        BankOperatorGrantId id,
        BankId bankId,
        DiscordUserId discordUserId,
        DiscordUserId grantedBy,
        UtcTimestamp grantedAt)
    {
        if (discordUserId == grantedBy)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankOperatorGrantSelfService);
        }

        return new BankOperatorGrant(
            id,
            bankId,
            discordUserId,
            BankOperatorGrantStatus.Active,
            grantedBy,
            grantedAt,
            revokedAt: null,
            InitialVersion);
    }

    public static BankOperatorGrant Rehydrate(
        BankOperatorGrantId id,
        BankId bankId,
        DiscordUserId discordUserId,
        BankOperatorGrantStatus status,
        DiscordUserId grantedBy,
        UtcTimestamp grantedAt,
        UtcTimestamp? revokedAt,
        long version) =>
        new(id, bankId, discordUserId, status, grantedBy, grantedAt, revokedAt, version);

    public void Revoke(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, BankOperatorGrantStatus.Revoked);

        Status = BankOperatorGrantStatus.Revoked;
        RevokedAt = now;
        AdvanceVersion();
    }
}

public static class BankOperatorGrantCatalog
{
    public static string ToToken(this BankOperatorGrantStatus status) => status switch
    {
        BankOperatorGrantStatus.Active => "ACTIVE",
        BankOperatorGrantStatus.Revoked => "REVOKED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BankOperatorGrantStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = BankOperatorGrantStatus.Active;
                return true;
            case "REVOKED":
                status = BankOperatorGrantStatus.Revoked;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BankOperatorGrantStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BankOperatorGrantStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BankOperatorGrantStatusUnknown);
}
