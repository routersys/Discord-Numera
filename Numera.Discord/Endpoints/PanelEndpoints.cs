using System.Globalization;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed class ManagePanelEndpoints : IEconomyEndpoint
{
    private static readonly IReadOnlyDictionary<string, string> NoViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [EconomySlashCommand("panel", "管理メニューを表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public Task<DiscordEndpointResponse> ShowAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DiscordEndpointResponse.Message(ViewKeys.ManagePanel, NoViewData));
    }
}

[EconomyCommandGroup("system", "システム全体を管理します。")]
public sealed class SystemEndpoints : IEconomyEndpoint
{
    private static readonly IReadOnlyDictionary<string, string> NoViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IApplicationCommandSynchronizer synchronizer;

    public SystemEndpoints(IApplicationCommandSynchronizer synchronizer)
    {
        ArgumentNullException.ThrowIfNull(synchronizer);
        this.synchronizer = synchronizer;
    }

    [EconomySlashCommand("panel", "システムメニューを表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.SystemOwner)]
    public Task<DiscordEndpointResponse> ShowAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DiscordEndpointResponse.Message(ViewKeys.SystemPanel, NoViewData));
    }

    [EconomySlashCommand("commands-sync", "Command 宣言を Discord へ同期します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.SystemOwner)]
    public async Task<DiscordEndpointResponse> SynchronizeAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        DiscordCommandSyncOutcome outcome = await synchronizer
            .SynchronizeAsync(cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.Message(
            ViewKeys.SystemCommandsSynced,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["created"] = outcome.Created.ToString(CultureInfo.InvariantCulture),
                ["updated"] = outcome.Edited.ToString(CultureInfo.InvariantCulture),
                ["deleted"] = outcome.Deleted.ToString(CultureInfo.InvariantCulture),
            });
    }
}
