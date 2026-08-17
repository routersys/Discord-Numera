using Numera.Host.Startup;

namespace Numera.Host.Tests;

internal sealed class RecoveryStepRecorder
{
    internal List<RecoveryStage> Invoked { get; } = [];

    internal Dictionary<RecoveryStage, StartupCheckResult> Overrides { get; } = [];

    internal PreviousStartupClassification Previous { get; set; } = PreviousStartupClassification.Clean;

    internal StartupRecoverySteps Build() => new(
        AcquireSingleInstanceLock: () => Run(RecoveryStage.SingleInstanceLock),
        ReadPreviousRuntimeMarker: () =>
        {
            Invoked.Add(RecoveryStage.ReadPreviousRuntimeMarker);
            return Previous;
        },
        WriteRunningMarker: () => Run(RecoveryStage.WriteCurrentRunningMarker),
        OpenMainDatabase: () => Run(RecoveryStage.OpenMainDatabaseWithWalFilesUntouched),
        VerifySchemaVersion: () => Run(RecoveryStage.VerifySchemaVersionNotNewerThanBinary),
        QuickCheck: () => Run(RecoveryStage.PragmaQuickCheck),
        ForeignKeyCheck: () => Run(RecoveryStage.PragmaForeignKeyCheck),
        IntegrityCheck: () => Run(RecoveryStage.PragmaIntegrityCheck),
        FinancialReconciliation: () => Run(RecoveryStage.FinancialReconciliation),
        RecoverIncompleteWork: () => Run(RecoveryStage.RecoverExpiredLeasesAndIdempotentInProgressWork),
        VerifyNoOrphanState: () => Run(RecoveryStage.VerifyNoOrphanHoldOrImpossibleTerminalState),
        EnableWriteAdmission: () => Run(RecoveryStage.EnableSqliteWriteAdmission),
        StartBackgroundWorkers: () => Run(RecoveryStage.StartBackgroundWorkers),
        ConnectDiscord: () => Run(RecoveryStage.ConnectDiscord));

    private StartupCheckResult Run(RecoveryStage stage)
    {
        Invoked.Add(stage);

        return Overrides.TryGetValue(stage, out StartupCheckResult? result) ? result : StartupCheckResult.Passed;
    }
}

[TestClass]
public sealed class StartupRecoveryMachineTests
{
    private static readonly RecoveryStage[] FailClosedStages =
    [
        RecoveryStage.OpenMainDatabaseWithWalFilesUntouched,
        RecoveryStage.VerifySchemaVersionNotNewerThanBinary,
        RecoveryStage.PragmaQuickCheck,
        RecoveryStage.PragmaForeignKeyCheck,
        RecoveryStage.FinancialReconciliation,
        RecoveryStage.RecoverExpiredLeasesAndIdempotentInProgressWork,
        RecoveryStage.VerifyNoOrphanHoldOrImpossibleTerminalState,
    ];

    [TestMethod]
    public void TheCanonicalOrderCoversEveryStage()
    {
        Assert.HasCount(16, StartupRecoveryMachine.CanonicalOrder);
        Assert.AreEqual(RecoveryStage.SingleInstanceLock, StartupRecoveryMachine.CanonicalOrder[0]);
        Assert.AreEqual(RecoveryStage.ConnectDiscord, StartupRecoveryMachine.CanonicalOrder[^1]);
    }

    [TestMethod]
    public void ACleanRunReachesTheDiscordConnection()
    {
        RecoveryStepRecorder recorder = new();

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        Assert.AreEqual(StartupRecoveryOutcome.Recovered, report.Outcome);
        Assert.IsTrue(report.MayConnectDiscord);
        Assert.IsTrue(report.MayAcceptFinancialWrites);
    }

    [TestMethod]
    public void ACleanPreviousShutdownSkipsTheIntegrityCheck()
    {
        RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Clean };

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        CollectionAssert.DoesNotContain(recorder.Invoked, RecoveryStage.PragmaIntegrityCheck);
        CollectionAssert.Contains(report.Skipped.ToArray(), RecoveryStage.PragmaIntegrityCheck);
    }

    [TestMethod]
    public void AnUncleanPreviousShutdownRunsTheIntegrityCheck()
    {
        RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Unclean };

        StartupRecoveryMachine.Run(recorder.Build());

        CollectionAssert.Contains(recorder.Invoked, RecoveryStage.PragmaIntegrityCheck);
    }

    [TestMethod]
    public void AnUncleanStartupThatPassesEveryCheckKeepsTheCurrentDatabase()
    {
        RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Unclean };

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        Assert.AreEqual(StartupRecoveryOutcome.Recovered, report.Outcome);
        Assert.AreEqual(PreviousStartupClassification.Unclean, report.Previous);
    }

    [TestMethod]
    public void EveryFailClosedStageStopsBeforeWriteAdmissionAndDiscord()
    {
        foreach (RecoveryStage stage in FailClosedStages)
        {
            RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Unclean };
            recorder.Overrides[stage] = StartupCheckResult.Failed("check");

            StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

            Assert.AreEqual(StartupRecoveryOutcome.RecoveryRequired, report.Outcome, stage.ToString());
            Assert.AreEqual(stage, report.LastStage, stage.ToString());
            Assert.IsFalse(report.MayConnectDiscord, stage.ToString());
            Assert.IsFalse(report.MayAcceptFinancialWrites, stage.ToString());
            CollectionAssert.DoesNotContain(recorder.Invoked, RecoveryStage.ConnectDiscord);
            CollectionAssert.DoesNotContain(recorder.Invoked, RecoveryStage.EnableSqliteWriteAdmission);
        }
    }

    [TestMethod]
    public void AFailedIntegrityCheckOnAnUncleanStartupRequiresRecovery()
    {
        RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Unclean };
        recorder.Overrides[RecoveryStage.PragmaIntegrityCheck] = StartupCheckResult.Failed("integrity");

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        Assert.AreEqual(StartupRecoveryOutcome.RecoveryRequired, report.Outcome);
        Assert.IsFalse(report.MayConnectDiscord);
    }

    [TestMethod]
    public void AnUnavailableLockStopsEverything()
    {
        RecoveryStepRecorder recorder = new();
        recorder.Overrides[RecoveryStage.SingleInstanceLock] = StartupCheckResult.Failed("lock");

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        Assert.AreEqual(RecoveryStage.SingleInstanceLock, report.LastStage);
        CollectionAssert.DoesNotContain(recorder.Invoked, RecoveryStage.WriteCurrentRunningMarker);
    }

    [TestMethod]
    public void StartupRecoveredPrecedesWriteAdmission()
    {
        RecoveryStepRecorder recorder = new();

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        List<RecoveryStage> completed = [.. report.Completed];

        Assert.IsLessThan(
            completed.IndexOf(RecoveryStage.EnableSqliteWriteAdmission),
            completed.IndexOf(RecoveryStage.StartupRecovered));

        Assert.IsLessThan(
            completed.IndexOf(RecoveryStage.ConnectDiscord),
            completed.IndexOf(RecoveryStage.EnableSqliteWriteAdmission));
    }

    [TestMethod]
    public void TheRunningMarkerIsWrittenBeforeTheDatabaseIsOpened()
    {
        RecoveryStepRecorder recorder = new();

        StartupRecoveryMachine.Run(recorder.Build());

        Assert.IsLessThan(
            recorder.Invoked.IndexOf(RecoveryStage.OpenMainDatabaseWithWalFilesUntouched),
            recorder.Invoked.IndexOf(RecoveryStage.WriteCurrentRunningMarker));

        Assert.IsLessThan(
            recorder.Invoked.IndexOf(RecoveryStage.WriteCurrentRunningMarker),
            recorder.Invoked.IndexOf(RecoveryStage.ReadPreviousRuntimeMarker));
    }

    [TestMethod]
    public void RecoveryRequiredKeepsTheHostFromStarting()
    {
        foreach (RecoveryStage stage in FailClosedStages)
        {
            RecoveryStepRecorder recorder = new() { Previous = PreviousStartupClassification.Unclean };
            recorder.Overrides[stage] = StartupCheckResult.Failed("check");

            StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

            Assert.IsFalse(NumeraHost.MayStartHost(report), stage.ToString());
        }
    }

    [TestMethod]
    public void ARecoveredStartupLetsTheHostStart()
    {
        RecoveryStepRecorder recorder = new();

        Assert.IsTrue(NumeraHost.MayStartHost(StartupRecoveryMachine.Run(recorder.Build())));
    }

    [TestMethod]
    public void UnavailableChecksAreReportedAsSkippedWithoutBlockingStartup()
    {
        RecoveryStepRecorder recorder = new();
        recorder.Overrides[RecoveryStage.FinancialReconciliation] = StartupCheckResult.NotAvailable;

        StartupRecoveryReport report = StartupRecoveryMachine.Run(recorder.Build());

        Assert.AreEqual(StartupRecoveryOutcome.Recovered, report.Outcome);
        CollectionAssert.Contains(report.Skipped.ToArray(), RecoveryStage.FinancialReconciliation);
    }
}

[TestClass]
public sealed class RuntimeStateMarkerTests
{
    private static readonly DateTimeOffset CanonicalInstant = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static (RuntimeStateMarker Marker, string Path, string Directory) Create()
    {
        string directory = Path.Combine(Path.GetTempPath(), "numera-marker-" + Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = RuntimeStateMarker.PathFor(directory);

        return (new RuntimeStateMarker(path, new FixedTimeProvider(CanonicalInstant)), path, directory);
    }

    [TestMethod]
    public void AMissingMarkerIsUnclean()
    {
        (RuntimeStateMarker marker, _, string directory) = Create();

        try
        {
            Assert.AreEqual(PreviousStartupClassification.Unclean, marker.ReadPrevious());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ARunningMarkerIsUnclean()
    {
        (RuntimeStateMarker marker, _, string directory) = Create();

        try
        {
            marker.WriteRunning(Guid.CreateVersion7());

            Assert.AreEqual(PreviousStartupClassification.Unclean, marker.ReadPrevious());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ACleanShutdownMarkerIsClean()
    {
        (RuntimeStateMarker marker, string path, string directory) = Create();

        try
        {
            marker.WriteRunning(Guid.CreateVersion7());
            marker.WriteCleanShutdown();

            Assert.AreEqual(PreviousStartupClassification.Clean, marker.ReadPrevious());
            Assert.Contains(RuntimeStateMarker.CleanShutdownToken, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void UnreadableContentIsUnclean()
    {
        (RuntimeStateMarker marker, string path, string directory) = Create();

        try
        {
            File.WriteAllText(path, "not json");

            Assert.AreEqual(PreviousStartupClassification.Unclean, marker.ReadPrevious());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AnUnknownFormatVersionIsUnclean()
    {
        (RuntimeStateMarker marker, string path, string directory) = Create();

        try
        {
            File.WriteAllText(path, """{"format_version":99,"state":"CLEAN_SHUTDOWN"}""");

            Assert.AreEqual(PreviousStartupClassification.Unclean, marker.ReadPrevious());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void TheMarkerCarriesTheCanonicalSchema()
    {
        (RuntimeStateMarker marker, string path, string directory) = Create();

        try
        {
            marker.WriteRunning(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));

            string content = File.ReadAllText(path);

            Assert.Contains("\"format_version\":1", content);
            Assert.Contains("\"process_instance_id\":\"01234567-89ab-cdef-0123-456789abcdef\"", content);
            Assert.Contains("\"started_at_utc\":\"2026-08-15T12:00:00.0000000Z\"", content);
            Assert.Contains("\"state\":\"RUNNING\"", content);
            Assert.Contains("\"clean_shutdown_at_utc\":null", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void NoTemporaryFileSurvivesAWrite()
    {
        (RuntimeStateMarker marker, string path, string directory) = Create();

        try
        {
            marker.WriteRunning(Guid.CreateVersion7());
            marker.WriteCleanShutdown();

            Assert.IsFalse(File.Exists(path + RuntimeStateMarker.TemporarySuffix));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
