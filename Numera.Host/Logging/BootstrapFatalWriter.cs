using Microsoft.Extensions.Logging;

namespace Numera.Host.Logging;

internal static class BootstrapFatalEvents
{
    internal const int ConfigurationInvalidId = 9001;
    internal const string ConfigurationInvalidName = "Application.Bootstrap.ConfigurationInvalid";

    internal const int SingleInstanceUnavailableId = 9002;
    internal const string SingleInstanceUnavailableName = "Application.Bootstrap.SingleInstanceUnavailable";

    internal const int DatabaseUnavailableId = 9003;
    internal const string DatabaseUnavailableName = "Application.Bootstrap.DatabaseUnavailable";

    internal const int RecoveryRequiredId = 9004;
    internal const string RecoveryRequiredName = "Application.Bootstrap.RecoveryRequired";

    internal const int UnexpectedId = 9005;
    internal const string UnexpectedName = "Application.Bootstrap.Failed";
}

internal sealed class BootstrapFatalWriter
{
    private readonly TextWriter writer;
    private readonly TimeProvider timeProvider;

    internal BootstrapFatalWriter(TextWriter writer, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.writer = writer;
        this.timeProvider = timeProvider;
    }

    internal static BootstrapFatalWriter Standard() =>
        new(System.Console.Error, TimeProvider.System);

    internal void Write(int eventId, string eventName, string message, string? errorCode = null)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(message);

        BankingLogRecord record = new()
        {
            Timestamp = timeProvider.GetUtcNow(),
            Level = LogLevel.Critical,
            EventId = eventId,
            EventName = eventName,
            Message = message,
            ErrorCode = errorCode,
        };

        BankingJsonLogWriter.Write(writer, record);
        writer.Flush();
    }

    internal void Write(int eventId, string eventName, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);

        BankingLogRecord record = new()
        {
            Timestamp = timeProvider.GetUtcNow(),
            Level = LogLevel.Critical,
            EventId = eventId,
            EventName = eventName,
            Message = message,
            ExceptionType = exception.GetType().FullName,
            StackTrace = exception.StackTrace,
        };

        BankingJsonLogWriter.Write(writer, record);
        writer.Flush();
    }
}
