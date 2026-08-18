using SkiaSharp;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class BankCardRendererTests
{
    private sealed class StubFontProvider : ICardFontProvider
    {
        public SKTypeface Resolve(CardFontRole role) => SKTypeface.Default;

        public bool TryResolveFallback(out SKTypeface typeface)
        {
            typeface = SKTypeface.Default;
            return true;
        }
    }

    private sealed class GlyphlessFontProvider : ICardFontProvider
    {
        public SKTypeface Resolve(CardFontRole role) => SKTypeface.Empty;

        public bool TryResolveFallback(out SKTypeface typeface)
        {
            typeface = null!;
            return false;
        }
    }

    private sealed class RecordingDiagnostics : ICardRenderDiagnostics
    {
        public List<int> CodePoints { get; } = [];

        public List<string> Unavailable { get; } = [];

        public void MissingGlyph(int codePoint) => CodePoints.Add(codePoint);

        public void RendererUnavailable(string reason) => Unavailable.Add(reason);
    }

    private static BankCardRenderer Renderer(ICardRenderDiagnostics? diagnostics = null) =>
        new(new StubFontProvider(), diagnostics ?? new RecordingDiagnostics());

    private static BankCardRenderRequest Request(
        int backgroundRgb = 0x102A54,
        CardFaceMode faceMode = CardFaceMode.Numbered,
        string bankName = "ヌメラ銀行") =>
        new(
            bankName,
            "CASH / DEBIT",
            "山田 太郎",
            "1234 5678 9012 3456",
            "12/30",
            backgroundRgb,
            BackgroundImage: null,
            faceMode);

    [TestMethod]
    public void TheRenderedCardIsACanonicalSizedPng()
    {
        byte[] png = Renderer().Render(Request());

        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());

        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.AreEqual(CardCanvas.Width, decoded.Width);
        Assert.AreEqual(CardCanvas.Height, decoded.Height);
    }

    [TestMethod]
    public void TheCornersAreTransparent()
    {
        byte[] png = Renderer().Render(Request());
        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.AreEqual(0, decoded.GetPixel(0, 0).Alpha);
        Assert.AreEqual(0, decoded.GetPixel(decoded.Width - 1, 0).Alpha);
        Assert.AreEqual(0, decoded.GetPixel(0, decoded.Height - 1).Alpha);
        Assert.AreEqual(0, decoded.GetPixel(decoded.Width - 1, decoded.Height - 1).Alpha);
    }

    [TestMethod]
    public void TheCentreIsOpaqueBackground()
    {
        byte[] png = Renderer().Render(Request(0x102A54));
        using SKBitmap decoded = SKBitmap.Decode(png);

        SKColor centre = decoded.GetPixel(CardCanvas.Width / 2, 200);

        Assert.AreEqual(255, centre.Alpha);
    }

    [TestMethod]
    public void ANumberlessFaceOmitsTheCardIdentifier()
    {
        IReadOnlyList<CardTextElement> numbered =
            BankCardRenderer.Layout(Request(faceMode: CardFaceMode.Numbered));
        IReadOnlyList<CardTextElement> numberless =
            BankCardRenderer.Layout(Request(faceMode: CardFaceMode.Numberless));

        Assert.IsTrue(numbered.Any(static element => element.Y == 274));
        Assert.IsFalse(numberless.Any(static element => element.Y == 274));
    }

    [TestMethod]
    public void TheDefaultLayoutMatchesTheCanonicalTable()
    {
        IReadOnlyList<CardTextElement> elements = BankCardRenderer.Layout(Request());

        CardTextElement bankName = elements[0];
        Assert.AreEqual(72, bankName.X);
        Assert.AreEqual(64, bankName.Y);
        Assert.AreEqual(710, bankName.Width);
        Assert.AreEqual(80, bankName.Height);
        Assert.AreEqual(42, bankName.FontSizePx);
        Assert.AreEqual(30, bankName.MinimumFontSizePx);

        CardTextElement capability = elements[1];
        Assert.AreEqual(760, capability.X);
        Assert.AreEqual(194, capability.Width);
        Assert.AreEqual(CardTextAlignment.Right, capability.Alignment);
        Assert.AreEqual(CardCanvas.MinimumFontSize, capability.MinimumFontSizePx);

        CardTextElement identifier = elements[2];
        Assert.AreEqual(882, identifier.Width);
        Assert.AreEqual(46, identifier.FontSizePx);
        Assert.AreEqual(36, identifier.MinimumFontSizePx);
        Assert.AreEqual(CardFontRole.Mono, identifier.Role);
    }

    [TestMethod]
    public void DigitsAreGroupedInFours()
    {
        Assert.AreEqual("1234 5678 9012 3456", BankCardRenderer.GroupDigits("1234567890123456"));
        Assert.AreEqual("1234 56", BankCardRenderer.GroupDigits("123456"));
        Assert.AreEqual("12", BankCardRenderer.GroupDigits("12"));
    }

    [TestMethod]
    public void ADarkBackgroundRendersLightText()
    {
        byte[] png = Renderer().Render(Request(0x000000));
        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.IsTrue(BrightestIn(decoded, 72, 64, 710, 80) > 200);
    }

    [TestMethod]
    public void ALightBackgroundRendersDarkText()
    {
        byte[] png = Renderer().Render(Request(0xFFFFFF));
        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.IsTrue(DarkestIn(decoded, 72, 64, 710, 80) < 60);
    }

    [TestMethod]
    public void AMissingGlyphIsRecordedOncePerCodePoint()
    {
        RecordingDiagnostics diagnostics = new();
        BankCardRenderer renderer = new(new GlyphlessFontProvider(), diagnostics);

        renderer.Render(new BankCardRenderRequest(
            "AAB", "CASH", "AAB", "1234", null, 0xFFFFFF, null, CardFaceMode.Numberless));

        CollectionAssert.AreEquivalent(
            new[] { 'A', 'B', 'C', 'S', 'H' }.Select(static value => (int)value).ToArray(),
            diagnostics.CodePoints.Distinct().Order().ToArray());
        Assert.AreEqual(
            diagnostics.CodePoints.Distinct().Count(),
            diagnostics.CodePoints.Count,
            "同じ Code Point を複数回記録しています。");
    }

    private static int BrightestIn(SKBitmap bitmap, int x, int y, int width, int height)
    {
        int best = 0;

        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                SKColor pixel = bitmap.GetPixel(column, row);
                best = Math.Max(best, Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)));
            }
        }

        return best;
    }

    private static int DarkestIn(SKBitmap bitmap, int x, int y, int width, int height)
    {
        int best = 255;

        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                SKColor pixel = bitmap.GetPixel(column, row);
                best = Math.Min(best, Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)));
            }
        }

        return best;
    }
}

[TestClass]
public sealed class CardFontManifestTests
{
    [TestMethod]
    public void AMissingManifestFailsClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "numera-fonts", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        try
        {
            Assert.ThrowsExactly<CardFontManifestException>(
                () => CardFontManifest.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AManifestMissingARoleFailsClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "numera-fonts", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, CardFontManifest.FileName),
                """
                {
                  "general": {
                    "family": "BIZ UDPGothic",
                    "style": "Bold",
                    "weight": "700",
                    "relativePath": "BIZUDPGothic-Bold.ttf",
                    "sha256": "00",
                    "licenseSpdx": "OFL-1.1",
                    "upstreamRelease": "v1.051"
                  }
                }
                """);

            Assert.ThrowsExactly<CardFontManifestException>(
                () => CardFontManifest.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AHashMismatchFailsClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "numera-fonts", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "font.ttf"), [1, 2, 3]);

            CardFontEntry entry = new(
                "BIZ UDPGothic", "Bold", "700", "font.ttf", new string('0', 64), "OFL-1.1", "v1.051");

            Assert.ThrowsExactly<CardFontManifestException>(
                () => CardFontManifest.ReadVerified(directory, entry));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
