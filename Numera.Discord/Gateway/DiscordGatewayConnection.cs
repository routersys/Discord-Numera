using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Numera.Discord.Abstractions;
using Numera.Discord.Rendering;

namespace Numera.Discord.Gateway;

internal sealed class DiscordGatewayConnection : IDiscordGateway
{
    private readonly DiscordSocketClient client;
    private readonly InteractionService interactionService;
    private readonly DiscordInteractionRouter router;
    private readonly IDiscordCredentialProvider credentials;
    private readonly IDiscordDiagnostics diagnostics;
    private readonly ITextCatalog catalog;

    private bool subscribed;

    internal DiscordGatewayConnection(
        DiscordSocketClient client,
        InteractionService interactionService,
        DiscordInteractionRouter router,
        IDiscordCredentialProvider credentials,
        IDiscordDiagnostics diagnostics,
        ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(interactionService);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(catalog);

        this.client = client;
        this.interactionService = interactionService;
        this.router = router;
        this.credentials = credentials;
        this.diagnostics = diagnostics;
        this.catalog = catalog;
    }

    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        Subscribe();

        string token = await credentials.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        await client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();

        await client.StopAsync().ConfigureAwait(false);
        await client.LogoutAsync().ConfigureAwait(false);
    }

    internal void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        subscribed = true;

        client.InteractionCreated += OnInteractionCreatedAsync;
        client.Ready += OnReadyAsync;
        client.Disconnected += OnDisconnectedAsync;
        client.Log += OnLibraryLogAsync;
        interactionService.Log += OnLibraryLogAsync;
        interactionService.InteractionExecuted += OnInteractionExecutedAsync;
    }

    internal void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        subscribed = false;

        client.InteractionCreated -= OnInteractionCreatedAsync;
        client.Ready -= OnReadyAsync;
        client.Disconnected -= OnDisconnectedAsync;
        client.Log -= OnLibraryLogAsync;
        interactionService.Log -= OnLibraryLogAsync;
        interactionService.InteractionExecuted -= OnInteractionExecutedAsync;
    }

    private Task OnInteractionCreatedAsync(SocketInteraction interaction) => router.RouteAsync(client, interaction);

    private Task OnInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result) =>
        router.HandleExecutedAsync(context.Interaction, result);

    private async Task OnReadyAsync()
    {
        await client
            .SetGameAsync(
                catalog.Resolve(TextCatalogKeys.PresenceActivity),
                streamUrl: null,
                DiscordClientConfiguration.CanonicalActivityType)
            .ConfigureAwait(false);

        await client.SetStatusAsync(DiscordClientConfiguration.CanonicalStatus).ConfigureAwait(false);

        diagnostics.GatewayReady(client.CurrentUser?.Id ?? 0UL);
    }

    private Task OnDisconnectedAsync(Exception? exception)
    {
        diagnostics.GatewayDisconnected(exception);

        return Task.CompletedTask;
    }

    private Task OnLibraryLogAsync(LogMessage message)
    {
        DiscordLogBridge.Forward(diagnostics, message);

        return Task.CompletedTask;
    }
}
