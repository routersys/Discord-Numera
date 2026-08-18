using Numera.Domain.Common;

namespace Numera.Domain.Identity;

public enum AccountLinkGrantStatus
{
    Issued = 1,
    Consumed = 2,
    Expired = 3,
    Revoked = 4,
}

public sealed class AccountLinkGrant : VersionedEntity
{
    public const int DigestLength = 32;
    public const long LifetimeMilliseconds = 10 * 60 * 1000;

    private static readonly StateTransitionTable<AccountLinkGrantStatus> Transitions =
        StateTransitionTable<AccountLinkGrantStatus>
            .Create(InvariantViolationCode.LinkGrantTransitionInvalid)
            .AllowCreation(AccountLinkGrantStatus.Issued)
            .Allow(
                AccountLinkGrantStatus.Issued,
                AccountLinkGrantStatus.Consumed,
                AccountLinkGrantStatus.Expired,
                AccountLinkGrantStatus.Revoked)
            .Build();

    private AccountLinkGrant(
        AccountLinkGrantId id,
        CustomerAccountId customerAccountId,
        ReadOnlyMemory<byte> codeDigest,
        AccountLinkGrantStatus status,
        UtcTimestamp issuedAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? consumedAt,
        DiscordUserId? consumedBy,
        long version)
        : base(version)
    {
        Id = id;
        CustomerAccountId = customerAccountId;
        CodeDigest = codeDigest;
        Status = status;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        ConsumedBy = consumedBy;
    }

    public AccountLinkGrantId Id { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public ReadOnlyMemory<byte> CodeDigest { get; }

    public AccountLinkGrantStatus Status { get; private set; }

    public UtcTimestamp IssuedAt { get; }

    public UtcTimestamp ExpiresAt { get; }

    public UtcTimestamp? ConsumedAt { get; private set; }

    public DiscordUserId? ConsumedBy { get; private set; }

    public bool IsExpiredAt(UtcTimestamp now) => now.UnixMilliseconds >= ExpiresAt.UnixMilliseconds;

    public static AccountLinkGrant Issue(
        AccountLinkGrantId id,
        CustomerAccountId customerAccountId,
        ReadOnlyMemory<byte> codeDigest,
        UtcTimestamp issuedAt)
    {
        if (codeDigest.Length != DigestLength)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LinkGrantDigestInvalid);
        }

        return new AccountLinkGrant(
            id,
            customerAccountId,
            codeDigest,
            AccountLinkGrantStatus.Issued,
            issuedAt,
            UtcTimestamp.FromUnixMilliseconds(issuedAt.UnixMilliseconds + LifetimeMilliseconds),
            consumedAt: null,
            consumedBy: null,
            InitialVersion);
    }

    public static AccountLinkGrant Rehydrate(
        AccountLinkGrantId id,
        CustomerAccountId customerAccountId,
        ReadOnlyMemory<byte> codeDigest,
        AccountLinkGrantStatus status,
        UtcTimestamp issuedAt,
        UtcTimestamp expiresAt,
        UtcTimestamp? consumedAt,
        DiscordUserId? consumedBy,
        long version) =>
        new(id, customerAccountId, codeDigest, status, issuedAt, expiresAt, consumedAt, consumedBy, version);

    public void Consume(DiscordUserId discordUserId, UtcTimestamp now)
    {
        if (IsExpiredAt(now))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.LinkGrantExpired);
        }

        Transitions.EnsureAllowed(Status, AccountLinkGrantStatus.Consumed);

        Status = AccountLinkGrantStatus.Consumed;
        ConsumedAt = now;
        ConsumedBy = discordUserId;
        AdvanceVersion();
    }

    public void Expire()
    {
        Transitions.EnsureAllowed(Status, AccountLinkGrantStatus.Expired);

        Status = AccountLinkGrantStatus.Expired;
        AdvanceVersion();
    }

    public void Revoke()
    {
        Transitions.EnsureAllowed(Status, AccountLinkGrantStatus.Revoked);

        Status = AccountLinkGrantStatus.Revoked;
        AdvanceVersion();
    }
}

public static class AccountLinkGrantCatalog
{
    public static string ToToken(this AccountLinkGrantStatus status) => status switch
    {
        AccountLinkGrantStatus.Issued => "ISSUED",
        AccountLinkGrantStatus.Consumed => "CONSUMED",
        AccountLinkGrantStatus.Expired => "EXPIRED",
        AccountLinkGrantStatus.Revoked => "REVOKED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AccountLinkGrantStatus status)
    {
        switch (token)
        {
            case "ISSUED":
                status = AccountLinkGrantStatus.Issued;
                return true;
            case "CONSUMED":
                status = AccountLinkGrantStatus.Consumed;
                return true;
            case "EXPIRED":
                status = AccountLinkGrantStatus.Expired;
                return true;
            case "REVOKED":
                status = AccountLinkGrantStatus.Revoked;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AccountLinkGrantStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AccountLinkGrantStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.LinkGrantStatusUnknown);
}
