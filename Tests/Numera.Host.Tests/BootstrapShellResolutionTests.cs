using Microsoft.Extensions.Configuration;
using Numera.Host.Console;
using Numera.Host.Startup;

namespace Numera.Host.Tests;

[TestClass]
public sealed class BootstrapShellResolutionTests
{
    private static readonly DateTimeOffset Instant = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static (StartupComposer Composer, string Root) Compose()
    {
        string root = Path.Combine(Path.GetTempPath(), "numera-shell", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(root, "data", "economy.db"),
                ["Database:BusyTimeoutSeconds"] = "5",
            })
            .Build();

        return (
            new StartupComposer(
                configuration,
                new ConfigurationOnlyBootstrapSettingsStore(),
                new FixedTimeProvider(Instant)),
            root);
    }

    private static StartupSequenceReport RunThroughShellResolution(StartupComposer composer)
    {
        IReadOnlyList<StartupStepBinding> bindings = composer.Bind("Production");

        return StartupSequence.Execute(
            [.. bindings.Where(static binding => binding.Step <= StartupStep.BootstrapShellResolution)]);
    }

    private static void Cleanup(StartupComposer composer, string root)
    {
        composer.Lock?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [TestMethod]
    public void TheShellResolutionStepCompletesInsteadOfBeingSkipped()
    {
        (StartupComposer composer, string root) = Compose();

        try
        {
            StartupSequenceReport report = RunThroughShellResolution(composer);

            Assert.IsTrue(report.Succeeded, report.Detail);
            CollectionAssert.Contains(
                report.Completed.ToArray(), StartupStep.BootstrapShellResolution);
            CollectionAssert.DoesNotContain(
                report.Skipped.ToArray(), StartupStep.BootstrapShellResolution);
        }
        finally
        {
            Cleanup(composer, root);
        }
    }

    [TestMethod]
    public void TheResolvedShellExecutesRecoveryCommands()
    {
        (StartupComposer composer, string root) = Compose();

        try
        {
            Assert.IsTrue(RunThroughShellResolution(composer).Succeeded);

            using StringReader input = new("health\ndatabase verify\nshutdown\n");
            using StringWriter output = new();

            ShellSession session = composer.RunRecoveryShell(input, output, CancellationToken.None);

            Assert.AreEqual(ShellExitReason.ShutdownRequested, session.Reason);
            Assert.AreEqual(2, session.ExecutedCount);
            StringAssert.Contains(output.ToString(), "Runtime State:");
            StringAssert.Contains(output.ToString(), "Financial Reconciliation: UNKNOWN");
        }
        finally
        {
            Cleanup(composer, root);
        }
    }

    [TestMethod]
    public void AnUnresolvedShellReportsClosedInputInsteadOfThrowing()
    {
        (StartupComposer composer, string root) = Compose();

        try
        {
            using StringReader input = new("health\n");
            using StringWriter output = new();

            ShellSession session = composer.RunRecoveryShell(input, output, CancellationToken.None);

            Assert.AreEqual(ShellExitReason.InputClosed, session.Reason);
            Assert.AreEqual(0, session.ExecutedCount);
            Assert.IsEmpty(output.ToString());
        }
        finally
        {
            Cleanup(composer, root);
        }
    }
}
