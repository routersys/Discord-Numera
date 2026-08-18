using Discord.Interactions;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;

namespace Numera.Discord.Gateway;

public interface IGeneratedEndpointDispatcher
{
    DiscordEndpointContext CreateContext(SocketInteractionContext context, string commandPath);

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

    public GeneratedEndpointDispatcher(IDiscordEndpointExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        this.executor = executor;
    }

    public DiscordEndpointContext CreateContext(SocketInteractionContext context, string commandPath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandPath);

        return new DiscordEndpointContext(
            context.Interaction.Id,
            context.Interaction.User.Id,
            context.Interaction.GuildId ?? 0UL,
            context.Interaction.ChannelId ?? 0UL,
            context.Interaction.UserLocale ?? string.Empty,
            commandPath);
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

        return executor.ExecuteAsync(exchange, response, cancellationToken);
    }
}
