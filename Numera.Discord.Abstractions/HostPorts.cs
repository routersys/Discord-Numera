namespace Numera.Discord.Abstractions;

public enum DiscordDiagnosticSeverity
{
    Critical = 1,
    Error = 2,
    Warning = 3,
    Information = 4,
    Verbose = 5,
    Debug = 6,
}

public sealed record DiscordInteractionCorrelation(
    string CorrelationId,
    ulong InteractionId,
    ulong GuildId,
    ulong UserId);

public sealed record DiscordCommandSyncOutcome(int Created, int Edited, int Deleted, int Unchanged);

public sealed record DiscordCommandRegistrationOptions(
    bool UseGuildRegistration,
    ulong TestGuildId,
    ulong ControlGuildId);

public interface IDiscordDiagnostics
{
    IDisposable BeginInteractionScope(DiscordInteractionCorrelation correlation);

    void GatewayReady(ulong applicationId);

    void GatewayDisconnected(Exception? exception);

    void InteractionReceived(string commandPath);

    void InteractionFailed(string errorCode);

    void InteractionFaulted(Exception exception);

    void CommandSyncCompleted(DiscordCommandSyncOutcome outcome);

    void CommandSyncFailed(Exception exception);

    void LibraryEvent(DiscordDiagnosticSeverity severity, string source, string message, Exception? exception);
}

public interface IDiscordGateway
{
    Task LoginAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IDiscordCredentialProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken);

    void Clear();
}
