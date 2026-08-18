using Numera.Application.Banking;
using Numera.Domain.Banking;

namespace Numera.Discord.Rendering;

public interface IBankCardImageService
{
    byte[]? TryRender(BankCardRenderView view, string customerDisplayName);
}

internal sealed class BankCardImageService : IBankCardImageService
{
    internal const int DefaultBackgroundRgb = 0x102A54;

    private readonly IBankCardRenderer renderer;
    private readonly ICardRenderDiagnostics diagnostics;

    public BankCardImageService(IBankCardRenderer renderer, ICardRenderDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.renderer = renderer;
        this.diagnostics = diagnostics;
    }

    public byte[]? TryRender(BankCardRenderView view, string customerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(view);

        try
        {
            return renderer.Render(new BankCardRenderRequest(
                view.BankName,
                CapabilityLabel(view.Form),
                customerDisplayName,
                Identifier(view),
                Expiry(view.ExpiresAt),
                DefaultBackgroundRgb,
                BackgroundImage: null,
                view.DebitDisplayNumber is null ? CardFaceMode.Numberless : CardFaceMode.Numbered));
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

    internal static string Identifier(BankCardRenderView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view.DebitDisplayNumber is { Length: > 0 } number
            ? BankCardRenderer.GroupDigits(number.Replace("*", string.Empty, StringComparison.Ordinal))
            : view.DisplayIdentifier;
    }

    internal static string? Expiry(long? expiresAt) =>
        expiresAt is { } value
            ? DateTimeOffset.FromUnixTimeMilliseconds(value).ToString(
                "MM/yy", System.Globalization.CultureInfo.InvariantCulture)
            : null;
}
