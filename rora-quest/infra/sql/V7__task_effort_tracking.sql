-- Adds effort-tracking columns to task_items.
-- All columns are nullable so existing rows remain valid without backfill.

ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS estimated_hours NUMERIC(6,2) NULL,
    ADD COLUMN IF NOT EXISTS actual_hours    NUMERIC(6,2) NULL,
    ADD COLUMN IF NOT EXISTS story_points    INTEGER      NULL;

-- Non-negative guards
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_task_items_estimated_hours') THEN
        ALTER TABLE task_items ADD CONSTRAINT ck_task_items_estimated_hours CHECK (estimated_hours IS NULL OR estimated_hours >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_task_items_actual_hours') THEN
        ALTER TABLE task_items ADD CONSTRAINT ck_task_items_actual_hours CHECK (actual_hours IS NULL OR actual_hours >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_task_items_story_points') THEN
        ALTER TABLE task_items ADD CONSTRAINT ck_task_items_story_points CHECK (story_points IS NULL OR story_points >= 0);
    END IF;
END $$;

INSERT INTO schema_migrations (version, description)
VALUES ('V7', 'Add effort tracking columns (estimated_hours, actual_hours, story_points) to task_items')
ON CONFLICT (version) DO NOTHING;
