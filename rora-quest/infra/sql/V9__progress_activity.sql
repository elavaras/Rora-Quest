-- Retained evidence: deliberately no foreign keys to the replaceable task aggregate.
CREATE TABLE IF NOT EXISTS progress_owners (
    owner_id text PRIMARY KEY,
    tracking_started_at timestamptz NOT NULL,
    aggregate_revision bigint NOT NULL DEFAULT 0 CHECK (aggregate_revision >= 0)
);
CREATE TABLE IF NOT EXISTS progress_completions (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    owner_id text NOT NULL REFERENCES progress_owners(owner_id) ON DELETE RESTRICT,
    unit_key text COLLATE "C" NOT NULL,
    kind text NOT NULL CHECK (kind IN ('task', 'substep')),
    occurred_at timestamptz NOT NULL,
    task_id uuid NOT NULL,
    substep_id uuid,
    task_title text NOT NULL,
    substep_title text,
    UNIQUE(owner_id, unit_key),
    CHECK ((kind = 'task' AND substep_id IS NULL AND substep_title IS NULL)
        OR (kind = 'substep' AND substep_id IS NOT NULL AND substep_title IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS ix_progress_owner_time ON progress_completions(owner_id, occurred_at, unit_key);
CREATE INDEX IF NOT EXISTS ix_progress_owner_sequence ON progress_completions(owner_id, sequence);
CREATE TABLE IF NOT EXISTS progress_mutation_receipts (
    commit_id uuid PRIMARY KEY,
    owner_id text NOT NULL REFERENCES progress_owners(owner_id) ON DELETE RESTRICT,
    committed_at timestamptz NOT NULL,
    aggregate_revision bigint NOT NULL
);
CREATE TABLE IF NOT EXISTS progress_capture_sessions (
    session_id uuid PRIMARY KEY,
    started_at timestamptz NOT NULL,
    verified_through timestamptz NOT NULL,
    stopped_at timestamptz,
    CHECK (verified_through >= started_at),
    CHECK (stopped_at IS NULL OR stopped_at >= verified_through)
);
CREATE TABLE IF NOT EXISTS progress_capture_interruptions (
    id uuid PRIMARY KEY,
    start_at timestamptz NOT NULL,
    end_at timestamptz,
    reason text NOT NULL CHECK (reason IN ('maintenance', 'unrecognizedWriter', 'recovery')),
    CHECK (end_at IS NULL OR end_at >= start_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_progress_one_open_interruption
    ON progress_capture_interruptions ((1)) WHERE end_at IS NULL;

CREATE OR REPLACE FUNCTION progress_guard_legacy_writer() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE earliest timestamptz;
BEGIN
    -- This constant is shared with ProgressLedger.ControlLock. Control never takes owner locks.
    PERFORM pg_advisory_xact_lock(726578430019::bigint);
    IF current_setting('roraquest.progress_capture_version', true) = '1' THEN RETURN NULL; END IF;
    SELECT min(started_at) INTO earliest FROM progress_capture_sessions;
    IF earliest IS NULL THEN RETURN NULL; END IF;
    UPDATE progress_capture_interruptions
       SET start_at = LEAST(start_at, earliest), reason = 'unrecognizedWriter'
     WHERE end_at IS NULL;
    IF NOT FOUND THEN
        INSERT INTO progress_capture_interruptions(id, start_at, reason)
        VALUES (gen_random_uuid(), earliest, 'unrecognizedWriter');
    END IF;
    RETURN NULL;
END $$;
CREATE OR REPLACE TRIGGER progress_task_writer BEFORE INSERT OR UPDATE OR DELETE ON task_items
    FOR EACH STATEMENT EXECUTE FUNCTION progress_guard_legacy_writer();
CREATE OR REPLACE TRIGGER progress_substep_writer BEFORE INSERT OR UPDATE OR DELETE ON task_sub_steps
    FOR EACH STATEMENT EXECUTE FUNCTION progress_guard_legacy_writer();
INSERT INTO schema_migrations(version, description) VALUES ('V9', 'Retained progress activity and capture certification')
    ON CONFLICT(version) DO NOTHING;
