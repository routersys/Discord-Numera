CREATE TABLE bank_card_design_template_versions(
    bank_card_design_version_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(bank_card_design_version_id) = 16),
    bank_id BLOB NOT NULL REFERENCES banks(bank_id) ON DELETE RESTRICT,
    face_mode TEXT NOT NULL CHECK(face_mode IN ('NUMBERED','NUMBERLESS')),
    status TEXT NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
    background_rgb INTEGER NOT NULL CHECK(background_rgb BETWEEN 0 AND 16777215),
    version INTEGER NOT NULL CHECK(version >= 1),
    created_at INTEGER NOT NULL,
    published_at INTEGER NULL,
    retired_at INTEGER NULL,
    CHECK((status = 'PUBLISHED' AND published_at IS NOT NULL)
        OR (status <> 'PUBLISHED' AND retired_at IS NULL)),
    CHECK(status <> 'RETIRED' OR (published_at IS NOT NULL AND retired_at IS NOT NULL))
) STRICT;

CREATE UNIQUE INDEX ux_bank_card_design_published
    ON bank_card_design_template_versions(bank_id) WHERE status = 'PUBLISHED';

CREATE INDEX ix_bank_card_design_bank
    ON bank_card_design_template_versions(bank_id, status);

CREATE TABLE bank_card_design_text_slots(
    bank_card_design_text_slot_id BLOB NOT NULL PRIMARY KEY
        CHECK(length(bank_card_design_text_slot_id) = 16),
    bank_card_design_version_id BLOB NOT NULL
        REFERENCES bank_card_design_template_versions(bank_card_design_version_id) ON DELETE RESTRICT,
    slot_index INTEGER NOT NULL CHECK(slot_index BETWEEN 0 AND 7),
    token TEXT NOT NULL CHECK(token IN ('{bank.name}','{bank.code}','{customer.display_name}',
        '{card.number}','{card.last4}','{card.expiry}','{currency.name}','{currency.code}',
        '{account.masked_number}')),
    x INTEGER NOT NULL CHECK(x >= 0),
    y INTEGER NOT NULL CHECK(y >= 0),
    width INTEGER NOT NULL CHECK(width > 0),
    height INTEGER NOT NULL CHECK(height > 0),
    font_size_px INTEGER NOT NULL CHECK(font_size_px BETWEEN 16 AND 72),
    minimum_font_size_px INTEGER NOT NULL CHECK(minimum_font_size_px BETWEEN 16 AND 72),
    font_weight TEXT NOT NULL CHECK(font_weight IN ('SEMIBOLD','BOLD')),
    horizontal_alignment TEXT NOT NULL CHECK(horizontal_alignment IN ('LEFT','CENTER','RIGHT')),
    large_text INTEGER NOT NULL CHECK(large_text IN (0, 1)),
    fixed_text_rgb INTEGER NULL CHECK(fixed_text_rgb IS NULL
        OR fixed_text_rgb BETWEEN 0 AND 16777215),
    UNIQUE(bank_card_design_version_id, slot_index),
    CHECK(minimum_font_size_px <= font_size_px),
    CHECK(x + width <= 1026),
    CHECK(y + height <= 647)
) STRICT;

ALTER TABLE bank_cards ADD COLUMN bank_card_design_version_id BLOB NULL
    REFERENCES bank_card_design_template_versions(bank_card_design_version_id) ON DELETE RESTRICT;
