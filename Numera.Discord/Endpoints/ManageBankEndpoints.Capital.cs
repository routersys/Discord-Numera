using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Discord.Sessions;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

public sealed partial class ManageBankEndpoints
{
    [EconomyComponent(EconomyComponentKind.Button, BankCapitalFlow.InputAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> OpenBankCapitalInputAsync(
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
                Request(context, input.SessionToken, scope, BankCapitalFlow.CapitalState, 0L),
                cancellationToken)
            .ConfigureAwait(false);

        return current.IsSuccess
            ? DiscordEndpointResponse.Modal(
                ViewKeys.ManageBankCapitalModal,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customId"] = DiscordCustomId.Modal(BankCapitalFlow.ModalAction, input.SessionToken),
                })
            : EndpointFailures.From(current.Error!);
    }

    [EconomyModal(BankCapitalFlow.ModalAction, typeof(BankCapitalForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> SubmitBankCapitalAsync(
        DiscordEndpointContext context,
        BankCapitalForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (!long.TryParse(
                form.Amount, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture,
                out long amount) ||
            amount <= 0)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.AmountInvalid);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, context.SessionToken, scope, BankCapitalFlow.CapitalState, 0L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankCapitalPayload payload = BankCapitalPayloadCodec.Read(current.Value.PayloadJson) with
        {
            AmountMinor = amount,
            SourceInstitutionCode = form.SourceInstitutionCode ?? string.Empty,
        };

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, context.SessionToken, scope, BankCapitalFlow.CapitalState, 0L),
                BankCapitalFlow.ReviewState,
                BankCapitalPayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.ManageBankCapitalReview,
            CapitalReview(payload),
            new DiscordResponseBody(
                [
                    CapitalField(ViewKeys.FieldInstitution),
                    CapitalField(ViewKeys.FieldCapitalAmount),
                    CapitalField(ViewKeys.FieldCapitalSource),
                ],
                new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(BankCapitalFlow.CommitAction, context.SessionToken),
                            ViewKeys.ManageBankCapitalCommitLabel,
                            DiscordButtonStyle.Primary),
                    ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, BankCapitalFlow.CommitAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> CommitBankCapitalAsync(
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
                Request(context, input.SessionToken, scope, BankCapitalFlow.ReviewState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankCapitalPayload payload = BankCapitalPayloadCodec.Read(current.Value.PayloadJson);

        Result<BankCapitalView> contributed = await banks
            .ContributeBankCapitalAsync(
                new ContributeBankCapitalCommand(
                    EndpointAuthorization.ToActor(context),
                    payload.InstitutionCode,
                    string.IsNullOrWhiteSpace(payload.SourceInstitutionCode)
                        ? null
                        : payload.SourceInstitutionCode,
                    payload.AmountMinor,
                    current.Value.Id.Value.ToString()),
                cancellationToken)
            .ConfigureAwait(false);

        if (!contributed.IsSuccess)
        {
            return EndpointFailures.From(contributed.Error!);
        }

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, input.SessionToken, scope, BankCapitalFlow.ReviewState, 1L),
                BankCapitalFlow.ActivationState,
                BankCapitalPayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.ManageBankCapitalContributed,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = contributed.Value.InstitutionCode,
                ["amount"] = Minor(contributed.Value.ContributedAmount),
                ["paidIn"] = Minor(contributed.Value.PaidInCapital),
                ["minimum"] = Minor(contributed.Value.MinimumInitialCapital),
            },
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                null,
                [
                    new DiscordResponseButton(
                        DiscordCustomId.Button(BankCapitalFlow.ActivateAction, input.SessionToken),
                        ViewKeys.ManageBankActivateLabel,
                        DiscordButtonStyle.Primary),
                ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, BankCapitalFlow.ActivateAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> ActivateBankAsync(
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
                Request(context, input.SessionToken, scope, BankCapitalFlow.ActivationState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankCapitalPayload payload = BankCapitalPayloadCodec.Read(current.Value.PayloadJson);

        Result<BankView> activated = await banks
            .ActivateBankAsync(
                new ActivateBankCommand(
                    EndpointAuthorization.ToActor(context),
                    payload.InstitutionCode,
                    current.Value.Id.Value.ToString()),
                cancellationToken)
            .ConfigureAwait(false);

        if (!activated.IsSuccess)
        {
            return EndpointFailures.From(activated.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                Request(context, input.SessionToken, scope, BankCapitalFlow.ActivationState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.ManageBankActivated,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = activated.Value.InstitutionCode,
                ["bankName"] = activated.Value.Name,
                ["status"] = catalog.Resolve(ViewKeys.StatusOf(activated.Value.Status.ToToken())),
            });
    }

    private async Task<DiscordEndpointResponse> OpenCapitalStageAsync(
        DiscordEndpointContext context,
        EconomyScopeId scope,
        string institutionCode,
        string viewKey,
        Dictionary<string, string> data,
        bool replacesOriginalMessage,
        CancellationToken cancellationToken)
    {
        Result<InteractionSessionTicket> ticket = await sessions
            .OpenAsync(
                new OpenInteractionSessionRequest(
                    context.UserId,
                    context.GuildId,
                    scope,
                    BankCapitalFlow.FlowType,
                    BankCapitalFlow.CapitalState,
                    BankCapitalPayloadCodec.Write(
                        BankCapitalPayloadCodec.Empty with { InstitutionCode = institutionCode })),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ticket.IsSuccess)
        {
            return EndpointFailures.From(ticket.Error!);
        }

        DiscordResponseBody body = DiscordResponseBody.WithComponents(new DiscordResponseComponents(
            null,
            [
                new DiscordResponseButton(
                    DiscordCustomId.Button(BankCapitalFlow.InputAction, ticket.Value.RawToken),
                    ViewKeys.ManageBankCapitalInputLabel,
                    DiscordButtonStyle.Primary),
            ]));

        return replacesOriginalMessage
            ? DiscordEndpointResponse.UpdateMessage(viewKey, data, body)
            : DiscordEndpointResponse.Message(viewKey, data, body);
    }

    private Dictionary<string, string> CapitalReview(BankCapitalPayload payload) =>
        new(StringComparer.Ordinal)
        {
            ["institutionCode"] = payload.InstitutionCode,
            ["amount"] = payload.AmountMinor.ToString(CultureInfo.InvariantCulture),
            ["source"] = string.IsNullOrWhiteSpace(payload.SourceInstitutionCode)
                ? catalog.Resolve(ViewKeys.ManageBankCapitalIssuerLabel)
                : payload.SourceInstitutionCode,
        };

    private static DiscordResponseField CapitalField(string field) =>
        new(
            ViewKeys.FieldLabel(ViewKeys.ManageBankCapitalReview, field),
            ViewKeys.FieldValue(ViewKeys.ManageBankCapitalReview, field));

    private static string Minor(MoneyMinor amount) =>
        amount.Value.ToString(CultureInfo.InvariantCulture);
}
