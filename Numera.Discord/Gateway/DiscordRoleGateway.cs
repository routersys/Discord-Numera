using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Numera.Application.Abstractions;

namespace Numera.Discord.Gateway;

internal sealed class DiscordRoleGateway : IDiscordRoleGateway
{
    private const string GuildMissing = "GUILD_MISSING";
    private const string MemberMissing = "MEMBER_MISSING";
    private const string RoleMissing = "ROLE_MISSING";
    private const string IdentifierInvalid = "IDENTIFIER_INVALID";
    private const string Forbidden = "FORBIDDEN";
    private const string Transient = "TRANSIENT";

    private readonly DiscordSocketClient client;

    public DiscordRoleGateway(DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        this.client = client;
    }

    public Task<DiscordRoleOutcome> GrantAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(guildId, discordUserId, discordRoleId, granting: true, cancellationToken);

    public Task<DiscordRoleOutcome> RevokeAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(guildId, discordUserId, discordRoleId, granting: false, cancellationToken);

    private async Task<DiscordRoleOutcome> ApplyAsync(
        string guildId,
        string discordUserId,
        string discordRoleId,
        bool granting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ulong.TryParse(guildId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong guild) ||
            !ulong.TryParse(discordUserId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong user) ||
            !ulong.TryParse(discordRoleId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong role))
        {
            return DiscordRoleOutcome.Permanent(IdentifierInvalid);
        }

        if (client.GetGuild(guild) is not { } socketGuild)
        {
            return DiscordRoleOutcome.Retryable(GuildMissing);
        }

        if (socketGuild.GetRole(role) is not { } socketRole)
        {
            return granting
                ? DiscordRoleOutcome.Permanent(RoleMissing)
                : DiscordRoleOutcome.Succeeded;
        }

        if (socketGuild.GetUser(user) is not { } member)
        {
            return granting
                ? DiscordRoleOutcome.Retryable(MemberMissing)
                : DiscordRoleOutcome.Succeeded;
        }

        try
        {
            if (granting)
            {
                await member.AddRoleAsync(socketRole).ConfigureAwait(false);
            }
            else
            {
                await member.RemoveRoleAsync(socketRole).ConfigureAwait(false);
            }

            return DiscordRoleOutcome.Succeeded;
        }
        catch (HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.Forbidden)
        {
            return DiscordRoleOutcome.Permanent(Forbidden);
        }
        catch (HttpException)
        {
            return DiscordRoleOutcome.Retryable(Transient);
        }
    }
}
