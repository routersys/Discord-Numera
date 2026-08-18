namespace Numera.Discord.Abstractions;

public enum DiscordResponseKind
{
    Message = 1,
    UpdateMessage = 2,
    Modal = 3,
    Autocomplete = 4,
    NoContent = 5,
    Failure = 6,
}

public sealed class DiscordEndpointFailure
{
    public DiscordEndpointFailure(string categoryToken, string errorCode, string? field)
    {
        ArgumentNullException.ThrowIfNull(categoryToken);
        ArgumentNullException.ThrowIfNull(errorCode);

        CategoryToken = categoryToken;
        ErrorCode = errorCode;
        Field = field;
    }

    public string CategoryToken { get; }

    public string ErrorCode { get; }

    public string? Field { get; }
}

public sealed class DiscordEndpointContext
{
    public DiscordEndpointContext(
        ulong interactionId,
        ulong userId,
        ulong guildId,
        ulong channelId,
        string locale,
        string commandPath,
        AuthorizationLevel level,
        string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(commandPath);
        ArgumentNullException.ThrowIfNull(sessionToken);

        InteractionId = interactionId;
        UserId = userId;
        GuildId = guildId;
        ChannelId = channelId;
        Locale = locale;
        CommandPath = commandPath;
        Level = level;
        SessionToken = sessionToken;
    }

    public ulong InteractionId { get; }

    public ulong UserId { get; }

    public ulong GuildId { get; }

    public ulong ChannelId { get; }

    public string Locale { get; }

    public string CommandPath { get; }

    public AuthorizationLevel Level { get; }

    public string SessionToken { get; }
}

public sealed class DiscordUserInput
{
    public DiscordUserInput(ulong userId) => UserId = userId;

    public ulong UserId { get; }
}

public sealed class DiscordMessageInput
{
    public DiscordMessageInput(ulong messageId, ulong channelId, ulong authorUserId)
    {
        MessageId = messageId;
        ChannelId = channelId;
        AuthorUserId = authorUserId;
    }

    public ulong MessageId { get; }

    public ulong ChannelId { get; }

    public ulong AuthorUserId { get; }
}

public sealed class DiscordComponentInput
{
    private static readonly IReadOnlyList<string> EmptyValues = [];

    public DiscordComponentInput(string action, string sessionToken, IReadOnlyList<string>? values = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(sessionToken);

        Action = action;
        SessionToken = sessionToken;
        Values = values ?? EmptyValues;
    }

    public string Action { get; }

    public string SessionToken { get; }

    public IReadOnlyList<string> Values { get; }
}

public sealed class DiscordAutocompleteRequest
{
    public DiscordAutocompleteRequest(
        ulong userId,
        ulong guildId,
        string commandPath,
        string optionName,
        string value)
    {
        ArgumentNullException.ThrowIfNull(commandPath);
        ArgumentNullException.ThrowIfNull(optionName);
        ArgumentNullException.ThrowIfNull(value);

        UserId = userId;
        GuildId = guildId;
        CommandPath = commandPath;
        OptionName = optionName;
        Value = value;
    }

    public ulong UserId { get; }

    public ulong GuildId { get; }

    public string CommandPath { get; }

    public string OptionName { get; }

    public string Value { get; }
}

public enum DiscordButtonStyle
{
    Primary = 1,
    Secondary = 2,
    Danger = 3,
}

public static class ComponentFailure
{
    public const string SelectOptionCountOutOfRange =
        "Select の Option は1件以上25件以下でなければなりません。";

    public const string ButtonCountOutOfRange =
        "Button は1行あたり5個以下でなければなりません。";

    public const string FieldCountOutOfRange =
        "Embed の Field は10個以下でなければなりません。";
}

public sealed class DiscordResponseButton
{
    public DiscordResponseButton(
        string customId,
        string labelKey,
        DiscordButtonStyle style,
        bool disabled = false)
    {
        ArgumentNullException.ThrowIfNull(customId);
        ArgumentNullException.ThrowIfNull(labelKey);

        CustomId = customId;
        LabelKey = labelKey;
        Style = style;
        Disabled = disabled;
    }

    public string CustomId { get; }

    public string LabelKey { get; }

    public DiscordButtonStyle Style { get; }

    public bool Disabled { get; }
}

public sealed class DiscordResponseSelectOption
{
    public DiscordResponseSelectOption(string label, string value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);

        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}

public sealed class DiscordResponseSelect
{
    public const int MaximumOptionCount = 25;

    public DiscordResponseSelect(
        string customId,
        string placeholderKey,
        IReadOnlyList<DiscordResponseSelectOption> options)
    {
        ArgumentNullException.ThrowIfNull(customId);
        ArgumentNullException.ThrowIfNull(placeholderKey);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Count is 0 or > MaximumOptionCount)
        {
            throw new ArgumentException(ComponentFailure.SelectOptionCountOutOfRange, nameof(options));
        }

        CustomId = customId;
        PlaceholderKey = placeholderKey;
        Options = options;
    }

    public string CustomId { get; }

    public string PlaceholderKey { get; }

    public IReadOnlyList<DiscordResponseSelectOption> Options { get; }
}

public sealed class DiscordResponseComponents
{
    public const int MaximumButtonCount = 5;

    private static readonly IReadOnlyList<DiscordResponseButton> EmptyButtons = [];

    public static readonly DiscordResponseComponents None = new(null, EmptyButtons);

    public DiscordResponseComponents(
        DiscordResponseSelect? select,
        IReadOnlyList<DiscordResponseButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        if (buttons.Count > MaximumButtonCount)
        {
            throw new ArgumentException(ComponentFailure.ButtonCountOutOfRange, nameof(buttons));
        }

        Select = select;
        Buttons = buttons;
    }

    public DiscordResponseSelect? Select { get; }

    public IReadOnlyList<DiscordResponseButton> Buttons { get; }

    public bool IsEmpty => Select is null && Buttons.Count == 0;
}

public sealed class DiscordResponseField
{
    public DiscordResponseField(string labelKey, string valueKey)
    {
        ArgumentNullException.ThrowIfNull(labelKey);
        ArgumentNullException.ThrowIfNull(valueKey);

        LabelKey = labelKey;
        ValueKey = valueKey;
    }

    public string LabelKey { get; }

    public string ValueKey { get; }
}

public sealed record DiscordResponseAttachment(string FileName, byte[] Content)
{
    public const string CanonicalScheme = "attachment://";

    public string Reference => CanonicalScheme + FileName;
}

public sealed class DiscordResponseBody
{
    public const int MaximumFieldCount = 10;

    private static readonly IReadOnlyList<DiscordResponseField> EmptyFields = [];

    public static readonly DiscordResponseBody Plain =
        new(EmptyFields, DiscordResponseComponents.None);

    public DiscordResponseBody(
        IReadOnlyList<DiscordResponseField> fields,
        DiscordResponseComponents components,
        DiscordResponseAttachment? attachment = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(components);

        if (fields.Count > MaximumFieldCount)
        {
            throw new ArgumentException(ComponentFailure.FieldCountOutOfRange, nameof(fields));
        }

        Fields = fields;
        Components = components;
        Attachment = attachment;
    }

    public IReadOnlyList<DiscordResponseField> Fields { get; }

    public DiscordResponseComponents Components { get; }

    public DiscordResponseAttachment? Attachment { get; }

    public static DiscordResponseBody WithComponents(DiscordResponseComponents components) =>
        new(EmptyFields, components);

    public static DiscordResponseBody WithFields(IReadOnlyList<DiscordResponseField> fields) =>
        new(fields, DiscordResponseComponents.None);

    public static DiscordResponseBody WithAttachment(DiscordResponseAttachment attachment) =>
        new(EmptyFields, DiscordResponseComponents.None, attachment);
}

public sealed class DiscordEndpointResponse
{
    private static readonly IReadOnlyDictionary<string, string> EmptyViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private DiscordEndpointResponse(
        DiscordResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral,
        DiscordResponseBody body,
        DiscordEndpointFailure? failure = null)
    {
        Kind = kind;
        ViewKey = viewKey;
        ViewData = viewData;
        Ephemeral = ephemeral;
        Body = body;
        Failure = failure;
    }

    public DiscordResponseBody Body { get; }

    public DiscordEndpointFailure? Failure { get; }

    public DiscordResponseKind Kind { get; }

    public string ViewKey { get; }

    public IReadOnlyDictionary<string, string> ViewData { get; }

    public bool Ephemeral { get; }

    public static DiscordEndpointResponse Message(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral = true) =>
        Create(DiscordResponseKind.Message, viewKey, viewData, ephemeral, DiscordResponseBody.Plain);

    public static DiscordEndpointResponse Message(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        DiscordResponseBody body,
        bool ephemeral = true) =>
        Create(DiscordResponseKind.Message, viewKey, viewData, ephemeral, body);

    public static DiscordEndpointResponse UpdateMessage(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.UpdateMessage, viewKey, viewData, true, DiscordResponseBody.Plain);

    public static DiscordEndpointResponse UpdateMessage(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        DiscordResponseBody body) =>
        Create(DiscordResponseKind.UpdateMessage, viewKey, viewData, true, body);

    public static DiscordEndpointResponse Modal(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.Modal, viewKey, viewData, true, DiscordResponseBody.Plain);

    public static DiscordEndpointResponse Autocomplete(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.Autocomplete, viewKey, viewData, true, DiscordResponseBody.Plain);

    public static DiscordEndpointResponse NoContent() =>
        Create(DiscordResponseKind.NoContent, string.Empty, EmptyViewData, true, DiscordResponseBody.Plain);

    public static DiscordEndpointResponse Failed(DiscordEndpointFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new DiscordEndpointResponse(
            DiscordResponseKind.Failure,
            string.Empty,
            EmptyViewData,
            true,
            DiscordResponseBody.Plain,
            failure);
    }

    private static DiscordEndpointResponse Create(
        DiscordResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral,
        DiscordResponseBody body)
    {
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentNullException.ThrowIfNull(body);

        return new DiscordEndpointResponse(kind, viewKey, viewData, ephemeral, body);
    }
}

public interface IEconomyEndpoint;

public sealed class DiscordAutocompleteOption
{
    public const int MinimumNameLength = 1;
    public const int MaximumNameLength = 100;
    public const int MaximumValueLength = 100;

    private DiscordAutocompleteOption(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    public static bool IsAcceptable(string? name, string? value) =>
        name is not null
        && value is not null
        && name.Length >= MinimumNameLength
        && name.Length <= MaximumNameLength
        && value.Length <= MaximumValueLength;

    public static bool TryCreate(string? name, string? value, out DiscordAutocompleteOption? option)
    {
        if (!IsAcceptable(name, value))
        {
            option = null;
            return false;
        }

        option = new DiscordAutocompleteOption(name!, value!);
        return true;
    }

    public static DiscordAutocompleteOption Create(string name, string value) =>
        TryCreate(name, value, out DiscordAutocompleteOption? option)
            ? option!
            : throw new ArgumentException(AutocompleteFailure.OptionOutOfRange, nameof(name));
}

public static class AutocompleteFailure
{
    public const string OptionOutOfRange =
        "Autocomplete Option の名前は1文字以上100文字以下、値は100文字以下でなければなりません。";
}

public sealed class DiscordModalFieldDefinition
{
    public DiscordModalFieldDefinition(
        string customId,
        string label,
        string placeholder,
        EconomyModalFieldStyle style,
        bool required,
        int minimumLength,
        int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(customId);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(placeholder);

        CustomId = customId;
        Label = label;
        Placeholder = placeholder;
        Style = style;
        Required = required;
        MinimumLength = minimumLength;
        MaximumLength = maximumLength;
    }

    public string CustomId { get; }

    public string Label { get; }

    public string Placeholder { get; }

    public EconomyModalFieldStyle Style { get; }

    public bool Required { get; }

    public int MinimumLength { get; }

    public int MaximumLength { get; }
}

public interface IModalFormCatalog
{
    IReadOnlyList<DiscordModalFieldDefinition> Resolve(string action);
}

public static class DiscordCustomId
{
    public const string Prefix = "bank";
    public const string Version = "v1";
    public const string Separator = ":";
    public const string ButtonKind = "btn";
    public const string SelectKind = "sel";
    public const string ModalKind = "modal";
    public const string Wildcard = "*";

    public static string Button(string action, string sessionToken) => Compose(ButtonKind, action, sessionToken);

    public static string Select(string action, string sessionToken) => Compose(SelectKind, action, sessionToken);

    public static string Modal(string action, string sessionToken) => Compose(ModalKind, action, sessionToken);

    private static string Compose(string kind, string action, string sessionToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(sessionToken);

        return string.Concat(
            Prefix, Separator, Version, Separator, kind, Separator, action, Separator, sessionToken);
    }
}
