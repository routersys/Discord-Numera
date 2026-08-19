using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record CreatePaymentOrderCommand(
    ulong GuildId,
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

public sealed record PrepareTransferToCustomerQuery(
    ulong GuildId,
    CustomerAccountId PayerCustomerAccountId,
    DepositAccountId SourceDepositAccountId,
    ulong BeneficiaryDiscordUserId);

public sealed record TransferDestinationCandidate(
    DepositAccountId DepositAccountId,
    string InstitutionCode,
    string BranchCode,
    string AccountNumber,
    string BankName)
{
    public string AccountNumberSuffix =>
        AccountNumber is { Length: >= Numera.Domain.Banking.AccountNumber.SuffixLength } number
            ? number[^Numera.Domain.Banking.AccountNumber.SuffixLength..]
            : AccountNumber;
}

public sealed record TransferPreparationView(
    DepositAccountId SourceDepositAccountId,
    IReadOnlyList<TransferDestinationCandidate> Candidates);

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
    Task<Result<TransferPreparationView>> PrepareTransferToCustomerAsync(
        PrepareTransferToCustomerQuery query,
        CancellationToken cancellationToken);

    Task<Result<PaymentOrderView>> CreatePaymentOrderAsync(
        CreatePaymentOrderCommand command,
        CancellationToken cancellationToken);

    Task<Result<PaymentPreferenceView>> SetPaymentPreferenceAsync(
        SetPaymentPreferenceCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class PaymentApplicationService : IPaymentApplicationService
{
    public const string OperationType = "PAYMENT_TRANSFER";
    public const string CompletedEventType = "PAYMENT_COMPLETED";
    public const string TransactionType = "INTERNAL_TRANSFER";
    public const string DescriptionCode = "TRANSFER";
    public const string HoldReason = "TRANSFER";
    public const string PaymentMethod = "INTERNAL_TRANSFER";
    public const string InterbankPaymentMethod = "RTGS_TRANSFER";
    public const string ClearingPaymentMethod = "CLEARING_TRANSFER";
    public const string ClearingTransactionType = "CLEARING_TRANSFER";
    public const string ClearingReceivableTransactionType = "CLEARING_RECEIVABLE";
    public const string ClearingInstructionKind = "RETAIL_CREDIT_TRANSFER";
    public const string InterbankTransactionType = "RTGS_TRANSFER";
    public const string SettlementTransactionType = "RTGS_SETTLEMENT";
    public const string BeneficiaryTransactionType = "RTGS_BENEFICIARY_CREDIT";
    public const string SettlementDescriptionCode = "SETTLEMENT";
    public const string BeneficiaryDescriptionCode = "BENEFICIARY_CREDIT";
    public const string AcceptedEventType = "PAYMENT_ACCEPTED";
    public const string CancelledEventType = "PAYMENT_CANCELLED";
    public const string ReversalOperationType = "PAYMENT_REVERSAL";
    public const string ReversalTransactionType = "RTGS_CANCELLATION";
    public const string ReversalDescriptionCode = "REVERSAL";

    private const FeeChannel TransferChannel = FeeChannel.Discord;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IBankingReadGateway readGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PaymentApplicationService(
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

        if (held.Value.Mode == SettlementMode.Clearing)
        {
            return await writeGateway.ExecuteAsync(
                unitOfWork => PostClearingDebit(unitOfWork, orderId),
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
            unitOfWork => PostBeneficiaryCredit(unitOfWork, orderId),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<TransferPreparationView>> PrepareTransferToCustomerAsync(
        PrepareTransferToCustomerQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context => PrepareTransfer(context, query)));
    }

    private static Result<TransferPreparationView> PrepareTransfer(
        IBankingReadContext context,
        PrepareTransferToCustomerQuery query)
    {
        ITransferPreparationReadRepository repository = context.TransferPreparation;

        if (context.EconomyScopes.FindByGuild(query.GuildId) is not { } economyScopeId)
        {
            return Result<TransferPreparationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (repository.FindOwnedSource(query.PayerCustomerAccountId, query.SourceDepositAccountId)
            is not { } source)
        {
            ApplicationError denied = TargetAccessPolicy.ToError(
                TargetAccess.NotOwned,
                BankingErrorCodes.DepositAccountNotFound,
                BankingErrorCodes.DepositAccountNotOperable);

            return Result<TransferPreparationView>.Failure(denied.Category, denied.Code);
        }

        CustomerAccountId? beneficiary = repository.FindCustomerByDiscordUser(
            economyScopeId,
            query.BeneficiaryDiscordUserId.ToString(CultureInfo.InvariantCulture));

        if (beneficiary is not { } beneficiaryCustomerAccountId)
        {
            return Result<TransferPreparationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        IReadOnlyList<TransferDestinationCandidate> candidates = repository.ListPublicReceivingAccounts(
            beneficiaryCustomerAccountId,
            source.CurrencyId,
            source.Id,
            PaginationBudget.SelectCandidatePageSize);

        return candidates.Count == 0
            ? Result<TransferPreparationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound)
            : Result<TransferPreparationView>.Success(new TransferPreparationView(source.Id, candidates));
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

    internal readonly record struct ReservedTransfer(PaymentOrderId OrderId, SettlementMode Mode);

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

    internal Result<ReservedTransfer> ReserveOpeningFunding(
        IBankingUnitOfWork unitOfWork,
        CreatePaymentOrderCommand command,
        IdempotencyKey idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TryValidate(command, out TransferRequest request, out ApplicationError? error)
            ? ReserveFunds(unitOfWork, command, request, idempotencyKey, fundsAccountOpening: true)
            : Result<ReservedTransfer>.Failure(error!.Category, error.Code, error.Field);
    }

    private Result<ReservedTransfer> ReserveFunds(
        IBankingUnitOfWork unitOfWork,
        CreatePaymentOrderCommand command,
        TransferRequest request,
        IdempotencyKey idempotencyKey,
        bool fundsAccountOpening = false)
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

        if (unitOfWork.GuildEconomies.FindEconomyScope(command.GuildId) is not { } economyScopeId)
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
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
            economyScopeId, request.InstitutionCode.Value);
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

        if (!AcceptsCredit(destination, fundsAccountOpening))
        {
            return Result<ReservedTransfer>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        Result<PaymentRoute> routed = PaymentRoutePolicy.Resolve(
            unitOfWork, economyScopeId, interbank, request.Amount);

        if (!routed.IsSuccess)
        {
            return Result<ReservedTransfer>.Failure(routed.Error!);
        }

        PaymentRoute route = routed.Value;

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
            economyScopeId,
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
            route.Method,
            route.Mode,
            route.PostingPolicy,
            route.PolicyVersionId,
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

        Result<FeeAssessmentPlan> fee = ResolveOrderFee(
            unitOfWork, bank, source, destination, order, point);

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

    internal Result<PaymentOrderView> PostSourceDebit(
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

        Result<FeeAssessmentPlan> fee = ResolveOrderFee(
            unitOfWork, bank, source, destination, order, point);

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

    private Result<PaymentOrderView> PostClearingDebit(
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

        Bank sourceBank = unitOfWork.Banks.Find(source.BankId)!;
        Bank destinationBank = unitOfWork.Banks.Find(destination.BankId)!;
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        if (order.PaymentNetworkPolicyVersionId is not { } policyVersionId ||
            unitOfWork.PaymentNetworks.FindPolicy(policyVersionId) is not { } policy)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PaymentNetworkPolicyUnavailable);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(sourceBank.GeneralLedgerBookId, businessDate)
            is not { } sourcePeriod ||
            unitOfWork.AccountingPeriods.FindOpen(destinationBank.GeneralLedgerBookId, businessDate)
            is not { } destinationPeriod)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(unitOfWork.EconomyCalendars, sourceBank.EconomyScopeId, now)
            is not { } point)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            sourceBank,
            source,
            FeeTypeOf(order),
            ChannelOf(order),
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

        LedgerAccount? payable = unitOfWork.LedgerAccounts.FindPostingByKind(
            sourceBank.GeneralLedgerBookId, LedgerAccountKind.ClearingPayable, order.CurrencyId);

        LedgerAccount? receivable = unitOfWork.LedgerAccounts.FindPostingByKind(
            destinationBank.GeneralLedgerBookId, LedgerAccountKind.ClearingReceivable, order.CurrencyId);

        LedgerAccount? suspense = unitOfWork.LedgerAccounts.FindPostingByKind(
            destinationBank.GeneralLedgerBookId, LedgerAccountKind.IncomingSettlementSuspense, order.CurrencyId);

        bool netSettleable = unitOfWork.LedgerAccounts.FindPostingByKind(
                sourceBank.GeneralLedgerBookId, LedgerAccountKind.ClearingReceivable, order.CurrencyId) is not null &&
            unitOfWork.LedgerAccounts.FindPostingByKind(
                destinationBank.GeneralLedgerBookId, LedgerAccountKind.ClearingPayable, order.CurrencyId) is not null;

        if (payable is null || receivable is null || suspense is null || !netSettleable)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
        }

        Result<ClearingCycle> cycle = ResolveOpenCycle(unitOfWork, sourceBank, order, policy, now);
        if (!cycle.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(cycle.Error!);
        }

        LedgerAccount sourceLedger = unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!;

        LedgerPostingBuilder acceptance = new();
        acceptance.Add(PostingLine.DepositReleasingHold(sourceLedger, EntrySide.Debit, order.Amount, hold.Amount));
        acceptance.Add(PostingLine.Institutional(payable, EntrySide.Credit, order.Amount));

        if (plan.RequiresPosting)
        {
            acceptance.Add(PostingLine.Deposit(sourceLedger, EntrySide.Debit, plan.Quote.Amount));
            acceptance.Add(PostingLine.Institutional(plan.RevenueAccount, EntrySide.Credit, plan.Quote.Amount));
        }

        LedgerAccount[] acceptanceAccounts = acceptance.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                sourceBank.GeneralLedgerBookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                ClearingTransactionType,
                DescriptionCode,
                acceptance.BuildDrafts(acceptanceAccounts, idGenerator),
                LedgerAccountSet.From(acceptanceAccounts)),
            sourcePeriod);

        hold.Capture(hold.Amount, now);
        unitOfWork.Holds.Update(hold);

        acceptance.ApplyProjections(unitOfWork, acceptanceAccounts, now);
        RecordFee(unitOfWork, order, plan, source, sourceLedger, now);

        bool preCredited = order.BeneficiaryPostingPolicy == BeneficiaryPostingPolicy.GuaranteedPreCredit &&
            unitOfWork.PaymentNetworks.FindRouting(sourceBank.EconomyScopeId) is { } network &&
            PaymentRoutePolicy.CoversPreCredit(
                unitOfWork, network, policy, source.BankId, order.CurrencyId, order.Amount);

        LedgerPostingBuilder claim = new();
        claim.Add(PostingLine.Institutional(receivable, EntrySide.Debit, order.Amount));

        claim.Add(preCredited
            ? PostingLine.Deposit(
                unitOfWork.LedgerAccounts.Find(destination.LedgerAccountId)!, EntrySide.Credit, order.Amount)
            : PostingLine.Institutional(suspense, EntrySide.Credit, order.Amount));

        LedgerAccount[] claimAccounts = claim.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                destinationBank.GeneralLedgerBookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                ClearingReceivableTransactionType,
                SettlementDescriptionCode,
                claim.BuildDrafts(claimAccounts, idGenerator),
                LedgerAccountSet.From(claimAccounts)),
            destinationPeriod);

        claim.ApplyProjections(unitOfWork, claimAccounts, now);

        ClearingInstruction instruction = ClearingInstruction.Create(
            ClearingInstructionId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            order.Id,
            order.CurrencyId,
            source.BankId,
            destination.BankId,
            order.Amount,
            ClearingInstructionKind,
            now);

        instruction.Accept(cycle.Value.Id);
        unitOfWork.Clearing.AddInstruction(instruction);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Value.Id,
            source.BankId,
            order.CurrencyId,
            MoneyMinor.Zero,
            order.Amount);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Value.Id,
            destination.BankId,
            order.CurrencyId,
            order.Amount,
            MoneyMinor.Zero);

        order.Accept();

        if (preCredited)
        {
            order.RecordBeneficiaryPosting(now);
        }

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

    private Result<ClearingCycle> ResolveOpenCycle(
        IBankingUnitOfWork unitOfWork,
        Bank sourceBank,
        PaymentOrder order,
        PaymentNetworkPolicyVersion policy,
        UtcTimestamp now)
    {
        string cycleKey = PaymentRoutePolicy.CycleKeyOf(policy, now);

        ClearingCycle? existing = unitOfWork.Clearing.FindCycle(
            sourceBank.EconomyScopeId, order.CurrencyId, cycleKey);

        if (existing is { } cycle)
        {
            return cycle.AcceptsNewInstructions
                ? Result<ClearingCycle>.Success(cycle)
                : Result<ClearingCycle>.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        ClearingCycle opened = ClearingCycle.Open(
            ClearingCycleId.FromValue(idGenerator.NextId()),
            sourceBank.EconomyScopeId,
            order.CurrencyId,
            cycleKey,
            now);

        unitOfWork.Clearing.AddCycle(opened);

        return Result<ClearingCycle>.Success(opened);
    }

    internal Result<PaymentOrderView> SettleInterbank(
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

        if (!HasSettlementLiquidity(unitOfWork, accounts.Source, order.Amount))
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

        Result<SettlementLeg[]> legs = BuildSettlementLegs(unitOfWork, accounts, businessDate);
        if (!legs.IsSuccess)
        {
            return Result<PaymentOrderView>.Failure(legs.Error!);
        }

        instruction.LockForSettlement(now);
        order.BeginSettling();

        foreach (SettlementLeg leg in legs.Value)
        {
            PostSettlementLeg(unitOfWork, leg, order, businessDate, now);
        }

        instruction.Settle(now);
        unitOfWork.SettlementInstructions.Update(instruction);

        order.RecordSettlementFinality(now);
        order.Settle();
        unitOfWork.PaymentOrders.Update(order);

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    internal Result<PaymentOrderView> PostBeneficiaryCredit(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId)
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

        if (order.BeneficiaryPostedAt is not null)
        {
            order.Complete(now);
            unitOfWork.PaymentOrders.Update(order);
            CommitOperation(unitOfWork, order.BusinessOperationId, now);

            unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
                OutboxEventId.FromValue(idGenerator.NextId()),
                order.BusinessOperationId,
                CompletedEventType,
                Payload(order),
                now));

            return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

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

        CommitOperation(unitOfWork, order.BusinessOperationId, now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            order.BusinessOperationId,
            CompletedEventType,
            Payload(order),
            now));

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    internal Result<PaymentOrderView> CancelQueuedSettlement(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId)
    {
        PaymentOrder order = unitOfWork.PaymentOrders.Find(paymentOrderId)!;
        DepositAccount source = unitOfWork.DepositAccounts.Find(order.SourceDepositAccountId)!;
        DepositAccount destination = unitOfWork.DepositAccounts.Find(order.DestinationDepositAccountId)!;
        Hold hold = unitOfWork.Holds.FindByBusinessOperation(order.BusinessOperationId)!;
        MoneyMinor feeAmount = hold.Amount.Subtract(order.Amount);

        if (order.Status == PaymentOrderStatus.Cancelled)
        {
            return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
        }

        SettlementInstruction? instruction =
            unitOfWork.SettlementInstructions.FindByBusinessOperation(order.BusinessOperationId);

        if (order.Status != PaymentOrderStatus.Queued ||
            instruction is null ||
            instruction.Status != SettlementInstructionStatus.Queued)
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

        LedgerAccount? payable = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.SettlementPayable, order.CurrencyId);

        if (payable is null)
        {
            return Result<PaymentOrderView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
        }

        BusinessOperation reversal = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            ReversalOperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(ReversalOperationType, order.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(reversal);

        LedgerAccount sourceLedger = unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!;

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(payable, EntrySide.Debit, order.Amount));
        posting.Add(PostingLine.Deposit(sourceLedger, EntrySide.Credit, order.Amount));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                reversal.Id,
                order.CurrencyId,
                businessDate,
                now,
                now,
                ReversalTransactionType,
                ReversalDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        instruction.Cancel();
        unitOfWork.SettlementInstructions.Update(instruction);

        order.Cancel();
        unitOfWork.PaymentOrders.Update(order);

        CommitOperation(unitOfWork, order.BusinessOperationId, now);

        reversal.Commit(now);
        unitOfWork.BusinessOperations.Update(reversal);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            reversal.Id,
            CancelledEventType,
            Payload(order),
            now));

        return Result<PaymentOrderView>.Success(ToView(unitOfWork, order, feeAmount, source, destination));
    }

    private static void CommitOperation(
        IBankingUnitOfWork unitOfWork,
        BusinessOperationId businessOperationId,
        UtcTimestamp now)
    {
        BusinessOperation operation = unitOfWork.BusinessOperations.FindById(businessOperationId)!;

        if (operation.Status != BusinessOperationStatus.Committed)
        {
            operation.Commit(now);
            unitOfWork.BusinessOperations.Update(operation);
        }
    }

    private readonly record struct SettlementLeg(
        AccountingBookId BookId,
        AccountingPeriodId Period,
        LedgerAccount Debit,
        LedgerAccount Credit);

    private static bool HasSettlementLiquidity(
        IBankingUnitOfWork unitOfWork,
        SettlementSide side,
        MoneyMinor amount)
    {
        LedgerBalance reserve = unitOfWork.LedgerAccounts.FindProjection(side.SettlingReserve.Id)
            ?? LedgerBalance.Empty;

        if (!reserve.CanReserve(amount))
        {
            return false;
        }

        if (side.AgentBalance is not { } agentBalance)
        {
            return true;
        }

        LedgerBalance held = unitOfWork.LedgerAccounts.FindProjection(agentBalance.Id) ?? LedgerBalance.Empty;
        return held.CanReserve(amount);
    }

    private static Result<SettlementLeg[]> BuildSettlementLegs(
        IBankingUnitOfWork unitOfWork,
        InterbankSettlementAccounts accounts,
        BusinessDate businessDate)
    {
        List<(AccountingBookId Book, LedgerAccount Debit, LedgerAccount Credit)> drafts =
        [
            (accounts.Source.Bank.GeneralLedgerBookId, accounts.SourcePayable, accounts.Source.SettlementAsset),
        ];

        if (accounts.Source.IsIndirect)
        {
            drafts.Add((
                accounts.Source.SettlingBank.GeneralLedgerBookId,
                accounts.Source.AgentClientDeposit!,
                accounts.Source.SettlingReserve));
        }

        if (accounts.RequiresCentralBankLeg)
        {
            drafts.Add((
                accounts.CentralBankBookId,
                accounts.Source.CentralBankLiability,
                accounts.Destination.CentralBankLiability));
        }

        if (accounts.Destination.IsIndirect)
        {
            drafts.Add((
                accounts.Destination.SettlingBank.GeneralLedgerBookId,
                accounts.Destination.SettlingReserve,
                accounts.Destination.AgentClientDeposit!));
        }

        drafts.Add((
            accounts.Destination.Bank.GeneralLedgerBookId,
            accounts.Destination.SettlementAsset,
            accounts.DestinationSuspense));

        SettlementLeg[] legs = new SettlementLeg[drafts.Count];

        for (int index = 0; index < drafts.Count; index++)
        {
            if (unitOfWork.AccountingPeriods.FindOpen(drafts[index].Book, businessDate) is not { } period)
            {
                return Result<SettlementLeg[]>.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
            }

            legs[index] = new SettlementLeg(
                drafts[index].Book, period, drafts[index].Debit, drafts[index].Credit);
        }

        return Result<SettlementLeg[]>.Success(legs);
    }

    private void PostSettlementLeg(
        IBankingUnitOfWork unitOfWork,
        SettlementLeg leg,
        PaymentOrder order,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(leg.Debit, EntrySide.Debit, order.Amount));
        posting.Add(PostingLine.Institutional(leg.Credit, EntrySide.Credit, order.Amount));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                leg.BookId,
                order.BusinessOperationId,
                order.CurrencyId,
                businessDate,
                now,
                now,
                SettlementTransactionType,
                SettlementDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            leg.Period);

        posting.ApplyProjections(unitOfWork, ordered, now);
    }

    private static Result<FeeAssessmentPlan> ResolveOrderFee(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount source,
        DepositAccount destination,
        PaymentOrder order,
        BusinessTimePoint point)
    {
        if (!string.Equals(order.Method, MerchantRefundMethod, StringComparison.Ordinal))
        {
            return FeeResolver.Resolve(
                unitOfWork,
                bank,
                source,
                FeeTypeOf(order),
                ChannelOf(order),
                destination.BankId,
                order.Amount,
                point);
        }

        return unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, order.CurrencyId)
            is { } revenue &&
            bank.CurrentFeeScheduleVersionId is { } scheduleVersionId
            ? Result<FeeAssessmentPlan>.Success(new FeeAssessmentPlan(
                new FeeQuote(
                    scheduleVersionId,
                    FeeRuleId.FromValue(EntityIdValue.FromBits(0)),
                    FeeType.DebitPurchase,
                    MoneyMinor.Zero,
                    WaiverCounterKey: null,
                    WaiverApplied: false),
                revenue,
                point.BusinessMonth))
            : Result<FeeAssessmentPlan>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRevenueAccountUnavailable);
    }

    private static FeeType FeeTypeOf(PaymentOrder order) =>
        string.Equals(order.Method, MerchantPaymentMethod, StringComparison.Ordinal)
            ? FeeType.DebitPurchase
            : order.SettlementMode == SettlementMode.Internal
                ? FeeType.SameBankTransfer
                : FeeType.InterbankTransfer;

    private static FeeChannel ChannelOf(PaymentOrder order) =>
        string.Equals(order.Method, MerchantPaymentMethod, StringComparison.Ordinal)
            ? FeeChannel.Merchant
            : TransferChannel;

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

    private static bool AcceptsCredit(DepositAccount destination, bool fundsAccountOpening) =>
        destination.Permits(AccountOperation.ExternalCredit) == StatusPermission.Allowed
        || (fundsAccountOpening && destination.Status == DepositAccountStatus.Pending);

    private static Result<ReservedTransfer> Denied(TargetAccess access)
    {
        ApplicationError error = TargetAccessPolicy.ToError(
            access, BankingErrorCodes.DepositAccountNotFound, BankingErrorCodes.DepositAccountNotOperable);

        return Result<ReservedTransfer>.Failure(error.Category, error.Code);
    }
}
