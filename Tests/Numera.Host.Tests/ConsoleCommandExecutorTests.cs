using Numera.Host.Console;
using Numera.Host.Startup;
using Numera.Persistence.Sqlite;

namespace Numera.Host.Tests;

internal sealed class StubReconciliation : IDatabaseReconciliationRunner
{
    internal string? Status { get; set; }

    public ReconciliationOutcome RunFinancialReconciliation(long nowMilliseconds) =>
        ReconciliationOutcome.Ok;

    public LeaseRecoveryOutcome RecoverExpiredLeases(long nowMilliseconds) => new(0);

    public ReconciliationOutcome VerifyNoOrphanState(long nowMilliseconds) => ReconciliationOutcome.Ok;

    public string? LastRunStatus(string scopeType) => Status;
}

[TestClass]
public sealed class ConsoleCommandExecutorTests
{
    private sealed class StubProbe : IDatabaseIntegrityProbe
    {
        internal DatabaseProbeResult Quick { get; set; } = DatabaseProbeResult.Ok;

        internal DatabaseProbeResult ForeignKeys { get; set; } = DatabaseProbeResult.Ok;

        internal DatabaseProbeResult Integrity { get; set; } = DatabaseProbeResult.Ok;

        public DatabaseProbeResult QuickCheck() => Quick;

        public DatabaseProbeResult ForeignKeyCheck() => ForeignKeys;

        public DatabaseProbeResult IntegrityCheck() => Integrity;
    }

    private sealed class StubBackups : IDatabaseBackupService
    {
        internal BackupCreationResult Creation { get; set; } =
            new(true, "data/backups/economy.db", "data/backups/economy.manifest.json", string.Empty);

        internal BackupVerificationResult Verification { get; set; } = BackupVerificationResult.Passed;

        internal BackupSummary Inventory { get; set; } = BackupSummary.Empty;

        internal int CreateCalls { get; private set; }

        internal BackupKind LastKind { get; private set; }

        public BackupCreationResult Create(BackupKind kind)
        {
            CreateCalls++;
            LastKind = kind;
            return Creation;
        }

        public BackupVerificationResult VerifyAt(string databasePath) => Verification;

        public BackupSummary Summarize() => Inventory;

        internal string? Latest { get; set; }

        public string? FindLatestVerified() => Latest;

        public int PruneAutomatic() => 0;
    }

    private sealed class StubRestores : IDatabaseRestoreService
    {
        internal RestoreResult Outcome { get; set; } = new(true, string.Empty, "economy.db.recovery");

        internal string LastPath { get; private set; } = string.Empty;

        public RestoreResult Restore(string backupDatabasePath, long restoredAtUnixMilliseconds)
        {
            LastPath = backupDatabasePath;
            return Outcome;
        }
    }

    private sealed class StubGate : IMaintenanceGate
    {
        internal StubGate(bool quiesced) => IsQuiesced = quiesced;

        public bool IsQuiesced { get; }
    }

    private static ConsoleCommandResult Run(
        string line,
        StubProbe? probe = null,
        StubBackups? backups = null,
        StubRestores? restores = null,
        bool quiesced = true,
        PreviousStartupClassification previous = PreviousStartupClassification.Clean,
        StubReconciliation? reconciliation = null) =>
        new ConsoleCommandExecutor(
            probe ?? new StubProbe(),
            reconciliation ?? new StubReconciliation(),
            backups ?? new StubBackups(),
            restores ?? new StubRestores(),
            new StubGate(quiesced),
            TimeProvider.System,
            () => previous).Execute(ConsoleCommandLine.Parse(line));

    [TestMethod]
    public void HealthReportsTheLastFinancialReconciliation()
    {
        ConsoleCommandResult unknown = Run("health");

        CollectionAssert.Contains(unknown.Lines.ToArray(), "Financial Reconciliation: UNKNOWN");

        ConsoleCommandResult succeeded = Run(
            "health", reconciliation: new StubReconciliation { Status = "SUCCEEDED" });

        Assert.IsTrue(succeeded.IsSuccess);
        CollectionAssert.Contains(succeeded.Lines.ToArray(), "Financial Reconciliation: OK");

        ConsoleCommandResult issues = Run(
            "health", reconciliation: new StubReconciliation { Status = "ISSUES_FOUND" });

        Assert.IsFalse(issues.IsSuccess);
        CollectionAssert.Contains(issues.Lines.ToArray(), "Financial Reconciliation: FAILED");
    }

    [TestMethod]
    public void AHealthyDatabaseVerifies()
    {
        ConsoleCommandResult result = Run("database verify");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "Quick Check: OK");
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.DatabaseVerified);
    }

    [TestMethod]
    public void AFailedForeignKeyCheckFailsTheVerification()
    {
        StubProbe probe = new() { ForeignKeys = DatabaseProbeResult.Failed("3") };

        ConsoleCommandResult result = Run("database verify", probe);

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "Foreign Key Check: FAILED");
        CollectionAssert.DoesNotContain(result.Lines.ToArray(), ConsoleText.DatabaseVerified);
    }

    [TestMethod]
    public void TheBackupCommandRequestsAManualBackup()
    {
        StubBackups backups = new();

        ConsoleCommandResult result = Run("database backup", backups: backups);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, backups.CreateCalls);
        Assert.AreEqual(BackupKind.Manual, backups.LastKind);
    }

    [TestMethod]
    public void AFailedBackupReportsItsDetail()
    {
        StubBackups backups = new() { Creation = BackupCreationResult.Failed("QUICK_CHECK_FAILED") };

        ConsoleCommandResult result = Run("database backup", backups: backups);

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "QUICK_CHECK_FAILED");
    }

    [TestMethod]
    public void TheBackupListReportsTheInventory()
    {
        StubBackups backups = new()
        {
            Inventory = new BackupSummary(4, 1, 2, 8_192L, "2026-01-01T00:00:00Z", "2026-01-08T00:00:00Z"),
        };

        ConsoleCommandResult result = Run("database backup list", backups: backups);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "Automatic Backup Count: 4");
        CollectionAssert.Contains(result.Lines.ToArray(), "Manual Backup Count: 1");
        CollectionAssert.Contains(result.Lines.ToArray(), "Backup Total Bytes: 8192");
        CollectionAssert.Contains(result.Lines.ToArray(), "Oldest Backup: 2026-01-01T00:00:00Z");
    }

    [TestMethod]
    public void AnEmptyInventoryReportsNone()
    {
        ConsoleCommandResult result = Run("database backup list");

        CollectionAssert.Contains(result.Lines.ToArray(), "Oldest Backup: none");
    }

    [TestMethod]
    public void HealthCarriesTheCanonicalDisplayItems()
    {
        ConsoleCommandResult result = Run("health");

        string[] lines = [.. result.Lines];

        Assert.AreEqual(13, lines.Length);
        Assert.AreEqual("Runtime State: CLEAN", lines[0]);
        Assert.AreEqual("Current Database Quick Check: OK", lines[1]);
        Assert.AreEqual("Current Database Integrity Check: OK", lines[2]);
        Assert.AreEqual("Foreign Key Check: OK", lines[3]);
        Assert.AreEqual("Financial Reconciliation: UNKNOWN", lines[4]);
        Assert.AreEqual("Backup Redundancy: LOCAL_ONLY", lines[10]);
        Assert.AreEqual("Recovery Copy Present: no", lines[12]);
    }

    [TestMethod]
    public void AnUncleanPreviousStartupIsReported()
    {
        ConsoleCommandResult result = Run(
            "database recovery status", previous: PreviousStartupClassification.Unclean);

        Assert.AreEqual("Runtime State: UNCLEAN_RECOVERED", result.Lines[0]);
    }

    [TestMethod]
    public void AnUnknownLineIsRejected()
    {
        ConsoleCommandResult result = Run("database explode");

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.UnknownCommand);
    }

    [TestMethod]
    public void HelpListsEveryConsoleCommand()
    {
        ConsoleCommandResult result = Run("help");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "database restore latest");
    }

    [TestMethod]
    public void RestoringRequiresAQuiescedHost()
    {
        ConsoleCommandResult result = Run("database restore backups/economy.db", quiesced: false);

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.MaintenanceRequired);
    }

    [TestMethod]
    public void RestoringUsesTheGivenBackupPath()
    {
        StubRestores restores = new();

        ConsoleCommandResult result = Run("database restore backups/economy.db", restores: restores);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("backups/economy.db", restores.LastPath);
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.Restored + " economy.db.recovery");
    }

    [TestMethod]
    public void RestoringLatestRequiresAVerifiedBackup()
    {
        ConsoleCommandResult result = Run("database restore latest");

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.NoVerifiedBackup);
    }

    [TestMethod]
    public void RestoringLatestPicksTheVerifiedBackup()
    {
        StubBackups backups = new() { Latest = "backups/newest.db" };
        StubRestores restores = new();

        ConsoleCommandResult result = Run("database restore latest", backups: backups, restores: restores);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("backups/newest.db", restores.LastPath);
    }

    [TestMethod]
    public void AFailedRestoreReportsItsDetail()
    {
        StubRestores restores = new() { Outcome = RestoreResult.Failed("TEMP_QUICK_CHECK_FAILED") };

        ConsoleCommandResult result = Run("database restore backups/economy.db", restores: restores);

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), "TEMP_QUICK_CHECK_FAILED");
    }

    [TestMethod]
    public void UnboundCommandsReportThatTheyAreUnavailable()
    {
        ConsoleCommandResult result = Run("config show");

        Assert.IsFalse(result.IsSuccess);
        CollectionAssert.Contains(result.Lines.ToArray(), ConsoleText.NotImplemented);
    }
}
