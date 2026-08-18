using Numera.Domain.Common;

namespace Numera.Domain.Identity;

public enum GuildEconomyStatus
{
    Active = 1,
    Suspended = 2,
    Disabled = 3,
}

public static class GuildEconomyStatusCatalog
{
    private static readonly StateTransitionTable<GuildEconomyStatus> Transitions =
        StateTransitionTable<GuildEconomyStatus>
            .Create(InvariantViolationCode.GuildEconomyTransitionInvalid)
            .AllowCreation(GuildEconomyStatus.Active)
            .Allow(GuildEconomyStatus.Active, GuildEconomyStatus.Suspended, GuildEconomyStatus.Disabled)
            .Allow(GuildEconomyStatus.Suspended, GuildEconomyStatus.Active, GuildEconomyStatus.Disabled)
            .Allow(GuildEconomyStatus.Disabled, GuildEconomyStatus.Active)
            .Build();

    public static void EnsureCreatable(GuildEconomyStatus status) =>
        Transitions.EnsureCreatable(status);

    public static void EnsureTransition(GuildEconomyStatus from, GuildEconomyStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static bool IsAllowed(GuildEconomyStatus from, GuildEconomyStatus to) =>
        Transitions.IsAllowed(from, to);

    public static string ToToken(this GuildEconomyStatus status) => status switch
    {
        GuildEconomyStatus.Active => "ACTIVE",
        GuildEconomyStatus.Suspended => "SUSPENDED",
        GuildEconomyStatus.Disabled => "DISABLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out GuildEconomyStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = GuildEconomyStatus.Active;
                return true;
            case "SUSPENDED":
                status = GuildEconomyStatus.Suspended;
                return true;
            case "DISABLED":
                status = GuildEconomyStatus.Disabled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static GuildEconomyStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out GuildEconomyStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.GuildEconomyStatusUnknown);
}
