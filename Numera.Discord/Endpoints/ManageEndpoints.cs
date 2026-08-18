using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Accounting;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed class ManageEndpoints : IEconomyEndpoint
{
    private readonly ICurrencyAdministrationApplicationService currencies;
    private readonly IBankAdministrationApplicationService banks;

    public ManageEndpoints(
        ICurrencyAdministrationApplicationService currencies,
        IBankAdministrationApplicationService banks)
    {
        ArgumentNullException.ThrowIfNull(currencies);
        ArgumentNullException.ThrowIfNull(banks);

        this.currencies = currencies;
        this.banks = banks;
    }

    [EconomySlashCommand("currency-create", "経済圏の通貨を作成します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> CreateCurrencyAsync(
        DiscordEndpointContext context,
        [EconomyOption("book", "発行元の会計帳簿を入力します。", true)] string book,
        [EconomyOption("name", "通貨名を入力します。", true)] string name,
        [EconomyOption("code", "通貨コードを入力します。", true)] string code,
        [EconomyOption("symbol", "通貨記号を入力します。", true)] string symbol,
        [EconomyOption("minor-unit-digits", "小数部の桁数を入力します。", true)] int minorUnitDigits,
        [EconomyOption("genesis", "初期発行量を入力します。", true)] long genesis,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!EntityReference.TryParse(book, out EntityIdValue bookId))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyIssuanceAccountUnavailable);
        }

        Result<CurrencyView> result = await currencies
            .CreateCurrencyAsync(
                new CreateCurrencyCommand(
                    EndpointAuthorization.ToActor(context),
                    AccountingBookId.FromValue(bookId),
                    name,
                    code,
                    symbol,
                    "{symbol}{amount}",
                    minorUnitDigits,
                    null,
                    genesis,
                    "GENESIS_MINT",
                    Token(context)),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.ManageCurrencyCreated,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["code"] = result.Value.Code,
                    ["baseMoneySupply"] = Text(result.Value.BaseMoneySupply),
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("currency-issue", "通貨を追加発行します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> IssueCurrencyAsync(
        DiscordEndpointContext context,
        [EconomyOption("currency", "対象の通貨を入力します。", true)] string currency,
        [EconomyOption("destination", "発行先の勘定を入力します。", true)] string destination,
        [EconomyOption("amount", "発行量を入力します。", true)] long amount,
        [EconomyOption("reason", "理由コードを入力します。", true)] string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!EntityReference.TryParse(currency, out EntityIdValue currencyId) ||
            !EntityReference.TryParse(destination, out EntityIdValue accountId))
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.CurrencyNotFound);
        }

        Result<CurrencySupplyView> result = await currencies
            .IssueAsync(
                new IssueCurrencyCommand(
                    EndpointAuthorization.ToActor(context),
                    CurrencyId.FromValue(currencyId),
                    LedgerAccountId.FromValue(accountId),
                    amount,
                    reason,
                    Token(context)),
                cancellationToken)
            .ConfigureAwait(false);

        return Supply(result, ViewKeys.ManageCurrencyIssued);
    }

    [EconomySlashCommand("currency-burn", "通貨を償却します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> BurnCurrencyAsync(
        DiscordEndpointContext context,
        [EconomyOption("currency", "対象の通貨を入力します。", true)] string currency,
        [EconomyOption("source", "償却元の勘定を入力します。", true)] string source,
        [EconomyOption("amount", "償却量を入力します。", true)] long amount,
        [EconomyOption("reason", "理由コードを入力します。", true)] string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!EntityReference.TryParse(currency, out EntityIdValue currencyId) ||
            !EntityReference.TryParse(source, out EntityIdValue accountId))
        {
            return EndpointFailures.From(ErrorCategory.Validation, BankingErrorCodes.CurrencyNotFound);
        }

        Result<CurrencySupplyView> result = await currencies
            .BurnAsync(
                new BurnCurrencyCommand(
                    EndpointAuthorization.ToActor(context),
                    CurrencyId.FromValue(currencyId),
                    LedgerAccountId.FromValue(accountId),
                    amount,
                    reason,
                    Token(context)),
                cancellationToken)
            .ConfigureAwait(false);

        return Supply(result, ViewKeys.ManageCurrencyBurned);
    }

    private static DiscordEndpointResponse Supply(Result<CurrencySupplyView> result, string viewKey) =>
        result.IsSuccess
            ? DiscordEndpointResponse.Message(
                viewKey,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["amount"] = Text(result.Value.Amount),
                    ["baseMoneySupply"] = Text(result.Value.BaseMoneySupply),
                })
            : EndpointFailures.From(result.Error!);

    private static string Text(MoneyMinor amount) =>
        amount.Value.ToString(CultureInfo.InvariantCulture);

    private static string Token(DiscordEndpointContext context) =>
        context.InteractionId.ToString(CultureInfo.InvariantCulture);
}

internal static class EntityReference
{
    internal static bool TryParse(string text, out EntityIdValue value)
    {
        if (Guid.TryParse(text, out Guid parsed))
        {
            value = EntityIdValue.FromBytes(parsed.ToByteArray(bigEndian: true));
            return true;
        }

        value = default;
        return false;
    }
}
