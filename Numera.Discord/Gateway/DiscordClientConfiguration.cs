using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Numera.Discord.Gateway;

internal static class DiscordClientConfiguration
{
    internal const GatewayIntents CanonicalIntents = GatewayIntents.Guilds;
    internal const int CanonicalMessageCacheSize = 0;
    internal const UserStatus CanonicalStatus = UserStatus.Online;
    internal const ActivityType CanonicalActivityType = ActivityType.Playing;

    internal static DiscordSocketConfig CreateSocketConfig() => new()
    {
        GatewayIntents = CanonicalIntents,
        AlwaysDownloadUsers = false,
        MessageCacheSize = CanonicalMessageCacheSize,
        IncludeRawPayloadOnGatewayErrors = false,
        LogGatewayIntentWarnings = true,
    };

    internal static InteractionServiceConfig CreateInteractionServiceConfig() => new()
    {
        DefaultRunMode = RunMode.Async,
        UseCompiledLambda = true,
        AutoServiceScopes = true,
        ThrowOnError = false,
    };

    internal static AllowedMentions CanonicalAllowedMentions => AllowedMentions.None;
}
