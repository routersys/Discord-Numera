namespace Numera.Application.Abstractions;

public enum DiscordRoleOutcomeKind
{
    Succeeded,
    Retryable,
    Permanent,
}

public readonly record struct DiscordRoleOutcome(DiscordRoleOutcomeKind Kind, string? FailureCode)
{
    public static DiscordRoleOutcome Succeeded { get; } = new(DiscordRoleOutcomeKind.Succeeded, null);

    public static DiscordRoleOutcome Retryable(string failureCode) =>
        new(DiscordRoleOutcomeKind.Retryable, failureCode);

    public static DiscordRoleOutcome Permanent(string failureCode) =>
        new(DiscordRoleOutcomeKind.Permanent, failureCode);
}

public interface IDiscordRoleGateway
{
    Task<DiscordRoleOutcome> GrantAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken);

    Task<DiscordRoleOutcome> RevokeAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken);
}
