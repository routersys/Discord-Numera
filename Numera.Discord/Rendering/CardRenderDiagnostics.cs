using System.Globalization;
using Numera.Discord.Abstractions;

namespace Numera.Discord.Rendering;

internal sealed class CardRenderDiagnostics : ICardRenderDiagnostics
{
    internal const string GlyphMissingPrefix = "CARD_GLYPH_MISSING_U+";
    internal const string RendererUnavailablePrefix = "CARD_RENDERER_UNAVAILABLE_";

    private readonly IDiscordDiagnostics diagnostics;

    public CardRenderDiagnostics(IDiscordDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.diagnostics = diagnostics;
    }

    public void MissingGlyph(int codePoint) =>
        diagnostics.InteractionFailed(
            GlyphMissingPrefix + codePoint.ToString("X4", CultureInfo.InvariantCulture));

    public void RendererUnavailable(string reason) =>
        diagnostics.InteractionFailed(RendererUnavailablePrefix + Sanitise(reason));

    internal static string Sanitise(string reason) =>
        string.Concat(reason.Where(static value => char.IsAsciiLetterOrDigit(value) || value == '_'))
            .ToUpperInvariant();
}
