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

public interface IPaymentApplicationService
{
    Task<Result<PaymentOrderView>> CreatePaymentOrderAsync(
        CreatePaymentOrderCommand command,
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

    private const FeeChannel TransferChannel = FeeChannel.Discord;
    private const FeeType TransferFeeType = FeeType.SameBankTransfer;

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

        Result<PaymentOrderId> held = await writeGateway.ExecuteAsync(
            unitOfWork => ReserveFunds(unitOfWork, command, request, idempotencyKey),
            cancellationToken).ConfigureAwait(false);

        if (!held.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(held.Error!.Category, held.Error.Code, held.Error.Field);
        }

        return await writeGateway.ExecuteAsync(
            unitOfWork => PostTransfer(unitOfWork, held.Value, idempotencyKey),
            cancellationToken).ConfigureAwait(false);
    }

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

    private Result<PaymentOrderId> ReserveFunds(
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
                ? Result<PaymentOrderId>.Success(replayed.Id)
                : Result<PaymentOrderId>.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        CustomerAccount? payer = unitOfWork.CustomerAccounts.Find(command.PayerCustomerAccountId);
        if (payer is null)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        DepositAccount? source = unitOfWork.DepositAccounts.Find(command.SourceDepositAccountId);
        if (source is null || source.CustomerAccountId != command.PayerCustomerAccountId)
        {
            return Denied(TargetAccess.NotOwned);
        }

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        Bank? sourceBank = unitOfWork.Banks.Find(source.BankId);
        if (sourceBank is null || sourceBank.Status != BankStatus.Operating)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Bank? destinationBank = unitOfWork.Banks.FindByInstitutionCode(
            command.EconomyScopeId, request.InstitutionCode.Value);
        if (destinationBank is null)
        {
            return Result<PaymentOrderId>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (destinationBank.Id != sourceBank.Id)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.InterbankTransferUnavailable);
        }

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
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.SelfTransferRejected);
        }

        if (destination.CurrencyId != source.CurrencyId)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        UtcTimestamp now = clock.Now();

        BusinessTimePoint? resolved = EconomyBusinessCalendar.Resolve(
            unitOfWork.EconomyCalendars, sourceBank.EconomyScopeId, now);

        if (resolved is not { } point)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result limits = TransferLimitPolicy.Evaluate(
            unitOfWork, sourceBank, source, request.Amount, point, nameof(command.AmountMinor));

        if (!limits.IsSuccess)
        {
            return Result<PaymentOrderId>.Failure(limits.Error!);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            sourceBank,
            source,
            TransferFeeType,
            TransferChannel,
            destinationBank.Id,
            request.Amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<PaymentOrderId>.Failure(fee.Error!);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(sourceBank.GeneralLedgerBookId, BusinessDateOf(now)) is null)
        {
            return Result<PaymentOrderId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        MoneyMinor totalDebit = request.Amount.Add(fee.Value.Quote.Amount);

        LedgerBalance sourceBalance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        Result holdLimit = TransferLimitPolicy.EvaluateActiveHolds(
            unitOfWork, sourceBank, sourceBalance.HeldAmount, totalDebit);

        if (!holdLimit.IsSuccess)
        {
            return Result<PaymentOrderId>.Failure(holdLimit.Error!);
        }

        if (!sourceBalance.CanReserve(totalDebit))
        {
            return Result<PaymentOrderId>.Failure(
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
            PaymentMethod,
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
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

        return Result<PaymentOrderId>.Success(order.Id);
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
            TransferFeeType,
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
        LedgerAccount[] ordered = plan.RequiresPosting
            ? [sourceLedger, destinationLedger, plan.RevenueAccount]
            : [sourceLedger, destinationLedger];

        Array.Sort(ordered, static (left, right) => left.Id.Value.CompareTo(right.Id.Value));

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
            BuildDrafts(ordered, order, plan, sourceLedger, destinationLedger),
            LedgerAccountSet.From(ordered));

        unitOfWork.AccountingTransactions.Add(transaction, period);

        hold.Capture(hold.Amount, now);
        unitOfWork.Holds.Update(hold);

        ApplyProjections(unitOfWork, ordered, order, plan, sourceLedger, destinationLedger, now);
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
            $$"""{"payment_order_id":"{{order.Id.Value}}","amount_minor":{{order.Amount.Value}}}""",
            now));

        return Result<PaymentOrderView>.Success(
            ToView(unitOfWork, order, plan.Quote.Amount, source, destination));
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);

    private JournalEntryDraft[] BuildDrafts(
        LedgerAccount[] ordered,
        PaymentOrder order,
        FeeAssessmentPlan plan,
        LedgerAccount sourceLedger,
        LedgerAccount destinationLedger)
    {
        List<JournalEntryDraft> drafts = new(plan.RequiresPosting ? 4 : 2);

        foreach (LedgerAccount ledger in ordered)
        {
            if (ledger.Id == sourceLedger.Id)
            {
                drafts.Add(Draft(sourceLedger, EntrySide.Debit, order.Amount));

                if (plan.RequiresPosting)
                {
                    drafts.Add(Draft(sourceLedger, EntrySide.Debit, plan.Quote.Amount));
                }
            }
            else if (ledger.Id == destinationLedger.Id)
            {
                drafts.Add(Draft(destinationLedger, EntrySide.Credit, order.Amount));
            }
            else
            {
                drafts.Add(Draft(plan.RevenueAccount, EntrySide.Credit, plan.Quote.Amount));
            }
        }

        return [.. drafts];
    }

    private JournalEntryDraft Draft(LedgerAccount ledger, EntrySide side, MoneyMinor amount) =>
        new(JournalEntryId.FromValue(idGenerator.NextId()), ledger.Id, side, amount);

    private static void ApplyProjections(
        IBankingUnitOfWork unitOfWork,
        LedgerAccount[] ordered,
        PaymentOrder order,
        FeeAssessmentPlan plan,
        LedgerAccount sourceLedger,
        LedgerAccount destinationLedger,
        UtcTimestamp now)
    {
        foreach (LedgerAccount ledger in ordered)
        {
            LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(ledger.Id) ?? LedgerBalance.Empty;

            if (ledger.Id == sourceLedger.Id)
            {
                MoneyMinor debited = order.Amount.Add(plan.Quote.Amount);
                balance = balance
                    .ApplyPosting(EntrySide.Debit, ledger.NormalSide, debited)
                    .DecreaseHold(debited)
                    .EnsureDepositAccountInvariants();
            }
            else if (ledger.Id == destinationLedger.Id)
            {
                balance = balance
                    .ApplyPosting(EntrySide.Credit, ledger.NormalSide, order.Amount)
                    .EnsureDepositAccountInvariants();
            }
            else
            {
                balance = balance.ApplyPosting(EntrySide.Credit, ledger.NormalSide, plan.Quote.Amount);
            }

            unitOfWork.LedgerAccounts.UpsertProjection(ledger.Id, balance, now);
        }
    }

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

    private static Result<PaymentOrderId> Denied(TargetAccess access)
    {
        ApplicationError error = TargetAccessPolicy.ToError(
            access, BankingErrorCodes.DepositAccountNotFound, BankingErrorCodes.DepositAccountNotOperable);

        return Result<PaymentOrderId>.Failure(error.Category, error.Code);
    }
}
