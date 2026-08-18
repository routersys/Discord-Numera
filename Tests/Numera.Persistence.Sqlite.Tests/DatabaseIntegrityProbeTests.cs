using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class DatabaseIntegrityProbeTests
{
    private static SqlMigration Migration(string sql) => SqlMigration.Create("0001_probe.sql", sql);

    [TestMethod]
    public void AHealthyDatabasePassesEveryProbe()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration("CREATE TABLE probe(id INTEGER PRIMARY KEY) STRICT;"));

        SqliteDatabaseIntegrityProbe probe = new(fixture.ConnectionFactory);

        Assert.IsTrue(probe.QuickCheck().IsOk);
        Assert.IsTrue(probe.ForeignKeyCheck().IsOk);
        Assert.IsTrue(probe.IntegrityCheck().IsOk);
    }

    [TestMethod]
    public void AViolatedForeignKeyIsReported()
    {
        using SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.Initialize(Migration("""
            CREATE TABLE parent(id INTEGER PRIMARY KEY) STRICT;

            CREATE TABLE child(
                id INTEGER PRIMARY KEY,
                parent_id INTEGER NOT NULL REFERENCES parent(id)
            ) STRICT;
            """));

        using (SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = OFF;
                INSERT INTO child(id, parent_id) VALUES(1, 99);
                """;
            command.ExecuteNonQuery();
        }

        SqliteDatabaseIntegrityProbe probe = new(fixture.ConnectionFactory);
        DatabaseProbeResult result = probe.ForeignKeyCheck();

        Assert.AreEqual(DatabaseProbeStatus.Failed, result.Status);
        Assert.AreEqual("1", result.Detail);
        Assert.IsTrue(probe.QuickCheck().IsOk);
    }

    [TestMethod]
    public void TheOkResultCarriesTheCanonicalToken() =>
        Assert.AreEqual("ok", DatabaseProbeResult.Ok.Detail);
}
