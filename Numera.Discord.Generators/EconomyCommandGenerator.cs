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

        IncrementalValuesProvider<HandlerDescriptor> components = Handlers(context, ComponentAttribute, 1);
        IncrementalValuesProvider<HandlerDescriptor> modals = Handlers(context, ModalAttribute, 0);
        IncrementalValuesProvider<HandlerDescriptor> providers =
            Handlers(context, AutocompleteProviderAttribute, 0);
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
        int keyArgumentIndex) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeName,
            static (node, _) => true,
            (syntaxContext, _) => BuildHandler(syntaxContext, keyArgumentIndex));

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
            LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None));
    }

    private static HandlerDescriptor BuildHandler(GeneratorAttributeSyntaxContext context, int keyArgumentIndex)
    {
        IMethodSymbol method = (IMethodSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];

        return new HandlerDescriptor(
            ReadString(attribute, keyArgumentIndex) ?? string.Empty,
            method.ToDisplayString(),
            method.ReturnType.ToDisplayString(),
            EndsWithCancellationToken(method),
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

    private static ImmutableArray<string> ReadGroupPath(INamedTypeSymbol? containingType)
    {
        List<string> path = [];

        for (INamedTypeSymbol? current = containingType; current is not null; current = current.ContainingType)
        {
            AttributeData? group = current.GetAttributes().FirstOrDefault(
                static data => data.AttributeClass?.ToDisplayString() == GroupAttribute);

            if (group is not null)
            {
                path.Insert(0, ReadString(group, 0) ?? string.Empty);
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

            int choiceCount = attributes.Count(
                static data => data.AttributeClass?.ToDisplayString() == ChoiceAttribute);

            options.Add(new OptionDescriptor(
                ReadString(option, 0) ?? string.Empty,
                ReadString(option, 1) ?? string.Empty,
                ReadBoolean(option, 2),
                choiceCount,
                autocomplete is null ? null : ReadString(autocomplete, 0)));
        }

        return options.ToImmutable();
    }

    private static bool EndsWithCancellationToken(IMethodSymbol method) =>
        method.Parameters.Length > 0
        && method.Parameters[method.Parameters.Length - 1].Type.ToDisplayString() == CancellationTokenType;

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
        builder.AppendLine("}");

        return SourceText.From(builder.ToString(), Encoding.UTF8);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
