using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record RegisterCustomerAccountCommand(
    EconomyScopeId EconomyScopeId,
    ulong DiscordUserId,
    string PublicHandle,
    string DisplayName);

public sealed record CustomerAccountView(
    CustomerAccountId Id,
    string PublicHandle,
    string DisplayName,
    CustomerAccountStatus Status,
    UtcTimestamp CreatedAt);

public sealed record GetCustomerAccountStatusQuery(ulong DiscordUserId);

public sealed record CustomerAccountStatusView(
    CustomerAccountId Id,
    string PublicHandle,
    string DisplayName,
    CustomerAccountStatus Status,
    UtcTimestamp RegisteredAt);

public interface ICustomerAccountApplicationService
{
    Task<Result<CustomerAccountView>> RegisterCustomerAccountAsync(
        RegisterCustomerAccountCommand command,
        CancellationToken cancellationToken);

    Task<Result<CustomerAccountStatusView>> GetCustomerAccountStatusAsync(
        GetCustomerAccountStatusQuery query,
        CancellationToken cancellationToken);
}

public sealed class CustomerAccountApplicationService : ICustomerAccountApplicationService
{
    public const string OperationType = "ACCOUNT_REGISTER";
    public const string RegisteredEventType = "CUSTOMER_ACCOUNT_REGISTERED";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IBankingReadGateway readGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public CustomerAccountApplicationService(
        IBankingWriteGateway writeGateway,
        IBankingReadGateway readGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(readGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.readGateway = readGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<CustomerAccountStatusView>> GetCustomerAccountStatusAsync(
        GetCustomerAccountStatusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.DiscordUserId == 0)
        {
            return Task.FromResult(Result<CustomerAccountStatusView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound));
        }

        DiscordUserId discordUserId = DiscordUserId.FromUInt64(query.DiscordUserId);

        CustomerAccountStatusView? view = readGateway.Execute(
            context => context.CustomerIdentities.FindByDiscordUser(discordUserId));

        return Task.FromResult(view is null
            ? Result<CustomerAccountStatusView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound)
            : Result<CustomerAccountStatusView>.Success(view));
    }

    public Task<Result<CustomerAccountView>> RegisterCustomerAccountAsync(
        RegisterCustomerAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!PublicHandle.TryParse(command.PublicHandle, out PublicHandle handle))
        {
            return Failed(ErrorCategory.Validation, BankingErrorCodes.HandleFormatInvalid, nameof(command.PublicHandle));
        }

        if (!DisplayName.TryParse(command.DisplayName, out DisplayName displayName))
        {
            return Failed(ErrorCategory.Validation, BankingErrorCodes.DisplayNameInvalid, nameof(command.DisplayName));
        }

        if (command.DiscordUserId == 0)
        {
            return Failed(ErrorCategory.Validation, BankingErrorCodes.HandleFormatInvalid, nameof(command.DiscordUserId));
        }

        DiscordUserId discordUserId = DiscordUserId.FromUInt64(command.DiscordUserId);
        IdempotencyKey idempotencyKey = IdempotencyKey.Create(OperationType, discordUserId.ToString());

        return writeGateway.ExecuteAsync(
            unitOfWork => Register(unitOfWork, discordUserId, handle, displayName, command.EconomyScopeId, idempotencyKey),
            cancellationToken);
    }

    private Result<CustomerAccountView> Register(
        IBankingUnitOfWork unitOfWork,
        DiscordUserId discordUserId,
        PublicHandle handle,
        DisplayName displayName,
        EconomyScopeId economyScopeId,
        IdempotencyKey idempotencyKey)
    {
        BusinessOperation? existing = unitOfWork.BusinessOperations.Find(idempotencyKey);
        if (existing is not null)
        {
            DiscordIdentityLink? completedLink = unitOfWork.DiscordIdentityLinks.FindActive(discordUserId);
            if (completedLink is not null)
            {
                CustomerAccount? account = unitOfWork.CustomerAccounts.Find(completedLink.CustomerAccountId);
                if (account is not null)
                {
                    return Result<CustomerAccountView>.Success(ToView(account));
                }
            }

            return Result<CustomerAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.IdentityAlreadyLinked);
        }

        if (unitOfWork.DiscordIdentityLinks.FindActive(discordUserId) is not null)
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.IdentityAlreadyLinked);
        }

        if (unitOfWork.CustomerAccounts.HandleExists(handle))
        {
            return Result<CustomerAccountView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.HandleAlreadyTaken, nameof(PublicHandle));
        }

        UtcTimestamp now = clock.Now();
        PartyId partyId = PartyId.FromValue(idGenerator.NextId());
        CustomerAccountId customerAccountId = CustomerAccountId.FromValue(idGenerator.NextId());
        BusinessOperationId operationId = BusinessOperationId.FromValue(idGenerator.NextId());

        BusinessOperation operation = BusinessOperation.Start(
            operationId,
            OperationType,
            economyScopeId,
            partyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        Party party = Party.Create(partyId, PartyType.Customer, displayName, now);
        CustomerAccount customerAccount = CustomerAccount.Register(
            customerAccountId, partyId, handle, displayName, now);
        DiscordIdentityLink link = DiscordIdentityLink.Link(
            DiscordIdentityLinkId.FromValue(idGenerator.NextId()),
            customerAccountId,
            discordUserId,
            isPrimary: true,
            now);

        unitOfWork.Parties.Add(party);
        unitOfWork.CustomerAccounts.Add(customerAccount);
        unitOfWork.DiscordIdentityLinks.Add(link);
        unitOfWork.BusinessOperations.Add(operation);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operationId,
            RegisteredEventType,
            BuildRegisteredPayload(customerAccountId, handle),
            now));

        return Result<CustomerAccountView>.Success(ToView(customerAccount));
    }

    private static string BuildRegisteredPayload(CustomerAccountId id, PublicHandle handle) =>
        $$"""{"customer_account_id":"{{id.Value}}","public_handle":"{{handle.Value}}"}""";

    private static CustomerAccountView ToView(CustomerAccount account) =>
        new(account.Id, account.PublicHandle.Value, account.DisplayName.Value, account.Status, account.CreatedAt);

    private static Task<Result<CustomerAccountView>> Failed(ErrorCategory category, string code, string field) =>
        Task.FromResult(Result<CustomerAccountView>.Failure(category, code, field));
}
