using Numera.Host.Startup;

namespace Numera.Host.Tests;

[TestClass]
public sealed class StartupSequenceTests
{
    private static StartupStepBinding Passing(StartupStep step, List<StartupStep> observed) =>
        new(step, () =>
        {
            observed.Add(step);
            return StartupCheckResult.Passed;
        });

    private static IReadOnlyList<StartupStepBinding> All(List<StartupStep> observed, StartupStep? failAt = null) =>
        [.. StartupSequence.CanonicalOrder.Select(step => failAt == step
            ? new StartupStepBinding(step, () =>
            {
                observed.Add(step);
                return StartupCheckResult.Failed("BANK-UNEXPECTED-001");
            })
            : Passing(step, observed))];

    [TestMethod]
    public void TheCanonicalOrderMatchesTheSpecifiedSeventeenSteps()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                StartupStep.BaseConfigurationBuild,
                StartupStep.DatabaseBootstrapOptionsValidation,
                StartupStep.SingleInstanceLock,
                StartupStep.SqliteDirectory,
                StartupStep.SqliteConnectionAndPragma,
                StartupStep.PreMigrationRecoveryPoint,
                StartupStep.DatabaseMigration,
                StartupStep.HostSettingsLoad,
                StartupStep.BootstrapShellResolution,
                StartupStep.EffectiveRuntimeOptionsValidation,
                StartupStep.PragmaQuickCheck,
                StartupStep.ReconciliationStartupCheck,
                StartupStep.HostStart,
                StartupStep.DiscordLogin,
                StartupStep.DiscordGatewayStart,
                StartupStep.ReadyReceived,
                StartupStep.CommandSynchronization,
                StartupStep.ReadyLog,
            },
            StartupSequence.CanonicalOrder.ToArray());
    }

    [TestMethod]
    public void TwelveStepsPrecedeTheDiscordConnection()
    {
        Assert.HasCount(12, StartupSequence.BeforeDiscordConnection);
        Assert.AreEqual(
            StartupStep.ReconciliationStartupCheck,
            StartupSequence.BeforeDiscordConnection[^1]);
    }

    [TestMethod]
    public void StepsRunInTheDeclaredOrder()
    {
        List<StartupStep> observed = [];

        StartupSequenceReport report = StartupSequence.Execute(All(observed));

        Assert.IsTrue(report.Succeeded);
        CollectionAssert.AreEqual(StartupSequence.CanonicalOrder.ToArray(), observed);
    }

    [TestMethod]
    public void OutOfOrderBindingsAreRejected()
    {
        List<StartupStep> observed = [];

        Assert.ThrowsExactly<ArgumentException>(() => StartupSequence.Execute(
        [
            Passing(StartupStep.DatabaseMigration, observed),
            Passing(StartupStep.SingleInstanceLock, observed),
        ]));
    }

    [TestMethod]
    public void DuplicateStepsAreRejected()
    {
        List<StartupStep> observed = [];

        Assert.ThrowsExactly<ArgumentException>(() => StartupSequence.Execute(
        [
            Passing(StartupStep.SingleInstanceLock, observed),
            Passing(StartupStep.SingleInstanceLock, observed),
        ]));
    }

    [TestMethod]
    public void AFailureBeforeTheDiscordConnectionStopsTheSequence()
    {
        foreach (StartupStep failAt in StartupSequence.BeforeDiscordConnection)
        {
            List<StartupStep> observed = [];

            StartupSequenceReport report = StartupSequence.Execute(All(observed, failAt));

            Assert.IsFalse(report.Succeeded, failAt.ToString());
            Assert.AreEqual(failAt, report.FailedStep);
            CollectionAssert.DoesNotContain(observed, StartupStep.HostStart);
            CollectionAssert.DoesNotContain(observed, StartupStep.DiscordLogin);
            CollectionAssert.DoesNotContain(observed, StartupStep.DiscordGatewayStart);
            CollectionAssert.DoesNotContain(observed, StartupStep.CommandSynchronization);
        }
    }

    [TestMethod]
    public void UnavailableStepsAreSkippedWithoutStoppingTheSequence()
    {
        List<StartupStep> observed = [];

        StartupSequenceReport report = StartupSequence.Execute(
        [
            Passing(StartupStep.BaseConfigurationBuild, observed),
            new StartupStepBinding(StartupStep.HostSettingsLoad, static () => StartupCheckResult.NotAvailable),
            Passing(StartupStep.PragmaQuickCheck, observed),
        ]);

        Assert.IsTrue(report.Succeeded);
        CollectionAssert.AreEqual(new[] { StartupStep.HostSettingsLoad }, report.Skipped.ToArray());
    }

    [TestMethod]
    public void TheShutdownOrderMatchesTheSpecifiedElevenSteps()
    {
        Assert.HasCount(11, StartupSequence.CanonicalShutdownOrder);
        Assert.AreEqual(
            ShutdownStep.StopInteractionsAndForegroundAdmission,
            StartupSequence.CanonicalShutdownOrder[0]);
        Assert.AreEqual(ShutdownStep.ReleaseSingleInstanceLock, StartupSequence.CanonicalShutdownOrder[^1]);
    }

    [TestMethod]
    public void ProducersAreQuiescedBeforeTheWriteQueueIsDrained()
    {
        int quiesce = StartupSequence.CanonicalShutdownOrder.ToList()
            .IndexOf(ShutdownStep.QuiesceEveryWriteProducer);
        int drain = StartupSequence.CanonicalShutdownOrder.ToList().IndexOf(ShutdownStep.DrainAcceptedWrites);

        Assert.IsLessThan(drain, quiesce);
    }

    [TestMethod]
    public void OutboxDeliveryBookkeepingPrecedesTheWriterDrain()
    {
        List<ShutdownStep> order = [.. StartupSequence.CanonicalShutdownOrder];

        Assert.IsLessThan(
            order.IndexOf(ShutdownStep.DrainAcceptedWrites),
            order.IndexOf(ShutdownStep.StopOutboxDispatch));
    }

    [TestMethod]
    public void TheGatewayStopsAfterTheWriteQueueIsIdle()
    {
        List<ShutdownStep> order = [.. StartupSequence.CanonicalShutdownOrder];

        Assert.IsLessThan(order.IndexOf(ShutdownStep.StopGateway), order.IndexOf(ShutdownStep.ConfirmWriterIdle));
        Assert.IsLessThan(order.IndexOf(ShutdownStep.LogoutDiscord), order.IndexOf(ShutdownStep.StopGateway));
    }

    [TestMethod]
    public void AFailingShutdownStepNeverSkipsTheRemainingSteps()
    {
        List<ShutdownStep> observed = [];

        ShutdownSequenceReport report = StartupSequence.ExecuteShutdown(
        [
            new ShutdownStepBinding(ShutdownStep.StopInteractionsAndForegroundAdmission, () =>
                observed.Add(ShutdownStep.StopInteractionsAndForegroundAdmission)),
            new ShutdownStepBinding(ShutdownStep.DrainAcceptedWrites, static () =>
                throw new TimeoutException()),
            new ShutdownStepBinding(ShutdownStep.ReleaseSingleInstanceLock, () =>
                observed.Add(ShutdownStep.ReleaseSingleInstanceLock)),
        ]);

        CollectionAssert.AreEqual(new[] { ShutdownStep.DrainAcceptedWrites }, report.Failed.ToArray());
        CollectionAssert.Contains(observed, ShutdownStep.ReleaseSingleInstanceLock);
    }

    [TestMethod]
    public void TheShutdownBudgetIsThirtySeconds()
    {
        int budget = StartupSequence.ShutdownBudgetSeconds;

        Assert.AreEqual(30, budget);
        Assert.AreEqual(TimeSpan.FromSeconds(30), NumeraHost.ShutdownTimeout);
    }
}
