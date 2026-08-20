using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Numera.Application.Banking;
using Numera.Discord.Abstractions;
using Numera.Host.Configuration;
using Numera.Host.Discord;
using Numera.Host.Workers;

namespace Numera.Host.Tests;

[TestClass]
public sealed class HostCompositionTests
{
    private static NumeraOptions CanonicalOptions() => new(
        HostEnvironmentKind.Production,
        ApplicationId: 1,
        TestGuildId: 0,
        ControlGuildId: 3,
        CommandRegistrationMode.Global,
        [10UL],
        NumeraOptionsValidator.CanonicalDatabasePath,
        NumeraOptionsValidator.CanonicalBusyTimeoutSeconds,
        NumeraOptionsValidator.CanonicalInteractionSessionMinutes,
        NumeraOptionsValidator.CanonicalStatementPageSize);

    private static IHost Build()
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        NumeraHost.Configure(builder, CanonicalOptions());

        return builder.Build();
    }

    [TestMethod]
    public void EveryRegisteredServiceResolves()
    {
        using IHost host = Build();

        Assert.IsNotNull(host.Services.GetRequiredService<IDiscordGateway>());
        Assert.IsNotNull(host.Services.GetRequiredService<IDiscordDiagnostics>());
        Assert.IsNotNull(host.Services.GetRequiredService<IDiscordCredentialProvider>());
        Assert.IsNotNull(host.Services.GetRequiredService<ISettlementMaintenanceRunner>());
        Assert.IsNotNull(host.Services.GetRequiredService<SettlementMaintenanceService>());
        Assert.IsNotNull(host.Services.GetRequiredService<PaymentApplicationService>());
    }

    [TestMethod]
    public async Task EveryGeneratedModuleIsAcceptedByTheInteractionService()
    {
        using IHost host = Build();

        InteractionService interactions = host.Services.GetRequiredService<InteractionService>();

        await host.Services.GetRequiredService<Numera.Discord.Gateway.IGeneratedModuleRegistrar>()
            .RegisterAsync(interactions, TestContext.CancellationTokenSource.Token);

        CollectionAssert.AreEqual(
            new[]
            {
                "bank:v1:btn:bank-activate:*",
                "bank:v1:btn:bank-capital-commit:*",
                "bank:v1:btn:bank-capital-input:*",
                "bank:v1:btn:bank-create-commit:*",
                "bank:v1:btn:bank-create-input:*",
                "bank:v1:btn:bank-loan-commit:*",
                "bank:v1:btn:bank-loan-input:*",
                "bank:v1:btn:transfer-execute:*",
                "bank:v1:btn:transfer-input:*",
                "bank:v1:sel:bank-detail:*",
                "bank:v1:sel:panel-action:*",
                "bank:v1:sel:panel-category:*",
                "bank:v1:sel:transfer-source:*",
            },
            interactions.ComponentCommands.Select(static command => command.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                "bank:v1:modal:bank-capital:*",
                "bank:v1:modal:bank-create:*",
                "bank:v1:modal:bank-loan:*",
                "bank:v1:modal:transfer:*",
            },
            interactions.ModalCommands.Select(static command => command.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        ModalCommandInfo modal = interactions.ModalCommands
            .Single(static command => string.Equals(
                command.Name, "bank:v1:modal:transfer:*", StringComparison.Ordinal));

        CollectionAssert.AreEqual(
            new[] { "bank-code", "branch-code", "account-number", "amount", "memo" },
            modal.Modal.TextInputComponents.Select(static component => component.CustomId).ToArray());

        ModalCommandInfo bankCreate = interactions.ModalCommands
            .Single(static command => string.Equals(
                command.Name, "bank:v1:modal:bank-create:*", StringComparison.Ordinal));

        CollectionAssert.AreEqual(
            new[] { "bank-name", "branch-code", "branch-name", "product-code", "product-name" },
            bankCreate.Modal.TextInputComponents
                .Select(static component => component.CustomId).ToArray());
    }

    [TestMethod]
    public void TheHostedServicesStopInTheOrderTheShutdownContractRequires()
    {
        using IHost host = Build();

        string[] started = [.. host.Services.GetServices<IHostedService>()
            .Select(static service => service.GetType().Name)];

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(DiscordGatewayShutdownService),
                nameof(SqliteWriteAdmissionService),
                nameof(SettlementMaintenanceWorker),
                nameof(AutomaticBackupWorker),
                nameof(DiscordHostedService),
            },
            started);

        string[] stopped = [.. started.Reverse()];

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(DiscordHostedService),
                nameof(AutomaticBackupWorker),
                nameof(SettlementMaintenanceWorker),
                nameof(SqliteWriteAdmissionService),
                nameof(DiscordGatewayShutdownService),
            },
            stopped);
    }

    [TestMethod]
    public void TheShutdownTimeoutIsFixedToTheCanonicalBudget()
    {
        using IHost host = Build();

        HostOptions options = host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HostOptions>>().Value;

        Assert.AreEqual(NumeraHost.ShutdownTimeout, options.ShutdownTimeout);
    }

    [TestMethod]
    public void GuildRegistrationIsDerivedFromTheRegistrationMode()
    {
        DiscordCommandRegistrationOptions global = NumeraHost.RegistrationOptions(CanonicalOptions());

        Assert.IsFalse(global.UseGuildRegistration);

        DiscordCommandRegistrationOptions guild = NumeraHost.RegistrationOptions(CanonicalOptions() with
        {
            Environment = HostEnvironmentKind.Development,
            RegistrationMode = CommandRegistrationMode.Guild,
            TestGuildId = 2,
        });

        Assert.IsTrue(guild.UseGuildRegistration);
        Assert.AreEqual(2UL, guild.TestGuildId);
    }

    public TestContext TestContext { get; set; } = null!;
}
