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

public sealed partial class BankQueryEndpoints
{
    private async Task<DiscordEndpointResponse> OpenBankDetailSelectionAsync(
        DiscordEndpointContext context,
        IReadOnlyList<BankListItem> banks,
        Dictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Result<InteractionSessionTicket> ticket = await sessions
            .OpenAsync(
                new OpenInteractionSessionRequest(
                    context.UserId,
                    context.GuildId,
                    scope,
                    BankDetailFlow.FlowType,
                    BankDetailFlow.SelectState,
                    BankDetailPayloadCodec.Write(BankDetailPayloadCodec.Empty)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ticket.IsSuccess)
        {
            return EndpointFailures.From(ticket.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.BankList,
            data,
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                new DiscordResponseSelect(
                    DiscordCustomId.Select(BankDetailFlow.SelectAction, ticket.Value.RawToken),
                    ViewKeys.BankDetailPlaceholder,
                    [
                        .. banks.Select(item => new DiscordResponseSelectOption(
                            item.InstitutionCode + " " + item.Name, item.InstitutionCode)),
                    ]),
                [])));
    }

    [EconomyComponent(EconomyComponentKind.Select, BankDetailFlow.SelectAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> SelectBankDetailAsync(
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

        if (input.Values.Count != 1)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.BankNotFound);
        }

        string institutionCode = input.Values[0];

        Result<BankDetailView> detail = await queries
            .GetBankDetailAsync(new GetBankDetailQuery(context.GuildId, institutionCode), cancellationToken)
            .ConfigureAwait(false);

        if (!detail.IsSuccess)
        {
            return EndpointFailures.From(detail.Error!);
        }

        Result<BankProductPageView> products = await queries
            .ListBankProductsAsync(
                new ListBankProductsQuery(context.GuildId, institutionCode, null), cancellationToken)
            .ConfigureAwait(false);

        if (!products.IsSuccess)
        {
            return EndpointFailures.From(products.Error!);
        }

        Result<LoanProductPageView> loanProducts = await loans
            .GetLoanProductsAsync(
                new GetLoanProductsQuery(context.GuildId, institutionCode), cancellationToken)
            .ConfigureAwait(false);

        if (!loanProducts.IsSuccess)
        {
            return EndpointFailures.From(loanProducts.Error!);
        }

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                DetailRequest(context, input.SessionToken, scope, BankDetailFlow.SelectState, 0L),
                BankDetailFlow.DetailState,
                BankDetailPayloadCodec.Write(
                    BankDetailPayloadCodec.Empty with { InstitutionCode = institutionCode }),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        Dictionary<string, string> data = new(StringComparer.Ordinal)
        {
            ["institutionCode"] = detail.Value.InstitutionCode,
            ["bankName"] = detail.Value.Name,
            ["status"] = catalog.Resolve(ViewKeys.StatusOf(detail.Value.Status.ToToken())),
            ["products"] = string.Join(
                Separator,
                products.Value.Items.Select(static item => item.ProductCode + " " + item.Name)),
            ["loanProducts"] = string.Join(
                Separator,
                loanProducts.Value.Items.Select(static item =>
                    item.ProductCode + " "
                    + item.AnnualRatePpt.ToString(CultureInfo.InvariantCulture))),
        };

        return detail.Value.Status == BankStatus.Operating
            ? DiscordEndpointResponse.UpdateMessage(
                ViewKeys.BankDetail,
                data,
                DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(BankDetailFlow.LoanInputAction, input.SessionToken),
                            ViewKeys.BankLoanInputLabel,
                            DiscordButtonStyle.Primary),
                    ])))
            : DiscordEndpointResponse.UpdateMessage(ViewKeys.BankDetail, data);
    }

    [EconomyComponent(EconomyComponentKind.Button, BankDetailFlow.LoanInputAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> OpenBankLoanInputAsync(
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
                DetailRequest(context, input.SessionToken, scope, BankDetailFlow.DetailState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        return current.IsSuccess
            ? DiscordEndpointResponse.Modal(
                ViewKeys.BankLoanModal,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customId"] = DiscordCustomId.Modal(
                        BankDetailFlow.LoanModalAction, input.SessionToken),
                })
            : EndpointFailures.From(current.Error!);
    }

    [EconomyModal(BankDetailFlow.LoanModalAction, typeof(BankLoanForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> SubmitBankLoanAsync(
        DiscordEndpointContext context,
        BankLoanForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (!long.TryParse(
                form.Principal, NumberStyles.None, CultureInfo.InvariantCulture, out long principal) ||
            principal <= 0)
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.LoanPrincipalInvalid);
        }

        Result<InteractionSessionSnapshot> current = await sessions
            .ConsumeAsync(
                DetailRequest(context, context.SessionToken, scope, BankDetailFlow.DetailState, 1L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankDetailPayload payload = BankDetailPayloadCodec.Read(current.Value.PayloadJson) with
        {
            ProductCode = form.ProductCode,
            PrincipalMinor = principal,
        };

        Result<InteractionSessionSnapshot> advanced = await sessions
            .AdvanceAsync(
                DetailRequest(context, context.SessionToken, scope, BankDetailFlow.DetailState, 1L),
                BankDetailFlow.ReviewState,
                BankDetailPayloadCodec.Write(payload),
                cancellationToken)
            .ConfigureAwait(false);

        if (!advanced.IsSuccess)
        {
            return EndpointFailures.From(advanced.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.BankLoanReview,
            LoanReview(payload),
            new DiscordResponseBody(
                [
                    LoanField(ViewKeys.FieldInstitution),
                    LoanField(ViewKeys.FieldLoanPrincipal),
                    LoanField(ViewKeys.FieldLoanProduct),
                ],
                new DiscordResponseComponents(
                    null,
                    [
                        new DiscordResponseButton(
                            DiscordCustomId.Button(
                                BankDetailFlow.LoanCommitAction, context.SessionToken),
                            ViewKeys.BankLoanCommitLabel,
                            DiscordButtonStyle.Primary),
                    ])));
    }

    [EconomyComponent(EconomyComponentKind.Button, BankDetailFlow.LoanCommitAction)]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    internal async Task<DiscordEndpointResponse> CommitBankLoanAsync(
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
                DetailRequest(context, input.SessionToken, scope, BankDetailFlow.ReviewState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        if (!current.IsSuccess)
        {
            return EndpointFailures.From(current.Error!);
        }

        BankDetailPayload payload = BankDetailPayloadCodec.Read(current.Value.PayloadJson);

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<BankAccountPageView> accounts = await queries
            .ListCustomerBankAccountsAsync(
                new ListCustomerBankAccountsQuery(customer.Value.Id, null), cancellationToken)
            .ConfigureAwait(false);

        if (!accounts.IsSuccess)
        {
            return EndpointFailures.From(accounts.Error!);
        }

        if (accounts.Value.Items.FirstOrDefault(item => string.Equals(
                item.InstitutionCode, payload.InstitutionCode, StringComparison.Ordinal))
            is not { } destination)
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<LoanApplicationView> loan = await loans
            .ApplyLoanAsync(
                new ApplyLoanCommand(
                    customer.Value.Id,
                    destination.DepositAccountId,
                    payload.InstitutionCode,
                    payload.ProductCode,
                    payload.PrincipalMinor),
                cancellationToken)
            .ConfigureAwait(false);

        if (!loan.IsSuccess)
        {
            return EndpointFailures.From(loan.Error!);
        }

        _ = await sessions
            .CompleteAsync(
                DetailRequest(context, input.SessionToken, scope, BankDetailFlow.ReviewState, 2L),
                cancellationToken)
            .ConfigureAwait(false);

        return DiscordEndpointResponse.UpdateMessage(
            ViewKeys.BankLoanOriginated,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = payload.InstitutionCode,
                ["principal"] = loan.Value.Principal.Value.ToString(CultureInfo.InvariantCulture),
                ["status"] = catalog.Resolve(ViewKeys.StatusOf(loan.Value.Status.ToToken())),
            });
    }

    private static Dictionary<string, string> LoanReview(BankDetailPayload payload) =>
        new(StringComparer.Ordinal)
        {
            ["institutionCode"] = payload.InstitutionCode,
            ["principal"] = payload.PrincipalMinor.ToString(CultureInfo.InvariantCulture),
            ["productCode"] = payload.ProductCode,
        };

    private static DiscordResponseField LoanField(string field) =>
        new(
            ViewKeys.FieldLabel(ViewKeys.BankLoanReview, field),
            ViewKeys.FieldValue(ViewKeys.BankLoanReview, field));

    private static ConsumeInteractionSessionRequest DetailRequest(
        DiscordEndpointContext context,
        string sessionToken,
        EconomyScopeId scope,
        string state,
        long stateVersion) =>
        new(sessionToken, context.UserId, context.GuildId, scope, state, stateVersion);
}
