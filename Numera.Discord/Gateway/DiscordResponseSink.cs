using Numera.Discord.Abstractions;

namespace Numera.Discord.Gateway;

internal sealed record DiscordEmbedFieldPayload(string Name, string Value);

internal sealed record DiscordEmbedPayload(
    string Title,
    string Description,
    string? Footer,
    uint Color,
    IReadOnlyList<DiscordEmbedFieldPayload>? Fields = null);

internal sealed record DiscordButtonPayload(
    string CustomId,
    string Label,
    DiscordButtonStyle Style,
    bool Disabled);

internal sealed record DiscordSelectOptionPayload(string Label, string Value);

internal sealed record DiscordSelectPayload(
    string CustomId,
    string Placeholder,
    IReadOnlyList<DiscordSelectOptionPayload> Options);

internal sealed record DiscordComponentPayload(
    DiscordSelectPayload? Select,
    IReadOnlyList<DiscordButtonPayload> Buttons)
{
    internal static readonly DiscordComponentPayload None = new(null, []);

    internal bool IsEmpty => Select is null && Buttons.Count == 0;
}

internal sealed record DiscordModalField(
    string CustomId,
    string Label,
    string? Description,
    string? Placeholder,
    int MinimumLength,
    int MaximumLength,
    bool Required,
    EconomyModalFieldStyle Style = EconomyModalFieldStyle.Short);

internal sealed record DiscordModalPayload(
    string CustomId,
    string Title,
    IReadOnlyList<DiscordModalField> Fields);

internal interface IDiscordResponseSink
{
    Task DeferAsync(bool ephemeral, CancellationToken cancellationToken);

    Task RespondAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        bool ephemeral,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        CancellationToken cancellationToken);

    Task ModifyOriginalResponseAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        CancellationToken cancellationToken);

    Task RespondWithModalAsync(DiscordModalPayload modal, CancellationToken cancellationToken);

    Task RespondWithAutocompleteAsync(
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken);
}
