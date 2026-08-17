using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Abstractions;

public interface IPartyRepository
{
    void Add(Party party);

    Party? Find(PartyId id);
}

public interface ICustomerAccountRepository
{
    void Add(CustomerAccount account);

    void Update(CustomerAccount account);

    CustomerAccount? Find(CustomerAccountId id);

    bool HandleExists(PublicHandle handle);
}

public interface IDiscordIdentityLinkRepository
{
    void Add(DiscordIdentityLink link);

    void Update(DiscordIdentityLink link);

    DiscordIdentityLink? FindActive(DiscordUserId discordUserId);
}

public interface IBusinessOperationRepository
{
    void Add(BusinessOperation operation);

    void Update(BusinessOperation operation);

    BusinessOperation? Find(IdempotencyKey idempotencyKey);
}

public interface IOutboxRepository
{
    void Add(OutboxEvent outboxEvent);
}

public interface IBankingUnitOfWork
{
    IPartyRepository Parties { get; }

    ICustomerAccountRepository CustomerAccounts { get; }

    IDiscordIdentityLinkRepository DiscordIdentityLinks { get; }

    IBusinessOperationRepository BusinessOperations { get; }

    IOutboxRepository Outbox { get; }
}

public interface IBankingWriteGateway
{
    Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IBankingUnitOfWork, Result<TValue>> operation,
        CancellationToken cancellationToken);
}

public interface IBankReadRepository
{
    IReadOnlyList<Numera.Application.Banking.BankSuggestion> ListSuggestible(
        EconomyScopeId economyScopeId,
        IReadOnlyList<Numera.Domain.Banking.BankStatus> selectableStatuses,
        ulong? operatorDiscordUserId,
        int limit);
}

public interface ICurrencyReadRepository
{
    IReadOnlyList<Numera.Application.Banking.CurrencySuggestion> ListSuggestible(
        EconomyScopeId economyScopeId,
        int limit);
}

public interface IBankingReadContext
{
    IBankReadRepository Banks { get; }

    ICurrencyReadRepository Currencies { get; }
}

public interface IBankingReadGateway
{
    TResult Execute<TResult>(Func<IBankingReadContext, TResult> query);
}
