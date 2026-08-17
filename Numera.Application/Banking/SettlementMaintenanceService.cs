using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record SettlementMaintenanceReport(int Examined, int Settled);

public sealed class SettlementMaintenanceService
{
    public const int BatchSize = 100;

    private readonly IBankingWriteGateway writeGateway;
    private readonly PaymentApplicationService payments;

    public SettlementMaintenanceService(
        IBankingWriteGateway writeGateway,
        PaymentApplicationService payments)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(payments);

        this.writeGateway = writeGateway;
        this.payments = payments;
    }

    public async Task<SettlementMaintenanceReport> ProcessQueuedAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<BusinessOperationId>> due = await writeGateway.ExecuteAsync(
            unitOfWork => Result<IReadOnlyList<BusinessOperationId>>.Success(
                unitOfWork.SettlementInstructions.ListQueued(afterId: null, BatchSize)),
            cancellationToken).ConfigureAwait(false);

        if (!due.IsSuccess)
        {
            return new SettlementMaintenanceReport(0, 0);
        }

        int settled = 0;

        foreach (BusinessOperationId operationId in due.Value)
        {
            if (await TrySettleAsync(operationId, cancellationToken).ConfigureAwait(false))
            {
                settled++;
            }
        }

        return new SettlementMaintenanceReport(due.Value.Count, settled);
    }

    private async Task<bool> TrySettleAsync(
        BusinessOperationId operationId,
        CancellationToken cancellationToken)
    {
        Result<PaymentOrderView> settled = await writeGateway.ExecuteAsync(
            unitOfWork => Resume(unitOfWork, operationId, payments.SettleInterbank),
            cancellationToken).ConfigureAwait(false);

        if (!settled.IsSuccess || settled.Value.Status != PaymentOrderStatus.Settled)
        {
            return false;
        }

        Result<PaymentOrderView> completed = await writeGateway.ExecuteAsync(
            unitOfWork => Resume(unitOfWork, operationId, payments.PostBeneficiaryCredit),
            cancellationToken).ConfigureAwait(false);

        return completed.IsSuccess && completed.Value.Status == PaymentOrderStatus.Completed;
    }

    private static Result<PaymentOrderView> Resume(
        IBankingUnitOfWork unitOfWork,
        BusinessOperationId operationId,
        Func<IBankingUnitOfWork, PaymentOrderId, Result<PaymentOrderView>> step) =>
        unitOfWork.PaymentOrders.FindByBusinessOperation(operationId) is { } order
            ? step(unitOfWork, order.Id)
            : Result<PaymentOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
}
