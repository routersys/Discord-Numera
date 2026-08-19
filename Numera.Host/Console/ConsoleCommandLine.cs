namespace Numera.Host.Console;

public enum ConsoleCommandKind
{
    Unknown = 0,
    ConfigShow = 1,
    ConfigApplicationIdSet = 2,
    ConfigTestGuildSet = 3,
    ConfigControlGuildSet = 4,
    ConfigRegistrationModeSet = 5,
    ConfigOwnerAdd = 6,
    ConfigOwnerRemove = 7,
    ConfigTokenSet = 8,
    ConfigTokenClear = 9,
    DiscordReconnect = 10,
    CommandsSync = 11,
    DatabaseVerify = 12,
    DatabaseBackup = 13,
    DatabaseBackupList = 14,
    DatabaseBackupVerify = 15,
    DatabaseRestore = 16,
    DatabaseRestoreLatest = 17,
    DatabaseRecoveryStatus = 18,
    Health = 19,
    Help = 20,
    Shutdown = 21,
    EconomyInit = 22,
}

public sealed record ConsoleCommand(ConsoleCommandKind Kind, string Argument)
{
    public static ConsoleCommand Unknown { get; } = new(ConsoleCommandKind.Unknown, string.Empty);
}

public static class ConsoleCommandLine
{
    public const string Prompt = "> ";

    public static ConsoleCommand Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return ConsoleCommand.Unknown;
        }

        string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts switch
        {
            ["config", "show"] => Simple(ConsoleCommandKind.ConfigShow),
            ["config", "application-id", "set", string id] => WithArgument(ConsoleCommandKind.ConfigApplicationIdSet, id),
            ["config", "test-guild", "set", string id] => WithArgument(ConsoleCommandKind.ConfigTestGuildSet, id),
            ["config", "control-guild", "set", string id] => WithArgument(ConsoleCommandKind.ConfigControlGuildSet, id),
            ["config", "registration-mode", "set", string mode] => WithArgument(ConsoleCommandKind.ConfigRegistrationModeSet, mode),
            ["config", "owner", "add", string id] => WithArgument(ConsoleCommandKind.ConfigOwnerAdd, id),
            ["config", "owner", "remove", string id] => WithArgument(ConsoleCommandKind.ConfigOwnerRemove, id),
            ["config", "token", "set"] => Simple(ConsoleCommandKind.ConfigTokenSet),
            ["config", "token", "clear"] => Simple(ConsoleCommandKind.ConfigTokenClear),
            ["discord", "reconnect"] => Simple(ConsoleCommandKind.DiscordReconnect),
            ["commands", "sync"] => Simple(ConsoleCommandKind.CommandsSync),
            ["database", "verify"] => Simple(ConsoleCommandKind.DatabaseVerify),
            ["database", "backup"] => Simple(ConsoleCommandKind.DatabaseBackup),
            ["database", "backup", "list"] => Simple(ConsoleCommandKind.DatabaseBackupList),
            ["database", "backup", "verify", string path] => WithArgument(ConsoleCommandKind.DatabaseBackupVerify, path),
            ["database", "restore", "latest"] => Simple(ConsoleCommandKind.DatabaseRestoreLatest),
            ["database", "restore", string path] => WithArgument(ConsoleCommandKind.DatabaseRestore, path),
            ["database", "recovery", "status"] => Simple(ConsoleCommandKind.DatabaseRecoveryStatus),
            ["economy", "init", string guild, string timezone, string capital] =>
                WithArgument(ConsoleCommandKind.EconomyInit, guild + " " + timezone + " " + capital),
            ["health"] => Simple(ConsoleCommandKind.Health),
            ["help"] => Simple(ConsoleCommandKind.Help),
            ["shutdown"] => Simple(ConsoleCommandKind.Shutdown),
            _ => ConsoleCommand.Unknown,
        };
    }

    public static bool AcceptsSecretFromPrompt(ConsoleCommandKind kind) =>
        kind == ConsoleCommandKind.ConfigTokenSet;

    private static ConsoleCommand Simple(ConsoleCommandKind kind) => new(kind, string.Empty);

    private static ConsoleCommand WithArgument(ConsoleCommandKind kind, string argument) => new(kind, argument);
}
