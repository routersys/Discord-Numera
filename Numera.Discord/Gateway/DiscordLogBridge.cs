using Discord;
using Numera.Discord.Abstractions;

namespace Numera.Discord.Gateway;

internal static class DiscordLogBridge
{
    internal static DiscordDiagnosticSeverity Map(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => DiscordDiagnosticSeverity.Critical,
        LogSeverity.Error => DiscordDiagnosticSeverity.Error,
        LogSeverity.Warning => DiscordDiagnosticSeverity.Warning,
        LogSeverity.Info => DiscordDiagnosticSeverity.Information,
        LogSeverity.Verbose => DiscordDiagnosticSeverity.Verbose,
        _ => DiscordDiagnosticSeverity.Debug,
    };

    internal static void Forward(IDiscordDiagnostics diagnostics, LogMessage message)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        diagnostics.LibraryEvent(
            Map(message.Severity),
            message.Source ?? string.Empty,
            message.Message ?? string.Empty,
            message.Exception);
    }
}
