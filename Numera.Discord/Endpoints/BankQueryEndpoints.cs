using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed partial class BankQueryEndpoints : IEconomyEndpoint
{
    private const string Separator = " / ";

    private readonly IBankQueryApplicationService queries;
    private readonly ICustomerAccountApplicationService customers;
    private readonly ILoanApplicationService loans;
    private readonly ITextCatalog catalog;
    private readonly Sessions.InteractionSessionService sessions;

    public BankQueryEndpoints(
        IBankQueryApplicationService queries,
        ICustomerAccountApplicationService customers,
        ILoanApplicationService loans,
        ITextCatalog catalog,
        Sessions.InteractionSessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(loans);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);

        this.queries = queries;
        this.customers = customers;
        this.loans = loans;
        this.catalog = catalog;
        this.sessions = sessions;
    }

    [EconomySlashCommand("list", "利用できる銀行の一覧を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> ListAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<BankPageView> result = await queries
            .ListBanksAsync(new ListBanksQuery(context.GuildId, null), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        Dictionary<string, string> data = new(StringComparer.Ordinal)
        {
            ["items"] = string.Join(
                Separator,
                result.Value.Items.Select(item =>
                    $"{item.InstitutionCode} {item.Name} {Status(item.Status.ToToken())}")),
            ["count"] = result.Value.Items.Count.ToString(CultureInfo.InvariantCulture),
        };

        return result.Value.Items.Count == 0
            ? DiscordEndpointResponse.Message(ViewKeys.BankListEmpty, data)
            : await OpenBankDetailSelectionAsync(context, result.Value.Items, data, cancellationToken)
                .ConfigureAwait(false);
    }

    [EconomySlashCommand("accounts", "自分の口座の一覧を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> AccountsAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        Result<BankAccountPageView> result = await queries
            .ListCustomerBankAccountsAsync(
                new ListCustomerBankAccountsQuery(customer.Value.Id, null),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return DiscordEndpointResponse.Message(
            result.Value.Items.Count == 0 ? ViewKeys.BankAccountListEmpty : ViewKeys.BankAccountList,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["items"] = string.Join(
                    Separator,
                    result.Value.Items.Select(item =>
                        $"{item.InstitutionCode} *{item.AccountNumberSuffix} "
                        + $"{Status(item.Status.ToToken())} {item.AvailableBalance.Value}")),
                ["count"] = result.Value.Items.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    [EconomySlashCommand("statement", "口座の取引明細を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> StatementAsync(
        DiscordEndpointContext context,
        [EconomyOption("account", "口座を選びます。", true)] string account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        if (!DepositAccountReference.TryParse(account, out Numera.Domain.Common.DepositAccountId id))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<AccountStatementPageView> result = await queries
            .GetAccountStatementAsync(
                new GetAccountStatementQuery(customer.Value.Id, id, null),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return DiscordEndpointResponse.Message(
            result.Value.Items.Count == 0 ? ViewKeys.StatementEmpty : ViewKeys.Statement,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["items"] = string.Join(
                    Separator,
                    result.Value.Items.Select(static item =>
                        $"{item.PostedAt} {item.DescriptionCode} {item.Amount.Value}")),
                ["count"] = result.Value.Items.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    private string Status(string token) => catalog.Resolve(ViewKeys.StatusOf(token));

    private Task<Result<CustomerAccountStatusView>> ResolveAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken) =>
        customers.GetCustomerAccountStatusAsync(
            new GetCustomerAccountStatusQuery(context.UserId),
            cancellationToken);
}
