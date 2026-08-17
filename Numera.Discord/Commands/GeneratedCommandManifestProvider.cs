using Numera.Discord.Gateway;
using Numera.Discord.Routing;

namespace Numera.Discord.Commands;

internal static class GeneratedOptionType
{
    internal const int SubCommand = 1;
    internal const int SubCommandGroup = 2;
    internal const int String = 3;
    internal const int Integer = 4;
    internal const int Boolean = 5;

    internal static int Of(GeneratedOptionValueKind kind) => kind switch
    {
        GeneratedOptionValueKind.String => String,
        GeneratedOptionValueKind.Boolean => Boolean,
        GeneratedOptionValueKind.Integer => Integer,
        GeneratedOptionValueKind.Enum => String,
        _ => throw new InvalidOperationException(
            $"Option value kind '{kind}' has no Discord option type."),
    };
}

internal sealed class GeneratedCommandManifestProvider : ICommandManifestProvider
{
    internal const string ControlCommandName = "system";

    private readonly IReadOnlyList<CommandManifestEntry> primary;
    private readonly IReadOnlyList<CommandManifestEntry> control;

    public GeneratedCommandManifestProvider()
        : this(EconomyCommandManifest.Declarations)
    {
    }

    internal GeneratedCommandManifestProvider(IReadOnlyList<GeneratedCommandDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        List<CommandManifestEntry> entries = [.. Build(declarations)];

        primary = [.. entries.Where(static entry => !IsControl(entry))];
        control = [.. entries.Where(IsControl)];
    }

    public IReadOnlyList<CommandManifestEntry> PrimaryCommands() => primary;

    public IReadOnlyList<CommandManifestEntry> ControlCommands() => control;

    private static bool IsControl(CommandManifestEntry entry) =>
        entry.Type == CommandManifestType.Slash
        && string.Equals(entry.Name, ControlCommandName, StringComparison.Ordinal);

    private static IEnumerable<CommandManifestEntry> Build(
        IReadOnlyList<GeneratedCommandDeclaration> declarations)
    {
        foreach (IGrouping<string, GeneratedCommandDeclaration> root in declarations
            .GroupBy(RootName, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            foreach (CommandManifestEntry entry in BuildRoot(root.Key, [.. root]))
            {
                yield return entry;
            }
        }
    }

    private static string RootName(GeneratedCommandDeclaration declaration) =>
        declaration.GroupPath.Length == 0 ? declaration.Name : declaration.GroupPath[0].Name;

    private static IEnumerable<CommandManifestEntry> BuildRoot(
        string name,
        IReadOnlyList<GeneratedCommandDeclaration> declarations)
    {
        GeneratedCommandDeclaration[] ungrouped =
            [.. declarations.Where(static declaration => declaration.GroupPath.Length == 0)];

        foreach (GeneratedCommandDeclaration declaration in ungrouped)
        {
            yield return new CommandManifestEntry(
                Kind(declaration.Kind),
                declaration.Name,
                declaration.Description,
                Options(declaration));
        }

        GeneratedCommandDeclaration[] grouped =
            [.. declarations.Where(static declaration => declaration.GroupPath.Length > 0)];

        if (grouped.Length == 0)
        {
            yield break;
        }

        yield return new CommandManifestEntry(
            CommandManifestType.Slash,
            name,
            grouped[0].GroupPath[0].Description,
            Subcommands(grouped));
    }

    private static IReadOnlyList<CommandOptionManifest> Subcommands(
        IReadOnlyList<GeneratedCommandDeclaration> declarations)
    {
        List<CommandOptionManifest> options = [];

        foreach (GeneratedCommandDeclaration declaration in declarations
            .Where(static declaration => declaration.GroupPath.Length == 1)
            .OrderBy(static declaration => declaration.Name, StringComparer.Ordinal))
        {
            options.Add(new CommandOptionManifest(
                declaration.Name,
                declaration.Description,
                GeneratedOptionType.SubCommand,
                Required: false,
                Autocomplete: false,
                CommandOptionManifest.NoChoices,
                Options(declaration)));
        }

        foreach (IGrouping<string, GeneratedCommandDeclaration> group in declarations
            .Where(static declaration => declaration.GroupPath.Length > 1)
            .GroupBy(static declaration => declaration.GroupPath[1].Name, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            GeneratedCommandDeclaration[] members =
                [.. group.OrderBy(static declaration => declaration.Name, StringComparer.Ordinal)];

            options.Add(new CommandOptionManifest(
                group.Key,
                members[0].GroupPath[1].Description,
                GeneratedOptionType.SubCommandGroup,
                Required: false,
                Autocomplete: false,
                CommandOptionManifest.NoChoices,
                [.. members.Select(static member => new CommandOptionManifest(
                    member.Name,
                    member.Description,
                    GeneratedOptionType.SubCommand,
                    Required: false,
                    Autocomplete: false,
                    CommandOptionManifest.NoChoices,
                    Options(member)))]));
        }

        return options;
    }

    private static IReadOnlyList<CommandOptionManifest> Options(GeneratedCommandDeclaration declaration)
    {
        if (declaration.Options.Length == 0)
        {
            return CommandOptionManifest.None;
        }

        return
        [
            .. declaration.Options.Select(static option => new CommandOptionManifest(
                option.Name,
                option.Description,
                GeneratedOptionType.Of(option.ValueKind),
                option.Required,
                option.Autocomplete,
                option.Choices.Length == 0
                    ? CommandOptionManifest.NoChoices
                    : [.. option.Choices.Select(static choice =>
                        new CommandChoiceManifest(choice.Name, choice.Value))],
                CommandOptionManifest.None)),
        ];
    }

    private static CommandManifestType Kind(GeneratedCommandKind kind) => kind switch
    {
        GeneratedCommandKind.Slash => CommandManifestType.Slash,
        GeneratedCommandKind.User => CommandManifestType.User,
        GeneratedCommandKind.Message => CommandManifestType.Message,
        _ => throw new InvalidOperationException($"Command kind '{kind}' is not supported."),
    };
}
