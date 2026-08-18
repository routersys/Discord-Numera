CREATE TABLE account_link_grants(
    account_link_grant_id BLOB NOT NULL PRIMARY KEY CHECK(length(account_link_grant_id) = 16),
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    code_digest BLOB NOT NULL CHECK(length(code_digest) = 32),
    status TEXT NOT NULL CHECK(status IN ('ISSUED','CONSUMED','EXPIRED','REVOKED')),
    issued_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    consumed_at INTEGER NULL,
    consumed_by_discord_user_id TEXT NULL CHECK(
        consumed_by_discord_user_id IS NULL OR length(consumed_by_discord_user_id) BETWEEN 1 AND 20),
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(expires_at > issued_at),
    CHECK((status = 'CONSUMED' AND consumed_at IS NOT NULL AND consumed_by_discord_user_id IS NOT NULL)
        OR (status <> 'CONSUMED' AND consumed_at IS NULL AND consumed_by_discord_user_id IS NULL))
) STRICT;

CREATE UNIQUE INDEX ux_account_link_grants_digest ON account_link_grants(code_digest);

CREATE INDEX ix_account_link_grants_customer
    ON account_link_grants(customer_account_id, status, expires_at);
