using Discord.Interactions;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

public interface IGeneratedEndpointDispatcher
{
    Task<DiscordEndpointContext> CreateContextAsync(
        SocketInteractionContext context,
        string commandPath,
        string sessionToken,
        CancellationToken cancellationToken);

    DiscordUserInput CreateUserInput(global::Discord.IUser user);

    DiscordMessageInput CreateMessageInput(global::Discord.IMessage message);

    DiscordAutocompleteRequest CreateAutocompleteRequest(
        SocketInteractionContext context,
        string commandPath);

    Task<ResponsePlanFailure> DispatchAutocompleteAsync(
        SocketInteractionContext context,
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken);

    Task<ResponsePlanFailure> DispatchAsync(
        SocketInteractionContext context,
        DiscordInteractionKind kind,
        DiscordEndpointResponse response,
        CancellationToken cancellationToken);
}

internal sealed class GeneratedEndpointDispatcher : IGeneratedEndpointDispatcher
{
    private readonly IDiscordEndpointExecutor executor;
    private readonly IAuthorizationResolver authorization;
    private readonly ErrorRenderer errorRenderer;
    private readonly IModalFormCatalog modalForms;

    public GeneratedEndpointDispatcher(
        IDiscordEndpointExecutor executor,
        IAuthorizationResolver authorization,
        ErrorRenderer errorRenderer,
        IModalFormCatalog modalForms)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(errorRenderer);
        ArgumentNullException.ThrowIfNull(modalForms);

        this.executor = executor;
        this.authorization = authorization;
        this.errorRenderer = errorRenderer;
        this.modalForms = modalForms;
    }

    public async Task<DiscordEndpointContext> CreateContextAsync(
        SocketInteractionContext context,
        string commandPath,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandPath);
        ArgumentNullException.ThrowIfNull(sessionToken);

        ulong userId = context.Interaction.User.Id;
        ulong guildId = context.Interaction.GuildId ?? 0UL;

        AuthorizationContext actor = await authorization
            .ResolveAsync(userId, guildId, context.Interaction.User as global::Discord.IGuildUser, cancellationToken)
            .ConfigureAwait(false);

        return new DiscordEndpointContext(
            context.Interaction.Id,
            userId,
            guildId,
            context.Interaction.ChannelId ?? 0UL,
            context.Interaction.UserLocale ?? string.Empty,
            commandPath,
            EndpointAuthorization.ToContract(actor.Level),
            sessionToken);
    }

    public DiscordUserInput CreateUserInput(global::Discord.IUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new DiscordUserInput(user.Id);
    }

    public DiscordMessageInput CreateMessageInput(global::Discord.IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new DiscordMessageInput(message.Id, message.Channel.Id, message.Author.Id);
    }

    public DiscordAutocompleteRequest CreateAutocompleteRequest(
        SocketInteractionContext context,
        string commandPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandPath);

        global::Discord.WebSocket.SocketAutocompleteInteraction interaction =
            (global::Discord.WebSocket.SocketAutocompleteInteraction)context.Interaction;

        return new DiscordAutocompleteRequest(
            interaction.User.Id,
            interaction.GuildId ?? 0UL,
            commandPath,
            interaction.Data.Current.Name ?? string.Empty,
            interaction.Data.Current.Value?.ToString() ?? string.Empty);
    }

    public Task<ResponsePlanFailure> DispatchAutocompleteAsync(
        SocketInteractionContext context,
        IReadOnlyList<DiscordAutocompleteOption> options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        DiscordInteractionExchange exchange = new(
            DiscordInteractionKind.Autocomplete, new SocketResponseSink(context.Interaction));

        return executor.ExecuteAutocompleteAsync(exchange, options, cancellationToken);
    }

    public Task<ResponsePlanFailure> DispatchAsync(
        SocketInteractionContext context,
        DiscordInteractionKind kind,
        DiscordEndpointResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        DiscordInteractionExchange exchange = new(kind, new SocketResponseSink(context.Interaction));

        if (response.Kind == DiscordResponseKind.Modal)
        {
            return executor.ExecuteModalAsync(exchange, response, ResolveFields(response), cancellationToken);
        }

        if (response.Kind != DiscordResponseKind.Failure)
        {
            return executor.ExecuteAsync(exchange, response, cancellationToken);
        }

        RenderedError rendered = errorRenderer.Render(
            EndpointFailures.ToApplicationError(response.Failure!),
            OperationPublicId.From(context.Interaction.Id));

        return executor.ExecuteErrorAsync(exchange, rendered, cancellationToken);
    }

    private IReadOnlyList<DiscordModalField> ResolveFields(DiscordEndpointResponse response)
    {
        response.ViewData.TryGetValue(ComposerViewData.CustomId, out string? customId);

        return
        [
            .. modalForms.Resolve(CustomIdRoute.Describe(customId)).Select(static definition =>
                new DiscordModalField(
                    definition.CustomId,
                    definition.Label,
                    null,
                    definition.Placeholder,
                    definition.MinimumLength,
                    definition.MaximumLength,
                    definition.Required,
                    definition.Style)),
        ];
    }
}
