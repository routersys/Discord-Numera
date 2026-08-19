using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CommerceMaintenanceReport(int Examined, int Cancelled);

public sealed record CommerceSettlementFinalityReport(int Examined, int Finalized);

public sealed class CommerceMaintenanceService
{
    public const int BatchSize = 100;

    public const string FinalityEventType = "COMMERCE_SETTLEMENT_FINALIZED";

    public const string FinalityOperationType = "COMMERCE_SETTLEMENT_FINALITY";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public CommerceMaintenanceService(
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

    public async Task<CommerceSettlementFinalityReport> FinalizeMerchantSettlementsAsync(
        CancellationToken cancellationToken)
    {
        Result<CommerceSettlementFinalityReport> outcome = await writeGateway
            .ExecuteAsync(FinalizeMerchantSettlements, cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? outcome.Value : new CommerceSettlementFinalityReport(0, 0);
    }

    private Result<CommerceSettlementFinalityReport> FinalizeMerchantSettlements(
        IBankingUnitOfWork unitOfWork)
    {
        UtcTimestamp now = clock.Now();

        IReadOnlyList<CommercePaymentRecord> pending =
            unitOfWork.Commerce.ListPaymentsAwaitingSettlementFinality(BatchSize);

        int finalized = 0;

        foreach (CommercePaymentRecord payment in pending)
        {
            if (FinalityOf(unitOfWork, payment) is not { } instant)
            {
                continue;
            }

            unitOfWork.Commerce.UpdatePayment(payment with
            {
                MerchantSettlementFinalizedAt = instant,
                Version = payment.Version + 1,
            });

            BusinessOperation operation = BusinessOperation.Start(
                BusinessOperationId.FromValue(idGenerator.NextId()),
                FinalityOperationType,
                ScopeOf(unitOfWork, payment),
                null,
                idGenerator.NextId(),
                IdempotencyKey.Create(FinalityOperationType, payment.Id.Value.ToString()),
                now);

            unitOfWork.BusinessOperations.Add(operation);
            operation.Commit(now);
            unitOfWork.BusinessOperations.Update(operation);

            unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
                OutboxEventId.FromValue(idGenerator.NextId()),
                operation.Id,
                FinalityEventType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"commerce_payment_id":"{{payment.Id.Value}}"}"""),
                now));

            unitOfWork.BankAdministration.AddAuditRecord(
                AuditRecordId.FromValue(idGenerator.NextId()),
                operation.Id,
                null,
                FinalityOperationType,
                "commerce_payments",
                payment.Id.Value,
                null,
                now);

            finalized++;
        }

        return Result<CommerceSettlementFinalityReport>.Success(
            new CommerceSettlementFinalityReport(pending.Count, finalized));
    }

    private static EconomyScopeId ScopeOf(
        IBankingUnitOfWork unitOfWork,
        CommercePaymentRecord payment) =>
        unitOfWork.Commerce.FindOrder(payment.CommerceOrderId) is { } order &&
        unitOfWork.Commerce.FindMerchantProfile(order.MerchantProfileId) is { } profile &&
        unitOfWork.DepositAccounts.Find(profile.SettlementDepositAccountId) is { } settlement &&
        unitOfWork.Banks.Find(settlement.BankId) is { } bank
            ? bank.EconomyScopeId
            : default;

    private static UtcTimestamp? FinalityOf(
        IBankingUnitOfWork unitOfWork,
        CommercePaymentRecord payment)
    {
        if (payment.DebitCardAuthorizationId is not { } authorizationId ||
            unitOfWork.DebitCardAuthorizations.FindCapture(authorizationId) is not { } capture ||
            payment.CaptureCommittedAt is not { } committedAt)
        {
            return null;
        }

        if (capture.PaymentOrderId is { } paymentOrderId)
        {
            return unitOfWork.PaymentOrders.Find(paymentOrderId) is not { } order
                ? null
                : order.SettlementMode == SettlementMode.Internal
                    ? committedAt
                    : order.Status is PaymentOrderStatus.Settled or PaymentOrderStatus.Completed
                        ? order.CompletedAt ?? committedAt
                        : null;
        }

        return capture.FxBusinessOperationId is { } fxOperationId &&
            unitOfWork.Fx.AreSettlementLegsFinal(fxOperationId)
                ? committedAt
                : null;
    }

    public async Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(
        CancellationToken cancellationToken)
    {
        Result<CommerceMaintenanceReport> outcome = await writeGateway
            .ExecuteAsync(ExpireCheckouts, cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? outcome.Value : new CommerceMaintenanceReport(0, 0);
    }

    private Result<CommerceMaintenanceReport> ExpireCheckouts(IBankingUnitOfWork unitOfWork)
    {
        UtcTimestamp now = clock.Now();

        IReadOnlyList<CommerceOrderRecord> due =
            unitOfWork.Commerce.ListExpiredAwaitingConfirmationOrders(now, BatchSize);

        int cancelled = 0;

        foreach (CommerceOrderRecord order in due)
        {
            if (!CommerceOrderStatusCatalog.IsAllowed(order.Status, CommerceOrderStatus.Cancelled))
            {
                continue;
            }

            unitOfWork.Commerce.UpdateOrder(order with
            {
                Status = CommerceOrderStatus.Cancelled,
                Version = order.Version + 1,
            });

            if (unitOfWork.Commerce.FindPaymentByOrder(order.Id) is { } payment &&
                CommercePaymentStatusCatalog.IsAllowed(payment.Status, CommercePaymentStatus.Cancelled))
            {
                unitOfWork.Commerce.UpdatePayment(payment with
                {
                    Status = CommercePaymentStatus.Cancelled,
                    Version = payment.Version + 1,
                });
            }

            cancelled++;
        }

        return Result<CommerceMaintenanceReport>.Success(
            new CommerceMaintenanceReport(due.Count, cancelled));
    }
}
