CREATE TABLE deposit_insurance_funds(
    deposit_insurance_fund_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_fund_id) = 16),
    economy_scope_id BLOB NOT NULL
        REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    owner_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    accounting_book_id BLOB NOT NULL
        REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    central_bank_settlement_liability_ledger_account_id BLOB NOT NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    liquid_asset_ledger_account_id BLOB NOT NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    premium_revenue_ledger_account_id BLOB NOT NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    claim_expense_ledger_account_id BLOB NOT NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','RETIRED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, currency_id)
) STRICT;

CREATE TABLE deposit_insurance_schemes(
    deposit_insurance_scheme_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_scheme_id) = 16),
    economy_scope_id BLOB NOT NULL
        REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    protection_class_code TEXT NOT NULL
        CHECK(length(protection_class_code) BETWEEN 1 AND 32
            AND protection_class_code NOT GLOB '*[^A-Z0-9_]*'),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    current_version_id BLOB NULL
        REFERENCES deposit_insurance_scheme_versions(deposit_insurance_scheme_version_id)
        ON DELETE RESTRICT,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, currency_id, protection_class_code)
) STRICT;

CREATE TABLE deposit_insurance_scheme_versions(
    deposit_insurance_scheme_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_scheme_version_id) = 16),
    deposit_insurance_scheme_id BLOB NOT NULL
        REFERENCES deposit_insurance_schemes(deposit_insurance_scheme_id) ON DELETE RESTRICT,
    deposit_insurance_fund_id BLOB NOT NULL
        REFERENCES deposit_insurance_funds(deposit_insurance_fund_id) ON DELETE RESTRICT,
    coverage_limit_minor INTEGER NOT NULL CHECK(coverage_limit_minor > 0),
    enrollment_fee_minor INTEGER NOT NULL CHECK(enrollment_fee_minor >= 0),
    effective_from INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(deposit_insurance_scheme_id, version)
) STRICT;

CREATE TABLE deposit_insurance_premium_payments(
    deposit_insurance_premium_payment_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_premium_payment_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    deposit_insurance_fund_id BLOB NOT NULL
        REFERENCES deposit_insurance_funds(deposit_insurance_fund_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    source_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    posted_at INTEGER NOT NULL
) STRICT;

CREATE TABLE deposit_insurance_enrollments(
    deposit_insurance_enrollment_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_enrollment_id) = 16),
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    protection_class_code TEXT NOT NULL
        CHECK(length(protection_class_code) BETWEEN 1 AND 32
            AND protection_class_code NOT GLOB '*[^A-Z0-9_]*'),
    deposit_insurance_scheme_version_id BLOB NOT NULL
        REFERENCES deposit_insurance_scheme_versions(deposit_insurance_scheme_version_id)
        ON DELETE RESTRICT,
    coverage_limit_minor_snapshot INTEGER NOT NULL CHECK(coverage_limit_minor_snapshot > 0),
    enrollment_fee_minor_snapshot INTEGER NOT NULL CHECK(enrollment_fee_minor_snapshot >= 0),
    deposit_insurance_premium_payment_id BLOB NULL
        REFERENCES deposit_insurance_premium_payments(deposit_insurance_premium_payment_id)
        ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','CANCELLED','CLAIMED')),
    enrolled_at INTEGER NOT NULL,
    terminal_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((enrollment_fee_minor_snapshot = 0 AND deposit_insurance_premium_payment_id IS NULL)
       OR (enrollment_fee_minor_snapshot > 0 AND deposit_insurance_premium_payment_id IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_deposit_insurance_enrollments_live
    ON deposit_insurance_enrollments(deposit_account_id) WHERE status = 'ACTIVE';

CREATE INDEX ix_deposit_insurance_enrollments_customer
    ON deposit_insurance_enrollments(
        customer_account_id, status, deposit_insurance_enrollment_id);

CREATE TABLE deposit_insurance_reservations(
    deposit_insurance_reservation_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_reservation_id) = 16),
    deposit_insurance_enrollment_id BLOB NOT NULL UNIQUE
        REFERENCES deposit_insurance_enrollments(deposit_insurance_enrollment_id)
        ON DELETE RESTRICT,
    deposit_insurance_fund_id BLOB NOT NULL
        REFERENCES deposit_insurance_funds(deposit_insurance_fund_id) ON DELETE RESTRICT,
    reserved_minor INTEGER NOT NULL CHECK(reserved_minor > 0),
    consumed_minor INTEGER NOT NULL CHECK(consumed_minor >= 0),
    released_minor INTEGER NOT NULL CHECK(released_minor >= 0),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SETTLED')),
    created_at INTEGER NOT NULL,
    terminal_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(consumed_minor + released_minor <= reserved_minor),
    CHECK((status = 'ACTIVE' AND consumed_minor + released_minor < reserved_minor
            AND terminal_at IS NULL)
       OR (status = 'SETTLED' AND consumed_minor + released_minor = reserved_minor
            AND terminal_at IS NOT NULL))
) STRICT;

CREATE TABLE insurance_settlement_wallets(
    insurance_settlement_wallet_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(insurance_settlement_wallet_id) = 16),
    deposit_insurance_fund_id BLOB NOT NULL
        REFERENCES deposit_insurance_funds(deposit_insurance_fund_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    liability_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(customer_account_id, currency_id)
) STRICT;

CREATE TABLE insurance_settlement_wallet_payouts(
    insurance_settlement_wallet_payout_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(insurance_settlement_wallet_payout_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    insurance_settlement_wallet_id BLOB NOT NULL
        REFERENCES insurance_settlement_wallets(insurance_settlement_wallet_id) ON DELETE RESTRICT,
    deposit_insurance_fund_id BLOB NOT NULL
        REFERENCES deposit_insurance_funds(deposit_insurance_fund_id) ON DELETE RESTRICT,
    destination_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    destination_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    completed_at INTEGER NOT NULL
) STRICT;

CREATE TABLE deposit_insurance_claims(
    deposit_insurance_claim_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(deposit_insurance_claim_id) = 16),
    resolution_case_id BLOB NOT NULL
        REFERENCES resolution_cases(resolution_case_id) ON DELETE RESTRICT,
    deposit_insurance_scheme_version_id BLOB NOT NULL
        REFERENCES deposit_insurance_scheme_versions(deposit_insurance_scheme_version_id)
        ON DELETE RESTRICT,
    deposit_insurance_enrollment_id BLOB NOT NULL
        REFERENCES deposit_insurance_enrollments(deposit_insurance_enrollment_id)
        ON DELETE RESTRICT,
    party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    protection_class_code TEXT NOT NULL
        CHECK(length(protection_class_code) BETWEEN 1 AND 32
            AND protection_class_code NOT GLOB '*[^A-Z0-9_]*'),
    insurance_settlement_wallet_id BLOB NOT NULL
        REFERENCES insurance_settlement_wallets(insurance_settlement_wallet_id) ON DELETE RESTRICT,
    eligible_minor INTEGER NOT NULL CHECK(eligible_minor >= 0),
    insured_minor INTEGER NOT NULL CHECK(insured_minor >= 0),
    paid_minor INTEGER NOT NULL CHECK(paid_minor >= 0),
    status TEXT NOT NULL CHECK(status IN ('CALCULATED','APPROVED','PAID','REJECTED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(insured_minor <= eligible_minor),
    CHECK(paid_minor <= insured_minor),
    CHECK((status = 'PAID' AND paid_minor = insured_minor)
       OR (status <> 'PAID' AND paid_minor = 0)),
    UNIQUE(resolution_case_id, party_id, bank_id, protection_class_code)
) STRICT;

CREATE INDEX ix_deposit_insurance_claims_customer
    ON deposit_insurance_claims(customer_account_id, status, deposit_insurance_claim_id);
