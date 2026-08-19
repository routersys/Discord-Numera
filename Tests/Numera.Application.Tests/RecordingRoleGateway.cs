using System.Globalization;
using Numera.Application.Abstractions;

namespace Numera.Application.Tests;

internal sealed class RecordingRoleGateway : IDiscordRoleGateway
{
    internal List<string> Calls { get; } = [];

    internal DiscordRoleOutcome Outcome { get; set; } = DiscordRoleOutcome.Succeeded;

    public Task<DiscordRoleOutcome> GrantAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken)
    {
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"GRANT:{discordRoleId}"));

        return Task.FromResult(Outcome);
    }

    public Task<DiscordRoleOutcome> RevokeAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken)
    {
        Calls.Add(string.Create(CultureInfo.InvariantCulture, $"REVOKE:{discordRoleId}"));

        return Task.FromResult(Outcome);
    }
}
