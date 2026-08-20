using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Discord.Sessions;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string FieldDate = "date";
    internal const string FieldDayClass = "class";
    internal const string FieldDescription = "reason";
    internal const string FieldCurrent = "current";

    [EconomyComponent(EconomyComponentKind.Button, ManagePanelFlow.EditAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> OpenEditorAsync(
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
                Request(context, input.SessionToken, scope, ManagePanelFlow.EditorState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        ManagePanelPayload payload = ManagePanelPayloadCodec.Read(current.Value.PayloadJson);

        if (Editor(payload.Action) is not { } editor)
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }

        return DiscordEndpointResponse.Modal(
            ViewKeys.PanelEditorModal(payload.Action),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customId"] = DiscordCustomId.Modal(editor, input.SessionToken),
            });
    }

    [EconomyModal(ManagementPanelCatalog.CalendarSetEditor, typeof(PanelCalendarSetForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitCalendarSetAsync(
        DiscordEndpointContext context,
        PanelCalendarSetForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (!BusinessDayClassCatalog.TryParseToken(form.DayClass.Trim(), out BusinessDayClass parsed))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CalendarDayClassInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldDate] = form.LocalDate.Trim(),
                [FieldDayClass] = parsed.ToToken(),
                [FieldDescription] = form.Description.Trim(),
            },
            cancellationToken);
    }

    [EconomyModal(ManagementPanelCatalog.CalendarClearEditor, typeof(PanelCalendarClearForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal Task<DiscordEndpointResponse> SubmitCalendarClearAsync(
        DiscordEndpointContext context,
        PanelCalendarClearForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldDate] = form.LocalDate.Trim(),
            },
            cancellationToken);
    }

    [EconomyComponent(EconomyComponentKind.Button, ManagePanelFlow.CommitAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> CommitEditorAsync(
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
                Request(context, input.SessionToken, scope, ManagePanelFlow.ReviewState, 3L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        ManagePanelPayload payload = ManagePanelPayloadCodec.Read(current.Value.PayloadJson);
        AuthorizationContext actor = EndpointAuthorization.ToActor(context);

        Result applied = await ApplyAsync(actor, payload, cancellationToken).ConfigureAwait(false);

        if (!applied.IsSuccess)
        {
            return EndpointFailures.From(applied.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                Request(context, input.SessionToken, scope, ManagePanelFlow.ReviewState, 3L),
                cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.ManagePanelApplied, Describe(payload));
    }

    private async Task<Result> ApplyAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        switch (payload.Action)
        {
            case "calendar-set":
            {
                if (!BusinessDayClassCatalog.TryParseToken(
                        Field(payload, FieldDayClass), out BusinessDayClass dayClass))
                {
                    return Result.Failure(
                        ErrorCategory.Validation, BankingErrorCodes.CalendarDayClassInvalid);
                }

                string description = Field(payload, FieldDescription);

                Result<BusinessCalendarDateView> outcome = await calendars
                    .SetDateOverrideAsync(
                        new SetBusinessCalendarDateCommand(
                            actor,
                            Field(payload, FieldDate),
                            dayClass,
                            description.Length == 0 ? null : description),
                        cancellationToken)
                    .ConfigureAwait(false);

                return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
            }

            case "calendar-clear":
                return await calendars
                    .ClearDateOverrideAsync(
                        new ClearBusinessCalendarDateCommand(actor, Field(payload, FieldDate)),
                        cancellationToken)
                    .ConfigureAwait(false);

            default:
                return Result.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown);
        }
    }

    private async Task<string> CurrentAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action is not ("calendar-set" or "calendar-clear"))
        {
            return catalog.Resolve(ViewKeys.PanelCurrentUnavailable);
        }

        Result<BusinessCalendarDateStatusView> status = await calendars
            .GetDateStatusAsync(
                new GetBusinessCalendarDateQuery(actor, Field(payload, FieldDate)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!status.IsSuccess)
        {
            return catalog.Resolve(ViewKeys.PanelCurrentUnavailable);
        }

        string label = catalog.Resolve(ViewKeys.StatusOf(status.Value.DayClass.ToToken()));

        return status.Value.HasOverride
            ? label
            : label + catalog.Resolve(ViewKeys.PanelCurrentDefaultSuffix);
    }

    private async Task<DiscordEndpointResponse> ReviewAsync(
        DiscordEndpointContext context,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, context.SessionToken, scope, ManagePanelFlow.EditorState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        ManagePanelPayload payload =
            ManagePanelPayloadCodec.Read(current.Value.PayloadJson) with { Fields = fields };

        string before = await CurrentAsync(
            EndpointAuthorization.ToActor(context), payload, cancellationToken).ConfigureAwait(false);

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, context.SessionToken, scope, ManagePanelFlow.EditorState, 2L),
                ManagePanelFlow.ReviewState,
                ManagePanelPayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        Dictionary<string, string> data = new(Describe(payload), StringComparer.Ordinal)
        {
            [FieldCurrent] = before,
        };

        return DiscordEndpointResponse.Message(
            ViewKeys.ManagePanelReview,
            data,
            new DiscordResponseBody(
                [
                    new DiscordResponseField(ViewKeys.PanelFieldCurrent, ViewKeys.PanelValueCurrent),
                    new DiscordResponseField(ViewKeys.PanelFieldAfter, ViewKeys.PanelValueAfter),
                ],
                new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(
                                ManagePanelFlow.CommitAction, context.SessionToken),
                            ViewKeys.ManagePanelCommitLabel,
                            DiscordButtonStyle.Primary),
                    ])));
    }

    private Dictionary<string, string> Describe(ManagePanelPayload payload)
    {
        string after = string.Join(
            ' ',
            payload.Fields
                .Where(static entry => entry.Value.Length > 0)
                .Select(static entry => entry.Value));

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["category"] = catalog.Resolve(ViewKeys.PanelCategoryLabel(payload.Category)),
            ["action"] = catalog.Resolve(
                ViewKeys.PanelActionLabel(payload.Category, payload.Action)),
            ["after"] = after.Length == 0 ? catalog.Resolve(ViewKeys.PanelCurrentUnavailable) : after,
        };
    }

    private static string Field(ManagePanelPayload payload, string key) =>
        payload.Fields.TryGetValue(key, out string? value) ? value : string.Empty;

    private static string? Editor(string action) =>
        ManagementPanelCatalog.Categories
            .SelectMany(static category => category.Actions)
            .FirstOrDefault(entry => string.Equals(entry.Value, action, StringComparison.Ordinal))
            is { HasEditor: true } found
            ? found.Editor
            : null;
}
