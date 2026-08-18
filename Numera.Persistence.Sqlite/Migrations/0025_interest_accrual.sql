CREATE TABLE interest_accruals(
    interest_accrual_id BLOB NOT NULL PRIMARY KEY CHECK(length(interest_accrual_id) = 16),
    deposit_account_id BLOB NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    loan_contract_id BLOB NULL
        REFERENCES loan_contracts(loan_contract_id) ON DELETE RESTRICT,
    product_version_id BLOB NULL
        REFERENCES account_product_versions(product_version_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    accrual_date TEXT NOT NULL CHECK(length(accrual_date) = 10),
    principal_minor INTEGER NOT NULL CHECK(principal_minor >= 0),
    annual_rate_ppt INTEGER NOT NULL,
    accrual_minor INTEGER NOT NULL,
    residual_numerator TEXT NOT NULL CHECK(length(residual_numerator) BETWEEN 1 AND 48),
    posted INTEGER NOT NULL CHECK(posted IN (0,1)),
    created_at INTEGER NOT NULL,
    CHECK((deposit_account_id IS NOT NULL) <> (loan_contract_id IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_interest_accruals_deposit_date
    ON interest_accruals(deposit_account_id, accrual_date)
    WHERE deposit_account_id IS NOT NULL;

CREATE UNIQUE INDEX ux_interest_accruals_loan_date
    ON interest_accruals(loan_contract_id, accrual_date)
    WHERE loan_contract_id IS NOT NULL;

CREATE TABLE interest_posting_batches(
    interest_posting_batch_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(interest_posting_batch_id) = 16),
    economy_scope_id BLOB NOT NULL
        REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    business_date TEXT NOT NULL CHECK(length(business_date) = 10),
    idempotency_key TEXT NOT NULL UNIQUE CHECK(length(idempotency_key) BETWEEN 1 AND 128),
    status TEXT NOT NULL CHECK(status IN ('PENDING','POSTED','FAILED')),
    created_at INTEGER NOT NULL,
    posted_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'POSTED' AND posted_at IS NOT NULL)
        OR (status <> 'POSTED' AND posted_at IS NULL))
) STRICT;
