using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Numera.Discord.Generators;

internal static class CommandSurfaceValidator
{
    internal const string EndpointReturnType = "System.Threading.Tasks.Task<Numera.Discord.Abstractions.DiscordEndpointResponse>";
    internal const string AutocompleteReturnType = "System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<Numera.Discord.Abstractions.DiscordAutocompleteOption>>";

    private const string ContextType = "Numera.Discord.Abstractions.DiscordEndpointContext";
    private const string AutocompleteRequestType = "Numera.Discord.Abstractions.DiscordAutocompleteRequest";
    private const string UserInputType = "Numera.Discord.Abstractions.DiscordUserInput";
    private const string MessageInputType = "Numera.Discord.Abstractions.DiscordMessageInput";
    private const string ComponentInputType = "Numera.Discord.Abstractions.DiscordComponentInput";

    internal static void Validate(SourceProductionContext context, CommandSurface surface)
    {
        ValidateCommands(context, surface);
        ValidateHandlerUniqueness(context, surface);
        ValidateModalForms(context, surface);
        ValidateAutocompleteReferences(context, surface);
        ValidateScopeCounts(context, surface);
    }

    private static void ValidateCommands(SourceProductionContext context, CommandSurface surface)
    {
        HashSet<string> seenCommandKeys = new(System.StringComparer.Ordinal);

        foreach (CommandDescriptor command in surface.Commands)
        {
            Location location = Resolve(command.Location);

            if (!seenCommandKeys.Add(command.DuplicateKey))
            {
                Report(context, CommandDiagnostics.DuplicateCommandName, location,
                    command.Kind.ToString(), "Global", command.CanonicalPath);
            }

            if (!CommandNameRules.IsLengthValid(command.Name))
            {
                Report(context, CommandDiagnostics.CommandNameLengthInvalid, location, command.Name);
            }

            if (command.GroupPath.Length > CommandNameRules.MaximumGroupDepth)
            {
                Report(context, CommandDiagnostics.GroupDepthExceeded, location, command.CanonicalPath);
            }

            if (command.Kind == CommandKind.Slash)
            {
                ValidateSlashCommand(context, command, location);
            }
            else if (!string.IsNullOrEmpty(command.Description))
            {
                Report(context, CommandDiagnostics.ContextCommandDescriptionForbidden, location,
                    command.EndpointDisplayName);
            }

            if (CommandNameRules.ContainsEmoji(command.Description) || CommandNameRules.ContainsEmoji(command.Name))
            {
                Report(context, CommandDiagnostics.EmojiInPublicText, location, command.CanonicalPath);
            }

            ValidateEndpointSignature(
                context, location, command.EndpointDisplayName, command.ReturnTypeDisplayName,
                command.EndsWithCancellationToken, EndpointReturnType);

            ValidateFirstParameter(
                context, location, command.EndpointDisplayName, command.ParameterTypeDisplayNames, ContextType);

            if (command.Kind == CommandKind.User)
            {
                ValidateInputParameter(
                    context, location, command.EndpointDisplayName, command.ParameterTypeDisplayNames, UserInputType);
            }
            else if (command.Kind == CommandKind.Message)
            {
                ValidateInputParameter(
                    context, location, command.EndpointDisplayName, command.ParameterTypeDisplayNames, MessageInputType);
            }
        }
    }

    private static void ValidateSlashCommand(
        SourceProductionContext context,
        CommandDescriptor command,
        Location location)
    {
        if (!CommandNameRules.IsNameFormatValid(command.Name))
        {
            Report(context, CommandDiagnostics.CommandNameFormatInvalid, location, command.Name);
        }

        if (!CommandNameRules.IsDescriptionLengthValid(command.Description))
        {
            Report(context, CommandDiagnostics.DescriptionLengthInvalid, location, command.CanonicalPath);
        }

        if (command.Options.Length > CommandNameRules.MaximumOptionCount)
        {
            Report(context, CommandDiagnostics.OptionCountExceeded, location,
                command.CanonicalPath, command.Options.Length.ToString());
        }

        HashSet<string> seenOptionNames = new(System.StringComparer.Ordinal);
        bool optionalSeen = false;

        foreach (OptionDescriptor option in command.Options)
        {
            if (!seenOptionNames.Add(option.Name))
            {
                Report(context, CommandDiagnostics.DuplicateOptionName, location,
                    command.CanonicalPath, option.Name);
            }

            if (!CommandNameRules.IsNameFormatValid(option.Name) || !CommandNameRules.IsLengthValid(option.Name))
            {
                Report(context, CommandDiagnostics.CommandNameFormatInvalid, location, option.Name);
            }

            if (!CommandNameRules.IsDescriptionLengthValid(option.Description))
            {
                Report(context, CommandDiagnostics.DescriptionLengthInvalid, location, option.Name);
            }

            if (CommandNameRules.ContainsEmoji(option.Description))
            {
                Report(context, CommandDiagnostics.EmojiInPublicText, location, option.Name);
            }

            if (option.Required && optionalSeen)
            {
                Report(context, CommandDiagnostics.RequiredOptionAfterOptional, location,
                    command.CanonicalPath, option.Name);
            }

            if (!option.Required)
            {
                optionalSeen = true;
            }

            if (option.ChoiceCount > CommandNameRules.MaximumChoiceCount)
            {
                Report(context, CommandDiagnostics.ChoiceCountExceeded, location,
                    option.Name, option.ChoiceCount.ToString());
            }

            if (option.ChoiceCount > 0 && !string.IsNullOrEmpty(option.AutocompleteProviderKey))
            {
                Report(context, CommandDiagnostics.ChoiceAndAutocompleteTogether, location, option.Name);
            }

            if (!option.TypeSupported)
            {
                Report(context, CommandDiagnostics.OptionTypeNotSupported, location,
                    option.Name, option.TypeDisplayName);
            }
        }
    }

    private static void ValidateHandlerUniqueness(SourceProductionContext context, CommandSurface surface)
    {
        ReportDuplicates(context, surface.Components, CommandDiagnostics.DuplicateComponentAction);
        ReportDuplicates(context, surface.Modals, CommandDiagnostics.DuplicateModalAction);
        ReportDuplicates(context, surface.AutocompleteProviders, CommandDiagnostics.DuplicateAutocompleteProviderKey);

        foreach (HandlerDescriptor handler in surface.Components.Concat(surface.Modals))
        {
            Location location = Resolve(handler.Location);

            if (handler.Key.Length > CommandNameRules.MaximumCustomIdLength)
            {
                Report(context, CommandDiagnostics.CustomIdTooLong, location, handler.Key);
            }

            ValidateEndpointSignature(
                context, location, handler.EndpointDisplayName, handler.ReturnTypeDisplayName,
                handler.EndsWithCancellationToken, EndpointReturnType);

            ValidateFirstParameter(
                context, location, handler.EndpointDisplayName, handler.ParameterTypeDisplayNames, ContextType);

            ValidateInputParameter(
                context, location, handler.EndpointDisplayName, handler.ParameterTypeDisplayNames,
                handler.DeclaredInputTypeDisplayName ?? ComponentInputType);
        }

        foreach (HandlerDescriptor provider in surface.AutocompleteProviders)
        {
            Location location = Resolve(provider.Location);

            ValidateEndpointSignature(
                context, location, provider.EndpointDisplayName, provider.ReturnTypeDisplayName,
                provider.EndsWithCancellationToken, AutocompleteReturnType);

            ValidateFirstParameter(
                context, location, provider.EndpointDisplayName, provider.ParameterTypeDisplayNames,
                AutocompleteRequestType);
        }
    }

    private static void ValidateModalForms(SourceProductionContext context, CommandSurface surface)
    {
        foreach (ModalFormDescriptor form in surface.ModalForms)
        {
            Location location = Resolve(form.Location);

            if (form.Title.Length > CommandNameRules.MaximumModalTitleLength)
            {
                Report(context, CommandDiagnostics.ModalTextLengthInvalid, location,
                    form.TypeDisplayName, "Title", CommandNameRules.MaximumModalTitleLength.ToString());
            }

            if (CommandNameRules.ContainsEmoji(form.Title))
            {
                Report(context, CommandDiagnostics.EmojiInPublicText, location, form.TypeDisplayName);
            }

            HashSet<string> seenFieldIds = new(System.StringComparer.Ordinal);

            foreach (ModalFieldDescriptor field in form.Fields)
            {
                if (!seenFieldIds.Add(field.CustomId))
                {
                    Report(context, CommandDiagnostics.ModalFieldCustomIdDuplicated, location,
                        form.TypeDisplayName, field.CustomId);
                }

                if (field.Label.Length > CommandNameRules.MaximumModalLabelLength)
                {
                    Report(context, CommandDiagnostics.ModalTextLengthInvalid, location,
                        form.TypeDisplayName, "Label", CommandNameRules.MaximumModalLabelLength.ToString());
                }

                if (field.Placeholder.Length > CommandNameRules.MaximumModalPlaceholderLength)
                {
                    Report(context, CommandDiagnostics.ModalTextLengthInvalid, location,
                        form.TypeDisplayName, "Placeholder", CommandNameRules.MaximumModalPlaceholderLength.ToString());
                }

                if (CommandNameRules.ContainsEmoji(field.Label) || CommandNameRules.ContainsEmoji(field.Placeholder))
                {
                    Report(context, CommandDiagnostics.EmojiInPublicText, location, field.CustomId);
                }
            }
        }
    }

    private static void ValidateAutocompleteReferences(SourceProductionContext context, CommandSurface surface)
    {
        HashSet<string> providerKeys = new(
            surface.AutocompleteProviders.Select(static provider => provider.Key),
            System.StringComparer.Ordinal);

        foreach (CommandDescriptor command in surface.Commands)
        {
            foreach (OptionDescriptor option in command.Options)
            {
                if (string.IsNullOrEmpty(option.AutocompleteProviderKey))
                {
                    continue;
                }

                if (!providerKeys.Contains(option.AutocompleteProviderKey!))
                {
                    Report(context, CommandDiagnostics.AutocompleteProviderMissing,
                        Resolve(command.Location), option.Name, option.AutocompleteProviderKey!);
                }
            }
        }
    }

    private static void ValidateScopeCounts(SourceProductionContext context, CommandSurface surface)
    {
        CheckScope(context, surface, CommandKind.Slash, CommandNameRules.MaximumChatInputCommands);
        CheckScope(context, surface, CommandKind.User, CommandNameRules.MaximumContextCommands);
        CheckScope(context, surface, CommandKind.Message, CommandNameRules.MaximumContextCommands);
    }

    private static void CheckScope(
        SourceProductionContext context,
        CommandSurface surface,
        CommandKind kind,
        int maximum)
    {
        ImmutableArray<CommandDescriptor> matching =
            surface.Commands.Where(command => command.Kind == kind).ToImmutableArray();

        int rootCount = matching
            .Select(static command => command.GroupPath.IsDefaultOrEmpty ? command.Name : command.GroupPath[0])
            .Distinct(System.StringComparer.Ordinal)
            .Count();

        if (rootCount > maximum)
        {
            Report(context, CommandDiagnostics.ScopeCommandCountExceeded,
                Resolve(matching.Length > 0 ? matching[0].Location : null),
                kind.ToString(), maximum.ToString(), rootCount.ToString());
        }
    }

    private static void ValidateEndpointSignature(
        SourceProductionContext context,
        Location location,
        string endpointDisplayName,
        string actualReturnType,
        bool endsWithCancellationToken,
        string expectedReturnType)
    {
        if (!string.Equals(actualReturnType, expectedReturnType, System.StringComparison.Ordinal))
        {
            Report(context, CommandDiagnostics.EndpointReturnTypeInvalid, location,
                endpointDisplayName, expectedReturnType);
        }

        if (!endsWithCancellationToken)
        {
            Report(context, CommandDiagnostics.CancellationTokenParameterInvalid, location, endpointDisplayName);
        }
    }

    private static void ValidateFirstParameter(
        SourceProductionContext context,
        Location location,
        string endpointDisplayName,
        ImmutableArray<string> parameterTypes,
        string expectedType)
    {
        if (parameterTypes.IsDefaultOrEmpty
            || !string.Equals(parameterTypes[0], expectedType, System.StringComparison.Ordinal))
        {
            Report(context, CommandDiagnostics.EndpointContextParameterInvalid, location,
                endpointDisplayName, expectedType);
        }
    }

    private static void ValidateInputParameter(
        SourceProductionContext context,
        Location location,
        string endpointDisplayName,
        ImmutableArray<string> parameterTypes,
        string expectedType)
    {
        if (parameterTypes.IsDefaultOrEmpty
            || parameterTypes.Length < 2
            || !string.Equals(parameterTypes[1], expectedType, System.StringComparison.Ordinal))
        {
            Report(context, CommandDiagnostics.EndpointInputParameterInvalid, location,
                endpointDisplayName, expectedType);
        }
    }

    private static void ReportDuplicates(
        SourceProductionContext context,
        ImmutableArray<HandlerDescriptor> handlers,
        DiagnosticDescriptor descriptor)
    {
        HashSet<string> seen = new(System.StringComparer.Ordinal);

        foreach (HandlerDescriptor handler in handlers)
        {
            if (!seen.Add(handler.Key))
            {
                Report(context, descriptor, Resolve(handler.Location), handler.Key);
            }
        }
    }

    private static Location Resolve(LocationInfo? location) => location?.ToLocation() ?? Location.None;

    private static void Report(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        Location location,
        params object[] messageArguments) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArguments));
}
