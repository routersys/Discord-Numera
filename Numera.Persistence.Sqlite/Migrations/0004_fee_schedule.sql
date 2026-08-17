CREATE TABLE economy_calendar_overrides(
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    local_date TEXT NOT NULL CHECK(length(local_date) = 10),
    day_class TEXT NOT NULL CHECK(day_class IN ('BUSINESS_DAY','NON_BUSINESS_DAY')),
    description TEXT NULL CHECK(description IS NULL OR length(description) BETWEEN 1 AND 200),
    version INTEGER NOT NULL CHECK(version >= 1),
    PRIMARY KEY(economy_scope_id, local_date)
) STRICT;

CREATE TABLE fee_schedule_versions(
    fee_schedule_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(fee_schedule_version_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(effective_to IS NULL OR effective_to > effective_from)
) STRICT;

CREATE INDEX ix_fee_schedule_versions_bank ON fee_schedule_versions(bank_id, effective_from);

CREATE TABLE fee_rules(
    fee_rule_id BLOB NOT NULL PRIMARY KEY CHECK(length(fee_rule_id) = 16),
    fee_schedule_version_id BLOB NOT NULL REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    fee_type TEXT NOT NULL CHECK(length(fee_type) BETWEEN 1 AND 48 AND fee_type NOT GLOB '*[^A-Z_]*'),
    priority INTEGER NOT NULL CHECK(priority BETWEEN 0 AND 65535),
    channel TEXT NOT NULL CHECK(channel IN ('ANY','DISCORD','ATM','SCHEDULED','DIRECT_DEBIT','MERCHANT','FX','SYSTEM')),
    account_product_id BLOB NULL REFERENCES account_products(product_id) ON DELETE RESTRICT,
    atm_network_id BLOB NULL CHECK(atm_network_id IS NULL OR length(atm_network_id) = 16),
    counterparty_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    amount_min_minor INTEGER NOT NULL CHECK(amount_min_minor >= 0),
    amount_max_minor INTEGER NULL,
    day_class TEXT NOT NULL CHECK(day_class IN ('ANY','BUSINESS_DAY','NON_BUSINESS_DAY')),
    local_start_minute INTEGER NULL,
    local_end_minute INTEGER NULL,
    fixed_minor INTEGER NOT NULL CHECK(fixed_minor >= 0),
    basis_points INTEGER NOT NULL CHECK(basis_points BETWEEN 0 AND 100000),
    minimum_minor INTEGER NOT NULL CHECK(minimum_minor >= 0),
    maximum_minor INTEGER NULL,
    waiver_counter_key TEXT NULL CHECK(waiver_counter_key IS NULL OR (length(waiver_counter_key) BETWEEN 1 AND 64 AND waiver_counter_key NOT GLOB '*[^a-z0-9-]*')),
    free_occurrences_per_business_month INTEGER NOT NULL CHECK(free_occurrences_per_business_month BETWEEN 0 AND 1000),
    CHECK(amount_max_minor IS NULL OR amount_max_minor > amount_min_minor),
    CHECK(maximum_minor IS NULL OR maximum_minor >= minimum_minor),
    CHECK((local_start_minute IS NULL AND local_end_minute IS NULL)
       OR (local_start_minute BETWEEN 0 AND 1439 AND local_end_minute BETWEEN 1 AND 1440 AND local_start_minute < local_end_minute)),
    CHECK(free_occurrences_per_business_month = 0 OR waiver_counter_key IS NOT NULL),
    UNIQUE(fee_schedule_version_id, fee_type, priority)
) STRICT;

CREATE TABLE fee_waiver_usage_counters(
    deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    waiver_counter_key TEXT NOT NULL CHECK(length(waiver_counter_key) BETWEEN 1 AND 64),
    business_month INTEGER NOT NULL CHECK(business_month BETWEEN 190001 AND 999912 AND (business_month % 100) BETWEEN 1 AND 12),
    used_count INTEGER NOT NULL CHECK(used_count >= 0),
    version INTEGER NOT NULL CHECK(version >= 1),
    PRIMARY KEY(deposit_account_id, waiver_counter_key, business_month)
) STRICT;

CREATE TABLE fee_assessments(
    fee_assessment_id BLOB NOT NULL PRIMARY KEY CHECK(length(fee_assessment_id) = 16),
    business_operation_id BLOB NOT NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    fee_schedule_version_id BLOB NULL REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    fee_rule_id BLOB NULL REFERENCES fee_rules(fee_rule_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    payer_ledger_account_id BLOB NOT NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    recipient_ledger_account_id BLOB NOT NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    fee_type TEXT NOT NULL CHECK(length(fee_type) BETWEEN 1 AND 48 AND fee_type NOT GLOB '*[^A-Z_]*'),
    amount_minor INTEGER NOT NULL CHECK(amount_minor >= 0),
    assessed_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_fee_assessments_operation ON fee_assessments(business_operation_id);
