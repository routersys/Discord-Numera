CREATE TABLE presentation_profile_versions(
    presentation_profile_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(presentation_profile_version_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    information_rgb INTEGER NULL CHECK(information_rgb IS NULL OR information_rgb BETWEEN 0 AND 16777215),
    success_rgb INTEGER NULL CHECK(success_rgb IS NULL OR success_rgb BETWEEN 0 AND 16777215),
    warning_rgb INTEGER NULL CHECK(warning_rgb IS NULL OR warning_rgb BETWEEN 0 AND 16777215),
    error_rgb INTEGER NULL CHECK(error_rgb IS NULL OR error_rgb BETWEEN 0 AND 16777215),
    neutral_rgb INTEGER NULL CHECK(neutral_rgb IS NULL OR neutral_rgb BETWEEN 0 AND 16777215),
    field_layout TEXT NULL CHECK(field_layout IS NULL
        OR field_layout IN ('CANONICAL','INLINE_WHEN_SHORT','BLOCK')),
    density TEXT NULL CHECK(density IS NULL OR density IN ('COMFORTABLE','COMPACT')),
    operation_footer_mode TEXT NULL CHECK(operation_footer_mode IS NULL
        OR operation_footer_mode IN ('SHOW','HIDE')),
    author_mode TEXT NULL CHECK(author_mode IS NULL OR author_mode IN ('HIDE','PUBLIC_BANK_NAME')),
    thumbnail_mode TEXT NULL CHECK(thumbnail_mode IS NULL
        OR thumbnail_mode IN ('HIDE','PUBLIC_BANK_LOGO')),
    image_mode TEXT NULL CHECK(image_mode IS NULL
        OR image_mode IN ('CANONICAL_ONLY','ALLOW_PUBLIC_BANK_MEDIA')),
    public_logo_asset_id BLOB NULL,
    public_banner_asset_id BLOB NULL,
    fx_background_rgb INTEGER NULL CHECK(fx_background_rgb IS NULL
        OR fx_background_rgb BETWEEN 0 AND 16777215),
    fx_panel_rgb INTEGER NULL CHECK(fx_panel_rgb IS NULL OR fx_panel_rgb BETWEEN 0 AND 16777215),
    fx_primary_text_rgb INTEGER NULL CHECK(fx_primary_text_rgb IS NULL
        OR fx_primary_text_rgb BETWEEN 0 AND 16777215),
    fx_secondary_text_rgb INTEGER NULL CHECK(fx_secondary_text_rgb IS NULL
        OR fx_secondary_text_rgb BETWEEN 0 AND 16777215),
    fx_grid_rgb INTEGER NULL CHECK(fx_grid_rgb IS NULL OR fx_grid_rgb BETWEEN 0 AND 16777215),
    fx_positive_rgb INTEGER NULL CHECK(fx_positive_rgb IS NULL
        OR fx_positive_rgb BETWEEN 0 AND 16777215),
    fx_negative_rgb INTEGER NULL CHECK(fx_negative_rgb IS NULL
        OR fx_negative_rgb BETWEEN 0 AND 16777215),
    fx_neutral_accent_rgb INTEGER NULL CHECK(fx_neutral_accent_rgb IS NULL
        OR fx_neutral_accent_rgb BETWEEN 0 AND 16777215),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(bank_id IS NOT NULL OR (author_mode IS NULL OR author_mode = 'HIDE')),
    CHECK(bank_id IS NOT NULL OR (thumbnail_mode IS NULL OR thumbnail_mode = 'HIDE')),
    CHECK(bank_id IS NOT NULL OR (image_mode IS NULL OR image_mode = 'CANONICAL_ONLY')),
    CHECK(bank_id IS NOT NULL OR public_logo_asset_id IS NULL)
) STRICT;

CREATE UNIQUE INDEX ux_presentation_profile_published_guild
    ON presentation_profile_versions(economy_scope_id)
    WHERE status = 'PUBLISHED' AND bank_id IS NULL;

CREATE UNIQUE INDEX ux_presentation_profile_published_bank
    ON presentation_profile_versions(bank_id)
    WHERE status = 'PUBLISHED' AND bank_id IS NOT NULL;

CREATE TABLE currency_trust_policy_versions(
    currency_trust_policy_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(currency_trust_policy_version_id) = 16),
    economy_scope_id BLOB NOT NULL REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    established_min_age_seconds INTEGER NOT NULL CHECK(established_min_age_seconds >= 604800),
    established_min_trade_days INTEGER NOT NULL CHECK(established_min_trade_days >= 3),
    established_min_counterparties INTEGER NOT NULL CHECK(established_min_counterparties >= 2),
    trusted_min_age_seconds INTEGER NOT NULL CHECK(trusted_min_age_seconds >= 2592000),
    trusted_min_trade_days INTEGER NOT NULL CHECK(trusted_min_trade_days >= 10),
    trusted_min_counterparties INTEGER NOT NULL CHECK(trusted_min_counterparties >= 3),
    reserve_min_age_seconds INTEGER NOT NULL CHECK(reserve_min_age_seconds >= 7776000),
    reserve_min_trade_days INTEGER NOT NULL CHECK(reserve_min_trade_days >= 30),
    reserve_min_counterparties INTEGER NOT NULL CHECK(reserve_min_counterparties >= 5),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(economy_scope_id, version)
) STRICT;

CREATE UNIQUE INDEX ux_currency_trust_policy_current
    ON currency_trust_policy_versions(economy_scope_id) WHERE status = 'PUBLISHED';

CREATE TABLE currency_trust_designations(
    currency_trust_designation_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(currency_trust_designation_id) = 16),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    currency_trust_policy_version_id BLOB NOT NULL
        REFERENCES currency_trust_policy_versions(currency_trust_policy_version_id) ON DELETE RESTRICT,
    trust_tier TEXT NOT NULL CHECK(trust_tier IN (
        'EXPERIMENTAL','ESTABLISHED','TRUSTED','RESERVE_ELIGIBLE')),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','SUPERSEDED')),
    authorization_decision_id BLOB NULL,
    qualified_age_seconds INTEGER NOT NULL CHECK(qualified_age_seconds >= 0),
    qualified_trade_days INTEGER NOT NULL CHECK(qualified_trade_days >= 0),
    qualified_counterparties INTEGER NOT NULL CHECK(qualified_counterparties >= 0),
    effective_from INTEGER NOT NULL,
    terminal_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(trust_tier = 'EXPERIMENTAL' OR authorization_decision_id IS NOT NULL)
) STRICT;

CREATE UNIQUE INDEX ux_currency_trust_current
    ON currency_trust_designations(currency_id) WHERE status IN ('ACTIVE','SUSPENDED');

CREATE TABLE monetary_authorities(
    monetary_authority_id BLOB NOT NULL PRIMARY KEY CHECK(length(monetary_authority_id) = 16),
    economy_scope_id BLOB NOT NULL UNIQUE
        REFERENCES guild_economies(economy_scope_id) ON DELETE RESTRICT,
    party_id BLOB NOT NULL UNIQUE REFERENCES parties(party_id) ON DELETE RESTRICT,
    accounting_book_id BLOB NOT NULL UNIQUE
        REFERENCES accounting_books(accounting_book_id) ON DELETE RESTRICT,
    home_currency_id BLOB NOT NULL UNIQUE REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    home_fx_funding_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','RETIRED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE official_reserve_portfolios(
    official_reserve_portfolio_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(official_reserve_portfolio_id) = 16),
    monetary_authority_id BLOB NOT NULL UNIQUE
        REFERENCES monetary_authorities(monetary_authority_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE TABLE official_reserve_positions(
    official_reserve_position_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(official_reserve_position_id) = 16),
    official_reserve_portfolio_id BLOB NOT NULL
        REFERENCES official_reserve_portfolios(official_reserve_portfolio_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    asset_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    custodian_monetary_authority_id BLOB NOT NULL
        REFERENCES monetary_authorities(monetary_authority_id) ON DELETE RESTRICT,
    custodian_liability_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','RESTRICTED','CLOSED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(official_reserve_portfolio_id, currency_id)
) STRICT;

CREATE TABLE fx_intervention_mandates(
    fx_intervention_mandate_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(fx_intervention_mandate_id) = 16),
    monetary_authority_id BLOB NOT NULL
        REFERENCES monetary_authorities(monetary_authority_id) ON DELETE RESTRICT,
    market_id BLOB NOT NULL REFERENCES fx_markets(market_id) ON DELETE RESTRICT,
    allowed_side TEXT NOT NULL CHECK(allowed_side IN ('BUY_BASE','SELL_BASE','BOTH')),
    maximum_source_minor_per_order INTEGER NOT NULL CHECK(maximum_source_minor_per_order > 0),
    maximum_source_minor_total INTEGER NOT NULL CHECK(maximum_source_minor_total > 0),
    used_source_minor INTEGER NOT NULL CHECK(used_source_minor >= 0),
    maximum_slippage_bps INTEGER NOT NULL CHECK(maximum_slippage_bps BETWEEN 0 AND 10000),
    valid_from INTEGER NOT NULL,
    valid_until INTEGER NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('DRAFT','ACTIVE','SUSPENDED','EXPIRED','CANCELLED')),
    authorization_decision_id BLOB NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(valid_until > valid_from),
    CHECK(used_source_minor <= maximum_source_minor_total)
) STRICT;

CREATE TABLE resolution_cases(
    resolution_case_id BLOB NOT NULL PRIMARY KEY CHECK(length(resolution_case_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    status TEXT NOT NULL CHECK(status IN (
        'OPEN','RESTRICTED','TRANSFER_IN_PROGRESS','RESOLVED','LIQUIDATED')),
    opened_at INTEGER NOT NULL,
    insurance_cutoff_at INTEGER NOT NULL,
    selected_successor_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    bridge_bank_id BLOB NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    resolved_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(insurance_cutoff_at = opened_at)
) STRICT;

CREATE INDEX ix_resolution_cases_bank ON resolution_cases(bank_id, status);

CREATE TABLE loan_contracts(
    loan_contract_id BLOB NOT NULL PRIMARY KEY CHECK(length(loan_contract_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    customer_account_id BLOB NOT NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    loan_asset_ledger_account_id BLOB NOT NULL UNIQUE
        REFERENCES ledger_accounts(ledger_account_id) ON DELETE RESTRICT,
    disbursement_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    principal_original_minor INTEGER NOT NULL CHECK(principal_original_minor > 0),
    principal_outstanding_minor INTEGER NOT NULL CHECK(principal_outstanding_minor >= 0),
    annual_rate_ppt INTEGER NOT NULL,
    status TEXT NOT NULL CHECK(status IN (
        'APPROVED','ACTIVE','DELINQUENT','DEFAULTED','PAID','WRITTEN_OFF','CANCELLED')),
    originated_at INTEGER NOT NULL,
    maturity_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK(principal_outstanding_minor <= principal_original_minor)
) STRICT;

CREATE INDEX ix_loan_contracts_customer ON loan_contracts(customer_account_id, status);

CREATE TABLE loan_schedules(
    loan_schedule_id BLOB NOT NULL PRIMARY KEY CHECK(length(loan_schedule_id) = 16),
    loan_contract_id BLOB NOT NULL REFERENCES loan_contracts(loan_contract_id) ON DELETE RESTRICT,
    installment_no INTEGER NOT NULL CHECK(installment_no > 0),
    due_at INTEGER NOT NULL,
    principal_due_minor INTEGER NOT NULL CHECK(principal_due_minor >= 0),
    interest_due_minor INTEGER NOT NULL CHECK(interest_due_minor >= 0),
    paid_principal_minor INTEGER NOT NULL CHECK(paid_principal_minor >= 0),
    paid_interest_minor INTEGER NOT NULL CHECK(paid_interest_minor >= 0),
    status TEXT NOT NULL CHECK(status IN (
        'SCHEDULED','DUE','PARTIALLY_PAID','PAID','OVERDUE','WAIVED')),
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(loan_contract_id, installment_no)
) STRICT;

CREATE TABLE merchant_profiles(
    merchant_profile_id BLOB NOT NULL PRIMARY KEY CHECK(length(merchant_profile_id) = 16),
    party_id BLOB NOT NULL REFERENCES parties(party_id) ON DELETE RESTRICT,
    home_guild_id TEXT NOT NULL CHECK(length(home_guild_id) BETWEEN 1 AND 20),
    currency_id BLOB NOT NULL REFERENCES currencies(currency_id) ON DELETE RESTRICT,
    settlement_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    display_name TEXT NOT NULL CHECK(length(display_name) BETWEEN 1 AND 64),
    catalog_visibility_scope TEXT NOT NULL CHECK(catalog_visibility_scope IN ('LOCAL_GUILD','GLOBAL')),
    payment_scope TEXT NOT NULL CHECK(payment_scope IN ('LOCAL_GUILD','GLOBAL')),
    cross_currency_mode TEXT NOT NULL CHECK(cross_currency_mode IN ('DISABLED','FX_FOK')),
    maximum_checkout_slippage_bps INTEGER NOT NULL
        CHECK(maximum_checkout_slippage_bps BETWEEN 0 AND 10000),
    current_aftercare_policy_version_id BLOB NULL,
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','SUSPENDED','CLOSING','CLOSED')),
    created_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

CREATE INDEX ix_merchant_profiles_guild ON merchant_profiles(home_guild_id, status);

CREATE TABLE merchant_operator_grants(
    merchant_operator_grant_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(merchant_operator_grant_id) = 16),
    merchant_profile_id BLOB NOT NULL
        REFERENCES merchant_profiles(merchant_profile_id) ON DELETE RESTRICT,
    discord_user_id TEXT NOT NULL CHECK(length(discord_user_id) BETWEEN 1 AND 20),
    manage_catalog INTEGER NOT NULL CHECK(manage_catalog IN (0, 1)),
    manage_payment_policy INTEGER NOT NULL CHECK(manage_payment_policy IN (0, 1)),
    manage_refunds INTEGER NOT NULL CHECK(manage_refunds IN (0, 1)),
    manage_returns INTEGER NOT NULL CHECK(manage_returns IN (0, 1)),
    manage_settlement_account INTEGER NOT NULL CHECK(manage_settlement_account IN (0, 1)),
    status TEXT NOT NULL CHECK(status IN ('ACTIVE','REVOKED')),
    granted_by_discord_user_id TEXT NOT NULL CHECK(length(granted_by_discord_user_id) BETWEEN 1 AND 20),
    granted_at INTEGER NOT NULL,
    revoked_at INTEGER NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    CHECK((status = 'ACTIVE' AND revoked_at IS NULL)
        OR (status = 'REVOKED' AND revoked_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_merchant_operator_grants_active
    ON merchant_operator_grants(merchant_profile_id, discord_user_id) WHERE status = 'ACTIVE';
