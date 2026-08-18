using Numera.Discord.Abstractions;

namespace Numera.Discord.Gateway;

internal sealed record DiscordEmbedPayload(string Title, string Description, string? Footer, uint Color);

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

    Task RespondAsync(DiscordEmbedPayload embed, bool ephemeral, CancellationToken cancellationToken);

    Task UpdateAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken);

    Task ModifyOriginalResponseAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken);

    Task RespondWithModalAsync(DiscordModalPayload modal, CancellationToken cancellationToken);

    Task RespondWithAutocompleteAsync(
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken);
}
