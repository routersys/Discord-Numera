using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed partial class PaymentApplicationService
{
    public const string MerchantPaymentMethod = "DEBIT_CARD_MERCHANT";
    public const string MerchantOperationType = "COMMERCE_CAPTURE";
    public const string MerchantHoldReason = "DEBIT_PURCHASE";
    public const string MerchantRefundMethod = "DEBIT_CARD_REFUND";
    public const string MerchantRefundOperationType = "COMMERCE_REFUND";

    internal readonly record struct MerchantPurchaseReservation(
        PaymentOrderId OrderId,
        HoldId HoldId,
        MoneyMinor PurchaseFee,
        FeeScheduleVersionId FeeScheduleVersionId,
        BusinessOperationId BusinessOperationId,
        SettlementMode SettlementMode);

    internal Result<MerchantPurchaseReservation> ReserveMerchantPurchase(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        CustomerAccount payer,
        DepositAccount source,
        DepositAccount destination,
        MoneyMinor amount,
        IdempotencyKey idempotencyKey,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        if (destination.CurrencyId != source.CurrencyId)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (unitOfWork.Banks.Find(source.BankId) is not { Status: BankStatus.Operating } bank)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Result<PaymentRoute> routed = PaymentRoutePolicy.Resolve(
            unitOfWork, bank.EconomyScopeId, destination.BankId != source.BankId, amount);

        if (!routed.IsSuccess)
        {
            return Result<MerchantPurchaseReservation>.Failure(routed.Error!);
        }

        if (routed.Value.Mode == SettlementMode.Rtgs)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.CommerceInterbankCaptureUnavailable);
        }

        if (bank.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeScheduleUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            bank,
            source,
            FeeType.DebitPurchase,
            FeeChannel.Merchant,
            destination.BankId,
            amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<MerchantPurchaseReservation>.Failure(fee.Error!);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(
                bank.GeneralLedgerBookId, BusinessDateOf(now)) is null)
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        MoneyMinor totalDebit = amount.Add(fee.Value.Quote.Amount);

        LedgerBalance sourceBalance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        Result holdLimit = TransferLimitPolicy.EvaluateActiveHolds(
            unitOfWork, bank, sourceBalance.HeldAmount, totalDebit);

        if (!holdLimit.IsSuccess)
        {
            return Result<MerchantPurchaseReservation>.Failure(holdLimit.Error!);
        }

        if (!sourceBalance.CanReserve(totalDebit))
        {
            return Result<MerchantPurchaseReservation>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            MerchantOperationType,
            economyScopeId,
            payer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        PaymentOrder order = PaymentOrder.Create(
            PaymentOrderId.FromValue(idGenerator.NextId()),
            operation.Id,
            payer.Id,
            source.Id,
            destination.Id,
            source.CurrencyId,
            amount,
            MerchantPaymentMethod,
            routed.Value.Mode,
            routed.Value.PostingPolicy,
            routed.Value.PolicyVersionId,
            memo: null,
            now);

        order.Authorize();

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            source.Id,
            operation.Id,
            totalDebit,
            MerchantHoldReason,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            source.LedgerAccountId, sourceBalance.IncreaseHold(totalDebit), now);

        order.HoldFunds();
        unitOfWork.PaymentOrders.Add(order);

        return Result<MerchantPurchaseReservation>.Success(new MerchantPurchaseReservation(
            order.Id,
            hold.Id,
            fee.Value.Quote.Amount,
            feeScheduleVersionId,
            operation.Id,
            routed.Value.Mode));
    }

    internal readonly record struct MerchantAuthorizationReservation(
        HoldId HoldId,
        MoneyMinor PurchaseFee,
        FeeScheduleVersionId FeeScheduleVersionId,
        BusinessOperationId BusinessOperationId);

    internal Result<MerchantAuthorizationReservation> ReserveMerchantAuthorization(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        CustomerAccount payer,
        DepositAccount source,
        DepositAccount destination,
        MoneyMinor amount,
        IdempotencyKey idempotencyKey,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<MerchantAuthorizationReservation>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (unitOfWork.Banks.Find(source.BankId) is not { Status: BankStatus.Operating } bank)
        {
            return Result<MerchantAuthorizationReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (bank.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<MerchantAuthorizationReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeScheduleUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<MerchantAuthorizationReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            bank,
            source,
            FeeType.DebitPurchase,
            FeeChannel.Merchant,
            destination.BankId,
            amount,
            point);

        if (!fee.IsSuccess)
        {
            return Result<MerchantAuthorizationReservation>.Failure(fee.Error!);
        }

        MoneyMinor totalDebit = amount.Add(fee.Value.Quote.Amount);

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        Result holdLimit = TransferLimitPolicy.EvaluateActiveHolds(
            unitOfWork, bank, balance.HeldAmount, totalDebit);

        if (!holdLimit.IsSuccess)
        {
            return Result<MerchantAuthorizationReservation>.Failure(holdLimit.Error!);
        }

        if (!balance.CanReserve(totalDebit))
        {
            return Result<MerchantAuthorizationReservation>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            MerchantOperationType,
            economyScopeId,
            payer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            source.Id,
            operation.Id,
            totalDebit,
            MerchantHoldReason,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            source.LedgerAccountId, balance.IncreaseHold(totalDebit), now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return Result<MerchantAuthorizationReservation>.Success(new MerchantAuthorizationReservation(
            hold.Id, fee.Value.Quote.Amount, feeScheduleVersionId, operation.Id));
    }

    internal readonly record struct MerchantRefundPosting(
        PaymentOrderId OrderId,
        BusinessOperationId BusinessOperationId,
        SettlementMode SettlementMode);

    internal Result<MerchantRefundPosting> PostMerchantRefund(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        PartyId payerPartyId,
        CustomerAccountId payerCustomerAccountId,
        DepositAccount merchantSource,
        DepositAccount cardholderDestination,
        MoneyMinor amount,
        IdempotencyKey idempotencyKey,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(merchantSource);
        ArgumentNullException.ThrowIfNull(cardholderDestination);

        if (merchantSource.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (cardholderDestination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        if (cardholderDestination.CurrencyId != merchantSource.CurrencyId)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (unitOfWork.Banks.Find(merchantSource.BankId) is not { Status: BankStatus.Operating } bank)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        Result<PaymentRoute> routed = PaymentRoutePolicy.Resolve(
            unitOfWork,
            bank.EconomyScopeId,
            cardholderDestination.BankId != merchantSource.BankId,
            amount);

        if (!routed.IsSuccess)
        {
            return Result<MerchantRefundPosting>.Failure(routed.Error!);
        }

        if (routed.Value.Mode == SettlementMode.Rtgs)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.CommerceInterbankCaptureUnavailable);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(
                bank.GeneralLedgerBookId, BusinessDateOf(now)) is null)
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(merchantSource.LedgerAccountId)
                ?? LedgerBalance.Empty;

        if (!balance.CanReserve(amount))
        {
            return Result<MerchantRefundPosting>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            MerchantRefundOperationType,
            economyScopeId,
            payerPartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        PaymentOrder order = PaymentOrder.Create(
            PaymentOrderId.FromValue(idGenerator.NextId()),
            operation.Id,
            payerCustomerAccountId,
            merchantSource.Id,
            cardholderDestination.Id,
            merchantSource.CurrencyId,
            amount,
            MerchantRefundMethod,
            routed.Value.Mode,
            routed.Value.PostingPolicy,
            routed.Value.PolicyVersionId,
            memo: null,
            now);

        order.Authorize();

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            merchantSource.Id,
            operation.Id,
            amount,
            MerchantRefundMethod,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            merchantSource.LedgerAccountId, balance.IncreaseHold(amount), now);

        order.HoldFunds();
        unitOfWork.PaymentOrders.Add(order);

        Result<PaymentOrderView> posted = routed.Value.Mode == SettlementMode.Internal
            ? PostTransfer(unitOfWork, order.Id, idempotencyKey)
            : PostClearingDebit(unitOfWork, order.Id);

        return posted.IsSuccess
            ? Result<MerchantRefundPosting>.Success(
                new MerchantRefundPosting(order.Id, operation.Id, routed.Value.Mode))
            : Result<MerchantRefundPosting>.Failure(posted.Error!);
    }

    internal readonly record struct MerchantFxReservation(
        PaymentOrderId? OrderId,
        HoldId HoldId,
        MoneyMinor SourcePrincipal,
        MoneyMinor PurchaseFee,
        FeeScheduleVersionId FeeScheduleVersionId,
        BusinessOperationId BusinessOperationId);

    internal Result<MerchantFxReservation> ReserveMerchantFxPurchase(
        IBankingUnitOfWork unitOfWork,
        FxApplicationService markets,
        EconomyScopeId economyScopeId,
        CustomerAccount payer,
        DepositAccount source,
        DepositAccount destination,
        FxMarketId marketId,
        FxMarketPolicyVersionId policyVersionId,
        MerchantProfileId merchantProfileId,
        CommerceOrderId commerceOrderId,
        MoneyMinor presentmentTotal,
        MoneyMinor confirmedMaxSourceDebit,
        IdempotencyKey idempotencyKey,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(payer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        if (unitOfWork.Banks.Find(source.BankId) is not { Status: BankStatus.Operating } bank)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (bank.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeScheduleUnavailable);
        }

        BusinessDate businessDate = BusinessDateOf(now);

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate) is null)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            MerchantOperationType,
            economyScopeId,
            payer.PartyId,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result<FxApplicationService.FxCashDeliveryOutcome> delivered = markets.DeliverPurchase(
            unitOfWork,
            operation,
            payer,
            source,
            destination,
            bank,
            marketId,
            policyVersionId,
            merchantProfileId,
            commerceOrderId,
            presentmentTotal,
            businessDate,
            now);

        if (!delivered.IsSuccess)
        {
            return Result<MerchantFxReservation>.Failure(delivered.Error!);
        }

        MoneyMinor principal = delivered.Value.SourceDebit;

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> fee = FeeResolver.Resolve(
            unitOfWork,
            bank,
            source,
            FeeType.DebitPurchase,
            FeeChannel.Merchant,
            destination.BankId,
            principal,
            point);

        if (!fee.IsSuccess)
        {
            return Result<MerchantFxReservation>.Failure(fee.Error!);
        }

        MoneyMinor totalDebit = principal.Add(fee.Value.Quote.Amount);

        if (totalDebit > confirmedMaxSourceDebit)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceConfirmedDebitExceeded);
        }

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            source.Id,
            operation.Id,
            totalDebit,
            MerchantHoldReason,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        hold.Capture(totalDebit, now);
        unitOfWork.Holds.Update(hold);

        if (!fee.Value.RequiresPosting)
        {
            return Result<MerchantFxReservation>.Success(new MerchantFxReservation(
                OrderId: null,
                hold.Id,
                principal,
                fee.Value.Quote.Amount,
                feeScheduleVersionId,
                operation.Id));
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (!balance.CanReserve(fee.Value.Quote.Amount))
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate)
            is not { } periodId)
        {
            return Result<MerchantFxReservation>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Deposit(
            unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!,
            EntrySide.Debit,
            fee.Value.Quote.Amount));
        posting.Add(PostingLine.Institutional(
            fee.Value.RevenueAccount, EntrySide.Credit, fee.Value.Quote.Amount));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                operation.Id,
                source.CurrencyId,
                businessDate,
                now,
                now,
                MerchantPaymentMethod,
                MerchantHoldReason,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        unitOfWork.FeeAssessments.Add(FeeAssessment.Assess(
            FeeAssessmentId.FromValue(idGenerator.NextId()),
            operation.Id,
            fee.Value.Quote.ScheduleVersionId,
            fee.Value.Quote.RuleId,
            source.CurrencyId,
            source.LedgerAccountId,
            fee.Value.RevenueAccount.Id,
            fee.Value.Quote.Type,
            fee.Value.Quote.Amount,
            now));

        return Result<MerchantFxReservation>.Success(new MerchantFxReservation(
            OrderId: null,
            hold.Id,
            principal,
            fee.Value.Quote.Amount,
            feeScheduleVersionId,
            operation.Id));
    }

    internal Result<PaymentOrderView> PostMerchantPurchase(
        IBankingUnitOfWork unitOfWork,
        MerchantPurchaseReservation reservation,
        IdempotencyKey idempotencyKey) =>
        reservation.SettlementMode == SettlementMode.Internal
            ? PostTransfer(unitOfWork, reservation.OrderId, idempotencyKey)
            : PostClearingDebit(unitOfWork, reservation.OrderId);
}
