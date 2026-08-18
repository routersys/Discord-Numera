CREATE TABLE reconciliation_runs(
    reconciliation_run_id BLOB NOT NULL PRIMARY KEY CHECK(length(reconciliation_run_id) = 16),
    scope_type TEXT NOT NULL CHECK(length(scope_type) BETWEEN 1 AND 64),
    scope_id BLOB NULL CHECK(scope_id IS NULL OR length(scope_id) = 16),
    started_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    status TEXT NOT NULL CHECK(status IN ('RUNNING','SUCCEEDED','FAILED','ISSUES_FOUND')),
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'RUNNING' AND completed_at IS NULL)
        OR (status <> 'RUNNING' AND completed_at IS NOT NULL))
) STRICT;

CREATE INDEX ix_reconciliation_runs_scope
    ON reconciliation_runs(scope_type, scope_id, started_at);

CREATE TABLE reconciliation_issues(
    reconciliation_issue_id BLOB NOT NULL PRIMARY KEY CHECK(length(reconciliation_issue_id) = 16),
    reconciliation_run_id BLOB NOT NULL
        REFERENCES reconciliation_runs(reconciliation_run_id) ON DELETE RESTRICT,
    issue_code TEXT NOT NULL CHECK(length(issue_code) BETWEEN 1 AND 64),
    severity TEXT NOT NULL CHECK(severity IN ('WARNING','ERROR','CRITICAL')),
    target_type TEXT NOT NULL CHECK(length(target_type) BETWEEN 1 AND 64),
    target_id BLOB NULL CHECK(target_id IS NULL OR length(target_id) = 16),
    detail TEXT NOT NULL CHECK(length(CAST(detail AS BLOB)) <= 4096),
    detected_at INTEGER NOT NULL,
    resolved_at INTEGER NULL,
    resolution_business_operation_id BLOB NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    CHECK(resolved_at IS NULL OR resolved_at >= detected_at)
) STRICT;

CREATE INDEX ix_reconciliation_issues_run
    ON reconciliation_issues(reconciliation_run_id, detected_at);
