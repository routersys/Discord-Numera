CREATE TABLE bank_operator_grants(
    bank_operator_grant_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_operator_grant_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    discord_user_id TEXT NOT NULL CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','REVOKED')),
    granted_by_discord_user_id TEXT NOT NULL CHECK(length(granted_by_discord_user_id) BETWEEN 1 AND 20),
    granted_at INTEGER NOT NULL,
    revoked_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'ACTIVE' AND revoked_at IS NULL) OR (status = 'REVOKED' AND revoked_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_bank_operator_grants_active
    ON bank_operator_grants(bank_id, discord_user_id) WHERE status = 'ACTIVE';
