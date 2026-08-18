CREATE TABLE fx_markets(
    market_id BLOB NOT NULL PRIMARY KEY CHECK(length(market_id) = 16),
    base_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    quote_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    operator_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    current_policy_version_id BLOB NULL,
    price_scale INTEGER NOT NULL CHECK(price_scale > 0),
    tick_size_price_units INTEGER NOT NULL CHECK(tick_size_price_units > 0),
    lot_size_base_minor INTEGER NOT NULL CHECK(lot_size_base_minor > 0),
    next_order_sequence_no INTEGER NOT NULL CHECK(next_order_sequence_no > 0),
    next_trade_sequence_no INTEGER NOT NULL CHECK(next_trade_sequence_no > 0),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PENDING_APPROVAL','ACTIVE','SUSPENDED','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(base_currency_id < quote_currency_id),
    UNIQUE(base_currency_id, quote_currency_id)
) STRICT;

CREATE TABLE fx_market_policy_versions(
    fx_market_policy_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(fx_market_policy_version_id) = 16),
    market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    maker_fee_bps INTEGER NOT NULL CHECK(maker_fee_bps BETWEEN 0 AND 9999),
    taker_fee_bps INTEGER NOT NULL CHECK(taker_fee_bps BETWEEN 0 AND 9999),
    maximum_market_slippage_bps INTEGER NOT NULL CHECK(maximum_market_slippage_bps >= 0),
    effective_from INTEGER NOT NULL,
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(market_id, version)
) STRICT;

CREATE TABLE fx_funding_endpoints(
    fx_funding_endpoint_id BLOB NOT NULL PRIMARY KEY CHECK(length(fx_funding_endpoint_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    endpoint_kind TEXT NOT NULL CHECK(endpoint_kind IN (
        'CUSTOMER_DEPOSIT','BANK_TREASURY_LEDGER','MONETARY_AUTHORITY_LEDGER')),
    owner_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    ledger_account_id BLOB NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    monetary_authority_id BLOB NULL,
    created_at INTEGER NOT NULL,
    CHECK(
        (endpoint_kind = 'CUSTOMER_DEPOSIT' AND deposit_account_id IS NOT NULL
            AND ledger_account_id IS NULL AND bank_id IS NOT NULL AND monetary_authority_id IS NULL)
        OR (endpoint_kind = 'BANK_TREASURY_LEDGER' AND deposit_account_id IS NULL
            AND ledger_account_id IS NOT NULL AND bank_id IS NOT NULL AND monetary_authority_id IS NULL)
        OR (endpoint_kind = 'MONETARY_AUTHORITY_LEDGER' AND deposit_account_id IS NULL
            AND ledger_account_id IS NOT NULL AND bank_id IS NULL AND monetary_authority_id IS NOT NULL))
) STRICT;

CREATE TABLE fx_settlement_endpoints(
    fx_settlement_endpoint_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(fx_settlement_endpoint_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    endpoint_kind TEXT NOT NULL CHECK(endpoint_kind IN (
        'CUSTOMER_DEPOSIT','ATM_CASH_DELIVERY','INSTITUTIONAL_LEDGER','MERCHANT_PURCHASE_DELIVERY')),
    deposit_account_id BLOB NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    atm_terminal_id BLOB NULL,
    customer_cash_holder_id BLOB NULL,
    business_operation_id BLOB NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    destination_ledger_account_id BLOB NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    destination_party_id BLOB NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    merchant_profile_id BLOB NULL,
    commerce_order_id BLOB NULL,
    created_at INTEGER NOT NULL,
    CHECK(
        (endpoint_kind = 'CUSTOMER_DEPOSIT' AND deposit_account_id IS NOT NULL
            AND atm_terminal_id IS NULL AND customer_cash_holder_id IS NULL
            AND business_operation_id IS NULL AND destination_ledger_account_id IS NULL
            AND merchant_profile_id IS NULL AND commerce_order_id IS NULL)
        OR (endpoint_kind = 'ATM_CASH_DELIVERY' AND deposit_account_id IS NULL
            AND atm_terminal_id IS NOT NULL AND customer_cash_holder_id IS NOT NULL
            AND business_operation_id IS NOT NULL AND destination_ledger_account_id IS NULL
            AND merchant_profile_id IS NULL AND commerce_order_id IS NULL)
        OR (endpoint_kind = 'INSTITUTIONAL_LEDGER' AND deposit_account_id IS NULL
            AND atm_terminal_id IS NULL AND customer_cash_holder_id IS NULL
            AND destination_ledger_account_id IS NOT NULL AND destination_party_id IS NOT NULL
            AND merchant_profile_id IS NULL AND commerce_order_id IS NULL)
        OR (endpoint_kind = 'MERCHANT_PURCHASE_DELIVERY' AND deposit_account_id IS NOT NULL
            AND atm_terminal_id IS NULL AND customer_cash_holder_id IS NULL
            AND business_operation_id IS NOT NULL AND destination_ledger_account_id IS NULL
            AND merchant_profile_id IS NOT NULL AND commerce_order_id IS NOT NULL))
) STRICT;

CREATE TABLE fx_orders(
    fx_order_id BLOB NOT NULL PRIMARY KEY CHECK(length(fx_order_id) = 16),
    market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    participant_kind TEXT NOT NULL CHECK(participant_kind IN (
        'CUSTOMER','BANK_TREASURY','MONETARY_AUTHORITY')),
    participant_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    customer_account_id BLOB NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    fx_intervention_mandate_id BLOB NULL,
    side TEXT NOT NULL CHECK(side IN ('BUY_BASE','SELL_BASE')),
    order_type TEXT NOT NULL CHECK(order_type IN ('LIMIT','MARKET_IOC','MARKET_FOK')),
    time_in_force TEXT NOT NULL CHECK(time_in_force IN ('GTC','IOC','FOK')),
    price_units INTEGER NULL CHECK(price_units IS NULL OR price_units > 0),
    maximum_slippage_bps INTEGER NULL CHECK(maximum_slippage_bps IS NULL OR maximum_slippage_bps >= 0),
    original_base_minor INTEGER NOT NULL CHECK(original_base_minor > 0),
    filled_base_minor INTEGER NOT NULL CHECK(filled_base_minor >= 0),
    sequence_no INTEGER NOT NULL,
    status TEXT NOT NULL CHECK(status IN (
        'OPEN','PARTIALLY_FILLED','FILLED','CANCELLED','EXPIRED','REJECTED')),
    source_funding_endpoint_id BLOB NOT NULL
        REFERENCES fx_funding_endpoints(fx_funding_endpoint_id) ON DELETE RESTRICT,
    destination_settlement_endpoint_id BLOB NOT NULL
        REFERENCES fx_settlement_endpoints(fx_settlement_endpoint_id) ON DELETE RESTRICT,
    source_hold_id BLOB NOT NULL REFERENCES holds(hold_id) ON DELETE RESTRICT,
    fee_policy_version_id BLOB NOT NULL
        REFERENCES fx_market_policy_versions(fx_market_policy_version_id) ON DELETE RESTRICT,
    maker_received_gross_minor INTEGER NOT NULL DEFAULT 0 CHECK(maker_received_gross_minor >= 0),
    maker_fee_charged_minor INTEGER NOT NULL DEFAULT 0 CHECK(maker_fee_charged_minor >= 0),
    taker_received_gross_minor INTEGER NOT NULL DEFAULT 0 CHECK(taker_received_gross_minor >= 0),
    taker_fee_charged_minor INTEGER NOT NULL DEFAULT 0 CHECK(taker_fee_charged_minor >= 0),
    created_at INTEGER NOT NULL,
    terminal_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(filled_base_minor <= original_base_minor),
    CHECK(maker_fee_charged_minor <= maker_received_gross_minor),
    CHECK(taker_fee_charged_minor <= taker_received_gross_minor),
    CHECK((participant_kind = 'CUSTOMER' AND customer_account_id IS NOT NULL
            AND fx_intervention_mandate_id IS NULL)
        OR (participant_kind = 'BANK_TREASURY' AND customer_account_id IS NULL
            AND fx_intervention_mandate_id IS NULL)
        OR (participant_kind = 'MONETARY_AUTHORITY' AND customer_account_id IS NULL
            AND fx_intervention_mandate_id IS NOT NULL)),
    UNIQUE(market_id, sequence_no)
) STRICT;

CREATE INDEX ix_fx_orders_book
    ON fx_orders(market_id, side, status, price_units, sequence_no);

CREATE INDEX ix_fx_orders_participant
    ON fx_orders(participant_party_id, status, created_at);

CREATE TABLE fx_trades(
    fx_trade_id BLOB NOT NULL PRIMARY KEY CHECK(length(fx_trade_id) = 16),
    market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    maker_order_id BLOB NOT NULL REFERENCES fx_orders(fx_order_id) ON DELETE RESTRICT,
    taker_order_id BLOB NOT NULL REFERENCES fx_orders(fx_order_id) ON DELETE RESTRICT,
    maker_fee_policy_version_id BLOB NOT NULL
        REFERENCES fx_market_policy_versions(fx_market_policy_version_id) ON DELETE RESTRICT,
    taker_fee_policy_version_id BLOB NOT NULL
        REFERENCES fx_market_policy_versions(fx_market_policy_version_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    price_units INTEGER NOT NULL CHECK(price_units > 0),
    base_minor INTEGER NOT NULL CHECK(base_minor > 0),
    quote_minor INTEGER NOT NULL CHECK(quote_minor > 0),
    maker_fee_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    maker_fee_minor INTEGER NOT NULL CHECK(maker_fee_minor >= 0),
    taker_fee_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    taker_fee_minor INTEGER NOT NULL CHECK(taker_fee_minor >= 0),
    sequence_no INTEGER NOT NULL,
    executed_at INTEGER NOT NULL,
    UNIQUE(market_id, sequence_no)
) STRICT;

CREATE INDEX ix_fx_trades_market ON fx_trades(market_id, executed_at);

CREATE TABLE fx_settlement_legs(
    fx_settlement_leg_id BLOB NOT NULL PRIMARY KEY CHECK(length(fx_settlement_leg_id) = 16),
    fx_trade_id BLOB NOT NULL REFERENCES fx_trades(fx_trade_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    leg_kind TEXT NOT NULL CHECK(leg_kind IN ('BASE','QUOTE')),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    source_funding_endpoint_id BLOB NOT NULL
        REFERENCES fx_funding_endpoints(fx_funding_endpoint_id) ON DELETE RESTRICT,
    destination_settlement_endpoint_id BLOB NOT NULL
        REFERENCES fx_settlement_endpoints(fx_settlement_endpoint_id) ON DELETE RESTRICT,
    gross_minor INTEGER NOT NULL CHECK(gross_minor > 0),
    recipient_net_minor INTEGER NOT NULL CHECK(recipient_net_minor >= 0),
    operator_fee_minor INTEGER NOT NULL CHECK(operator_fee_minor >= 0),
    operator_fee_treasury_ledger_account_id BLOB NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('CLEARING','SETTLED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(gross_minor = recipient_net_minor + operator_fee_minor),
    CHECK((operator_fee_minor = 0 AND operator_fee_treasury_ledger_account_id IS NULL)
        OR (operator_fee_minor > 0 AND operator_fee_treasury_ledger_account_id IS NOT NULL)),
    UNIQUE(fx_trade_id, leg_kind)
) STRICT;

CREATE TABLE fx_settlement_leg_components(
    fx_settlement_leg_component_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(fx_settlement_leg_component_id) = 16),
    fx_settlement_leg_id BLOB NOT NULL
        REFERENCES fx_settlement_legs(fx_settlement_leg_id) ON DELETE RESTRICT,
    component_kind TEXT NOT NULL CHECK(component_kind IN ('RECIPIENT_NET','OPERATOR_FEE')),
    source_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    destination_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    source_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    destination_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    settlement_path TEXT NOT NULL CHECK(settlement_path IN (
        'INTERNAL_BOOK','BANK_CLEARING','CENTRAL_BANK_DIRECT')),
    destination_settlement_endpoint_id BLOB NULL
        REFERENCES fx_settlement_endpoints(fx_settlement_endpoint_id) ON DELETE RESTRICT,
    destination_ledger_account_id BLOB NULL
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    clearing_instruction_id BLOB NULL
        REFERENCES clearing_instructions(clearing_instruction_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('INTERNAL_FINAL','CLEARING','SETTLED')),
    created_at INTEGER NOT NULL,
    settled_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((component_kind = 'RECIPIENT_NET' AND destination_settlement_endpoint_id IS NOT NULL
            AND destination_ledger_account_id IS NULL)
        OR (component_kind = 'OPERATOR_FEE' AND destination_settlement_endpoint_id IS NULL
            AND destination_ledger_account_id IS NOT NULL)),
    CHECK((settlement_path = 'BANK_CLEARING' AND source_bank_id IS NOT NULL
            AND destination_bank_id IS NOT NULL AND clearing_instruction_id IS NOT NULL)
        OR (settlement_path = 'INTERNAL_BOOK' AND clearing_instruction_id IS NULL)
        OR (settlement_path = 'CENTRAL_BANK_DIRECT' AND clearing_instruction_id IS NULL)),
    UNIQUE(fx_settlement_leg_id, component_kind)
) STRICT;

CREATE TABLE fx_market_summaries(
    market_id BLOB NOT NULL PRIMARY KEY REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    last_trade_price_units INTEGER NULL CHECK(
        last_trade_price_units IS NULL OR last_trade_price_units > 0),
    last_trade_sequence_no INTEGER NULL,
    summary_version INTEGER NOT NULL CHECK(summary_version > 0),
    order_book_version INTEGER NOT NULL CHECK(order_book_version > 0),
    updated_at INTEGER NOT NULL
) STRICT;

CREATE TABLE fx_ohlc_buckets(
    market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    bucket_seconds INTEGER NOT NULL CHECK(bucket_seconds IN (60, 300, 3600)),
    bucket_start INTEGER NOT NULL,
    open_price_units INTEGER NOT NULL CHECK(open_price_units > 0),
    high_price_units INTEGER NOT NULL CHECK(high_price_units > 0),
    low_price_units INTEGER NOT NULL CHECK(low_price_units > 0),
    close_price_units INTEGER NOT NULL CHECK(close_price_units > 0),
    base_volume_minor INTEGER NOT NULL CHECK(base_volume_minor >= 0),
    quote_volume_minor INTEGER NOT NULL CHECK(quote_volume_minor >= 0),
    last_trade_sequence_no INTEGER NOT NULL,
    projection_version INTEGER NOT NULL CHECK(projection_version > 0),
    PRIMARY KEY(market_id, bucket_seconds, bucket_start)
) STRICT;
