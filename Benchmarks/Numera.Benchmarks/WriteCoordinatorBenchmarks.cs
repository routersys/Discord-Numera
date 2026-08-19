using BenchmarkDotNet.Attributes;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Benchmarks;

[MemoryDiagnoser]
public class WriteCoordinatorBenchmarks
{
    private string root = string.Empty;
    private SqliteConnectionFactory connectionFactory = null!;
    private SqliteWriteCoordinator coordinator = null!;

    [Params(1, 8, 64)]
    public int Batch { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "numera-bench", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
            Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

        connectionFactory = new SqliteConnectionFactory(options);

        new SqliteDatabaseInitializer(
            options, connectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
            .Initialize(1_776_000_000_000);

        coordinator = new SqliteWriteCoordinator(connectionFactory, new SqliteRetryPolicy());
        coordinator.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ForegroundLane()
    {
        Task<WriteOutcome<int>>[] pending = new Task<WriteOutcome<int>>[Batch];

        for (int index = 0; index < Batch; index++)
        {
            pending[index] = coordinator.ExecuteAsync(
                WriteLane.Foreground, static _ => 1, CancellationToken.None);
        }

        WriteOutcome<int>[] results = await Task.WhenAll(pending).ConfigureAwait(false);

        return results.Length;
    }

    [Benchmark]
    public async Task<int> BackgroundLane()
    {
        Task<WriteOutcome<int>>[] pending = new Task<WriteOutcome<int>>[Batch];

        for (int index = 0; index < Batch; index++)
        {
            pending[index] = coordinator.ExecuteAsync(
                WriteLane.Background, static _ => 1, CancellationToken.None);
        }

        WriteOutcome<int>[] results = await Task.WhenAll(pending).ConfigureAwait(false);

        return results.Length;
    }

    [Benchmark]
    public async Task<int> MixedLanesUnderTheWeightedRule()
    {
        Task<WriteOutcome<int>>[] pending = new Task<WriteOutcome<int>>[Batch * 2];

        for (int index = 0; index < Batch; index++)
        {
            pending[index * 2] = coordinator.ExecuteAsync(
                WriteLane.Foreground, static _ => 1, CancellationToken.None);
            pending[(index * 2) + 1] = coordinator.ExecuteAsync(
                WriteLane.Background, static _ => 1, CancellationToken.None);
        }

        WriteOutcome<int>[] results = await Task.WhenAll(pending).ConfigureAwait(false);

        return results.Length;
    }
}
