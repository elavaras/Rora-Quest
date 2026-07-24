-- Structured checklist intake: Month/Week hierarchy, Pattern-confidence self-assessment,
-- and a per-week confidence checklist surfaced on the weekly task view.

-- 1) Month label on draft items (visual-only grouping context for the preview).
ALTER TABLE checklist_draft_items
    ADD COLUMN IF NOT EXISTS month_label TEXT NULL;

-- 2) Parsed "Pattern confidence:" lines held alongside an import until commit.
CREATE TABLE IF NOT EXISTS checklist_confidence_items (
    id UUID PRIMARY KEY,
    checklist_import_id UUID NOT NULL REFERENCES checklist_imports(id) ON DELETE CASCADE,
    order_index INTEGER NOT NULL,
    text TEXT NOT NULL,
    week_number INTEGER NULL,
    sub_category_name TEXT NULL,
    month_label TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_checklist_confidence_items_order_index CHECK (order_index >= 1),
    CONSTRAINT ck_checklist_confidence_items_week_number CHECK (week_number IS NULL OR week_number >= 1)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_checklist_confidence_items_import_order
    ON checklist_confidence_items (checklist_import_id, order_index);

-- 3) Committed per-week confidence checklist items (checkable on the weekly view).
CREATE TABLE IF NOT EXISTS week_confidence_items (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    week_start DATE NOT NULL,
    label TEXT NOT NULL DEFAULT '',
    text TEXT NOT NULL,
    is_done BOOLEAN NOT NULL DEFAULT FALSE,
    order_index INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_week_confidence_items_user_week
    ON week_confidence_items (user_id, week_start);

INSERT INTO schema_migrations (version, description)
VALUES ('V5', 'Add month labels, checklist confidence drafts, and per-week confidence items')
ON CONFLICT (version) DO NOTHING;
