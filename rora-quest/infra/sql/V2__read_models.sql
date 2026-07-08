CREATE TABLE IF NOT EXISTS weekly_scorecard_projections (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    week_start_date DATE NOT NULL,
    planned_tasks INTEGER NOT NULL DEFAULT 0,
    completed_tasks INTEGER NOT NULL DEFAULT 0,
    carry_over_moved INTEGER NOT NULL DEFAULT 0,
    carry_over_pending INTEGER NOT NULL DEFAULT 0,
    completion_rate_percent NUMERIC(5, 2) NOT NULL DEFAULT 0,
    avg_progress_percent NUMERIC(5, 2) NOT NULL DEFAULT 0,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, week_start_date),
    CONSTRAINT ck_weekly_scorecard_planned_tasks CHECK (planned_tasks >= 0),
    CONSTRAINT ck_weekly_scorecard_completed_tasks CHECK (completed_tasks >= 0),
    CONSTRAINT ck_weekly_scorecard_carry_over_moved CHECK (carry_over_moved >= 0),
    CONSTRAINT ck_weekly_scorecard_carry_over_pending CHECK (carry_over_pending >= 0)
);

CREATE TABLE IF NOT EXISTS daily_consistency_projections (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    activity_date DATE NOT NULL,
    completed_task_count INTEGER NOT NULL DEFAULT 0,
    progress_percent NUMERIC(5, 2) NOT NULL DEFAULT 0,
    streak_eligible BOOLEAN NOT NULL DEFAULT FALSE,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, activity_date),
    CONSTRAINT ck_daily_consistency_completed_task_count CHECK (completed_task_count >= 0)
);

CREATE TABLE IF NOT EXISTS recommendation_snapshots (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    snapshot_date DATE NOT NULL,
    profile TEXT NOT NULL,
    suggested_mode TEXT NOT NULL,
    completion_rate_percent NUMERIC(5, 2) NOT NULL DEFAULT 0,
    computed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, snapshot_date),
    CONSTRAINT ck_recommendation_snapshots_mode CHECK (suggested_mode IN ('Green', 'Yellow', 'Red'))
);

INSERT INTO schema_migrations (version, description)
VALUES ('V2', 'Reporting and recommendation read-model tables')
ON CONFLICT (version) DO NOTHING;
