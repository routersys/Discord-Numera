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

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed partial class BankEndpoints : IEconomyEndpoint
{
    private readonly IBankAccountApplicationService accounts;
    private readonly IPaymentApplicationService payments;
    private readonly ICustomerAccountApplicationService customers;
    private readonly IBankQueryApplicationService queries;
    private readonly InteractionSessionService sessions;

    public BankEndpoints(
        IBankAccountApplicationService accounts,
        IPaymentApplicationService payments,
        ICustomerAccountApplicationService customers,
        IBankQueryApplicationService queries,
        InteractionSessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(sessions);

        this.accounts = accounts;
        this.payments = payments;
        this.customers = customers;
        this.queries = queries;
        this.sessions = sessions;
    }

    [EconomySlashCommand("open", "銀行口座を開設します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> OpenAsync(
        DiscordEndpointContext context,
        [EconomyOption("bank", "銀行を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.BankProviderKey)]
        string bank,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveCustomerAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<AccountOpeningView> result = await accounts
            .OpenDepositAccountAsync(
                new OpenDepositAccountCommand(context.GuildId, customer.Value.Id, bank),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return DiscordEndpointResponse.Message(
            result.Value.Status == DepositAccountStatus.Active
                ? ViewKeys.BankAccountOpened
                : ViewKeys.BankAccountSubmitted,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = result.Value.InstitutionCode,
                ["accountNumberSuffix"] = Suffix(result.Value.AccountNumber),
            });
    }

    [EconomySlashCommand("transfer", "他の口座へ振り込みます。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> TransferAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

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

        Result<BankAccountPageView> accounts = await queries
            .ListCustomerBankAccountsAsync(
                new ListCustomerBankAccountsQuery(customer.Value.Id, null),
                cancellationToken)
            .ConfigureAwait(false);

        if (!accounts.IsSuccess)
        {
            return EndpointFailures.From(accounts.Error!);
        }

        TransferCandidate[] candidates = Candidates(accounts.Value.Items);

        if (candidates.Length == 0)
        {
            return DiscordEndpointResponse.Message(
                ViewKeys.TransferSourceEmpty,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        Result<InteractionSessionTicket> ticket = await sessions
            .OpenAsync(
                new OpenInteractionSessionRequest(
                    context.UserId,
                    context.GuildId,
                    scope,
                    TransferFlow.FlowType,
                    TransferFlow.SourceSelectState,
                    TransferPayloadCodec.Write(TransferPayloadCodec.Empty with { Candidates = candidates })),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ticket.IsSuccess)
        {
            return EndpointFailures.From(ticket.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.TransferSource,
            new Dictionary<string, string>(StringComparer.Ordinal),
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                new DiscordResponseSelect(
                    DiscordCustomId.Select(TransferFlow.SourceAction, ticket.Value.RawToken),
                    ViewKeys.TransferSourcePlaceholder,
                    [
                        .. candidates.Select(static candidate => new DiscordResponseSelectOption(
                            Describe(candidate),
                            TransferPayloadCodec.OptionValue(candidate.Token))),
                    ]),
                [])));
    }

    private static TransferCandidate[] Candidates(IReadOnlyList<BankAccountItem> items)
    {
        List<TransferCandidate> candidates = [];

        foreach (BankAccountItem item in items)
        {
            if (item.Status != DepositAccountStatus.Active
                || candidates.Count == DiscordResponseSelect.MaximumOptionCount)
            {
                continue;
            }

            candidates.Add(new TransferCandidate(
                candidates.Count.ToString(CultureInfo.InvariantCulture),
                item.DepositAccountId.Value.ToString(),
                item.InstitutionCode,
                item.AccountNumberSuffix));
        }

        return [.. candidates];
    }

    [EconomySlashCommand("close", "口座の解約を申し込みます。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> CloseAsync(
        DiscordEndpointContext context,
        [EconomyOption("account", "解約する口座を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.DepositAccountProviderKey)]
        string account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveCustomerAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        if (!DepositAccountReference.TryParse(account, out Numera.Domain.Common.DepositAccountId id))
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result result = await accounts
            .CloseDepositAccountAsync(
                new CloseDepositAccountCommand(customer.Value.Id, id),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.BankAccountClosing,
                new Dictionary<string, string>(StringComparer.Ordinal))
            : EndpointFailures.From(result.Error!);
    }
    private Task<Result<CustomerAccountStatusView>> ResolveCustomerAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken) =>
        customers.GetCustomerAccountStatusAsync(
            new GetCustomerAccountStatusQuery(context.UserId),
            cancellationToken);

    private static string Suffix(string accountNumber) =>
        accountNumber.Length >= AccountNumber.SuffixLength
            ? accountNumber[^AccountNumber.SuffixLength..]
            : accountNumber;
}

internal static class DepositAccountReference
{
    internal static string Format(DepositAccountId id) =>
        new Guid(id.Value.ToByteArray(), bigEndian: true).ToString();

    internal static bool TryParse(string text, out DepositAccountId id)
    {
        if (Guid.TryParse(text, out Guid parsed))
        {
            id = DepositAccountId.FromValue(EntityIdValue.FromBytes(parsed.ToByteArray(bigEndian: true)));
            return true;
        }

        id = default;
        return false;
    }
}
