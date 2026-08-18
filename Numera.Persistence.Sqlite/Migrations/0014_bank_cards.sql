CREATE TABLE bank_cards(
    bank_card_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_card_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    card_form TEXT NOT NULL CHECK(card_form IN ('CASH_ONLY','DEBIT_ONLY','INTEGRATED_CASH_DEBIT')),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','LOCKED','REPLACED','EXPIRED','CLOSED')),
    display_identifier TEXT NOT NULL CHECK(length(display_identifier) BETWEEN 4 AND 32),
    issued_at INTEGER NOT NULL,
    expires_at INTEGER NULL,
    replaced_by_bank_card_id BLOB NULL REFERENCES bank_cards(bank_card_id) ON DELETE RESTRICT,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(expires_at IS NULL OR expires_at > issued_at),
    CHECK(card_form = 'CASH_ONLY' OR expires_at IS NOT NULL),
    CHECK((status = 'REPLACED' AND replaced_by_bank_card_id IS NOT NULL)
        OR (status <> 'REPLACED' AND replaced_by_bank_card_id IS NULL)),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE INDEX ix_bank_cards_account ON bank_cards(deposit_account_id, status);

CREATE UNIQUE INDEX ux_bank_cards_display_identifier ON bank_cards(bank_id, display_identifier);

CREATE TABLE cash_cards(
    cash_card_id BLOB NOT NULL PRIMARY KEY CHECK(length(cash_card_id) = 16),
    bank_card_id BLOB NOT NULL UNIQUE REFERENCES bank_cards(bank_card_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','LOCKED','CLOSED')),
    issued_at INTEGER NOT NULL,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE UNIQUE INDEX ux_cash_cards_active_account
    ON cash_cards(deposit_account_id) WHERE status IN ('ACTIVE','LOCKED');

CREATE TABLE debit_cards(
    debit_card_id BLOB NOT NULL PRIMARY KEY CHECK(length(debit_card_id) = 16),
    bank_card_id BLOB NOT NULL UNIQUE REFERENCES bank_cards(bank_card_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','LOCKED','CLOSED')),
    display_number TEXT NOT NULL CHECK(length(display_number) BETWEEN 4 AND 24),
    expires_at INTEGER NOT NULL,
    issued_at INTEGER NOT NULL,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(expires_at > issued_at),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE UNIQUE INDEX ux_debit_cards_active_account
    ON debit_cards(deposit_account_id) WHERE status IN ('ACTIVE','LOCKED');
