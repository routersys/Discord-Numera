namespace Numera.Host.Startup;

internal enum RecoveryStage
{
    SingleInstanceLock = 1,
    ReadPreviousRuntimeMarker = 2,
    WriteCurrentRunningMarker = 3,
    OpenMainDatabaseWithWalFilesUntouched = 4,
    LetSqliteCompleteNativeRecovery = 5,
    VerifySchemaVersionNotNewerThanBinary = 6,
    PragmaQuickCheck = 7,
    PragmaForeignKeyCheck = 8,
    PragmaIntegrityCheck = 9,
    FinancialReconciliation = 10,
    RecoverExpiredLeasesAndIdempotentInProgressWork = 11,
    VerifyNoOrphanHoldOrImpossibleTerminalState = 12,
    StartupRecovered = 13,
    EnableSqliteWriteAdmission = 14,
    StartBackgroundWorkers = 15,
    ConnectDiscord = 16,
}

internal enum StartupCheckStatus
{
    Passed = 1,
    Failed = 2,
    NotAvailable = 3,
}

internal enum StartupRecoveryOutcome
{
    Recovered = 1,
    RecoveryRequired = 2,
}

internal sealed record StartupCheckResult(StartupCheckStatus Status, string? Detail = null)
{
    internal static StartupCheckResult Passed { get; } = new(StartupCheckStatus.Passed);

    internal static StartupCheckResult NotAvailable { get; } = new(StartupCheckStatus.NotAvailable);

    internal static StartupCheckResult Failed(string detail) => new(StartupCheckStatus.Failed, detail);
}

internal sealed record StartupRecoveryReport(
    PreviousStartupClassification Previous,
    StartupRecoveryOutcome Outcome,
    RecoveryStage LastStage,
    IReadOnlyList<RecoveryStage> Completed,
    IReadOnlyList<RecoveryStage> Skipped,
    string? Detail)
{
    internal bool MayConnectDiscord => Completed.Contains(RecoveryStage.ConnectDiscord);

    internal bool MayAcceptFinancialWrites => Completed.Contains(RecoveryStage.EnableSqliteWriteAdmission);
}

internal sealed record StartupRecoverySteps(
    Func<StartupCheckResult> AcquireSingleInstanceLock,
    Func<PreviousStartupClassification> ReadPreviousRuntimeMarker,
    Func<StartupCheckResult> WriteRunningMarker,
    Func<StartupCheckResult> OpenMainDatabase,
    Func<StartupCheckResult> VerifySchemaVersion,
    Func<StartupCheckResult> QuickCheck,
    Func<StartupCheckResult> ForeignKeyCheck,
    Func<StartupCheckResult> IntegrityCheck,
    Func<StartupCheckResult> FinancialReconciliation,
    Func<StartupCheckResult> RecoverIncompleteWork,
    Func<StartupCheckResult> VerifyNoOrphanState,
    Func<StartupCheckResult> EnableWriteAdmission,
    Func<StartupCheckResult> StartBackgroundWorkers,
    Func<StartupCheckResult> ConnectDiscord);

internal static class StartupRecoveryMachine
{
    internal static IReadOnlyList<RecoveryStage> CanonicalOrder { get; } =
        [.. Enum.GetValues<RecoveryStage>()];

    internal static StartupRecoveryReport Run(StartupRecoverySteps steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<RecoveryStage> completed = [];
        List<RecoveryStage> skipped = [];

        StartupCheckResult lockResult = steps.AcquireSingleInstanceLock();

        if (lockResult.Status == StartupCheckStatus.Failed)
        {
            return Stop(PreviousStartupClassification.Unclean, RecoveryStage.SingleInstanceLock, completed, skipped, lockResult);
        }

        completed.Add(RecoveryStage.SingleInstanceLock);

        PreviousStartupClassification previous = steps.ReadPreviousRuntimeMarker();
        completed.Add(RecoveryStage.ReadPreviousRuntimeMarker);

        (RecoveryStage Stage, Func<StartupCheckResult> Step)[] remaining =
        [
            (RecoveryStage.WriteCurrentRunningMarker, steps.WriteRunningMarker),
            (RecoveryStage.OpenMainDatabaseWithWalFilesUntouched, steps.OpenMainDatabase),
            (RecoveryStage.VerifySchemaVersionNotNewerThanBinary, steps.VerifySchemaVersion),
            (RecoveryStage.PragmaQuickCheck, steps.QuickCheck),
            (RecoveryStage.PragmaForeignKeyCheck, steps.ForeignKeyCheck),
            (RecoveryStage.PragmaIntegrityCheck, steps.IntegrityCheck),
            (RecoveryStage.FinancialReconciliation, steps.FinancialReconciliation),
            (RecoveryStage.RecoverExpiredLeasesAndIdempotentInProgressWork, steps.RecoverIncompleteWork),
            (RecoveryStage.VerifyNoOrphanHoldOrImpossibleTerminalState, steps.VerifyNoOrphanState),
            (RecoveryStage.EnableSqliteWriteAdmission, steps.EnableWriteAdmission),
            (RecoveryStage.StartBackgroundWorkers, steps.StartBackgroundWorkers),
            (RecoveryStage.ConnectDiscord, steps.ConnectDiscord),
        ];

        completed.Add(RecoveryStage.LetSqliteCompleteNativeRecovery);

        foreach ((RecoveryStage stage, Func<StartupCheckResult> step) in remaining)
        {
            if (stage == RecoveryStage.PragmaIntegrityCheck && previous == PreviousStartupClassification.Clean)
            {
                skipped.Add(stage);
                continue;
            }

            if (stage == RecoveryStage.EnableSqliteWriteAdmission)
            {
                completed.Add(RecoveryStage.StartupRecovered);
            }

            StartupCheckResult result = step();

            if (result.Status == StartupCheckStatus.Failed)
            {
                return Stop(previous, stage, completed, skipped, result);
            }

            if (result.Status == StartupCheckStatus.NotAvailable)
            {
                skipped.Add(stage);
                continue;
            }

            completed.Add(stage);
        }

        return new StartupRecoveryReport(
            previous,
            StartupRecoveryOutcome.Recovered,
            RecoveryStage.ConnectDiscord,
            completed,
            skipped,
            Detail: null);
    }

    private static StartupRecoveryReport Stop(
        PreviousStartupClassification previous,
        RecoveryStage stage,
        List<RecoveryStage> completed,
        List<RecoveryStage> skipped,
        StartupCheckResult result) =>
        new(previous, StartupRecoveryOutcome.RecoveryRequired, stage, completed, skipped, result.Detail);
}
