using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Discord.Sessions;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    [EconomyComponent(EconomyComponentKind.Select, ManagePanelFlow.CategoryAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> SelectCategoryAsync(
        DiscordEndpointContext context,
        DiscordComponentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (input.Values.Count != 1 ||
            ManagementPanelCatalog.Find(input.Values[0]) is not { } category)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, input.SessionToken, scope, ManagePanelFlow.CategoryState, 0L),
                ManagePanelFlow.ActionState,
                ManagePanelPayloadCodec.Write(
                    ManagePanelPayloadCodec.Empty with { Category = category.Value }),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.ManagePanelCategory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = catalog.Resolve(ViewKeys.PanelCategoryLabel(category.Value)),
            },
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                new DiscordResponseSelect(
                    DiscordCustomId.Select(ManagePanelFlow.ActionAction, input.SessionToken),
                    ViewKeys.ManagePanelActionPlaceholder,
                    [
                        .. category.Actions.Select(action => new DiscordResponseSelectOption(
                            catalog.Resolve(ViewKeys.PanelActionLabel(category.Value, action.Value)),
                            action.Value)),
                    ]),
                [])));
    }

    [EconomyComponent(EconomyComponentKind.Select, ManagePanelFlow.ActionAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> SelectActionAsync(
        DiscordEndpointContext context,
        DiscordComponentInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, input.SessionToken, scope, ManagePanelFlow.ActionState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        ManagePanelPayload payload = ManagePanelPayloadCodec.Read(current.Value.PayloadJson);

        if (ManagementPanelCatalog.Find(payload.Category) is not { } category ||
            input.Values.Count != 1 ||
            ManagementPanelCatalog.FindAction(category, input.Values[0]) is not { } action)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, input.SessionToken, scope, ManagePanelFlow.ActionState, 1L),
                ManagePanelFlow.ReviewState,
                ManagePanelPayloadCodec.Write(payload with { Action = action.Value }),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                Request(context, input.SessionToken, scope, ManagePanelFlow.ReviewState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, string> data = new(StringComparer.Ordinal)
        {
            ["category"] = catalog.Resolve(ViewKeys.PanelCategoryLabel(category.Value)),
            ["action"] = catalog.Resolve(ViewKeys.PanelActionLabel(category.Value, action.Value)),
            ["route"] = action.Route,
        };

        return DiscordEndpointResponse.UpdateMessage(
            action.IsImplemented ? ViewKeys.ManagePanelRoute : ViewKeys.ManagePanelPending, data);
    }

    private static ConsumeInteractionSessionRequest Request(
        DiscordEndpointContext context,
        string sessionToken,
        EconomyScopeId scope,
        string state,
        long stateVersion) =>
        new(sessionToken, context.UserId, context.GuildId, scope, state, stateVersion);
}
