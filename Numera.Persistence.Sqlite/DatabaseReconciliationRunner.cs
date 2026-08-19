using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Numera.Persistence.Sqlite;

public sealed record ReconciliationFinding(
    string IssueCode,
    string Severity,
    string TargetType,
    byte[]? TargetId,
    string Detail);

public sealed record ReconciliationOutcome(
    bool IsOk,
    string Detail,
    IReadOnlyList<ReconciliationFinding> Findings)
{
    public static ReconciliationOutcome Ok { get; } = new(true, OkDetail, []);

    public const string OkDetail = "ok";

    public static ReconciliationOutcome Issues(IReadOnlyList<ReconciliationFinding> findings) =>
        new(
            false,
            findings.Count.ToString(CultureInfo.InvariantCulture),
            findings);
}

public sealed record LeaseRecoveryOutcome(int OutboxClaims);

public interface IDatabaseReconciliationRunner
{
    ReconciliationOutcome RunFinancialReconciliation(long nowMilliseconds);

    LeaseRecoveryOutcome RecoverExpiredLeases(long nowMilliseconds);

    ReconciliationOutcome VerifyNoOrphanState(long nowMilliseconds);

    string? LastRunStatus(string scopeType);
}

public sealed class SqliteDatabaseReconciliationRunner : IDatabaseReconciliationRunner
{
    public const string ScopeFinancial = "FINANCIAL_STARTUP";
    public const string ScopeOrphan = "ORPHAN_STARTUP";

    public const string SeverityError = "ERROR";
    public const string SeverityCritical = "CRITICAL";

    public const string IssueTransactionUnbalanced = "TRANSACTION_UNBALANCED";
    public const string IssuePostedBalanceMismatch = "POSTED_BALANCE_MISMATCH";
    public const string IssueHeldBalanceMismatch = "HELD_BALANCE_MISMATCH";
    public const string IssueDepositControlMismatch = "DEPOSIT_CONTROL_MISMATCH";
    public const string IssueDepositBalanceInvalid = "DEPOSIT_BALANCE_INVALID";
    public const string IssueOrphanHold = "ORPHAN_HOLD";
    public const string IssueClosedAccountPendingPayment = "CLOSED_ACCOUNT_PENDING_PAYMENT";
    public const string IssueImpossibleTerminalState = "IMPOSSIBLE_TERMINAL_STATE";

    private const string TargetTransaction = "ACCOUNTING_TRANSACTION";
    private const string TargetLedgerAccount = "LEDGER_ACCOUNT";
    private const string TargetDepositAccount = "DEPOSIT_ACCOUNT";
    private const string TargetHold = "HOLD";
    private const string TargetPaymentOrder = "PAYMENT_ORDER";

    private readonly SqliteConnectionFactory connectionFactory;
    private readonly Func<byte[]> idFactory;

    public SqliteDatabaseReconciliationRunner(
        SqliteConnectionFactory connectionFactory,
        Func<byte[]> idFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(idFactory);

        this.connectionFactory = connectionFactory;
        this.idFactory = idFactory;
    }

    public ReconciliationOutcome RunFinancialReconciliation(long nowMilliseconds) =>
        Run(ScopeFinancial, nowMilliseconds, CollectFinancialFindings);

    public ReconciliationOutcome VerifyNoOrphanState(long nowMilliseconds) =>
        Run(ScopeOrphan, nowMilliseconds, CollectOrphanFindings);

    public string? LastRunStatus(string scopeType)
    {
        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT status FROM reconciliation_runs
            WHERE scope_type = $scope
            ORDER BY started_at DESC, reconciliation_run_id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", scopeType);

        return command.ExecuteScalar() as string;
    }

    public LeaseRecoveryOutcome RecoverExpiredLeases(long nowMilliseconds)
    {
        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_events
            SET status = 'RETRY_DUE',
                claim_token = NULL,
                claimed_at = NULL,
                claim_expires_at = NULL,
                next_attempt_at = $now,
                version = version + 1
            WHERE status = 'CLAIMED' AND claim_expires_at <= $now;
            """;
        command.Parameters.AddWithValue("$now", nowMilliseconds);

        int recovered = command.ExecuteNonQuery();
        transaction.Commit();

        return new LeaseRecoveryOutcome(recovered);
    }

    private ReconciliationOutcome Run(
        string scopeType,
        long nowMilliseconds,
        Func<SqliteConnection, SqliteTransaction, List<ReconciliationFinding>> collect)
    {
        using SqliteConnection connection = connectionFactory.OpenRuntimeConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        byte[] runId = idFactory();

        Execute(
            connection,
            transaction,
            """
            INSERT INTO reconciliation_runs(reconciliation_run_id, scope_type, scope_id, started_at,
                completed_at, status, version)
            VALUES($id, $scope, NULL, $now, NULL, 'RUNNING', 1);
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", runId);
                command.Parameters.AddWithValue("$scope", scopeType);
                command.Parameters.AddWithValue("$now", nowMilliseconds);
            });

        List<ReconciliationFinding> findings = collect(connection, transaction);

        foreach (ReconciliationFinding finding in findings)
        {
            Execute(
                connection,
                transaction,
                """
                INSERT INTO reconciliation_issues(reconciliation_issue_id, reconciliation_run_id,
                    issue_code, severity, target_type, target_id, detail, detected_at, resolved_at,
                    resolution_business_operation_id)
                VALUES($id, $run, $code, $severity, $targetType, $targetId, $detail, $now, NULL, NULL);
                """,
                command =>
                {
                    command.Parameters.AddWithValue("$id", idFactory());
                    command.Parameters.AddWithValue("$run", runId);
                    command.Parameters.AddWithValue("$code", finding.IssueCode);
                    command.Parameters.AddWithValue("$severity", finding.Severity);
                    command.Parameters.AddWithValue("$targetType", finding.TargetType);
                    command.Parameters.AddWithValue("$targetId", (object?)finding.TargetId ?? DBNull.Value);
                    command.Parameters.AddWithValue("$detail", finding.Detail);
                    command.Parameters.AddWithValue("$now", nowMilliseconds);
                });
        }

        Execute(
            connection,
            transaction,
            """
            UPDATE reconciliation_runs
            SET status = $status, completed_at = $now, version = version + 1
            WHERE reconciliation_run_id = $id;
            """,
            command =>
            {
                command.Parameters.AddWithValue("$status", findings.Count == 0 ? "SUCCEEDED" : "ISSUES_FOUND");
                command.Parameters.AddWithValue("$now", nowMilliseconds);
                command.Parameters.AddWithValue("$id", runId);
            });

        transaction.Commit();

        return findings.Count == 0 ? ReconciliationOutcome.Ok : ReconciliationOutcome.Issues(findings);
    }

    private static List<ReconciliationFinding> CollectFinancialFindings(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        List<ReconciliationFinding> findings = [];

        Collect(
            connection,
            transaction,
            """
            SELECT accounting_transaction_id,
                   SUM(CASE WHEN side = 'DEBIT' THEN amount_minor ELSE -amount_minor END)
            FROM journal_entries
            GROUP BY accounting_transaction_id
            HAVING SUM(CASE WHEN side = 'DEBIT' THEN amount_minor ELSE -amount_minor END) <> 0;
            """,
            IssueTransactionUnbalanced,
            SeverityCritical,
            TargetTransaction,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT a.ledger_account_id,
                   COALESCE(p.posted_balance_minor, 0) - COALESCE(j.signed_total, 0)
            FROM ledger_accounts AS a
            LEFT JOIN ledger_balance_projections AS p ON p.ledger_account_id = a.ledger_account_id
            LEFT JOIN (
                SELECT e.ledger_account_id AS ledger_account_id,
                       SUM(CASE WHEN e.side = 'DEBIT' THEN e.amount_minor ELSE -e.amount_minor END)
                           AS signed_total
                FROM journal_entries AS e
                GROUP BY e.ledger_account_id
            ) AS j ON j.ledger_account_id = a.ledger_account_id
            WHERE COALESCE(p.posted_balance_minor, 0) <> CASE
                WHEN a.normal_side = 'DEBIT' THEN COALESCE(j.signed_total, 0)
                ELSE -COALESCE(j.signed_total, 0)
            END;
            """,
            IssuePostedBalanceMismatch,
            SeverityCritical,
            TargetLedgerAccount,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT d.deposit_account_id,
                   COALESCE(p.held_minor, 0) - COALESCE(h.active_total, 0)
            FROM deposit_accounts AS d
            LEFT JOIN ledger_balance_projections AS p ON p.ledger_account_id = d.ledger_account_id
            LEFT JOIN (
                SELECT x.deposit_account_id AS deposit_account_id, SUM(x.remaining_minor) AS active_total
                FROM holds AS x
                WHERE x.status = 'ACTIVE' AND x.hold_scope_kind = 'CUSTOMER_DEPOSIT'
                GROUP BY x.deposit_account_id
            ) AS h ON h.deposit_account_id = d.deposit_account_id
            WHERE COALESCE(p.held_minor, 0) <> COALESCE(h.active_total, 0);
            """,
            IssueHeldBalanceMismatch,
            SeverityError,
            TargetDepositAccount,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT d.deposit_account_id, 1
            FROM deposit_accounts AS d
            JOIN ledger_accounts AS a ON a.ledger_account_id = d.ledger_account_id
            LEFT JOIN ledger_accounts AS control
                ON control.ledger_account_id = a.parent_account_id
               AND control.account_kind = 'DEMAND_DEPOSIT_CONTROL'
               AND control.accounting_book_id = a.accounting_book_id
               AND control.currency_id = a.currency_id
            WHERE control.ledger_account_id IS NULL;
            """,
            IssueDepositControlMismatch,
            SeverityCritical,
            TargetDepositAccount,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT d.deposit_account_id, COALESCE(p.posted_balance_minor, 0)
            FROM deposit_accounts AS d
            LEFT JOIN ledger_balance_projections AS p ON p.ledger_account_id = d.ledger_account_id
            WHERE COALESCE(p.posted_balance_minor, 0) < 0
               OR COALESCE(p.held_minor, 0) > COALESCE(p.posted_balance_minor, 0);
            """,
            IssueDepositBalanceInvalid,
            SeverityCritical,
            TargetDepositAccount,
            findings);

        return findings;
    }

    private static List<ReconciliationFinding> CollectOrphanFindings(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        List<ReconciliationFinding> findings = [];

        Collect(
            connection,
            transaction,
            """
            SELECT h.hold_id, h.remaining_minor
            FROM holds AS h
            JOIN deposit_accounts AS d ON d.deposit_account_id = h.deposit_account_id
            WHERE h.status = 'ACTIVE'
              AND d.status IN ('CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION');
            """,
            IssueOrphanHold,
            SeverityCritical,
            TargetHold,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT o.payment_order_id, 1
            FROM payment_orders AS o
            JOIN deposit_accounts AS d ON d.deposit_account_id = o.source_deposit_account_id
            WHERE o.status NOT IN ('COMPLETED','FAILED','CANCELLED')
              AND d.status IN ('CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION');
            """,
            IssueClosedAccountPendingPayment,
            SeverityError,
            TargetPaymentOrder,
            findings);

        Collect(
            connection,
            transaction,
            """
            SELECT d.deposit_account_id, COALESCE(p.held_minor, 0)
            FROM deposit_accounts AS d
            LEFT JOIN ledger_balance_projections AS p ON p.ledger_account_id = d.ledger_account_id
            WHERE d.status IN ('CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION')
              AND (COALESCE(p.posted_balance_minor, 0) <> 0 OR COALESCE(p.held_minor, 0) <> 0);
            """,
            IssueImpossibleTerminalState,
            SeverityCritical,
            TargetDepositAccount,
            findings);

        return findings;
    }

    private static void Collect(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        string issueCode,
        string severity,
        string targetType,
        List<ReconciliationFinding> findings)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            findings.Add(new ReconciliationFinding(
                issueCode,
                severity,
                targetType,
                reader.IsDBNull(0) ? null : reader.GetFieldValue<byte[]>(0),
                reader.GetInt64(1).ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Action<SqliteCommand> bind)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        bind(command);
        command.ExecuteNonQuery();
    }
}
