PRAGMA defer_foreign_keys = ON;

CREATE TABLE idempotency_records_rebuilt(
    idempotency_record_id BLOB NOT NULL PRIMARY KEY CHECK(length(idempotency_record_id) = 16),
    idempotency_scope TEXT NOT NULL CHECK(length(idempotency_scope) BETWEEN 1 AND 64),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 1 AND 128),
    business_operation_id BLOB NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    operation_result_id BLOB NULL CHECK(operation_result_id IS NULL OR length(operation_result_id) = 16),
    status TEXT NOT NULL CHECK(status IN ('IN_PROGRESS','COMPLETED','FAILED')),
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    CHECK((status = 'IN_PROGRESS' AND completed_at IS NULL) OR (status <> 'IN_PROGRESS' AND completed_at IS NOT NULL))
) STRICT;

INSERT INTO idempotency_records_rebuilt(
    idempotency_record_id,
    idempotency_scope,
    idempotency_key,
    business_operation_id,
    operation_result_id,
    status,
    created_at,
    completed_at)
SELECT
    idempotency_record_id,
    idempotency_scope,
    idempotency_key,
    business_operation_id,
    operation_result_id,
    CASE WHEN completed_at IS NULL THEN 'IN_PROGRESS' ELSE 'COMPLETED' END,
    created_at,
    completed_at
FROM idempotency_records;

DROP TABLE idempotency_records;

ALTER TABLE idempotency_records_rebuilt RENAME TO idempotency_records;

CREATE UNIQUE INDEX ux_idempotency_records_scope_key
    ON idempotency_records(idempotency_scope, idempotency_key);
