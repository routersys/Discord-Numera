using Microsoft.Extensions.Logging;
using Numera.Application.Common;

namespace Numera.Host.Logging;

internal static class MaintenanceLogEvents
{
    internal const int SettlementMaintenanceCompletedId = 5001;
    internal const string SettlementMaintenanceCompletedName = "Settlement.Maintenance.Completed";

    internal const int SettlementMaintenanceFailedId = 5002;
    internal const string SettlementMaintenanceFailedName = "Settlement.Maintenance.Failed";

    internal const int WriteAdmissionOpenedId = 1001;
    internal const string WriteAdmissionOpenedName = "Application.Started";

    internal const int WriteAdmissionClosedId = 1002;
    internal const string WriteAdmissionClosedName = "Application.Stopping";
}

internal interface IMaintenanceDiagnostics
{
    void SettlementMaintenanceCompleted(int examined, int settled);

    void SettlementMaintenanceFailed(Exception exception);

    void WriteAdmissionOpened();

    void WriteAdmissionClosed();
}

internal sealed partial class MaintenanceDiagnostics : IMaintenanceDiagnostics
{
    private readonly ILogger<MaintenanceDiagnostics> logger;

    public MaintenanceDiagnostics(ILogger<MaintenanceDiagnostics> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    public void SettlementMaintenanceCompleted(int examined, int settled) =>
        LogSettlementMaintenanceCompleted(examined, settled);

    public void SettlementMaintenanceFailed(Exception exception) =>
        LogSettlementMaintenanceFailed(exception, BankingErrorCodes.SystemBusy);

    public void WriteAdmissionOpened() => LogWriteAdmissionOpened();

    public void WriteAdmissionClosed() => LogWriteAdmissionClosed();

    [LoggerMessage(
        EventId = MaintenanceLogEvents.SettlementMaintenanceCompletedId,
        EventName = MaintenanceLogEvents.SettlementMaintenanceCompletedName,
        Level = LogLevel.Information,
        Message = "Settlement maintenance examined {examined} records and settled {settled}.")]
    private partial void LogSettlementMaintenanceCompleted(int examined, int settled);

    [LoggerMessage(
        EventId = MaintenanceLogEvents.SettlementMaintenanceFailedId,
        EventName = MaintenanceLogEvents.SettlementMaintenanceFailedName,
        Level = LogLevel.Error,
        Message = "Settlement maintenance failed: {errorCode}.")]
    private partial void LogSettlementMaintenanceFailed(Exception exception, string errorCode);

    [LoggerMessage(
        EventId = MaintenanceLogEvents.WriteAdmissionOpenedId,
        EventName = MaintenanceLogEvents.WriteAdmissionOpenedName,
        Level = LogLevel.Information,
        Message = "SQLite write admission is open.")]
    private partial void LogWriteAdmissionOpened();

    [LoggerMessage(
        EventId = MaintenanceLogEvents.WriteAdmissionClosedId,
        EventName = MaintenanceLogEvents.WriteAdmissionClosedName,
        Level = LogLevel.Information,
        Message = "SQLite write admission is closed and the queue is drained.")]
    private partial void LogWriteAdmissionClosed();
}
