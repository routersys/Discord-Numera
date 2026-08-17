namespace Numera.Discord.Abstractions;

public enum DiscordResponseKind
{
    Message = 1,
    UpdateMessage = 2,
    Modal = 3,
    Autocomplete = 4,
    NoContent = 5,
}

public sealed class DiscordEndpointContext
{
    public DiscordEndpointContext(
        ulong interactionId,
        ulong userId,
        ulong guildId,
        ulong channelId,
        string locale,
        string commandPath)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(commandPath);

        InteractionId = interactionId;
        UserId = userId;
        GuildId = guildId;
        ChannelId = channelId;
        Locale = locale;
        CommandPath = commandPath;
    }

    public ulong InteractionId { get; }

    public ulong UserId { get; }

    public ulong GuildId { get; }

    public ulong ChannelId { get; }

    public string Locale { get; }

    public string CommandPath { get; }
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

public sealed class DiscordEndpointResponse
{
    private static readonly IReadOnlyDictionary<string, string> EmptyViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private DiscordEndpointResponse(
        DiscordResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral)
    {
        Kind = kind;
        ViewKey = viewKey;
        ViewData = viewData;
        Ephemeral = ephemeral;
    }

    public DiscordResponseKind Kind { get; }

    public string ViewKey { get; }

    public IReadOnlyDictionary<string, string> ViewData { get; }

    public bool Ephemeral { get; }

    public static DiscordEndpointResponse Message(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral = true) =>
        Create(DiscordResponseKind.Message, viewKey, viewData, ephemeral);

    public static DiscordEndpointResponse UpdateMessage(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.UpdateMessage, viewKey, viewData, ephemeral: true);

    public static DiscordEndpointResponse Modal(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.Modal, viewKey, viewData, ephemeral: true);

    public static DiscordEndpointResponse Autocomplete(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(DiscordResponseKind.Autocomplete, viewKey, viewData, ephemeral: true);

    public static DiscordEndpointResponse NoContent() =>
        Create(DiscordResponseKind.NoContent, string.Empty, EmptyViewData, ephemeral: true);

    private static DiscordEndpointResponse Create(
        DiscordResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral)
    {
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentNullException.ThrowIfNull(viewData);
        return new DiscordEndpointResponse(kind, viewKey, viewData, ephemeral);
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
