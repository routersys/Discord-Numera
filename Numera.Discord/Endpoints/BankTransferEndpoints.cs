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

public sealed partial class BankEndpoints
{
    [EconomyComponent(EconomyComponentKind.Select, TransferFlow.SourceAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> SelectTransferSourceAsync(
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
                Request(context, input.SessionToken, scope, TransferFlow.SourceSelectState, 0L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        TransferPayload payload = TransferPayloadCodec.Read(current.Value.PayloadJson);

        if (Selected(payload, input.Values) is not { } candidate)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, input.SessionToken, scope, TransferFlow.SourceSelectState, 0L),
                TransferFlow.InputState,
                TransferPayloadCodec.Write(payload with { SourceDepositAccountId = candidate.DepositAccountId }),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.TransferInput,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceAccount"] = Describe(candidate),
            },
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                null,
                [
                    new DiscordResponseButton(
                        DiscordCustomId.Button(TransferFlow.InputAction, input.SessionToken),
                        ViewKeys.TransferInputLabel,
                        DiscordButtonStyle.Primary),
                ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, TransferFlow.InputAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> OpenTransferInputAsync(
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
                Request(context, input.SessionToken, scope, TransferFlow.InputState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        return current.IsSuccess
            ? DiscordEndpointResponse.Modal(
                ViewKeys.TransferModal,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customId"] = DiscordCustomId.Modal(TransferFlow.ModalAction, input.SessionToken),
                })
            : EndpointFailures.From(current.Error!);
    }

    [EconomyModal(TransferFlow.ModalAction, typeof(TransferForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> SubmitTransferAsync(
        DiscordEndpointContext context,
        TransferForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (!long.TryParse(form.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out long amount)
            || amount <= 0L)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.AmountInvalid);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, context.SessionToken, scope, TransferFlow.InputState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        TransferPayload payload = TransferPayloadCodec.Read(current.Value.PayloadJson) with
        {
            InstitutionCode = form.BankCode,
            BranchCode = form.BranchCode,
            AccountNumber = form.AccountNumber,
            AmountMinor = amount,
            Memo = form.Memo,
        };

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                Request(context, context.SessionToken, scope, TransferFlow.InputState, 1L),
                TransferFlow.ConfirmState,
                TransferPayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.TransferConfirm,
            Confirmation(payload),
            new DiscordResponseBody(
                [
                    Field(ViewKeys.FieldSource),
                    Field(ViewKeys.FieldBank),
                    Field(ViewKeys.FieldBranch),
                    Field(ViewKeys.FieldAccount),
                    Field(ViewKeys.FieldAmount),
                    Field(ViewKeys.FieldFee),
                    Field(ViewKeys.FieldTotal),
                ],
                new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(TransferFlow.ExecuteAction, context.SessionToken),
                            ViewKeys.TransferExecuteLabel,
                            DiscordButtonStyle.Primary),
                    ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, TransferFlow.ExecuteAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> ExecuteTransferAsync(
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

        Result<CustomerAccountStatusView> customer = await ResolveCustomerAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                Request(context, input.SessionToken, scope, TransferFlow.ConfirmState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        TransferPayload payload = TransferPayloadCodec.Read(current.Value.PayloadJson);

        if (!EntityIdValue.TryParse(payload.SourceDepositAccountId, out EntityIdValue source))
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<PaymentOrderView> result = await payments
            .CreatePaymentOrderAsync(
                new CreatePaymentOrderCommand(
                    context.GuildId,
                    customer.Value.Id,
                    DepositAccountId.FromValue(source),
                    payload.InstitutionCode,
                    payload.BranchCode,
                    payload.AccountNumber,
                    payload.AmountMinor,
                    string.IsNullOrEmpty(payload.Memo) ? null : payload.Memo,
                    current.Value.Id.Value.ToString()),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                Request(context, input.SessionToken, scope, TransferFlow.ConfirmState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.UpdateMessage(
            result.Value.Status == PaymentOrderStatus.Completed
                ? ViewKeys.TransferCompleted
                : ViewKeys.TransferAccepted,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = Text(result.Value.Amount),
                ["fee"] = Text(result.Value.FeeAmount),
                ["availableBalance"] = Text(result.Value.SourceAvailableBalance),
            });
    }

    private static DiscordResponseField Field(string field) =>
        new(
            ViewKeys.FieldLabel(ViewKeys.TransferConfirm, field),
            ViewKeys.FieldValue(ViewKeys.TransferConfirm, field));

    private static ConsumeInteractionSessionRequest Request(
        DiscordEndpointContext context,
        string sessionToken,
        EconomyScopeId scope,
        string state,
        long stateVersion) =>
        new(sessionToken, context.UserId, context.GuildId, scope, state, stateVersion);

    private static TransferCandidate? Selected(TransferPayload payload, IReadOnlyList<string> values)
    {
        if (values.Count != 1 || TransferPayloadCodec.TokenOf(values[0]) is not { } token)
        {
            return null;
        }

        foreach (TransferCandidate candidate in payload.Candidates)
        {
            if (string.Equals(candidate.Token, token, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Dictionary<string, string> Confirmation(TransferPayload payload) =>
        new(StringComparer.Ordinal)
        {
            ["sourceAccount"] = Describe(payload),
            ["institutionCode"] = payload.InstitutionCode,
            ["branchCode"] = payload.BranchCode,
            ["accountNumberSuffix"] = Suffix(payload.AccountNumber),
            ["amount"] = payload.AmountMinor.ToString(CultureInfo.InvariantCulture),
        };

    private static string Describe(TransferCandidate candidate) =>
        candidate.InstitutionCode + " " + candidate.AccountNumberSuffix;

    private static string Describe(TransferPayload payload)
    {
        foreach (TransferCandidate candidate in payload.Candidates)
        {
            if (string.Equals(candidate.DepositAccountId, payload.SourceDepositAccountId, StringComparison.Ordinal))
            {
                return Describe(candidate);
            }
        }

        return payload.SourceDepositAccountId;
    }

    private static string Text(MoneyMinor amount) =>
        amount.Value.ToString(CultureInfo.InvariantCulture);
}
