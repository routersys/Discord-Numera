using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Numera.Discord.Commands;

internal enum CommandManifestType
{
    Slash = 1,
    User = 2,
    Message = 3,
}

internal enum CommandScope
{
    Global = 1,
    Guild = 2,
}

internal sealed record CommandChoiceManifest(string Name, string Value);

internal sealed record CommandOptionManifest(
    string Name,
    string Description,
    int Type,
    bool Required,
    bool Autocomplete,
    IReadOnlyList<CommandChoiceManifest> Choices,
    IReadOnlyList<CommandOptionManifest> Options)
{
    internal static IReadOnlyList<CommandOptionManifest> None { get; } = [];

    internal static IReadOnlyList<CommandChoiceManifest> NoChoices { get; } = [];
}

internal sealed record CommandManifestEntry(
    CommandManifestType Type,
    string Name,
    string Description,
    IReadOnlyList<CommandOptionManifest> Options)
{
    internal string Key => CommandManifestKey.Of(Type, Name);
}

internal sealed record CommandManifest(
    CommandScope Scope,
    ulong GuildId,
    IReadOnlyList<CommandManifestEntry> Commands);

internal static class CommandManifestKey
{
    internal const char Separator = '/';

    internal static string Of(CommandManifestType type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Concat(((int)type).ToString(CultureInfo.InvariantCulture), Separator.ToString(), name);
    }
}

internal static class CommandManifestJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    internal static string Write(CommandManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        ArrayBufferWriter<byte> buffer = new(512);

        using (Utf8JsonWriter json = new(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("scope", (int)manifest.Scope);
            json.WriteString("guildId", manifest.GuildId.ToString(CultureInfo.InvariantCulture));
            json.WriteStartArray("commands");

            foreach (CommandManifestEntry entry in Ordered(manifest.Commands))
            {
                WriteEntry(json, entry);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string Write(CommandManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        ArrayBufferWriter<byte> buffer = new(256);

        using (Utf8JsonWriter json = new(buffer, WriterOptions))
        {
            WriteEntry(json, entry);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string Hash(CommandManifest manifest) => Hash(Write(manifest));

    internal static string HashOf(CommandManifestEntry entry) => Hash(Write(entry));

    internal static IEnumerable<CommandManifestEntry> Ordered(IReadOnlyList<CommandManifestEntry> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        List<CommandManifestEntry> ordered = [.. commands];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        return ordered;
    }

    private static void WriteEntry(Utf8JsonWriter json, CommandManifestEntry entry)
    {
        json.WriteStartObject();
        json.WriteNumber("type", (int)entry.Type);
        json.WriteString("name", entry.Name);
        json.WriteString("description", entry.Description);
        WriteOptions(json, entry.Options);
        json.WriteEndObject();
    }

    private static void WriteOptions(Utf8JsonWriter json, IReadOnlyList<CommandOptionManifest> options)
    {
        if (options.Count == 0)
        {
            return;
        }

        json.WriteStartArray("options");

        foreach (CommandOptionManifest option in options)
        {
            json.WriteStartObject();
            json.WriteString("name", option.Name);
            json.WriteString("description", option.Description);
            json.WriteNumber("type", option.Type);
            json.WriteBoolean("required", option.Required);
            json.WriteBoolean("autocomplete", option.Autocomplete);

            if (option.Choices.Count > 0)
            {
                json.WriteStartArray("choices");

                foreach (CommandChoiceManifest choice in option.Choices)
                {
                    json.WriteStartObject();
                    json.WriteString("name", choice.Name);
                    json.WriteString("value", choice.Value);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            WriteOptions(json, option.Options);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static string Hash(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}
