DROP INDEX ix_interaction_sessions_expiry;

DROP TABLE interaction_sessions;

CREATE TABLE interaction_sessions(
    interaction_session_id BLOB NOT NULL PRIMARY KEY CHECK(length(interaction_session_id) = 16),
    discord_user_id TEXT NOT NULL CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    guild_id TEXT NOT NULL CHECK(length(guild_id) BETWEEN 1 AND 20),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    flow_type TEXT NOT NULL CHECK(length(flow_type) BETWEEN 1 AND 64),
    state TEXT NOT NULL CHECK(length(state) BETWEEN 1 AND 64),
    token_hash BLOB NOT NULL UNIQUE CHECK(length(token_hash) = 32),
    payload_json TEXT NOT NULL CHECK(length(CAST(payload_json AS BLOB)) <= 32768),
    state_version INTEGER NOT NULL CHECK(state_version >= 0),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','COMPLETED','CANCELLED','EXPIRED','SUPERSEDED')),
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    CHECK(expires_at > created_at),
    CHECK((status = 'ACTIVE' AND completed_at IS NULL) OR (status <> 'ACTIVE' AND completed_at IS NOT NULL))
) STRICT;

CREATE INDEX ix_interaction_sessions_expiry ON interaction_sessions(status, expires_at);

CREATE INDEX ix_interaction_sessions_owner ON interaction_sessions(discord_user_id, status, created_at);
