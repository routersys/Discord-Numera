CREATE TABLE clearing_cycles(
    clearing_cycle_id BLOB NOT NULL PRIMARY KEY CHECK(length(clearing_cycle_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    cycle_key TEXT NOT NULL CHECK(length(cycle_key) BETWEEN 1 AND 32),
    status TEXT NOT NULL CHECK(status IN ('OPEN','LOCKED','SETTLING','CLOSED')),
    opened_at INTEGER NOT NULL,
    locked_at INTEGER NULL,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, currency_id, cycle_key),
    CHECK(status NOT IN ('LOCKED','SETTLING','CLOSED') OR locked_at IS NOT NULL),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE INDEX ix_clearing_cycles_open
    ON clearing_cycles(economy_scope_id, currency_id, status);

CREATE TABLE clearing_instructions(
    clearing_instruction_id BLOB NOT NULL PRIMARY KEY CHECK(length(clearing_instruction_id) = 16),
    business_operation_id BLOB NOT NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    payment_order_id BLOB NULL REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    clearing_cycle_id BLOB NULL REFERENCES clearing_cycles(clearing_cycle_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    source_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    destination_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    instruction_kind TEXT NOT NULL CHECK(length(instruction_kind) BETWEEN 1 AND 48 AND instruction_kind NOT GLOB '*[^A-Z_]*'),
    status TEXT NOT NULL CHECK(status IN ('CREATED','ACCEPTED','LOCKED','SETTLED','CANCELLED','FAILED')),
    created_at INTEGER NOT NULL,
    settled_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(source_bank_id <> destination_bank_id),
    CHECK((status = 'SETTLED' AND settled_at IS NOT NULL) OR (status <> 'SETTLED' AND settled_at IS NULL)),
    CHECK(status NOT IN ('LOCKED','SETTLED') OR clearing_cycle_id IS NOT NULL)
) STRICT;

CREATE INDEX ix_clearing_instructions_cycle ON clearing_instructions(clearing_cycle_id, status);

CREATE INDEX ix_clearing_instructions_operation ON clearing_instructions(business_operation_id);

CREATE TABLE clearing_positions(
    clearing_position_id BLOB NOT NULL PRIMARY KEY CHECK(length(clearing_position_id) = 16),
    clearing_cycle_id BLOB NOT NULL REFERENCES clearing_cycles(clearing_cycle_id) ON DELETE RESTRICT,
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    gross_receivable_minor INTEGER NOT NULL CHECK(gross_receivable_minor >= 0),
    gross_payable_minor INTEGER NOT NULL CHECK(gross_payable_minor >= 0),
    net_minor INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(clearing_cycle_id, bank_id),
    CHECK(net_minor = gross_receivable_minor - gross_payable_minor)
) STRICT;
