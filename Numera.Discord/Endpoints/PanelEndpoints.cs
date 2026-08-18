using System.Globalization;
using Numera.Application.Common;
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

    private const string ScopeEconomy = "ECONOMY";
    private const string ScopeBank = "BANK";
    private const string ScopeBook = "ACCOUNTING_BOOK";

    private static readonly string[] ReconcileSteps = ["scope", "review", "run"];

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

    [EconomySlashCommand("guild", "管理対象の Guild を選択します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.SystemOwner)]
    public Task<DiscordEndpointResponse> SelectGuildAsync(
        DiscordEndpointContext context,
        [EconomyOption("guild", "対象の Guild ID を入力します。", true)] string guild,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ulong.TryParse(guild, NumberStyles.None, CultureInfo.InvariantCulture, out ulong scope) ||
            scope == 0)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.GuildEconomyNotFound));
        }

        return Task.FromResult(DiscordEndpointResponse.Message(
            ViewKeys.ManagePanel,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = scope.ToString(CultureInfo.InvariantCulture),
            }));
    }

    [EconomySlashCommand("reconcile", "整合性検査の手続を開始します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.SystemOwner)]
    public Task<DiscordEndpointResponse> ReconcileAsync(
        DiscordEndpointContext context,
        [EconomyOption("scope", "検査する範囲を選びます。", true)]
        [EconomyChoice("経済圏全体", ScopeEconomy)]
        [EconomyChoice("銀行", ScopeBank)]
        [EconomyChoice("会計帳簿", ScopeBook)]
        string scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DiscordEndpointResponse.Message(
            ViewKeys.SystemReconcile,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scope"] = scope,
                ["steps"] = string.Join(" → ", ReconcileSteps),
            }));
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
