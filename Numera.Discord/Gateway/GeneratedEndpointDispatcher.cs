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
        CancellationToken cancellationToken);

    DiscordUserInput CreateUserInput(global::Discord.IUser user);

    DiscordMessageInput CreateMessageInput(global::Discord.IMessage message);

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

    public GeneratedEndpointDispatcher(
        IDiscordEndpointExecutor executor,
        IAuthorizationResolver authorization,
        ErrorRenderer errorRenderer)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(errorRenderer);

        this.executor = executor;
        this.authorization = authorization;
        this.errorRenderer = errorRenderer;
    }

    public async Task<DiscordEndpointContext> CreateContextAsync(
        SocketInteractionContext context,
        string commandPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandPath);

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
            EndpointAuthorization.ToContract(actor.Level));
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

    public Task<ResponsePlanFailure> DispatchAsync(
        SocketInteractionContext context,
        DiscordInteractionKind kind,
        DiscordEndpointResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        DiscordInteractionExchange exchange = new(kind, new SocketResponseSink(context.Interaction));

        if (response.Kind != DiscordResponseKind.Failure)
        {
            return executor.ExecuteAsync(exchange, response, cancellationToken);
        }

        RenderedError rendered = errorRenderer.Render(
            EndpointFailures.ToApplicationError(response.Failure!),
            OperationPublicId.From(context.Interaction.Id));

        return executor.ExecuteErrorAsync(exchange, rendered, cancellationToken);
    }
}
