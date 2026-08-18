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

    internal readonly record struct MerchantPurchaseReservation(
        PaymentOrderId OrderId,
        HoldId HoldId,
        MoneyMinor PurchaseFee,
        FeeScheduleVersionId FeeScheduleVersionId,
        BusinessOperationId BusinessOperationId);

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

        if (destination.BankId != source.BankId)
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
            SettlementMode.Internal,
            BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            paymentNetworkPolicyVersionId: null,
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
            order.Id, hold.Id, fee.Value.Quote.Amount, feeScheduleVersionId, operation.Id));
    }

    internal Result<PaymentOrderView> PostMerchantPurchase(
        IBankingUnitOfWork unitOfWork,
        PaymentOrderId paymentOrderId,
        IdempotencyKey idempotencyKey) =>
        PostTransfer(unitOfWork, paymentOrderId, idempotencyKey);
}
