using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class DatabaseRestoreServiceTests
{
    private const long RestoredAt = 1_776_000_000_000L;

    private static SqlMigration Migration() => SqlMigration.Create(
        "0001_restore.sql", "CREATE TABLE probe(id INTEGER PRIMARY KEY) STRICT;");

    private static SqliteDatabaseBackupService Backups(SqliteDatabaseFixture fixture) =>
        new(fixture.Options, fixture.ConnectionFactory, TimeProvider.System, "1.0.0-test");

    private static SqliteDatabaseRestoreService Restores(
        SqliteDatabaseFixture fixture,
        IDatabaseBackupService backups) =>
        new(fixture.Options, backups, new MigrationRunner([Migration()]));

    private static void Insert(SqliteDatabaseFixture fixture, int id)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO probe(id) VALUES({id});";
        command.ExecuteNonQuery();
    }

    [TestMethod]
    public void RestoringReturnsTheDatabaseToTheBackupContent()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());
        Insert(fixture, 1);

        SqliteDatabaseBackupService backups = Backups(fixture);
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        Insert(fixture, 2);
        Assert.AreEqual(2L, fixture.CountRows("probe"));

        RestoreResult result = Restores(fixture, backups).Restore(created.DatabasePath, RestoredAt);

        Assert.IsTrue(result.IsSuccess, result.Detail);
        Assert.AreEqual(1L, fixture.CountRows("probe"));
    }

    [TestMethod]
    public void TheReplacedDatabaseIsKeptAsARecoveryCopy()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService backups = Backups(fixture);
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        RestoreResult result = Restores(fixture, backups).Restore(created.DatabasePath, RestoredAt);

        Assert.IsTrue(File.Exists(result.RecoveryCopyPath));
        Assert.AreEqual(fixture.Options.FullPath + SqliteDatabaseRestoreService.RecoveryCopySuffix,
            result.RecoveryCopyPath);
    }

    [TestMethod]
    public void AnUnknownBackupPathIsRejectedWithoutTouchingTheDatabase()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());
        Insert(fixture, 1);

        RestoreResult result = Restores(fixture, Backups(fixture))
            .Restore(Path.Combine(fixture.Options.BackupDirectoryPath, "missing.db"), RestoredAt);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("BACKUP_NOT_VERIFIED", result.Detail);
        Assert.AreEqual(1L, fixture.CountRows("probe"));
        Assert.IsFalse(File.Exists(fixture.Options.FullPath + SqliteDatabaseRestoreService.RecoveryCopySuffix));
    }

    [TestMethod]
    public void ATamperedBackupIsRejected()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService backups = Backups(fixture);
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        File.AppendAllText(created.DatabasePath, "corrupted");

        RestoreResult result = Restores(fixture, backups).Restore(created.DatabasePath, RestoredAt);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("BACKUP_NOT_VERIFIED", result.Detail);
    }

    [TestMethod]
    public void NoTemporaryFileSurvivesASuccessfulRestore()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService backups = Backups(fixture);
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        Assert.IsTrue(Restores(fixture, backups).Restore(created.DatabasePath, RestoredAt).IsSuccess);
        Assert.IsFalse(File.Exists(fixture.Options.FullPath + SqliteDatabaseRestoreService.TempSuffix));
    }

    [TestMethod]
    public void TheRestoredDatabaseStaysUsable()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration());

        SqliteDatabaseBackupService backups = Backups(fixture);
        BackupCreationResult created = backups.Create(BackupKind.Manual);

        Assert.IsTrue(Restores(fixture, backups).Restore(created.DatabasePath, RestoredAt).IsSuccess);

        Insert(fixture, 7);

        Assert.AreEqual(1L, fixture.CountRows("probe"));
    }
}
