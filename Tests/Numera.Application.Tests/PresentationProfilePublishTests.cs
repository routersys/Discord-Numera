using Microsoft.Data.Sqlite;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Application.Tests;

[TestClass]
public sealed class PresentationProfilePublishTests
{
    private const ulong OperatorDiscordUserId = 870_000_000_000_000_001UL;
    private const ulong GuildId = 970UL;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root;

        private Harness(string root, SqliteDatabaseOptions options)
        {
            this.root = root;
            ConnectionFactory = new SqliteConnectionFactory(options);
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public SqliteWriteCoordinator Coordinator { get; private set; } = null!;

        public PresentationProfileAdministrationApplicationService Profiles { get; private set; } = null!;

        public static Harness Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "numera-profile", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(root);

            SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"), SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

            Harness harness = new(root, options);
            new SqliteDatabaseInitializer(
                options, harness.ConnectionFactory, new MigrationRunner([.. EmbeddedMigrationCatalog.Load()]))
                .Initialize(1_776_000_000_000);
            harness.Seed();

            harness.Coordinator = new SqliteWriteCoordinator(
                harness.ConnectionFactory, new SqliteRetryPolicy(3, 1, static () => 0));
            harness.Coordinator.Start();

            harness.Profiles = new PresentationProfileAdministrationApplicationService(
                new SqliteBankingWriteGateway(new FinancialWriteCoordinator(harness.Coordinator)),
                new FixedClock(),
                new SequentialIdGenerator(9_000));

            return harness;
        }

        private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

        private void Seed() => Execute($"""
            INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
            VALUES({Blob(1)}, '{GuildId}', 'Asia/Tokyo', 'ACTIVE', 1);
            """);

        public void Execute(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string? ReadText(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar() as string;
        }

        public long Count(string sql)
        {
            using SqliteConnection connection = ConnectionFactory.OpenRuntimeConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return (long)command.ExecuteScalar()!;
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            SqliteConnection.ClearPool(ConnectionFactory.OpenRuntimeConnection());

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AuthorizationContext Operator() =>
        new(AuthorizationLevel.GuildOperator, OperatorDiscordUserId, GuildId);

    private static PresentationProfilePalette Palette(int information = 0x112233) =>
        new(information, 0x00AA00, 0xAAAA00, 0xAA0000, 0x333333);

    private static async Task<PresentationProfileDraftView> DraftAsync(Harness harness)
    {
        Result<PresentationProfileDraftView> draft = await harness.Profiles.StartDraftAsync(
            new StartPresentationProfileDraftCommand(Operator(), Palette()),
            CancellationToken.None);

        Assert.IsTrue(draft.IsSuccess, draft.Error?.Code);

        return draft.Value;
    }

    [TestMethod]
    public async Task PublishingRetiresThePreviousCurrentProfile()
    {
        await using Harness harness = Harness.Create();
        PresentationProfileDraftView first = await DraftAsync(harness);

        Result<PresentationProfileVersionView> published = await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(Operator(), first.Id, first.Version),
            CancellationToken.None);

        Assert.IsTrue(published.IsSuccess, published.Error?.Code);

        PresentationProfileDraftView second = await DraftAsync(harness);

        Result<PresentationProfileVersionView> replacement = await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(Operator(), second.Id, second.Version),
            CancellationToken.None);

        Assert.IsTrue(replacement.IsSuccess, replacement.Error?.Code);
        Assert.AreEqual(
            1L,
            harness.Count("""
                SELECT COUNT(*) FROM presentation_profile_versions WHERE status = 'PUBLISHED';
                """));
        Assert.AreEqual(
            1L,
            harness.Count("""
                SELECT COUNT(*) FROM presentation_profile_versions WHERE status = 'RETIRED';
                """));
    }

    [TestMethod]
    public async Task PublishingStampsTheCommitTimestamps()
    {
        await using Harness harness = Harness.Create();
        PresentationProfileDraftView draft = await DraftAsync(harness);

        Assert.IsTrue((await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(Operator(), draft.Id, draft.Version),
            CancellationToken.None)).IsSuccess);

        Assert.AreEqual(
            0L,
            harness.Count("""
                SELECT COUNT(*) FROM presentation_profile_versions
                WHERE created_at = 0 OR published_at = 0;
                """));
    }

    [TestMethod]
    public async Task PublishingWritesTheAuditAndOutbox()
    {
        await using Harness harness = Harness.Create();
        PresentationProfileDraftView draft = await DraftAsync(harness);

        Assert.IsTrue((await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(Operator(), draft.Id, draft.Version),
            CancellationToken.None)).IsSuccess);

        Assert.AreEqual(
            1L,
            harness.Count("""
                SELECT COUNT(*) FROM audit_records a
                INNER JOIN business_operations o ON o.business_operation_id = a.business_operation_id
                WHERE a.action = 'PRESENTATION_PROFILE_PUBLISH' AND o.status = 'COMMITTED';
                """));
        Assert.AreEqual(
            1L,
            harness.Count("""
                SELECT COUNT(*) FROM outbox_events
                WHERE event_type = 'PRESENTATION_PROFILE_PUBLISHED';
                """));
    }

    [TestMethod]
    public async Task AStaleExpectedVersionConflicts()
    {
        await using Harness harness = Harness.Create();
        PresentationProfileDraftView draft = await DraftAsync(harness);

        Result<PresentationProfileVersionView> published = await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(Operator(), draft.Id, draft.Version - 1),
            CancellationToken.None);

        Assert.IsFalse(published.IsSuccess);
        Assert.AreEqual(ErrorCategory.ConcurrencyConflict, published.Error!.Category);
        Assert.AreEqual(
            "DRAFT",
            harness.ReadText("SELECT status FROM presentation_profile_versions;"));
    }

    [TestMethod]
    public async Task ARejectedPublishWritesNothing()
    {
        await using Harness harness = Harness.Create();
        PresentationProfileDraftView draft = await DraftAsync(harness);

        Assert.IsFalse((await harness.Profiles.PublishAsync(
            new PublishPresentationProfileCommand(
                new AuthorizationContext(AuthorizationLevel.Customer, OperatorDiscordUserId, GuildId),
                draft.Id,
                draft.Version),
            CancellationToken.None)).IsSuccess);

        Assert.AreEqual(0L, harness.Count("SELECT COUNT(*) FROM business_operations;"));
        Assert.AreEqual(0L, harness.Count("SELECT COUNT(*) FROM outbox_events;"));
    }
}
