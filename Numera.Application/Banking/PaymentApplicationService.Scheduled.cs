using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;

namespace Numera.Application.Banking;

public sealed partial class PaymentApplicationService
{
    public const string ScheduledOperationType = "SCHEDULED_PAYMENT";
    public const string DirectDebitOperationType = "DIRECT_DEBIT_COLLECTION";

    internal Result<ReservedTransfer> ReserveRecurringTransfer(
        IBankingUnitOfWork unitOfWork,
        CreatePaymentOrderCommand command,
        IdempotencyKey idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TryValidate(command, out TransferRequest request, out ApplicationError? error)
            ? ReserveFunds(unitOfWork, command, request, idempotencyKey)
            : Result<ReservedTransfer>.Failure(error!.Category, error.Code, error.Field);
    }

    internal Result<PaymentOrderView> PostRecurringTransfer(
        IBankingUnitOfWork unitOfWork,
        ReservedTransfer reserved,
        IdempotencyKey idempotencyKey) => reserved.Mode switch
        {
            SettlementMode.Internal => PostTransfer(unitOfWork, reserved.OrderId, idempotencyKey),
            SettlementMode.Clearing => PostClearingDebit(unitOfWork, reserved.OrderId),
            _ => PostSourceDebit(unitOfWork, reserved.OrderId),
        };
}
