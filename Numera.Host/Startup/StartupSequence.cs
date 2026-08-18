namespace Numera.Host.Startup;

internal enum StartupStep
{
    BaseConfigurationBuild = 1,
    DatabaseBootstrapOptionsValidation = 2,
    SingleInstanceLock = 3,
    SqliteDirectory = 4,
    SqliteConnectionAndPragma = 5,
    PreMigrationRecoveryPoint = 6,
    DatabaseMigration = 7,
    HostSettingsLoad = 8,
    BootstrapShellResolution = 9,
    EffectiveRuntimeOptionsValidation = 10,
    PragmaQuickCheck = 11,
    ReconciliationStartupCheck = 12,
    HostStart = 13,
    DiscordLogin = 14,
    DiscordGatewayStart = 15,
    ReadyReceived = 16,
    CommandSynchronization = 17,
    ReadyLog = 18,
}

internal enum ShutdownStep
{
    StopInteractionsAndForegroundAdmission = 1,
    QuiesceBackgroundProducers = 2,
    FinishInFlightOutboxDelivery = 3,
    StopOutboxDispatch = 4,
    QuiesceEveryWriteProducer = 5,
    DrainAcceptedWrites = 6,
    ConfirmWriterIdle = 7,
    StopGateway = 8,
    LogoutDiscord = 9,
    DisposeSqliteConnections = 10,
    ReleaseSingleInstanceLock = 11,
}

internal sealed record StartupStepBinding(StartupStep Step, Func<StartupCheckResult> Run);

internal sealed record ShutdownStepBinding(ShutdownStep Step, Action Run);

internal sealed record StartupSequenceReport(
    IReadOnlyList<StartupStep> Completed,
    IReadOnlyList<StartupStep> Skipped,
    StartupStep? FailedStep,
    string? Detail)
{
    internal bool Succeeded => FailedStep is null;
}

internal sealed record ShutdownSequenceReport(
    IReadOnlyList<ShutdownStep> Completed,
    IReadOnlyList<ShutdownStep> Failed);

internal static class StartupSequence
{
    internal const int ShutdownBudgetSeconds = 30;

    internal static IReadOnlyList<StartupStep> CanonicalOrder { get; } = [.. Enum.GetValues<StartupStep>()];

    internal static IReadOnlyList<StartupStep> BeforeDiscordConnection { get; } =
        [.. CanonicalOrder.Where(static step => step <= StartupStep.ReconciliationStartupCheck)];

    internal static IReadOnlyList<ShutdownStep> CanonicalShutdownOrder { get; } =
        [.. Enum.GetValues<ShutdownStep>()];

    internal static StartupSequenceReport Execute(IReadOnlyList<StartupStepBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        List<StartupStep> completed = [];
        List<StartupStep> skipped = [];

        StartupStep? previous = null;

        foreach (StartupStepBinding binding in bindings)
        {
            if (previous is StartupStep earlier && binding.Step <= earlier)
            {
                throw new ArgumentException(StartupFailure.StepsOutOfOrder, nameof(bindings));
            }

            previous = binding.Step;

            StartupCheckResult result = binding.Run();

            switch (result.Status)
            {
                case StartupCheckStatus.Failed:
                    return new StartupSequenceReport(completed, skipped, binding.Step, result.Detail);

                case StartupCheckStatus.NotAvailable:
                    skipped.Add(binding.Step);
                    continue;

                default:
                    completed.Add(binding.Step);
                    continue;
            }
        }

        return new StartupSequenceReport(completed, skipped, FailedStep: null, Detail: null);
    }

    internal static ShutdownSequenceReport ExecuteShutdown(IReadOnlyList<ShutdownStepBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        List<ShutdownStep> completed = [];
        List<ShutdownStep> failed = [];

        ShutdownStep? previous = null;

        foreach (ShutdownStepBinding binding in bindings)
        {
            if (previous is ShutdownStep earlier && binding.Step <= earlier)
            {
                throw new ArgumentException(StartupFailure.StepsOutOfOrder, nameof(bindings));
            }

            previous = binding.Step;

            try
            {
                binding.Run();
                completed.Add(binding.Step);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failed.Add(binding.Step);
            }
        }

        return new ShutdownSequenceReport(completed, failed);
    }
}

internal static class StartupFailure
{
    internal const string StepsOutOfOrder = "起動と終了の手順は宣言順序どおりに束ねる必要があります。";
}
