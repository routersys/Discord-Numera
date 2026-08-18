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

    public Task RespondAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        bool ephemeral,
        CancellationToken cancellationToken) =>
        interaction.RespondAsync(
            text: null,
            embeds: null,
            isTTS: false,
            ephemeral: ephemeral,
            allowedMentions: DiscordClientConfiguration.CanonicalAllowedMentions,
            components: BuildComponents(components),
            embed: BuildEmbed(embed),
            options: Options(cancellationToken));

    public Task UpdateAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        CancellationToken cancellationToken) =>
        interaction is IComponentInteraction component
            ? component.UpdateAsync(
                properties => Apply(properties, embed, components), Options(cancellationToken))
            : throw new InvalidOperationException(SinkFailure.ComponentInteractionRequired);

    public Task ModifyOriginalResponseAsync(
        DiscordEmbedPayload embed,
        DiscordComponentPayload components,
        CancellationToken cancellationToken) =>
        interaction.ModifyOriginalResponseAsync(
            properties => Apply(properties, embed, components), Options(cancellationToken));

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
                Resolve(field.Style),
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

    private static TextInputStyle Resolve(Numera.Discord.Abstractions.EconomyModalFieldStyle style) =>
        style == Numera.Discord.Abstractions.EconomyModalFieldStyle.Paragraph
            ? TextInputStyle.Paragraph
            : CanonicalTextInputStyle;

    internal static MessageComponent? BuildComponents(DiscordComponentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.IsEmpty)
        {
            return null;
        }

        ComponentBuilder builder = new();

        if (payload.Select is { } select)
        {
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId(select.CustomId)
                .WithPlaceholder(select.Placeholder)
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (DiscordSelectOptionPayload option in select.Options)
            {
                menu.AddOption(new SelectMenuOptionBuilder()
                    .WithLabel(option.Label)
                    .WithValue(option.Value));
            }

            builder.WithSelectMenu(menu, row: 0);
        }

        int buttonRow = payload.Select is null ? 0 : 1;

        foreach (DiscordButtonPayload button in payload.Buttons)
        {
            builder.WithButton(
                new ButtonBuilder()
                    .WithCustomId(button.CustomId)
                    .WithLabel(button.Label)
                    .WithStyle(Resolve(button.Style))
                    .WithDisabled(button.Disabled),
                buttonRow);
        }

        return builder.Build();
    }

    private static ButtonStyle Resolve(DiscordButtonStyle style) => style switch
    {
        DiscordButtonStyle.Primary => ButtonStyle.Primary,
        DiscordButtonStyle.Danger => ButtonStyle.Danger,
        _ => ButtonStyle.Secondary,
    };

    private static void Apply(
        MessageProperties properties,
        DiscordEmbedPayload payload,
        DiscordComponentPayload components)
    {
        properties.Embed = BuildEmbed(payload);
        properties.Components = BuildComponents(components);
        properties.AllowedMentions = DiscordClientConfiguration.CanonicalAllowedMentions;
    }

    private static RequestOptions Options(CancellationToken cancellationToken) =>
        new() { CancelToken = cancellationToken };
}
