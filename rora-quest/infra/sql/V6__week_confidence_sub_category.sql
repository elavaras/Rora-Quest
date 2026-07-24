-- Link per-week confidence items to their sub-category via a real foreign key so the
-- association survives sub-category renames. The existing `label` column is retained as a
-- denormalized fallback (used when the FK is null or the category was deleted).
ALTER TABLE week_confidence_items
    ADD COLUMN IF NOT EXISTS sub_category_id UUID NULL REFERENCES categories(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_week_confidence_items_sub_category
    ON week_confidence_items (sub_category_id);

INSERT INTO schema_migrations (version, description)
VALUES ('V6', 'Link week confidence items to sub-category via FK (name retained as fallback)')
ON CONFLICT (version) DO NOTHING;
