using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace Numera.Discord.Rendering;

internal enum CardFontRole
{
    General = 1,
    Mono = 2,
    Fallback = 3,
}

internal sealed record CardFontEntry(
    string Family,
    string Style,
    string Weight,
    string RelativePath,
    string Sha256,
    string LicenseSpdx,
    string UpstreamRelease);

internal interface ICardFontProvider
{
    SKTypeface Resolve(CardFontRole role);

    bool TryResolveFallback(out SKTypeface typeface);
}

internal sealed class CardFontManifestException : Exception
{
    internal CardFontManifestException(string message)
        : base(message)
    {
    }
}

internal static class CardFontManifest
{
    internal const string FileName = "font-manifest.json";

    internal static IReadOnlyDictionary<CardFontRole, CardFontEntry> Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string path = Path.Combine(directory, FileName);

        if (!File.Exists(path))
        {
            throw new CardFontManifestException(FileName);
        }

        Dictionary<string, CardFontEntry>? declared;

        try
        {
            declared = JsonSerializer.Deserialize<Dictionary<string, CardFontEntry>>(
                File.ReadAllBytes(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            throw new CardFontManifestException(FileName);
        }

        if (declared is null)
        {
            throw new CardFontManifestException(FileName);
        }

        Dictionary<CardFontRole, CardFontEntry> resolved = [];

        foreach (CardFontRole role in Enum.GetValues<CardFontRole>())
        {
            if (!declared.TryGetValue(role.ToString().ToLowerInvariant(), out CardFontEntry? entry))
            {
                throw new CardFontManifestException(role.ToString());
            }

            resolved[role] = entry;
        }

        return resolved;
    }

    internal static byte[] ReadVerified(string directory, CardFontEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string path = Path.Combine(directory, entry.RelativePath);

        if (!File.Exists(path))
        {
            throw new CardFontManifestException(entry.RelativePath);
        }

        byte[] content = File.ReadAllBytes(path);
        string digest = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        return string.Equals(digest, entry.Sha256, StringComparison.OrdinalIgnoreCase)
            ? content
            : throw new CardFontManifestException(entry.RelativePath);
    }
}

internal sealed class ManifestCardFontProvider : ICardFontProvider, IDisposable
{
    private readonly string directory;
    private readonly Dictionary<CardFontRole, SKTypeface> loaded = [];
    private readonly Lock gate = new();
    private IReadOnlyDictionary<CardFontRole, CardFontEntry>? manifest;

    public ManifestCardFontProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
    }

    public SKTypeface Resolve(CardFontRole role)
    {
        lock (gate)
        {
            if (loaded.TryGetValue(role, out SKTypeface? cached))
            {
                return cached;
            }

            manifest ??= CardFontManifest.Load(directory);

            byte[] content = CardFontManifest.ReadVerified(directory, manifest[role]);
            SKTypeface? typeface = SKTypeface.FromData(SKData.CreateCopy(content));

            if (typeface is null)
            {
                throw new CardFontManifestException(role.ToString());
            }

            loaded[role] = typeface;
            return typeface;
        }
    }

    public bool TryResolveFallback(out SKTypeface typeface)
    {
        try
        {
            typeface = Resolve(CardFontRole.Fallback);
            return true;
        }
        catch (CardFontManifestException)
        {
            typeface = null!;
            return false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (SKTypeface typeface in loaded.Values)
            {
                typeface.Dispose();
            }

            loaded.Clear();
        }
    }
}
