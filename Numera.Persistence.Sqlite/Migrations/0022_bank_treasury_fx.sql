CREATE TABLE bank_treasury_fx_accounts(
    bank_treasury_fx_account_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(bank_treasury_fx_account_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    asset_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, currency_id)
) STRICT;
