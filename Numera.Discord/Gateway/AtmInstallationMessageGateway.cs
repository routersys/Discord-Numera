using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Numera.Application.Abstractions;
using Numera.Domain.Common;

namespace Numera.Discord.Gateway;

internal sealed class AtmInstallationMessageGateway : IAtmInstallationMessageGateway
{
    private readonly DiscordSocketClient client;

    public AtmInstallationMessageGateway(DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        this.client = client;
    }

    public async Task<AtmInstallationMessageState> ConfirmAsync(
        string guildId,
        string channelId,
        string messageId,
        EntityIdValue installationNonce,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ulong.TryParse(guildId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong guild) ||
            !ulong.TryParse(channelId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong channel) ||
            !ulong.TryParse(messageId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong message))
        {
            return AtmInstallationMessageState.Missing;
        }

        if (client.GetGuild(guild) is not { } socketGuild ||
            socketGuild.GetChannel(channel) is not IMessageChannel socketChannel)
        {
            return AtmInstallationMessageState.Unknown;
        }

        try
        {
            if (await socketChannel.GetMessageAsync(message).ConfigureAwait(false) is not { } existing)
            {
                return AtmInstallationMessageState.Missing;
            }

            return Carries(existing, installationNonce)
                ? AtmInstallationMessageState.Confirmed
                : AtmInstallationMessageState.Missing;
        }
        catch (HttpException exception)
            when (exception.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            return AtmInstallationMessageState.Missing;
        }
        catch (HttpException)
        {
            return AtmInstallationMessageState.Unknown;
        }
    }

    private static bool Carries(IMessage message, EntityIdValue installationNonce)
    {
        string nonce = installationNonce.ToString();

        foreach (IMessageComponent component in message.Components)
        {
            if (Carries(component, nonce))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Carries(IMessageComponent component, string nonce) => component switch
    {
        ActionRowComponent row => row.Components.Any(child => Carries(child, nonce)),
        ButtonComponent button => button.CustomId?.Contains(nonce, StringComparison.Ordinal) == true,
        SelectMenuComponent select => select.CustomId?.Contains(nonce, StringComparison.Ordinal) == true,
        _ => false,
    };
}
