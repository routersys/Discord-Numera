CREATE TABLE payment_networks(
    payment_network_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_network_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    network_code TEXT NOT NULL CHECK(length(network_code) BETWEEN 1 AND 32 AND network_code NOT GLOB '*[^A-Z0-9_]*'),
    operator_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    accounting_book_id BLOB NOT NULL REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    liquid_asset_ledger_account_id BLOB NOT NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    current_policy_version_id BLOB NULL REFERENCES payment_network_policy_versions(payment_network_policy_version_id) ON DELETE RESTRICT,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, network_code),
    CHECK(status <> 'DRAFT' OR current_policy_version_id IS NULL),
    CHECK(status = 'DRAFT' OR status = 'RETIRED' OR current_policy_version_id IS NOT NULL)
) STRICT;

CREATE UNIQUE INDEX ux_payment_networks_active_economy
    ON payment_networks(economy_scope_id) WHERE status = 'ACTIVE';

CREATE TABLE payment_network_policy_versions(
    payment_network_policy_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_network_policy_version_id) = 16),
    payment_network_id BLOB NOT NULL REFERENCES payment_networks(payment_network_id) ON DELETE RESTRICT,
    settlement_mode TEXT NOT NULL CHECK(settlement_mode IN ('RTGS','CLEARING')),
    beneficiary_posting_policy TEXT NOT NULL CHECK(beneficiary_posting_policy IN ('AFTER_FINAL_SETTLEMENT','GUARANTEED_PRE_CREDIT')),
    rtgs_threshold_minor INTEGER NULL CHECK(rtgs_threshold_minor IS NULL OR rtgs_threshold_minor >= 0),
    clearing_cycle_interval_seconds INTEGER NULL CHECK(clearing_cycle_interval_seconds IS NULL OR clearing_cycle_interval_seconds BETWEEN 60 AND 86400),
    precredit_enabled INTEGER NOT NULL CHECK(precredit_enabled IN (0, 1)),
    precredit_prefund_ratio_bps INTEGER NOT NULL CHECK(precredit_prefund_ratio_bps >= 10000),
    per_bank_precredit_exposure_limit_minor INTEGER NOT NULL CHECK(per_bank_precredit_exposure_limit_minor >= 0),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(payment_network_id, version),
    CHECK((beneficiary_posting_policy = 'GUARANTEED_PRE_CREDIT' AND settlement_mode = 'CLEARING' AND precredit_enabled = 1)
       OR (beneficiary_posting_policy = 'AFTER_FINAL_SETTLEMENT' AND precredit_enabled = 0)),
    CHECK(settlement_mode <> 'RTGS' OR beneficiary_posting_policy = 'AFTER_FINAL_SETTLEMENT'),
    CHECK(settlement_mode <> 'CLEARING' OR clearing_cycle_interval_seconds IS NOT NULL)
) STRICT;

CREATE TABLE payment_network_prefunds(
    payment_network_prefund_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_network_prefund_id) = 16),
    payment_network_id BLOB NOT NULL REFERENCES payment_networks(payment_network_id) ON DELETE RESTRICT,
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    prefund_liability_ledger_account_id BLOB NOT NULL UNIQUE REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(payment_network_id, bank_id, currency_id)
) STRICT;

PRAGMA defer_foreign_keys = ON;

CREATE TABLE payment_orders_rebuilt(
    payment_order_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_order_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    payer_customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    destination_deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    method TEXT NOT NULL CHECK(length(method) BETWEEN 1 AND 32),
    settlement_mode TEXT NOT NULL CHECK(settlement_mode IN ('INTERNAL','RTGS','CLEARING')),
    beneficiary_posting_policy TEXT NOT NULL CHECK(beneficiary_posting_policy IN ('IMMEDIATE_AFTER_ACCEPTANCE','AFTER_FINAL_SETTLEMENT','GUARANTEED_PRE_CREDIT')),
    payment_network_policy_version_id BLOB NULL REFERENCES payment_network_policy_versions(payment_network_policy_version_id) ON DELETE RESTRICT,
    memo TEXT NULL CHECK(memo IS NULL OR length(memo) <= 100),
    status TEXT NOT NULL CHECK(status IN ('CREATED','AUTHORIZED','FUNDS_HELD','ACCEPTED','QUEUED','SETTLING','SETTLED','COMPLETED','FAILED','CANCELLED')),
    beneficiary_posted_at INTEGER NULL,
    settlement_finalized_at INTEGER NULL,
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(source_deposit_account_id <> destination_deposit_account_id),
    CHECK(settlement_mode <> 'INTERNAL' OR payment_network_policy_version_id IS NULL),
    CHECK(settlement_mode <> 'CLEARING' OR payment_network_policy_version_id IS NOT NULL),
    CHECK(beneficiary_posting_policy <> 'GUARANTEED_PRE_CREDIT' OR settlement_mode = 'CLEARING'),
    CHECK(settlement_mode = 'INTERNAL' OR status NOT IN ('SETTLED','COMPLETED') OR settlement_finalized_at IS NOT NULL),
    CHECK(status <> 'COMPLETED' OR beneficiary_posted_at IS NOT NULL),
    CHECK(beneficiary_posted_at IS NULL OR status NOT IN ('FAILED','CANCELLED'))
) STRICT;

INSERT INTO payment_orders_rebuilt(
    payment_order_id,
    business_operation_id,
    payer_customer_account_id,
    source_deposit_account_id,
    destination_deposit_account_id,
    currency_id,
    amount_minor,
    method,
    settlement_mode,
    beneficiary_posting_policy,
    payment_network_policy_version_id,
    memo,
    status,
    beneficiary_posted_at,
    settlement_finalized_at,
    created_at,
    completed_at,
    version)
SELECT
    payment_order_id,
    business_operation_id,
    payer_customer_account_id,
    source_deposit_account_id,
    destination_deposit_account_id,
    currency_id,
    amount_minor,
    method,
    settlement_mode,
    beneficiary_posting_policy,
    payment_network_policy_version_id,
    memo,
    status,
    beneficiary_posted_at,
    settlement_finalized_at,
    created_at,
    completed_at,
    version
FROM payment_orders;

DROP TABLE payment_orders;

ALTER TABLE payment_orders_rebuilt RENAME TO payment_orders;

CREATE INDEX ix_payment_orders_source_created
    ON payment_orders(source_deposit_account_id, created_at);

CREATE INDEX ix_payment_orders_status_created_at
    ON payment_orders(status, created_at);
