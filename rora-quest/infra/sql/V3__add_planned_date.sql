-- Adds a dedicated per-day planning anchor (planned_date) to task_items.
-- Distinct from due_date (deadline): planned_date is the day the task is scheduled to be worked on.
-- Used by the Notion-style weekly task view to bucket tasks into day columns.

ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS planned_date DATE NULL;

CREATE INDEX IF NOT EXISTS ix_task_items_user_planned_date
    ON task_items (user_id, planned_date);

INSERT INTO schema_migrations (version, description)
VALUES ('V3', 'Add planned_date to task_items for day-level scheduling')
ON CONFLICT (version) DO NOTHING;
