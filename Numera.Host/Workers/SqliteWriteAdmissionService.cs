using Microsoft.Extensions.Hosting;
using Numera.Host.Logging;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Host.Workers;

internal sealed class SqliteWriteAdmissionService : IHostedService
{
    private readonly SqliteWriteCoordinator coordinator;
    private readonly IMaintenanceDiagnostics diagnostics;

    public SqliteWriteAdmissionService(
        SqliteWriteCoordinator coordinator,
        IMaintenanceDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(diagnostics);

        this.coordinator = coordinator;
        this.diagnostics = diagnostics;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        coordinator.Start();
        diagnostics.WriteAdmissionOpened();

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        coordinator.CloseForegroundAdmission();
        coordinator.CloseBackgroundAdmission();

        await coordinator.DrainAsync().ConfigureAwait(false);

        diagnostics.WriteAdmissionClosed();
    }
}

internal sealed class DiscordGatewayShutdownService : IHostedService
{
    private readonly Numera.Discord.Abstractions.IDiscordGateway gateway;

    public DiscordGatewayShutdownService(Numera.Discord.Abstractions.IDiscordGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        this.gateway = gateway;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => gateway.StopAsync(cancellationToken);
}
