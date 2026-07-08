CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    display_name TEXT NULL,
    primary_email TEXT NULL,
    timezone TEXT NOT NULL DEFAULT 'Asia/Kolkata',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS categories (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    parent_category_id UUID NULL REFERENCES categories(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_user_parent_name
    ON categories (user_id, COALESCE(parent_category_id, '00000000-0000-0000-0000-000000000000'::uuid), LOWER(name));

CREATE TABLE IF NOT EXISTS task_items (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    description TEXT NULL,
    category_id UUID NULL REFERENCES categories(id) ON DELETE SET NULL,
    sub_category_id UUID NULL REFERENCES categories(id) ON DELETE SET NULL,
    planned_week_start DATE NOT NULL,
    assigned_to TEXT NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    priority TEXT NULL,
    status TEXT NOT NULL,
    due_date DATE NULL,
    start_at TIMESTAMPTZ NULL,
    end_at TIMESTAMPTZ NULL,
    calendar_event_id TEXT NULL,
    reminder_at TIMESTAMPTZ NULL,
    question_and_reasoning TEXT NULL,
    logic_notes TEXT NULL,
    algorithm_notes TEXT NULL,
    diagram_content TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    row_version INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT ck_task_items_status CHECK (status IN ('Todo', 'InProgress', 'Done', 'Cancelled', 'Skipped')),
    CONSTRAINT ck_task_items_row_version CHECK (row_version >= 1),
    CONSTRAINT ck_task_items_end_after_start CHECK (end_at IS NULL OR start_at IS NULL OR end_at > start_at)
);

CREATE INDEX IF NOT EXISTS ix_task_items_user_week
    ON task_items (user_id, planned_week_start);

CREATE INDEX IF NOT EXISTS ix_task_items_user_status
    ON task_items (user_id, status);

CREATE INDEX IF NOT EXISTS ix_task_items_category
    ON task_items (category_id, sub_category_id);

CREATE TABLE IF NOT EXISTS task_sub_steps (
    id UUID PRIMARY KEY,
    task_item_id UUID NOT NULL REFERENCES task_items(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    is_done BOOLEAN NOT NULL DEFAULT FALSE,
    order_index INTEGER NOT NULL,
    completed_at TIMESTAMPTZ NULL,
    row_version INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT ck_task_sub_steps_order_index CHECK (order_index >= 1),
    CONSTRAINT ck_task_sub_steps_row_version CHECK (row_version >= 1)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_task_sub_steps_task_order
    ON task_sub_steps (task_item_id, order_index);

CREATE TABLE IF NOT EXISTS task_reference_links (
    id UUID PRIMARY KEY,
    task_item_id UUID NOT NULL REFERENCES task_items(id) ON DELETE CASCADE,
    url TEXT NOT NULL,
    label TEXT NULL,
    source_type TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS task_assets (
    id UUID PRIMARY KEY,
    task_item_id UUID NOT NULL REFERENCES task_items(id) ON DELETE CASCADE,
    asset_type TEXT NOT NULL,
    storage_path_or_url TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NULL,
    size_bytes BIGINT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_task_assets_size_bytes CHECK (size_bytes IS NULL OR size_bytes >= 0)
);

CREATE TABLE IF NOT EXISTS checklist_imports (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    source_type TEXT NOT NULL,
    raw_text TEXT NOT NULL,
    category_name TEXT NOT NULL,
    days_per_week TEXT[] NOT NULL DEFAULT '{}'::TEXT[],
    parsed_count INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_checklist_imports_parsed_count CHECK (parsed_count >= 0)
);

CREATE INDEX IF NOT EXISTS ix_checklist_imports_user_created
    ON checklist_imports (user_id, created_at DESC);

CREATE TABLE IF NOT EXISTS checklist_draft_items (
    id UUID PRIMARY KEY,
    checklist_import_id UUID NOT NULL REFERENCES checklist_imports(id) ON DELETE CASCADE,
    order_index INTEGER NOT NULL,
    text TEXT NOT NULL,
    week_number INTEGER NULL,
    sub_category_name TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_checklist_draft_items_order_index CHECK (order_index >= 1),
    CONSTRAINT ck_checklist_draft_items_week_number CHECK (week_number IS NULL OR week_number >= 1)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_checklist_draft_items_import_order
    ON checklist_draft_items (checklist_import_id, order_index);

CREATE TABLE IF NOT EXISTS task_status_events (
    id UUID PRIMARY KEY,
    task_item_id UUID NOT NULL REFERENCES task_items(id) ON DELETE CASCADE,
    from_status TEXT NOT NULL,
    to_status TEXT NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_task_status_events_from_status CHECK (from_status IN ('Todo', 'InProgress', 'Done', 'Cancelled', 'Skipped')),
    CONSTRAINT ck_task_status_events_to_status CHECK (to_status IN ('Todo', 'InProgress', 'Done', 'Cancelled', 'Skipped'))
);

CREATE INDEX IF NOT EXISTS ix_task_status_events_task_changed
    ON task_status_events (task_item_id, changed_at DESC);

CREATE TABLE IF NOT EXISTS task_spillover_events (
    id UUID PRIMARY KEY,
    task_item_id UUID NOT NULL REFERENCES task_items(id) ON DELETE CASCADE,
    from_week_start DATE NOT NULL,
    to_week_start DATE NOT NULL,
    reason TEXT NOT NULL,
    moved_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_task_spillover_events_task_moved
    ON task_spillover_events (task_item_id, moved_at DESC);

CREATE TABLE IF NOT EXISTS week_plans (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    week_start_date DATE NOT NULL,
    workload_mode TEXT NOT NULL,
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_week_plans_workload_mode CHECK (workload_mode IN ('Green', 'Yellow', 'Red'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_week_plans_user_week
    ON week_plans (user_id, week_start_date);

CREATE TABLE IF NOT EXISTS rule_definitions (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    rule_type TEXT NOT NULL,
    severity TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    rule_config_json JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_rule_definitions_severity CHECK (severity IN ('Warning', 'Block', 'AutoFix'))
);

CREATE INDEX IF NOT EXISTS ix_rule_definitions_user_active
    ON rule_definitions (user_id, is_active);

CREATE TABLE IF NOT EXISTS notification_settings (
    user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    daily_digest_time TIME NOT NULL DEFAULT TIME '09:00:00',
    evening_reminder_time TIME NOT NULL DEFAULT TIME '19:30:00',
    teams_destination TEXT NOT NULL DEFAULT 'personal-chat',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS notification_schedules (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    task_item_id UUID NULL REFERENCES task_items(id) ON DELETE SET NULL,
    channel TEXT NOT NULL,
    scheduled_at TIMESTAMPTZ NOT NULL,
    status TEXT NOT NULL,
    sent_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS ix_notification_schedules_user_scheduled
    ON notification_schedules (user_id, scheduled_at DESC);

CREATE TABLE IF NOT EXISTS integration_settings (
    id UUID PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider TEXT NOT NULL,
    account_identifier TEXT NOT NULL,
    access_token_ref TEXT NOT NULL,
    refresh_token_ref TEXT NOT NULL,
    token_expiry_utc TIMESTAMPTZ NOT NULL,
    is_connected BOOLEAN NOT NULL DEFAULT TRUE,
    last_sync_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_integration_settings_user_provider_account
    ON integration_settings (user_id, LOWER(provider), LOWER(account_identifier));

INSERT INTO schema_migrations (version, description)
VALUES ('V1', 'Baseline relational schema for Rora Quest')
ON CONFLICT (version) DO NOTHING;
