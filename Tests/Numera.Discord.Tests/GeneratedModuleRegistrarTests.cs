using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Numera.Discord.Gateway;
using Numera.Discord.Routing;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class GeneratedModuleRegistrarTests
{
    private sealed class ScopedServices : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this : null;

        public void Dispose()
        {
        }
    }

    private static IServiceProvider CreateServices() => new ScopedServices();

    private static InteractionService CreateInteractionService() =>
        new(new DiscordSocketClient(DiscordClientConfiguration.CreateSocketConfig()),
            DiscordClientConfiguration.CreateInteractionServiceConfig());

    [TestMethod]
    public void TheGeneratedListIsTheOnlySource()
    {
        GeneratedModuleRegistrar registrar = new(CreateServices());

        CollectionAssert.AreEqual(EconomyGeneratedModules.All, registrar.Modules.ToArray());
    }

    [TestMethod]
    public async Task AnEmptyListRegistersNothing()
    {
        using InteractionService interactionService = CreateInteractionService();
        GeneratedModuleRegistrar registrar = new(CreateServices(), []);

        await registrar.RegisterAsync(interactionService, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(0, interactionService.Modules.Count);
    }

    [TestMethod]
    public async Task RegistrationHappensOnlyOnce()
    {
        using InteractionService interactionService = CreateInteractionService();
        GeneratedModuleRegistrar registrar = new(CreateServices(), [typeof(ProbeModule)]);

        await registrar.RegisterAsync(interactionService, TestContext.CancellationTokenSource.Token);
        int afterFirst = interactionService.Modules.Count;

        await registrar.RegisterAsync(interactionService, TestContext.CancellationTokenSource.Token);

        Assert.IsGreaterThan(0, afterFirst);
        Assert.AreEqual(afterFirst, interactionService.Modules.Count);
    }

    [TestMethod]
    public async Task ANestedModuleIsRegisteredWithItsParent()
    {
        using InteractionService interactionService = CreateInteractionService();
        GeneratedModuleRegistrar registrar = new(CreateServices(), [typeof(ProbeModule)]);

        await registrar.RegisterAsync(interactionService, TestContext.CancellationTokenSource.Token);

        ModuleInfo parent = interactionService.Modules
            .Single(module => string.Equals(module.SlashGroupName, "probe", StringComparison.Ordinal));

        Assert.AreEqual(1, parent.SubModules.Count);
        Assert.AreEqual("nested", parent.SubModules.Single().SlashGroupName);
        Assert.AreEqual("pong", parent.SubModules.Single().SlashCommands.Single().Name);
    }

    public TestContext TestContext { get; set; } = null!;
}

[Group("probe", "検証用のグループです。")]
public sealed class ProbeModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "検証用のコマンドです。")]
    public Task PingAsync() => Task.CompletedTask;

    [Group("nested", "検証用の入れ子グループです。")]
    public sealed class NestedProbeModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("pong", "検証用の入れ子コマンドです。")]
        public Task PongAsync() => Task.CompletedTask;
    }
}
