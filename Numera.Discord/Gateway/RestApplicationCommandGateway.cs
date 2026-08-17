using System.Globalization;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Numera.Discord.Commands;

namespace Numera.Discord.Gateway;

internal static class CommandGatewayFailure
{
    internal const string CommandNotListed =
        "The command must come from the most recent listing of the same target.";

    internal const string CommandTypeUnsupported = "Only slash, user and message commands are synchronized.";
}

internal sealed class RestApplicationCommandGateway : IApplicationCommandGateway
{
    private readonly DiscordSocketClient client;
    private readonly Dictionary<string, RestApplicationCommand> listed = new(StringComparer.Ordinal);

    internal RestApplicationCommandGateway(DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    internal static CommandManifestType? MapType(ApplicationCommandType type) => type switch
    {
        ApplicationCommandType.Slash => CommandManifestType.Slash,
        ApplicationCommandType.User => CommandManifestType.User,
        ApplicationCommandType.Message => CommandManifestType.Message,
        _ => null,
    };

    internal static CommandManifestEntry Project(IApplicationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommandManifestType type = MapType(command.Type)
            ?? throw new InvalidOperationException(CommandGatewayFailure.CommandTypeUnsupported);

        return new CommandManifestEntry(type, command.Name, command.Description ?? string.Empty, ProjectOptions(command.Options));
    }

    internal static IReadOnlyList<CommandOptionManifest> ProjectOptions(
        IReadOnlyCollection<IApplicationCommandOption>? options)
    {
        if (options is null || options.Count == 0)
        {
            return CommandOptionManifest.None;
        }

        List<CommandOptionManifest> projected = new(options.Count);

        foreach (IApplicationCommandOption option in options)
        {
            projected.Add(new CommandOptionManifest(
                option.Name,
                option.Description ?? string.Empty,
                (int)option.Type,
                option.IsRequired ?? false,
                option.IsAutocomplete ?? false,
                ProjectChoices(option.Choices),
                ProjectOptions(option.Options)));
        }

        return projected;
    }

    internal static ApplicationCommandProperties Build(CommandManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        switch (entry.Type)
        {
            case CommandManifestType.User:
                return new UserCommandBuilder().WithName(entry.Name).Build();

            case CommandManifestType.Message:
                return new MessageCommandBuilder().WithName(entry.Name).Build();

            default:
                SlashCommandBuilder builder = new SlashCommandBuilder()
                    .WithName(entry.Name)
                    .WithDescription(entry.Description);

                foreach (CommandOptionManifest option in entry.Options)
                {
                    builder.AddOption(BuildOption(option));
                }

                return builder.Build();
        }
    }

    public async Task<IReadOnlyList<RegisteredCommand>> ListAsync(
        CommandSyncTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        IReadOnlyCollection<RestApplicationCommand> commands = target.Scope == CommandScope.Global
            ? await client.Rest
                .GetGlobalApplicationCommands(withLocalizations: false, locale: null, Options(cancellationToken))
                .ConfigureAwait(false)
            : await client.Rest
                .GetGuildApplicationCommands(
                    target.GuildId,
                    withLocalizations: false,
                    locale: null,
                    Options(cancellationToken))
                .ConfigureAwait(false);

        listed.Clear();

        List<RegisteredCommand> registered = new(commands.Count);

        foreach (RestApplicationCommand command in commands)
        {
            if (MapType(command.Type) is null)
            {
                continue;
            }

            string commandId = command.Id.ToString(CultureInfo.InvariantCulture);
            listed[commandId] = command;
            registered.Add(new RegisteredCommand(commandId, Project(command)));
        }

        return registered;
    }

    public async Task CreateAsync(
        CommandSyncTarget target,
        CommandManifestEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        ApplicationCommandProperties properties = Build(entry);

        if (target.Scope == CommandScope.Global)
        {
            await client.Rest.CreateGlobalCommand(properties, Options(cancellationToken)).ConfigureAwait(false);
            return;
        }

        await client.Rest
            .CreateGuildCommand(properties, target.GuildId, Options(cancellationToken))
            .ConfigureAwait(false);
    }

    public Task EditAsync(CommandSyncTarget target, CommandSyncEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        RestApplicationCommand command = Resolve(edit.CommandId);
        CommandManifestEntry desired = edit.Desired;

        return desired.Type switch
        {
            CommandManifestType.User => command.ModifyAsync<UserCommandProperties>(
                properties => properties.Name = desired.Name,
                Options(cancellationToken)),

            CommandManifestType.Message => command.ModifyAsync<MessageCommandProperties>(
                properties => properties.Name = desired.Name,
                Options(cancellationToken)),

            _ => command.ModifyAsync<SlashCommandProperties>(
                properties => ApplySlash(properties, desired),
                Options(cancellationToken)),
        };
    }

    public Task DeleteAsync(
        CommandSyncTarget target,
        RegisteredCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return Resolve(command.CommandId).DeleteAsync(Options(cancellationToken));
    }

    private static IReadOnlyList<CommandChoiceManifest> ProjectChoices(
        IReadOnlyCollection<IApplicationCommandOptionChoice>? choices)
    {
        if (choices is null || choices.Count == 0)
        {
            return CommandOptionManifest.NoChoices;
        }

        List<CommandChoiceManifest> projected = new(choices.Count);

        foreach (IApplicationCommandOptionChoice choice in choices)
        {
            projected.Add(new CommandChoiceManifest(
                choice.Name,
                Convert.ToString(choice.Value, CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return projected;
    }

    private static SlashCommandOptionBuilder BuildOption(CommandOptionManifest option)
    {
        SlashCommandOptionBuilder builder = new SlashCommandOptionBuilder()
            .WithName(option.Name)
            .WithDescription(option.Description)
            .WithType((ApplicationCommandOptionType)option.Type)
            .WithRequired(option.Required);

        if (option.Autocomplete)
        {
            builder.WithAutocomplete(true);
        }

        foreach (CommandChoiceManifest choice in option.Choices)
        {
            builder.AddChoice(choice.Name, choice.Value);
        }

        foreach (CommandOptionManifest nested in option.Options)
        {
            builder.AddOption(BuildOption(nested));
        }

        return builder;
    }

    private static void ApplySlash(SlashCommandProperties properties, CommandManifestEntry desired)
    {
        properties.Name = desired.Name;
        properties.Description = desired.Description;

        List<ApplicationCommandOptionProperties> options = new(desired.Options.Count);

        foreach (CommandOptionManifest option in desired.Options)
        {
            options.Add(BuildOption(option).Build());
        }

        properties.Options = options;
    }

    private static RequestOptions Options(CancellationToken cancellationToken) =>
        new() { CancelToken = cancellationToken };

    private RestApplicationCommand Resolve(string commandId) =>
        listed.TryGetValue(commandId, out RestApplicationCommand? command)
            ? command
            : throw new InvalidOperationException(CommandGatewayFailure.CommandNotListed);
}
