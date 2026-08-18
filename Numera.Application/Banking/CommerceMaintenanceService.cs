using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CommerceMaintenanceReport(int Examined, int Cancelled);

public sealed class CommerceMaintenanceService
{
    public const int BatchSize = 100;

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;

    public CommerceMaintenanceService(IBankingWriteGateway writeGateway, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);

        this.writeGateway = writeGateway;
        this.clock = clock;
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
