CREATE TABLE currency_supply_operations(
    currency_supply_operation_id BLOB NOT NULL PRIMARY KEY CHECK(length(currency_supply_operation_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL UNIQUE REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    operation_kind TEXT NOT NULL CHECK(operation_kind IN ('GENESIS','ISSUE','BURN')),
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    source_ledger_account_id BLOB NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    destination_ledger_account_id BLOB NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    reason_code TEXT NOT NULL CHECK(length(reason_code) BETWEEN 1 AND 32 AND reason_code NOT GLOB '*[^A-Z0-9_]*'),
    occurred_at INTEGER NOT NULL,
    CHECK(source_ledger_account_id IS NULL OR length(source_ledger_account_id) = 16),
    CHECK(destination_ledger_account_id IS NULL OR length(destination_ledger_account_id) = 16),
    CHECK((operation_kind IN ('GENESIS','ISSUE')
            AND source_ledger_account_id IS NULL
            AND destination_ledger_account_id IS NOT NULL)
       OR (operation_kind = 'BURN'
            AND source_ledger_account_id IS NOT NULL
            AND destination_ledger_account_id IS NULL))
) STRICT;

CREATE INDEX ix_currency_supply_operations_currency
    ON currency_supply_operations(currency_id, occurred_at);
