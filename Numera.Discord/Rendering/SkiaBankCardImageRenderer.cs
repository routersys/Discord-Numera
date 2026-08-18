using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Banking;

namespace Numera.Discord.Rendering;

internal sealed class SkiaBankCardImageRenderer : IBankCardImageRenderer
{
    internal const string FileName = "bank-card.png";
    internal const int DefaultBackgroundRgb = 0x102A54;

    private readonly IBankCardRenderer renderer;
    private readonly ICardRenderDiagnostics diagnostics;

    public SkiaBankCardImageRenderer(IBankCardRenderer renderer, ICardRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.renderer = renderer;
        this.diagnostics = diagnostics;
    }

    public BankCardImage? TryRender(BankCardRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            byte[] content = renderer.Render(new BankCardRenderRequest(
                model.BankName,
                CapabilityLabel(model.Form),
                model.CustomerDisplayName,
                Identifier(model),
                Expiry(model.ExpiresAt),
                DefaultBackgroundRgb,
                BackgroundImage: null,
                model.DebitDisplayNumber is null ? CardFaceMode.Numberless : CardFaceMode.Numbered));

            return new BankCardImage(FileName, CardCanvas.Width, CardCanvas.Height, content);
        }
        catch (CardFontManifestException failure)
        {
            diagnostics.RendererUnavailable(failure.Message);
            return null;
        }
        catch (CardContrastException)
        {
            diagnostics.RendererUnavailable(nameof(CardContrastException));
            return null;
        }
    }

    internal static string CapabilityLabel(BankCardForm form) => form switch
    {
        BankCardForm.CashOnly => "CASH",
        BankCardForm.DebitOnly => "DEBIT",
        BankCardForm.IntegratedCashDebit => "CASH / DEBIT",
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    internal static string Identifier(BankCardRenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.DebitDisplayNumber is { Length: > 0 } number
            ? BankCardRenderer.GroupDigits(number.Replace("*", string.Empty, StringComparison.Ordinal))
            : model.DisplayIdentifier;
    }

    internal static string? Expiry(long? expiresAt) =>
        expiresAt is { } value
            ? DateTimeOffset.FromUnixTimeMilliseconds(value).ToString(
                "MM/yy", CultureInfo.InvariantCulture)
            : null;
}
