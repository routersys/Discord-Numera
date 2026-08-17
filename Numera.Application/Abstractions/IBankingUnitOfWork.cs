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

public interface IInteractionSessionRepository
{
    void Add(InteractionSession session);

    void Update(InteractionSession session);

    InteractionSession? FindByTokenHash(byte[] tokenHash);

    IReadOnlyList<InteractionSession> ListActiveByUser(string discordUserId);

    IReadOnlyList<InteractionSession> ListExpired(UtcTimestamp now, int batchSize);

    int PurgeTerminal(UtcTimestamp completedBefore, int batchSize);
}

public interface IBankingUnitOfWork
{
    IPartyRepository Parties { get; }

    ICustomerAccountRepository CustomerAccounts { get; }

    IDiscordIdentityLinkRepository DiscordIdentityLinks { get; }

    IBusinessOperationRepository BusinessOperations { get; }

    IOutboxRepository Outbox { get; }

    IInteractionSessionRepository InteractionSessions { get; }

    IBankRepository Banks { get; }

    IBankCustomerRelationshipRepository Relationships { get; }

    ILedgerAccountRepository LedgerAccounts { get; }

    IDepositAccountRepository DepositAccounts { get; }

    IAccountProductRepository AccountProducts { get; }
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

public interface IBankRepository
{
    Numera.Domain.Banking.Bank? FindByInstitutionCode(EconomyScopeId economyScopeId, string institutionCode);

    Numera.Domain.Banking.Bank? Find(BankId id);
}

public interface IBankCustomerRelationshipRepository
{
    void Add(Numera.Domain.Banking.BankCustomerRelationship relationship);

    void Update(Numera.Domain.Banking.BankCustomerRelationship relationship);

    Numera.Domain.Banking.BankCustomerRelationship? Find(BankId bankId, PartyId partyId);

    long CountByBank(BankId bankId);
}

public interface ILedgerAccountRepository
{
    void Add(Numera.Domain.Accounting.LedgerAccount account);

    Numera.Domain.Accounting.LedgerAccount? Find(LedgerAccountId id);

    Numera.Domain.Accounting.LedgerAccount? FindByCode(AccountingBookId bookId, string accountCode);

    void UpsertProjection(LedgerAccountId id, Numera.Domain.Accounting.LedgerBalance balance, UtcTimestamp updatedAt);

    Numera.Domain.Accounting.LedgerBalance? FindProjection(LedgerAccountId id);
}

public interface IDepositAccountRepository
{
    void Add(Numera.Domain.Banking.DepositAccount account);

    void Update(Numera.Domain.Banking.DepositAccount account);

    Numera.Domain.Banking.DepositAccount? Find(DepositAccountId id);

    Numera.Domain.Banking.DepositAccount? FindByCustomer(BankId bankId, CustomerAccountId customerAccountId);

    long CountByBranch(BankId bankId, BranchId branchId);
}

public interface IAccountProductRepository
{
    AccountProductSelection? FindDefault(BankId bankId);
}

public sealed record AccountProductSelection(
    AccountProductId ProductId,
    AccountProductVersionId ProductVersionId,
    BranchId BranchId);
