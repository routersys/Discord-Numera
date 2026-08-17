using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

internal sealed class DiscordInteractionRouter
{
    private readonly InteractionService interactionService;
    private readonly IServiceProvider services;
    private readonly IDiscordEndpointExecutor executor;
    private readonly ErrorRenderer errorRenderer;
    private readonly IDiscordDiagnostics diagnostics;

    public DiscordInteractionRouter(
        InteractionService interactionService,
        IServiceProvider services,
        IDiscordEndpointExecutor executor,
        ErrorRenderer errorRenderer,
        IDiscordDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(interactionService);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(errorRenderer);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.interactionService = interactionService;
        this.services = services;
        this.executor = executor;
        this.errorRenderer = errorRenderer;
        this.diagnostics = diagnostics;
    }

    internal static DiscordInteractionKind? Classify(IDiscordInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return interaction switch
        {
            ISlashCommandInteraction => DiscordInteractionKind.SlashCommand,
            IUserCommandInteraction => DiscordInteractionKind.UserCommand,
            IMessageCommandInteraction => DiscordInteractionKind.MessageCommand,
            IModalInteraction => DiscordInteractionKind.ModalSubmit,
            IAutocompleteInteraction => DiscordInteractionKind.Autocomplete,
            IComponentInteraction component => ClassifyComponent(component),
            _ => null,
        };
    }

    internal static string DescribePath(IDiscordInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return interaction switch
        {
            ISlashCommandInteraction slash => slash.Data.Name,
            IUserCommandInteraction user => user.Data.Name,
            IMessageCommandInteraction message => message.Data.Name,
            IAutocompleteInteraction autocomplete => autocomplete.Data.CommandName,
            IComponentInteraction component => CustomIdRoute.Describe(component.Data.CustomId),
            IModalInteraction modal => CustomIdRoute.Describe(modal.Data.CustomId),
            _ => CustomIdRoute.Unknown,
        };
    }

    internal async Task RouteAsync(DiscordSocketClient client, SocketInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(interaction);

        DiscordInteractionKind? kind = Classify(interaction);

        if (kind is null)
        {
            diagnostics.InteractionFailed(BankingErrorCodes.InteractionKindUnsupported);
            return;
        }

        using IDisposable scope = diagnostics.BeginInteractionScope(new DiscordInteractionCorrelation(
            CorrelationId.Create(),
            interaction.Id,
            interaction.GuildId ?? 0UL,
            interaction.User.Id));

        diagnostics.InteractionReceived(DescribePath(interaction));

        try
        {
            SocketInteractionContext context = new(client, interaction);
            IResult result = await interactionService.ExecuteCommandAsync(context, services).ConfigureAwait(false);

            if (IsUnknownRoute(result))
            {
                await FailAsync(interaction, kind.Value, BankingErrorCodes.InteractionRouteUnknown)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            diagnostics.InteractionFaulted(exception);
        }
    }

    internal async Task HandleExecutedAsync(IDiscordInteraction interaction, IResult result)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess || IsUnknownRoute(result))
        {
            return;
        }

        DiscordInteractionKind? kind = Classify(interaction);

        if (kind is null)
        {
            diagnostics.InteractionFailed(BankingErrorCodes.InteractionKindUnsupported);
            return;
        }

        await FailAsync(interaction, kind.Value, BankingErrorCodes.InteractionExecutionFailed).ConfigureAwait(false);
    }

    internal async Task FailAsync(IDiscordInteraction interaction, DiscordInteractionKind kind, string errorCode)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(errorCode);

        diagnostics.InteractionFailed(errorCode);

        if (interaction.HasResponded || kind == DiscordInteractionKind.Autocomplete)
        {
            return;
        }

        ApplicationError error = ApplicationError.Create(ErrorCategory.Unexpected, errorCode);
        DiscordInteractionExchange exchange = new(kind, new SocketResponseSink(interaction));

        await executor
            .ExecuteErrorAsync(
                exchange,
                errorRenderer.Render(error, OperationPublicId.From(interaction.Id)),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    internal static bool IsUnknownRoute(IResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return !result.IsSuccess && result.Error == InteractionCommandError.UnknownCommand;
    }

    private static DiscordInteractionKind ClassifyComponent(IComponentInteraction component) =>
        component.Data.Type == ComponentType.Button
            ? DiscordInteractionKind.Button
            : DiscordInteractionKind.SelectMenu;
}

internal static class CustomIdRoute
{
    internal const string Unknown = "unknown";
    internal const char Separator = ':';
    internal const int ActionSegment = 3;

    internal static string Describe(string? customId)
    {
        if (string.IsNullOrEmpty(customId))
        {
            return Unknown;
        }

        int start = 0;
        int segment = 0;

        for (int index = 0; index <= customId.Length; index++)
        {
            if (index != customId.Length && customId[index] != Separator)
            {
                continue;
            }

            if (segment == ActionSegment)
            {
                return customId[start..index];
            }

            segment++;
            start = index + 1;
        }

        return Unknown;
    }
}

internal static class CorrelationId
{
    internal static string Create() => Guid.CreateVersion7().ToString("N");
}

internal static class OperationPublicId
{
    internal const int Length = 12;

    internal static string From(ulong interactionId)
    {
        string text = interactionId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return text.Length <= Length ? text : text[^Length..];
    }
}
