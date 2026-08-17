CREATE TABLE host_settings(
    key TEXT NOT NULL PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE system_owner_identities(
    discord_user_id TEXT NOT NULL PRIMARY KEY CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    created_at INTEGER NOT NULL
) STRICT;

CREATE TABLE parties(
    party_id BLOB NOT NULL PRIMARY KEY CHECK(length(party_id) = 16),
    party_type TEXT NOT NULL CHECK(party_type IN ('CUSTOMER','BANK','GUILD_TREASURY','GOVERNMENT','SYSTEM','CORPORATION')),
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE customer_accounts(
    customer_account_id BLOB NOT NULL PRIMARY KEY CHECK(length(customer_account_id) = 16),
    party_id BLOB NOT NULL UNIQUE REFERENCES parties(party_id) ON DELETE RESTRICT,
    public_handle TEXT NOT NULL UNIQUE CHECK(length(public_handle) BETWEEN 3 AND 32),
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','SUSPENDED','CLOSED')),
    created_at INTEGER NOT NULL,
    last_authenticated_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE discord_identity_links(
    discord_identity_link_id BLOB NOT NULL PRIMARY KEY CHECK(length(discord_identity_link_id) = 16),
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    discord_user_id TEXT NOT NULL CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    is_primary INTEGER NOT NULL CHECK(is_primary IN (0, 1)),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','UNLINKED')),
    linked_at INTEGER NOT NULL,
    unlinked_at INTEGER NULL,
    last_authenticated_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'ACTIVE' AND unlinked_at IS NULL) OR (status = 'UNLINKED' AND unlinked_at IS NOT NULL)),
    CHECK(status = 'ACTIVE' OR is_primary = 0)
) STRICT;

CREATE UNIQUE INDEX ux_discord_identity_links_active_user
    ON discord_identity_links(discord_user_id) WHERE status = 'ACTIVE';

CREATE UNIQUE INDEX ux_discord_identity_links_active_primary
    ON discord_identity_links(customer_account_id) WHERE status = 'ACTIVE' AND is_primary = 1;

CREATE TABLE guild_economies(
    economy_scope_id BLOB NOT NULL PRIMARY KEY CHECK(length(economy_scope_id) = 16),
    guild_id TEXT NOT NULL UNIQUE CHECK(length(guild_id) BETWEEN 1 AND 20),
    canonical_timezone TEXT NOT NULL CHECK(length(canonical_timezone) BETWEEN 1 AND 64),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','DISABLED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE currencies(
    currency_id BLOB NOT NULL PRIMARY KEY CHECK(length(currency_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','RETIRING','RETIRED')),
    minor_unit_digits INTEGER NOT NULL CHECK(minor_unit_digits BETWEEN 0 AND 6),
    base_money_supply_cap_minor INTEGER NULL CHECK(base_money_supply_cap_minor IS NULL OR base_money_supply_cap_minor >= 0),
    created_at INTEGER NOT NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE UNIQUE INDEX ux_currencies_current_per_scope
    ON currencies(economy_scope_id) WHERE status IN ('ACTIVE', 'SUSPENDED', 'RETIRING');

CREATE TABLE currency_metadata_versions(
    currency_metadata_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(currency_metadata_version_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 64),
    code TEXT NOT NULL CHECK(length(code) BETWEEN 1 AND 16),
    symbol TEXT NOT NULL CHECK(length(symbol) BETWEEN 1 AND 8),
    display_pattern TEXT NOT NULL CHECK(length(display_pattern) BETWEEN 1 AND 64),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(effective_to IS NULL OR effective_to > effective_from)
) STRICT;

CREATE TABLE accounting_books(
    accounting_book_id BLOB NOT NULL PRIMARY KEY CHECK(length(accounting_book_id) = 16),
    owner_party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    book_kind TEXT NOT NULL CHECK(book_kind IN ('COMMERCIAL_BANK','CENTRAL_BANK','SYSTEM')),
    status TEXT NOT NULL CHECK(status IN ('OPEN','RECONCILIATION_REQUIRED','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE accounting_periods(
    accounting_period_id BLOB NOT NULL PRIMARY KEY CHECK(length(accounting_period_id) = 16),
    accounting_book_id BLOB NOT NULL REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    period_key TEXT NOT NULL CHECK(length(period_key) BETWEEN 1 AND 16),
    starts_on TEXT NOT NULL CHECK(length(starts_on) = 10),
    ends_on TEXT NOT NULL CHECK(length(ends_on) = 10),
    status TEXT NOT NULL CHECK(status IN ('OPEN','CLOSING','CLOSED')),
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(accounting_book_id, period_key),
    CHECK(ends_on >= starts_on),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE TABLE banks(
    bank_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    party_id BLOB NOT NULL UNIQUE REFERENCES parties(party_id) ON DELETE RESTRICT,
    institution_code TEXT NOT NULL UNIQUE CHECK(length(institution_code) BETWEEN 4 AND 16),
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 80),
    bank_kind TEXT NOT NULL CHECK(bank_kind IN ('NORMAL','BRIDGE')),
    resolution_case_id BLOB NULL CHECK(resolution_case_id IS NULL OR length(resolution_case_id) = 16),
    status TEXT NOT NULL CHECK(status IN ('PENDING_ACTIVATION','OPERATING','RESTRICTED','SETTLEMENT_SUSPENDED','RESOLUTION','CLOSING','CLOSED')),
    general_ledger_book_id BLOB NOT NULL UNIQUE REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    current_policy_version_id BLOB NULL CHECK(current_policy_version_id IS NULL OR length(current_policy_version_id) = 16),
    current_fee_schedule_version_id BLOB NULL CHECK(current_fee_schedule_version_id IS NULL OR length(current_fee_schedule_version_id) = 16),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((bank_kind = 'NORMAL' AND resolution_case_id IS NULL) OR (bank_kind = 'BRIDGE' AND resolution_case_id IS NOT NULL))
) STRICT;

CREATE TABLE branches(
    branch_id BLOB NOT NULL PRIMARY KEY CHECK(length(branch_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    branch_code TEXT NOT NULL CHECK(length(branch_code) BETWEEN 3 AND 8),
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 80),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    created_at INTEGER NOT NULL,
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, branch_code),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE TABLE bank_customer_relationships(
    relationship_id BLOB NOT NULL PRIMARY KEY CHECK(length(relationship_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    customer_number TEXT NOT NULL CHECK(length(customer_number) BETWEEN 6 AND 16),
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','RESTRICTED','TERMINATING','CLOSED')),
    opened_at INTEGER NOT NULL,
    closed_at INTEGER NULL,
    risk_classification TEXT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, party_id),
    UNIQUE(bank_id, customer_number),
    CHECK((status = 'CLOSED' AND closed_at IS NOT NULL) OR (status <> 'CLOSED' AND closed_at IS NULL))
) STRICT;

CREATE TABLE account_products(
    product_id BLOB NOT NULL PRIMARY KEY CHECK(length(product_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    product_code TEXT NOT NULL CHECK(length(product_code) BETWEEN 1 AND 32),
    name TEXT NOT NULL CHECK(length(name) BETWEEN 1 AND 80),
    deposit_class TEXT NOT NULL CHECK(deposit_class IN ('DEMAND','CURRENT','SAVINGS','TIME')),
    version_application_policy TEXT NOT NULL CHECK(version_application_policy IN ('FOLLOW_LATEST','FIXED_CONTRACT')),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, product_code)
) STRICT;

CREATE TABLE account_product_versions(
    product_version_id BLOB NOT NULL PRIMARY KEY CHECK(length(product_version_id) = 16),
    product_id BLOB NOT NULL REFERENCES account_products(product_id) ON DELETE RESTRICT,
    version INTEGER NOT NULL CHECK(version >= 1),
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    annual_rate_ppt INTEGER NOT NULL CHECK(annual_rate_ppt >= 0),
    day_count_basis TEXT NOT NULL CHECK(day_count_basis = 'ACTUAL_365_FIXED'),
    minimum_balance_minor INTEGER NOT NULL CHECK(minimum_balance_minor >= 0),
    maximum_balance_minor INTEGER NULL CHECK(maximum_balance_minor IS NULL OR maximum_balance_minor >= minimum_balance_minor),
    daily_outgoing_limit_minor INTEGER NULL CHECK(daily_outgoing_limit_minor IS NULL OR daily_outgoing_limit_minor >= 0),
    per_transaction_limit_minor INTEGER NULL CHECK(per_transaction_limit_minor IS NULL OR per_transaction_limit_minor >= 0),
    transfer_capabilities TEXT NOT NULL,
    deposit_insurance_class_code TEXT NOT NULL CHECK(length(deposit_insurance_class_code) BETWEEN 1 AND 32 AND deposit_insurance_class_code NOT GLOB '*[^A-Z0-9_]*'),
    overdraft_policy TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    UNIQUE(product_id, version),
    CHECK(effective_to IS NULL OR effective_to > effective_from)
) STRICT;

CREATE TABLE ledger_accounts(
    ledger_account_id BLOB NOT NULL PRIMARY KEY CHECK(length(ledger_account_id) = 16),
    accounting_book_id BLOB NOT NULL REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    parent_account_id BLOB NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    account_code TEXT NOT NULL CHECK(length(account_code) BETWEEN 1 AND 32),
    account_kind TEXT NOT NULL CHECK(length(account_kind) BETWEEN 1 AND 48 AND account_kind NOT GLOB '*[^A-Z_]*'),
    accounting_type TEXT NOT NULL CHECK(accounting_type IN ('ASSET','LIABILITY','EQUITY','REVENUE','EXPENSE','CONTRA_ASSET')),
    normal_side TEXT NOT NULL CHECK(normal_side IN ('DEBIT','CREDIT')),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    posting_allowed INTEGER NOT NULL CHECK(posting_allowed IN (0, 1)),
    owner_reference_type TEXT NULL,
    owner_reference_id BLOB NULL CHECK(owner_reference_id IS NULL OR length(owner_reference_id) = 16),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(accounting_book_id, account_code)
) STRICT;

CREATE TABLE ledger_balance_projections(
    ledger_account_id BLOB NOT NULL PRIMARY KEY REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    posted_balance_minor INTEGER NOT NULL,
    held_minor INTEGER NOT NULL CHECK(held_minor >= 0),
    version INTEGER NOT NULL CHECK(version >= 1),
    updated_at INTEGER NOT NULL
) STRICT;

CREATE TABLE deposit_accounts(
    deposit_account_id BLOB NOT NULL PRIMARY KEY CHECK(length(deposit_account_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    branch_id BLOB NOT NULL REFERENCES branches(branch_id) ON DELETE RESTRICT,
    relationship_id BLOB NOT NULL REFERENCES bank_customer_relationships(relationship_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    product_id BLOB NOT NULL REFERENCES account_products(product_id) ON DELETE RESTRICT,
    current_product_version_id BLOB NOT NULL REFERENCES account_product_versions(product_version_id) ON DELETE RESTRICT,
    ledger_account_id BLOB NOT NULL UNIQUE REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    account_number TEXT NOT NULL CHECK(length(account_number) BETWEEN 6 AND 16),
    public_receiving_enabled INTEGER NOT NULL CHECK(public_receiving_enabled IN (0, 1)),
    last_customer_activity_at INTEGER NOT NULL,
    next_dormancy_fee_at INTEGER NULL,
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','RESTRICTED','FROZEN','DORMANT','CLOSING','CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION','REOPENING')),
    opened_at INTEGER NOT NULL,
    closing_requested_at INTEGER NULL,
    closure_reason TEXT NULL CHECK(closure_reason IS NULL OR closure_reason IN ('USER','DORMANCY','RESOLUTION')),
    closed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(bank_id, customer_account_id),
    UNIQUE(bank_id, branch_id, account_number),
    CHECK((status IN ('CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION') AND closed_at IS NOT NULL AND closure_reason IS NOT NULL)
       OR (status NOT IN ('CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION') AND closed_at IS NULL)),
    CHECK(status <> 'CLOSED_USER' OR closure_reason = 'USER'),
    CHECK(status <> 'CLOSED_DORMANCY' OR closure_reason = 'DORMANCY'),
    CHECK(status <> 'CLOSED_RESOLUTION' OR closure_reason = 'RESOLUTION'),
    CHECK(status IN ('CLOSING','CLOSED_USER','CLOSED_DORMANCY','CLOSED_RESOLUTION') OR closure_reason IS NULL)
) STRICT;

CREATE TABLE business_operations(
    business_operation_id BLOB NOT NULL PRIMARY KEY CHECK(length(business_operation_id) = 16),
    operation_type TEXT NOT NULL CHECK(length(operation_type) BETWEEN 1 AND 64),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    actor_party_id BLOB NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    correlation_id BLOB NOT NULL CHECK(length(correlation_id) = 16),
    idempotency_scope TEXT NOT NULL CHECK(length(idempotency_scope) BETWEEN 1 AND 64),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 1 AND 128),
    status TEXT NOT NULL CHECK(status IN ('STARTED','COMMITTED','FAILED')),
    created_at INTEGER NOT NULL,
    committed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(idempotency_scope, idempotency_key),
    CHECK((status = 'COMMITTED' AND committed_at IS NOT NULL) OR (status <> 'COMMITTED' AND committed_at IS NULL))
) STRICT;

CREATE TABLE accounting_transactions(
    accounting_transaction_id BLOB NOT NULL PRIMARY KEY CHECK(length(accounting_transaction_id) = 16),
    accounting_book_id BLOB NOT NULL REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    accounting_period_id BLOB NOT NULL REFERENCES accounting_periods(accounting_period_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    transaction_type TEXT NOT NULL CHECK(length(transaction_type) BETWEEN 1 AND 64),
    business_date TEXT NOT NULL CHECK(length(business_date) = 10),
    occurred_at INTEGER NOT NULL,
    posted_at INTEGER NOT NULL,
    reverses_transaction_id BLOB NULL REFERENCES accounting_transactions(accounting_transaction_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status = 'POSTED'),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE journal_entries(
    journal_entry_id BLOB NOT NULL PRIMARY KEY CHECK(length(journal_entry_id) = 16),
    accounting_transaction_id BLOB NOT NULL REFERENCES accounting_transactions(accounting_transaction_id) ON DELETE RESTRICT,
    ledger_account_id BLOB NOT NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    entry_sequence INTEGER NOT NULL CHECK(entry_sequence >= 0),
    side TEXT NOT NULL CHECK(side IN ('DEBIT','CREDIT')),
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    created_at INTEGER NOT NULL,
    UNIQUE(accounting_transaction_id, entry_sequence)
) STRICT;

CREATE INDEX ix_journal_entries_ledger_account ON journal_entries(ledger_account_id);

CREATE TABLE holds(
    hold_id BLOB NOT NULL PRIMARY KEY CHECK(length(hold_id) = 16),
    hold_scope_kind TEXT NOT NULL CHECK(hold_scope_kind IN ('CUSTOMER_DEPOSIT','LEDGER_ASSET')),
    deposit_account_id BLOB NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    ledger_account_id BLOB NULL REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    business_operation_id BLOB NOT NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    remaining_minor INTEGER NOT NULL CHECK(remaining_minor >= 0 AND remaining_minor <= amount_minor),
    reason TEXT NOT NULL CHECK(length(reason) BETWEEN 1 AND 64),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','CAPTURED','RELEASED','EXPIRED')),
    created_at INTEGER NOT NULL,
    expires_at INTEGER NULL,
    terminal_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'ACTIVE' AND remaining_minor > 0 AND terminal_at IS NULL)
       OR (status IN ('CAPTURED','RELEASED','EXPIRED') AND remaining_minor = 0 AND terminal_at IS NOT NULL)),
    CHECK((hold_scope_kind = 'CUSTOMER_DEPOSIT' AND deposit_account_id IS NOT NULL AND ledger_account_id IS NULL)
       OR (hold_scope_kind = 'LEDGER_ASSET' AND deposit_account_id IS NULL AND ledger_account_id IS NOT NULL))
) STRICT;

CREATE INDEX ix_holds_active_deposit_account
    ON holds(deposit_account_id) WHERE status = 'ACTIVE';

CREATE TABLE payment_orders(
    payment_order_id BLOB NOT NULL PRIMARY KEY CHECK(length(payment_order_id) = 16),
    business_operation_id BLOB NOT NULL UNIQUE REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    payer_customer_account_id BLOB NOT NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    destination_deposit_account_id BLOB NOT NULL REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    amount_minor INTEGER NOT NULL CHECK(amount_minor > 0),
    method TEXT NOT NULL CHECK(length(method) BETWEEN 1 AND 32),
    settlement_mode TEXT NOT NULL CHECK(settlement_mode IN ('INTERNAL','RTGS','CLEARING')),
    beneficiary_posting_policy TEXT NOT NULL CHECK(beneficiary_posting_policy IN ('IMMEDIATE_AFTER_ACCEPTANCE','AFTER_FINAL_SETTLEMENT','GUARANTEED_PRE_CREDIT')),
    payment_network_policy_version_id BLOB NULL CHECK(payment_network_policy_version_id IS NULL OR length(payment_network_policy_version_id) = 16),
    memo TEXT NULL CHECK(memo IS NULL OR length(memo) <= 100),
    status TEXT NOT NULL CHECK(status IN ('CREATED','AUTHORIZED','FUNDS_HELD','ACCEPTED','QUEUED','SETTLING','SETTLED','COMPLETED','FAILED','CANCELLED')),
    beneficiary_posted_at INTEGER NULL,
    settlement_finalized_at INTEGER NULL,
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(source_deposit_account_id <> destination_deposit_account_id),
    CHECK(settlement_mode <> 'INTERNAL' OR payment_network_policy_version_id IS NULL),
    CHECK(settlement_mode <> 'CLEARING' OR payment_network_policy_version_id IS NOT NULL),
    CHECK(beneficiary_posting_policy <> 'GUARANTEED_PRE_CREDIT' OR settlement_mode = 'CLEARING'),
    CHECK(settlement_mode = 'INTERNAL' OR status NOT IN ('SETTLED','COMPLETED') OR settlement_finalized_at IS NOT NULL),
    CHECK(status <> 'COMPLETED' OR beneficiary_posted_at IS NOT NULL),
    CHECK(beneficiary_posted_at IS NULL OR status NOT IN ('FAILED','CANCELLED'))
) STRICT;

CREATE TABLE outbox_events(
    outbox_event_id BLOB NOT NULL PRIMARY KEY CHECK(length(outbox_event_id) = 16),
    business_operation_id BLOB NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    event_type TEXT NOT NULL CHECK(length(event_type) BETWEEN 1 AND 64),
    payload_json TEXT NOT NULL CHECK(length(CAST(payload_json AS BLOB)) <= 32768),
    status TEXT NOT NULL CHECK(status IN ('PENDING','CLAIMED','PUBLISHED','RETRY_DUE','DEAD_LETTER')),
    claim_token BLOB NULL CHECK(claim_token IS NULL OR length(claim_token) = 16),
    claimed_at INTEGER NULL,
    claim_expires_at INTEGER NULL,
    next_attempt_at INTEGER NULL,
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    attempt_count INTEGER NOT NULL CHECK(attempt_count BETWEEN 0 AND 5),
    last_error_code TEXT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'CLAIMED' AND claim_token IS NOT NULL AND claimed_at IS NOT NULL AND claim_expires_at IS NOT NULL AND next_attempt_at IS NULL)
       OR (status <> 'CLAIMED' AND claim_token IS NULL AND claimed_at IS NULL AND claim_expires_at IS NULL)),
    CHECK((status = 'RETRY_DUE' AND next_attempt_at IS NOT NULL) OR (status <> 'RETRY_DUE' AND next_attempt_at IS NULL)),
    CHECK((status = 'PUBLISHED' AND published_at IS NOT NULL) OR (status <> 'PUBLISHED' AND published_at IS NULL))
) STRICT;

CREATE INDEX ix_outbox_events_dispatchable ON outbox_events(status, created_at);

CREATE TABLE idempotency_records(
    idempotency_record_id BLOB NOT NULL PRIMARY KEY CHECK(length(idempotency_record_id) = 16),
    idempotency_scope TEXT NOT NULL CHECK(length(idempotency_scope) BETWEEN 1 AND 64),
    idempotency_key TEXT NOT NULL CHECK(length(idempotency_key) BETWEEN 1 AND 128),
    business_operation_id BLOB NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    operation_result_id BLOB NULL CHECK(operation_result_id IS NULL OR length(operation_result_id) = 16),
    created_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    UNIQUE(idempotency_scope, idempotency_key)
) STRICT;

CREATE TABLE interaction_sessions(
    interaction_session_id BLOB NOT NULL PRIMARY KEY CHECK(length(interaction_session_id) = 16),
    discord_user_id TEXT NOT NULL CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    session_kind TEXT NOT NULL CHECK(length(session_kind) BETWEEN 1 AND 64),
    token_hash BLOB NOT NULL UNIQUE CHECK(length(token_hash) = 32),
    payload_json TEXT NOT NULL CHECK(length(CAST(payload_json AS BLOB)) <= 8192),
    expected_version INTEGER NOT NULL CHECK(expected_version >= 0),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','COMPLETED','CANCELLED','EXPIRED')),
    created_at INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    completed_at INTEGER NULL,
    CHECK(expires_at > created_at),
    CHECK((status = 'ACTIVE' AND completed_at IS NULL) OR (status <> 'ACTIVE' AND completed_at IS NOT NULL))
) STRICT;

CREATE INDEX ix_interaction_sessions_expiry ON interaction_sessions(status, expires_at);

CREATE TABLE audit_records(
    audit_record_id BLOB NOT NULL PRIMARY KEY CHECK(length(audit_record_id) = 16),
    business_operation_id BLOB NULL REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    actor_discord_user_id TEXT NULL CHECK(actor_discord_user_id IS NULL OR length(actor_discord_user_id) BETWEEN 1 AND 20),
    actor_customer_account_id BLOB NULL REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    action TEXT NOT NULL CHECK(length(action) BETWEEN 1 AND 64),
    target_type TEXT NOT NULL CHECK(length(target_type) BETWEEN 1 AND 64),
    target_id BLOB NULL CHECK(target_id IS NULL OR length(target_id) = 16),
    reason TEXT NULL,
    occurred_at INTEGER NOT NULL
) STRICT;

CREATE INDEX ix_audit_records_occurred_at ON audit_records(occurred_at);
