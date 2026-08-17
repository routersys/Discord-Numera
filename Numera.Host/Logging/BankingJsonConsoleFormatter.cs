using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Numera.Host.Logging;

internal sealed class BankingJsonConsoleFormatter : ConsoleFormatter
{
    internal const string FormatterName = "banking-json";

    private readonly TimeProvider timeProvider;

    public BankingJsonConsoleFormatter()
        : this(TimeProvider.System)
    {
    }

    internal BankingJsonConsoleFormatter(TimeProvider timeProvider)
        : base(FormatterName)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        BankingLogRecord record = new()
        {
            Timestamp = timeProvider.GetUtcNow(),
            Level = logEntry.LogLevel,
            EventId = logEntry.EventId.Id,
            EventName = logEntry.EventId.Name,
            Message = logEntry.Formatter is null
                ? string.Empty
                : logEntry.Formatter(logEntry.State, null),
        };

        if (logEntry.Exception is Exception exception)
        {
            record.ExceptionType = exception.GetType().FullName;

            if (BankingLogSchema.CarriesStackTrace(logEntry.LogLevel))
            {
                record.StackTrace = exception.StackTrace;
            }
        }

        scopeProvider?.ForEachScope(static (scope, state) => ApplyScope(scope, state), record);
        ApplyState(logEntry.State, record);

        BankingJsonLogWriter.Write(textWriter, record);
    }

    private static void ApplyScope(object? scope, BankingLogRecord record) => ApplyPairs(scope, record);

    private static void ApplyState<TState>(TState state, BankingLogRecord record) => ApplyPairs(state, record);

    private static void ApplyPairs(object? candidate, BankingLogRecord record)
    {
        if (candidate is not IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            return;
        }

        for (int index = 0; index < pairs.Count; index++)
        {
            KeyValuePair<string, object?> pair = pairs[index];
            record.TryApply(pair.Key, pair.Value);
        }
    }
}

internal static class BankingConsoleLogging
{
    internal const LogLevel StandardErrorThreshold = LogLevel.Error;

    internal static void Configure(ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ClearProviders();
        builder.AddConsole(ConfigureConsole);
        builder.AddConsoleFormatter<BankingJsonConsoleFormatter, ConsoleFormatterOptions>();
        builder.Services.Configure<ConsoleFormatterOptions>(
            BankingJsonConsoleFormatter.FormatterName,
            ConfigureFormatter);
    }

    internal static void ConfigureConsole(ConsoleLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.FormatterName = BankingJsonConsoleFormatter.FormatterName;
        options.LogToStandardErrorThreshold = StandardErrorThreshold;
    }

    internal static void ConfigureFormatter(ConsoleFormatterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.IncludeScopes = true;
        options.TimestampFormat = null;
        options.UseUtcTimestamp = true;
    }
}
