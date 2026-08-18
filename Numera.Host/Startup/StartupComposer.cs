using Microsoft.Extensions.Configuration;
using Numera.Application.Common;
using Numera.Host.Configuration;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Host.Startup;

internal interface IHostBootstrapSettingsStore
{
    StartupCheckResult Load();
}

internal sealed class ConfigurationOnlyBootstrapSettingsStore : IHostBootstrapSettingsStore
{
    public StartupCheckResult Load() => StartupCheckResult.NotAvailable;
}

internal sealed record StartupAssets(
    SqliteDatabaseOptions DatabaseOptions,
    SingleInstanceLock? Lock,
    MigrationOutcome Migration);

internal sealed class StartupComposer
{
    internal const string ConfigurationSectionDiscord = "Discord";
    internal const string ConfigurationSectionDatabase = "Database";
    internal const string ConfigurationSectionBanking = "Banking";
    internal const string ConfigurationSectionSecurity = "Security";

    private readonly IConfiguration configuration;
    private readonly IHostBootstrapSettingsStore bootstrapSettings;
    private readonly TimeProvider timeProvider;

    private SqliteDatabaseOptions? databaseOptions;
    private SingleInstanceLock? instanceLock;
    private SqliteConnectionFactory? connectionFactory;
    private RuntimeStateMarker? marker;
    private NumeraOptions? effectiveOptions;

    internal StartupComposer(
        IConfiguration configuration,
        IHostBootstrapSettingsStore bootstrapSettings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(bootstrapSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.configuration = configuration;
        this.bootstrapSettings = bootstrapSettings;
        this.timeProvider = timeProvider;
    }

    internal SingleInstanceLock? Lock => instanceLock;

    internal NumeraOptions? EffectiveOptions => effectiveOptions;

    internal RuntimeStateMarker? Marker => marker;

    internal static NumeraOptions ReadOptions(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        HostEnvironmentKind environment =
            NumeraOptionsValidator.TryParseEnvironment(environmentName, out HostEnvironmentKind parsed)
                ? parsed
                : HostEnvironmentKind.Production;

        NumeraOptionsValidator.TryParseRegistrationMode(
            configuration[$"{ConfigurationSectionDiscord}:CommandRegistrationMode"],
            out CommandRegistrationMode mode);

        return new NumeraOptions(
            environment,
            ReadUInt64(configuration, $"{ConfigurationSectionDiscord}:ApplicationId"),
            ReadUInt64(configuration, $"{ConfigurationSectionDiscord}:TestGuildId"),
            ReadUInt64(configuration, $"{ConfigurationSectionDiscord}:ControlGuildId"),
            mode,
            ReadOwners(configuration),
            configuration[$"{ConfigurationSectionDatabase}:Path"] ?? NumeraOptionsValidator.CanonicalDatabasePath,
            ReadInt32(
                configuration,
                $"{ConfigurationSectionDatabase}:BusyTimeoutSeconds",
                NumeraOptionsValidator.CanonicalBusyTimeoutSeconds),
            ReadInt32(
                configuration,
                $"{ConfigurationSectionBanking}:InteractionSessionMinutes",
                NumeraOptionsValidator.CanonicalInteractionSessionMinutes),
            ReadInt32(
                configuration,
                $"{ConfigurationSectionBanking}:StatementPageSize",
                NumeraOptionsValidator.CanonicalStatementPageSize));
    }

    internal IReadOnlyList<StartupStepBinding> Bind(string environmentName) =>
    [
        new StartupStepBinding(StartupStep.BaseConfigurationBuild, BuildBaseConfiguration),
        new StartupStepBinding(
            StartupStep.DatabaseBootstrapOptionsValidation,
            () => ValidateBootstrapOptions(environmentName)),
        new StartupStepBinding(StartupStep.SingleInstanceLock, AcquireLock),
        new StartupStepBinding(StartupStep.SqliteDirectory, EnsureDirectory),
        new StartupStepBinding(StartupStep.SqliteConnectionAndPragma, VerifyConnection),
        new StartupStepBinding(StartupStep.DatabaseMigration, ApplyMigrations),
        new StartupStepBinding(StartupStep.HostSettingsLoad, bootstrapSettings.Load),
        new StartupStepBinding(StartupStep.BootstrapShellResolution, ResolveBootstrapShell),
        new StartupStepBinding(
            StartupStep.EffectiveRuntimeOptionsValidation,
            () => ValidateEffectiveOptions(environmentName)),
        new StartupStepBinding(StartupStep.PragmaQuickCheck, QuickCheck),
        new StartupStepBinding(StartupStep.ReconciliationStartupCheck, Reconciliation),
    ];

    internal StartupRecoverySteps BindRecovery(Func<StartupCheckResult> connectDiscord) => new(
        AcquireSingleInstanceLock: static () => StartupCheckResult.Passed,
        ReadPreviousRuntimeMarker: ReadPreviousMarker,
        WriteRunningMarker: WriteRunningMarker,
        OpenMainDatabase: VerifyConnection,
        VerifySchemaVersion: VerifySchemaVersion,
        QuickCheck: QuickCheck,
        ForeignKeyCheck: ForeignKeyCheck,
        IntegrityCheck: IntegrityCheck,
        FinancialReconciliation: Reconciliation,
        RecoverIncompleteWork: static () => StartupCheckResult.NotAvailable,
        VerifyNoOrphanState: static () => StartupCheckResult.NotAvailable,
        EnableWriteAdmission: static () => StartupCheckResult.Passed,
        StartBackgroundWorkers: static () => StartupCheckResult.Passed,
        ConnectDiscord: connectDiscord);

    private static IReadOnlyList<ulong> ReadOwners(IConfiguration configuration)
    {
        List<ulong> owners = [];

        foreach (IConfigurationSection section in
            configuration.GetSection($"{ConfigurationSectionSecurity}:SystemOwnerDiscordUserIds").GetChildren())
        {
            if (ulong.TryParse(section.Value, out ulong owner))
            {
                owners.Add(owner);
            }
        }

        return owners;
    }

    private static ulong ReadUInt64(IConfiguration configuration, string key) =>
        ulong.TryParse(configuration[key], out ulong value) ? value : 0UL;

    private static int ReadInt32(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out int value) ? value : fallback;

    private StartupCheckResult BuildBaseConfiguration() =>
        configuration.GetChildren().Any() ? StartupCheckResult.Passed : StartupCheckResult.NotAvailable;

    private StartupCheckResult ValidateBootstrapOptions(string environmentName)
    {
        NumeraOptions options = ReadOptions(configuration, environmentName);

        try
        {
            databaseOptions =
                SqliteDatabaseOptions.Create(options.DatabasePath, options.DatabaseBusyTimeoutSeconds);
        }
        catch (PersistenceFailureException exception)
        {
            return StartupCheckResult.Failed(exception.Code);
        }

        connectionFactory = new SqliteConnectionFactory(databaseOptions);
        marker = new RuntimeStateMarker(
            RuntimeStateMarker.PathFor(databaseOptions.DirectoryPath ?? "."),
            timeProvider);

        return StartupCheckResult.Passed;
    }

    private StartupCheckResult AcquireLock()
    {
        if (databaseOptions is null)
        {
            return StartupCheckResult.Failed(BankingErrorCodes.SystemBusy);
        }

        Directory.CreateDirectory(databaseOptions.DirectoryPath ?? ".");

        try
        {
            instanceLock = SingleInstanceLock.Acquire(databaseOptions);

            return StartupCheckResult.Passed;
        }
        catch (PersistenceFailureException exception)
        {
            return StartupCheckResult.Failed(exception.Code);
        }
    }

    private StartupCheckResult EnsureDirectory()
    {
        if (databaseOptions is null || connectionFactory is null)
        {
            return StartupCheckResult.Failed(BankingErrorCodes.SystemBusy);
        }

        Initializer().EnsureDirectory();

        DirectoryProtectionResult protection = DataDirectoryProtection.Apply(
            databaseOptions.DirectoryPath ?? ".",
            databaseOptions.BackupDirectoryPath);

        return protection.IsApplied
            ? StartupCheckResult.Passed
            : StartupCheckResult.Failed(protection.Detail);
    }

    private StartupCheckResult VerifyConnection() => Guarded(static initializer =>
    {
        initializer.VerifyRuntimeReadiness();

        return StartupCheckResult.Passed;
    });

    private StartupCheckResult ApplyMigrations() => Guarded(initializer =>
    {
        initializer.Initialize(timeProvider.GetUtcNow().ToUnixTimeMilliseconds());

        return StartupCheckResult.Passed;
    });

    private StartupCheckResult VerifySchemaVersion() => Guarded(static initializer =>
    {
        initializer.VerifyRuntimeReadiness();

        return StartupCheckResult.Passed;
    });

    private StartupCheckResult QuickCheck() => Guarded(static initializer =>
    {
        initializer.VerifyRuntimeReadiness();

        return StartupCheckResult.Passed;
    });

    private StartupCheckResult Reconciliation() => StartupCheckResult.NotAvailable;

    private StartupCheckResult ResolveBootstrapShell() => StartupCheckResult.NotAvailable;

    private StartupCheckResult ValidateEffectiveOptions(string environmentName)
    {
        NumeraOptions options = ReadOptions(configuration, environmentName);
        IReadOnlyList<OptionsViolation> violations = NumeraOptionsValidator.Validate(options);

        if (violations.Count > 0)
        {
            return StartupCheckResult.Failed(violations[0].Code);
        }

        effectiveOptions = options;

        return StartupCheckResult.Passed;
    }

    private PreviousStartupClassification ReadPreviousMarker() =>
        marker?.ReadPrevious() ?? PreviousStartupClassification.Unclean;

    private StartupCheckResult WriteRunningMarker()
    {
        if (marker is null)
        {
            return StartupCheckResult.Failed(BankingErrorCodes.SystemBusy);
        }

        try
        {
            marker.WriteRunning(Guid.CreateVersion7());

            return StartupCheckResult.Passed;
        }
        catch (IOException exception)
        {
            return StartupCheckResult.Failed(exception.GetType().Name);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StartupCheckResult.Failed(exception.GetType().Name);
        }
    }

    private SqliteDatabaseInitializer Initializer() => new(
        databaseOptions!,
        connectionFactory!,
        new MigrationRunner(EmbeddedMigrationCatalog.Load()));

    private StartupCheckResult ForeignKeyCheck() => Probe(static probe => probe.ForeignKeyCheck());

    private StartupCheckResult IntegrityCheck() => Probe(static probe => probe.IntegrityCheck());

    private StartupCheckResult Probe(Func<IDatabaseIntegrityProbe, DatabaseProbeResult> action)
    {
        if (connectionFactory is null)
        {
            return StartupCheckResult.Failed(BankingErrorCodes.SystemBusy);
        }

        try
        {
            DatabaseProbeResult result = action(new SqliteDatabaseIntegrityProbe(connectionFactory));

            return result.IsOk ? StartupCheckResult.Passed : StartupCheckResult.Failed(result.Detail);
        }
        catch (PersistenceFailureException exception)
        {
            return StartupCheckResult.Failed(exception.Code);
        }
    }

    private StartupCheckResult Guarded(Func<SqliteDatabaseInitializer, StartupCheckResult> action)
    {
        if (databaseOptions is null || connectionFactory is null)
        {
            return StartupCheckResult.Failed(BankingErrorCodes.SystemBusy);
        }

        try
        {
            return action(Initializer());
        }
        catch (PersistenceFailureException exception)
        {
            return StartupCheckResult.Failed(exception.Code);
        }
    }
}
