using Numera.Host.Console;
using Numera.Host.Startup;
using Numera.Persistence.Sqlite;

namespace Numera.Host.Tests;

[TestClass]
public sealed class BootstrapShellTests
{
    private sealed class HealthyProbe : IDatabaseIntegrityProbe
    {
        public DatabaseProbeResult QuickCheck() => DatabaseProbeResult.Ok;

        public DatabaseProbeResult ForeignKeyCheck() => DatabaseProbeResult.Ok;

        public DatabaseProbeResult IntegrityCheck() => DatabaseProbeResult.Ok;
    }

    private sealed class SilentBackups : IDatabaseBackupService
    {
        public BackupCreationResult Create(BackupKind kind) =>
            new(true, "economy.db", "economy.manifest.json", string.Empty);

        public BackupVerificationResult VerifyAt(string databasePath) => BackupVerificationResult.Passed;

        public BackupSummary Summarize() => BackupSummary.Empty;

        public string? FindLatestVerified() => null;

        public int PruneAutomatic() => 0;
    }

    private sealed class UnusedRestores : IDatabaseRestoreService
    {
        public RestoreResult Restore(string backupDatabasePath, long restoredAtUnixMilliseconds) =>
            RestoreResult.Failed("UNUSED");
    }

    private sealed class OpenGate : IMaintenanceGate
    {
        public bool IsQuiesced => false;
    }

    private static (ShellSession Session, string Output) Run(string script, CancellationToken cancellationToken)
    {
        ConsoleCommandExecutor executor = new(
            new HealthyProbe(),
            new StubReconciliation(),
            new SilentBackups(),
            new UnusedRestores(),
            new OpenGate(),
            TimeProvider.System,
            static () => PreviousStartupClassification.Clean);

        using StringReader input = new(script);
        using StringWriter output = new();

        ShellSession session = new BootstrapShell(executor, input, output).Run(cancellationToken);

        return (session, output.ToString());
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void TheShellStopsOnShutdown()
    {
        (ShellSession session, string output) = Run(
            "database verify\nshutdown\n", TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(ShellExitReason.ShutdownRequested, session.Reason);
        Assert.AreEqual(1, session.ExecutedCount);
        Assert.AreEqual(0, session.FailedCount);
        StringAssert.Contains(output, ConsoleText.DatabaseVerified);
    }

    [TestMethod]
    public void TheShellStopsWhenInputCloses()
    {
        (ShellSession session, _) = Run("help\n", TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(ShellExitReason.InputClosed, session.Reason);
        Assert.AreEqual(1, session.ExecutedCount);
    }

    [TestMethod]
    public void EveryLineIsPrompted()
    {
        (_, string output) = Run("help\nshutdown\n", TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(2, output.Split(ConsoleCommandLine.Prompt).Length - 1);
    }

    [TestMethod]
    public void FailedCommandsAreCounted()
    {
        (ShellSession session, string output) = Run(
            "database explode\nshutdown\n", TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, session.ExecutedCount);
        Assert.AreEqual(1, session.FailedCount);
        StringAssert.Contains(output, ConsoleText.UnknownCommand);
    }

    [TestMethod]
    public void ACancelledShellStopsBeforeReading()
    {
        using CancellationTokenSource source = new();
        source.Cancel();

        (ShellSession session, string output) = Run("shutdown\n", source.Token);

        Assert.AreEqual(ShellExitReason.Cancelled, session.Reason);
        Assert.AreEqual(0, session.ExecutedCount);
        Assert.IsEmpty(output);
    }
}
