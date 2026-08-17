using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite.Transactions;

public sealed class SqliteWriteCoordinatorOptions
{
    public const int ForegroundCapacity = 128;
    public const int BackgroundCapacity = 32;
    public const int ForegroundBurst = 8;

    public static SqliteWriteCoordinatorOptions Canonical { get; } = new();
}

public sealed class SqliteWriteCoordinator : IAsyncDisposable
{
    private readonly Channel<WriteRequest> foreground = CreateLane(SqliteWriteCoordinatorOptions.ForegroundCapacity);
    private readonly Channel<WriteRequest> background = CreateLane(SqliteWriteCoordinatorOptions.BackgroundCapacity);
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteRetryPolicy retryPolicy;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Lock gate = new();

    private Task? consumer;
    private bool foregroundClosed;
    private bool backgroundClosed;
    private bool disposed;

    public SqliteWriteCoordinator(SqliteConnectionFactory connectionFactory, SqliteRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        this.connectionFactory = connectionFactory;
        this.retryPolicy = retryPolicy;
    }

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (consumer is not null)
            {
                throw PersistenceFailureException.Create(PersistenceFailureCode.WriteCoordinatorAlreadyStarted);
            }

            consumer = Task.Run(ConsumeAsync);
        }
    }

    public Task<WriteOutcome<TResult>> ExecuteAsync<TResult>(
        WriteLane lane,
        Func<SqliteUnitOfWork, TResult> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteWithDecisionAsync(
            lane,
            unitOfWork => WriteDecision<TResult>.Commit(operation(unitOfWork)),
            cancellationToken);
    }

    public Task<WriteOutcome<TResult>> ExecuteWithDecisionAsync<TResult>(
        WriteLane lane,
        Func<SqliteUnitOfWork, WriteDecision<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(disposed, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(WriteOutcome<TResult>.CancelledBeforeExecution());
        }

        WriteRequest<TResult> request = new(operation, cancellationToken);
        Channel<WriteRequest> target = lane == WriteLane.Foreground ? foreground : background;

        return target.Writer.TryWrite(request)
            ? request.Completion
            : Task.FromResult(WriteOutcome<TResult>.RejectedSystemBusy());
    }

    public void CloseForegroundAdmission()
    {
        lock (gate)
        {
            if (!foregroundClosed)
            {
                foregroundClosed = true;
                foreground.Writer.TryComplete();
            }
        }
    }

    public void CloseBackgroundAdmission()
    {
        lock (gate)
        {
            if (!backgroundClosed)
            {
                backgroundClosed = true;
                background.Writer.TryComplete();
            }
        }
    }

    public async Task DrainAsync()
    {
        CloseForegroundAdmission();
        CloseBackgroundAdmission();

        Task? running;
        lock (gate)
        {
            running = consumer;
        }

        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        CloseForegroundAdmission();
        CloseBackgroundAdmission();

        if (consumer is not null)
        {
            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await shutdown.CancelAsync().ConfigureAwait(false);
        shutdown.Dispose();
    }

    private static Channel<WriteRequest> CreateLane(int capacity) =>
        Channel.CreateBounded<WriteRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

    private async Task ConsumeAsync()
    {
        while (true)
        {
            bool progressed = false;

            for (int burst = 0; burst < SqliteWriteCoordinatorOptions.ForegroundBurst; burst++)
            {
                if (!foreground.Reader.TryRead(out WriteRequest? request))
                {
                    break;
                }

                await ExecuteRequestAsync(request).ConfigureAwait(false);
                progressed = true;
            }

            if (background.Reader.TryRead(out WriteRequest? backgroundRequest))
            {
                await ExecuteRequestAsync(backgroundRequest).ConfigureAwait(false);
                progressed = true;
            }

            if (progressed)
            {
                continue;
            }

            if (!await WaitForWorkAsync().ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<bool> WaitForWorkAsync()
    {
        Task<bool> foregroundWait = foreground.Reader.WaitToReadAsync().AsTask();
        Task<bool> backgroundWait = background.Reader.WaitToReadAsync().AsTask();

        Task<bool> completed = await Task.WhenAny(foregroundWait, backgroundWait).ConfigureAwait(false);

        if (await completed.ConfigureAwait(false))
        {
            return true;
        }

        Task<bool> other = ReferenceEquals(completed, foregroundWait) ? backgroundWait : foregroundWait;
        return await other.ConfigureAwait(false);
    }

    private async Task ExecuteRequestAsync(WriteRequest request)
    {
        if (request.CancellationToken.IsCancellationRequested)
        {
            request.CancelBeforeExecution();
            return;
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                request.Run(connectionFactory);
                return;
            }
            catch (Exception exception) when (SqliteRetryPolicy.IsTransientContention(exception)
                && attempt < retryPolicy.MaximumAttempts)
            {
                await Task.Delay(retryPolicy.DelayForAttempt(attempt)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                request.Fail(exception);
                return;
            }
        }
    }

    private abstract class WriteRequest
    {
        public abstract CancellationToken CancellationToken { get; }

        public abstract void Run(SqliteConnectionFactory connectionFactory);

        public abstract void CancelBeforeExecution();

        public abstract void Fail(Exception exception);
    }

    private sealed class WriteRequest<TResult> : WriteRequest
    {
        private readonly Func<SqliteUnitOfWork, WriteDecision<TResult>> operation;
        private readonly TaskCompletionSource<WriteOutcome<TResult>> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WriteRequest(
            Func<SqliteUnitOfWork, WriteDecision<TResult>> operation,
            CancellationToken cancellationToken)
        {
            this.operation = operation;
            CancellationToken = cancellationToken;
        }

        public override CancellationToken CancellationToken { get; }

        public Task<WriteOutcome<TResult>> Completion => completion.Task;

        public override void Run(SqliteConnectionFactory connectionFactory)
        {
            using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

            WriteDecision<TResult> decision = operation(new SqliteUnitOfWork(connection, transaction));

            if (decision.ShouldCommit)
            {
                transaction.Commit();
                completion.TrySetResult(WriteOutcome<TResult>.Committed(decision.Value));
                return;
            }

            transaction.Rollback();
            completion.TrySetResult(WriteOutcome<TResult>.RolledBack(decision.Value));
        }

        public override void CancelBeforeExecution() =>
            completion.TrySetResult(WriteOutcome<TResult>.CancelledBeforeExecution());

        public override void Fail(Exception exception) => completion.TrySetException(exception);
    }
}
