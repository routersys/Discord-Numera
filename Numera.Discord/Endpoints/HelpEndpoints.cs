using Numera.Discord.Abstractions;
using Numera.Discord.Rendering;

namespace Numera.Discord.Endpoints;

public sealed class HelpEndpoints : IEconomyEndpoint
{
    private static readonly IReadOnlyDictionary<string, string> NoViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [EconomySlashCommand("help", "使い方を表示します。")]
    [EconomyAuthorization(AuthorizationLevel.Unregistered)]
    public Task<DiscordEndpointResponse> ShowAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DiscordEndpointResponse.Message(ViewKeys.Help, NoViewData));
    }
}
