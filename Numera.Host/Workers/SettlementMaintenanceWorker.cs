using Microsoft.Extensions.Hosting;
using Numera.Application.Banking;
using Numera.Host.Logging;

namespace Numera.Host.Workers;

internal interface ISettlementMaintenanceRunner
{
    Task<SettlementMaintenanceReport> ProcessQueuedAsync(CancellationToken cancellationToken);

    Task<SettlementMaintenanceReport> ProcessClearingCyclesAsync(CancellationToken cancellationToken);

    Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(CancellationToken cancellationToken);

    Task<CommerceSettlementFinalityReport> FinalizeMerchantSettlementsAsync(
        CancellationToken cancellationToken);

    Task<CommerceFulfillmentReport> ProcessDueFulfillmentsAsync(CancellationToken cancellationToken);

    Task<AtmInstallationRecoveryReport> ScanAtmInstallationsAsync(CancellationToken cancellationToken);

    Task<ExpiryMaintenanceReport> ProcessDueExpiriesAsync(CancellationToken cancellationToken);

    Task<DormancyMaintenanceReport> ProcessDueDormancyAsync(CancellationToken cancellationToken);
}

internal sealed class SettlementMaintenanceRunner : ISettlementMaintenanceRunner
{
    private readonly SettlementMaintenanceService service;
    private readonly CommerceMaintenanceService commerce;
    private readonly CommerceFulfillmentService fulfillments;
    private readonly AtmInstallationRecoveryService installations;
    private readonly ExpiryMaintenanceService expiries;
    private readonly DormancyMaintenanceService dormancy;

    public SettlementMaintenanceRunner(
        SettlementMaintenanceService service,
        CommerceMaintenanceService commerce,
        CommerceFulfillmentService fulfillments,
        AtmInstallationRecoveryService installations,
        ExpiryMaintenanceService expiries,
        DormancyMaintenanceService dormancy)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(commerce);
        ArgumentNullException.ThrowIfNull(expiries);
        ArgumentNullException.ThrowIfNull(dormancy);

        this.service = service;
        this.commerce = commerce;
        this.fulfillments = fulfillments;
        this.installations = installations;
        this.expiries = expiries;
        this.dormancy = dormancy;
    }

    public Task<DormancyMaintenanceReport> ProcessDueDormancyAsync(CancellationToken cancellationToken) =>
        dormancy.ProcessDueAsync(cancellationToken);

    public Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(CancellationToken cancellationToken) =>
        commerce.ExpireCheckoutsAsync(cancellationToken);

    public Task<CommerceSettlementFinalityReport> FinalizeMerchantSettlementsAsync(
        CancellationToken cancellationToken) =>
        commerce.FinalizeMerchantSettlementsAsync(cancellationToken);

    public Task<CommerceFulfillmentReport> ProcessDueFulfillmentsAsync(
        CancellationToken cancellationToken) =>
        fulfillments.ProcessDueAsync(cancellationToken);

    public Task<AtmInstallationRecoveryReport> ScanAtmInstallationsAsync(
        CancellationToken cancellationToken) =>
        installations.ScanAsync(cancellationToken);

    public Task<ExpiryMaintenanceReport> ProcessDueExpiriesAsync(CancellationToken cancellationToken) =>
        expiries.ProcessDueAsync(cancellationToken);

    public Task<SettlementMaintenanceReport> ProcessQueuedAsync(CancellationToken cancellationToken) =>
        service.ProcessQueuedAsync(cancellationToken);

    public Task<SettlementMaintenanceReport> ProcessClearingCyclesAsync(CancellationToken cancellationToken) =>
        service.ProcessClearingCyclesAsync(cancellationToken);
}

internal sealed class SettlementMaintenanceWorker : BackgroundService
{
    internal const int IntervalSeconds = 60;
    internal const int MaximumRecordsPerBatch = SettlementMaintenanceService.BatchSize;

    private readonly ISettlementMaintenanceRunner runner;
    private readonly IMaintenanceDiagnostics diagnostics;
    private readonly TimeProvider timeProvider;

    public SettlementMaintenanceWorker(
        ISettlementMaintenanceRunner runner,
        IMaintenanceDiagnostics diagnostics,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.runner = runner;
        this.diagnostics = diagnostics;
        this.timeProvider = timeProvider;
    }

    internal static TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            SettlementMaintenanceReport queued = await runner.ProcessQueuedAsync(cancellationToken)
                .ConfigureAwait(false);
            SettlementMaintenanceReport cycles = await runner.ProcessClearingCyclesAsync(cancellationToken)
                .ConfigureAwait(false);
            CommerceMaintenanceReport checkouts = await runner.ExpireCheckoutsAsync(cancellationToken)
                .ConfigureAwait(false);
            CommerceSettlementFinalityReport finality = await runner
                .FinalizeMerchantSettlementsAsync(cancellationToken).ConfigureAwait(false);
            CommerceFulfillmentReport fulfillments = await runner
                .ProcessDueFulfillmentsAsync(cancellationToken).ConfigureAwait(false);
            AtmInstallationRecoveryReport installations = await runner
                .ScanAtmInstallationsAsync(cancellationToken).ConfigureAwait(false);
            ExpiryMaintenanceReport expiries = await runner.ProcessDueExpiriesAsync(cancellationToken)
                .ConfigureAwait(false);
            DormancyMaintenanceReport dormancy = await runner.ProcessDueDormancyAsync(cancellationToken)
                .ConfigureAwait(false);

            int dormancyCount = dormancy.Assessed + dormancy.Closed;

            diagnostics.SettlementMaintenanceCompleted(
                queued.Examined + cycles.Examined + checkouts.Examined + finality.Examined +
                    fulfillments.Examined + installations.Examined + expiries.Total + dormancyCount,
                queued.Settled + cycles.Settled + checkouts.Cancelled + finality.Finalized +
                    fulfillments.Succeeded + installations.Confirmed + installations.Broken +
                    expiries.Total + dormancyCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.SettlementMaintenanceFailed(exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
