CREATE TABLE currency_denominations(
    currency_denomination_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(currency_denomination_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    value_minor INTEGER NOT NULL CHECK(value_minor > 0),
    kind TEXT NOT NULL CHECK(kind IN ('NOTE','COIN')),
    atm_dispense_enabled INTEGER NOT NULL CHECK(atm_dispense_enabled IN (0,1)),
    atm_deposit_enabled INTEGER NOT NULL CHECK(atm_deposit_enabled IN (0,1)),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(currency_id, value_minor)
) STRICT;

CREATE TABLE cash_holders(
    cash_holder_id BLOB NOT NULL PRIMARY KEY CHECK(length(cash_holder_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    holder_type TEXT NOT NULL
        CHECK(holder_type IN ('BANK_VAULT','ATM_CASSETTE','CUSTOMER_WALLET')),
    owner_reference_id BLOB NOT NULL CHECK(length(owner_reference_id) = 16),
    created_at INTEGER NOT NULL,
    UNIQUE(currency_id, holder_type, owner_reference_id)
) STRICT;

CREATE TABLE cash_wallets(
    cash_wallet_id BLOB NOT NULL PRIMARY KEY CHECK(length(cash_wallet_id) = 16),
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    cash_holder_id BLOB NOT NULL UNIQUE
        REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(customer_account_id, currency_id)
) STRICT;

CREATE TABLE bank_cash_vaults(
    bank_cash_vault_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_cash_vault_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    cash_holder_id BLOB NOT NULL UNIQUE
        REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, currency_id)
) STRICT;

CREATE TABLE cash_positions(
    cash_holder_id BLOB NOT NULL REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    currency_denomination_id BLOB NOT NULL
        REFERENCES currency_denominations(currency_denomination_id) ON DELETE RESTRICT,
    on_hand_count INTEGER NOT NULL CHECK(on_hand_count >= 0),
    reserved_count INTEGER NOT NULL CHECK(reserved_count >= 0),
    version INTEGER NOT NULL CHECK(version >= 1),
    PRIMARY KEY(cash_holder_id, currency_denomination_id),
    CHECK(reserved_count <= on_hand_count)
) STRICT;

CREATE TABLE cash_movements(
    cash_movement_id BLOB NOT NULL PRIMARY KEY CHECK(length(cash_movement_id) = 16),
    business_operation_id BLOB NOT NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    currency_denomination_id BLOB NOT NULL
        REFERENCES currency_denominations(currency_denomination_id) ON DELETE RESTRICT,
    from_cash_holder_id BLOB NULL REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    to_cash_holder_id BLOB NULL REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    quantity INTEGER NOT NULL CHECK(quantity > 0),
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    movement_kind TEXT NOT NULL CHECK(movement_kind IN ('TRANSFER',
        'CENTRAL_BANK_CONVERSION_OUT','CENTRAL_BANK_CONVERSION_IN')),
    created_at INTEGER NOT NULL,
    CHECK(from_cash_holder_id IS NOT NULL OR to_cash_holder_id IS NOT NULL),
    CHECK(from_cash_holder_id IS NULL OR to_cash_holder_id IS NULL
        OR from_cash_holder_id <> to_cash_holder_id)
) STRICT;

CREATE INDEX ix_cash_movements_operation
    ON cash_movements(business_operation_id, cash_movement_id);

CREATE TABLE atm_networks(
    atm_network_id BLOB NOT NULL PRIMARY KEY CHECK(length(atm_network_id) = 16),
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 64),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE atm_network_participations(
    atm_network_id BLOB NOT NULL REFERENCES atm_networks(atm_network_id) ON DELETE RESTRICT,
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    issuer_enabled INTEGER NOT NULL CHECK(issuer_enabled IN (0,1)),
    acquirer_enabled INTEGER NOT NULL CHECK(acquirer_enabled IN (0,1)),
    withdrawal_enabled INTEGER NOT NULL CHECK(withdrawal_enabled IN (0,1)),
    deposit_enabled INTEGER NOT NULL CHECK(deposit_enabled IN (0,1)),
    balance_inquiry_enabled INTEGER NOT NULL CHECK(balance_inquiry_enabled IN (0,1)),
    transfer_enabled INTEGER NOT NULL CHECK(transfer_enabled IN (0,1)),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    PRIMARY KEY(atm_network_id, bank_id, effective_from)
) STRICT;

CREATE INDEX ix_atm_network_participations_bank
    ON atm_network_participations(bank_id, effective_from DESC);

CREATE TABLE atm_terminals(
    atm_terminal_id BLOB NOT NULL PRIMARY KEY CHECK(length(atm_terminal_id) = 16),
    owner_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    placement_guild_id TEXT NOT NULL CHECK(length(placement_guild_id) BETWEEN 1 AND 20),
    branch_id BLOB NULL REFERENCES branches(branch_id) ON DELETE RESTRICT,
    atm_network_id BLOB NULL REFERENCES atm_networks(atm_network_id) ON DELETE RESTRICT,
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    status TEXT NOT NULL
        CHECK(status IN ('OPERATING','CASH_RESTRICTED','OUT_OF_SERVICE','RETIRED')),
    withdrawal_enabled INTEGER NOT NULL CHECK(withdrawal_enabled IN (0,1)),
    deposit_enabled INTEGER NOT NULL CHECK(deposit_enabled IN (0,1)),
    balance_inquiry_enabled INTEGER NOT NULL CHECK(balance_inquiry_enabled IN (0,1)),
    transfer_enabled INTEGER NOT NULL CHECK(transfer_enabled IN (0,1)),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_atm_terminals_placement
    ON atm_terminals(placement_guild_id, status, atm_terminal_id);

CREATE INDEX ix_atm_terminals_owner
    ON atm_terminals(owner_bank_id, status, atm_terminal_id);

CREATE TABLE atm_placement_agreements(
    atm_placement_agreement_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(atm_placement_agreement_id) = 16),
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    placement_guild_id TEXT NOT NULL CHECK(length(placement_guild_id) BETWEEN 1 AND 20),
    operator_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    host_approval_decision_id BLOB NULL CHECK(host_approval_decision_id IS NULL
        OR length(host_approval_decision_id) = 16),
    operator_approval_decision_id BLOB NULL CHECK(operator_approval_decision_id IS NULL
        OR length(operator_approval_decision_id) = 16),
    override_decision_id BLOB NULL CHECK(override_decision_id IS NULL
        OR length(override_decision_id) = 16),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    placement_fee_schedule_version_id BLOB NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    revenue_share_bps INTEGER NOT NULL CHECK(revenue_share_bps BETWEEN 0 AND 10000),
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','SUSPENDED','ENDED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_atm_placement_agreements_guild
    ON atm_placement_agreements(
        placement_guild_id, status, effective_from, atm_placement_agreement_id);

CREATE TABLE atm_terminal_currency_services(
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    withdrawal_enabled INTEGER NOT NULL CHECK(withdrawal_enabled IN (0,1)),
    deposit_enabled INTEGER NOT NULL CHECK(deposit_enabled IN (0,1)),
    cross_currency_withdrawal_enabled INTEGER NOT NULL
        CHECK(cross_currency_withdrawal_enabled IN (0,1)),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    PRIMARY KEY(atm_terminal_id, currency_id)
) STRICT;

CREATE INDEX ix_atm_terminal_currency_services_status
    ON atm_terminal_currency_services(atm_terminal_id, status, currency_id);

CREATE TABLE atm_cash_cassettes(
    atm_cash_cassette_id BLOB NOT NULL PRIMARY KEY CHECK(length(atm_cash_cassette_id) = 16),
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    cash_holder_id BLOB NOT NULL UNIQUE
        REFERENCES cash_holders(cash_holder_id) ON DELETE RESTRICT,
    currency_denomination_id BLOB NOT NULL
        REFERENCES currency_denominations(currency_denomination_id) ON DELETE RESTRICT,
    cassette_role TEXT NOT NULL CHECK(cassette_role IN ('DISPENSE','DEPOSIT','RECYCLE')),
    cassette_priority INTEGER NOT NULL CHECK(cassette_priority BETWEEN 0 AND 7),
    capacity_count INTEGER NOT NULL CHECK(capacity_count > 0),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','DISABLED','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(atm_terminal_id, cassette_priority)
) STRICT;

CREATE TABLE atm_transactions(
    atm_transaction_id BLOB NOT NULL PRIMARY KEY CHECK(length(atm_transaction_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    cash_card_id BLOB NOT NULL REFERENCES cash_cards(cash_card_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    issuer_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    acquirer_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    transaction_type TEXT NOT NULL
        CHECK(transaction_type IN ('WITHDRAWAL','DEPOSIT','BALANCE_INQUIRY','TRANSFER')),
    source_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    source_amount_minor INTEGER NOT NULL CHECK(source_amount_minor >= 0),
    cash_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    cash_amount_minor INTEGER NOT NULL CHECK(cash_amount_minor >= 0),
    issuer_fee_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    issuer_fee_minor INTEGER NOT NULL CHECK(issuer_fee_minor >= 0),
    acquirer_fee_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    acquirer_fee_minor INTEGER NOT NULL CHECK(acquirer_fee_minor >= 0),
    placement_fee_currency_id BLOB NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    placement_fee_minor INTEGER NOT NULL CHECK(placement_fee_minor >= 0),
    status TEXT NOT NULL CHECK(status IN ('PENDING','CUSTOMER_POSTED','INTERBANK_PENDING',
        'SETTLED','DECLINED','CANCELLED')),
    clearing_instruction_id BLOB NULL
        REFERENCES clearing_instructions(clearing_instruction_id) ON DELETE RESTRICT,
    fx_business_operation_id BLOB NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_atm_transactions_account
    ON atm_transactions(deposit_account_id, created_at DESC, atm_transaction_id DESC);

CREATE INDEX ix_atm_transactions_status
    ON atm_transactions(status, created_at, atm_transaction_id);

CREATE TABLE atm_discord_installations(
    atm_discord_installation_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(atm_discord_installation_id) = 16),
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    guild_id TEXT NOT NULL CHECK(length(guild_id) BETWEEN 1 AND 20),
    channel_id TEXT NOT NULL CHECK(length(channel_id) BETWEEN 1 AND 20),
    message_id TEXT NOT NULL CHECK(length(message_id) BETWEEN 1 AND 20),
    installation_nonce BLOB NOT NULL UNIQUE CHECK(length(installation_nonce) = 16),
    presentation_profile_version_id BLOB NULL
        REFERENCES presentation_profile_versions(presentation_profile_version_id)
        ON DELETE RESTRICT,
    banner_asset_id BLOB NULL CHECK(banner_asset_id IS NULL OR length(banner_asset_id) = 16),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','BROKEN','REMOVED')),
    installed_by_discord_user_id TEXT NOT NULL
        CHECK(length(installed_by_discord_user_id) BETWEEN 1 AND 20),
    installed_at INTEGER NOT NULL,
    last_synced_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(channel_id, message_id)
) STRICT;

CREATE INDEX ix_atm_discord_installations_terminal
    ON atm_discord_installations(atm_terminal_id, status, atm_discord_installation_id);

PRAGMA defer_foreign_keys = ON;

CREATE TABLE fee_rules_rebuilt(
    fee_rule_id BLOB NOT NULL PRIMARY KEY CHECK(length(fee_rule_id) = 16),
    fee_schedule_version_id BLOB NOT NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    fee_type TEXT NOT NULL
        CHECK(length(fee_type) BETWEEN 1 AND 48 AND fee_type NOT GLOB '*[^A-Z_]*'),
    priority INTEGER NOT NULL CHECK(priority BETWEEN 0 AND 65535),
    channel TEXT NOT NULL
        CHECK(channel IN ('ANY','DISCORD','ATM','SCHEDULED','DIRECT_DEBIT','MERCHANT','FX','SYSTEM')),
    account_product_id BLOB NULL REFERENCES account_products(product_id) ON DELETE RESTRICT,
    atm_network_id BLOB NULL REFERENCES atm_networks(atm_network_id) ON DELETE RESTRICT,
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
    waiver_counter_key TEXT NULL CHECK(waiver_counter_key IS NULL
        OR (length(waiver_counter_key) BETWEEN 1 AND 64
            AND waiver_counter_key NOT GLOB '*[^a-z0-9-]*')),
    free_occurrences_per_business_month INTEGER NOT NULL
        CHECK(free_occurrences_per_business_month BETWEEN 0 AND 1000),
    CHECK(amount_max_minor IS NULL OR amount_max_minor > amount_min_minor),
    CHECK(maximum_minor IS NULL OR maximum_minor >= minimum_minor),
    CHECK((local_start_minute IS NULL AND local_end_minute IS NULL)
       OR (local_start_minute BETWEEN 0 AND 1439 AND local_end_minute BETWEEN 1 AND 1440
           AND local_start_minute < local_end_minute)),
    CHECK(free_occurrences_per_business_month = 0 OR waiver_counter_key IS NOT NULL),
    UNIQUE(fee_schedule_version_id, fee_type, priority)
) STRICT;

INSERT INTO fee_rules_rebuilt(
    fee_rule_id,
    fee_schedule_version_id,
    fee_type,
    priority,
    channel,
    account_product_id,
    atm_network_id,
    counterparty_bank_id,
    amount_min_minor,
    amount_max_minor,
    day_class,
    local_start_minute,
    local_end_minute,
    fixed_minor,
    basis_points,
    minimum_minor,
    maximum_minor,
    waiver_counter_key,
    free_occurrences_per_business_month)
SELECT
    fee_rule_id,
    fee_schedule_version_id,
    fee_type,
    priority,
    channel,
    account_product_id,
    atm_network_id,
    counterparty_bank_id,
    amount_min_minor,
    amount_max_minor,
    day_class,
    local_start_minute,
    local_end_minute,
    fixed_minor,
    basis_points,
    minimum_minor,
    maximum_minor,
    waiver_counter_key,
    free_occurrences_per_business_month
FROM fee_rules;

DROP TABLE fee_rules;

ALTER TABLE fee_rules_rebuilt RENAME TO fee_rules;
