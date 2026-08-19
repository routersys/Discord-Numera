using Microsoft.Data.Sqlite;
using Numera.Persistence.Sqlite.Migrations;

namespace Numera.Persistence.Sqlite.Tests;

[TestClass]
public sealed class DatabaseReconciliationRunnerTests
{
    private const long Now = 1_776_000_000_000;

    private const string EmptyJson = "'{}'";

    private static SqliteDatabaseFixture Initialized()
    {
        SqliteDatabaseFixture fixture = SqliteDatabaseFixture.Create();
        fixture.CreateInitializer([.. EmbeddedMigrationCatalog.Load()]).Initialize(Now);
        return fixture;
    }

    private static SqliteDatabaseReconciliationRunner Runner(SqliteDatabaseFixture fixture)
    {
        int next = 0x40;

        return new SqliteDatabaseReconciliationRunner(
            fixture.ConnectionFactory,
            () =>
            {
                byte[] id = new byte[16];
                id[15] = (byte)next++;
                return id;
            });
    }

    private static void Execute(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ReadText(SqliteDatabaseFixture fixture, string sql)
    {
        using SqliteConnection connection = fixture.ConnectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    private static string Blob(int seed) => $"x'{new string('0', 30)}{seed:x2}'";

    private static void SeedBook(SqliteDatabaseFixture fixture) => Execute(fixture, $"""
        INSERT INTO guild_economies(economy_scope_id, guild_id, canonical_timezone, status, version)
        VALUES({Blob(1)}, '900', 'Asia/Tokyo', 'ACTIVE', 1);

        INSERT INTO currencies(currency_id, economy_scope_id, status, minor_unit_digits,
            base_money_supply_cap_minor, created_at, retired_at, version)
        VALUES({Blob(2)}, {Blob(1)}, 'ACTIVE', 2, NULL, 1, NULL, 1);

        INSERT INTO parties(party_id, party_type, display_name, status, created_at, version)
        VALUES({Blob(3)}, 'BANK', '銀行主体', 'ACTIVE', 1, 1);

        INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
            created_at, version)
        VALUES({Blob(4)}, {Blob(3)}, 'COMMERCIAL_BANK', 'OPEN', 1, 1);

        INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
            starts_on, ends_on, status, closed_at, version)
        VALUES({Blob(5)}, {Blob(4)}, '2026', '2000-01-01', '2100-12-31', 'OPEN', NULL, 1);

        INSERT INTO business_operations(business_operation_id, operation_type, economy_scope_id,
            actor_party_id, correlation_id, idempotency_scope, idempotency_key, status,
            created_at, committed_at, version)
        VALUES({Blob(6)}, 'TEST', {Blob(1)}, {Blob(3)}, {Blob(7)}, 'TEST', 'k1', 'COMMITTED', 1, 1, 1);

        INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
            account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
            owner_reference_type, owner_reference_id, status, created_at, version)
        VALUES({Blob(10)}, {Blob(4)}, NULL, '1000', 'CASH_ASSET', 'ASSET', 'DEBIT',
            {Blob(2)}, 1, NULL, NULL, 'ACTIVE', 1, 1);

        INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id,
            account_code, account_kind, accounting_type, normal_side, currency_id, posting_allowed,
            owner_reference_type, owner_reference_id, status, created_at, version)
        VALUES({Blob(11)}, {Blob(4)}, NULL, '2000', 'DEMAND_DEPOSIT_CONTROL', 'LIABILITY', 'CREDIT',
            {Blob(2)}, 0, NULL, NULL, 'ACTIVE', 1, 1);
        """);

    private static void SeedBalancedTransaction(SqliteDatabaseFixture fixture) => Execute(fixture, $"""
        INSERT INTO accounting_transactions(accounting_transaction_id, accounting_book_id,
            accounting_period_id, business_operation_id, currency_id, transaction_type,
            business_date, occurred_at, posted_at, reverses_transaction_id, status, version)
        VALUES({Blob(20)}, {Blob(4)}, {Blob(5)}, {Blob(6)}, {Blob(2)}, 'TEST',
            '2026-04-12', 1, 1, NULL, 'POSTED', 1);

        INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id, ledger_account_id,
            entry_sequence, side, amount_minor, created_at)
        VALUES({Blob(21)}, {Blob(20)}, {Blob(10)}, 0, 'DEBIT', 500, 1);

        INSERT INTO journal_entries(journal_entry_id, accounting_transaction_id, ledger_account_id,
            entry_sequence, side, amount_minor, created_at)
        VALUES({Blob(22)}, {Blob(20)}, {Blob(11)}, 1, 'CREDIT', 500, 1);

        INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
            version, updated_at)
        VALUES({Blob(10)}, 500, 0, 1, 1);

        INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
            version, updated_at)
        VALUES({Blob(11)}, 500, 0, 1, 1);
        """);

    [TestMethod]
    public void AnEmptyDatabaseReconcilesCleanly()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        ReconciliationOutcome outcome = Runner(fixture).RunFinancialReconciliation(Now);

        Assert.IsTrue(outcome.IsOk);
        Assert.AreEqual("ok", outcome.Detail);
        Assert.AreEqual(1L, fixture.CountRows("reconciliation_runs"));
        Assert.AreEqual(0L, fixture.CountRows("reconciliation_issues"));
        Assert.AreEqual("SUCCEEDED", ReadText(fixture, "SELECT status FROM reconciliation_runs;"));
    }

    [TestMethod]
    public void ABalancedLedgerProducesNoIssue()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedBook(fixture);
        SeedBalancedTransaction(fixture);

        ReconciliationOutcome outcome = Runner(fixture).RunFinancialReconciliation(Now);

        Assert.IsTrue(outcome.IsOk);
        Assert.AreEqual(0L, fixture.CountRows("reconciliation_issues"));
    }

    [TestMethod]
    public void AnUnbalancedTransactionIsRecordedAsCritical()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedBook(fixture);
        SeedBalancedTransaction(fixture);
        Execute(fixture, $"""
            UPDATE journal_entries SET amount_minor = 400 WHERE journal_entry_id = {Blob(22)};
            UPDATE ledger_balance_projections SET posted_balance_minor = 400
            WHERE ledger_account_id = {Blob(11)};
            """);

        ReconciliationOutcome outcome = Runner(fixture).RunFinancialReconciliation(Now);

        Assert.IsFalse(outcome.IsOk);
        Assert.AreEqual("ISSUES_FOUND", ReadText(fixture, "SELECT status FROM reconciliation_runs;"));
        Assert.AreEqual(
            SqliteDatabaseReconciliationRunner.IssueTransactionUnbalanced,
            ReadText(fixture, "SELECT issue_code FROM reconciliation_issues LIMIT 1;"));
        Assert.AreEqual(
            "CRITICAL",
            ReadText(fixture, "SELECT severity FROM reconciliation_issues LIMIT 1;"));
    }

    [TestMethod]
    public void AProjectionThatDivergesFromTheJournalIsDetected()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedBook(fixture);
        SeedBalancedTransaction(fixture);
        Execute(fixture, $"""
            UPDATE ledger_balance_projections SET posted_balance_minor = 501
            WHERE ledger_account_id = {Blob(10)};
            """);

        ReconciliationOutcome outcome = Runner(fixture).RunFinancialReconciliation(Now);

        Assert.IsFalse(outcome.IsOk);
        Assert.AreEqual(
            SqliteDatabaseReconciliationRunner.IssuePostedBalanceMismatch,
            ReadText(fixture, "SELECT issue_code FROM reconciliation_issues LIMIT 1;"));
    }

    [TestMethod]
    public void ExpiredOutboxClaimsAreReturnedToRetryDue()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedBook(fixture);
        Execute(fixture, $"""
            INSERT INTO outbox_events(outbox_event_id, business_operation_id, event_type, payload_json,
                status, claim_token, claimed_at, claim_expires_at, next_attempt_at, created_at,
                published_at, attempt_count, last_error_code, version)
            VALUES({Blob(30)}, {Blob(6)}, 'TEST', {EmptyJson}, 'CLAIMED', {Blob(31)}, 1, {Now - 1}, NULL,
                1, NULL, 1, NULL, 1);

            INSERT INTO outbox_events(outbox_event_id, business_operation_id, event_type, payload_json,
                status, claim_token, claimed_at, claim_expires_at, next_attempt_at, created_at,
                published_at, attempt_count, last_error_code, version)
            VALUES({Blob(32)}, {Blob(6)}, 'TEST', {EmptyJson}, 'CLAIMED', {Blob(33)}, 1, {Now + 60_000}, NULL,
                1, NULL, 1, NULL, 1);
            """);

        LeaseRecoveryOutcome outcome = Runner(fixture).RecoverExpiredLeases(Now);

        Assert.AreEqual(1, outcome.OutboxClaims);
        Assert.AreEqual(
            "RETRY_DUE",
            ReadText(fixture, $"SELECT status FROM outbox_events WHERE outbox_event_id = {Blob(30)};"));
        Assert.AreEqual(
            "CLAIMED",
            ReadText(fixture, $"SELECT status FROM outbox_events WHERE outbox_event_id = {Blob(32)};"));
    }

    [TestMethod]
    public void RecoveringLeasesTwiceIsIdempotent()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SeedBook(fixture);
        Execute(fixture, $"""
            INSERT INTO outbox_events(outbox_event_id, business_operation_id, event_type, payload_json,
                status, claim_token, claimed_at, claim_expires_at, next_attempt_at, created_at,
                published_at, attempt_count, last_error_code, version)
            VALUES({Blob(30)}, {Blob(6)}, 'TEST', {EmptyJson}, 'CLAIMED', {Blob(31)}, 1, {Now - 1}, NULL,
                1, NULL, 1, NULL, 1);
            """);

        SqliteDatabaseReconciliationRunner runner = Runner(fixture);

        Assert.AreEqual(1, runner.RecoverExpiredLeases(Now).OutboxClaims);
        Assert.AreEqual(0, runner.RecoverExpiredLeases(Now).OutboxClaims);
    }

    [TestMethod]
    public void TheLastRunStatusIsReadable()
    {
        using SqliteDatabaseFixture fixture = Initialized();
        SqliteDatabaseReconciliationRunner runner = Runner(fixture);

        Assert.IsNull(runner.LastRunStatus(SqliteDatabaseReconciliationRunner.ScopeFinancial));

        runner.RunFinancialReconciliation(Now);

        Assert.AreEqual(
            "SUCCEEDED", runner.LastRunStatus(SqliteDatabaseReconciliationRunner.ScopeFinancial));
        Assert.IsNull(runner.LastRunStatus(SqliteDatabaseReconciliationRunner.ScopeOrphan));
    }

    [TestMethod]
    public void OrphanVerificationPassesOnAnEmptyDatabase()
    {
        using SqliteDatabaseFixture fixture = Initialized();

        ReconciliationOutcome outcome = Runner(fixture).VerifyNoOrphanState(Now);

        Assert.IsTrue(outcome.IsOk);
        Assert.AreEqual(
            SqliteDatabaseReconciliationRunner.ScopeOrphan,
            ReadText(fixture, "SELECT scope_type FROM reconciliation_runs;"));
    }
}
