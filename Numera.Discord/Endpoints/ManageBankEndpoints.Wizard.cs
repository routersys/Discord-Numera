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
    internal const bool DefaultOpeningEnabled = true;
    internal const int DefaultMinimumAgeDays = 0;
    internal const long DefaultMinimumInitialFunding = 0L;
    internal const bool DefaultManualApproval = false;
    internal const bool DefaultReopenAllowed = true;
    internal const bool DefaultPublicReceiving = true;

    [EconomyComponent(EconomyComponentKind.Button, BankCreateFlow.InputAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> OpenBankCreateInputAsync(
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
                Request(context, input.SessionToken, scope, BankCreateFlow.IdentityState, 0L),
                cancellationToken)
            .ConfigureAwait(false);

        return current.IsSuccess
            ? DiscordEndpointResponse.Modal(
                ViewKeys.ManageBankCreateModal,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customId"] = DiscordCustomId.Modal(BankCreateFlow.ModalAction, input.SessionToken),
                })
            : EndpointFailures.From(current.Error!);
    }

    [EconomyModal(BankCreateFlow.ModalAction, typeof(BankCreateForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> SubmitBankCreateAsync(
        DiscordEndpointContext context,
        BankCreateForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, context.SessionToken, scope, BankCreateFlow.IdentityState, 0L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankCreatePayload payload = BankCreatePayloadCodec.Read(current.Value.PayloadJson) with
        {
            BankName = form.BankName,
            BranchCode = form.BranchCode,
            BranchName = form.BranchName,
            ProductCode = form.ProductCode,
            ProductName = form.ProductName,
        };

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, context.SessionToken, scope, BankCreateFlow.IdentityState, 0L),
                BankCreateFlow.ReviewState,
                BankCreatePayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.ManageBankCreateReview,
            Review(payload),
            new DiscordResponseBody(
                [
                    ReviewField(ViewKeys.FieldInstitution),
                    ReviewField(ViewKeys.FieldBankName),
                    ReviewField(ViewKeys.FieldBranch),
                    ReviewField(ViewKeys.FieldProduct),
                    ReviewField(ViewKeys.FieldOpeningPolicy),
                ],
                new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(BankCreateFlow.CommitAction, context.SessionToken),
                            ViewKeys.ManageBankCreateCommitLabel,
                            DiscordButtonStyle.Primary),
                    ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, BankCreateFlow.CommitAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    internal async Task<DiscordEndpointResponse> CommitBankCreateAsync(
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
                Request(context, input.SessionToken, scope, BankCreateFlow.ReviewState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankCreatePayload payload = BankCreatePayloadCodec.Read(current.Value.PayloadJson);

        if (!EntityIdValue.TryParse(payload.CentralBankAccountingBookId, out EntityIdValue book))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CentralBankBookUnavailable);
        }

        Result<BankView> result = await banks
            .CommitCreateBankAsync(
                new CommitCreateBankCommand(
                    EndpointAuthorization.ToActor(context),
                    payload.InstitutionCode,
                    payload.BankName,
                    payload.BranchCode,
                    payload.BranchName,
                    payload.ProductCode,
                    payload.ProductName,
                    DefaultOpeningEnabled,
                    DefaultMinimumAgeDays,
                    DefaultMinimumInitialFunding,
                    DefaultManualApproval,
                    DefaultReopenAllowed,
                    DefaultPublicReceiving,
                    SettlementParticipationMode.Direct,
                    SettlementAgentInstitutionCode: null,
                    AccountingBookId.FromValue(book)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                Request(context, input.SessionToken, scope, BankCreateFlow.ReviewState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.ManageBankCreated,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = result.Value.InstitutionCode,
                ["bankName"] = result.Value.Name,
            });
    }

    private static Dictionary<string, string> Review(BankCreatePayload payload) =>
        new(StringComparer.Ordinal)
        {
            ["institutionCode"] = payload.InstitutionCode,
            ["bankName"] = payload.BankName,
            ["branchCode"] = payload.BranchCode,
            ["branchName"] = payload.BranchName,
            ["productCode"] = payload.ProductCode,
            ["productName"] = payload.ProductName,
        };

    private static DiscordResponseField ReviewField(string field) =>
        new(
            ViewKeys.FieldLabel(ViewKeys.ManageBankCreateReview, field),
            ViewKeys.FieldValue(ViewKeys.ManageBankCreateReview, field));

    private static ConsumeInteractionSessionRequest Request(
        DiscordEndpointContext context,
        string sessionToken,
        EconomyScopeId scope,
        string state,
        long stateVersion) =>
        new(sessionToken, context.UserId, context.GuildId, scope, state, stateVersion);
}
