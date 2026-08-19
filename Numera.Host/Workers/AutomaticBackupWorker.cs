using System.Globalization;
using Microsoft.Extensions.Hosting;
using Numera.Host.Logging;
using Numera.Persistence.Sqlite;

namespace Numera.Host.Workers;

internal interface IAutomaticBackupScheduler
{
    bool RunOnce();
}

internal sealed class AutomaticBackupScheduler : IAutomaticBackupScheduler
{
    internal const int CadenceHours = 6;
    internal const int SuppressionMinutes = 60;

    private readonly IDatabaseBackupService backups;
    private readonly IMaintenanceDiagnostics diagnostics;
    private readonly TimeProvider timeProvider;

    public AutomaticBackupScheduler(
        IDatabaseBackupService backups,
        IMaintenanceDiagnostics diagnostics,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.backups = backups;
        this.diagnostics = diagnostics;
        this.timeProvider = timeProvider;
    }

    internal static TimeSpan Cadence => TimeSpan.FromHours(CadenceHours);

    internal static TimeSpan Suppression => TimeSpan.FromMinutes(SuppressionMinutes);

    public bool RunOnce()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (!IsDue(backups.NewestAutomaticCreatedAtUtc(), now))
        {
            return false;
        }

        BackupCreationResult created = backups.Create(BackupKind.Automatic);

        if (!created.IsSuccess)
        {
            diagnostics.AutomaticBackupFailed(created.Detail);

            return false;
        }

        backups.PruneAutomatic();

        if (created.Detail.Length > 0)
        {
            diagnostics.AutomaticBackupFailed(created.Detail);
        }

        return true;
    }

    internal static bool IsDue(string? newestCreatedAtUtc, DateTimeOffset now)
    {
        if (newestCreatedAtUtc is null)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                newestCreatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset newest))
        {
            return true;
        }

        return now - newest >= Cadence;
    }

    internal static bool IsSuppressed(string? newestCreatedAtUtc, DateTimeOffset now) =>
        newestCreatedAtUtc is not null
        && DateTimeOffset.TryParse(
            newestCreatedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset newest)
        && now - newest < Suppression;
}

internal sealed class AutomaticBackupWorker : BackgroundService
{
    internal const int PollMinutes = 15;

    private readonly IAutomaticBackupScheduler scheduler;
    private readonly TimeProvider timeProvider;

    public AutomaticBackupWorker(IAutomaticBackupScheduler scheduler, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.scheduler = scheduler;
        this.timeProvider = timeProvider;
    }

    internal static TimeSpan PollInterval => TimeSpan.FromMinutes(PollMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        scheduler.RunOnce();

        using PeriodicTimer timer = new(PollInterval, timeProvider);

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

            scheduler.RunOnce();
        }
    }
}
