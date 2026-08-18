using Microsoft.Extensions.Hosting;
using Numera.Application.Banking;
using Numera.Host.Composition;
using Numera.Host.Logging;
using Numera.Host.Workers;

namespace Numera.Host.Tests;

internal sealed class RecordingMaintenanceRunner : ISettlementMaintenanceRunner
{
    internal List<string> Calls { get; } = [];

    internal Exception? Fault { get; set; }

    public Task<SettlementMaintenanceReport> ProcessQueuedAsync(CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ProcessQueuedAsync));

        return Fault is null
            ? Task.FromResult(new SettlementMaintenanceReport(3, 2))
            : Task.FromException<SettlementMaintenanceReport>(Fault);
    }

    public Task<SettlementMaintenanceReport> ProcessClearingCyclesAsync(CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ProcessClearingCyclesAsync));

        return Task.FromResult(new SettlementMaintenanceReport(5, 4));
    }

    public Task<CommerceMaintenanceReport> ExpireCheckoutsAsync(CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ExpireCheckoutsAsync));

        return Task.FromResult(new CommerceMaintenanceReport(1, 1));
    }
}

internal sealed class RecordingMaintenanceDiagnostics : IMaintenanceDiagnostics
{
    internal List<string> Calls { get; } = [];

    internal int LastExamined { get; private set; }

    internal int LastSettled { get; private set; }

    public void SettlementMaintenanceCompleted(int examined, int settled)
    {
        Calls.Add(nameof(SettlementMaintenanceCompleted));
        LastExamined = examined;
        LastSettled = settled;
    }

    public void SettlementMaintenanceFailed(Exception exception) => Calls.Add(nameof(SettlementMaintenanceFailed));

    public void WriteAdmissionOpened() => Calls.Add(nameof(WriteAdmissionOpened));

    public void WriteAdmissionClosed() => Calls.Add(nameof(WriteAdmissionClosed));
}

[TestClass]
public sealed class SettlementMaintenanceWorkerTests
{
    private static (SettlementMaintenanceWorker Worker, RecordingMaintenanceRunner Runner,
        RecordingMaintenanceDiagnostics Diagnostics) Create()
    {
        RecordingMaintenanceRunner runner = new();
        RecordingMaintenanceDiagnostics diagnostics = new();

        return (new SettlementMaintenanceWorker(runner, diagnostics, TimeProvider.System), runner, diagnostics);
    }

    [TestMethod]
    public void OneBatchNeverExceedsAHundredRecords()
    {
        int canonical = SettlementMaintenanceService.BatchSize;

        Assert.AreEqual(100, canonical);
        int worker = SettlementMaintenanceWorker.MaximumRecordsPerBatch;
        Assert.AreEqual(100, worker);
    }

    [TestMethod]
    public void TheIntervalIsSixtySeconds()
    {
        int seconds = SettlementMaintenanceWorker.IntervalSeconds;

        Assert.AreEqual(60, seconds);
        Assert.AreEqual(TimeSpan.FromSeconds(60), SettlementMaintenanceWorker.Interval);
    }

    [TestMethod]
    public async Task OneTickRunsQueuedSettlementBeforeClearingCycles()
    {
        (SettlementMaintenanceWorker worker, RecordingMaintenanceRunner runner, _) = Create();

        await worker.RunOnceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "ProcessQueuedAsync", "ProcessClearingCyclesAsync", "ExpireCheckoutsAsync" },
            runner.Calls);
    }

    [TestMethod]
    public async Task OneTickReportsTheCombinedCounts()
    {
        (SettlementMaintenanceWorker worker, _, RecordingMaintenanceDiagnostics diagnostics) = Create();

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.AreEqual(9, diagnostics.LastExamined);
        Assert.AreEqual(7, diagnostics.LastSettled);
    }

    [TestMethod]
    public async Task AFailingTickIsLoggedAndNeverStopsTheWorker()
    {
        (SettlementMaintenanceWorker worker, RecordingMaintenanceRunner runner,
            RecordingMaintenanceDiagnostics diagnostics) = Create();

        runner.Fault = new InvalidOperationException("boom");

        await worker.RunOnceAsync(CancellationToken.None);

        CollectionAssert.Contains(diagnostics.Calls, "SettlementMaintenanceFailed");

        runner.Fault = null;
        await worker.RunOnceAsync(CancellationToken.None);

        CollectionAssert.Contains(diagnostics.Calls, "SettlementMaintenanceCompleted");
    }

    [TestMethod]
    public async Task CancellationPropagatesInsteadOfBeingSwallowed()
    {
        (SettlementMaintenanceWorker worker, RecordingMaintenanceRunner runner, _) = Create();

        runner.Fault = new OperationCanceledException();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            worker.RunOnceAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task TheWorkerStopsWhenTheHostStops()
    {
        (SettlementMaintenanceWorker worker, RecordingMaintenanceRunner runner, _) = Create();

        using CancellationTokenSource cancellation = new();
        await ((IHostedService)worker).StartAsync(cancellation.Token);
        await ((IHostedService)worker).StopAsync(CancellationToken.None);

        Assert.IsEmpty(runner.Calls);
    }
}

[TestClass]
public sealed class SystemServicesTests
{
    [TestMethod]
    public void TheClockFollowsTheInjectedTimeProvider()
    {
        DateTimeOffset instant = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        SystemClock clock = new(new FixedTimeProvider(instant));

        Assert.AreEqual(instant.ToUnixTimeMilliseconds(), clock.Now().UnixMilliseconds);
    }

    [TestMethod]
    public void GeneratedIdentifiersAreDistinctAndOrdered()
    {
        UuidVersion7IdGenerator generator = new();

        HashSet<string> seen = [];

        for (int index = 0; index < 64; index++)
        {
            Assert.IsTrue(seen.Add(generator.NextId().ToString()));
        }
    }

    [TestMethod]
    public void GeneratedIdentifiersAreNotEmpty()
    {
        UuidVersion7IdGenerator generator = new();

        Assert.AreNotEqual(Numera.Domain.Common.EntityIdValue.Empty, generator.NextId());
    }
}
