using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Domain.Common;

namespace Numera.Application.Tests;

internal sealed class RecordingInstallationMessageGateway : IAtmInstallationMessageGateway
{
    internal List<string> Calls { get; } = [];

    internal AtmInstallationMessageState State { get; set; } = AtmInstallationMessageState.Confirmed;

    public Task<AtmInstallationMessageState> ConfirmAsync(
        string guildId,
        string channelId,
        string messageId,
        EntityIdValue installationNonce,
        CancellationToken cancellationToken)
    {
        Calls.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{channelId}:{messageId}:{installationNonce}"));

        return Task.FromResult(State);
    }
}
