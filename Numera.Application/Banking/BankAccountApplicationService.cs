using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record OpenDepositAccountCommand(
    ulong GuildId,
    CustomerAccountId CustomerAccountId,
    string InstitutionCode);

public sealed record AccountOpeningView(
    DepositAccountId Id,
    string InstitutionCode,
    string AccountNumber,
    DepositAccountStatus Status,
    MoneyMinor PostedBalance,
    MoneyMinor AvailableBalance);

public interface IBankAccountApplicationService
{
    Task<Result<AccountOpeningView>> OpenDepositAccountAsync(
        OpenDepositAccountCommand command,
        CancellationToken cancellationToken);

    Task<Result<AccountLimitPreferenceView>> UpdateLimitsAsync(
        UpdateAccountLimitPreferenceCommand command,
        CancellationToken cancellationToken);

    Task<Result> ReactivateDepositAccountAsync(
        ReactivateDepositAccountCommand command,
        CancellationToken cancellationToken);

    Task<Result> CloseDepositAccountAsync(
        CloseDepositAccountCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class BankAccountApplicationService : IBankAccountApplicationService
{
    public const string OperationType = "ACCOUNT_OPEN";
    public const string OpenedEventType = "DEPOSIT_ACCOUNT_OPENED";
    public const string SubmittedEventType = "ACCOUNT_OPENING_SUBMITTED";
    public const string DemandDepositControlCode = AccountOpeningWorkflow.DemandDepositControlCode;
    public const int NumberDigits = AccountOpeningWorkflow.NumberDigits;

    public const string FundingOperationType = "ACCOUNT_OPEN_FUNDING";
    public const string ActivatedEventType = "DEPOSIT_ACCOUNT_ACTIVATED";
    public const string OpeningFeeTransactionType = "ACCOUNT_OPENING_FEE";
    public const string OpeningFeeDescriptionCode = "ACCOUNT_OPENING_FEE";

    private readonly IBankingWriteGateway writeGateway;
    private readonly PaymentApplicationService payments;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public BankAccountApplicationService(
        IBankingWriteGateway writeGateway,
        PaymentApplicationService payments,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.payments = payments;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    private readonly record struct OpeningResult(
        AccountOpeningView View,
        AccountOpeningApplicationId? ApplicationId,
        PaymentOrderId? FundingPaymentOrderId);

    public Task<Result<AccountOpeningView>> OpenDepositAccountAsync(
        OpenDepositAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!InstitutionCode.TryParse(command.InstitutionCode, out InstitutionCode institutionCode))
        {
            return Task.FromResult(Result<AccountOpeningView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound, nameof(command.InstitutionCode)));
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            OperationType,
            $"{command.CustomerAccountId.Value}.{institutionCode.Value}");

        return OpenAndFinalizeAsync(command, institutionCode, idempotencyKey, cancellationToken);
    }

    private async Task<Result<AccountOpeningView>> OpenAndFinalizeAsync(
        OpenDepositAccountCommand command,
        InstitutionCode institutionCode,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        Result<OpeningResult> opened = await writeGateway.ExecuteAsync(
            unitOfWork => Open(unitOfWork, command, institutionCode, idempotencyKey),
            cancellationToken).ConfigureAwait(false);

        if (!opened.IsSuccess)
        {
            return Result<AccountOpeningView>.Failure(opened.Error!);
        }

        if (opened.Value.FundingPaymentOrderId is not { } order
            || opened.Value.ApplicationId is not { } applicationId)
        {
            return Result<AccountOpeningView>.Success(opened.Value.View);
        }

        Result<PaymentOrderView> debited = await writeGateway.ExecuteAsync(
            unitOfWork => payments.PostSourceDebit(unitOfWork, order),
            cancellationToken).ConfigureAwait(false);

        if (!debited.IsSuccess)
        {
            return Result<AccountOpeningView>.Failure(debited.Error!);
        }

        Result<PaymentOrderView> settled = await writeGateway.ExecuteAsync(
            unitOfWork => payments.SettleInterbank(unitOfWork, order),
            cancellationToken).ConfigureAwait(false);

        if (!settled.IsSuccess)
        {
            return Result<AccountOpeningView>.Failure(settled.Error!);
        }

        return settled.Value.Status == PaymentOrderStatus.Settled
            ? await writeGateway.ExecuteAsync(
                unitOfWork => Finalize(unitOfWork, applicationId),
                cancellationToken).ConfigureAwait(false)
            : Result<AccountOpeningView>.Success(opened.Value.View);
    }

    private Result<OpeningResult> Open(
        IBankingUnitOfWork unitOfWork,
        OpenDepositAccountCommand command,
        InstitutionCode institutionCode,
        IdempotencyKey idempotencyKey)
    {
        if (unitOfWork.GuildEconomies.FindEconomyScope(command.GuildId) is not { } economyScopeId)
        {
            return Failure(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        Bank? bank = unitOfWork.Banks.FindByInstitutionCode(economyScopeId, institutionCode.Value);
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
                ? Result<OpeningResult>.Success(
                    new OpeningResult(ToView(unitOfWork, bank, existing), null, null))
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
            return Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.AccountProductUnavailable);
        }

        LedgerAccount? control = unitOfWork.LedgerAccounts.FindByCode(
            bank.GeneralLedgerBookId, DemandDepositControlCode);
        if (control is null)
        {
            return Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        UtcTimestamp now = clock.Now();

        Result<AccountOpeningContract> resolved = AccountOpeningWorkflow.ResolveContract(
            unitOfWork, economyScopeId, bank, product, control.CurrencyId, now);

        if (!resolved.IsSuccess)
        {
            return Result<OpeningResult>.Failure(resolved.Error!);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            economyScopeId,
            customer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        Result<AccountOpeningOutcome> outcome = OpenUnderContract(
            unitOfWork, command.GuildId, bank, customer, product, control, resolved.Value, now);

        if (!outcome.IsSuccess)
        {
            return Result<OpeningResult>.Failure(outcome.Error!);
        }

        unitOfWork.BusinessOperations.Add(operation);
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        Publish(unitOfWork, operation, outcome.Value, now);

        AccountOpeningApplication? application = outcome.Value.Application;

        return Result<OpeningResult>.Success(new OpeningResult(
            ToView(bank, outcome.Value),
            application?.Id,
            application?.FundingPaymentOrderId));
    }

    private Result<AccountOpeningOutcome> OpenUnderContract(
        IBankingUnitOfWork unitOfWork,
        ulong guildId,
        Bank bank,
        CustomerAccount customer,
        AccountProductSelection product,
        LedgerAccount control,
        AccountOpeningContract contract,
        UtcTimestamp now)
    {
        Result eligible = AccountOpeningWorkflow.EnsureEligible(
            unitOfWork, bank, customer, contract, control.CurrencyId, now);

        if (!eligible.IsSuccess)
        {
            return Result<AccountOpeningOutcome>.Failure(eligible.Error!);
        }

        if (unitOfWork.BankAdministration.FindPendingOpeningApplication(bank.Id, customer.Id) is not null)
        {
            return Result<AccountOpeningOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AccountOpeningApplicationAlreadyPending);
        }

        AccountOpeningApplication application = AccountOpeningWorkflow.Submit(
            idGenerator, bank, customer, product, contract, now);

        unitOfWork.BankAdministration.AddOpeningApplication(application);

        if (contract.Policy.RequiresManualApproval)
        {
            return Result<AccountOpeningOutcome>.Success(new AccountOpeningOutcome(application, null, bank));
        }

        application.Approve(now, decidedByDiscordUserId: null);

        Result<AccountOpeningOutcome> advanced = AccountOpeningWorkflow.Advance(
            unitOfWork,
            idGenerator,
            bank,
            customer,
            product,
            control,
            contract,
            application,
            control.CurrencyId,
            now);

        if (!advanced.IsSuccess)
        {
            return advanced;
        }

        if (application.Status == AccountOpeningApplicationStatus.AwaitingFunding)
        {
            Result reserved = ReserveFunding(
                unitOfWork, guildId, bank, customer, contract, application, advanced.Value);

            if (!reserved.IsSuccess)
            {
                return Result<AccountOpeningOutcome>.Failure(reserved.Error!);
            }
        }

        unitOfWork.BankAdministration.UpdateOpeningApplication(application);

        return advanced;
    }

    private Result ReserveFunding(
        IBankingUnitOfWork unitOfWork,
        ulong guildId,
        Bank bank,
        CustomerAccount customer,
        AccountOpeningContract contract,
        AccountOpeningApplication application,
        AccountOpeningOutcome outcome)
    {
        DepositAccount account = outcome.Account!;

        if (unitOfWork.Branches.FindCodeById(account.BranchId) is not { } branchCode)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        string token = application.Id.Value.ToString();

        Result<PaymentApplicationService.ReservedTransfer> reserved = payments.ReserveOpeningFunding(
            unitOfWork,
            new CreatePaymentOrderCommand(
                guildId,
                customer.Id,
                application.FundingSourceDepositAccountId!.Value,
                bank.InstitutionCode.Value,
                branchCode,
                account.AccountNumber.Value,
                contract.RequiredFunding.Value,
                null,
                token),
            IdempotencyKey.Create(FundingOperationType, token));

        if (!reserved.IsSuccess)
        {
            return Result.Failure(reserved.Error!);
        }

        application.AttachFundingPayment(reserved.Value.OrderId);

        return Result.Success();
    }

    private void Publish(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        AccountOpeningOutcome outcome,
        UtcTimestamp now)
    {
        if (outcome.Account is { } account)
        {
            unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
                OutboxEventId.FromValue(idGenerator.NextId()),
                operation.Id,
                OpenedEventType,
                $$"""{"deposit_account_id":"{{account.Id.Value}}","account_number":"{{account.AccountNumber.Value}}"}""",
                now));

            return;
        }

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            SubmittedEventType,
            $$"""{"account_opening_application_id":"{{outcome.Application!.Id.Value}}"}""",
            now));
    }

    private static AccountOpeningView ToView(Bank bank, AccountOpeningOutcome outcome) =>
        outcome.Account is { } account
            ? new AccountOpeningView(
                account.Id,
                bank.InstitutionCode.Value,
                account.AccountNumber.Value,
                account.Status,
                MoneyMinor.Zero,
                MoneyMinor.Zero)
            : new AccountOpeningView(
                DepositAccountId.FromValue(EntityIdValue.Empty),
                bank.InstitutionCode.Value,
                string.Empty,
                DepositAccountStatus.Pending,
                MoneyMinor.Zero,
                MoneyMinor.Zero);

    private static AccountOpeningView ToView(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account)
    {
        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        return new AccountOpeningView(
            account.Id,
            bank.InstitutionCode.Value,
            account.AccountNumber.Value,
            account.Status,
            balance.PostedBalance,
            balance.AvailableBalance);
    }

    private static Result<OpeningResult> Failure(ErrorCategory category, string code) =>
        Result<OpeningResult>.Failure(category, code);
}
