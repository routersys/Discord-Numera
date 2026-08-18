CREATE TABLE authorization_decisions(
    authorization_decision_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(authorization_decision_id) = 16),
    target_type TEXT NOT NULL CHECK(target_type IN ('FX_MARKET_ACTIVATION','ATM_PLACEMENT',
        'CURRENCY_TRUST_DESIGNATION','FX_INTERVENTION_MANDATE')),
    target_id BLOB NOT NULL CHECK(length(target_id) = 16),
    scope_guild_id TEXT NULL CHECK(scope_guild_id IS NULL
        OR length(scope_guild_id) BETWEEN 1 AND 20),
    authority_kind TEXT NOT NULL CHECK(authority_kind IN ('GUILD_OPERATOR','BANK_OPERATOR',
        'MERCHANT_OPERATOR','SYSTEM_OWNER')),
    actor_discord_user_id TEXT NOT NULL CHECK(length(actor_discord_user_id) BETWEEN 1 AND 20),
    actor_customer_account_id BLOB NULL
        REFERENCES customer_accounts(customer_account_id) ON DELETE RESTRICT,
    decision_kind TEXT NOT NULL CHECK(decision_kind IN ('APPROVE','OVERRIDE','REVOKE')),
    reason_code TEXT NULL CHECK(reason_code IS NULL OR length(reason_code) BETWEEN 1 AND 64),
    occurred_at INTEGER NOT NULL,
    supersedes_decision_id BLOB NULL
        REFERENCES authorization_decisions(authorization_decision_id) ON DELETE RESTRICT
) STRICT;

CREATE INDEX ix_authorization_decisions_target
    ON authorization_decisions(
        target_type, target_id, scope_guild_id, authority_kind,
        occurred_at DESC, authorization_decision_id DESC);

CREATE INDEX ix_authorization_decisions_actor
    ON authorization_decisions(actor_discord_user_id, occurred_at DESC);

CREATE TABLE bank_assets(
    bank_asset_id BLOB NOT NULL PRIMARY KEY CHECK(length(bank_asset_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    asset_kind TEXT NOT NULL CHECK(asset_kind IN ('PUBLIC_LOGO','PUBLIC_BANNER','ATM_BANNER',
        'CARD_BACKGROUND')),
    sha256 BLOB NOT NULL CHECK(length(sha256) = 32),
    mime_type TEXT NOT NULL CHECK(mime_type = 'image/png'),
    width INTEGER NOT NULL CHECK(width BETWEEN 1 AND 4096),
    height INTEGER NOT NULL CHECK(height BETWEEN 1 AND 4096),
    byte_length INTEGER NOT NULL CHECK(byte_length BETWEEN 1 AND 8388608),
    content BLOB NOT NULL,
    created_at INTEGER NOT NULL
) STRICT;

CREATE INDEX ix_bank_assets_bank_kind ON bank_assets(bank_id, asset_kind, created_at);

CREATE TABLE resolution_transfers(
    resolution_transfer_id BLOB NOT NULL PRIMARY KEY CHECK(length(resolution_transfer_id) = 16),
    resolution_case_id BLOB NOT NULL
        REFERENCES resolution_cases(resolution_case_id) ON DELETE RESTRICT,
    source_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    successor_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    successor_deposit_account_id BLOB NOT NULL
        REFERENCES deposit_accounts(deposit_account_id) ON DELETE RESTRICT,
    transferred_claim_minor INTEGER NOT NULL CHECK(transferred_claim_minor >= 0),
    business_operation_id BLOB NOT NULL UNIQUE
        REFERENCES business_operations(business_operation_id) ON DELETE RESTRICT,
    transferred_at INTEGER NOT NULL,
    version INTEGER NOT NULL CHECK(version >= 1),
    UNIQUE(resolution_case_id, source_deposit_account_id),
    CHECK(source_deposit_account_id <> successor_deposit_account_id)
) STRICT;

PRAGMA defer_foreign_keys = ON;

CREATE TABLE atm_placement_agreements_rebuilt(
    atm_placement_agreement_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(atm_placement_agreement_id) = 16),
    atm_terminal_id BLOB NOT NULL REFERENCES atm_terminals(atm_terminal_id) ON DELETE RESTRICT,
    placement_guild_id TEXT NOT NULL CHECK(length(placement_guild_id) BETWEEN 1 AND 20),
    operator_bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    host_approval_decision_id BLOB NULL
        REFERENCES authorization_decisions(authorization_decision_id) ON DELETE RESTRICT,
    operator_approval_decision_id BLOB NULL
        REFERENCES authorization_decisions(authorization_decision_id) ON DELETE RESTRICT,
    override_decision_id BLOB NULL
        REFERENCES authorization_decisions(authorization_decision_id) ON DELETE RESTRICT,
    effective_from INTEGER NOT NULL,
    effective_to INTEGER NULL,
    placement_fee_schedule_version_id BLOB NULL
        REFERENCES fee_schedule_versions(fee_schedule_version_id) ON DELETE RESTRICT,
    revenue_share_bps INTEGER NOT NULL CHECK(revenue_share_bps BETWEEN 0 AND 10000),
    status TEXT NOT NULL CHECK(status IN ('PENDING','ACTIVE','SUSPENDED','ENDED')),
    version INTEGER NOT NULL CHECK(version >= 1)
) STRICT;

INSERT INTO atm_placement_agreements_rebuilt(
    atm_placement_agreement_id,
    atm_terminal_id,
    placement_guild_id,
    operator_bank_id,
    host_approval_decision_id,
    operator_approval_decision_id,
    override_decision_id,
    effective_from,
    effective_to,
    placement_fee_schedule_version_id,
    revenue_share_bps,
    status,
    version)
SELECT
    atm_placement_agreement_id,
    atm_terminal_id,
    placement_guild_id,
    operator_bank_id,
    host_approval_decision_id,
    operator_approval_decision_id,
    override_decision_id,
    effective_from,
    effective_to,
    placement_fee_schedule_version_id,
    revenue_share_bps,
    status,
    version
FROM atm_placement_agreements;

DROP TABLE atm_placement_agreements;

ALTER TABLE atm_placement_agreements_rebuilt RENAME TO atm_placement_agreements;

CREATE INDEX ix_atm_placement_agreements_guild
    ON atm_placement_agreements(
        placement_guild_id, status, effective_from, atm_placement_agreement_id);
