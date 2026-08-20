using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record BankSuggestion(string InstitutionCode, string Name, BankStatus Status);

public sealed record CurrencySuggestion(CurrencyId Id, string Code, string Name);

public sealed record SuggestBanksQuery(
    AuthorizationContext Authorization,
    string Input);

public sealed record SuggestCurrenciesQuery(
    AuthorizationContext Authorization,
    string Input);

public sealed record DepositAccountSuggestion(
    DepositAccountId Id,
    string InstitutionCode,
    string AccountNumberSuffix,
    DepositAccountStatus Status);

public sealed record SuggestDepositAccountsQuery(
    AuthorizationContext Authorization,
    string Input);

public sealed record SuggestFxMarketsQuery(
    AuthorizationContext Authorization,
    string Input);

public sealed record SuggestFxOrdersQuery(
    AuthorizationContext Authorization,
    string Input);

public interface ISuggestionApplicationService
{
    Task<Result<IReadOnlyList<BankSuggestion>>> SuggestBanksAsync(
        SuggestBanksQuery query,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CurrencySuggestion>>> SuggestCurrenciesAsync(
        SuggestCurrenciesQuery query,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<DepositAccountSuggestion>>> SuggestDepositAccountsAsync(
        SuggestDepositAccountsQuery query,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<FxMarketSuggestion>>> SuggestFxMarketsAsync(
        SuggestFxMarketsQuery query,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<FxOrderSuggestion>>> SuggestFxOrdersAsync(
        SuggestFxOrdersQuery query,
        CancellationToken cancellationToken);
}

public sealed class SuggestionApplicationService : ISuggestionApplicationService
{
    public const int CandidateFetchLimit = 200;

    private readonly IBankingReadGateway readGateway;

    public SuggestionApplicationService(IBankingReadGateway readGateway)
    {
        ArgumentNullException.ThrowIfNull(readGateway);
        this.readGateway = readGateway;
    }

    public Task<Result<IReadOnlyList<BankSuggestion>>> SuggestBanksAsync(
        SuggestBanksQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Authorization.Level == AuthorizationLevel.Unregistered)
        {
            return Task.FromResult(Result<IReadOnlyList<BankSuggestion>>.Success([]));
        }

        IReadOnlyList<BankSuggestion> suggestions = readGateway.Execute(context =>
            context.EconomyScopes.FindByGuild(query.Authorization.GuildId) is { } scope
                ? context.Banks.ListSuggestible(
                    scope,
                    SelectableStatuses(query.Authorization.Level),
                    query.Authorization.Level == AuthorizationLevel.BankOperator
                        ? query.Authorization.DiscordUserId
                        : null,
                    CandidateFetchLimit)
                : []);

        return Task.FromResult(Result<IReadOnlyList<BankSuggestion>>.Success(suggestions));
    }

    public Task<Result<IReadOnlyList<CurrencySuggestion>>> SuggestCurrenciesAsync(
        SuggestCurrenciesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Authorization.Level == AuthorizationLevel.Unregistered)
        {
            return Task.FromResult(Result<IReadOnlyList<CurrencySuggestion>>.Success([]));
        }

        IReadOnlyList<CurrencySuggestion> suggestions = readGateway.Execute(context =>
            context.EconomyScopes.FindByGuild(query.Authorization.GuildId) is { } scope
                ? context.Currencies.ListSuggestible(scope, CandidateFetchLimit)
                : []);

        return Task.FromResult(Result<IReadOnlyList<CurrencySuggestion>>.Success(suggestions));
    }

    public Task<Result<IReadOnlyList<DepositAccountSuggestion>>> SuggestDepositAccountsAsync(
        SuggestDepositAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Authorization.Level == AuthorizationLevel.Unregistered)
        {
            return Task.FromResult(Result<IReadOnlyList<DepositAccountSuggestion>>.Success([]));
        }

        IReadOnlyList<DepositAccountSuggestion> suggestions = readGateway.Execute(context =>
            context.CustomerIdentities.FindByDiscordUser(
                DiscordUserId.FromUInt64(query.Authorization.DiscordUserId)) is { } customer
                ? (IReadOnlyList<DepositAccountSuggestion>)
                [
                    .. context.BankQueries
                        .ListCustomerAccounts(customer.Id, null, CandidateFetchLimit)
                        .Select(static item => new DepositAccountSuggestion(
                            item.DepositAccountId,
                            item.InstitutionCode,
                            item.AccountNumberSuffix,
                            item.Status)),
                ]
                : []);

        return Task.FromResult(Result<IReadOnlyList<DepositAccountSuggestion>>.Success(suggestions));
    }

    public Task<Result<IReadOnlyList<FxMarketSuggestion>>> SuggestFxMarketsAsync(
        SuggestFxMarketsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Authorization.Level == AuthorizationLevel.Unregistered)
        {
            return Task.FromResult(Result<IReadOnlyList<FxMarketSuggestion>>.Success([]));
        }

        IReadOnlyList<FxMarketSuggestion> suggestions = readGateway.Execute(context =>
            context.EconomyScopes.FindByGuild(query.Authorization.GuildId) is { } scope
                ? context.FxSuggestions.ListMarkets(scope, CandidateFetchLimit)
                : []);

        return Task.FromResult(Result<IReadOnlyList<FxMarketSuggestion>>.Success(suggestions));
    }

    public Task<Result<IReadOnlyList<FxOrderSuggestion>>> SuggestFxOrdersAsync(
        SuggestFxOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Authorization.Level == AuthorizationLevel.Unregistered)
        {
            return Task.FromResult(Result<IReadOnlyList<FxOrderSuggestion>>.Success([]));
        }

        IReadOnlyList<FxOrderSuggestion> suggestions = readGateway.Execute(context =>
            context.CustomerIdentities.FindByDiscordUser(
                DiscordUserId.FromUInt64(query.Authorization.DiscordUserId)) is { } customer
                ? context.FxSuggestions.ListRestingOrders(customer.Id, CandidateFetchLimit)
                : []);

        return Task.FromResult(Result<IReadOnlyList<FxOrderSuggestion>>.Success(suggestions));
    }

    public static IReadOnlyList<BankStatus> SelectableStatuses(AuthorizationLevel level) => level switch
    {
        AuthorizationLevel.SystemOwner or AuthorizationLevel.GuildOperator =>
        [
            BankStatus.PendingActivation,
            BankStatus.Operating,
            BankStatus.Restricted,
            BankStatus.SettlementSuspended,
            BankStatus.Resolution,
            BankStatus.Closing,
        ],
        AuthorizationLevel.BankOperator =>
        [
            BankStatus.PendingActivation,
            BankStatus.Operating,
            BankStatus.Restricted,
            BankStatus.SettlementSuspended,
        ],
        AuthorizationLevel.MerchantOperator or AuthorizationLevel.Customer =>
        [
            BankStatus.Operating,
            BankStatus.SettlementSuspended,
        ],
        _ => [],
    };
}
