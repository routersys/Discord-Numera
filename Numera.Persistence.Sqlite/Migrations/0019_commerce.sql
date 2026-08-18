CREATE TABLE merchant_aftercare_policy_versions(
    merchant_aftercare_policy_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_aftercare_policy_version_id) = 16),
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    refund_window_seconds INTEGER NOT NULL CHECK(refund_window_seconds BETWEEN 0 AND 31536000),
    return_request_window_seconds INTEGER NOT NULL
        CHECK(return_request_window_seconds BETWEEN 0 AND 31536000),
    customer_return_request_enabled INTEGER NOT NULL
        CHECK(customer_return_request_enabled IN (0,1)),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(merchant_profile_id, version)
) STRICT;

CREATE UNIQUE INDEX ux_merchant_aftercare_policy_current
    ON merchant_aftercare_policy_versions(merchant_profile_id) WHERE status = 'PUBLISHED';

CREATE TABLE merchant_products(
    merchant_product_id BLOB NOT NULL PRIMARY KEY CHECK(length(merchant_product_id) = 16),
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    sku TEXT NOT NULL CHECK(length(sku) BETWEEN 1 AND 32),
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    description TEXT NOT NULL CHECK(length(description) BETWEEN 0 AND 512),
    inventory_mode TEXT NOT NULL CHECK(inventory_mode IN ('UNLIMITED','FINITE')),
    sale_scope_override TEXT NOT NULL CHECK(sale_scope_override IN ('INHERIT','LOCAL_GUILD')),
    current_price_version_id BLOB NULL
        REFERENCES merchant_product_price_versions(merchant_product_price_version_id)
        ON DELETE RESTRICT,
    current_purchase_policy_version_id BLOB NULL
        REFERENCES merchant_product_purchase_policy_versions(
            merchant_product_purchase_policy_version_id) ON DELETE RESTRICT,
    current_fulfillment_policy_version_id BLOB NULL
        REFERENCES merchant_fulfillment_policy_versions(merchant_fulfillment_policy_version_id)
        ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(merchant_profile_id, sku)
) STRICT;

CREATE INDEX ix_merchant_products_profile_status
    ON merchant_products(merchant_profile_id, status, merchant_product_id);

CREATE TABLE merchant_product_price_versions(
    merchant_product_price_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_product_price_version_id) = 16),
    merchant_product_id BLOB NOT NULL
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    unit_price_minor INTEGER NOT NULL CHECK(unit_price_minor > 0),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(merchant_product_id, version)
) STRICT;

CREATE UNIQUE INDEX ux_merchant_product_price_current
    ON merchant_product_price_versions(merchant_product_id) WHERE status = 'PUBLISHED';

CREATE INDEX ix_merchant_product_price_history
    ON merchant_product_price_versions(merchant_product_id, status, version DESC);

CREATE TABLE merchant_product_purchase_policy_versions(
    merchant_product_purchase_policy_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_product_purchase_policy_version_id) = 16),
    merchant_product_id BLOB NOT NULL
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    per_order_quantity_limit INTEGER NULL
        CHECK(per_order_quantity_limit IS NULL OR per_order_quantity_limit > 0),
    per_customer_business_day_limit INTEGER NULL
        CHECK(per_customer_business_day_limit IS NULL OR per_customer_business_day_limit > 0),
    per_customer_lifetime_limit INTEGER NULL
        CHECK(per_customer_lifetime_limit IS NULL OR per_customer_lifetime_limit > 0),
    available_from INTEGER NULL,
    available_until INTEGER NULL,
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(available_from IS NULL OR available_until IS NULL OR available_until > available_from),
    UNIQUE(merchant_product_id, version)
) STRICT;

CREATE UNIQUE INDEX ux_merchant_product_purchase_policy_current
    ON merchant_product_purchase_policy_versions(merchant_product_id) WHERE status = 'PUBLISHED';

CREATE INDEX ix_merchant_product_purchase_policy_history
    ON merchant_product_purchase_policy_versions(merchant_product_id, status, version DESC);

CREATE TABLE merchant_fulfillment_policy_versions(
    merchant_fulfillment_policy_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_fulfillment_policy_version_id) = 16),
    merchant_product_id BLOB NOT NULL
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    fulfillment_kind TEXT NOT NULL CHECK(fulfillment_kind IN ('NONE','DISCORD_ROLE')),
    trigger TEXT NOT NULL CHECK(trigger IN ('ON_CAPTURE','ON_SETTLEMENT_FINAL')),
    discord_role_id TEXT NULL CHECK(discord_role_id IS NULL
        OR length(discord_role_id) BETWEEN 1 AND 20),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((fulfillment_kind = 'NONE' AND discord_role_id IS NULL)
       OR (fulfillment_kind = 'DISCORD_ROLE' AND discord_role_id IS NOT NULL)),
    UNIQUE(merchant_product_id, version)
) STRICT;

CREATE UNIQUE INDEX ux_merchant_product_fulfillment_policy_current
    ON merchant_fulfillment_policy_versions(merchant_product_id) WHERE status = 'PUBLISHED';

CREATE UNIQUE INDEX ux_merchant_fulfillment_role_current
    ON merchant_fulfillment_policy_versions(discord_role_id)
    WHERE status = 'PUBLISHED' AND fulfillment_kind = 'DISCORD_ROLE';

CREATE INDEX ix_merchant_fulfillment_policy_history
    ON merchant_fulfillment_policy_versions(merchant_product_id, status, version DESC);

CREATE TABLE merchant_inventory_positions(
    merchant_product_id BLOB NOT NULL PRIMARY KEY
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    on_hand_quantity INTEGER NOT NULL CHECK(on_hand_quantity >= 0),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE commerce_orders(
    commerce_order_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_order_id) = 16),
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    origin_guild_id TEXT NOT NULL CHECK(length(origin_guild_id) BETWEEN 1 AND 20),
    merchant_home_guild_id_snapshot TEXT NOT NULL
        CHECK(length(merchant_home_guild_id_snapshot) BETWEEN 1 AND 20),
    purchaser_discord_user_id_snapshot TEXT NOT NULL
        CHECK(length(purchaser_discord_user_id_snapshot) BETWEEN 1 AND 20),
    merchant_aftercare_policy_version_id BLOB NOT NULL
        REFERENCES merchant_aftercare_policy_versions(merchant_aftercare_policy_version_id)
        ON DELETE RESTRICT,
    presentment_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    order_total_presentment_minor INTEGER NOT NULL CHECK(order_total_presentment_minor > 0),
    status TEXT NOT NULL CHECK(status IN ('CREATED','AWAITING_CONFIRMATION','PROCESSING','PAID',
        'PARTIALLY_REFUNDED','REFUNDED','CANCELLED','FAILED')),
    created_at INTEGER NOT NULL,
    checkout_expires_at INTEGER NOT NULL,
    confirmed_at INTEGER NULL,
    refund_eligible_until INTEGER NULL,
    return_request_eligible_until INTEGER NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(checkout_expires_at > created_at)
) STRICT;

CREATE INDEX ix_commerce_orders_customer
    ON commerce_orders(customer_account_id, created_at DESC, commerce_order_id DESC);

CREATE INDEX ix_commerce_orders_merchant
    ON commerce_orders(merchant_profile_id, status, created_at, commerce_order_id);

CREATE INDEX ix_commerce_orders_checkout_expiry
    ON commerce_orders(status, checkout_expires_at, commerce_order_id);

CREATE INDEX ix_commerce_orders_refund_window
    ON commerce_orders(merchant_profile_id, status, refund_eligible_until, commerce_order_id);

CREATE TABLE commerce_order_lines(
    commerce_order_line_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_order_line_id) = 16),
    commerce_order_id BLOB NOT NULL
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    merchant_product_id BLOB NOT NULL
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    merchant_product_price_version_id BLOB NOT NULL
        REFERENCES merchant_product_price_versions(merchant_product_price_version_id)
        ON DELETE RESTRICT,
    merchant_product_purchase_policy_version_id BLOB NULL
        REFERENCES merchant_product_purchase_policy_versions(
            merchant_product_purchase_policy_version_id) ON DELETE RESTRICT,
    merchant_fulfillment_policy_version_id BLOB NULL
        REFERENCES merchant_fulfillment_policy_versions(merchant_fulfillment_policy_version_id)
        ON DELETE RESTRICT,
    product_name_snapshot TEXT NOT NULL CHECK(length(product_name_snapshot) BETWEEN 1 AND 64),
    unit_price_minor INTEGER NOT NULL CHECK(unit_price_minor > 0),
    quantity INTEGER NOT NULL CHECK(quantity > 0),
    line_subtotal_minor INTEGER NOT NULL CHECK(line_subtotal_minor > 0),
    UNIQUE(commerce_order_id, merchant_product_id)
) STRICT;

CREATE INDEX ix_commerce_order_lines_order
    ON commerce_order_lines(commerce_order_id, commerce_order_line_id);

CREATE INDEX ix_commerce_order_lines_product
    ON commerce_order_lines(merchant_product_id, commerce_order_id);

CREATE TABLE commerce_returns(
    commerce_return_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_return_id) = 16),
    commerce_order_id BLOB NOT NULL
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    requested_by_discord_user_id TEXT NOT NULL
        CHECK(length(requested_by_discord_user_id) BETWEEN 1 AND 20),
    decided_by_discord_user_id TEXT NULL
        CHECK(decided_by_discord_user_id IS NULL
            OR length(decided_by_discord_user_id) BETWEEN 1 AND 20),
    status TEXT NOT NULL
        CHECK(status IN ('PENDING','APPROVED','REJECTED','CANCELLED','COMPLETED')),
    reason_code TEXT NOT NULL CHECK(length(reason_code) BETWEEN 1 AND 64),
    cancellation_reason_code TEXT NULL
        CHECK(cancellation_reason_code IS NULL
            OR length(cancellation_reason_code) BETWEEN 1 AND 64),
    created_at INTEGER NOT NULL,
    decided_at INTEGER NULL,
    cancelled_at INTEGER NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_commerce_returns_order
    ON commerce_returns(commerce_order_id, status, commerce_return_id);

CREATE TABLE commerce_return_lines(
    commerce_return_line_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_return_line_id) = 16),
    commerce_return_id BLOB NOT NULL
        REFERENCES commerce_returns(commerce_return_id) ON DELETE RESTRICT,
    commerce_order_line_id BLOB NOT NULL
        REFERENCES commerce_order_lines(commerce_order_line_id) ON DELETE RESTRICT,
    quantity INTEGER NOT NULL CHECK(quantity > 0),
    UNIQUE(commerce_return_id, commerce_order_line_id)
) STRICT;

CREATE INDEX ix_commerce_return_lines_order_line
    ON commerce_return_lines(commerce_order_line_id, commerce_return_line_id);

CREATE TABLE merchant_inventory_movements(
    merchant_inventory_movement_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_inventory_movement_id) = 16),
    merchant_product_id BLOB NOT NULL
        REFERENCES merchant_products(merchant_product_id) ON DELETE RESTRICT,
    commerce_order_id BLOB NULL
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    commerce_return_line_id BLOB NULL
        REFERENCES commerce_return_lines(commerce_return_line_id) ON DELETE RESTRICT,
    movement_kind TEXT NOT NULL
        CHECK(movement_kind IN ('ADJUST_IN','ADJUST_OUT','SALE','REFUND_RETURN')),
    quantity_delta INTEGER NOT NULL CHECK(quantity_delta <> 0),
    created_by_discord_user_id TEXT NULL
        CHECK(created_by_discord_user_id IS NULL
            OR length(created_by_discord_user_id) BETWEEN 1 AND 20),
    created_at INTEGER NOT NULL,
    CHECK((movement_kind IN ('ADJUST_IN','REFUND_RETURN') AND quantity_delta > 0)
       OR (movement_kind IN ('ADJUST_OUT','SALE') AND quantity_delta < 0)),
    CHECK((movement_kind = 'REFUND_RETURN' AND commerce_return_line_id IS NOT NULL)
       OR (movement_kind <> 'REFUND_RETURN' AND commerce_return_line_id IS NULL)),
    UNIQUE(commerce_return_line_id)
) STRICT;

CREATE INDEX ix_merchant_inventory_movements_product
    ON merchant_inventory_movements(
        merchant_product_id, created_at DESC, merchant_inventory_movement_id DESC);

CREATE TABLE debit_card_authorizations(
    debit_card_authorization_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(debit_card_authorization_id) = 16),
    debit_card_id BLOB NOT NULL REFERENCES debit_cards(debit_card_id) ON DELETE RESTRICT,
    deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    commerce_order_id BLOB NULL
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    merchant_destination_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    source_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    presentment_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    hold_id BLOB NULL REFERENCES holds(hold_id) ON DELETE RESTRICT,
    merchant_reference TEXT NOT NULL CHECK(length(merchant_reference) BETWEEN 1 AND 64),
    authorization_amount_minor INTEGER NOT NULL CHECK(authorization_amount_minor > 0),
    captured_amount_minor INTEGER NOT NULL CHECK(captured_amount_minor >= 0),
    refunded_amount_minor INTEGER NOT NULL CHECK(refunded_amount_minor >= 0),
    presentment_authorized_minor INTEGER NOT NULL CHECK(presentment_authorized_minor > 0),
    presentment_captured_minor INTEGER NOT NULL CHECK(presentment_captured_minor >= 0),
    presentment_refunded_minor INTEGER NOT NULL CHECK(presentment_refunded_minor >= 0),
    fee_schedule_version_id BLOB NOT NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    purchase_fee_assessed_minor INTEGER NOT NULL DEFAULT 0
        CHECK(purchase_fee_assessed_minor >= 0),
    settlement_route TEXT NOT NULL
        CHECK(settlement_route IN ('SAME_CURRENCY_PAYMENT','FX_FOK')),
    status TEXT NOT NULL CHECK(status IN ('AUTHORIZED','PARTIALLY_CAPTURED','CAPTURED',
        'PARTIALLY_REFUNDED','REFUNDED','REVERSED','EXPIRED','DECLINED')),
    authorized_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(captured_amount_minor <= authorization_amount_minor),
    CHECK(presentment_captured_minor <= presentment_authorized_minor),
    CHECK(presentment_refunded_minor <= presentment_captured_minor),
    CHECK((settlement_route = 'SAME_CURRENCY_PAYMENT'
            AND source_currency_id = presentment_currency_id)
       OR settlement_route = 'FX_FOK'),
    CHECK((status = 'DECLINED' AND hold_id IS NULL AND captured_amount_minor = 0
            AND presentment_captured_minor = 0 AND refunded_amount_minor = 0
            AND presentment_refunded_minor = 0)
       OR (status <> 'DECLINED' AND hold_id IS NOT NULL)),
    CHECK(commerce_order_id IS NULL OR status NOT IN ('AUTHORIZED','PARTIALLY_CAPTURED')),
    UNIQUE(merchant_profile_id, merchant_reference),
    UNIQUE(commerce_order_id)
) STRICT;

CREATE INDEX ix_debit_card_authorizations_due
    ON debit_card_authorizations(status, expires_at, debit_card_authorization_id);

CREATE TABLE commerce_payments(
    commerce_payment_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_payment_id) = 16),
    commerce_order_id BLOB NOT NULL UNIQUE
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    debit_card_authorization_id BLOB NULL
        REFERENCES debit_card_authorizations(debit_card_authorization_id) ON DELETE RESTRICT,
    source_currency_id BLOB NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    source_principal_minor INTEGER NOT NULL DEFAULT 0 CHECK(source_principal_minor >= 0),
    presentment_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    presentment_paid_minor INTEGER NOT NULL DEFAULT 0 CHECK(presentment_paid_minor >= 0),
    presentment_refunded_minor INTEGER NOT NULL DEFAULT 0 CHECK(presentment_refunded_minor >= 0),
    payment_route TEXT NULL CHECK(payment_route IS NULL
        OR payment_route IN ('SAME_CURRENCY_DEBIT','FX_FOK_DEBIT')),
    status TEXT NOT NULL CHECK(status IN ('PENDING','PAID','PARTIALLY_REFUNDED','REFUNDED',
        'CANCELLED','FAILED')),
    created_at INTEGER NOT NULL,
    capture_committed_at INTEGER NULL,
    merchant_settlement_finalized_at INTEGER NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(presentment_refunded_minor <= presentment_paid_minor)
) STRICT;

CREATE INDEX ix_commerce_payments_status
    ON commerce_payments(status, created_at, commerce_payment_id);

CREATE TABLE commerce_checkout_confirmations(
    commerce_checkout_confirmation_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(commerce_checkout_confirmation_id) = 16),
    commerce_order_id BLOB NOT NULL
        REFERENCES commerce_orders(commerce_order_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    debit_card_id BLOB NOT NULL REFERENCES debit_cards(debit_card_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    source_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    presentment_currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    fx_market_id BLOB NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    fx_market_policy_version_id BLOB NULL
        REFERENCES fx_market_policy_versions(fx_market_policy_version_id) ON DELETE RESTRICT,
    order_book_version INTEGER NULL CHECK(order_book_version IS NULL OR order_book_version >= 0),
    estimated_source_principal_minor INTEGER NOT NULL
        CHECK(estimated_source_principal_minor >= 0),
    estimated_fx_fee_minor INTEGER NOT NULL CHECK(estimated_fx_fee_minor >= 0),
    estimated_purchase_fee_minor INTEGER NOT NULL CHECK(estimated_purchase_fee_minor >= 0),
    confirmed_maximum_slippage_bps INTEGER NOT NULL
        CHECK(confirmed_maximum_slippage_bps BETWEEN 0 AND 10000),
    confirmed_max_source_debit_minor INTEGER NOT NULL
        CHECK(confirmed_max_source_debit_minor > 0),
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    consumed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(expires_at > created_at),
    CHECK(expires_at - created_at <= 300000)
) STRICT;

CREATE INDEX ix_commerce_checkout_confirmations_order
    ON commerce_checkout_confirmations(
        commerce_order_id, customer_account_id, expires_at, commerce_checkout_confirmation_id);

CREATE TABLE commerce_refund_confirmations(
    commerce_refund_confirmation_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(commerce_refund_confirmation_id) = 16),
    commerce_payment_id BLOB NOT NULL
        REFERENCES commerce_payments(commerce_payment_id) ON DELETE RESTRICT,
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    actor_discord_user_id TEXT NOT NULL CHECK(length(actor_discord_user_id) BETWEEN 1 AND 20),
    presentment_refund_minor INTEGER NOT NULL CHECK(presentment_refund_minor > 0),
    fx_market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    fx_market_policy_version_id BLOB NOT NULL
        REFERENCES fx_market_policy_versions(fx_market_policy_version_id) ON DELETE RESTRICT,
    order_book_version INTEGER NOT NULL CHECK(order_book_version >= 0),
    estimated_source_refund_net_minor INTEGER NOT NULL
        CHECK(estimated_source_refund_net_minor > 0),
    confirmed_min_source_refund_net_minor INTEGER NOT NULL
        CHECK(confirmed_min_source_refund_net_minor > 0),
    confirmed_maximum_slippage_bps INTEGER NOT NULL
        CHECK(confirmed_maximum_slippage_bps BETWEEN 0 AND 10000),
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    consumed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(expires_at > created_at),
    CHECK(expires_at - created_at <= 300000),
    CHECK(confirmed_min_source_refund_net_minor <= estimated_source_refund_net_minor)
) STRICT;

CREATE INDEX ix_commerce_refund_confirmations_payment
    ON commerce_refund_confirmations(
        commerce_payment_id, expires_at, commerce_refund_confirmation_id);

CREATE TABLE commerce_fulfillments(
    commerce_fulfillment_id BLOB NOT NULL PRIMARY KEY CHECK(length(commerce_fulfillment_id) = 16),
    commerce_order_line_id BLOB NOT NULL
        REFERENCES commerce_order_lines(commerce_order_line_id) ON DELETE RESTRICT,
    merchant_fulfillment_policy_version_id BLOB NOT NULL
        REFERENCES merchant_fulfillment_policy_versions(merchant_fulfillment_policy_version_id)
        ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('PENDING','SUCCEEDED','CANCELLED_RETURNED',
        'FAILED_RETRYABLE','FAILED_MANUAL')),
    attempt_count INTEGER NOT NULL CHECK(attempt_count BETWEEN 0 AND 5),
    next_attempt_at INTEGER NULL,
    failure_code TEXT NULL CHECK(failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 64),
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(commerce_order_line_id, merchant_fulfillment_policy_version_id)
) STRICT;

CREATE INDEX ix_commerce_fulfillments_due
    ON commerce_fulfillments(status, next_attempt_at, commerce_fulfillment_id);

CREATE TABLE commerce_fulfillment_reversals(
    commerce_fulfillment_reversal_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(commerce_fulfillment_reversal_id) = 16),
    commerce_fulfillment_id BLOB NOT NULL UNIQUE
        REFERENCES commerce_fulfillments(commerce_fulfillment_id) ON DELETE RESTRICT,
    commerce_return_line_id BLOB NOT NULL
        REFERENCES commerce_return_lines(commerce_return_line_id) ON DELETE RESTRICT,
    status TEXT NOT NULL
        CHECK(status IN ('PENDING','SUCCEEDED','FAILED_RETRYABLE','FAILED_MANUAL')),
    attempt_count INTEGER NOT NULL CHECK(attempt_count BETWEEN 0 AND 5),
    next_attempt_at INTEGER NULL,
    failure_code TEXT NULL CHECK(failure_code IS NULL OR length(failure_code) BETWEEN 1 AND 64),
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_commerce_fulfillment_reversals_due
    ON commerce_fulfillment_reversals(status, next_attempt_at, commerce_fulfillment_reversal_id);

CREATE TABLE debit_card_captures(
    debit_card_capture_id BLOB NOT NULL PRIMARY KEY CHECK(length(debit_card_capture_id) = 16),
    debit_card_authorization_id BLOB NOT NULL
        REFERENCES debit_card_authorizations(debit_card_authorization_id) ON DELETE RESTRICT,
    merchant_capture_reference TEXT NOT NULL
        CHECK(length(merchant_capture_reference) BETWEEN 1 AND 64),
    source_principal_minor INTEGER NOT NULL CHECK(source_principal_minor > 0),
    presentment_amount_minor INTEGER NOT NULL CHECK(presentment_amount_minor > 0),
    purchase_fee_minor INTEGER NOT NULL CHECK(purchase_fee_minor >= 0),
    settlement_route TEXT NOT NULL CHECK(settlement_route IN ('SAME_CURRENCY_PAYMENT','FX_FOK')),
    payment_order_id BLOB NULL UNIQUE
        REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    fx_business_operation_id BLOB NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    captured_at INTEGER NOT NULL,
    CHECK((settlement_route = 'SAME_CURRENCY_PAYMENT' AND payment_order_id IS NOT NULL
            AND fx_business_operation_id IS NULL)
       OR (settlement_route = 'FX_FOK' AND payment_order_id IS NULL
            AND fx_business_operation_id IS NOT NULL)),
    UNIQUE(debit_card_authorization_id, merchant_capture_reference)
) STRICT;

CREATE INDEX ix_debit_card_captures_authorization
    ON debit_card_captures(debit_card_authorization_id, captured_at, debit_card_capture_id);

CREATE TABLE debit_card_refunds(
    debit_card_refund_id BLOB NOT NULL PRIMARY KEY CHECK(length(debit_card_refund_id) = 16),
    debit_card_authorization_id BLOB NOT NULL
        REFERENCES debit_card_authorizations(debit_card_authorization_id) ON DELETE RESTRICT,
    merchant_refund_reference TEXT NOT NULL
        CHECK(length(merchant_refund_reference) BETWEEN 1 AND 64),
    source_refund_minor INTEGER NOT NULL CHECK(source_refund_minor > 0),
    presentment_refund_minor INTEGER NOT NULL CHECK(presentment_refund_minor > 0),
    settlement_route TEXT NOT NULL CHECK(settlement_route IN ('SAME_CURRENCY_PAYMENT','FX_FOK')),
    payment_order_id BLOB NULL UNIQUE
        REFERENCES payment_orders(payment_order_id) ON DELETE RESTRICT,
    fx_business_operation_id BLOB NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    refunded_at INTEGER NOT NULL,
    CHECK((settlement_route = 'SAME_CURRENCY_PAYMENT' AND payment_order_id IS NOT NULL
            AND fx_business_operation_id IS NULL)
       OR (settlement_route = 'FX_FOK' AND payment_order_id IS NULL
            AND fx_business_operation_id IS NOT NULL)),
    UNIQUE(debit_card_authorization_id, merchant_refund_reference)
) STRICT;

CREATE INDEX ix_debit_card_refunds_authorization
    ON debit_card_refunds(debit_card_authorization_id, refunded_at, debit_card_refund_id);

PRAGMA defer_foreign_keys = ON;

CREATE TABLE merchant_profiles_rebuilt(
    merchant_profile_id BLOB NOT NULL PRIMARY KEY CHECK(length(merchant_profile_id) = 16),
    party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    home_guild_id TEXT NOT NULL CHECK(length(home_guild_id) BETWEEN 1 AND 20),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    settlement_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    catalog_visibility_scope TEXT NOT NULL
        CHECK(catalog_visibility_scope IN ('LOCAL_GUILD','GLOBAL')),
    payment_scope TEXT NOT NULL CHECK(payment_scope IN ('LOCAL_GUILD','GLOBAL')),
    cross_currency_mode TEXT NOT NULL CHECK(cross_currency_mode IN ('DISABLED','FX_FOK')),
    maximum_checkout_slippage_bps INTEGER NOT NULL
        CHECK(maximum_checkout_slippage_bps BETWEEN 0 AND 10000),
    current_aftercare_policy_version_id BLOB NULL
        REFERENCES merchant_aftercare_policy_versions(merchant_aftercare_policy_version_id)
        ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','CLOSING','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

INSERT INTO merchant_profiles_rebuilt(
    merchant_profile_id,
    party_id,
    home_guild_id,
    currency_id,
    settlement_deposit_account_id,
    display_name,
    catalog_visibility_scope,
    payment_scope,
    cross_currency_mode,
    maximum_checkout_slippage_bps,
    current_aftercare_policy_version_id,
    status,
    created_at,
    version)
SELECT
    merchant_profile_id,
    party_id,
    home_guild_id,
    currency_id,
    settlement_deposit_account_id,
    display_name,
    catalog_visibility_scope,
    payment_scope,
    cross_currency_mode,
    maximum_checkout_slippage_bps,
    current_aftercare_policy_version_id,
    status,
    created_at,
    version
FROM merchant_profiles;

DROP TABLE merchant_profiles;

ALTER TABLE merchant_profiles_rebuilt RENAME TO merchant_profiles;

CREATE INDEX ix_merchant_profiles_guild ON merchant_profiles(home_guild_id, status);

CREATE INDEX ix_merchant_profiles_settlement_account
    ON merchant_profiles(settlement_deposit_account_id, status, merchant_profile_id);
