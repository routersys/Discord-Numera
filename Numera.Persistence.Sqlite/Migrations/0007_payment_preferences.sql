CREATE TABLE payment_preferences(
    payment_preference_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_preference_id) = 16),
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    preference_kind TEXT NOT NULL CHECK(preference_kind IN ('DEFAULT_PAYMENT','DEFAULT_RECEIPT','SALARY_RECEIPT','REWARD_RECEIPT','TAX_PAYMENT')),
    deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    disabled_at INTEGER NULL,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(customer_account_id, preference_kind)
) STRICT;

CREATE INDEX ix_payment_preferences_deposit_account
    ON payment_preferences(deposit_account_id) WHERE disabled_at IS NULL;
