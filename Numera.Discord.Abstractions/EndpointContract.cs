namespace Numera.Discord.Abstractions;

public sealed class EconomyInvocation
{
    public EconomyInvocation(
        ulong discordUserId,
        ulong guildId,
        ulong channelId,
        string interactionId,
        string locale)
    {
        DiscordUserId = discordUserId;
        GuildId = guildId;
        ChannelId = channelId;
        InteractionId = interactionId;
        Locale = locale;
    }

    public ulong DiscordUserId { get; }

    public ulong GuildId { get; }

    public ulong ChannelId { get; }

    public string InteractionId { get; }

    public string Locale { get; }
}

public sealed class EconomyEndpointResponse
{
    private EconomyEndpointResponse(
        EconomyResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral)
    {
        Kind = kind;
        ViewKey = viewKey;
        ViewData = viewData;
        Ephemeral = ephemeral;
    }

    public EconomyResponseKind Kind { get; }

    public string ViewKey { get; }

    public IReadOnlyDictionary<string, string> ViewData { get; }

    public bool Ephemeral { get; }

    public static EconomyEndpointResponse Message(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral = true) =>
        Create(EconomyResponseKind.Message, viewKey, viewData, ephemeral);

    public static EconomyEndpointResponse UpdateMessage(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(EconomyResponseKind.UpdateMessage, viewKey, viewData, ephemeral: true);

    public static EconomyEndpointResponse Modal(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(EconomyResponseKind.Modal, viewKey, viewData, ephemeral: true);

    public static EconomyEndpointResponse Autocomplete(
        string viewKey,
        IReadOnlyDictionary<string, string> viewData) =>
        Create(EconomyResponseKind.Autocomplete, viewKey, viewData, ephemeral: true);

    public static EconomyEndpointResponse NoContent() =>
        Create(EconomyResponseKind.NoContent, string.Empty, EmptyViewData, ephemeral: true);

    private static readonly IReadOnlyDictionary<string, string> EmptyViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static EconomyEndpointResponse Create(
        EconomyResponseKind kind,
        string viewKey,
        IReadOnlyDictionary<string, string> viewData,
        bool ephemeral)
    {
        ArgumentNullException.ThrowIfNull(viewKey);
        ArgumentNullException.ThrowIfNull(viewData);
        return new EconomyEndpointResponse(kind, viewKey, viewData, ephemeral);
    }
}

public interface IEconomyEndpoint;

public sealed class EconomyAutocompleteOption
{
    public const int MinimumNameLength = 1;
    public const int MaximumNameLength = 100;
    public const int MaximumValueLength = 100;

    private EconomyAutocompleteOption(string name, string value)
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

    public static bool TryCreate(string? name, string? value, out EconomyAutocompleteOption? option)
    {
        if (!IsAcceptable(name, value))
        {
            option = null;
            return false;
        }

        option = new EconomyAutocompleteOption(name!, value!);
        return true;
    }

    public static EconomyAutocompleteOption Create(string name, string value) =>
        TryCreate(name, value, out EconomyAutocompleteOption? option)
            ? option!
            : throw new ArgumentException(AutocompleteFailure.OptionOutOfRange, nameof(name));
}

public static class AutocompleteFailure
{
    public const string OptionOutOfRange =
        "Autocomplete Option の名前は1文字以上100文字以下、値は100文字以下でなければなりません。";
}
