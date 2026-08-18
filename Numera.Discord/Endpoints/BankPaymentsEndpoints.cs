using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed partial class BankPaymentsEndpoints : IEconomyEndpoint
{
    private const string ViewBeneficiaries = "beneficiaries";
    private const string ViewScheduled = "scheduled";
    private const string ViewMandates = "mandates";

    private readonly IPaymentManagementApplicationService payments;
    private readonly ICustomerAccountApplicationService customers;
    private readonly ITextCatalog catalog;

    public BankPaymentsEndpoints(
        IPaymentManagementApplicationService payments,
        ICustomerAccountApplicationService customers,
        ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(catalog);

        this.payments = payments;
        this.customers = customers;
        this.catalog = catalog;
    }

    [EconomySlashCommand("payments", "登録した振込先と予約振込と口座振替を確認します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> PaymentsAsync(
        DiscordEndpointContext context,
        [EconomyOption("view", "表示する一覧を選びます。", false)]
        [EconomyChoice("登録した振込先", ViewBeneficiaries)]
        [EconomyChoice("予約振込", ViewScheduled)]
        [EconomyChoice("口座振替", ViewMandates)]
        string? view,
        [EconomyOption("cursor", "次のページの位置を指定します。", false)] string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await customers
            .GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(context.UserId), cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        return (view ?? ViewBeneficiaries) switch
        {
            ViewScheduled => await ScheduledAsync(customer.Value.Id, cursor, cancellationToken)
                .ConfigureAwait(false),
            ViewMandates => await MandatesAsync(customer.Value.Id, cursor, cancellationToken)
                .ConfigureAwait(false),
            _ => await BeneficiariesAsync(customer.Value.Id, cursor, cancellationToken)
                .ConfigureAwait(false),
        };
    }

    private async Task<DiscordEndpointResponse> BeneficiariesAsync(
        CustomerAccountId customerAccountId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        Result<BeneficiaryPageView> result = await payments
            .ListBeneficiariesAsync(
                new ListBeneficiariesQuery(customerAccountId, cursor), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Items.Count == 0
            ? Empty(ViewKeys.PaymentsBeneficiariesEmpty)
            : Page(
                ViewKeys.PaymentsBeneficiaries,
                result.Value.Items.Count,
                result.Value.NextCursor,
                [
                    .. result.Value.Items.Select(item =>
                        $"{item.DisplayName} {item.InstitutionCode} {item.AccountNumberSuffix} "
                        + Status(item.Status.ToToken())),
                ]);
    }

    private async Task<DiscordEndpointResponse> ScheduledAsync(
        CustomerAccountId customerAccountId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        Result<ScheduledPaymentPageView> result = await payments
            .ListScheduledPaymentsAsync(
                new ListScheduledPaymentsQuery(customerAccountId, cursor), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Items.Count == 0
            ? Empty(ViewKeys.PaymentsScheduledEmpty)
            : Page(
                ViewKeys.PaymentsScheduled,
                result.Value.Items.Count,
                result.Value.NextCursor,
                [
                    .. result.Value.Items.Select(item =>
                        $"{Kind(item.Kind.ToToken())} {item.Amount.Value} "
                        + Status(item.Status.ToToken())),
                ]);
    }

    private async Task<DiscordEndpointResponse> MandatesAsync(
        CustomerAccountId customerAccountId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        Result<DirectDebitMandatePageView> result = await payments
            .ListDirectDebitMandatesAsync(
                new ListDirectDebitMandatesQuery(customerAccountId, cursor), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return result.Value.Items.Count == 0
            ? Empty(ViewKeys.PaymentsMandatesEmpty)
            : Page(
                ViewKeys.PaymentsMandates,
                result.Value.Items.Count,
                result.Value.NextCursor,
                [
                    .. result.Value.Items.Select(item =>
                        $"{item.SingleCollectionLimit.Value} {Status(item.Status.ToToken())}"),
                ]);
    }

    private static DiscordEndpointResponse Empty(string viewKey) =>
        DiscordEndpointResponse.Message(viewKey, new Dictionary<string, string>(StringComparer.Ordinal));

    private static DiscordEndpointResponse Page(
        string viewKey,
        int count,
        string? nextCursor,
        IReadOnlyList<string> lines) =>
        DiscordEndpointResponse.Message(
            viewKey,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
                ["items"] = string.Join('\n', lines),
                ["cursor"] = nextCursor ?? string.Empty,
            });

    private string Status(string token) => catalog.Resolve(ViewKeys.StatusOf(token));

    private string Kind(string token) => catalog.Resolve(ViewKeys.ScheduledPaymentKindOf(token));
}
