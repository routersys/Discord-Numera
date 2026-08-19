using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class DatabaseBackupServiceTests
{
    private const string Version = "1.0.0-test";

    private static SqlMigration Migration() => SqlMigration.Create(
        "0001_backup.sql", "CREATE TABLE probe(id INTEGER PRIMARY KEY) STRICT;");

    private static SqliteDatabaseBackupService Create(SqliteDatabaseFixture fixture, TimeProvider? clock = null) =>
        new(fixture.Options, fixture.ConnectionFactory, clock ?? TimeProvider.System, Version);

    [TestMethod]
    public void ABackupIsWrittenWithItsManifest()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        BackupCreationResult result = Create(fixture).Create(BackupKind.Manual);

        Assert.IsTrue(result.IsSuccess, result.Detail);
        Assert.IsTrue(File.Exists(result.DatabasePath));
        Assert.IsTrue(File.Exists(result.ManifestPath));
        Assert.IsEmpty(Directory.GetFiles(fixture.Options.BackupDirectoryPath, "*.partial"));
    }

    [TestMethod]
    public void TheManifestDescribesTheCopiedDatabase()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);
        service.Create(BackupKind.PreMigration);

        BackupEntry entry = service.List().Single();

        Assert.AreEqual(SqliteDatabaseBackupService.ManifestFormatVersion, entry.Manifest.FormatVersion);
        Assert.AreEqual("PRE_MIGRATION", entry.Manifest.BackupKind);
        Assert.AreEqual(Version, entry.Manifest.ApplicationVersion);
        Assert.AreEqual(1, entry.Manifest.SourceDatabaseSchemaVersion);
        Assert.AreEqual("ok", entry.Manifest.QuickCheck);
        Assert.AreEqual(0, entry.Manifest.ForeignKeyCheckCount);
        Assert.AreEqual(new FileInfo(entry.DatabasePath).Length, entry.Manifest.DatabaseLengthBytes);
    }

    [TestMethod]
    public void AFreshBackupPassesFullVerification()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);
        service.Create(BackupKind.Automatic);

        BackupVerificationResult verified = service.Verify(service.List().Single());

        Assert.IsTrue(verified.IsSuccess, verified.Detail);
    }

    [TestMethod]
    public void ATamperedBackupFailsTheDigestCheck()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);
        BackupCreationResult created = service.Create(BackupKind.Automatic);
        BackupEntry entry = service.List().Single();

        using (SqliteConnection connection = new($"Data Source={created.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO probe(id) VALUES(1);";
            command.ExecuteNonQuery();
        }

        BackupVerificationResult verified = service.Verify(entry);

        Assert.IsFalse(verified.IsSuccess);
        Assert.IsTrue(
            verified.Detail is BackupFailure.DigestMismatch or BackupFailure.LengthMismatch,
            verified.Detail);
    }

    [TestMethod]
    public void ABackupWithoutItsManifestIsNotListed()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);
        BackupCreationResult created = service.Create(BackupKind.Automatic);
        File.Delete(created.ManifestPath);

        Assert.IsEmpty(service.List());
    }

    [TestMethod]
    public void RetentionKeepsTheNewestAutomaticGenerations()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        SqliteDatabaseBackupService service = Create(fixture, clock);

        for (int index = 0; index < SqliteDatabaseBackupService.AutomaticRetentionCount + 2; index++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsTrue(service.Create(BackupKind.Automatic).IsSuccess);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        service.Create(BackupKind.Manual);

        int removed = service.PruneAutomatic();

        Assert.AreEqual(2, removed);
        Assert.AreEqual(SqliteDatabaseBackupService.AutomaticRetentionCount + 1, service.List().Count);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset now;

        internal FakeTimeProvider(DateTimeOffset start) => now = start;

        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan delta) => now = now.Add(delta);
    }

    private static SqliteDatabaseFixture SecondaryFixture(out string secondary)
    {
        SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        secondary = Path.Combine(
            Path.GetTempPath(), "numera-secondary", Guid.NewGuid().ToString("n"));

        return fixture;
    }

    [TestMethod]
    public void ASecondaryDirectoryReceivesTheVerifiedCopy()
    {
        using SqliteDatabaseFixture source = SecondaryFixture(out string secondary);
        source.Initialize(Migration());

        SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
            source.Options.Path, source.Options.BusyTimeoutSeconds, secondary);

        SqliteDatabaseBackupService service = new(
            options, source.ConnectionFactory, TimeProvider.System, Version);

        try
        {
            BackupCreationResult result = service.Create(BackupKind.Automatic);

            Assert.IsTrue(result.IsSuccess, result.Detail);
            Assert.IsEmpty(result.Detail);

            string copied = Path.Combine(secondary, Path.GetFileName(result.DatabasePath));
            string manifest = Path.Combine(secondary, Path.GetFileName(result.ManifestPath));

            Assert.IsTrue(File.Exists(copied));
            Assert.IsTrue(File.Exists(manifest));
            Assert.IsEmpty(Directory.GetFiles(secondary, "*.partial"));
            Assert.AreEqual(
                new FileInfo(result.DatabasePath).Length, new FileInfo(copied).Length);
            Assert.AreEqual(BackupRedundancy.SecondaryOk, service.Summarize().Redundancy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(secondary, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public void ADeletedSecondaryCopyDegradesTheRedundancy()
    {
        using SqliteDatabaseFixture source = SecondaryFixture(out string secondary);
        source.Initialize(Migration());

        SqliteDatabaseOptions options = SqliteDatabaseOptions.Create(
            source.Options.Path, source.Options.BusyTimeoutSeconds, secondary);

        SqliteDatabaseBackupService service = new(
            options, source.ConnectionFactory, TimeProvider.System, Version);

        try
        {
            BackupCreationResult result = service.Create(BackupKind.Automatic);
            File.Delete(Path.Combine(secondary, Path.GetFileName(result.DatabasePath)));

            Assert.AreEqual(BackupRedundancy.SecondaryDegraded, service.Summarize().Redundancy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(secondary, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [TestMethod]
    public void WithoutASecondaryTargetTheRedundancyStaysLocal()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);
        service.Create(BackupKind.Automatic);

        Assert.AreEqual(BackupRedundancy.LocalOnly, service.Summarize().Redundancy);
        Assert.IsNull(SqliteDatabaseOptions.CreateDefault().SecondaryBackupDirectory);
    }

    [TestMethod]
    public void ASecondaryTargetInsideTheBackupDirectoryIsRejected()
    {
        SqliteDatabaseOptions canonical = SqliteDatabaseOptions.CreateDefault();

        PersistenceFailureException failure = Assert.ThrowsExactly<PersistenceFailureException>(() =>
            SqliteDatabaseOptions.Create(
                SqliteDatabaseOptions.DefaultPath,
                SqliteDatabaseOptions.DefaultBusyTimeoutSeconds,
                canonical.BackupDirectoryPath));

        Assert.AreEqual(PersistenceFailureCode.SecondaryBackupDirectoryInvalid, failure.Code);
    }

    [TestMethod]
    public void TheNewestAutomaticBackupTimestampIsReadable()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService service = Create(fixture);

        Assert.IsNull(service.NewestAutomaticCreatedAtUtc());

        service.Create(BackupKind.Manual);

        Assert.IsNull(service.NewestAutomaticCreatedAtUtc());

        service.Create(BackupKind.Automatic);

        Assert.IsNotNull(service.NewestAutomaticCreatedAtUtc());
    }
}
