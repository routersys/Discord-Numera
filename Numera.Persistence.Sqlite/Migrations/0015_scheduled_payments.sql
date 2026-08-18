CREATE TABLE saved_beneficiaries(
    saved_beneficiary_id BLOB NOT NULL PRIMARY KEY CHECK(length(saved_beneficiary_id) = 16),
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    destination_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    institution_code_snapshot TEXT NOT NULL CHECK(length(institution_code_snapshot) BETWEEN 1 AND 16),
    branch_code_snapshot TEXT NOT NULL CHECK(length(branch_code_snapshot) BETWEEN 1 AND 8),
    account_number_snapshot TEXT NOT NULL CHECK(length(account_number_snapshot) BETWEEN 6 AND 16),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','HIDDEN','INVALID')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE UNIQUE INDEX ux_saved_beneficiaries_active
    ON saved_beneficiaries(customer_account_id, destination_deposit_account_id)
    WHERE status = 'ACTIVE';

CREATE INDEX ix_saved_beneficiaries_customer
    ON saved_beneficiaries(customer_account_id, status, created_at);

CREATE TABLE scheduled_payment_plans(
    scheduled_payment_plan_id BLOB NOT NULL PRIMARY KEY CHECK(length(scheduled_payment_plan_id) = 16),
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    destination_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    saved_beneficiary_id BLOB NULL REFERENCES saved_beneficiaries(saved_beneficiary_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    kind TEXT NOT NULL CHECK(kind IN ('ONCE','WEEKLY','MONTHLY')),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','PAUSED','COMPLETED','CANCELLED')),
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    anchor_day_of_month INTEGER NULL CHECK(anchor_day_of_month IS NULL
        OR anchor_day_of_month BETWEEN 1 AND 31),
    canonical_timezone TEXT NOT NULL CHECK(length(canonical_timezone) BETWEEN 1 AND 64),
    next_due_at INTEGER NULL,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(kind <> 'MONTHLY' OR anchor_day_of_month IS NOT NULL),
    CHECK(kind = 'MONTHLY' OR anchor_day_of_month IS NULL),
    CHECK(source_deposit_account_id <> destination_deposit_account_id),
    CHECK(status NOT IN ('COMPLETED','CANCELLED') OR next_due_at IS NULL)
) STRICT;

CREATE INDEX ix_scheduled_payment_plans_due
    ON scheduled_payment_plans(status, next_due_at);

CREATE INDEX ix_scheduled_payment_plans_customer
    ON scheduled_payment_plans(customer_account_id, status, created_at);

CREATE TABLE scheduled_payment_occurrences(
    scheduled_payment_occurrence_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(scheduled_payment_occurrence_id) = 16),
    scheduled_payment_plan_id BLOB NOT NULL
        REFERENCES scheduled_payment_plans(scheduled_payment_plan_id) ON DELETE RESTRICT,
    payment_order_id BLOB NULL REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    scheduled_for INTEGER NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('PENDING','EXECUTING','SUCCEEDED','FAILED_FUNDS',
        'FAILED_RESTRICTED','FAILED_DESTINATION','CANCELLED')),
    attempted_at INTEGER NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(scheduled_payment_plan_id, scheduled_for),
    CHECK(status <> 'SUCCEEDED' OR payment_order_id IS NOT NULL)
) STRICT;

CREATE INDEX ix_scheduled_payment_occurrences_due
    ON scheduled_payment_occurrences(status, scheduled_for);

CREATE TABLE direct_debit_mandates(
    direct_debit_mandate_id BLOB NOT NULL PRIMARY KEY CHECK(length(direct_debit_mandate_id) = 16),
    creditor_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    creditor_settlement_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    debtor_customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    debtor_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','SUSPENDED','REVOKED','EXPIRED')),
    single_collection_limit_minor INTEGER NOT NULL CHECK(single_collection_limit_minor > 0),
    valid_from INTEGER NOT NULL,
    valid_until INTEGER NULL,
    activated_at INTEGER NULL,
    terminated_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(valid_until IS NULL OR valid_until > valid_from),
    CHECK(creditor_settlement_account_id <> debtor_deposit_account_id),
    CHECK(status <> 'PENDING' OR activated_at IS NULL),
    CHECK(status NOT IN ('REVOKED','EXPIRED') OR terminated_at IS NOT NULL)
) STRICT;

CREATE INDEX ix_direct_debit_mandates_debtor
    ON direct_debit_mandates(debtor_customer_account_id, status, valid_from);

CREATE INDEX ix_direct_debit_mandates_creditor
    ON direct_debit_mandates(creditor_party_id, status);

CREATE TABLE direct_debit_collections(
    direct_debit_collection_id BLOB NOT NULL PRIMARY KEY CHECK(length(direct_debit_collection_id) = 16),
    direct_debit_mandate_id BLOB NOT NULL
        REFERENCES direct_debit_mandates(direct_debit_mandate_id) ON DELETE RESTRICT,
    payment_order_id BLOB NULL REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    creditor_collection_reference TEXT NOT NULL
        CHECK(length(creditor_collection_reference) BETWEEN 1 AND 64),
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    status TEXT NOT NULL CHECK(status IN ('PENDING','EXECUTING','SETTLED','FAILED_FUNDS',
        'FAILED_MANDATE','FAILED_ACCOUNT','CANCELLED')),
    scheduled_for INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(direct_debit_mandate_id, creditor_collection_reference),
    CHECK(status <> 'SETTLED' OR payment_order_id IS NOT NULL)
) STRICT;

CREATE INDEX ix_direct_debit_collections_due
    ON direct_debit_collections(status, scheduled_for);
