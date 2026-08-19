namespace Numera.Host.Configuration;

public enum CommandRegistrationMode
{
    Guild = 1,
    Global = 2,
}

public enum HostEnvironmentKind
{
    Development = 1,
    Production = 2,
}

public sealed record NumeraOptions(
    HostEnvironmentKind Environment,
    ulong ApplicationId,
    ulong TestGuildId,
    ulong ControlGuildId,
    CommandRegistrationMode RegistrationMode,
    IReadOnlyList<ulong> SystemOwnerDiscordUserIds,
    string DatabasePath,
    int DatabaseBusyTimeoutSeconds,
    int InteractionSessionMinutes,
    int StatementPageSize,
    string? SecondaryBackupDirectory = null);

public sealed record OptionsViolation(string Setting, string Code);

public static class OptionsValidationCode
{
    public const string ApplicationIdInvalid = "DISCORD_APPLICATION_ID_INVALID";
    public const string ControlGuildIdInvalid = "DISCORD_CONTROL_GUILD_ID_INVALID";
    public const string TestGuildIdInvalid = "DISCORD_TEST_GUILD_ID_INVALID";
    public const string RegistrationModeNotAllowed = "DISCORD_REGISTRATION_MODE_NOT_ALLOWED";
    public const string SystemOwnerMissing = "SECURITY_SYSTEM_OWNER_MISSING";
    public const string SystemOwnerInvalid = "SECURITY_SYSTEM_OWNER_INVALID";
    public const string SystemOwnerDuplicated = "SECURITY_SYSTEM_OWNER_DUPLICATED";
    public const string DatabasePathNotCanonical = "DATABASE_PATH_NOT_CANONICAL";
    public const string BusyTimeoutNotCanonical = "DATABASE_BUSY_TIMEOUT_NOT_CANONICAL";
    public const string SessionMinutesNotCanonical = "BANKING_SESSION_MINUTES_NOT_CANONICAL";
    public const string StatementPageSizeNotCanonical = "BANKING_STATEMENT_PAGE_SIZE_NOT_CANONICAL";
}

public static class NumeraOptionsValidator
{
    public const string CanonicalDatabasePath = "data/economy.db";
    public const int CanonicalBusyTimeoutSeconds = 5;
    public const int CanonicalInteractionSessionMinutes = 15;
    public const int CanonicalStatementPageSize = 8;

    public static IReadOnlyList<OptionsViolation> Validate(NumeraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<OptionsViolation> violations = [];

        if (options.ApplicationId == 0)
        {
            violations.Add(new OptionsViolation("Discord:ApplicationId", OptionsValidationCode.ApplicationIdInvalid));
        }

        if (options.ControlGuildId == 0)
        {
            violations.Add(new OptionsViolation("Discord:ControlGuildId", OptionsValidationCode.ControlGuildIdInvalid));
        }

        if (options.RegistrationMode == CommandRegistrationMode.Guild && options.TestGuildId == 0)
        {
            violations.Add(new OptionsViolation("Discord:TestGuildId", OptionsValidationCode.TestGuildIdInvalid));
        }

        CommandRegistrationMode required = options.Environment == HostEnvironmentKind.Development
            ? CommandRegistrationMode.Guild
            : CommandRegistrationMode.Global;

        if (options.RegistrationMode != required)
        {
            violations.Add(new OptionsViolation(
                "Discord:CommandRegistrationMode", OptionsValidationCode.RegistrationModeNotAllowed));
        }

        ValidateSystemOwners(options.SystemOwnerDiscordUserIds, violations);

        if (!string.Equals(options.DatabasePath, CanonicalDatabasePath, StringComparison.Ordinal))
        {
            violations.Add(new OptionsViolation("Database:Path", OptionsValidationCode.DatabasePathNotCanonical));
        }

        if (options.DatabaseBusyTimeoutSeconds != CanonicalBusyTimeoutSeconds)
        {
            violations.Add(new OptionsViolation(
                "Database:BusyTimeoutSeconds", OptionsValidationCode.BusyTimeoutNotCanonical));
        }

        if (options.InteractionSessionMinutes != CanonicalInteractionSessionMinutes)
        {
            violations.Add(new OptionsViolation(
                "Banking:InteractionSessionMinutes", OptionsValidationCode.SessionMinutesNotCanonical));
        }

        if (options.StatementPageSize != CanonicalStatementPageSize)
        {
            violations.Add(new OptionsViolation(
                "Banking:StatementPageSize", OptionsValidationCode.StatementPageSizeNotCanonical));
        }

        return violations;
    }

    public static bool TryParseRegistrationMode(string? candidate, out CommandRegistrationMode mode)
    {
        switch (candidate)
        {
            case "Guild":
                mode = CommandRegistrationMode.Guild;
                return true;
            case "Global":
                mode = CommandRegistrationMode.Global;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static bool TryParseEnvironment(string? candidate, out HostEnvironmentKind environment)
    {
        switch (candidate)
        {
            case "Development":
                environment = HostEnvironmentKind.Development;
                return true;
            case "Production":
                environment = HostEnvironmentKind.Production;
                return true;
            default:
                environment = default;
                return false;
        }
    }

    private static void ValidateSystemOwners(IReadOnlyList<ulong> owners, List<OptionsViolation> violations)
    {
        if (owners.Count == 0)
        {
            violations.Add(new OptionsViolation(
                "Security:SystemOwnerDiscordUserIds", OptionsValidationCode.SystemOwnerMissing));
            return;
        }

        HashSet<ulong> seen = [];

        foreach (ulong owner in owners)
        {
            if (owner == 0)
            {
                violations.Add(new OptionsViolation(
                    "Security:SystemOwnerDiscordUserIds", OptionsValidationCode.SystemOwnerInvalid));
                continue;
            }

            if (!seen.Add(owner))
            {
                violations.Add(new OptionsViolation(
                    "Security:SystemOwnerDiscordUserIds", OptionsValidationCode.SystemOwnerDuplicated));
            }
        }
    }
}
