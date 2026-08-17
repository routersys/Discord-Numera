using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numera.Host.Startup;

internal enum RuntimeState
{
    Running = 1,
    CleanShutdown = 2,
}

internal enum PreviousStartupClassification
{
    Clean = 1,
    Unclean = 2,
}

internal sealed class RuntimeMarkerDocument
{
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("process_instance_id")]
    public string? ProcessInstanceId { get; set; }

    [JsonPropertyName("started_at_utc")]
    public string? StartedAtUtc { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("clean_shutdown_at_utc")]
    public string? CleanShutdownAtUtc { get; set; }
}

internal sealed class RuntimeStateMarker
{
    internal const int FormatVersion = 1;
    internal const string FileName = "runtime-state.json";
    internal const string TemporarySuffix = ".partial";
    internal const string RunningToken = "RUNNING";
    internal const string CleanShutdownToken = "CLEAN_SHUTDOWN";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string path;
    private readonly TimeProvider timeProvider;

    internal RuntimeStateMarker(string path, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.path = path;
        this.timeProvider = timeProvider;
    }

    internal static string PathFor(string directory) => Path.Combine(directory, FileName);

    internal PreviousStartupClassification ReadPrevious()
    {
        try
        {
            if (!File.Exists(path))
            {
                return PreviousStartupClassification.Unclean;
            }

            RuntimeMarkerDocument? document =
                JsonSerializer.Deserialize<RuntimeMarkerDocument>(File.ReadAllText(path), SerializerOptions);

            if (document is null || document.FormatVersion != FormatVersion)
            {
                return PreviousStartupClassification.Unclean;
            }

            return string.Equals(document.State, CleanShutdownToken, StringComparison.Ordinal)
                ? PreviousStartupClassification.Clean
                : PreviousStartupClassification.Unclean;
        }
        catch (IOException)
        {
            return PreviousStartupClassification.Unclean;
        }
        catch (UnauthorizedAccessException)
        {
            return PreviousStartupClassification.Unclean;
        }
        catch (JsonException)
        {
            return PreviousStartupClassification.Unclean;
        }
    }

    internal void WriteRunning(Guid processInstanceId) => Write(new RuntimeMarkerDocument
    {
        FormatVersion = FormatVersion,
        ProcessInstanceId = processInstanceId.ToString("D"),
        StartedAtUtc = Timestamp(),
        State = RunningToken,
        CleanShutdownAtUtc = null,
    });

    internal void WriteCleanShutdown()
    {
        RuntimeMarkerDocument document = Read() ?? new RuntimeMarkerDocument
        {
            FormatVersion = FormatVersion,
            ProcessInstanceId = Guid.CreateVersion7().ToString("D"),
            StartedAtUtc = Timestamp(),
        };

        document.FormatVersion = FormatVersion;
        document.State = CleanShutdownToken;
        document.CleanShutdownAtUtc = Timestamp();

        Write(document);
    }

    private RuntimeMarkerDocument? Read()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<RuntimeMarkerDocument>(File.ReadAllText(path), SerializerOptions)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Write(RuntimeMarkerDocument document)
    {
        string temporary = path + TemporarySuffix;

        File.WriteAllText(temporary, JsonSerializer.Serialize(document, SerializerOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private string Timestamp() =>
        timeProvider.GetUtcNow().UtcDateTime.ToString(BankingLogSchemaTimestamp.Format, provider: null);
}

internal static class BankingLogSchemaTimestamp
{
    internal const string Format = "O";
}
