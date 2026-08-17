using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Numera.Host.Logging;

internal static class BankingLogSchema
{
    internal const string Timestamp = "timestamp";
    internal const string Level = "level";
    internal const string EventId = "eventId";
    internal const string EventName = "eventName";
    internal const string Message = "message";
    internal const string CorrelationId = "correlationId";
    internal const string OperationId = "operationId";
    internal const string InteractionId = "interactionId";
    internal const string GuildId = "guildId";
    internal const string UserId = "userId";
    internal const string BankId = "bankId";
    internal const string AccountId = "accountId";
    internal const string ElapsedMs = "elapsedMs";
    internal const string ErrorCode = "errorCode";
    internal const string ExceptionType = "exceptionType";
    internal const string StackTrace = "stackTrace";

    internal const string TimestampFormat = "O";

    internal static readonly string[] PropertyOrder =
    [
        Timestamp,
        Level,
        EventId,
        EventName,
        Message,
        CorrelationId,
        OperationId,
        InteractionId,
        GuildId,
        UserId,
        BankId,
        AccountId,
        ElapsedMs,
        ErrorCode,
        ExceptionType,
        StackTrace,
    ];

    internal static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Information",
        LogLevel.Warning => "Warning",
        LogLevel.Error => "Error",
        _ => "Critical",
    };

    internal static bool CarriesStackTrace(LogLevel level) => level >= LogLevel.Error;
}

internal sealed class BankingLogRecord
{
    internal DateTimeOffset Timestamp { get; set; }

    internal LogLevel Level { get; set; } = LogLevel.Information;

    internal int EventId { get; set; }

    internal string? EventName { get; set; }

    internal string Message { get; set; } = string.Empty;

    internal string? CorrelationId { get; set; }

    internal string? OperationId { get; set; }

    internal string? InteractionId { get; set; }

    internal string? GuildId { get; set; }

    internal string? UserId { get; set; }

    internal string? BankId { get; set; }

    internal string? AccountId { get; set; }

    internal long? ElapsedMs { get; set; }

    internal string? ErrorCode { get; set; }

    internal string? ExceptionType { get; set; }

    internal string? StackTrace { get; set; }

    internal bool TryApply(string key, object? value)
    {
        switch (key)
        {
            case BankingLogSchema.CorrelationId:
                CorrelationId = Stringify(value);
                return true;
            case BankingLogSchema.OperationId:
                OperationId = Stringify(value);
                return true;
            case BankingLogSchema.InteractionId:
                InteractionId = Stringify(value);
                return true;
            case BankingLogSchema.GuildId:
                GuildId = Stringify(value);
                return true;
            case BankingLogSchema.UserId:
                UserId = Stringify(value);
                return true;
            case BankingLogSchema.BankId:
                BankId = Stringify(value);
                return true;
            case BankingLogSchema.AccountId:
                AccountId = Stringify(value);
                return true;
            case BankingLogSchema.ElapsedMs:
                ElapsedMs = AsMilliseconds(value);
                return true;
            case BankingLogSchema.ErrorCode:
                ErrorCode = Stringify(value);
                return true;
            default:
                return false;
        }
    }

    private static string? Stringify(object? value) => value switch
    {
        null => null,
        string text => text.Length == 0 ? null : text,
        ulong number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        Guid identifier => identifier.ToString("N", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static long? AsMilliseconds(object? value) => value switch
    {
        long number => number,
        int number => number,
        double number => (long)number,
        string text when long.TryParse(text, CultureInfo.InvariantCulture, out long parsed) => parsed,
        _ => null,
    };
}

internal static class BankingJsonLogWriter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    internal static void Write(TextWriter writer, BankingLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        writer.Write(Serialize(record));
        writer.Write(Environment.NewLine);
    }

    internal static string Serialize(BankingLogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        ArrayBufferWriter<byte> buffer = new(256);

        using (Utf8JsonWriter json = new(buffer, WriterOptions))
        {
            json.WriteStartObject();

            json.WriteString(
                BankingLogSchema.Timestamp,
                record.Timestamp.UtcDateTime.ToString(BankingLogSchema.TimestampFormat, CultureInfo.InvariantCulture));
            json.WriteString(BankingLogSchema.Level, BankingLogSchema.LevelName(record.Level));
            json.WriteNumber(BankingLogSchema.EventId, record.EventId);
            WriteOptional(json, BankingLogSchema.EventName, record.EventName);
            json.WriteString(BankingLogSchema.Message, record.Message);
            WriteOptional(json, BankingLogSchema.CorrelationId, record.CorrelationId);
            WriteOptional(json, BankingLogSchema.OperationId, record.OperationId);
            WriteOptional(json, BankingLogSchema.InteractionId, record.InteractionId);
            WriteOptional(json, BankingLogSchema.GuildId, record.GuildId);
            WriteOptional(json, BankingLogSchema.UserId, record.UserId);
            WriteOptional(json, BankingLogSchema.BankId, record.BankId);
            WriteOptional(json, BankingLogSchema.AccountId, record.AccountId);

            if (record.ElapsedMs is long elapsed)
            {
                json.WriteNumber(BankingLogSchema.ElapsedMs, elapsed);
            }

            WriteOptional(json, BankingLogSchema.ErrorCode, record.ErrorCode);
            WriteOptional(json, BankingLogSchema.ExceptionType, record.ExceptionType);
            WriteOptional(json, BankingLogSchema.StackTrace, record.StackTrace);

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOptional(Utf8JsonWriter json, string property, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(property, value);
        }
    }
}
