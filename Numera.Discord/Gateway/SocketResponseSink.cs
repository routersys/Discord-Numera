using Discord;
using Numera.Discord.Abstractions;

namespace Numera.Discord.Gateway;

internal static class SinkFailure
{
    internal const string ComponentInteractionRequired =
        "UpdateMessage is only valid for a component interaction.";

    internal const string AutocompleteInteractionRequired =
        "An autocomplete response is only valid for an autocomplete interaction.";
}

internal sealed class SocketResponseSink : IDiscordResponseSink
{
    internal const TextInputStyle CanonicalTextInputStyle = TextInputStyle.Short;

    private readonly IDiscordInteraction interaction;

    internal SocketResponseSink(IDiscordInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        this.interaction = interaction;
    }

    public Task DeferAsync(bool ephemeral, CancellationToken cancellationToken) =>
        interaction.DeferAsync(ephemeral, Options(cancellationToken));

    public Task RespondAsync(DiscordEmbedPayload embed, bool ephemeral, CancellationToken cancellationToken) =>
        interaction.RespondAsync(
            text: null,
            embeds: null,
            isTTS: false,
            ephemeral: ephemeral,
            allowedMentions: DiscordClientConfiguration.CanonicalAllowedMentions,
            components: null,
            embed: BuildEmbed(embed),
            options: Options(cancellationToken));

    public Task UpdateAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken) =>
        interaction is IComponentInteraction component
            ? component.UpdateAsync(properties => Apply(properties, embed), Options(cancellationToken))
            : throw new InvalidOperationException(SinkFailure.ComponentInteractionRequired);

    public Task ModifyOriginalResponseAsync(DiscordEmbedPayload embed, CancellationToken cancellationToken) =>
        interaction.ModifyOriginalResponseAsync(properties => Apply(properties, embed), Options(cancellationToken));

    public Task RespondWithModalAsync(DiscordModalPayload modal, CancellationToken cancellationToken) =>
        interaction.RespondWithModalAsync(BuildModal(modal), Options(cancellationToken));

    public Task RespondWithAutocompleteAsync(
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (interaction is not IAutocompleteInteraction autocomplete)
        {
            throw new InvalidOperationException(SinkFailure.AutocompleteInteractionRequired);
        }

        List<AutocompleteResult> results = new(options.Count);

        for (int index = 0; index < options.Count; index++)
        {
            results.Add(new AutocompleteResult(options[index].Name, options[index].Value));
        }

        return autocomplete.RespondAsync(results, Options(cancellationToken));
    }

    internal static Embed BuildEmbed(DiscordEmbedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle(payload.Title)
            .WithDescription(payload.Description)
            .WithColor(new Color(payload.Color));

        if (!string.IsNullOrEmpty(payload.Footer))
        {
            builder.WithFooter(payload.Footer);
        }

        return builder.Build();
    }

    internal static Modal BuildModal(DiscordModalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        ModalBuilder builder = new ModalBuilder()
            .WithTitle(payload.Title)
            .WithCustomId(payload.CustomId);

        foreach (DiscordModalField field in payload.Fields)
        {
            TextInputBuilder input = new(
                field.CustomId,
                CanonicalTextInputStyle,
                field.Placeholder,
                field.MinimumLength,
                field.MaximumLength,
                field.Required,
                value: null,
                id: null);

            builder.AddLabel(new LabelBuilder(field.Label, input, field.Description, id: null));
        }

        return builder.Build();
    }

    private static void Apply(MessageProperties properties, DiscordEmbedPayload payload)
    {
        properties.Embed = BuildEmbed(payload);
        properties.AllowedMentions = DiscordClientConfiguration.CanonicalAllowedMentions;
    }

    private static RequestOptions Options(CancellationToken cancellationToken) =>
        new() { CancelToken = cancellationToken };
}
