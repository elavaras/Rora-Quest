-- Seed uses explicit UUIDs so it works on Azure Database for PostgreSQL without pgcrypto/uuid extensions.

INSERT INTO users (id, display_name, primary_email)
VALUES ('demo-user', 'Demo User', 'demo@roraquest.local')
ON CONFLICT (id) DO NOTHING;

INSERT INTO notification_settings (user_id)
VALUES ('demo-user')
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO categories (id, user_id, name, parent_category_id)
VALUES ('11111111-1111-1111-1111-111111111111', 'demo-user', 'DSA', NULL)
ON CONFLICT (id) DO NOTHING;

INSERT INTO categories (id, user_id, name, parent_category_id)
VALUES ('22222222-2222-2222-2222-222222222222', 'demo-user', 'Array', '11111111-1111-1111-1111-111111111111')
ON CONFLICT (id) DO NOTHING;

INSERT INTO categories (id, user_id, name, parent_category_id)
VALUES ('33333333-3333-3333-3333-333333333333', 'demo-user', 'Sliding Window', '11111111-1111-1111-1111-111111111111')
ON CONFLICT (id) DO NOTHING;

INSERT INTO week_plans (id, user_id, week_start_date, workload_mode, notes)
VALUES ('44444444-4444-4444-4444-444444444444', 'demo-user', CURRENT_DATE - ((EXTRACT(DOW FROM CURRENT_DATE)::INT + 6) % 7), 'Green', 'Development seed data')
ON CONFLICT (user_id, week_start_date) DO NOTHING;

INSERT INTO rule_definitions (id, user_id, name, rule_type, severity, is_active, rule_config_json)
VALUES ('55555555-5555-5555-5555-555555555555', 'demo-user', 'Interruption handling', 'interruption', 'AutoFix', TRUE, '{}'::jsonb)
ON CONFLICT (id) DO NOTHING;
