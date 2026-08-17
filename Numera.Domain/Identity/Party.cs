namespace Numera.Domain.Identity;

public enum PartyType
{
    Customer = 1,
    Bank = 2,
    GuildTreasury = 3,
    Government = 4,
    System = 5,
    Corporation = 6,
}

public enum PartyStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public sealed class Party : VersionedEntity
{
    private static readonly StateTransitionTable<PartyStatus> Transitions =
        StateTransitionTable<PartyStatus>.Create(InvariantViolationCode.PartyTransitionInvalid)
            .AllowCreation(PartyStatus.Active)
            .Allow(PartyStatus.Active, PartyStatus.Restricted, PartyStatus.Closed)
            .Allow(PartyStatus.Restricted, PartyStatus.Active, PartyStatus.Closed)
            .Build();

    private Party(
        PartyId id,
        PartyType type,
        DisplayName displayName,
        PartyStatus status,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        Type = type;
        DisplayName = displayName;
        Status = status;
        CreatedAt = createdAt;
    }

    public PartyId Id { get; }

    public PartyType Type { get; }

    public DisplayName DisplayName { get; private set; }

    public PartyStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public bool IsClosed => Status == PartyStatus.Closed;

    public static Party Create(
        PartyId id,
        PartyType type,
        DisplayName displayName,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(PartyStatus.Active);
        return new Party(id, type, displayName, PartyStatus.Active, createdAt, InitialVersion);
    }

    public static Party Rehydrate(
        PartyId id,
        PartyType type,
        DisplayName displayName,
        PartyStatus status,
        UtcTimestamp createdAt,
        long version) =>
        new(id, type, displayName, status, createdAt, version);

    public void Restrict() => ChangeStatus(PartyStatus.Restricted);

    public void ClearRestriction() => ChangeStatus(PartyStatus.Active);

    public void Close() => ChangeStatus(PartyStatus.Closed);

    public void Rename(DisplayName displayName)
    {
        if (IsClosed)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PartyTransitionInvalid);
        }

        DisplayName = displayName;
        AdvanceVersion();
    }

    private void ChangeStatus(PartyStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }
}

public static class PartyCatalog
{
    public static string ToToken(this PartyType type) => type switch
    {
        PartyType.Customer => "CUSTOMER",
        PartyType.Bank => "BANK",
        PartyType.GuildTreasury => "GUILD_TREASURY",
        PartyType.Government => "GOVERNMENT",
        PartyType.System => "SYSTEM",
        PartyType.Corporation => "CORPORATION",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PartyTypeUnknown),
    };

    public static string ToToken(this PartyStatus status) => status switch
    {
        PartyStatus.Active => "ACTIVE",
        PartyStatus.Restricted => "RESTRICTED",
        PartyStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PartyTransitionInvalid),
    };

    public static bool TryParseTypeToken(ReadOnlySpan<char> token, out PartyType type)
    {
        switch (token)
        {
            case "CUSTOMER":
                type = PartyType.Customer;
                return true;
            case "BANK":
                type = PartyType.Bank;
                return true;
            case "GUILD_TREASURY":
                type = PartyType.GuildTreasury;
                return true;
            case "GOVERNMENT":
                type = PartyType.Government;
                return true;
            case "SYSTEM":
                type = PartyType.System;
                return true;
            case "CORPORATION":
                type = PartyType.Corporation;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static PartyType ParseTypeToken(ReadOnlySpan<char> token) =>
        TryParseTypeToken(token, out PartyType type)
            ? type
            : throw InvariantViolationException.Create(InvariantViolationCode.PartyTypeUnknown);

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out PartyStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = PartyStatus.Active;
                return true;
            case "RESTRICTED":
                status = PartyStatus.Restricted;
                return true;
            case "CLOSED":
                status = PartyStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static PartyStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out PartyStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.PartyTransitionInvalid);
}
