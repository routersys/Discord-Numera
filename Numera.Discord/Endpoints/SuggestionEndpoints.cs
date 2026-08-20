using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;

namespace Numera.Discord.Endpoints;

public sealed class SuggestionEndpoints : IEconomyEndpoint
{
    public const string BankProviderKey = "bank-suggest";
    public const string CurrencyProviderKey = "currency-suggest";
    public const string DepositAccountProviderKey = "account-suggest";
    public const string FxMarketProviderKey = "fx-market-suggest";
    public const string FxOrderProviderKey = "fx-order-suggest";

    private const int DisplayLimit = 25;

    private readonly ISuggestionApplicationService suggestions;
    private readonly IAuthorizationResolver authorization;

    public SuggestionEndpoints(
        ISuggestionApplicationService suggestions,
        IAuthorizationResolver authorization)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        ArgumentNullException.ThrowIfNull(authorization);

        this.suggestions = suggestions;
        this.authorization = authorization;
    }

    [EconomyAutocompleteProvider(BankProviderKey)]
    public async Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestBanksAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthorizationContext actor = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<BankSuggestion>> result = await suggestions
            .SuggestBanksAsync(new SuggestBanksQuery(actor, request.Value), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Take(result.Value.Select(static bank => (bank.Name, bank.InstitutionCode)))
            : [];
    }

    [EconomyAutocompleteProvider(DepositAccountProviderKey)]
    public async Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestDepositAccountsAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthorizationContext actor = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<DepositAccountSuggestion>> result = await suggestions
            .SuggestDepositAccountsAsync(
                new SuggestDepositAccountsQuery(actor, request.Value), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Take(result.Value.Select(static account => (
                account.InstitutionCode + " *" + account.AccountNumberSuffix,
                DepositAccountReference.Format(account.Id))))
            : [];
    }

    [EconomyAutocompleteProvider(FxMarketProviderKey)]
    public async Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestFxMarketsAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthorizationContext actor = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<FxMarketSuggestion>> result = await suggestions
            .SuggestFxMarketsAsync(new SuggestFxMarketsQuery(actor, request.Value), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Take(result.Value.Select(static market => (
                market.Pair + " " + market.Status.ToToken(),
                FxMarketReference.Format(market.Id))))
            : [];
    }

    [EconomyAutocompleteProvider(FxOrderProviderKey)]
    public async Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestFxOrdersAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthorizationContext actor = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<FxOrderSuggestion>> result = await suggestions
            .SuggestFxOrdersAsync(new SuggestFxOrdersQuery(actor, request.Value), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Take(result.Value.Select(static order => (
                order.Pair + " " + order.Side.ToToken() + " "
                    + order.RemainingBaseMinor.ToString(CultureInfo.InvariantCulture),
                FxOrderReference.Format(order.Id))))
            : [];
    }

    [EconomyAutocompleteProvider(CurrencyProviderKey)]
    public async Task<IReadOnlyList<DiscordAutocompleteOption>> SuggestCurrenciesAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AuthorizationContext actor = await ResolveAsync(request, cancellationToken).ConfigureAwait(false);

        Result<IReadOnlyList<CurrencySuggestion>> result = await suggestions
            .SuggestCurrenciesAsync(new SuggestCurrenciesQuery(actor, request.Value), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Take(result.Value.Select(static currency => (currency.Name, currency.Code)))
            : [];
    }

    private Task<AuthorizationContext> ResolveAsync(
        DiscordAutocompleteRequest request,
        CancellationToken cancellationToken) =>
        authorization.ResolveAsync(request.UserId, request.GuildId, member: null, cancellationToken);

    private static IReadOnlyList<DiscordAutocompleteOption> Take(
        IEnumerable<(string Name, string Value)> candidates)
    {
        List<DiscordAutocompleteOption> options = [];

        foreach ((string name, string value) in candidates)
        {
            if (options.Count == DisplayLimit)
            {
                break;
            }

            if (DiscordAutocompleteOption.TryCreate(name, value, out DiscordAutocompleteOption? option))
            {
                options.Add(option!);
            }
        }

        return options;
    }
}
