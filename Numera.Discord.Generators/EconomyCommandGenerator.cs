using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Numera.Discord.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class EconomyCommandGenerator : IIncrementalGenerator
{
    private const string Namespace = "Numera.Discord.Abstractions.";
    private const string SlashAttribute = Namespace + "EconomySlashCommandAttribute";
    private const string UserAttribute = Namespace + "EconomyUserCommandAttribute";
    private const string MessageAttribute = Namespace + "EconomyMessageCommandAttribute";
    private const string ComponentAttribute = Namespace + "EconomyComponentAttribute";
    private const string ModalAttribute = Namespace + "EconomyModalAttribute";
    private const string AutocompleteProviderAttribute = Namespace + "EconomyAutocompleteProviderAttribute";
    private const string ModalFormAttribute = Namespace + "EconomyModalFormAttribute";
    private const string ModalFieldAttribute = Namespace + "EconomyModalFieldAttribute";
    private const string GroupAttribute = Namespace + "EconomyCommandGroupAttribute";
    private const string OptionAttribute = Namespace + "EconomyOptionAttribute";
    private const string ChoiceAttribute = Namespace + "EconomyChoiceAttribute";
    private const string AutocompleteAttribute = Namespace + "EconomyAutocompleteAttribute";
    private const string CancellationTokenType = "System.Threading.CancellationToken";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<CommandDescriptor> slash =
            Commands(context, SlashAttribute, CommandKind.Slash);
        IncrementalValuesProvider<CommandDescriptor> user =
            Commands(context, UserAttribute, CommandKind.User);
        IncrementalValuesProvider<CommandDescriptor> message =
            Commands(context, MessageAttribute, CommandKind.Message);

        IncrementalValuesProvider<HandlerDescriptor> components = Handlers(context, ComponentAttribute, 1, -1);
        IncrementalValuesProvider<HandlerDescriptor> modals = Handlers(context, ModalAttribute, 0, 1);
        IncrementalValuesProvider<HandlerDescriptor> providers =
            Handlers(context, AutocompleteProviderAttribute, 0, -1);
        IncrementalValuesProvider<ModalFormDescriptor> forms = ModalForms(context);

        IncrementalValueProvider<CommandSurface> surface = slash.Collect()
            .Combine(user.Collect())
            .Combine(message.Collect())
            .Combine(components.Collect())
            .Combine(modals.Collect())
            .Combine(providers.Collect())
            .Combine(forms.Collect())
            .Select(static (tuple, _) =>
            {
                ImmutableArray<CommandDescriptor> commands = tuple.Left.Left.Left.Left.Left.Left
                    .AddRange(tuple.Left.Left.Left.Left.Left.Right)
                    .AddRange(tuple.Left.Left.Left.Left.Right)
                    .Sort(static (left, right) =>
                        string.CompareOrdinal(left.DuplicateKey, right.DuplicateKey));

                return new CommandSurface(
                    commands,
                    Sort(tuple.Left.Left.Left.Right),
                    Sort(tuple.Left.Left.Right),
                    Sort(tuple.Left.Right),
                    tuple.Right.Sort(static (left, right) =>
                        string.CompareOrdinal(left.TypeDisplayName, right.TypeDisplayName)));
            });

        context.RegisterSourceOutput(surface, static (production, value) =>
        {
            CommandSurfaceValidator.Validate(production, value);
            production.AddSource("EconomyCommandManifest.g.cs", ManifestWriter.Write(value));
        });
    }

    private static ImmutableArray<HandlerDescriptor> Sort(ImmutableArray<HandlerDescriptor> handlers) =>
        handlers.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

    private static IncrementalValuesProvider<CommandDescriptor> Commands(
        IncrementalGeneratorInitializationContext context,
        string attributeName,
        CommandKind kind) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeName,
            static (node, _) => true,
            (syntaxContext, _) => BuildCommand(syntaxContext, kind));

    private static IncrementalValuesProvider<HandlerDescriptor> Handlers(
        IncrementalGeneratorInitializationContext context,
        string attributeName,
        int keyArgumentIndex,
        int inputTypeArgumentIndex) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeName,
            static (node, _) => true,
            (syntaxContext, _) => BuildHandler(syntaxContext, keyArgumentIndex, inputTypeArgumentIndex));

    private static IncrementalValuesProvider<ModalFormDescriptor> ModalForms(
        IncrementalGeneratorInitializationContext context) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            ModalFormAttribute,
            static (node, _) => true,
            static (syntaxContext, _) => BuildModalForm(syntaxContext));

    private static CommandDescriptor BuildCommand(GeneratorAttributeSyntaxContext context, CommandKind kind)
    {
        IMethodSymbol method = (IMethodSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        string name = ReadString(attribute, 0) ?? string.Empty;
        string? description = kind == CommandKind.Slash ? ReadString(attribute, 1) : ReadString(attribute, 1);

        return new CommandDescriptor(
            kind,
            name,
            description,
            ReadGroupPath(method.ContainingType),
            ReadOptions(method),
            method.ToDisplayString(),
            method.ReturnType.ToDisplayString(),
            EndsWithCancellationToken(method),
            ReadParameterTypes(method),
            LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None));
    }

    private static HandlerDescriptor BuildHandler(
        GeneratorAttributeSyntaxContext context,
        int keyArgumentIndex,
        int inputTypeArgumentIndex)
    {
        IMethodSymbol method = (IMethodSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        return new HandlerDescriptor(
            ReadString(attribute, keyArgumentIndex) ?? string.Empty,
            method.ToDisplayString(),
            method.ReturnType.ToDisplayString(),
            EndsWithCancellationToken(method),
            ReadParameterTypes(method),
            inputTypeArgumentIndex < 0 ? null : ReadTypeDisplayName(attribute, inputTypeArgumentIndex),
            LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None));
    }

    private static ModalFormDescriptor BuildModalForm(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        ImmutableArray<ModalFieldDescriptor>.Builder fields = ImmutableArray.CreateBuilder<ModalFieldDescriptor>();

        foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? fieldAttribute = property.GetAttributes().FirstOrDefault(
                static data => data.AttributeClass?.ToDisplayString() == ModalFieldAttribute);

            if (fieldAttribute is null)
            {
                continue;
            }

            fields.Add(new ModalFieldDescriptor(
                ReadString(fieldAttribute, 0) ?? string.Empty,
                ReadString(fieldAttribute, 1) ?? string.Empty,
                ReadString(fieldAttribute, 6) ?? string.Empty));
        }

        return new ModalFormDescriptor(
            ReadString(attribute, 0) ?? string.Empty,
            fields.ToImmutable(),
            type.ToDisplayString(),
            LocationInfo.From(type.Locations.FirstOrDefault() ?? Location.None));
    }

    private static ImmutableArray<CommandGroupDescriptor> ReadGroupPath(INamedTypeSymbol? containingType)
    {
        List<CommandGroupDescriptor> path = [];

        for (INamedTypeSymbol? current = containingType; current is not null; current = current.ContainingType)
        {
            AttributeData? group = current.GetAttributes().FirstOrDefault(
                static data => data.AttributeClass?.ToDisplayString() == GroupAttribute);

            if (group is not null)
            {
                path.Insert(0, new CommandGroupDescriptor(
                    ReadString(group, 0) ?? string.Empty,
                    ReadString(group, 1) ?? string.Empty));
            }
        }

        return [.. path];
    }

    private static ImmutableArray<OptionDescriptor> ReadOptions(IMethodSymbol method)
    {
        ImmutableArray<OptionDescriptor>.Builder options = ImmutableArray.CreateBuilder<OptionDescriptor>();

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            ImmutableArray<AttributeData> attributes = parameter.GetAttributes();

            AttributeData? option = attributes.FirstOrDefault(
                static data => data.AttributeClass?.ToDisplayString() == OptionAttribute);

            if (option is null)
            {
                continue;
            }

            AttributeData? autocomplete = attributes.FirstOrDefault(
                static data => data.AttributeClass?.ToDisplayString() == AutocompleteAttribute);

            ImmutableArray<ChoiceDescriptor>.Builder choices = ImmutableArray.CreateBuilder<ChoiceDescriptor>();

            foreach (AttributeData choice in attributes.Where(
                static data => data.AttributeClass?.ToDisplayString() == ChoiceAttribute))
            {
                choices.Add(new ChoiceDescriptor(
                    ReadString(choice, 0) ?? string.Empty,
                    ReadString(choice, 1) ?? string.Empty));
            }

            options.Add(new OptionDescriptor(
                ReadString(option, 0) ?? string.Empty,
                ReadString(option, 1) ?? string.Empty,
                ReadBoolean(option, 2),
                choices.ToImmutable(),
                autocomplete is null ? null : ReadString(autocomplete, 0),
                parameter.Type.ToDisplayString(),
                ResolveOptionValueKind(parameter.Type)));
        }

        return options.ToImmutable();
    }

    private static OptionValueKind ResolveOptionValueKind(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return OptionValueKind.Enum;
        }

        return type.SpecialType switch
        {
            SpecialType.System_String => OptionValueKind.String,
            SpecialType.System_Boolean => OptionValueKind.Boolean,
            SpecialType.System_Int32 or SpecialType.System_Int64 => OptionValueKind.Integer,
            _ => OptionValueKind.Unsupported,
        };
    }

    private static ImmutableArray<string> ReadParameterTypes(IMethodSymbol method)
    {
        ImmutableArray<string>.Builder types = ImmutableArray.CreateBuilder<string>(method.Parameters.Length);

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            types.Add(parameter.Type.ToDisplayString());
        }

        return types.ToImmutable();
    }

    private static bool EndsWithCancellationToken(IMethodSymbol method) =>
        method.Parameters.Length > 0
        && method.Parameters[method.Parameters.Length - 1].Type.ToDisplayString() == CancellationTokenType;

    private static string? ReadTypeDisplayName(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index
        && attribute.ConstructorArguments[index].Value is ITypeSymbol type
            ? type.ToDisplayString()
            : null;

    private static string? ReadString(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;

    private static bool ReadBoolean(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index
        && attribute.ConstructorArguments[index].Value is bool value
        && value;
}

internal static class ManifestWriter
{
    internal static SourceText Write(CommandSurface surface)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace Numera.Discord.Routing;");
        builder.AppendLine();
        WriteTypes(builder);
        builder.AppendLine("internal static class EconomyCommandManifest");
        builder.AppendLine("{");
        builder.AppendLine("    internal static readonly string[] SlashCommandPaths =");
        builder.AppendLine("    [");

        foreach (CommandDescriptor command in surface.Commands.Where(static c => c.Kind == CommandKind.Slash))
        {
            builder.Append("        \"").Append(Escape(command.CanonicalPath)).AppendLine("\",");
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    internal static readonly string[] UserCommandNames =");
        builder.AppendLine("    [");

        foreach (CommandDescriptor command in surface.Commands.Where(static c => c.Kind == CommandKind.User))
        {
            builder.Append("        \"").Append(Escape(command.Name)).AppendLine("\",");
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    internal static readonly string[] MessageCommandNames =");
        builder.AppendLine("    [");

        foreach (CommandDescriptor command in surface.Commands.Where(static c => c.Kind == CommandKind.Message))
        {
            builder.Append("        \"").Append(Escape(command.Name)).AppendLine("\",");
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    internal static readonly string[] ComponentActions =");
        builder.AppendLine("    [");

        foreach (HandlerDescriptor handler in surface.Components)
        {
            builder.Append("        \"").Append(Escape(handler.Key)).AppendLine("\",");
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    internal static readonly string[] ModalActions =");
        builder.AppendLine("    [");

        foreach (HandlerDescriptor handler in surface.Modals)
        {
            builder.Append("        \"").Append(Escape(handler.Key)).AppendLine("\",");
        }

        builder.AppendLine("    ];");
        builder.AppendLine();
        builder.AppendLine("    internal static readonly GeneratedCommandDeclaration[] Declarations =");
        builder.AppendLine("    [");

        foreach (CommandDescriptor command in surface.Commands)
        {
            WriteDeclaration(builder, command);
        }

        builder.AppendLine("    ];");
        builder.AppendLine("}");

        return SourceText.From(builder.ToString(), Encoding.UTF8);
    }

    private static void WriteTypes(StringBuilder builder)
    {
        builder.AppendLine("internal enum GeneratedCommandKind");
        builder.AppendLine("{");
        builder.AppendLine("    Slash = 1,");
        builder.AppendLine("    User = 2,");
        builder.AppendLine("    Message = 3,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal enum GeneratedOptionValueKind");
        builder.AppendLine("{");
        builder.AppendLine("    Unsupported = 0,");
        builder.AppendLine("    String = 1,");
        builder.AppendLine("    Boolean = 2,");
        builder.AppendLine("    Integer = 3,");
        builder.AppendLine("    Enum = 4,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal sealed record GeneratedCommandChoice(string Name, string Value);");
        builder.AppendLine();
        builder.AppendLine("internal sealed record GeneratedCommandGroup(string Name, string Description);");
        builder.AppendLine();
        builder.AppendLine("internal sealed record GeneratedCommandOption(");
        builder.AppendLine("    string Name,");
        builder.AppendLine("    string Description,");
        builder.AppendLine("    GeneratedOptionValueKind ValueKind,");
        builder.AppendLine("    bool Required,");
        builder.AppendLine("    bool Autocomplete,");
        builder.AppendLine("    GeneratedCommandChoice[] Choices);");
        builder.AppendLine();
        builder.AppendLine("internal sealed record GeneratedCommandDeclaration(");
        builder.AppendLine("    GeneratedCommandKind Kind,");
        builder.AppendLine("    string Name,");
        builder.AppendLine("    string Description,");
        builder.AppendLine("    GeneratedCommandGroup[] GroupPath,");
        builder.AppendLine("    GeneratedCommandOption[] Options);");
        builder.AppendLine();
    }

    private static void WriteDeclaration(StringBuilder builder, CommandDescriptor command)
    {
        builder.AppendLine("        new GeneratedCommandDeclaration(");
        builder.Append("            GeneratedCommandKind.").Append(command.Kind).AppendLine(",");
        builder.Append("            \"").Append(Escape(command.Name)).AppendLine("\",");
        builder.Append("            \"").Append(Escape(command.Description ?? string.Empty)).AppendLine("\",");

        if (command.GroupPath.IsDefaultOrEmpty)
        {
            builder.AppendLine("            [],");
        }
        else
        {
            builder.AppendLine("            [");

            foreach (CommandGroupDescriptor group in command.GroupPath)
            {
                builder.Append("                new GeneratedCommandGroup(\"").Append(Escape(group.Name))
                    .Append("\", \"").Append(Escape(group.Description)).AppendLine("\"),");
            }

            builder.AppendLine("            ],");
        }

        if (command.Options.IsDefaultOrEmpty)
        {
            builder.AppendLine("            []),");
            return;
        }

        builder.AppendLine("            [");

        foreach (OptionDescriptor option in command.Options)
        {
            WriteOption(builder, option);
        }

        builder.AppendLine("            ]),");
    }

    private static void WriteOption(StringBuilder builder, OptionDescriptor option)
    {
        builder.AppendLine("                new GeneratedCommandOption(");
        builder.Append("                    \"").Append(Escape(option.Name)).AppendLine("\",");
        builder.Append("                    \"").Append(Escape(option.Description)).AppendLine("\",");
        builder.Append("                    GeneratedOptionValueKind.").Append(option.ValueKind).AppendLine(",");
        builder.Append("                    ").Append(option.Required ? "true" : "false").AppendLine(",");
        builder.Append("                    ")
            .Append(string.IsNullOrEmpty(option.AutocompleteProviderKey) ? "false" : "true").AppendLine(",");

        if (option.Choices.IsDefaultOrEmpty)
        {
            builder.AppendLine("                    []),");
            return;
        }

        builder.AppendLine("                    [");

        foreach (ChoiceDescriptor choice in option.Choices)
        {
            builder.Append("                        new GeneratedCommandChoice(\"").Append(Escape(choice.Name))
                .Append("\", \"").Append(Escape(choice.Value)).AppendLine("\"),");
        }

        builder.AppendLine("                    ]),");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
