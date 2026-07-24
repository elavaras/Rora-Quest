-- Adds weighted progress to sub-steps and problem metadata (pattern, difficulty) to tasks.
-- Every task tracks 9 standard preparation sub-steps whose weights sum to 100; task progress
-- is computed as (sum of completed sub-step weights) / (sum of all weights) * 100.

-- 1) Sub-step weight column (points contributed to task progress).
ALTER TABLE task_sub_steps
    ADD COLUMN IF NOT EXISTS weight INTEGER NOT NULL DEFAULT 0;

-- 2) Task problem metadata.
--    'pattern' is free text (e.g. Sliding Window). Title continues to serve as the Problem Name.
ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS pattern TEXT NULL;

ALTER TABLE task_items
    ADD COLUMN IF NOT EXISTS difficulty TEXT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_task_items_difficulty'
    ) THEN
        ALTER TABLE task_items
            ADD CONSTRAINT ck_task_items_difficulty
            CHECK (difficulty IS NULL OR difficulty IN ('Easy', 'Medium', 'Hard'));
    END IF;
END $$;

-- 3) Best-effort: assign standard weights to any pre-existing sub-steps that match the
--    standard titles but were created before weights existed (weight still 0).
UPDATE task_sub_steps s
SET weight = tpl.weight
FROM (VALUES
    ('Understand Problem', 5),
    ('Corner Cases', 10),
    ('Brute Force', 10),
    ('Optimized Solution', 25),
    ('Time Complexity', 10),
    ('Space Complexity', 10),
    ('Coding', 20),
    ('Testing', 5),
    ('Revision', 5)
) AS tpl(title, weight)
WHERE s.title = tpl.title AND s.weight = 0;

-- 4) Backfill: seed the 9 standard weighted sub-steps for every task that currently has none.
--    Uses core gen_random_uuid() (PostgreSQL 13+), which does NOT require the pgcrypto extension.
INSERT INTO task_sub_steps (id, task_item_id, title, is_done, order_index, completed_at, row_version, weight)
SELECT gen_random_uuid(), t.id, tpl.title, FALSE, tpl.ord, NULL, 1, tpl.weight
FROM task_items t
JOIN (VALUES
    ('Understand Problem', 1, 5),
    ('Corner Cases', 2, 10),
    ('Brute Force', 3, 10),
    ('Optimized Solution', 4, 25),
    ('Time Complexity', 5, 10),
    ('Space Complexity', 6, 10),
    ('Coding', 7, 20),
    ('Testing', 8, 5),
    ('Revision', 9, 5)
) AS tpl(title, ord, weight) ON TRUE
WHERE NOT EXISTS (
    SELECT 1 FROM task_sub_steps s WHERE s.task_item_id = t.id
);

INSERT INTO schema_migrations (version, description)
VALUES ('V4', 'Add sub-step weights and task pattern/difficulty; backfill standard weighted sub-steps')
ON CONFLICT (version) DO NOTHING;
