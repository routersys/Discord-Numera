using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Numera.Discord.Generators;

internal enum CommandKind
{
    Slash = 1,
    User = 2,
    Message = 3,
}

internal sealed class LocationInfo
{
    private LocationInfo(string filePath, TextSpanInfo span)
    {
        FilePath = filePath;
        Span = span;
    }

    internal string FilePath { get; }

    internal TextSpanInfo Span { get; }

    internal static LocationInfo? From(Location location)
    {
        if (location.SourceTree is null)
        {
            return null;
        }

        Microsoft.CodeAnalysis.Text.TextSpan span = location.SourceSpan;
        return new LocationInfo(location.SourceTree.FilePath, new TextSpanInfo(span.Start, span.Length));
    }

    internal Location ToLocation() =>
        Location.Create(
            FilePath,
            new Microsoft.CodeAnalysis.Text.TextSpan(Span.Start, Span.Length),
            new Microsoft.CodeAnalysis.Text.LinePositionSpan());

    public override bool Equals(object? obj) =>
        obj is LocationInfo other && FilePath == other.FilePath && Span.Equals(other.Span);

    public override int GetHashCode() => (FilePath.GetHashCode() * 397) ^ Span.GetHashCode();
}

internal readonly struct TextSpanInfo
{
    internal TextSpanInfo(int start, int length)
    {
        Start = start;
        Length = length;
    }

    internal int Start { get; }

    internal int Length { get; }

    public override bool Equals(object? obj) =>
        obj is TextSpanInfo other && Start == other.Start && Length == other.Length;

    public override int GetHashCode() => (Start * 397) ^ Length;
}

internal enum ComponentKind
{
    None = 0,
    Button = 1,
    Select = 2,
}

internal enum ModalFieldStyle
{
    Short = 1,
    Paragraph = 2,
}

internal enum OptionValueKind
{
    Unsupported = 0,
    String = 1,
    Boolean = 2,
    Integer = 3,
    Enum = 4,
}

internal sealed class ChoiceDescriptor
{
    internal ChoiceDescriptor(string name, string value)
    {
        Name = name;
        Value = value;
    }

    internal string Name { get; }

    internal string Value { get; }
}

internal sealed class OptionDescriptor
{
    internal OptionDescriptor(
        string name,
        string description,
        bool required,
        ImmutableArray<ChoiceDescriptor> choices,
        string? autocompleteProviderKey,
        string typeDisplayName,
        string typeFullyQualifiedName,
        OptionValueKind valueKind)
    {
        Name = name;
        Description = description;
        Required = required;
        Choices = choices;
        AutocompleteProviderKey = autocompleteProviderKey;
        TypeDisplayName = typeDisplayName;
        TypeFullyQualifiedName = typeFullyQualifiedName;
        ValueKind = valueKind;
    }

    internal string Name { get; }

    internal string Description { get; }

    internal bool Required { get; }

    internal ImmutableArray<ChoiceDescriptor> Choices { get; }

    internal int ChoiceCount => Choices.IsDefaultOrEmpty ? 0 : Choices.Length;

    internal string? AutocompleteProviderKey { get; }

    internal string TypeDisplayName { get; }

    internal string TypeFullyQualifiedName { get; }

    internal OptionValueKind ValueKind { get; }

    internal bool TypeSupported => ValueKind != OptionValueKind.Unsupported;
}

internal sealed class CommandGroupDescriptor
{
    internal CommandGroupDescriptor(string name, string description)
    {
        Name = name;
        Description = description;
    }

    internal string Name { get; }

    internal string Description { get; }
}

internal sealed class CommandDescriptor
{
    internal CommandDescriptor(
        CommandKind kind,
        string name,
        string? description,
        ImmutableArray<CommandGroupDescriptor> groupPath,
        ImmutableArray<OptionDescriptor> options,
        string endpointDisplayName,
        string endpointTypeDisplayName,
        string endpointTypeFullyQualifiedName,
        string endpointMethodName,
        string returnTypeDisplayName,
        bool endsWithCancellationToken,
        ImmutableArray<string> parameterTypeDisplayNames,
        LocationInfo? location)
    {
        Kind = kind;
        Name = name;
        Description = description;
        GroupPath = groupPath;
        Options = options;
        EndpointDisplayName = endpointDisplayName;
        EndpointTypeDisplayName = endpointTypeDisplayName;
        EndpointTypeFullyQualifiedName = endpointTypeFullyQualifiedName;
        EndpointMethodName = endpointMethodName;
        ReturnTypeDisplayName = returnTypeDisplayName;
        EndsWithCancellationToken = endsWithCancellationToken;
        ParameterTypeDisplayNames = parameterTypeDisplayNames;
        Location = location;
    }

    internal CommandKind Kind { get; }

    internal string Name { get; }

    internal string? Description { get; }

    internal ImmutableArray<CommandGroupDescriptor> GroupPath { get; }

    internal ImmutableArray<OptionDescriptor> Options { get; }

    internal string EndpointDisplayName { get; }

    internal string EndpointTypeDisplayName { get; }

    internal string EndpointTypeFullyQualifiedName { get; }

    internal string EndpointMethodName { get; }

    internal string ReturnTypeDisplayName { get; }

    internal bool EndsWithCancellationToken { get; }

    internal ImmutableArray<string> ParameterTypeDisplayNames { get; }

    internal LocationInfo? Location { get; }

    internal string RootName => GroupPath.IsDefaultOrEmpty ? Name : GroupPath[0].Name;

    internal string CanonicalPath =>
        GroupPath.IsDefaultOrEmpty
            ? Name
            : string.Join(" ", GroupPath.Select(static group => group.Name).Concat(new[] { Name }));

    internal string DuplicateKey => $"{Kind}:{CanonicalPath}";
}

internal sealed class HandlerDescriptor
{
    internal HandlerDescriptor(
        string key,
        string endpointDisplayName,
        string endpointTypeFullyQualifiedName,
        string endpointMethodName,
        string returnTypeDisplayName,
        bool endsWithCancellationToken,
        ImmutableArray<string> parameterTypeDisplayNames,
        string? declaredInputTypeDisplayName,
        string? declaredInputTypeFullyQualifiedName,
        ComponentKind componentKind,
        LocationInfo? location)
    {
        Key = key;
        EndpointDisplayName = endpointDisplayName;
        EndpointTypeFullyQualifiedName = endpointTypeFullyQualifiedName;
        EndpointMethodName = endpointMethodName;
        ReturnTypeDisplayName = returnTypeDisplayName;
        EndsWithCancellationToken = endsWithCancellationToken;
        ParameterTypeDisplayNames = parameterTypeDisplayNames;
        DeclaredInputTypeDisplayName = declaredInputTypeDisplayName;
        DeclaredInputTypeFullyQualifiedName = declaredInputTypeFullyQualifiedName;
        ComponentKind = componentKind;
        Location = location;
    }

    internal string Key { get; }

    internal string EndpointDisplayName { get; }

    internal string EndpointTypeFullyQualifiedName { get; }

    internal string EndpointMethodName { get; }

    internal string ReturnTypeDisplayName { get; }

    internal bool EndsWithCancellationToken { get; }

    internal ImmutableArray<string> ParameterTypeDisplayNames { get; }

    internal string? DeclaredInputTypeDisplayName { get; }

    internal string? DeclaredInputTypeFullyQualifiedName { get; }

    internal ComponentKind ComponentKind { get; }

    internal LocationInfo? Location { get; }
}

internal sealed class ModalFormDescriptor
{
    internal ModalFormDescriptor(
        string title,
        ImmutableArray<ModalFieldDescriptor> fields,
        string typeDisplayName,
        string typeFullyQualifiedName,
        LocationInfo? location)
    {
        Title = title;
        Fields = fields;
        TypeDisplayName = typeDisplayName;
        TypeFullyQualifiedName = typeFullyQualifiedName;
        Location = location;
    }

    internal string Title { get; }

    internal ImmutableArray<ModalFieldDescriptor> Fields { get; }

    internal string TypeDisplayName { get; }

    internal string TypeFullyQualifiedName { get; }

    internal LocationInfo? Location { get; }
}

internal sealed class ModalFieldDescriptor
{
    internal ModalFieldDescriptor(
        string propertyName,
        string customId,
        string label,
        string placeholder,
        ModalFieldStyle style,
        bool required,
        int minimumLength,
        int maximumLength)
    {
        PropertyName = propertyName;
        CustomId = customId;
        Label = label;
        Placeholder = placeholder;
        Style = style;
        Required = required;
        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
    }

    internal string PropertyName { get; }

    internal string CustomId { get; }

    internal string Label { get; }

    internal string Placeholder { get; }

    internal ModalFieldStyle Style { get; }

    internal bool Required { get; }

    internal int MinimumLength { get; }

    internal int MaximumLength { get; }
}

internal sealed class CommandSurface
{
    internal CommandSurface(
        ImmutableArray<CommandDescriptor> commands,
        ImmutableArray<HandlerDescriptor> components,
        ImmutableArray<HandlerDescriptor> modals,
        ImmutableArray<HandlerDescriptor> autocompleteProviders,
        ImmutableArray<ModalFormDescriptor> modalForms)
    {
        Commands = commands;
        Components = components;
        Modals = modals;
        AutocompleteProviders = autocompleteProviders;
        ModalForms = modalForms;
    }

    internal ImmutableArray<CommandDescriptor> Commands { get; }

    internal ImmutableArray<HandlerDescriptor> Components { get; }

    internal ImmutableArray<HandlerDescriptor> Modals { get; }

    internal ImmutableArray<HandlerDescriptor> AutocompleteProviders { get; }

    internal ImmutableArray<ModalFormDescriptor> ModalForms { get; }

    internal IEnumerable<HandlerDescriptor> AllHandlers =>
        Components.Concat(Modals).Concat(AutocompleteProviders);
}
