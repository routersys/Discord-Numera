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
                nameof(DiscordHostedService),
            },
            started);

        string[] stopped = [.. started.Reverse()];

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(DiscordHostedService),
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
}
