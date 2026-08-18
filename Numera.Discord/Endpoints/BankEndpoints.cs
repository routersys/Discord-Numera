using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed class BankEndpoints : IEconomyEndpoint
{
    private readonly IBankAccountApplicationService accounts;
    private readonly IPaymentApplicationService payments;
    private readonly ICustomerAccountApplicationService customers;

    public BankEndpoints(
        IBankAccountApplicationService accounts,
        IPaymentApplicationService payments,
        ICustomerAccountApplicationService customers)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(customers);

        this.accounts = accounts;
        this.payments = payments;
        this.customers = customers;
    }

    [EconomySlashCommand("open", "銀行口座を開設します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> OpenAsync(
        DiscordEndpointContext context,
        [EconomyOption("bank", "銀行を選びます。", true)] string bank,
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
        [EconomyOption("source-account", "送金元の口座番号を入力します。", true)] string sourceAccount,
        [EconomyOption("bank", "送金先の銀行を選びます。", true)] string bank,
        [EconomyOption("branch", "送金先の支店番号を入力します。", true)] string branch,
        [EconomyOption("account", "送金先の口座番号を入力します。", true)] string account,
        [EconomyOption("amount", "振込金額を入力します。", true)] long amount,
        [EconomyOption("memo", "摘要を入力します。", false)] string memo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await ResolveCustomerAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        if (!DepositAccountReference.TryParse(sourceAccount, out DepositAccountId sourceDepositAccountId))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<PaymentOrderView> result = await payments
            .CreatePaymentOrderAsync(
                new CreatePaymentOrderCommand(
                    context.GuildId,
                    customer.Value.Id,
                    sourceDepositAccountId,
                    bank,
                    branch,
                    account,
                    amount,
                    string.IsNullOrEmpty(memo) ? null : memo,
                    context.InteractionId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        return DiscordEndpointResponse.Message(
            result.Value.Status == PaymentOrderStatus.Completed
                ? ViewKeys.TransferCompleted
                : ViewKeys.TransferAccepted,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = result.Value.Amount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["fee"] = result.Value.FeeAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["availableBalance"] =
                    result.Value.SourceAvailableBalance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
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
