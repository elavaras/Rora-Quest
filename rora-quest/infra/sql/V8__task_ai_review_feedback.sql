-- Adds optional AI review/feedback notes for system-design/problem-solving tasks.
-- Nullable for backward compatibility with existing rows and clients.

ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS ai_review_feedback TEXT NULL;

INSERT INTO schema_migrations (version, description)
VALUES ('V8', 'Add ai_review_feedback column to task_items')
ON CONFLICT (version) DO NOTHING;
