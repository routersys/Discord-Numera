using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Discord.Sessions;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

internal enum PanelCurrentGroup
{
    None = 0,
    Calendar = 1,
    Governance = 2,
    Policy = 3,
    Atm = 4,
    Merchant = 5,
    Bank = 6,
    Resolution = 7,
}

public sealed partial class ManagePanelEndpoints
{
    internal const string FieldDate = "date";
    internal const string FieldDayClass = "class";
    internal const string FieldDescription = "reason";
    internal const string FieldCurrent = "current";

    internal const string ActionCalendarSet = "calendar-set";
    internal const string ActionCalendarClear = "calendar-clear";

    [EconomyComponent(EconomyComponentKind.Button, ManagePanelFlow.EditAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.MerchantOperator)]
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
    [EconomyAuthorization(Abstractions.AuthorizationLevel.MerchantOperator)]
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

        if (ManagementPanelCatalog.Find(payload.Category) is not { } category ||
            (int)actor.Level > (int)category.RequiredLevel)
        {
            return EndpointFailures.From(
                ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
        }

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

    private Task<Result> ApplyAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken) => payload.Action switch
    {
        ActionCalendarSet or ActionCalendarClear =>
            ApplyCalendarAsync(actor, payload, cancellationToken),
        ActionTrustPolicy => PublishTrustPolicyAsync(actor, payload, cancellationToken),
        ActionNetworkPolicy => PublishNetworkPolicyAsync(actor, payload, cancellationToken),
        ActionNetworkState => ChangeNetworkStateAsync(actor, payload, cancellationToken),
        ActionPrudentialPolicy => PublishPrudentialPolicyAsync(actor, payload, cancellationToken),
        ActionPresentation => PublishPresentationAsync(actor, payload, cancellationToken),
        ActionInsuranceScheme => PublishInsuranceSchemeAsync(actor, payload, cancellationToken),
        ActionInsuranceState => ChangeInsuranceStateAsync(actor, payload, cancellationToken),
        ActionIntervention => StartInterventionAsync(actor, payload, cancellationToken),
        ActionAtmNetwork => ApplyAtmNetworkAsync(actor, payload, cancellationToken),
        ActionAtmTerminal => ApplyAtmTerminalAsync(actor, payload, cancellationToken),
        ActionAtmService => ApplyAtmServiceAsync(actor, payload, cancellationToken),
        ActionAtmCassette => ApplyAtmCassetteAsync(actor, payload, cancellationToken),
        ActionDenomination => ApplyDenominationAsync(actor, payload, cancellationToken),
        ActionCashConversion => ApplyCashConversionAsync(actor, payload, cancellationToken),
        ActionMerchantProduct or ActionMerchantPrice or ActionMerchantStock =>
            ApplyMerchantAsync(actor, payload, cancellationToken),
        ActionOperatorGrant => ApplyOperatorGrantAsync(actor, payload, cancellationToken),
        ActionFeeSchedule => ApplyFeeScheduleAsync(actor, payload, cancellationToken),
        ActionAccountReview => ApplyAccountReviewAsync(actor, payload, cancellationToken),
        ActionBankDesign => ApplyBankDesignAsync(actor, payload, cancellationToken),
        ActionInsuranceFund => CreateInsuranceFundAsync(actor, payload, cancellationToken),
        ActionResolution => AdvanceResolutionAsync(actor, payload, cancellationToken),
        _ => Task.FromResult(Result.Failure(
            ErrorCategory.Validation, BankingErrorCodes.ManagementActionUnknown)),
    };

    private async Task<Result> ApplyCalendarAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action == ActionCalendarClear)
        {
            return await calendars
                .ClearDateOverrideAsync(
                    new ClearBusinessCalendarDateCommand(actor, Field(payload, FieldDate)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

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

    private async Task<string> CurrentAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Task<string?> lookup = CurrentGroupOf(payload.Action) switch
        {
            PanelCurrentGroup.Calendar => CalendarCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Governance => GovernanceCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Policy => PolicyCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Atm => AtmCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Merchant => MerchantCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Bank => BankCurrentAsync(actor, payload, cancellationToken),
            PanelCurrentGroup.Resolution => ResolutionCurrentAsync(actor, payload, cancellationToken),
            _ => Task.FromResult<string?>(null),
        };

        return await lookup.ConfigureAwait(false)
            ?? catalog.Resolve(ViewKeys.PanelCurrentUnavailable);
    }

    internal static PanelCurrentGroup CurrentGroupOf(string action) => action switch
    {
        ActionCalendarSet or ActionCalendarClear => PanelCurrentGroup.Calendar,
        ActionTrustPolicy or ActionNetworkPolicy or ActionNetworkState or ActionPrudentialPolicy =>
            PanelCurrentGroup.Governance,
        ActionPresentation or ActionInsuranceScheme or ActionInsuranceState or ActionIntervention =>
            PanelCurrentGroup.Policy,
        ActionAtmNetwork or ActionAtmTerminal or ActionAtmService or ActionAtmCassette
            or ActionDenomination or ActionCashConversion => PanelCurrentGroup.Atm,
        ActionMerchantProduct or ActionMerchantPrice or ActionMerchantStock =>
            PanelCurrentGroup.Merchant,
        ActionAccountReview or ActionBankDesign or ActionOperatorGrant or ActionFeeSchedule =>
            PanelCurrentGroup.Bank,
        ActionInsuranceFund or ActionResolution => PanelCurrentGroup.Resolution,
        _ => PanelCurrentGroup.None,
    };

    private async Task<string?> CalendarCurrentAsync(
        AuthorizationContext actor,
        ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<BusinessCalendarDateStatusView> status = await calendars
            .GetDateStatusAsync(
                new GetBusinessCalendarDateQuery(actor, Field(payload, FieldDate)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!status.IsSuccess)
        {
            return null;
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
