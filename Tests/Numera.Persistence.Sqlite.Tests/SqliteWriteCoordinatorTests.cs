using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class SqliteWriteCoordinatorTests
{
    private static SqlMigration CounterSchema() => SqlMigration.Create("0001_initial.sql", """
        CREATE TABLE counters(
            name TEXT NOT NULL PRIMARY KEY,
            value INTEGER NOT NULL
        ) STRICT;
        INSERT INTO counters(name, value) VALUES('total', 0);
        """);

    private static SqliteRetryPolicy Policy() =>
        new(maximumAttempts: 3, baseDelayMilliseconds: 1, jitterMillisecondsProvider: static () => 0);

    private static long ReadCounter(SqliteDatabaseFixture fixture)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM counters WHERE name = 'total';";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static long IncrementCounter(SqliteUnitOfWork unitOfWork)
    {
        long current;
        using (SqliteCommand read = unitOfWork.CreateCommand("SELECT value FROM counters WHERE name = 'total';"))
        {
            current = (long)(read.ExecuteScalar() ?? 0L);
        }

        using SqliteCommand write = unitOfWork.CreateCommand(
            "UPDATE counters SET value = $value WHERE name = 'total';");
        write.Parameters.AddWithValue("$value", current + 1);
        write.ExecuteNonQuery();

        return current + 1;
    }

    [TestMethod]
    public async Task CommittedOperationPersistsResult()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();

        WriteOutcome<long> outcome = await coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, CancellationToken.None);

        Assert.IsTrue(outcome.IsCommitted);
        Assert.AreEqual(1L, outcome.Value);
        Assert.AreEqual(1L, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task ConcurrentWritesAreSerialisedWithoutLostUpdates()
    {
        const int writers = 64;

        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();

        Task<WriteOutcome<long>>[] pending = new Task<WriteOutcome<long>>[writers];
        for (int index = 0; index < writers; index++)
        {
            pending[index] = coordinator.ExecuteAsync(
                WriteLane.Foreground, IncrementCounter, CancellationToken.None);
        }

        WriteOutcome<long>[] outcomes = await Task.WhenAll(pending);

        Assert.IsTrue(outcomes.All(static outcome => outcome.IsCommitted));
        Assert.AreEqual(writers, outcomes.Select(static outcome => outcome.Value).Distinct().Count());
        Assert.AreEqual((long)writers, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task ForegroundOverflowIsRejectedAsSystemBusy()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());

        for (int index = 0; index < SqliteWriteCoordinatorOptions.ForegroundCapacity; index++)
        {
            Task<WriteOutcome<long>> accepted = coordinator.ExecuteAsync(
                WriteLane.Foreground, IncrementCounter, CancellationToken.None);

            Assert.IsFalse(accepted.IsCompleted);
        }

        WriteOutcome<long> rejected = await coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.RejectedSystemBusy, rejected.Status);
        Assert.ThrowsExactly<PersistenceFailureException>(() => _ = rejected.Value);

        coordinator.Start();
        await coordinator.DrainAsync();
    }

    [TestMethod]
    public async Task BackgroundOverflowIsRejectedAtSmallerCapacity()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());

        for (int index = 0; index < SqliteWriteCoordinatorOptions.BackgroundCapacity; index++)
        {
            _ = coordinator.ExecuteAsync(WriteLane.Background, IncrementCounter, CancellationToken.None);
        }

        WriteOutcome<long> rejected = await coordinator.ExecuteAsync(
            WriteLane.Background, IncrementCounter, CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.RejectedSystemBusy, rejected.Status);

        coordinator.Start();
        await coordinator.DrainAsync();
    }

    [TestMethod]
    public async Task CancellationBeforeExecutionSkipsTransaction()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());

        using CancellationTokenSource cancellation = new();
        Task<WriteOutcome<long>> pending = coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, cancellation.Token);

        await cancellation.CancelAsync();
        coordinator.Start();

        WriteOutcome<long> outcome = await pending;

        Assert.AreEqual(WriteOutcomeStatus.CancelledBeforeExecution, outcome.Status);
        Assert.AreEqual(0L, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task AlreadyCancelledRequestIsNeverQueued()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        WriteOutcome<long> outcome = await coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, cancellation.Token);

        Assert.AreEqual(WriteOutcomeStatus.CancelledBeforeExecution, outcome.Status);
    }

    [TestMethod]
    public async Task FailedOperationRollsBackAndPropagates()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await coordinator.ExecuteAsync<long>(
                WriteLane.Foreground,
                unitOfWork =>
                {
                    IncrementCounter(unitOfWork);
                    throw new InvalidOperationException("failure");
                },
                CancellationToken.None));

        Assert.AreEqual(0L, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task LaterOperationsStillSucceedAfterFailure()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();

        Task<WriteOutcome<long>> failing = coordinator.ExecuteAsync<long>(
            WriteLane.Foreground,
            static _ => throw new InvalidOperationException("failure"),
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await failing);

        WriteOutcome<long> outcome = await coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, CancellationToken.None);

        Assert.IsTrue(outcome.IsCommitted);
        Assert.AreEqual(1L, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task BackgroundLaneIsNotStarvedByForegroundBurst()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        ConcurrentQueue<WriteLane> executionOrder = new();

        for (int index = 0; index < 24; index++)
        {
            _ = coordinator.ExecuteAsync(
                WriteLane.Foreground,
                unitOfWork =>
                {
                    executionOrder.Enqueue(WriteLane.Foreground);
                    return IncrementCounter(unitOfWork);
                },
                CancellationToken.None);
        }

        for (int index = 0; index < 3; index++)
        {
            _ = coordinator.ExecuteAsync(
                WriteLane.Background,
                unitOfWork =>
                {
                    executionOrder.Enqueue(WriteLane.Background);
                    return IncrementCounter(unitOfWork);
                },
                CancellationToken.None);
        }

        coordinator.Start();
        await coordinator.DrainAsync();

        WriteLane[] order = [.. executionOrder];

        Assert.AreEqual(27, order.Length);
        Assert.AreEqual(WriteLane.Background, order[8]);
        Assert.AreEqual(WriteLane.Background, order[17]);
        Assert.AreEqual(27L, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task DrainCompletesEveryAcceptedRequest()
    {
        const int accepted = 40;

        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());

        Task<WriteOutcome<long>>[] pending = new Task<WriteOutcome<long>>[accepted];
        for (int index = 0; index < accepted; index++)
        {
            pending[index] = coordinator.ExecuteAsync(
                WriteLane.Foreground, IncrementCounter, CancellationToken.None);
        }

        coordinator.Start();
        await coordinator.DrainAsync();

        Assert.IsTrue(pending.All(static task => task.IsCompletedSuccessfully));
        Assert.AreEqual((long)accepted, ReadCounter(fixture));
    }

    [TestMethod]
    public async Task StartingTwiceIsRejected()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();

        PersistenceFailureException exception =
            Assert.ThrowsExactly<PersistenceFailureException>(coordinator.Start);

        Assert.AreEqual(PersistenceFailureCode.WriteCoordinatorAlreadyStarted, exception.Code);
    }

    [TestMethod]
    public async Task ClosedForegroundAdmissionRejectsNewWork()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(CounterSchema());

        await using SqliteWriteCoordinator coordinator = new(fixture.ConnectionFactory, Policy());
        coordinator.Start();
        coordinator.CloseForegroundAdmission();

        WriteOutcome<long> outcome = await coordinator.ExecuteAsync(
            WriteLane.Foreground, IncrementCounter, CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.RejectedSystemBusy, outcome.Status);
    }
}

[TestClass]
public sealed class SqliteRetryPolicyTests
{
    [TestMethod]
    public void BackoffGrowsExponentiallyWithJitter()
    {
        SqliteRetryPolicy policy = new(
            maximumAttempts: 3, baseDelayMilliseconds: 10, jitterMillisecondsProvider: static () => 5);

        Assert.AreEqual(TimeSpan.FromMilliseconds(15), policy.DelayForAttempt(1));
        Assert.AreEqual(TimeSpan.FromMilliseconds(25), policy.DelayForAttempt(2));
        Assert.AreEqual(TimeSpan.FromMilliseconds(45), policy.DelayForAttempt(3));
    }

    [TestMethod]
    public void NonPositiveAttemptIsRejected()
    {
        SqliteRetryPolicy policy = new();

        Assert.ThrowsExactly<PersistenceFailureException>(() => policy.DelayForAttempt(0));
    }

    [TestMethod]
    [DataRow(0, 10)]
    [DataRow(3, -1)]
    public void InvalidConfigurationIsRejected(int attempts, int baseDelay)
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => new SqliteRetryPolicy(attempts, baseDelay));

        Assert.AreEqual(PersistenceFailureCode.RetryPolicyInvalid, exception.Code);
    }

    [TestMethod]
    public void OnlyContentionErrorsAreTransient()
    {
        Assert.IsFalse(SqliteRetryPolicy.IsTransientContention(new InvalidOperationException()));
        Assert.IsFalse(SqliteRetryPolicy.IsTransientContention(new PersistenceFailureException()));
    }
}
