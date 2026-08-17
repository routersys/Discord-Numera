PRAGMA defer_foreign_keys = ON;

CREATE TABLE banks_rebuilt(
    bank_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    party_id BLOB NOT NULL UNIQUE REFERENCES parties(party_id) ON DELETE RESTRICT,
    institution_code TEXT NOT NULL UNIQUE CHECK(length(institution_code) BETWEEN 4 AND 16),
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 80),
    bank_kind TEXT NOT NULL CHECK(bank_kind IN ('NORMAL','BRIDGE')),
    resolution_case_id BLOB NULL CHECK(resolution_case_id IS NULL OR length(resolution_case_id) = 16),
    status TEXT NOT NULL CHECK(status IN ('PENDING_ACTIVATION','OPERATING','RESTRICTED','SETTLEMENT_SUSPENDED','RESOLUTION','CLOSING','CLOSED')),
    general_ledger_book_id BLOB NOT NULL UNIQUE REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    current_policy_version_id BLOB NULL
        REFERENCES bank_policy_versions(bank_policy_version_id) ON DELETE RESTRICT,
    current_fee_schedule_version_id BLOB NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((bank_kind = 'NORMAL' AND resolution_case_id IS NULL) OR (bank_kind = 'BRIDGE' AND resolution_case_id IS NOT NULL))
) STRICT;

INSERT INTO banks_rebuilt(
    bank_id,
    economy_scope_id,
    party_id,
    institution_code,
    name,
    bank_kind,
    resolution_case_id,
    status,
    general_ledger_book_id,
    current_policy_version_id,
    current_fee_schedule_version_id,
    created_at,
    version)
SELECT
    bank_id,
    economy_scope_id,
    party_id,
    institution_code,
    name,
    bank_kind,
    resolution_case_id,
    status,
    general_ledger_book_id,
    current_policy_version_id,
    current_fee_schedule_version_id,
    created_at,
    version
FROM banks;

DROP TABLE banks;

ALTER TABLE banks_rebuilt RENAME TO banks;

CREATE TABLE prudential_policy_versions(
    prudential_policy_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(prudential_policy_version_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    minimum_cet1_bps INTEGER NOT NULL CHECK(minimum_cet1_bps >= 450),
    lending_cet1_bps INTEGER NOT NULL CHECK(lending_cet1_bps >= 700 AND lending_cet1_bps >= minimum_cet1_bps),
    minimum_leverage_bps INTEGER NOT NULL CHECK(minimum_leverage_bps >= 300),
    configured_warning_leverage_bps INTEGER NOT NULL CHECK(configured_warning_leverage_bps >= minimum_leverage_bps),
    minimum_liquidity_bps INTEGER NOT NULL CHECK(minimum_liquidity_bps >= 10000),
    minimum_initial_bank_capital_minor INTEGER NOT NULL CHECK(minimum_initial_bank_capital_minor > 0),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, version),
    CHECK(status <> 'DRAFT' OR (published_at IS NULL AND retired_at IS NULL)),
    CHECK(status <> 'PUBLISHED' OR (published_at IS NOT NULL AND retired_at IS NULL)),
    CHECK(status <> 'RETIRED' OR (published_at IS NOT NULL AND retired_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_prudential_policy_current_economy
    ON prudential_policy_versions(economy_scope_id) WHERE status = 'PUBLISHED';

CREATE TABLE account_opening_applications(
    account_opening_application_id BLOB NOT NULL PRIMARY KEY CHECK(length(account_opening_application_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    product_version_id BLOB NOT NULL REFERENCES account_product_versions(product_version_id) ON DELETE RESTRICT,
    policy_version_id BLOB NOT NULL REFERENCES bank_policy_versions(bank_policy_version_id) ON DELETE RESTRICT,
    fee_schedule_version_id BLOB NOT NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    funding_source_deposit_account_id BLOB NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    funding_payment_order_id BLOB NULL REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    minimum_initial_funding_minor INTEGER NOT NULL CHECK(minimum_initial_funding_minor >= 0),
    opening_fee_minor INTEGER NOT NULL CHECK(opening_fee_minor >= 0),
    cash_card_issue_fee_minor INTEGER NOT NULL CHECK(cash_card_issue_fee_minor >= 0),
    debit_card_issue_fee_minor INTEGER NOT NULL CHECK(debit_card_issue_fee_minor >= 0),
    required_funding_minor INTEGER NOT NULL CHECK(required_funding_minor >= 0),
    automatic_bank_card_issue_mode TEXT NOT NULL
        CHECK(automatic_bank_card_issue_mode IN ('NONE','CASH_ONLY','INTEGRATED_CASH_DEBIT')),
    decision_mode TEXT NOT NULL CHECK(decision_mode IN ('AUTOMATIC','MANUAL')),
    status TEXT NOT NULL CHECK(status IN ('SUBMITTED','APPROVED','AWAITING_FUNDING','READY_TO_ACTIVATE','COMPLETED','REJECTED','CANCELLED','FAILED')),
    submitted_at INTEGER NOT NULL,
    decided_at INTEGER NULL,
    decided_by_discord_user_id TEXT NULL
        CHECK(decided_by_discord_user_id IS NULL OR length(decided_by_discord_user_id) BETWEEN 1 AND 20),
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(status <> 'SUBMITTED' OR (decided_at IS NULL AND deposit_account_id IS NULL)),
    CHECK(status NOT IN ('APPROVED','AWAITING_FUNDING','READY_TO_ACTIVATE','COMPLETED','REJECTED')
       OR decided_at IS NOT NULL),
    CHECK(status NOT IN ('AWAITING_FUNDING','READY_TO_ACTIVATE','COMPLETED') OR deposit_account_id IS NOT NULL),
    CHECK(status <> 'REJECTED' OR deposit_account_id IS NULL),
    CHECK((status = 'COMPLETED' AND completed_at IS NOT NULL) OR (status <> 'COMPLETED' AND completed_at IS NULL)),
    CHECK(automatic_bank_card_issue_mode <> 'NONE'
       OR (cash_card_issue_fee_minor = 0 AND debit_card_issue_fee_minor = 0)),
    CHECK(automatic_bank_card_issue_mode <> 'CASH_ONLY' OR debit_card_issue_fee_minor = 0)
) STRICT;

CREATE UNIQUE INDEX ux_account_opening_applications_pending
    ON account_opening_applications(bank_id, customer_account_id)
    WHERE status IN ('SUBMITTED','APPROVED','AWAITING_FUNDING','READY_TO_ACTIVATE');

CREATE INDEX ix_account_opening_applications_recovery
    ON account_opening_applications(status, account_opening_application_id);
