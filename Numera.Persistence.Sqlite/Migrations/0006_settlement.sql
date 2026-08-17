CREATE TABLE central_bank_settlement_accounts(
    central_bank_settlement_account_id BLOB NOT NULL PRIMARY KEY CHECK(length(central_bank_settlement_account_id) = 16),
    bank_id BLOB NOT NULL UNIQUE REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    central_bank_ledger_account_id BLOB NOT NULL UNIQUE REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','CLOSED')),
    opened_at INTEGER NOT NULL,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE TABLE settlement_participations(
    settlement_participation_id BLOB NOT NULL PRIMARY KEY CHECK(length(settlement_participation_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    mode TEXT NOT NULL CHECK(mode IN ('DIRECT','INDIRECT')),
    settlement_agent_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    central_bank_settlement_account_id BLOB NULL
        REFERENCES central_bank_settlement_accounts(central_bank_settlement_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','SUSPENDED','ENDED')),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((mode = 'DIRECT' AND settlement_agent_bank_id IS NULL AND central_bank_settlement_account_id IS NOT NULL)
       OR (mode = 'INDIRECT' AND settlement_agent_bank_id IS NOT NULL AND central_bank_settlement_account_id IS NULL)),
    CHECK(settlement_agent_bank_id IS NULL OR settlement_agent_bank_id <> bank_id),
    CHECK(effective_to IS NULL OR effective_to > effective_from)
) STRICT;

CREATE UNIQUE INDEX ux_settlement_participations_live
    ON settlement_participations(bank_id) WHERE status <> 'ENDED';

CREATE TABLE settlement_instructions(
    settlement_instruction_id BLOB NOT NULL PRIMARY KEY CHECK(length(settlement_instruction_id) = 16),
    business_operation_id BLOB NOT NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    source_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    destination_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    status TEXT NOT NULL CHECK(status IN ('CREATED','QUEUED','LOCKED_FOR_SETTLEMENT','SETTLED','CANCELLED','FAILED')),
    created_at INTEGER NOT NULL,
    locked_at INTEGER NULL,
    settled_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(source_bank_id <> destination_bank_id),
    CHECK((status = 'SETTLED' AND settled_at IS NOT NULL) OR (status <> 'SETTLED' AND settled_at IS NULL)),
    CHECK(status NOT IN ('LOCKED_FOR_SETTLEMENT','SETTLED') OR locked_at IS NOT NULL)
) STRICT;

CREATE INDEX ix_settlement_instructions_operation ON settlement_instructions(business_operation_id);

CREATE INDEX ix_settlement_instructions_due
    ON settlement_instructions(status, created_at, settlement_instruction_id);
