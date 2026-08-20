using System.Globalization;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed partial class ManagePanelEndpoints : IEconomyEndpoint
{
    private static readonly IReadOnlyDictionary<string, string> NoViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Sessions.InteractionSessionService sessions;
    private readonly ITextCatalog catalog;
    private readonly Numera.Application.Banking.IEconomyCalendarAdministrationApplicationService calendars;
    private readonly Numera.Application.Banking.ICurrencyTrustAdministrationApplicationService trusts;
    private readonly Numera.Application.Banking.IPaymentNetworkAdministrationApplicationService networks;
    private readonly Numera.Application.Banking.IPrudentialAdministrationApplicationService prudential;
    private readonly Numera.Application.Banking.IPresentationProfileAdministrationApplicationService presentation;
    private readonly Numera.Application.Banking.IDepositInsuranceAdministrationApplicationService insurance;
    private readonly Numera.Application.Banking.IMonetaryAuthorityAdministrationApplicationService authorities;
    private readonly Numera.Application.Banking.IAtmAdministrationApplicationService atms;
    private readonly Numera.Application.Banking.ICashAdministrationApplicationService cash;

    public ManagePanelEndpoints(
        Sessions.InteractionSessionService sessions,
        ITextCatalog catalog,
        Numera.Application.Banking.IEconomyCalendarAdministrationApplicationService calendars,
        Numera.Application.Banking.ICurrencyTrustAdministrationApplicationService trusts,
        Numera.Application.Banking.IPaymentNetworkAdministrationApplicationService networks,
        Numera.Application.Banking.IPrudentialAdministrationApplicationService prudential,
        Numera.Application.Banking.IPresentationProfileAdministrationApplicationService presentation,
        Numera.Application.Banking.IDepositInsuranceAdministrationApplicationService insurance,
        Numera.Application.Banking.IMonetaryAuthorityAdministrationApplicationService authorities,
        Numera.Application.Banking.IAtmAdministrationApplicationService atms,
        Numera.Application.Banking.ICashAdministrationApplicationService cash)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(trusts);
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(prudential);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(insurance);
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(atms);
        ArgumentNullException.ThrowIfNull(cash);

        this.sessions = sessions;
        this.catalog = catalog;
        this.calendars = calendars;
        this.trusts = trusts;
        this.networks = networks;
        this.prudential = prudential;
        this.presentation = presentation;
        this.insurance = insurance;
        this.authorities = authorities;
        this.atms = atms;
        this.cash = cash;
    }

    [EconomySlashCommand("panel", "管理メニューを表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> ShowAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Result<Sessions.InteractionSessionTicket> ticket = await sessions
            .OpenAsync(
                new Sessions.OpenInteractionSessionRequest(
                    context.UserId,
                    context.GuildId,
                    scope,
                    Sessions.ManagePanelFlow.FlowType,
                    Sessions.ManagePanelFlow.CategoryState,
                    Sessions.ManagePanelPayloadCodec.Write(Sessions.ManagePanelPayloadCodec.Empty)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ticket.IsSuccess)
        {
            return EndpointFailures.From(ticket.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.ManagePanel,
            NoViewData,
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                new DiscordResponseSelect(
                    DiscordCustomId.Select(
                        Sessions.ManagePanelFlow.CategoryAction, ticket.Value.RawToken),
                    ViewKeys.ManagePanelPlaceholder,
                    [
                        .. Sessions.ManagementPanelCatalog.Visible(EndpointAuthorization.ToActor(context).Level).Select(
                            category => new DiscordResponseSelectOption(
                                catalog.Resolve(ViewKeys.PanelCategoryLabel(category.Value)),
                                category.Value)),
                    ]),
                [])));
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
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound));
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
