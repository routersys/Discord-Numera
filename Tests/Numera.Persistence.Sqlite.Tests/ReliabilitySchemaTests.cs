using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class ReliabilitySchemaTests
{
    private static SqliteDatabaseFixture Initialized()
    {
        SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.CreateInitializer([.. EmbeddedMigrationCatalog.Load()]).Initialize(1_776_000_000_000);
        return fixture;
    }

    private static void Execute(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteException Rejects(SqliteDatabaseFixture fixture, string sql) =>
        Assert.ThrowsExactly<SqliteException>(() => Execute(fixture, sql));

    private static long Scalar(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

    [TestMethod]
    public void TheRemainingCanonicalTablesExist()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        foreach (string table in new[]
        {
            "inbox_events",
            "operation_results",
            "account_restrictions",
            "routing_aliases",
            "account_product_version_assignments",
            "interest_accruals",
            "interest_posting_batches",
            "reconciliation_runs",
            "reconciliation_issues",
            "authorization_decisions",
            "bank_assets",
            "resolution_transfers",
        })
        {
            Assert.IsTrue(fixture.TableExists(table), table);
        }
    }

    [TestMethod]
    public void AnInboxEventKeepsProcessedAtConsistentWithItsStatus()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Execute(fixture, $"""
            INSERT INTO inbox_events(inbox_event_id, source, external_event_key, received_at,
                processed_at, status, version)
            VALUES({Blob(1)}, 'DISCORD', 'evt-1', 10, NULL, 'RECEIVED', 1);
            """);

        Rejects(fixture, $"""
            INSERT INTO inbox_events(inbox_event_id, source, external_event_key, received_at,
                processed_at, status, version)
            VALUES({Blob(2)}, 'DISCORD', 'evt-2', 10, NULL, 'PROCESSED', 1);
            """);
    }

    [TestMethod]
    public void TheSameExternalEventKeyIsRejectedTwice()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Execute(fixture, $"""
            INSERT INTO inbox_events(inbox_event_id, source, external_event_key, received_at,
                processed_at, status, version)
            VALUES({Blob(3)}, 'DISCORD', 'evt-3', 10, NULL, 'RECEIVED', 1);
            """);

        Rejects(fixture, $"""
            INSERT INTO inbox_events(inbox_event_id, source, external_event_key, received_at,
                processed_at, status, version)
            VALUES({Blob(4)}, 'DISCORD', 'evt-3', 11, NULL, 'RECEIVED', 1);
            """);
    }

    [TestMethod]
    public void AnOperationResultRequiresItsBusinessOperation()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO operation_results(operation_result_id, business_operation_id, result_kind,
                result_json, created_at)
            VALUES({Blob(5)}, {Blob(6)}, 'TRANSFER', '[]', 10);
            """);
    }

    [TestMethod]
    public void AReconciliationIssueRequiresItsRun()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO reconciliation_issues(reconciliation_issue_id, reconciliation_run_id,
                issue_code, severity, target_type, target_id, detail, detected_at, resolved_at,
                resolution_business_operation_id)
            VALUES({Blob(7)}, {Blob(8)}, 'LEDGER_IMBALANCE', 'CRITICAL', 'ledger_accounts', NULL,
                'x', 10, NULL, NULL);
            """);
    }

    [TestMethod]
    public void AnInterestAccrualBelongsToExactlyOneSubject()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Rejects(fixture, $"""
            INSERT INTO interest_accruals(interest_accrual_id, deposit_account_id, loan_contract_id,
                product_version_id, currency_id, accrual_date, principal_minor, annual_rate_ppt,
                accrual_minor, residual_numerator, posted, created_at)
            VALUES({Blob(9)}, NULL, NULL, NULL, {Blob(10)}, '2026-08-19', 0, 0, 0, '0', 0, 10);
            """);
    }

    [TestMethod]
    public void ThePlacementAgreementReferencesAuthorizationDecisions()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        Assert.AreEqual(
            3L,
            Scalar(fixture, """
                SELECT COUNT(*) FROM pragma_foreign_key_list('atm_placement_agreements')
                WHERE "table" = 'authorization_decisions';
                """));
    }

    [TestMethod]
    public void TheSchemaStaysReferentiallyIntact()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";

        using SqliteDataReader reader = command.ExecuteReader();

        Assert.IsFalse(reader.Read());
    }
}
