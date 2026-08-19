using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class SqliteDatabaseOptionsTests
{
    [TestMethod]
    public void DefaultOptionsMatchCanonicalContract()
    {
        SqliteDatabaseOptions options = SqliteDatabaseOptions.CreateDefault();

        Assert.AreEqual("data/economy.db", options.Path);
        Assert.AreEqual(5, options.BusyTimeoutSeconds);
        Assert.AreEqual(5_000, options.BusyTimeoutMilliseconds);
        Assert.IsTrue(options.LockFilePath.EndsWith(".lock", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void BlankPathIsRejected(string path)
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => SqliteDatabaseOptions.Create(path, 5));

        Assert.AreEqual(PersistenceFailureCode.DatabasePathInvalid, exception.Code);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(61)]
    public void BusyTimeoutOutOfRangeIsRejected(int seconds)
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => SqliteDatabaseOptions.Create("data/economy.db", seconds));

        Assert.AreEqual(PersistenceFailureCode.BusyTimeoutInvalid, exception.Code);
    }
}

[TestClass]
public sealed class SqliteConnectionFactoryTests
{
    private static SqlMigration Trivial() =>
        SqlMigration.Create("0001_initial.sql", "CREATE TABLE probe(id INTEGER NOT NULL PRIMARY KEY) STRICT;");

    [TestMethod]
    public void RuntimeConnectionAppliesCanonicalPragmas()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create(busyTimeoutSeconds: 7);
        fixture.Initialize(Trivial());

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();

        Assert.AreEqual("wal", ReadPragma(connection, "journal_mode"), ignoreCase: true);
        Assert.AreEqual("2", ReadPragma(connection, "synchronous"));
        Assert.AreEqual("1", ReadPragma(connection, "foreign_keys"));
        Assert.AreEqual("7000", ReadPragma(connection, "busy_timeout"));
    }

    [TestMethod]
    public void WalAutoCheckpointIsPinnedAtStartup()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Trivial());

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();

        Assert.AreEqual("1000", ReadPragma(connection, "wal_autocheckpoint"));
    }

    [TestMethod]
    public void ForeignKeyEnforcementIsActive()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(SqlMigration.Create("0001_initial.sql", """
            CREATE TABLE parent(id INTEGER NOT NULL PRIMARY KEY) STRICT;
            CREATE TABLE child(
                id INTEGER NOT NULL PRIMARY KEY,
                parent_id INTEGER NOT NULL REFERENCES parent(id)
            ) STRICT;
            """));

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO child(id, parent_id) VALUES(1, 99);";

        Assert.ThrowsExactly<SqliteException>(() => command.ExecuteNonQuery());
    }

    [TestMethod]
    public void SeparateConnectionsAreIndependentInstances()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Trivial());

        using SqliteConnection first = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteConnection second = fixture.ConnectionFactory.OpenRuntimeConnection();

        Assert.AreNotSame(first, second);
    }

    private static string ReadPragma(SqliteConnection connection, string pragma)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        object? value = command.ExecuteScalar();

        return value switch
        {
            long number => number.ToString(CultureInfo.InvariantCulture),
            string text => text,
            _ => string.Empty,
        };
    }
}

[TestClass]
public sealed class SqlMigrationTests
{
    [TestMethod]
    public void FileNameYieldsVersionAndName()
    {
        SqlMigration migration = SqlMigration.Create("0003_payment_order.sql", "SELECT 1;");

        Assert.AreEqual(3, migration.Version);
        Assert.AreEqual("payment_order", migration.Name);
    }

    [TestMethod]
    [DataRow("initial.sql")]
    [DataRow("1_initial.sql")]
    [DataRow("00001_initial.sql")]
    [DataRow("000a_initial.sql")]
    [DataRow("0000_initial.sql")]
    [DataRow("0001_.sql")]
    public void MalformedFileNamesAreRejected(string fileName)
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => SqlMigration.Create(fileName, "SELECT 1;"));

        Assert.AreEqual(PersistenceFailureCode.MigrationResourceNameInvalid, exception.Code);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EmptyScriptIsRejected(string script)
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => SqlMigration.Create("0001_initial.sql", script));

        Assert.AreEqual(PersistenceFailureCode.MigrationScriptEmpty, exception.Code);
    }

    [TestMethod]
    public void ChecksumIgnoresLineEndingStyle() =>
        Assert.AreEqual(
            SqlMigration.ComputeChecksum("SELECT 1;\nSELECT 2;"),
            SqlMigration.ComputeChecksum("SELECT 1;\r\nSELECT 2;"));

    [TestMethod]
    public void ChecksumChangesWithContent() =>
        Assert.AreNotEqual(
            SqlMigration.ComputeChecksum("SELECT 1;"),
            SqlMigration.ComputeChecksum("SELECT 2;"));

    [TestMethod]
    public void ChecksumIsLowercaseHexOfFixedLength()
    {
        string checksum = SqlMigration.ComputeChecksum("SELECT 1;");

        Assert.AreEqual(64, checksum.Length);
        foreach (char character in checksum)
        {
            Assert.IsTrue(character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
        }
    }
}

[TestClass]
public sealed class MigrationRunnerTests
{
    private static SqlMigration First() =>
        SqlMigration.Create("0001_initial.sql", "CREATE TABLE first(id INTEGER NOT NULL PRIMARY KEY) STRICT;");

    private static SqlMigration Second() =>
        SqlMigration.Create("0002_second.sql", "CREATE TABLE second(id INTEGER NOT NULL PRIMARY KEY) STRICT;");

    [TestMethod]
    public void MigrationsApplyInOrder()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();

        MigrationOutcome outcome = fixture.Initialize(First(), Second());

        Assert.AreEqual(2, outcome.KnownCount);
        Assert.AreEqual(2, outcome.AppliedCount);
        Assert.AreEqual(2, outcome.CurrentVersion);
        Assert.IsTrue(fixture.TableExists("first"));
        Assert.IsTrue(fixture.TableExists("second"));
        Assert.AreEqual(2L, fixture.CountRows("schema_migrations"));
    }

    [TestMethod]
    public void SecondRunAppliesNothing()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(First(), Second());

        MigrationOutcome outcome = fixture.Initialize(First(), Second());

        Assert.AreEqual(0, outcome.AppliedCount);
        Assert.AreEqual(2L, fixture.CountRows("schema_migrations"));
    }

    [TestMethod]
    public void NewMigrationIsAppliedIncrementally()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(First());

        MigrationOutcome outcome = fixture.Initialize(First(), Second());

        Assert.AreEqual(1, outcome.AppliedCount);
        Assert.IsTrue(fixture.TableExists("second"));
    }

    [TestMethod]
    public void ChangedScriptOfAppliedMigrationAbortsStartup()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(First());

        SqlMigration tampered = SqlMigration.Create(
            "0001_initial.sql",
            "CREATE TABLE first(id INTEGER NOT NULL PRIMARY KEY, extra TEXT) STRICT;");

        PersistenceFailureException exception =
            Assert.ThrowsExactly<PersistenceFailureException>(() => fixture.Initialize(tampered));

        Assert.AreEqual(PersistenceFailureCode.MigrationChecksumMismatch, exception.Code);
    }

    [TestMethod]
    public void RenamedMigrationAbortsStartup()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqlMigration original = First();
        fixture.Initialize(original);

        SqlMigration renamed = SqlMigration.Create("0001_renamed.sql", original.Script);

        PersistenceFailureException exception =
            Assert.ThrowsExactly<PersistenceFailureException>(() => fixture.Initialize(renamed));

        Assert.AreEqual(PersistenceFailureCode.MigrationNameMismatch, exception.Code);
    }

    [TestMethod]
    public void DowngradedCatalogAbortsStartup()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(First(), Second());

        PersistenceFailureException exception =
            Assert.ThrowsExactly<PersistenceFailureException>(() => fixture.Initialize(First()));

        Assert.AreEqual(PersistenceFailureCode.MigrationMissing, exception.Code);
    }

    [TestMethod]
    public void NonContiguousVersionsAreRejected()
    {
        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => new MigrationRunner([First(), SqlMigration.Create("0003_third.sql", "SELECT 1;")]));

        Assert.AreEqual(PersistenceFailureCode.MigrationSequenceInvalid, exception.Code);
    }

    [TestMethod]
    public void EmptyCatalogStillCreatesHistoryTable()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();

        MigrationOutcome outcome = fixture.Initialize();

        Assert.AreEqual(0, outcome.CurrentVersion);
        Assert.IsTrue(fixture.TableExists("schema_migrations"));
    }

    [TestMethod]
    public void FailedMigrationLeavesNoPartialSchema()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqlMigration broken = SqlMigration.Create("0001_initial.sql", """
            CREATE TABLE good(id INTEGER NOT NULL PRIMARY KEY) STRICT;
            CREATE TABLE bad(id INTEGER NOT NULL PRIMARY KEY) STRICT;
            THIS IS NOT SQL;
            """);

        Assert.ThrowsExactly<SqliteException>(() => fixture.Initialize(broken));

        Assert.IsFalse(fixture.TableExists("good"));
        Assert.IsFalse(fixture.TableExists("bad"));
        Assert.AreEqual(0L, fixture.CountRows("schema_migrations"));
    }
}

[TestClass]
public sealed class SqliteDatabaseInitializerTests
{
    [TestMethod]
    public void MissingDirectoryIsCreated()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        string directory = fixture.Options.DirectoryPath!;

        Assert.IsFalse(Directory.Exists(directory));

        fixture.Initialize();

        Assert.IsTrue(Directory.Exists(directory));
        Assert.IsTrue(File.Exists(fixture.Options.FullPath));
    }

    [TestMethod]
    public void RuntimeReadinessPassesAfterInitialization()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqliteDatabaseInitializer initializer = fixture.CreateInitializer();
        initializer.Initialize(1_776_000_000_000);

        initializer.VerifyRuntimeReadiness();
    }
}

[TestClass]
public sealed class SingleInstanceLockTests
{
    [TestMethod]
    public void SecondAcquisitionIsRejectedWhileFirstIsHeld()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize();

        using SingleInstanceLock first = SingleInstanceLock.Acquire(fixture.Options);

        PersistenceFailureException exception = Assert.ThrowsExactly<PersistenceFailureException>(
            () => SingleInstanceLock.Acquire(fixture.Options));

        Assert.AreEqual(PersistenceFailureCode.SingleInstanceLockUnavailable, exception.Code);
    }

    [TestMethod]
    public void LockIsReacquirableAfterRelease()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize();

        SingleInstanceLock first = SingleInstanceLock.Acquire(fixture.Options);
        first.Dispose();

        using SingleInstanceLock second = SingleInstanceLock.Acquire(fixture.Options);
    }

    [TestMethod]
    public void DisposingTwiceIsSafe()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize();

        SingleInstanceLock instanceLock = SingleInstanceLock.Acquire(fixture.Options);
        instanceLock.Dispose();
        instanceLock.Dispose();
    }

    [TestMethod]
    public void AFreshDatabaseBecomesRuntimeReadyWithoutMigrations()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqliteDatabaseInitializer initializer = fixture.CreateInitializer();

        Assert.IsTrue(initializer.IsFreshDatabase);

        initializer.VerifyRuntimeReadiness();

        Assert.IsFalse(initializer.IsFreshDatabase);

        initializer.VerifyRuntimeReadiness();
    }

    [TestMethod]
    public void AnEmptyFileLeftByAFailedStartupIsStillFresh()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqliteDatabaseInitializer initializer = fixture.CreateInitializer();

        initializer.EnsureDirectory();
        File.WriteAllBytes(fixture.Options.FullPath, []);

        Assert.IsTrue(initializer.IsFreshDatabase);

        initializer.VerifyRuntimeReadiness();

        Assert.IsFalse(initializer.IsFreshDatabase);
    }

    [TestMethod]
    public void ADatabaseLeftOutsideWriteAheadLoggingIsConverted()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqliteDatabaseInitializer initializer = fixture.CreateInitializer();

        initializer.EnsureDirectory();

        using (SqliteConnection rollback = new(
            "Data Source=" + fixture.Options.FullPath + ";Pooling=False"))
        {
            rollback.Open();

            using SqliteCommand command = rollback.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode = DELETE; CREATE TABLE legacy(id INTEGER PRIMARY KEY) STRICT;";
            command.ExecuteNonQuery();
        }

        Assert.AreEqual("delete", JournalMode(fixture));
        Assert.IsFalse(initializer.IsFreshDatabase);

        initializer.VerifyRuntimeReadiness();

        Assert.AreEqual("wal", JournalMode(fixture));
    }

    [TestMethod]
    public void ConvertingToWriteAheadLoggingKeepsTheExistingRows()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        SqliteDatabaseInitializer initializer = fixture.CreateInitializer();

        initializer.EnsureDirectory();

        using (SqliteConnection rollback = new(
            "Data Source=" + fixture.Options.FullPath + ";Pooling=False"))
        {
            rollback.Open();

            using SqliteCommand command = rollback.CreateCommand();
            command.CommandText =
                "PRAGMA journal_mode = DELETE;"
                + "CREATE TABLE legacy(id INTEGER PRIMARY KEY) STRICT;"
                + "INSERT INTO legacy(id) VALUES(7);";
            command.ExecuteNonQuery();
        }

        initializer.VerifyRuntimeReadiness();

        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand read = connection.CreateCommand();
        read.CommandText = "SELECT id FROM legacy;";

        Assert.AreEqual(7L, (long)(read.ExecuteScalar() ?? 0L));
    }

    private static string JournalMode(SqliteDatabaseFixture fixture)
    {
        using SqliteConnection connection = new(
            "Data Source=" + fixture.Options.FullPath + ";Pooling=False");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        return command.ExecuteScalar() as string ?? string.Empty;
    }
}
