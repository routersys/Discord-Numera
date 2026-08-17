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
    public EconomyAutocompleteOption(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}
