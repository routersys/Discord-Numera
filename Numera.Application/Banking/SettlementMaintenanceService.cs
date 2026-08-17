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
    private readonly ClearingSettlementService clearing;
    private readonly IClock clock;

    public SettlementMaintenanceService(
        IBankingWriteGateway writeGateway,
        PaymentApplicationService payments,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(clock);

        this.writeGateway = writeGateway;
        this.payments = payments;
        this.clock = clock;
        clearing = new ClearingSettlementService(clock, idGenerator);
    }

    public async Task<SettlementMaintenanceReport> ProcessClearingCyclesAsync(
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ClearingCycleId>> due = await writeGateway.ExecuteAsync(
            unitOfWork => Result<IReadOnlyList<ClearingCycleId>>.Success(DueCycles(unitOfWork)),
            cancellationToken).ConfigureAwait(false);

        if (!due.IsSuccess)
        {
            return new SettlementMaintenanceReport(0, 0);
        }

        int settled = 0;

        foreach (ClearingCycleId cycleId in due.Value)
        {
            if (await TrySettleCycleAsync(cycleId, cancellationToken).ConfigureAwait(false))
            {
                settled++;
            }
        }

        return new SettlementMaintenanceReport(due.Value.Count, settled);
    }

    private IReadOnlyList<ClearingCycleId> DueCycles(IBankingUnitOfWork unitOfWork)
    {
        List<ClearingCycleId> due = [];
        UtcTimestamp now = clock.Now();

        foreach (ClearingCycle cycle in unitOfWork.Clearing.ListUnclosedCycles(BatchSize))
        {
            if (cycle.Status != ClearingCycleStatus.Open)
            {
                due.Add(cycle.Id);
                continue;
            }

            if (IsIntervalElapsed(unitOfWork, cycle, now))
            {
                due.Add(cycle.Id);
            }
        }

        return due;
    }

    private static bool IsIntervalElapsed(
        IBankingUnitOfWork unitOfWork,
        ClearingCycle cycle,
        UtcTimestamp now)
    {
        if (unitOfWork.PaymentNetworks.FindRouting(cycle.EconomyScopeId) is not { RoutesPayments: true } network ||
            unitOfWork.PaymentNetworks.FindPolicy(network.CurrentPolicyVersionId!.Value) is not { } policy ||
            policy.ClearingCycleIntervalSeconds is not { } interval ||
            !long.TryParse(
                cycle.CycleKey,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long startSeconds))
        {
            return false;
        }

        return (now.UnixMilliseconds / 1000) >= startSeconds + interval;
    }

    private async Task<bool> TrySettleCycleAsync(ClearingCycleId cycleId, CancellationToken cancellationToken)
    {
        Result<bool> locked = await writeGateway.ExecuteAsync(
            unitOfWork => ToUnit(clearing.Lock(unitOfWork, cycleId)),
            cancellationToken).ConfigureAwait(false);

        if (!locked.IsSuccess)
        {
            return false;
        }

        Result<ClearingSettlementOutcome> settled = await writeGateway.ExecuteAsync(
            unitOfWork => clearing.Settle(unitOfWork, cycleId),
            cancellationToken).ConfigureAwait(false);

        if (!settled.IsSuccess)
        {
            return false;
        }

        foreach (BusinessOperationId operationId in settled.Value.SettledOperations)
        {
            await writeGateway.ExecuteAsync(
                unitOfWork => Resume(unitOfWork, operationId, payments.PostBeneficiaryCredit),
                cancellationToken).ConfigureAwait(false);
        }

        Result<bool> closed = await writeGateway.ExecuteAsync(
            unitOfWork => ToUnit(clearing.Close(unitOfWork, cycleId)),
            cancellationToken).ConfigureAwait(false);

        return closed.IsSuccess;
    }

    private static Result<bool> ToUnit(Result result) => result.IsSuccess
        ? Result<bool>.Success(true)
        : Result<bool>.Failure(result.Error!);

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

    public Task<Result<PaymentOrderView>> CancelQueuedAsync(
        BusinessOperationId businessOperationId,
        CancellationToken cancellationToken) =>
        writeGateway.ExecuteAsync(
            unitOfWork => Resume(unitOfWork, businessOperationId, payments.CancelQueuedSettlement),
            cancellationToken);

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
