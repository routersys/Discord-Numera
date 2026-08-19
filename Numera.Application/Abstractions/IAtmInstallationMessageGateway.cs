using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public enum AtmInstallationMessageState
{
    Confirmed,
    Missing,
    Unknown,
}

public interface IAtmInstallationMessageGateway
{
    Task<AtmInstallationMessageState> ConfirmAsync(
        string guildId,
        string channelId,
        string messageId,
        EntityIdValue installationNonce,
        CancellationToken cancellationToken);
}
