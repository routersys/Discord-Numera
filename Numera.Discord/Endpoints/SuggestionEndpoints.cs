using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;

namespace Numera.Discord.Endpoints;

public sealed class SuggestionEndpoints : IEconomyEndpoint
{
    public const string BankProviderKey = "bank-suggest";
    public const string CurrencyProviderKey = "currency-suggest";

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
