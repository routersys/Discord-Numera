using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Host.Composition;
using Numera.Host.Configuration;
using Numera.Host.Discord;
using Numera.Host.Logging;
using Numera.Host.Startup;
using Numera.Host.Workers;
using Numera.Persistence.Sqlite;

namespace Numera.Host;

internal static class NumeraHostExitCode
{
    internal const int Success = 0;
    internal const int StartupFailed = 1;
    internal const int RecoveryRequired = 2;
}

internal static class NumeraHost
{
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(StartupSequence.ShutdownBudgetSeconds);

    internal static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        BootstrapFatalWriter fatal = BootstrapFatalWriter.Standard();
        SingleInstanceLock? instanceLock = null;
        RuntimeStateMarker? marker = null;

        try
        {
            HostApplicationBuilder builder =
                Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
            BankingConsoleLogging.Configure(builder.Logging);

            StartupComposer composer = new(
                builder.Configuration,
                new ConfigurationOnlyBootstrapSettingsStore(),
                TimeProvider.System);

            StartupSequenceReport report = StartupSequence.Execute(composer.Bind(builder.Environment.EnvironmentName));
            instanceLock = composer.Lock;
            marker = composer.Marker;

            if (!report.Succeeded)
            {
                fatal.Write(
                    BootstrapFatalEvents.ConfigurationInvalidId,
                    BootstrapFatalEvents.ConfigurationInvalidName,
                    "Startup stopped before the Discord connection.",
                    report.Detail);

                return NumeraHostExitCode.StartupFailed;
            }

            if (composer.EffectiveOptions is not NumeraOptions options)
            {
                fatal.Write(
                    BootstrapFatalEvents.ConfigurationInvalidId,
                    BootstrapFatalEvents.ConfigurationInvalidName,
                    "The effective runtime options were not resolved.");

                return NumeraHostExitCode.StartupFailed;
            }

            StartupRecoveryReport recovery = StartupRecoveryMachine.Run(
                composer.BindRecovery(static () => StartupCheckResult.Passed));

            if (!MayStartHost(recovery))
            {
                fatal.Write(
                    BootstrapFatalEvents.RecoveryRequiredId,
                    BootstrapFatalEvents.RecoveryRequiredName,
                    "Startup entered RECOVERY_REQUIRED and neither Discord nor write admission was started.",
                    recovery.Detail);

                return NumeraHostExitCode.RecoveryRequired;
            }

            Configure(builder, options);

            using IHost host = builder.Build();
            await host.RunAsync().ConfigureAwait(false);

            marker?.WriteCleanShutdown();

            return NumeraHostExitCode.Success;
        }
        catch (PersistenceFailureException exception)
        {
            fatal.Write(
                BootstrapFatalEvents.DatabaseUnavailableId,
                BootstrapFatalEvents.DatabaseUnavailableName,
                "The database could not be prepared for startup.",
                exception.Code);

            return NumeraHostExitCode.RecoveryRequired;
        }
        catch (Exception exception)
        {
            fatal.Write(
                BootstrapFatalEvents.UnexpectedId,
                BootstrapFatalEvents.UnexpectedName,
                "Startup failed before the composition root was built.",
                exception);

            return NumeraHostExitCode.StartupFailed;
        }
        finally
        {
            instanceLock?.Dispose();
        }
    }

    internal static bool MayStartHost(StartupRecoveryReport recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        return recovery.Outcome == StartupRecoveryOutcome.Recovered
            && recovery.MayAcceptFinancialWrites
            && recovery.MayConnectDiscord;
    }

    internal static void Configure(HostApplicationBuilder builder, NumeraOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = ShutdownTimeout);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(RegistrationOptions(options));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDiscordDiagnostics, DiscordDiagnostics>();
        builder.Services.AddSingleton<IMaintenanceDiagnostics, MaintenanceDiagnostics>();
        builder.Services.AddSingleton<IDiscordCredentialProvider, EnvironmentDiscordCredentialProvider>();
        builder.Services.AddNumeraDiscord();
        builder.Services.AddNumeraBanking(options);

        builder.Services.AddHostedService<DiscordGatewayShutdownService>();
        builder.Services.AddHostedService<SqliteWriteAdmissionService>();
        builder.Services.AddHostedService<SettlementMaintenanceWorker>();
        builder.Services.AddHostedService<DiscordHostedService>();
    }

    internal static DiscordCommandRegistrationOptions RegistrationOptions(NumeraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new DiscordCommandRegistrationOptions(
            options.RegistrationMode == CommandRegistrationMode.Guild,
            options.TestGuildId,
            options.ControlGuildId);
    }
}
