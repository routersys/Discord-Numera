using Discord.Interactions;
using Numera.Discord.Routing;

namespace Numera.Discord.Gateway;

internal interface IGeneratedModuleRegistrar
{
    Task RegisterAsync(InteractionService interactionService, CancellationToken cancellationToken);
}

internal sealed class GeneratedModuleRegistrar : IGeneratedModuleRegistrar
{
    private readonly IServiceProvider services;
    private readonly IReadOnlyList<Type> modules;

    private int registered;

    public GeneratedModuleRegistrar(IServiceProvider services)
        : this(services, EconomyGeneratedModules.All)
    {
    }

    internal GeneratedModuleRegistrar(IServiceProvider services, IReadOnlyList<Type> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        this.services = services;
        this.modules = modules;
    }

    internal IReadOnlyList<Type> Modules => modules;

    public async Task RegisterAsync(InteractionService interactionService, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interactionService);

        if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
        {
            return;
        }

        foreach (Type module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await interactionService.AddModuleAsync(module, services).ConfigureAwait(false);
        }
    }
}
