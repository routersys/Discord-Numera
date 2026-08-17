using Microsoft.Extensions.Logging;
using Numera.Application.Common;
using Numera.Discord.Abstractions;

namespace Numera.Host.Logging;

internal static class DiscordLogEvents
{
    internal const int GatewayReadyId = 2001;
    internal const string GatewayReadyName = "Discord.Gateway.Ready";

    internal const int GatewayDisconnectedId = 2002;
    internal const string GatewayDisconnectedName = "Discord.Gateway.Disconnected";

    internal const int InteractionReceivedId = 2003;
    internal const string InteractionReceivedName = "Discord.Interaction.Received";

    internal const int InteractionFailedId = 2004;
    internal const string InteractionFailedName = "Discord.Interaction.Failed";

    internal const int CommandSyncCompletedId = 2005;
    internal const string CommandSyncCompletedName = "Discord.CommandSync.Completed";

    internal const int CommandSyncFailedId = 2006;
    internal const string CommandSyncFailedName = "Discord.CommandSync.Failed";

    internal const int LibraryEventId = 2007;
    internal const string LibraryEventName = "Discord.Library.Event";
}

internal sealed partial class DiscordDiagnostics : IDiscordDiagnostics
{
    private readonly ILogger<DiscordDiagnostics> logger;

    public DiscordDiagnostics(ILogger<DiscordDiagnostics> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    public IDisposable BeginInteractionScope(DiscordInteractionCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        List<KeyValuePair<string, object?>> state =
        [
            new(BankingLogSchema.CorrelationId, correlation.CorrelationId),
            new(BankingLogSchema.InteractionId, correlation.InteractionId),
            new(BankingLogSchema.UserId, correlation.UserId),
        ];

        if (correlation.GuildId != 0UL)
        {
            state.Add(new KeyValuePair<string, object?>(BankingLogSchema.GuildId, correlation.GuildId));
        }

        return logger.BeginScope(state) ?? NullScope.Instance;
    }

    public void GatewayReady(ulong applicationId) => LogGatewayReady(applicationId);

    public void GatewayDisconnected(Exception? exception) => LogGatewayDisconnected(exception);

    public void InteractionReceived(string commandPath) => LogInteractionReceived(commandPath);

    public void InteractionFailed(string errorCode) =>
        LogInteractionFailed(LogLevel.Warning, exception: null, errorCode);

    public void InteractionFaulted(Exception exception) =>
        LogInteractionFailed(LogLevel.Error, exception, BankingErrorCodes.InteractionExecutionFailed);

    public void CommandSyncCompleted(DiscordCommandSyncOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        LogCommandSyncCompleted(outcome.Created, outcome.Edited, outcome.Deleted, outcome.Unchanged);
    }

    public void CommandSyncFailed(Exception exception) =>
        LogCommandSyncFailed(exception, BankingErrorCodes.CommandSyncFailed);

    public void LibraryEvent(
        DiscordDiagnosticSeverity severity,
        string source,
        string message,
        Exception? exception) =>
        LogLibraryEvent(MapLevel(severity), exception, source, message);

    internal static LogLevel MapLevel(DiscordDiagnosticSeverity severity) => severity switch
    {
        DiscordDiagnosticSeverity.Critical => LogLevel.Critical,
        DiscordDiagnosticSeverity.Error => LogLevel.Error,
        DiscordDiagnosticSeverity.Warning => LogLevel.Warning,
        DiscordDiagnosticSeverity.Information => LogLevel.Information,
        DiscordDiagnosticSeverity.Verbose => LogLevel.Debug,
        _ => LogLevel.Trace,
    };

    [LoggerMessage(
        EventId = DiscordLogEvents.GatewayReadyId,
        EventName = DiscordLogEvents.GatewayReadyName,
        Level = LogLevel.Information,
        Message = "The Discord gateway is ready for application {applicationId}.")]
    private partial void LogGatewayReady(ulong applicationId);

    [LoggerMessage(
        EventId = DiscordLogEvents.GatewayDisconnectedId,
        EventName = DiscordLogEvents.GatewayDisconnectedName,
        Level = LogLevel.Warning,
        Message = "The Discord gateway was disconnected.")]
    private partial void LogGatewayDisconnected(Exception? exception);

    [LoggerMessage(
        EventId = DiscordLogEvents.InteractionReceivedId,
        EventName = DiscordLogEvents.InteractionReceivedName,
        Level = LogLevel.Information,
        Message = "An interaction was received for {commandPath}.")]
    private partial void LogInteractionReceived(string commandPath);

    [LoggerMessage(
        EventId = DiscordLogEvents.InteractionFailedId,
        EventName = DiscordLogEvents.InteractionFailedName,
        Message = "An interaction could not be completed: {errorCode}.")]
    private partial void LogInteractionFailed(LogLevel level, Exception? exception, string errorCode);

    [LoggerMessage(
        EventId = DiscordLogEvents.CommandSyncCompletedId,
        EventName = DiscordLogEvents.CommandSyncCompletedName,
        Level = LogLevel.Information,
        Message = "Command synchronization completed with {created} created, {edited} edited, {deleted} deleted and {unchanged} unchanged.")]
    private partial void LogCommandSyncCompleted(int created, int edited, int deleted, int unchanged);

    [LoggerMessage(
        EventId = DiscordLogEvents.CommandSyncFailedId,
        EventName = DiscordLogEvents.CommandSyncFailedName,
        Level = LogLevel.Error,
        Message = "Command synchronization failed: {errorCode}.")]
    private partial void LogCommandSyncFailed(Exception exception, string errorCode);

    [LoggerMessage(
        EventId = DiscordLogEvents.LibraryEventId,
        EventName = DiscordLogEvents.LibraryEventName,
        Message = "{librarySource}: {libraryMessage}")]
    private partial void LogLibraryEvent(
        LogLevel level,
        Exception? exception,
        string librarySource,
        string libraryMessage);
}

internal sealed class NullScope : IDisposable
{
    internal static NullScope Instance { get; } = new();

    private NullScope()
    {
    }

    public void Dispose()
    {
    }
}
