using SkiaSharp;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class BundledCardFontTests
{
    private sealed class SilentDiagnostics : ICardRenderDiagnostics
    {
        public List<int> Missing { get; } = [];

        public void MissingGlyph(int codePoint) => Missing.Add(codePoint);

        public void RendererUnavailable(string reason) => throw new InvalidOperationException(reason);
    }

    private static string Directory() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "fonts");

    [TestMethod]
    public void TheBundledManifestDeclaresEveryRole()
    {
        IReadOnlyDictionary<CardFontRole, CardFontEntry> manifest =
            CardFontManifest.Load(Directory());

        Assert.AreEqual(3, manifest.Count);
        Assert.AreEqual("BIZ UDPGothic", manifest[CardFontRole.General].Family);
        Assert.AreEqual("IBM Plex Mono", manifest[CardFontRole.Mono].Family);
        Assert.AreEqual("Noto Sans CJK JP", manifest[CardFontRole.Fallback].Family);
    }

    [TestMethod]
    public void EveryBundledFontMatchesItsDeclaredDigest()
    {
        IReadOnlyDictionary<CardFontRole, CardFontEntry> manifest =
            CardFontManifest.Load(Directory());

        foreach (CardFontEntry entry in manifest.Values)
        {
            Assert.IsTrue(CardFontManifest.ReadVerified(Directory(), entry).Length > 0);
        }
    }

    [TestMethod]
    public void EveryBundledFontDeclaresTheOpenFontLicence()
    {
        IReadOnlyDictionary<CardFontRole, CardFontEntry> manifest =
            CardFontManifest.Load(Directory());

        foreach (CardFontEntry entry in manifest.Values)
        {
            Assert.AreEqual("OFL-1.1", entry.LicenseSpdx);
        }
    }

    [TestMethod]
    public void TheDeclaredUpstreamReleasesAreThePinnedOnes()
    {
        IReadOnlyDictionary<CardFontRole, CardFontEntry> manifest =
            CardFontManifest.Load(Directory());

        Assert.AreEqual("v1.051", manifest[CardFontRole.General].UpstreamRelease);
        Assert.AreEqual("@ibm/plex-mono@2.5.0", manifest[CardFontRole.Mono].UpstreamRelease);
        Assert.AreEqual("Sans2.004", manifest[CardFontRole.Fallback].UpstreamRelease);
    }

    [TestMethod]
    public void TheProviderLoadsTheBundledTypefaces()
    {
        using ManifestCardFontProvider provider = new(Directory());

        Assert.IsTrue(provider.Resolve(CardFontRole.General).GlyphCount > 0);
        Assert.IsTrue(provider.Resolve(CardFontRole.Mono).GlyphCount > 0);
        Assert.IsTrue(provider.TryResolveFallback(out SKTypeface fallback));
        Assert.IsTrue(fallback.GlyphCount > 0);
    }

    [TestMethod]
    public void JapaneseAndDigitsRenderWithoutSubstitution()
    {
        using ManifestCardFontProvider provider = new(Directory());
        SilentDiagnostics diagnostics = new();
        BankCardRenderer renderer = new(provider, diagnostics);

        byte[] png = renderer.Render(new BankCardRenderRequest(
            "ヌメラ銀行",
            "CASH / DEBIT",
            "山田 太郎",
            "1234 5678 9012 3456",
            "12/30",
            0x102A54,
            BackgroundImage: null,
            CardFaceMode.Numbered));

        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.AreEqual(CardCanvas.Width, decoded.Width);
        Assert.AreEqual(CardCanvas.Height, decoded.Height);
        Assert.AreEqual(0, diagnostics.Missing.Count, "Bundled Font で置換が発生しました。");
    }

    [TestMethod]
    public void TheRenderedCardActuallyPaintsGlyphs()
    {
        using ManifestCardFontProvider provider = new(Directory());
        BankCardRenderer renderer = new(provider, new SilentDiagnostics());

        byte[] png = renderer.Render(new BankCardRenderRequest(
            "ヌメラ銀行",
            "CASH",
            "山田 太郎",
            "12345678",
            null,
            0x102A54,
            BackgroundImage: null,
            CardFaceMode.Numberless));

        using SKBitmap decoded = SKBitmap.Decode(png);

        int distinct = Distinct(decoded, 72, 64, 710, 80);

        Assert.IsTrue(distinct > 1, "Bank Name 領域が単色であり文字が描かれていません。");
    }

    private static int Distinct(SKBitmap bitmap, int x, int y, int width, int height)
    {
        HashSet<uint> seen = [];

        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                seen.Add((uint)bitmap.GetPixel(column, row));
            }
        }

        return seen.Count;
    }
}
