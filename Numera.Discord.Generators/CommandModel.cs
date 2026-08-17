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

internal sealed class OptionDescriptor
{
    internal OptionDescriptor(
        string name,
        string description,
        bool required,
        int choiceCount,
        string? autocompleteProviderKey)
    {
        Name = name;
        Description = description;
        Required = required;
        ChoiceCount = choiceCount;
        AutocompleteProviderKey = autocompleteProviderKey;
    }

    internal string Name { get; }

    internal string Description { get; }

    internal bool Required { get; }

    internal int ChoiceCount { get; }

    internal string? AutocompleteProviderKey { get; }
}

internal sealed class CommandDescriptor
{
    internal CommandDescriptor(
        CommandKind kind,
        string name,
        string? description,
        ImmutableArray<string> groupPath,
        ImmutableArray<OptionDescriptor> options,
        string endpointDisplayName,
        string returnTypeDisplayName,
        bool endsWithCancellationToken,
        LocationInfo? location)
    {
        Kind = kind;
        Name = name;
        Description = description;
        GroupPath = groupPath;
        Options = options;
        EndpointDisplayName = endpointDisplayName;
        ReturnTypeDisplayName = returnTypeDisplayName;
        EndsWithCancellationToken = endsWithCancellationToken;
        Location = location;
    }

    internal CommandKind Kind { get; }

    internal string Name { get; }

    internal string? Description { get; }

    internal ImmutableArray<string> GroupPath { get; }

    internal ImmutableArray<OptionDescriptor> Options { get; }

    internal string EndpointDisplayName { get; }

    internal string ReturnTypeDisplayName { get; }

    internal bool EndsWithCancellationToken { get; }

    internal LocationInfo? Location { get; }

    internal string CanonicalPath =>
        GroupPath.IsDefaultOrEmpty ? Name : string.Join(" ", GroupPath.Concat(new[] { Name }));

    internal string DuplicateKey => $"{Kind}:{CanonicalPath}";
}

internal sealed class HandlerDescriptor
{
    internal HandlerDescriptor(string key, string endpointDisplayName, string returnTypeDisplayName, bool endsWithCancellationToken, LocationInfo? location)
    {
        Key = key;
        EndpointDisplayName = endpointDisplayName;
        ReturnTypeDisplayName = returnTypeDisplayName;
        EndsWithCancellationToken = endsWithCancellationToken;
        Location = location;
    }

    internal string Key { get; }

    internal string EndpointDisplayName { get; }

    internal string ReturnTypeDisplayName { get; }

    internal bool EndsWithCancellationToken { get; }

    internal LocationInfo? Location { get; }
}

internal sealed class ModalFormDescriptor
{
    internal ModalFormDescriptor(string title, ImmutableArray<ModalFieldDescriptor> fields, string typeDisplayName, LocationInfo? location)
    {
        Title = title;
        Fields = fields;
        TypeDisplayName = typeDisplayName;
        Location = location;
    }

    internal string Title { get; }

    internal ImmutableArray<ModalFieldDescriptor> Fields { get; }

    internal string TypeDisplayName { get; }

    internal LocationInfo? Location { get; }
}

internal sealed class ModalFieldDescriptor
{
    internal ModalFieldDescriptor(string customId, string label, string placeholder)
    {
        CustomId = customId;
        Label = label;
        Placeholder = placeholder;
    }

    internal string CustomId { get; }

    internal string Label { get; }

    internal string Placeholder { get; }
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
