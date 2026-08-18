using Microsoft.Extensions.Hosting;
using Numera.Application.Banking;
using Numera.Host.Logging;

namespace Numera.Host.Workers;

internal interface ISettlementMaintenanceRunner
{
    Task<SettlementMaintenanceReport> ProcessQueuedAsync(CancellationToken cancellationToken);

    Task<SettlementMaintenanceReport> ProcessClearingCyclesAsync(CancellationToken cancellationToken);

    Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(CancellationToken cancellationToken);
}

internal sealed class SettlementMaintenanceRunner : ISettlementMaintenanceRunner
{
    private readonly SettlementMaintenanceService service;
    private readonly CommerceMaintenanceService commerce;

    public SettlementMaintenanceRunner(
        SettlementMaintenanceService service,
        CommerceMaintenanceService commerce)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(commerce);

        this.service = service;
        this.commerce = commerce;
    }

    public Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(CancellationToken cancellationToken) =>
        commerce.ExpireCheckoutsAsync(cancellationToken);

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

            diagnostics.SettlementMaintenanceCompleted(
                queued.Examined + cycles.Examined + checkouts.Examined,
                queued.Settled + cycles.Settled + checkouts.Cancelled);
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
