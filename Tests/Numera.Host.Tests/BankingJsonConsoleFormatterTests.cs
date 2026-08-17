using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Numera.Host.Logging;

namespace Numera.Host.Tests;

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset now;

    internal FixedTimeProvider(DateTimeOffset now) => this.now = now;

    public override DateTimeOffset GetUtcNow() => now;
}

[TestClass]
public sealed class BankingJsonConsoleFormatterTests
{
    private static readonly DateTimeOffset CanonicalInstant =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static string Format<TState>(
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter,
        IExternalScopeProvider? scopeProvider = null)
    {
        BankingJsonConsoleFormatter target = new(new FixedTimeProvider(CanonicalInstant));
        LogEntry<TState> entry = new(level, "Numera.Test", eventId, state, exception, formatter);
        StringWriter writer = new();

        target.Write(in entry, scopeProvider, writer);

        return writer.ToString();
    }

    private static string FormatMessage(
        LogLevel level,
        EventId eventId,
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string message,
        Exception? exception = null,
        IExternalScopeProvider? scopeProvider = null) =>
        Format(level, eventId, state, exception, (_, _) => message, scopeProvider);

    private static string[] PropertyNames(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return [.. document.RootElement.EnumerateObject().Select(static property => property.Name)];
    }

    private static string Value(string line, string property)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty(property).ToString();
    }

    private static IExternalScopeProvider Scope(params KeyValuePair<string, object?>[] pairs)
    {
        LoggerExternalScopeProvider provider = new();
        provider.Push(pairs);
        return provider;
    }

    [TestMethod]
    public void CanonicalEventMatchesTheSpecifiedLine()
    {
        string line = FormatMessage(
            LogLevel.Information,
            new EventId(4001, "Bank.Transfer.Accepted"),
            [],
            "Transfer was accepted.",
            scopeProvider: Scope(
                new KeyValuePair<string, object?>("correlationId", "01"),
                new KeyValuePair<string, object?>("operationId", "02"),
                new KeyValuePair<string, object?>("interactionId", 123456789012345678UL),
                new KeyValuePair<string, object?>("guildId", 223456789012345678UL),
                new KeyValuePair<string, object?>("userId", 323456789012345678UL),
                new KeyValuePair<string, object?>("elapsedMs", 18)));

        Assert.AreEqual(
            """
            {"timestamp":"2026-08-15T12:00:00.0000000Z","level":"Information","eventId":4001,"eventName":"Bank.Transfer.Accepted","message":"Transfer was accepted.","correlationId":"01","operationId":"02","interactionId":"123456789012345678","guildId":"223456789012345678","userId":"323456789012345678","elapsedMs":18}
            """ + Environment.NewLine,
            line);
    }

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    [TestMethod]
    public void EveryPropertyAppearsInTheCanonicalOrder()
    {
        string line = FormatMessage(
            LogLevel.Error,
            new EventId(9001, "Discord.Interaction.Failed"),
            [
                new KeyValuePair<string, object?>("bankId", "bank-1"),
                new KeyValuePair<string, object?>("accountId", "account-1"),
                new KeyValuePair<string, object?>("errorCode", "BANK-PAY-001"),
            ],
            "Interaction failed.",
            Thrown(),
            Scope(
                new KeyValuePair<string, object?>("correlationId", "01"),
                new KeyValuePair<string, object?>("operationId", "02"),
                new KeyValuePair<string, object?>("interactionId", "03"),
                new KeyValuePair<string, object?>("guildId", "04"),
                new KeyValuePair<string, object?>("userId", "05"),
                new KeyValuePair<string, object?>("elapsedMs", 7L)));

        string[] names = PropertyNames(line);

        CollectionAssert.AreEqual(BankingLogSchema.PropertyOrder, names);
    }

    [TestMethod]
    public void PresentPropertiesKeepTheCanonicalRelativeOrder()
    {
        string line = FormatMessage(
            LogLevel.Warning,
            new EventId(2001, "Discord.Gateway.Disconnected"),
            [new KeyValuePair<string, object?>("errorCode", "GW-1")],
            "Gateway disconnected.",
            scopeProvider: Scope(new KeyValuePair<string, object?>("correlationId", "01")));

        string[] names = PropertyNames(line);
        int[] positions = [.. names.Select(static name => Array.IndexOf(BankingLogSchema.PropertyOrder, name))];

        CollectionAssert.DoesNotContain(positions, -1);
        CollectionAssert.AreEqual(positions.Order().ToArray(), positions);
    }

    [TestMethod]
    public void AbsentOptionalPropertiesAreOmitted()
    {
        string line = FormatMessage(LogLevel.Information, new EventId(1001, "Application.Started"), [], "Started.");

        CollectionAssert.AreEqual(
            new[]
            {
                BankingLogSchema.Timestamp,
                BankingLogSchema.Level,
                BankingLogSchema.EventId,
                BankingLogSchema.EventName,
                BankingLogSchema.Message,
            },
            PropertyNames(line));
    }

    [TestMethod]
    public void EventNameIsOmittedWhenTheEventIdCarriesNoName()
    {
        string line = FormatMessage(LogLevel.Information, new EventId(1001), [], "Started.");

        CollectionAssert.DoesNotContain(PropertyNames(line), BankingLogSchema.EventName);
    }

    [TestMethod]
    public void OneEventOccupiesExactlyOnePhysicalLine()
    {
        string line = FormatMessage(
            LogLevel.Critical,
            new EventId(9005, "Application.Bootstrap.Failed"),
            [],
            "Line one.\nLine two.",
            new InvalidOperationException("boom"));

        string[] physical = line.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(1, physical.Length);
        Assert.AreEqual("Line one.\nLine two.", Value(line, BankingLogSchema.Message));
    }

    [TestMethod]
    public void ExceptionMessageNeverReachesTheOutput()
    {
        string line = FormatMessage(
            LogLevel.Error,
            new EventId(9005, "Application.Bootstrap.Failed"),
            [],
            "Operation failed.",
            new InvalidOperationException("token=abcdef"));

        Assert.IsFalse(line.Contains("abcdef", StringComparison.Ordinal));
        Assert.AreEqual("System.InvalidOperationException", Value(line, BankingLogSchema.ExceptionType));
    }

    [TestMethod]
    public void StackTraceIsRecordedOnlyForErrorAndCritical()
    {
        Exception thrown = Thrown();

        foreach (LogLevel level in (LogLevel[])[LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning])
        {
            string quiet = FormatMessage(level, new EventId(1), [], "Handled.", thrown);
            CollectionAssert.DoesNotContain(PropertyNames(quiet), BankingLogSchema.StackTrace);
        }

        foreach (LogLevel level in (LogLevel[])[LogLevel.Error, LogLevel.Critical])
        {
            string loud = FormatMessage(level, new EventId(1), [], "Failed.", thrown);
            CollectionAssert.Contains(PropertyNames(loud), BankingLogSchema.StackTrace);
        }
    }

    [TestMethod]
    [DataRow(LogLevel.Trace, "Trace")]
    [DataRow(LogLevel.Debug, "Debug")]
    [DataRow(LogLevel.Information, "Information")]
    [DataRow(LogLevel.Warning, "Warning")]
    [DataRow(LogLevel.Error, "Error")]
    [DataRow(LogLevel.Critical, "Critical")]
    public void LevelNamesAreCanonical(LogLevel level, string expected) =>
        Assert.AreEqual(expected, Value(FormatMessage(level, new EventId(1), [], "Message."), BankingLogSchema.Level));

    [TestMethod]
    public void StateOverridesScopeForTheSameProperty()
    {
        string line = FormatMessage(
            LogLevel.Information,
            new EventId(1),
            [new KeyValuePair<string, object?>("operationId", "state")],
            "Message.",
            scopeProvider: Scope(new KeyValuePair<string, object?>("operationId", "scope")));

        Assert.AreEqual("state", Value(line, BankingLogSchema.OperationId));
    }

    [TestMethod]
    public void UnknownStatePropertiesAreDropped()
    {
        string line = FormatMessage(
            LogLevel.Information,
            new EventId(1),
            [
                new KeyValuePair<string, object?>("amountMinor", 1000L),
                new KeyValuePair<string, object?>("{OriginalFormat}", "Message."),
            ],
            "Message.");

        CollectionAssert.DoesNotContain(PropertyNames(line), "amountMinor");
        Assert.IsFalse(line.Contains("1000", StringComparison.Ordinal));
    }

    [TestMethod]
    public void StandardErrorThresholdIsFixedToError()
    {
        ConsoleLoggerOptions options = new();

        BankingConsoleLogging.ConfigureConsole(options);

        LogLevel expected = LogLevel.Error;
        Assert.AreEqual(expected, options.LogToStandardErrorThreshold);
        Assert.AreEqual(BankingJsonConsoleFormatter.FormatterName, options.FormatterName);
    }

    [TestMethod]
    public void FormatterOptionsIncludeScopesInUtc()
    {
        ConsoleFormatterOptions options = new();

        BankingConsoleLogging.ConfigureFormatter(options);

        Assert.IsTrue(options.IncludeScopes);
        Assert.IsTrue(options.UseUtcTimestamp);
        Assert.IsNull(options.TimestampFormat);
    }
}
