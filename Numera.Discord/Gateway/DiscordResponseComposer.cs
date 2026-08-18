using Numera.Discord.Abstractions;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

internal static class PresentationColors
{
    internal const uint Information = 0x5865F2;
    internal const uint Success = 0x57F287;
    internal const uint Warning = 0xFEE75C;
    internal const uint Error = 0xED4245;
    internal const uint Neutral = 0x99AAB5;
}

internal static class ComposerViewData
{
    internal const string OperationPublicId = "operationPublicId";
    internal const string CustomId = "customId";
    internal const string TitleSuffix = ".title";
    internal const string DescriptionSuffix = ".description";
}

internal static class ComposerFailure
{
    internal const string ModalCustomIdMissing =
        "A modal response must carry the customId view data entry.";
}

internal interface IDiscordResponseComposer
{
    DiscordEmbedPayload Compose(DiscordEndpointResponse response);

    DiscordEmbedPayload Compose(RenderedError error);

    DiscordComponentPayload ComposeComponents(DiscordEndpointResponse response);

    string ResolveModalCustomId(DiscordEndpointResponse response);
}

internal sealed class CatalogResponseComposer : IDiscordResponseComposer
{
    private readonly ITextCatalog catalog;

    public CatalogResponseComposer(ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    public DiscordEmbedPayload Compose(DiscordEndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new DiscordEmbedPayload(
            catalog.Format(response.ViewKey + ComposerViewData.TitleSuffix, response.ViewData),
            catalog.Format(response.ViewKey + ComposerViewData.DescriptionSuffix, response.ViewData),
            ComposeFooter(response),
            PresentationColors.Information,
            [
                .. response.Body.Fields.Select(field => new DiscordEmbedFieldPayload(
                    catalog.Format(field.LabelKey, response.ViewData),
                    catalog.Format(field.ValueKey, response.ViewData))),
            ],
            response.Body.Attachment?.Reference);
    }

    public DiscordEmbedPayload Compose(RenderedError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new DiscordEmbedPayload(error.Title, error.Description, error.Footer, error.Color);
    }

    public DiscordComponentPayload ComposeComponents(DiscordEndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Body.Components.IsEmpty)
        {
            return DiscordComponentPayload.None;
        }

        DiscordSelectPayload? select = response.Body.Components.Select is { } declared
            ? new DiscordSelectPayload(
                declared.CustomId,
                catalog.Format(declared.PlaceholderKey, response.ViewData),
                [
                    .. declared.Options.Select(static option =>
                        new DiscordSelectOptionPayload(option.Label, option.Value)),
                ])
            : null;

        return new DiscordComponentPayload(
            select,
            [
                .. response.Body.Components.Buttons.Select(button => new DiscordButtonPayload(
                    button.CustomId,
                    catalog.Format(button.LabelKey, response.ViewData),
                    button.Style,
                    button.Disabled)),
            ]);
    }

    public string ResolveModalCustomId(DiscordEndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.ViewData.TryGetValue(ComposerViewData.CustomId, out string? customId)
            && !string.IsNullOrEmpty(customId)
            ? customId
            : throw new ArgumentException(ComposerFailure.ModalCustomIdMissing, nameof(response));
    }

    private string? ComposeFooter(DiscordEndpointResponse response) =>
        response.ViewData.ContainsKey(ComposerViewData.OperationPublicId)
            ? catalog.Format(TextCatalogKeys.OperationFooter, response.ViewData)
            : null;
}
