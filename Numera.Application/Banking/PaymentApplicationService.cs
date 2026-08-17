using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record CreatePaymentOrderCommand(
    EconomyScopeId EconomyScopeId,
    CustomerAccountId PayerCustomerAccountId,
    DepositAccountId SourceDepositAccountId,
    string DestinationInstitutionCode,
    string DestinationBranchCode,
    string DestinationAccountNumber,
    long AmountMinor,
    string? Memo,
    string IdempotencyToken);

public sealed record PaymentOrderView(
    PaymentOrderId Id,
    PaymentOrderStatus Status,
    MoneyMinor Amount,
    MoneyMinor FeeAmount,
    MoneyMinor TotalDebitAmount,
    string DestinationAccountNumber,
    MoneyMinor SourcePostedBalance,
    MoneyMinor SourceAvailableBalance);

public sealed record SetPaymentPreferenceCommand(
    CustomerAccountId CustomerAccountId,
    PaymentPreferenceKind Kind,
    DepositAccountId DepositAccountId);

public sealed record PaymentPreferenceView(
    PaymentPreferenceKind Kind,
    DepositAccountId DepositAccountId,
    string AccountNumberSuffix);

public interface IPaymentApplicationService
{
    Task<Result<PaymentOrderView>> CreatePaymentOrderAsync(
        CreatePaymentOrderCommand command,
        CancellationToken cancellationToken);

    Task<Result<PaymentPreferenceView>> SetPaymentPreferenceAsync(
        SetPaymentPreferenceCommand command,
        CancellationToken cancellationToken);
}

public sealed class PaymentApplicationService : IPaymentApplicationService
{
    public const string OperationType = "PAYMENT_TRANSFER";
    public const string CompletedEventType = "PAYMENT_COMPLETED";
    public const string TransactionType = "INTERNAL_TRANSFER";
    public const string DescriptionCode = "TRANSFER";
    public const string HoldReason = "TRANSFER";
    public const string PaymentMethod = "INTERNAL_TRANSFER";
    public const string InterbankPaymentMethod = "RTGS_TRANSFER";
    public const string InterbankTransactionType = "RTGS_TRANSFER";
    public const string SettlementTransactionType = "RTGS_SETTLEMENT";
    public const string BeneficiaryTransactionType = "RTGS_BENEFICIARY_CREDIT";
    public const string SettlementDescriptionCode = "SETTLEMENT";
    public const string BeneficiaryDescriptionCode = "BENEFICIARY_CREDIT";
    public const string AcceptedEventType = "PAYMENT_ACCEPTED";

    private const FeeChannel TransferChannel = FeeChannel.Discord;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PaymentApplicationService(
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

    public async Task<Result<PaymentOrderView>> CreatePaymentOrderAsync(
        CreatePaymentOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryValidate(command, out TransferRequest request, out ApplicationError? error))
        {
            return Result<PaymentOrderView>.Failure(error!.Category, error.Code, error.Field);
        }

        IdempotencyKey idempotencyKey = IdempotencyKey.Create(OperationType, command.IdempotencyToken);

        Result<ReservedTransfer> held = await writeGateway.ExecuteAsync(
            unitOfWork => ReserveFunds(unitOfWork, command, request, idempotencyKey),
            cancellationToken).ConfigureAwait(false);

        if (!held.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(held.Error!.Category, held.Error.Code, held.Error.Field);
        }

        PaymentOrderId orderId = held.Value.OrderId;

        if (held.Value.Mode == SettlementMode.Internal)
        {
            return await writeGateway.ExecuteAsync(
                unitOfWork => PostTransfer(unitOfWork, orderId, idempotencyKey),
                cancellationToken).ConfigureAwait(false);
        }

        Result<PaymentOrderView> accepted = await writeGateway.ExecuteAsync(
            unitOfWork => PostSourceDebit(unitOfWork, orderId),
            cancellationToken).ConfigureAwait(false);

        if (!accepted.IsSuccess)
        {
            return accepted;
        }

        Result<PaymentOrderView> settled = await writeGateway.ExecuteAsync(
            unitOfWork => SettleInterbank(unitOfWork, orderId),
            cancellationToken).ConfigureAwait(false);

        if (!settled.IsSuccess || settled.Value.Status != PaymentOrderStatus.Settled)
        {
            return settled;
        }

        return await writeGateway.ExecuteAsync(
            unitOfWork => PostBeneficiaryCredit(unitOfWork, orderId, idempotencyKey),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<PaymentPreferenceView>> SetPaymentPreferenceAsync(
        SetPaymentPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => SetPaymentPreference(unitOfWork, command),
            cancellationToken);
    }

    private Result<PaymentPreferenceView> SetPaymentPreference(
        IBankingUnitOfWork unitOfWork,
        SetPaymentPreferenceCommand command)
    {
        if (unitOfWork.CustomerAccounts.Find(command.CustomerAccountId) is not { } customer)
        {
            return Result<PaymentPreferenceView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (customer.Status != CustomerAccountStatus.Active)
        {
            return Result<PaymentPreferenceView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountNotOperable);
        }

        DepositAccount? account = unitOfWork.DepositAccounts.Find(command.DepositAccountId);
        if (account is null || account.CustomerAccountId != command.CustomerAccountId)
        {
            ApplicationError denied = TargetAccessPolicy.ToError(
                TargetAccess.NotOwned,
                BankingErrorCodes.DepositAccountNotFound,
                BankingErrorCodes.DepositAccountNotOperable);

            return Result<PaymentPreferenceView>.Failure(denied.Category, denied.Code);
        }

        AccountOperation required = command.Kind is PaymentPreferenceKind.DefaultPayment
            or PaymentPreferenceKind.TaxPayment
            ? AccountOperation.OutgoingTransfer
            : AccountOperation.ExternalCredit;

        if (account.Permits(required) is StatusPermission.Denied or StatusPermission.HistoryOnly)
        {
            return Result<PaymentPreferenceView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        UtcTimestamp now = clock.Now();
        PaymentPreference? existing = unitOfWork.PaymentPreferences.Find(
            command.CustomerAccountId, command.Kind);

        if (existing is null)
        {
            PaymentPreference created = PaymentPreference.Select(
                PaymentPreferenceId.FromValue(idGenerator.NextId()),
                command.CustomerAccountId,
                command.Kind,
                account.Id,
                now);

            unitOfWork.PaymentPreferences.Add(created);
        }
        else
        {
            existing.Reselect(account.Id);
            unitOfWork.PaymentPreferences.Update(existing);
        }

        return Result<PaymentPreferenceView>.Success(new PaymentPreferenceView(
            command.Kind, account.Id, account.AccountNumber.Suffix));
    }

    private readonly record struct ReservedTransfer(PaymentOrderId OrderId, SettlementMode Mode);

    private readonly record struct TransferRequest(
        InstitutionCode InstitutionCode,
        BranchCode BranchCode,
        AccountNumber AccountNumber,
        MoneyMinor Amount);

    private static bool TryValidate(
        CreatePaymentOrderCommand command,
        out TransferRequest request,
        out ApplicationError? error)
    {
        request = default;
        error = null;

        if (!InstitutionCode.TryParse(command.DestinationInstitutionCode, out InstitutionCode institutionCode))
        {
            error = ApplicationError.Create(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound, nameof(command.DestinationInstitutionCode));
            return false;
        }

        if (!BranchCode.TryParse(command.DestinationBranchCode, out BranchCode branchCode))
        {
            error = ApplicationError.Create(
                ErrorCategory.NotFound,
                BankingErrorCodes.DepositAccountNotFound,
                nameof(command.DestinationBranchCode));
            return false;
        }

        if (!AccountNumber.TryParse(command.DestinationAccountNumber, out AccountNumber accountNumber))
        {
            error = ApplicationError.Create(
                ErrorCategory.NotFound,
                BankingErrorCodes.DepositAccountNotFound,
                nameof(command.DestinationAccountNumber));
            return false;
        }

        if (command.AmountMinor < 1)
        {
            error = ApplicationError.Create(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(command.AmountMinor));
            return false;
        }

        if (command.Memo is { } memo && memo.Length > PaymentOrder.MaximumMemoLength)
        {
            error = ApplicationError.Create(
                ErrorCategory.Validation, BankingErrorCodes.MemoTooLong, nameof(command.Memo));
            return false;
        }

        request = new TransferRequest(
            institutionCode, branchCode, accountNumber, MoneyMinor.FromMinor(command.AmountMinor));
        return true;
    }

    private Result<ReservedTransfer> ReserveFunds(
        IBankingUnitOfWork unitOfWork,
        CreatePaymentOrderCommand command,
        TransferRequest request,
        IdempotencyKey idempotencyKey)
    {
        BusinessOperation? existing = unitOfWork.BusinessOperations.Find(idempotencyKey);
        if (existing is not null)
        {
            PaymentOrder? replayed = unitOfWork.PaymentOrders.FindByBusinessOperation(existing.Id);

            return replayed is not null
                ? Result<ReservedTransfer>.Success(new ReservedTransfer(replayed.Id, replayed.SettlementMode))
                : Result<ReservedTransfer>.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        CustomerAccount? payer = unitOfWork.CustomerAccounts.Find(command.PayerCustomerAccountId);
        if (payer is null)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        DepositAccount? source = unitOfWork.DepositAccounts.Find(command.SourceDepositAccountId);
        if (source is null || source.CustomerAccountId != command.PayerCustomerAccountId)
        {
            return Denied(TargetAccess.NotOwned);
        }

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        Bank? sourceBank = unitOfWork.Banks.Find(source.BankId);
        if (sourceBank is null || sourceBank.Status != BankStatus.Operating)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Bank? destinationBank = unitOfWork.Banks.FindByInstitutionCode(
            command.EconomyScopeId, request.InstitutionCode.Value);
        if (destinationBank is null)
        {
            return Result<ReservedTransfer>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        bool interbank = destinationBank.Id != sourceBank.Id;

        BranchId? branchId = unitOfWork.Branches.FindIdByCode(destinationBank.Id, request.BranchCode.Value);
        if (branchId is not { } branch)
        {
            return Denied(TargetAccess.Missing);
        }

        DepositAccount? destination = unitOfWork.DepositAccounts.FindByRouting(
            destinationBank.Id, branch, request.AccountNumber);
        if (destination is null)
        {
            return Denied(TargetAccess.Missing);
        }

        if (destination.Id == source.Id)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.SelfTransferRejected);
        }

        if (destination.CurrencyId != source.CurrencyId)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        if (interbank)
        {
            Result eligibility = InterbankSettlementPolicy.EnsureEligible(
                unitOfWork, sourceBank, destinationBank);

            if (!eligibility.IsSuccess)
            {
                return Result<ReservedTransfer>.Failure(eligibility.Error!);
            }
        }

        UtcTimestamp now = clock.Now();

        BusinessTimePoint? resolved = EconomyBusinessCalendar.Resolve(
            unitOfWork.EconomyCalendars, sourceBank.EconomyScopeId, now);

        if (resolved is not { } point)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result limits = TransferLimitPolicy.Evaluate(
            unitOfWork, sourceBank, source, request.Amount, point, nameof(command.AmountMinor));

        if (!limits.IsSuccess)
        {
            return Result<ReservedTransfer>.Failure(limits.Error!);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            sourceBank,
            source,
            interbank ? FeeType.InterbankTransfer : FeeType.SameBankTransfer,
            TransferChannel,
            destinationBank.Id,
            request.Amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<ReservedTransfer>.Failure(fee.Error!);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(sourceBank.GeneralLedgerBookId, BusinessDateOf(now)) is null)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        MoneyMinor totalDebit = request.Amount.Add(fee.Value.Quote.Amount);

        LedgerBalance sourceBalance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        Result holdLimit = TransferLimitPolicy.EvaluateActiveHolds(
            unitOfWork, sourceBank, sourceBalance.HeldAmount, totalDebit);

        if (!holdLimit.IsSuccess)
        {
            return Result<ReservedTransfer>.Failure(holdLimit.Error!);
        }

        if (!sourceBalance.CanReserve(totalDebit))
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            command.EconomyScopeId,
            payer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        PaymentOrder order = PaymentOrder.Create(
            PaymentOrderId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.PayerCustomerAccountId,
            source.Id,
            destination.Id,
            source.CurrencyId,
            request.Amount,
            interbank ? InterbankPaymentMethod : PaymentMethod,
            interbank ? SettlementMode.Rtgs : SettlementMode.Internal,
            interbank
                ? BeneficiaryPostingPolicy.AfterFinalSettlement
                : BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
            command.Memo,
            now);

        order.Authorize();

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            source.Id,
            operation.Id,
            totalDebit,
            HoldReason,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            source.LedgerAccountId, sourceBalance.IncreaseHold(totalDebit), now);

        order.HoldFunds();
        unitOfWork.PaymentOrders.Add(order);

        return Result<ReservedTransfer>.Success(new ReservedTransfer(order.Id, order.SettlementMode));
    }

    private Result<PaymentOrderView> PostTransfer(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId,
        IdempotencyKey idempotencyKey)
    {
        PaymentOrder? order = unitOfWork.PaymentOrders.Find(paymentOrderId);
        if (order is null)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        DepositAccount source = unitOfWork.DepositAccounts.Find(order.SourceDepositAccountId)!;
        DepositAccount destination = unitOfWork.DepositAccounts.Find(order.DestinationDepositAccountId)!;

        Hold? hold = unitOfWork.Holds.FindByBusinessOperation(order.BusinessOperationId);
        if (hold is null)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        MoneyMinor feeAmount = hold.Amount.Subtract(order.Amount);

        if (order.Status == PaymentOrderStatus.Completed)
        {
            return Result<PaymentOrderView>.Success(
                ToView(unitOfWork, order, feeAmount, source, destination));
        }

        if (order.Status != PaymentOrderStatus.FundsHeld)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        if (hold.Status != HoldStatus.Active)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        Bank bank = unitOfWork.Banks.Find(source.BankId)!;
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        AccountingPeriodId? periodId = unitOfWork.AccountingPeriods.FindOpen(
            bank.GeneralLedgerBookId, businessDate);
        if (periodId is not { } period)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            bank,
            source,
            FeeTypeOf(order),
            TransferChannel,
            destination.BankId,
            order.Amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(fee.Error!);
        }

        FeeAssessmentPlan plan = fee.Value;
        if (plan.Quote.Amount != hold.Amount.Subtract(order.Amount))
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.FeeQuoteStale);
        }

        LedgerAccount sourceLedger = unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!;
        LedgerAccount destinationLedger = unitOfWork.LedgerAccounts.Find(destination.LedgerAccountId)!;

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.DepositReleasingHold(sourceLedger, EntrySide.Debit, order.Amount, hold.Amount));
        posting.Add(PostingLine.Deposit(destinationLedger, EntrySide.Credit, order.Amount));

        if (plan.RequiresPosting)
        {
            posting.Add(PostingLine.Deposit(sourceLedger, EntrySide.Debit, plan.Quote.Amount));
            posting.Add(PostingLine.Institutional(plan.RevenueAccount, EntrySide.Credit, plan.Quote.Amount));
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        AccountingTransaction transaction = AccountingTransaction.Post(
            AccountingTransactionId.FromValue(idGenerator.NextId()),
            bank.GeneralLedgerBookId,
            order.BusinessOperationId,
            order.CurrencyId,
            businessDate,
            now,
            now,
            TransactionType,
            DescriptionCode,
            posting.BuildDrafts(ordered, idGenerator),
            LedgerAccountSet.From(ordered));

        unitOfWork.AccountingTransactions.Add(transaction, period);

        hold.Capture(hold.Amount, now);
        unitOfWork.Holds.Update(hold);

        posting.ApplyProjections(unitOfWork, ordered, now);
        RecordFee(unitOfWork, order, plan, source, sourceLedger, now);

        order.CompleteInternalTransfer(now);
        unitOfWork.PaymentOrders.Update(order);

        BusinessOperation operation = unitOfWork.BusinessOperations.Find(idempotencyKey)!;
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        source.RecordCustomerActivity(now);
        unitOfWork.DepositAccounts.Update(source);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            CompletedEventType,
            Payload(order),
            now));

        return Result<PaymentOrderView>.Success(
            ToView(unitOfWork, order, plan.Quote.Amount, source, destination));
    }

    private Result<PaymentOrderView> PostSourceDebit(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId)
    {
        PaymentOrder order = unitOfWork.PaymentOrders.Find(paymentOrderId)!;
        DepositAccount source = unitOfWork.DepositAccounts.Find(order.SourceDepositAccountId)!;
        DepositAccount destination = unitOfWork.DepositAccounts.Find(order.DestinationDepositAccountId)!;

        Hold? hold = unitOfWork.Holds.FindByBusinessOperation(order.BusinessOperationId);
        if (hold is null)
        {
            return Conflict();
        }

        MoneyMinor feeAmount = hold.Amount.Subtract(order.Amount);

        if (order.Status != PaymentOrderStatus.FundsHeld)
        {
            return order.Status is PaymentOrderStatus.Created or PaymentOrderStatus.Authorized
                ? Conflict()
                : Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

        if (hold.Status != HoldStatus.Active)
        {
            return Conflict();
        }

        Bank bank = unitOfWork.Banks.Find(source.BankId)!;
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate) is not { } period)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(unitOfWork.EconomyCalendars, bank.EconomyScopeId, now)
            is not { } point)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            bank,
            source,
            FeeTypeOf(order),
            TransferChannel,
            destination.BankId,
            order.Amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(fee.Error!);
        }

        FeeAssessmentPlan plan = fee.Value;
        if (plan.Quote.Amount != feeAmount)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.FeeQuoteStale);
        }

        LedgerAccount sourceLedger = unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!;
        LedgerAccount? payable = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.SettlementPayable, order.CurrencyId);

        if (payable is null)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.DepositReleasingHold(sourceLedger, EntrySide.Debit, order.Amount, hold.Amount));
        posting.Add(PostingLine.Institutional(payable, EntrySide.Credit, order.Amount));

        if (plan.RequiresPosting)
        {
            posting.Add(PostingLine.Deposit(sourceLedger, EntrySide.Debit, plan.Quote.Amount));
            posting.Add(PostingLine.Institutional(plan.RevenueAccount, EntrySide.Credit, plan.Quote.Amount));
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                InterbankTransactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        hold.Capture(hold.Amount, now);
        unitOfWork.Holds.Update(hold);

        posting.ApplyProjections(unitOfWork, ordered, now);
        RecordFee(unitOfWork, order, plan, source, sourceLedger, now);

        unitOfWork.SettlementInstructions.Add(SettlementInstruction.Create(
            SettlementInstructionId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            order.CurrencyId,
            source.BankId,
            destination.BankId,
            order.Amount,
            now));

        order.Accept();
        unitOfWork.PaymentOrders.Update(order);

        source.RecordCustomerActivity(now);
        unitOfWork.DepositAccounts.Update(source);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            AcceptedEventType,
            Payload(order),
            now));

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    private Result<PaymentOrderView> SettleInterbank(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId)
    {
        PaymentOrder order = unitOfWork.PaymentOrders.Find(paymentOrderId)!;
        DepositAccount source = unitOfWork.DepositAccounts.Find(order.SourceDepositAccountId)!;
        DepositAccount destination = unitOfWork.DepositAccounts.Find(order.DestinationDepositAccountId)!;
        Hold hold = unitOfWork.Holds.FindByBusinessOperation(order.BusinessOperationId)!;
        MoneyMinor feeAmount = hold.Amount.Subtract(order.Amount);

        if (order.Status is not (PaymentOrderStatus.Accepted or PaymentOrderStatus.Queued))
        {
            return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

        SettlementInstruction? instruction =
            unitOfWork.SettlementInstructions.FindByBusinessOperation(order.BusinessOperationId);

        if (instruction is null)
        {
            return Conflict();
        }

        Bank sourceBank = unitOfWork.Banks.Find(source.BankId)!;
        Bank destinationBank = unitOfWork.Banks.Find(destination.BankId)!;
        UtcTimestamp now = clock.Now();

        Result<InterbankSettlementAccounts> resolved = InterbankSettlementPolicy.ResolveAccounts(
            unitOfWork, sourceBank, destinationBank, order.CurrencyId);

        if (!resolved.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(resolved.Error!);
        }

        InterbankSettlementAccounts accounts = resolved.Value;
        LedgerBalance reserve = unitOfWork.LedgerAccounts.FindProjection(accounts.SourceReserve.Id)
            ?? LedgerBalance.Empty;

        if (!reserve.CanReserve(order.Amount))
        {
            if (instruction.Status == SettlementInstructionStatus.Created)
            {
                instruction.Queue();
                unitOfWork.SettlementInstructions.Update(instruction);
            }

            if (order.Status == PaymentOrderStatus.Accepted)
            {
                order.Queue();
                unitOfWork.PaymentOrders.Update(order);
            }

            return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

        BusinessDate businessDate = BusinessDateOf(now);

        Result<AccountingPeriodId[]> periods = OpenPeriods(
            unitOfWork,
            businessDate,
            sourceBank.GeneralLedgerBookId,
            accounts.CentralBankBookId,
            destinationBank.GeneralLedgerBookId);

        if (!periods.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(periods.Error!);
        }

        instruction.LockForSettlement(now);
        order.BeginSettling();

        PostSettlementLeg(
            unitOfWork,
            sourceBank.GeneralLedgerBookId,
            periods.Value[0],
            order,
            businessDate,
            now,
            accounts.SourcePayable,
            accounts.SourceReserve);

        PostSettlementLeg(
            unitOfWork,
            accounts.CentralBankBookId,
            periods.Value[1],
            order,
            businessDate,
            now,
            accounts.SourceCentralBankLiability,
            accounts.DestinationCentralBankLiability);

        PostSettlementLeg(
            unitOfWork,
            destinationBank.GeneralLedgerBookId,
            periods.Value[2],
            order,
            businessDate,
            now,
            accounts.DestinationReserve,
            accounts.DestinationSuspense);

        instruction.Settle(now);
        unitOfWork.SettlementInstructions.Update(instruction);

        order.RecordSettlementFinality(now);
        order.Settle();
        unitOfWork.PaymentOrders.Update(order);

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    private Result<PaymentOrderView> PostBeneficiaryCredit(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId,
        IdempotencyKey idempotencyKey)
    {
        PaymentOrder order = unitOfWork.PaymentOrders.Find(paymentOrderId)!;
        DepositAccount source = unitOfWork.DepositAccounts.Find(order.SourceDepositAccountId)!;
        DepositAccount destination = unitOfWork.DepositAccounts.Find(order.DestinationDepositAccountId)!;
        Hold hold = unitOfWork.Holds.FindByBusinessOperation(order.BusinessOperationId)!;
        MoneyMinor feeAmount = hold.Amount.Subtract(order.Amount);

        if (order.Status != PaymentOrderStatus.Settled)
        {
            return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

        Bank destinationBank = unitOfWork.Banks.Find(destination.BankId)!;
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        if (unitOfWork.AccountingPeriods.FindOpen(destinationBank.GeneralLedgerBookId, businessDate)
            is not { } period)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount? suspense = unitOfWork.LedgerAccounts.FindPostingByKind(
            destinationBank.GeneralLedgerBookId,
            LedgerAccountKind.IncomingSettlementSuspense,
            order.CurrencyId);

        if (suspense is null)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
        }

        LedgerAccount destinationLedger = unitOfWork.LedgerAccounts.Find(destination.LedgerAccountId)!;

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(suspense, EntrySide.Debit, order.Amount));
        posting.Add(PostingLine.Deposit(destinationLedger, EntrySide.Credit, order.Amount));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                destinationBank.GeneralLedgerBookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                BeneficiaryTransactionType,
                BeneficiaryDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        order.RecordBeneficiaryPosting(now);
        order.Complete(now);
        unitOfWork.PaymentOrders.Update(order);

        BusinessOperation operation = unitOfWork.BusinessOperations.Find(idempotencyKey)!;
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            CompletedEventType,
            Payload(order),
            now));

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    private void PostSettlementLeg(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        AccountingPeriodId period,
        PaymentOrder order,
        BusinessDate businessDate,
        UtcTimestamp now,
        LedgerAccount debit,
        LedgerAccount credit)
    {
        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(debit, EntrySide.Debit, order.Amount));
        posting.Add(PostingLine.Institutional(credit, EntrySide.Credit, order.Amount));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                SettlementTransactionType,
                SettlementDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);
    }

    private static Result<AccountingPeriodId[]> OpenPeriods(
        IBankingUnitOfWork unitOfWork,
        BusinessDate businessDate,
        params AccountingBookId[] books)
    {
        AccountingPeriodId[] periods = new AccountingPeriodId[books.Length];

        for (int index = 0; index < books.Length; index++)
        {
            if (unitOfWork.AccountingPeriods.FindOpen(books[index], businessDate) is not { } period)
            {
                return Result<AccountingPeriodId[]>.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
            }

            periods[index] = period;
        }

        return Result<AccountingPeriodId[]>.Success(periods);
    }

    private static FeeType FeeTypeOf(PaymentOrder order) =>
        order.SettlementMode == SettlementMode.Internal
            ? FeeType.SameBankTransfer
            : FeeType.InterbankTransfer;

    private static Result<PaymentOrderView> Conflict() => Result<PaymentOrderView>.Failure(
        ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);

    private static string Payload(PaymentOrder order) =>
        $$"""{"payment_order_id":"{{order.Id.Value}}","amount_minor":{{order.Amount.Value}}}""";

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);

    private void RecordFee(
        IBankingUnitOfWork unitOfWork,
        PaymentOrder order,
        FeeAssessmentPlan plan,
        DepositAccount source,
        LedgerAccount sourceLedger,
        UtcTimestamp now)
    {
        if (!plan.RequiresRecord)
        {
            return;
        }

        unitOfWork.FeeAssessments.Add(FeeAssessment.Assess(
            FeeAssessmentId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            plan.Quote.ScheduleVersionId,
            plan.Quote.RuleId,
            order.CurrencyId,
            sourceLedger.Id,
            plan.RevenueAccount.Id,
            plan.Quote.Type,
            plan.Quote.Amount,
            now));

        if (plan.Quote.WaiverApplied && plan.Quote.WaiverCounterKey is { } waiverCounterKey)
        {
            unitOfWork.FeeWaiverCounters.Consume(source.Id, waiverCounterKey, plan.BusinessMonth);
        }
    }

    private static PaymentOrderView ToView(
        IBankingUnitOfWork unitOfWork,
        PaymentOrder order,
        MoneyMinor feeAmount,
        DepositAccount source,
        DepositAccount destination)
    {
        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        return new PaymentOrderView(
            order.Id,
            order.Status,
            order.Amount,
            feeAmount,
            order.Amount.Add(feeAmount),
            destination.AccountNumber.Value,
            balance.PostedBalance,
            balance.AvailableBalance);
    }

    private static Result<ReservedTransfer> Denied(TargetAccess access)
    {
        ApplicationError error = TargetAccessPolicy.ToError(
            access, BankingErrorCodes.DepositAccountNotFound, BankingErrorCodes.DepositAccountNotOperable);

        return Result<ReservedTransfer>.Failure(error.Category, error.Code);
    }
}
