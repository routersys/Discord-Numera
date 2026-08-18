CREATE TABLE account_restrictions(
    account_restriction_id BLOB NOT NULL PRIMARY KEY CHECK(length(account_restriction_id) = 16),
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    restriction_type TEXT NOT NULL CHECK(length(restriction_type) BETWEEN 1 AND 64),
    scope TEXT NOT NULL CHECK(length(scope) BETWEEN 1 AND 64),
    amount_limit_minor INTEGER NULL CHECK(amount_limit_minor IS NULL OR amount_limit_minor >= 0),
    reason_code TEXT NOT NULL CHECK(length(reason_code) BETWEEN 1 AND 64),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL CHECK(effective_to IS NULL OR effective_to > effective_from),
    created_by_discord_user_id TEXT NOT NULL CHECK(length(created_by_discord_user_id) BETWEEN 1 AND 20),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_account_restrictions_account
    ON account_restrictions(deposit_account_id, effective_from);

CREATE TABLE routing_aliases(
    routing_alias_id BLOB NOT NULL PRIMARY KEY CHECK(length(routing_alias_id) = 16),
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    institution_code TEXT NOT NULL CHECK(length(institution_code) BETWEEN 1 AND 16),
    branch_code TEXT NOT NULL CHECK(length(branch_code) BETWEEN 1 AND 16),
    account_number TEXT NOT NULL CHECK(length(account_number) BETWEEN 1 AND 32),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL CHECK(effective_to IS NULL OR effective_to > effective_from),
    forwarding_deposit_account_id BLOB NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(forwarding_deposit_account_id IS NULL
        OR forwarding_deposit_account_id <> deposit_account_id)
) STRICT;

CREATE UNIQUE INDEX ux_routing_aliases_current
    ON routing_aliases(institution_code, branch_code, account_number)
    WHERE effective_to IS NULL;

CREATE TABLE account_product_version_assignments(
    account_product_version_assignment_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(account_product_version_assignment_id) = 16),
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    product_version_id BLOB NOT NULL
        REFERENCES account_product_versions(product_version_id) ON DELETE RESTRICT,
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL CHECK(effective_to IS NULL OR effective_to > effective_from),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE UNIQUE INDEX ux_account_product_version_assignments_current
    ON account_product_version_assignments(deposit_account_id)
    WHERE effective_to IS NULL;
