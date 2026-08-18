CREATE TABLE inbox_events(
    inbox_event_id BLOB NOT NULL PRIMARY KEY CHECK(length(inbox_event_id) = 16),
    source TEXT NOT NULL CHECK(length(source) BETWEEN 1 AND 64),
    external_event_key TEXT NOT NULL CHECK(length(external_event_key) BETWEEN 1 AND 128),
    received_at INTEGER NOT NULL,
    processed_at INTEGER NULL,
    status TEXT NOT NULL CHECK(status IN ('RECEIVED','PROCESSED','FAILED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'RECEIVED' AND processed_at IS NULL)
        OR (status <> 'RECEIVED' AND processed_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_inbox_events_source_key
    ON inbox_events(source, external_event_key);

CREATE TABLE operation_results(
    operation_result_id BLOB NOT NULL PRIMARY KEY CHECK(length(operation_result_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    result_kind TEXT NOT NULL CHECK(length(result_kind) BETWEEN 1 AND 64),
    result_json TEXT NOT NULL CHECK(length(CAST(result_json AS BLOB)) <= 32768),
    created_at INTEGER NOT NULL
) STRICT;
