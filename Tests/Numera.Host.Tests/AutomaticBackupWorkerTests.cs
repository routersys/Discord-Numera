using Numera.Host.Logging;
using Numera.Host.Workers;
using Numera.Persistence.Sqlite;

namespace Numera.Host.Tests;

internal sealed class RecordingBackupService : IDatabaseBackupService
{
    internal List<BackupKind> Created { get; } = [];

    internal int Pruned { get; private set; }

    internal string? Newest { get; set; }

    internal string Detail { get; set; } = string.Empty;

    internal bool Succeeds { get; set; } = true;

    public BackupCreationResult Create(BackupKind kind)
    {
        Created.Add(kind);

        return Succeeds
            ? new BackupCreationResult(true, "db", "manifest", Detail)
            : BackupCreationResult.Failed("BOOM");
    }

    public BackupVerificationResult VerifyAt(string databasePath) => BackupVerificationResult.Passed;

    public BackupSummary Summarize() => BackupSummary.Empty;

    public string? FindLatestVerified() => null;

    public int PruneAutomatic()
    {
        Pruned++;

        return 0;
    }

    public string? NewestAutomaticCreatedAtUtc() => Newest;
}

internal sealed class RecordingBackupDiagnostics : IMaintenanceDiagnostics
{
    internal List<string> Failures { get; } = [];

    public void SettlementMaintenanceCompleted(int examined, int settled)
    {
    }

    public void SettlementMaintenanceFailed(Exception exception)
    {
    }

    public void AutomaticBackupFailed(string detail) => Failures.Add(detail);

    public void WriteAdmissionOpened()
    {
    }

    public void WriteAdmissionClosed()
    {
    }
}

[TestClass]
public sealed class AutomaticBackupWorkerTests
{
    private static readonly DateTimeOffset Instant = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static (AutomaticBackupScheduler Scheduler, RecordingBackupService Backups,
        RecordingBackupDiagnostics Diagnostics) Create(string? newest)
    {
        RecordingBackupService backups = new() { Newest = newest };
        RecordingBackupDiagnostics diagnostics = new();

        return (
            new AutomaticBackupScheduler(backups, diagnostics, new FixedTimeProvider(Instant)),
            backups,
            diagnostics);
    }

    private static string At(TimeSpan before) =>
        Instant.Subtract(before).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    [TestMethod]
    public void TheCadenceIsSixHours()
    {
        int hours = AutomaticBackupScheduler.CadenceHours;

        Assert.AreEqual(6, hours);
        Assert.AreEqual(TimeSpan.FromHours(6), AutomaticBackupScheduler.Cadence);
    }

    [TestMethod]
    public void TheDuplicateSuppressionWindowIsSixtyMinutes()
    {
        int minutes = AutomaticBackupScheduler.SuppressionMinutes;

        Assert.AreEqual(60, minutes);
        Assert.IsTrue(AutomaticBackupScheduler.IsSuppressed(At(TimeSpan.FromMinutes(59)), Instant));
        Assert.IsFalse(AutomaticBackupScheduler.IsSuppressed(At(TimeSpan.FromMinutes(61)), Instant));
        Assert.IsFalse(AutomaticBackupScheduler.IsSuppressed(null, Instant));
    }

    [TestMethod]
    public void TheFirstRunWithoutHistoryCreatesOneBackup()
    {
        (AutomaticBackupScheduler scheduler, RecordingBackupService backups, _) = Create(null);

        Assert.IsTrue(scheduler.RunOnce());
        CollectionAssert.AreEqual(new[] { BackupKind.Automatic }, backups.Created);
        Assert.AreEqual(1, backups.Pruned);
    }

    [TestMethod]
    public void ARecentBackupSuppressesTheRun()
    {
        (AutomaticBackupScheduler scheduler, RecordingBackupService backups, _) =
            Create(At(TimeSpan.FromHours(1)));

        Assert.IsFalse(scheduler.RunOnce());
        Assert.IsEmpty(backups.Created);
    }

    [TestMethod]
    public void AnOverdueIntervalIsCaughtUpExactlyOnce()
    {
        (AutomaticBackupScheduler scheduler, RecordingBackupService backups, _) =
            Create(At(TimeSpan.FromDays(3)));

        Assert.IsTrue(scheduler.RunOnce());

        backups.Newest = At(TimeSpan.Zero);

        Assert.IsFalse(scheduler.RunOnce());
        Assert.AreEqual(1, backups.Created.Count);
    }

    [TestMethod]
    public void AFailedBackupIsLoggedAndDoesNotPrune()
    {
        (AutomaticBackupScheduler scheduler, RecordingBackupService backups,
            RecordingBackupDiagnostics diagnostics) = Create(null);

        backups.Succeeds = false;

        Assert.IsFalse(scheduler.RunOnce());
        Assert.AreEqual(0, backups.Pruned);
        CollectionAssert.Contains(diagnostics.Failures, "BOOM");
    }

    [TestMethod]
    public void ADegradedSecondaryCopyIsLoggedButKeepsTheBackup()
    {
        (AutomaticBackupScheduler scheduler, RecordingBackupService backups,
            RecordingBackupDiagnostics diagnostics) = Create(null);

        backups.Detail = "SECONDARY_COPY_FAILED";

        Assert.IsTrue(scheduler.RunOnce());
        Assert.AreEqual(1, backups.Pruned);
        CollectionAssert.Contains(diagnostics.Failures, "SECONDARY_COPY_FAILED");
    }

    [TestMethod]
    public void ThePollIntervalIsFifteenMinutes()
    {
        int minutes = AutomaticBackupWorker.PollMinutes;

        Assert.AreEqual(15, minutes);
        Assert.AreEqual(TimeSpan.FromMinutes(15), AutomaticBackupWorker.PollInterval);
    }
}
