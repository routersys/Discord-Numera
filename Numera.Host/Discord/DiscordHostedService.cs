using Microsoft.Extensions.Hosting;
using Numera.Discord.Abstractions;

namespace Numera.Host.Discord;

internal sealed class DiscordHostedService : IHostedService
{
    private readonly IDiscordGateway gateway;

    internal DiscordHostedService(IDiscordGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        this.gateway = gateway;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await gateway.LoginAsync(cancellationToken).ConfigureAwait(false);
        await gateway.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => gateway.StopAsync(cancellationToken);
}
