CREATE TABLE bank_policy_versions(
    bank_policy_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_policy_version_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    opening_enabled INTEGER NOT NULL CHECK(opening_enabled IN (0, 1)),
    minimum_customer_account_age_days INTEGER NOT NULL CHECK(minimum_customer_account_age_days >= 0),
    minimum_initial_funding_minor INTEGER NOT NULL CHECK(minimum_initial_funding_minor >= 0),
    requires_manual_approval INTEGER NOT NULL CHECK(requires_manual_approval IN (0, 1)),
    reopen_closed_account_allowed INTEGER NOT NULL CHECK(reopen_closed_account_allowed IN (0, 1)),
    public_receiving_enabled_default INTEGER NOT NULL CHECK(public_receiving_enabled_default IN (0, 1)),
    cash_card_enabled INTEGER NOT NULL CHECK(cash_card_enabled IN (0, 1)),
    debit_card_enabled INTEGER NOT NULL CHECK(debit_card_enabled IN (0, 1)),
    integrated_cash_debit_default INTEGER NOT NULL CHECK(integrated_cash_debit_default IN (0, 1)),
    automatic_bank_card_issue_mode TEXT NOT NULL CHECK(automatic_bank_card_issue_mode IN ('NONE','CASH_ONLY','INTEGRATED_CASH_DEBIT')),
    cash_atm_enabled INTEGER NOT NULL CHECK(cash_atm_enabled IN (0, 1)),
    cash_card_validity_months INTEGER NULL CHECK(cash_card_validity_months IS NULL OR cash_card_validity_months BETWEEN 1 AND 120),
    debit_card_validity_months INTEGER NOT NULL CHECK(debit_card_validity_months BETWEEN 1 AND 120),
    per_transfer_limit_minor INTEGER NULL CHECK(per_transfer_limit_minor IS NULL OR per_transfer_limit_minor >= 0),
    daily_outgoing_limit_minor INTEGER NULL CHECK(daily_outgoing_limit_minor IS NULL OR daily_outgoing_limit_minor >= 0),
    per_atm_withdrawal_limit_minor INTEGER NULL CHECK(per_atm_withdrawal_limit_minor IS NULL OR per_atm_withdrawal_limit_minor >= 0),
    daily_atm_withdrawal_limit_minor INTEGER NULL CHECK(daily_atm_withdrawal_limit_minor IS NULL OR daily_atm_withdrawal_limit_minor >= 0),
    daily_atm_transfer_limit_minor INTEGER NULL CHECK(daily_atm_transfer_limit_minor IS NULL OR daily_atm_transfer_limit_minor >= 0),
    daily_debit_purchase_limit_minor INTEGER NULL CHECK(daily_debit_purchase_limit_minor IS NULL OR daily_debit_purchase_limit_minor >= 0),
    daily_fx_order_notional_limit_minor INTEGER NULL CHECK(daily_fx_order_notional_limit_minor IS NULL OR daily_fx_order_notional_limit_minor >= 0),
    maximum_active_holds_minor INTEGER NULL CHECK(maximum_active_holds_minor IS NULL OR maximum_active_holds_minor >= 0),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(effective_to IS NULL OR effective_to > effective_from),
    CHECK(automatic_bank_card_issue_mode <> 'CASH_ONLY' OR cash_card_enabled = 1),
    CHECK(automatic_bank_card_issue_mode <> 'INTEGRATED_CASH_DEBIT'
       OR (cash_card_enabled = 1 AND debit_card_enabled = 1))
) STRICT;

CREATE INDEX ix_bank_policy_versions_bank ON bank_policy_versions(bank_id, effective_from);

CREATE TABLE account_limit_preferences(
    deposit_account_id BLOB NOT NULL PRIMARY KEY REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    per_transfer_limit_minor INTEGER NULL CHECK(per_transfer_limit_minor IS NULL OR per_transfer_limit_minor >= 0),
    daily_outgoing_limit_minor INTEGER NULL CHECK(daily_outgoing_limit_minor IS NULL OR daily_outgoing_limit_minor >= 0),
    per_atm_withdrawal_limit_minor INTEGER NULL CHECK(per_atm_withdrawal_limit_minor IS NULL OR per_atm_withdrawal_limit_minor >= 0),
    daily_atm_withdrawal_limit_minor INTEGER NULL CHECK(daily_atm_withdrawal_limit_minor IS NULL OR daily_atm_withdrawal_limit_minor >= 0),
    daily_atm_transfer_limit_minor INTEGER NULL CHECK(daily_atm_transfer_limit_minor IS NULL OR daily_atm_transfer_limit_minor >= 0),
    daily_debit_purchase_limit_minor INTEGER NULL CHECK(daily_debit_purchase_limit_minor IS NULL OR daily_debit_purchase_limit_minor >= 0),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_payment_orders_source_created
    ON payment_orders(source_deposit_account_id, created_at);
