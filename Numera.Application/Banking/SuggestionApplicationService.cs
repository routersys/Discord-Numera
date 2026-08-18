using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record BankSuggestion(string InstitutionCode, string Name, BankStatus Status);

public sealed record CurrencySuggestion(string Code, string Name);

public sealed record SuggestBanksQuery(
    AuthorizationContext Authorization,
    string Input);

public sealed record SuggestCurrenciesQuery(
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
