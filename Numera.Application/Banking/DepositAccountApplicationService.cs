using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record OpenDepositAccountCommand(
    EconomyScopeId EconomyScopeId,
    CustomerAccountId CustomerAccountId,
    string InstitutionCode);

public sealed record DepositAccountView(
    DepositAccountId Id,
    string InstitutionCode,
    string AccountNumber,
    DepositAccountStatus Status,
    MoneyMinor PostedBalance,
    MoneyMinor AvailableBalance);

public interface IDepositAccountApplicationService
{
    Task<Result<DepositAccountView>> OpenAsync(
        OpenDepositAccountCommand command,
        CancellationToken cancellationToken);
}

public sealed class DepositAccountApplicationService : IDepositAccountApplicationService
{
    public const string OperationType = "ACCOUNT_OPEN";
    public const string OpenedEventType = "DEPOSIT_ACCOUNT_OPENED";
    public const string DemandDepositControlCode = "2000";
    public const int NumberDigits = 10;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public DepositAccountApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<DepositAccountView>> OpenAsync(
        OpenDepositAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!InstitutionCode.TryParse(command.InstitutionCode, out InstitutionCode institutionCode))
        {
            return Task.FromResult(Result<DepositAccountView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound, nameof(command.InstitutionCode)));
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            OperationType,
            $"{command.CustomerAccountId.Value}.{institutionCode.Value}");

        return writeGateway.ExecuteAsync(
            unitOfWork => Open(unitOfWork, command, institutionCode, idempotencyKey),
            cancellationToken);
    }

    private Result<DepositAccountView> Open(
        IBankingUnitOfWork unitOfWork,
        OpenDepositAccountCommand command,
        InstitutionCode institutionCode,
        IdempotencyKey idempotencyKey)
    {
        Bank? bank = unitOfWork.Banks.FindByInstitutionCode(command.EconomyScopeId, institutionCode.Value);
        if (bank is null)
        {
            return Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        CustomerAccount? customer = unitOfWork.CustomerAccounts.Find(command.CustomerAccountId);
        if (customer is null)
        {
            return Failure(ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        DepositAccount? existing = unitOfWork.DepositAccounts.FindByCustomer(bank.Id, customer.Id);
        if (existing is not null)
        {
            return unitOfWork.BusinessOperations.Find(idempotencyKey) is not null
                ? Result<DepositAccountView>.Success(ToView(unitOfWork, bank, existing))
                : Failure(ErrorCategory.Conflict, BankingErrorCodes.DepositAccountAlreadyExists);
        }

        if (customer.Status != CustomerAccountStatus.Active)
        {
            return Failure(ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountNotOperable);
        }

        if (!bank.AcceptsAccountOpening)
        {
            return Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        AccountProductSelection? product = unitOfWork.AccountProducts.FindDefault(bank.Id);
        if (product is null)
        {
            return Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        LedgerAccount? control = unitOfWork.LedgerAccounts.FindByCode(
            bank.GeneralLedgerBookId, DemandDepositControlCode);
        if (control is null)
        {
            return Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        UtcTimestamp now = clock.Now();

        BankCustomerRelationship relationship = ResolveRelationship(unitOfWork, bank, customer, now);
        if (!relationship.AllowsNewAccount)
        {
            return Failure(ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountNotOperable);
        }

        AccountNumber accountNumber = AccountNumber.Parse(
            Sequence(unitOfWork.DepositAccounts.CountByBranch(bank.Id, product.BranchId) + 1));

        LedgerAccountId ledgerAccountId = LedgerAccountId.FromValue(idGenerator.NextId());
        DepositAccountId depositAccountId = DepositAccountId.FromValue(idGenerator.NextId());

        LedgerAccount postingAccount = LedgerAccount.CreatePosting(
            ledgerAccountId,
            bank.GeneralLedgerBookId,
            control.Id,
            $"{DemandDepositControlCode}-{accountNumber.Value}",
            LedgerAccountKind.DemandDepositControl,
            control.CurrencyId,
            LedgerOwnerReferenceType.DepositAccount,
            depositAccountId.Value);

        DepositAccount account = DepositAccount.OpenPending(
            depositAccountId,
            bank.Id,
            product.BranchId,
            relationship.Id,
            customer.Id,
            control.CurrencyId,
            product.ProductId,
            product.ProductVersionId,
            ledgerAccountId,
            accountNumber,
            publicReceivingEnabled: true,
            now);

        account.FinalizeOpening();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            command.EconomyScopeId,
            customer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.LedgerAccounts.Add(postingAccount);
        unitOfWork.LedgerAccounts.UpsertProjection(ledgerAccountId, LedgerBalance.Empty, now);
        unitOfWork.DepositAccounts.Add(account);
        unitOfWork.BusinessOperations.Add(operation);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            OpenedEventType,
            $$"""{"deposit_account_id":"{{depositAccountId.Value}}","account_number":"{{accountNumber.Value}}"}""",
            now));

        return Result<DepositAccountView>.Success(new DepositAccountView(
            account.Id,
            bank.InstitutionCode.Value,
            account.AccountNumber.Value,
            account.Status,
            MoneyMinor.Zero,
            MoneyMinor.Zero));
    }

    private BankCustomerRelationship ResolveRelationship(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CustomerAccount customer,
        UtcTimestamp now)
    {
        BankCustomerRelationship? existing = unitOfWork.Relationships.Find(bank.Id, customer.PartyId);

        if (existing is not null)
        {
            if (existing.Status == RelationshipStatus.Pending)
            {
                existing.Activate();
                unitOfWork.Relationships.Update(existing);
            }

            return existing;
        }

        BankCustomerRelationship created = BankCustomerRelationship.Open(
            BankCustomerRelationshipId.FromValue(idGenerator.NextId()),
            bank.Id,
            customer.PartyId,
            CustomerNumber.Parse(Sequence(unitOfWork.Relationships.CountByBank(bank.Id) + 1)),
            now);

        unitOfWork.Relationships.Add(created);
        created.Activate();
        unitOfWork.Relationships.Update(created);

        return created;
    }

    private static DepositAccountView ToView(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account)
    {
        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        return new DepositAccountView(
            account.Id,
            bank.InstitutionCode.Value,
            account.AccountNumber.Value,
            account.Status,
            balance.PostedBalance,
            balance.AvailableBalance);
    }

    private static Result<DepositAccountView> Failure(ErrorCategory category, string code) =>
        Result<DepositAccountView>.Failure(category, code);

    private static string Sequence(long value) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(NumberDigits, '0');
}
