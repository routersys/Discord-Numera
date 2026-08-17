using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class DiscordClientConfigurationTests
{
    [TestMethod]
    public void SocketConfigMatchesTheCanonicalContract()
    {
        DiscordSocketConfig config = DiscordClientConfiguration.CreateSocketConfig();

        GatewayIntents expectedIntents = GatewayIntents.Guilds;
        int expectedCacheSize = 0;

        Assert.AreEqual(expectedIntents, config.GatewayIntents);
        Assert.IsFalse(config.AlwaysDownloadUsers);
        Assert.AreEqual(expectedCacheSize, config.MessageCacheSize);
        Assert.IsFalse(config.IncludeRawPayloadOnGatewayErrors);
        Assert.IsTrue(config.LogGatewayIntentWarnings);
    }

    [TestMethod]
    public void MessageContentIntentIsNotRequested()
    {
        DiscordSocketConfig config = DiscordClientConfiguration.CreateSocketConfig();

        Assert.AreEqual(GatewayIntents.None, config.GatewayIntents & GatewayIntents.MessageContent);
        Assert.AreEqual(GatewayIntents.None, config.GatewayIntents & GatewayIntents.GuildMembers);
        Assert.AreEqual(GatewayIntents.None, config.GatewayIntents & GatewayIntents.GuildPresences);
    }

    [TestMethod]
    public void PrivilegedIntentsAreAbsent()
    {
        DiscordSocketConfig config = DiscordClientConfiguration.CreateSocketConfig();

        GatewayIntents privileged =
            GatewayIntents.GuildMembers | GatewayIntents.GuildPresences | GatewayIntents.MessageContent;

        Assert.AreEqual(GatewayIntents.None, config.GatewayIntents & privileged);
    }

    [TestMethod]
    public void InteractionServiceConfigMatchesTheCanonicalContract()
    {
        InteractionServiceConfig config = DiscordClientConfiguration.CreateInteractionServiceConfig();

        RunMode expectedRunMode = RunMode.Async;

        Assert.AreEqual(expectedRunMode, config.DefaultRunMode);
        Assert.IsTrue(config.UseCompiledLambda);
        Assert.IsTrue(config.AutoServiceScopes);
        Assert.IsFalse(config.ThrowOnError);
    }

    [TestMethod]
    public void MentionsAreSuppressedByDefault()
    {
        AllowedMentions mentions = DiscordClientConfiguration.CanonicalAllowedMentions;

        Assert.IsNull(mentions.AllowedTypes);
        Assert.IsEmpty(mentions.UserIds!);
        Assert.IsEmpty(mentions.RoleIds!);
        Assert.AreNotEqual(AllowedMentions.All.AllowedTypes, mentions.AllowedTypes);
    }

    [TestMethod]
    public void PresenceIsOnlineWithTheCatalogActivityText()
    {
        UserStatus status = DiscordClientConfiguration.CanonicalStatus;

        Assert.AreEqual(UserStatus.Online, status);

        string activity = CanonicalTextCatalog.Create().Resolve(TextCatalogKeys.PresenceActivity);
        Assert.AreEqual("銀行システム", activity);
    }
}
