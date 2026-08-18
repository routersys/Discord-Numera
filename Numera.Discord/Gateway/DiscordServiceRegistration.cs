using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

public static class DiscordServiceRegistration
{
    public static IServiceCollection AddNumeraDiscord(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(DiscordClientConfiguration.CreateSocketConfig());
        services.AddSingleton(static provider =>
            new DiscordSocketClient(provider.GetRequiredService<DiscordSocketConfig>()));
        services.AddSingleton(static provider => new InteractionService(
            provider.GetRequiredService<DiscordSocketClient>(),
            DiscordClientConfiguration.CreateInteractionServiceConfig()));

        services.AddSingleton<ITextCatalog>(static _ => CanonicalTextCatalog.Create());
        services.AddSingleton(static provider => new ErrorRenderer(provider.GetRequiredService<ITextCatalog>()));
        services.AddSingleton<IDiscordResponseComposer, CatalogResponseComposer>();
        services.AddSingleton<IDiscordEndpointExecutor, DiscordEndpointExecutor>();
        services.AddSingleton<IGeneratedEndpointDispatcher, GeneratedEndpointDispatcher>();
        services.AddSingleton<IGeneratedModuleRegistrar, GeneratedModuleRegistrar>();
        services.AddSingleton<IAuthorizationResolver, AuthorizationResolver>();
        services.AddSingleton<Endpoints.AccountEndpoints>();
        services.AddSingleton<Endpoints.HelpEndpoints>();
        services.AddSingleton<Endpoints.BankEndpoints>();
        services.AddSingleton<Endpoints.ManageEndpoints>();
        services.AddSingleton<DiscordInteractionRouter>();
        services.TryAddSingleton<ICommandManifestProvider, GeneratedCommandManifestProvider>();
        services.AddSingleton<IApplicationCommandGateway>(static provider =>
            new RestApplicationCommandGateway(provider.GetRequiredService<DiscordSocketClient>()));
        services.AddSingleton<IApplicationCommandSynchronizer, ApplicationCommandSynchronizer>();
        services.AddSingleton<IDiscordGateway, DiscordGatewayConnection>();

        return services;
    }
}
