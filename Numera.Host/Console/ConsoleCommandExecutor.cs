using System.Globalization;
using Numera.Host.Startup;
using Numera.Persistence.Sqlite;

namespace Numera.Host.Console;

public sealed record ConsoleCommandResult(bool IsSuccess, IReadOnlyList<string> Lines)
{
    public static ConsoleCommandResult Ok(params string[] lines) => new(true, lines);

    public static ConsoleCommandResult Failed(params string[] lines) => new(false, lines);
}

public static class ConsoleText
{
    public const string Ok = "OK";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
    public const string NotRun = "NOT_RUN";
    public const string None = "none";
    public const string Yes = "yes";
    public const string No = "no";
    public const string LocalOnly = "LOCAL_ONLY";
    public const string UnknownCommand = "Unknown command. Type help for the command list.";
    public const string NotImplemented = "This command is not available in this build.";
    public const string BackupCreated = "Backup created:";
    public const string BackupVerified = "Backup verified.";
    public const string DatabaseVerified = "Database verified.";
}

internal sealed class ConsoleCommandExecutor
{
    private readonly IDatabaseIntegrityProbe probe;
    private readonly IDatabaseBackupService backups;
    private readonly Func<PreviousStartupClassification> runtimeState;

    internal ConsoleCommandExecutor(
        IDatabaseIntegrityProbe probe,
        IDatabaseBackupService backups,
        Func<PreviousStartupClassification> runtimeState)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(runtimeState);

        this.probe = probe;
        this.backups = backups;
        this.runtimeState = runtimeState;
    }

    internal ConsoleCommandResult Execute(ConsoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.Kind switch
        {
            ConsoleCommandKind.DatabaseVerify => VerifyDatabase(),
            ConsoleCommandKind.DatabaseBackup => CreateBackup(),
            ConsoleCommandKind.DatabaseBackupList => ListBackups(),
            ConsoleCommandKind.DatabaseBackupVerify => VerifyBackup(command.Argument),
            ConsoleCommandKind.DatabaseRecoveryStatus or ConsoleCommandKind.Health => ReportHealth(),
            ConsoleCommandKind.Help => Help(),
            ConsoleCommandKind.Unknown => ConsoleCommandResult.Failed(ConsoleText.UnknownCommand),
            _ => ConsoleCommandResult.Failed(ConsoleText.NotImplemented),
        };
    }

    private static ConsoleCommandResult Help() => ConsoleCommandResult.Ok(
        "config show",
        "database verify",
        "database backup",
        "database backup list",
        "database backup verify <backup-path>",
        "database restore <backup-path>",
        "database restore latest",
        "database recovery status",
        "commands sync",
        "discord reconnect",
        "health",
        "help",
        "shutdown");

    private ConsoleCommandResult VerifyDatabase()
    {
        DatabaseProbeResult quick = probe.QuickCheck();
        DatabaseProbeResult foreignKeys = probe.ForeignKeyCheck();
        DatabaseProbeResult integrity = probe.IntegrityCheck();

        string[] lines =
        [
            "Quick Check: " + Token(quick),
            "Foreign Key Check: " + Token(foreignKeys),
            "Integrity Check: " + Token(integrity),
        ];

        return quick.IsOk && foreignKeys.IsOk && integrity.IsOk
            ? new ConsoleCommandResult(true, [.. lines, ConsoleText.DatabaseVerified])
            : new ConsoleCommandResult(false, lines);
    }

    private ConsoleCommandResult CreateBackup()
    {
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        return created.IsSuccess
            ? ConsoleCommandResult.Ok(ConsoleText.BackupCreated + " " + created.DatabasePath)
            : ConsoleCommandResult.Failed(created.Detail);
    }

    private ConsoleCommandResult ListBackups()
    {
        BackupSummary summary = backups.Summarize();

        return ConsoleCommandResult.Ok(
            "Automatic Backup Count: " + Number(summary.AutomaticCount),
            "Manual Backup Count: " + Number(summary.ManualCount),
            "Pre-Migration Backup Count: " + Number(summary.PreMigrationCount),
            "Backup Total Bytes: " + Number(summary.TotalBytes),
            "Oldest Backup: " + Present(summary.OldestCreatedAtUtc),
            "Newest Backup: " + Present(summary.NewestCreatedAtUtc));
    }

    private ConsoleCommandResult VerifyBackup(string path)
    {
        BackupVerificationResult verified = backups.VerifyAt(path);

        return verified.IsSuccess
            ? ConsoleCommandResult.Ok(ConsoleText.BackupVerified)
            : ConsoleCommandResult.Failed(verified.Detail);
    }

    private ConsoleCommandResult ReportHealth()
    {
        DatabaseProbeResult quick = probe.QuickCheck();
        DatabaseProbeResult foreignKeys = probe.ForeignKeyCheck();
        DatabaseProbeResult integrity = probe.IntegrityCheck();
        BackupSummary summary = backups.Summarize();

        return new ConsoleCommandResult(
            quick.IsOk && foreignKeys.IsOk && integrity.IsOk,
            [
                "Runtime State: " + RuntimeStateToken(),
                "Current Database Quick Check: " + Token(quick),
                "Current Database Integrity Check: " + Token(integrity),
                "Foreign Key Check: " + Token(foreignKeys),
                "Financial Reconciliation: " + ConsoleText.Unknown,
                "Last Successful Backup: " + Present(summary.NewestCreatedAtUtc),
                "Last Full-Verified Backup: " + Present(summary.NewestCreatedAtUtc),
                "Automatic Backup Count: " + Number(summary.AutomaticCount),
                "Manual Backup Count: " + Number(summary.ManualCount),
                "Backup Total Bytes: " + Number(summary.TotalBytes),
                "Backup Redundancy: " + ConsoleText.LocalOnly,
                "Recovery Point Age: " + ConsoleText.None,
                "Recovery Copy Present: " + ConsoleText.No,
            ]);
    }

    private string RuntimeStateToken() => runtimeState() switch
    {
        PreviousStartupClassification.Clean => "CLEAN",
        _ => "UNCLEAN_RECOVERED",
    };

    private static string Token(DatabaseProbeResult result) =>
        result.IsOk ? ConsoleText.Ok : ConsoleText.Failed;

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Present(string value) => value.Length == 0 ? ConsoleText.None : value;
}
