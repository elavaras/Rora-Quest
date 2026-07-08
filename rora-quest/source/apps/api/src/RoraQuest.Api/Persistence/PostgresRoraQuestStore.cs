using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// PostgreSQL-backed store using Npgsql + Dapper (no EF Core).
/// <para>
/// Load hydrates the full per-user aggregate with a handful of SELECTs.
/// Save persists the whole aggregate atomically inside a single transaction using a
/// delete-then-reinsert strategy for child collections and upserts for the user and
/// notification settings. For a single-user personal tracker the dataset is small, so
/// whole-aggregate replacement is simple and correct.
/// </para>
/// </summary>
public sealed class PostgresRoraQuestStore : IRoraQuestStore
{
    private readonly NpgsqlDataSource _dataSource;

    static PostgresRoraQuestStore()
    {
        // Map snake_case columns to PascalCase record parameters/properties.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public PostgresRoraQuestStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public UserData Load(string userId)
    {
        using var conn = _dataSource.OpenConnection();
        var data = new UserData();
        var arg = new { u = userId };

        // Categories
        foreach (var c in conn.Query<CategoryRow>(
            "SELECT id, user_id, name, parent_category_id, created_at FROM categories WHERE user_id = @u", arg))
        {
            data.Categories[c.Id] = new Category
            {
                Id = c.Id,
                UserId = c.UserId,
                Name = c.Name,
                ParentCategoryId = c.ParentCategoryId,
                CreatedAt = c.CreatedAt
            };
        }

        // Tasks
        foreach (var t in conn.Query<TaskRow>(
            @"SELECT id, user_id, title, description, category_id, sub_category_id, planned_week_start,
                     assigned_to, priority, status, due_date, start_at, end_at, calendar_event_id,
                     reminder_at, question_and_reasoning, logic_notes, algorithm_notes, diagram_content,
                     created_at, updated_at, row_version
              FROM task_items WHERE user_id = @u", arg))
        {
            var task = new TaskItem
            {
                Id = t.Id,
                UserId = t.UserId,
                Title = t.Title,
                Description = t.Description,
                CategoryId = t.CategoryId,
                SubCategoryId = t.SubCategoryId,
                PlannedWeekStart = DateOnly.FromDateTime(t.PlannedWeekStart),
                AssignedTo = t.AssignedTo,
                Priority = t.Priority,
                Status = Enum.Parse<TaskStatus>(t.Status),
                DueDate = t.DueDate.HasValue ? DateOnly.FromDateTime(t.DueDate.Value) : null,
                StartAt = t.StartAt,
                EndAt = t.EndAt,
                CalendarEventId = t.CalendarEventId,
                ReminderAt = t.ReminderAt,
                QuestionAndReasoning = t.QuestionAndReasoning,
                LogicNotes = t.LogicNotes,
                AlgorithmNotes = t.AlgorithmNotes,
                DiagramContent = t.DiagramContent,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                RowVersion = t.RowVersion
            };
            data.Tasks[task.Id] = task;
        }

        // Sub-steps
        foreach (var s in conn.Query<SubStepRow>(
            @"SELECT id, task_item_id, title, is_done, order_index, completed_at, row_version
              FROM task_sub_steps WHERE task_item_id IN (SELECT id FROM task_items WHERE user_id = @u)", arg))
        {
            if (data.Tasks.TryGetValue(s.TaskItemId, out var task))
            {
                task.SubSteps.Add(new TaskSubStep(s.Id, s.Title, s.IsDone, s.OrderIndex, s.CompletedAt, s.RowVersion));
            }
        }

        // Reference links
        foreach (var l in conn.Query<LinkRow>(
            @"SELECT id, task_item_id, url, label, source_type
              FROM task_reference_links WHERE task_item_id IN (SELECT id FROM task_items WHERE user_id = @u)", arg))
        {
            if (data.Tasks.TryGetValue(l.TaskItemId, out var task))
            {
                task.Links.Add(new TaskLink(l.Id, l.Url, l.Label, l.SourceType));
            }
        }

        // Assets
        foreach (var a in conn.Query<AssetRow>(
            @"SELECT id, task_item_id, asset_type, storage_path_or_url, file_name, content_type, size_bytes, created_at
              FROM task_assets WHERE task_item_id IN (SELECT id FROM task_items WHERE user_id = @u)", arg))
        {
            if (data.Tasks.TryGetValue(a.TaskItemId, out var task))
            {
                task.Assets.Add(new TaskAsset(a.Id, a.AssetType, a.StoragePathOrUrl, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt));
            }
        }

        // Status events
        foreach (var e in conn.Query<StatusEventRow>(
            @"SELECT id, task_item_id, from_status, to_status, changed_at
              FROM task_status_events WHERE task_item_id IN (SELECT id FROM task_items WHERE user_id = @u)", arg))
        {
            if (data.Tasks.TryGetValue(e.TaskItemId, out var task))
            {
                task.StatusEvents.Add(new TaskStatusEvent(e.Id, Enum.Parse<TaskStatus>(e.FromStatus), Enum.Parse<TaskStatus>(e.ToStatus), e.ChangedAt));
            }
        }

        // Spillover events
        foreach (var sp in conn.Query<SpilloverRow>(
            @"SELECT id, task_item_id, from_week_start, to_week_start, reason, moved_at
              FROM task_spillover_events WHERE task_item_id IN (SELECT id FROM task_items WHERE user_id = @u)", arg))
        {
            if (data.Tasks.TryGetValue(sp.TaskItemId, out var task))
            {
                task.Spillovers.Add(new TaskSpilloverEvent(sp.Id, DateOnly.FromDateTime(sp.FromWeekStart), DateOnly.FromDateTime(sp.ToWeekStart), sp.Reason, sp.MovedAt));
            }
        }

        // Checklist imports + drafts
        var imports = conn.Query<ImportRow>(
            @"SELECT id, user_id, source_type, raw_text, category_name, days_per_week, parsed_count, created_at
              FROM checklist_imports WHERE user_id = @u", arg).ToList();
        foreach (var im in imports)
        {
            data.Imports[im.Id] = new ChecklistImport
            {
                Id = im.Id,
                UserId = im.UserId,
                SourceType = im.SourceType,
                RawText = im.RawText,
                CategoryName = im.CategoryName,
                DaysPerWeek = (im.DaysPerWeek ?? []).ToList(),
                ParsedCount = im.ParsedCount,
                CreatedAt = im.CreatedAt,
                DraftItems = []
            };
        }

        foreach (var d in conn.Query<DraftRow>(
            @"SELECT id, checklist_import_id, order_index, text, week_number, sub_category_name
              FROM checklist_draft_items
              WHERE checklist_import_id IN (SELECT id FROM checklist_imports WHERE user_id = @u)
              ORDER BY order_index", arg))
        {
            if (data.Imports.TryGetValue(d.ChecklistImportId, out var import))
            {
                import.DraftItems.Add(new ChecklistDraftItem(d.Id, d.OrderIndex, d.Text, d.WeekNumber, d.SubCategoryName));
            }
        }

        // Week plans
        foreach (var w in conn.Query<WeekPlanRow>(
            @"SELECT id, user_id, week_start_date, workload_mode, notes, created_at, updated_at
              FROM week_plans WHERE user_id = @u", arg))
        {
            var weekStart = DateOnly.FromDateTime(w.WeekStartDate);
            data.WeekPlans[weekStart] = new WeekPlan(
                weekStart, Enum.Parse<WorkloadMode>(w.WorkloadMode), w.Notes, w.CreatedAt, w.UpdatedAt);
        }

        // Rules
        foreach (var r in conn.Query<RuleRow>(
            @"SELECT id, user_id, name, rule_type, severity, is_active, rule_config_json, created_at, updated_at
              FROM rule_definitions WHERE user_id = @u", arg))
        {
            data.Rules[r.Id] = new RuleDefinition
            {
                Id = r.Id,
                Name = r.Name,
                RuleType = r.RuleType,
                Severity = Enum.Parse<RuleSeverity>(r.Severity),
                IsActive = r.IsActive,
                RuleConfigJson = r.RuleConfigJson,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }

        // Notification settings
        var settings = conn.QuerySingleOrDefault<NotifSettingsRow>(
            @"SELECT daily_digest_time, evening_reminder_time, teams_destination
              FROM notification_settings WHERE user_id = @u", arg);
        if (settings is not null)
        {
            data.NotificationSettings = new NotificationSettings
            {
                DailyDigestTime = TimeOnly.FromTimeSpan(settings.DailyDigestTime),
                EveningReminderTime = TimeOnly.FromTimeSpan(settings.EveningReminderTime),
                TeamsDestination = settings.TeamsDestination
            };
        }

        // Notification schedules
        foreach (var sc in conn.Query<ScheduleRow>(
            @"SELECT id, user_id, task_item_id, channel, scheduled_at, status, sent_at
              FROM notification_schedules WHERE user_id = @u", arg))
        {
            data.NotificationSchedules.Add(new NotificationSchedule(sc.Id, sc.TaskItemId, sc.Channel, sc.ScheduledAt, sc.Status, sc.SentAt));
        }

        // Integrations
        foreach (var ig in conn.Query<IntegrationRow>(
            @"SELECT id, user_id, provider, account_identifier, access_token_ref, refresh_token_ref,
                     token_expiry_utc, is_connected, last_sync_at
              FROM integration_settings WHERE user_id = @u", arg))
        {
            data.Integrations[ig.Provider] = new IntegrationSetting
            {
                Id = ig.Id,
                UserId = ig.UserId,
                Provider = ig.Provider,
                AccountIdentifier = ig.AccountIdentifier,
                AccessTokenRef = ig.AccessTokenRef,
                RefreshTokenRef = ig.RefreshTokenRef,
                TokenExpiryUtc = ig.TokenExpiryUtc,
                IsConnected = ig.IsConnected,
                LastSyncAt = ig.LastSyncAt
            };
        }

        return data;
    }

    public void Save(string userId, UserData data)
    {
        using var conn = _dataSource.OpenConnection();
        using var tx = conn.BeginTransaction();

        // Ensure users exist for the owner and every distinct assignee (FK targets).
        var userIds = new HashSet<string>(StringComparer.Ordinal) { userId };
        foreach (var t in data.Tasks.Values)
        {
            if (!string.IsNullOrWhiteSpace(t.AssignedTo)) userIds.Add(t.AssignedTo);
        }
        foreach (var id in userIds)
        {
            Exec(conn, tx,
                @"INSERT INTO users (id, timezone, is_active, created_at, updated_at)
                  VALUES (@id, 'Asia/Kolkata', TRUE, NOW(), NOW())
                  ON CONFLICT (id) DO UPDATE SET updated_at = NOW()",
                p => p.AddWithValue("id", id));
        }

        // Delete existing aggregate for this user (children cascade via FKs).
        foreach (var sql in new[]
        {
            "DELETE FROM notification_schedules WHERE user_id = @u",
            "DELETE FROM integration_settings WHERE user_id = @u",
            "DELETE FROM rule_definitions WHERE user_id = @u",
            "DELETE FROM week_plans WHERE user_id = @u",
            "DELETE FROM checklist_imports WHERE user_id = @u",
            "DELETE FROM task_items WHERE user_id = @u",
            "DELETE FROM categories WHERE user_id = @u"
        })
        {
            Exec(conn, tx, sql, p => p.AddWithValue("u", userId));
        }

        // Categories (topological: parents before children).
        var remaining = data.Categories.Values.ToList();
        var insertedCats = new HashSet<Guid>();
        while (remaining.Count > 0)
        {
            var batch = remaining
                .Where(c => c.ParentCategoryId is null
                            || insertedCats.Contains(c.ParentCategoryId.Value)
                            || !data.Categories.ContainsKey(c.ParentCategoryId.Value))
                .ToList();
            if (batch.Count == 0)
            {
                batch = remaining.ToList(); // break potential cycle defensively
            }

            foreach (var c in batch)
            {
                var parent = c.ParentCategoryId is not null && data.Categories.ContainsKey(c.ParentCategoryId.Value)
                    ? c.ParentCategoryId
                    : (insertedCats.Contains(c.ParentCategoryId ?? Guid.Empty) ? c.ParentCategoryId : null);

                Exec(conn, tx,
                    @"INSERT INTO categories (id, user_id, name, parent_category_id, created_at, updated_at)
                      VALUES (@id, @user, @name, @parent, @created, NOW())",
                    p =>
                    {
                        p.AddWithValue("id", c.Id);
                        p.AddWithValue("user", userId);
                        p.AddWithValue("name", c.Name);
                        p.AddWithValue("parent", (object?)parent ?? DBNull.Value);
                        p.AddWithValue("created", c.CreatedAt);
                    });
                insertedCats.Add(c.Id);
            }

            remaining = remaining.Where(c => !insertedCats.Contains(c.Id)).ToList();
        }

        // Tasks + children.
        foreach (var t in data.Tasks.Values)
        {
            Exec(conn, tx,
                @"INSERT INTO task_items
                    (id, user_id, title, description, category_id, sub_category_id, planned_week_start,
                     assigned_to, priority, status, due_date, start_at, end_at, calendar_event_id,
                     reminder_at, question_and_reasoning, logic_notes, algorithm_notes, diagram_content,
                     created_at, updated_at, row_version)
                  VALUES
                    (@id, @user, @title, @description, @category, @sub, @week,
                     @assigned, @priority, @status, @due, @start, @end, @calendar,
                     @reminder, @qr, @logic, @algo, @diagram,
                     @created, @updated, @row)",
                p =>
                {
                    p.AddWithValue("id", t.Id);
                    p.AddWithValue("user", userId);
                    p.AddWithValue("title", t.Title);
                    p.AddWithValue("description", (object?)t.Description ?? DBNull.Value);
                    p.AddWithValue("category", (object?)t.CategoryId ?? DBNull.Value);
                    p.AddWithValue("sub", (object?)t.SubCategoryId ?? DBNull.Value);
                    p.AddWithValue("week", t.PlannedWeekStart);
                    p.AddWithValue("assigned", string.IsNullOrWhiteSpace(t.AssignedTo) ? userId : t.AssignedTo);
                    p.AddWithValue("priority", (object?)t.Priority ?? DBNull.Value);
                    p.AddWithValue("status", t.Status.ToString());
                    p.AddWithValue("due", (object?)t.DueDate ?? DBNull.Value);
                    p.AddWithValue("start", (object?)t.StartAt ?? DBNull.Value);
                    p.AddWithValue("end", (object?)t.EndAt ?? DBNull.Value);
                    p.AddWithValue("calendar", (object?)t.CalendarEventId ?? DBNull.Value);
                    p.AddWithValue("reminder", (object?)t.ReminderAt ?? DBNull.Value);
                    p.AddWithValue("qr", (object?)t.QuestionAndReasoning ?? DBNull.Value);
                    p.AddWithValue("logic", (object?)t.LogicNotes ?? DBNull.Value);
                    p.AddWithValue("algo", (object?)t.AlgorithmNotes ?? DBNull.Value);
                    p.AddWithValue("diagram", (object?)t.DiagramContent ?? DBNull.Value);
                    p.AddWithValue("created", t.CreatedAt);
                    p.AddWithValue("updated", t.UpdatedAt);
                    p.AddWithValue("row", t.RowVersion < 1 ? 1 : t.RowVersion);
                });

            // Sub-steps (re-sequence order_index to guarantee the unique (task, order) constraint).
            var steps = t.SubSteps.OrderBy(s => s.OrderIndex).ToList();
            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                var order = i + 1;
                Exec(conn, tx,
                    @"INSERT INTO task_sub_steps (id, task_item_id, title, is_done, order_index, completed_at, row_version)
                      VALUES (@id, @task, @title, @done, @order, @completed, @row)",
                    p =>
                    {
                        p.AddWithValue("id", s.Id);
                        p.AddWithValue("task", t.Id);
                        p.AddWithValue("title", s.Title);
                        p.AddWithValue("done", s.IsDone);
                        p.AddWithValue("order", order);
                        p.AddWithValue("completed", (object?)s.CompletedAt ?? DBNull.Value);
                        p.AddWithValue("row", s.RowVersion < 1 ? 1 : s.RowVersion);
                    });
            }

            foreach (var l in t.Links)
            {
                Exec(conn, tx,
                    @"INSERT INTO task_reference_links (id, task_item_id, url, label, source_type, created_at)
                      VALUES (@id, @task, @url, @label, @source, NOW())",
                    p =>
                    {
                        p.AddWithValue("id", l.Id);
                        p.AddWithValue("task", t.Id);
                        p.AddWithValue("url", l.Url);
                        p.AddWithValue("label", (object?)l.Label ?? DBNull.Value);
                        p.AddWithValue("source", (object?)l.SourceType ?? DBNull.Value);
                    });
            }

            foreach (var a in t.Assets)
            {
                Exec(conn, tx,
                    @"INSERT INTO task_assets (id, task_item_id, asset_type, storage_path_or_url, file_name, content_type, size_bytes, created_at)
                      VALUES (@id, @task, @type, @path, @file, @content, @size, @created)",
                    p =>
                    {
                        p.AddWithValue("id", a.Id);
                        p.AddWithValue("task", t.Id);
                        p.AddWithValue("type", a.AssetType);
                        p.AddWithValue("path", a.StoragePathOrUrl);
                        p.AddWithValue("file", a.FileName);
                        p.AddWithValue("content", (object?)a.ContentType ?? DBNull.Value);
                        p.AddWithValue("size", (object?)a.SizeBytes ?? DBNull.Value);
                        p.AddWithValue("created", a.CreatedAt);
                    });
            }

            foreach (var e in t.StatusEvents)
            {
                Exec(conn, tx,
                    @"INSERT INTO task_status_events (id, task_item_id, from_status, to_status, changed_at)
                      VALUES (@id, @task, @from, @to, @changed)",
                    p =>
                    {
                        p.AddWithValue("id", e.Id);
                        p.AddWithValue("task", t.Id);
                        p.AddWithValue("from", e.FromStatus.ToString());
                        p.AddWithValue("to", e.ToStatus.ToString());
                        p.AddWithValue("changed", e.ChangedAt);
                    });
            }

            foreach (var sp in t.Spillovers)
            {
                Exec(conn, tx,
                    @"INSERT INTO task_spillover_events (id, task_item_id, from_week_start, to_week_start, reason, moved_at)
                      VALUES (@id, @task, @from, @to, @reason, @moved)",
                    p =>
                    {
                        p.AddWithValue("id", sp.Id);
                        p.AddWithValue("task", t.Id);
                        p.AddWithValue("from", sp.FromWeekStart);
                        p.AddWithValue("to", sp.ToWeekStart);
                        p.AddWithValue("reason", sp.Reason);
                        p.AddWithValue("moved", sp.MovedAt);
                    });
            }
        }

        // Checklist imports + drafts.
        foreach (var im in data.Imports.Values)
        {
            Exec(conn, tx,
                @"INSERT INTO checklist_imports (id, user_id, source_type, raw_text, category_name, days_per_week, parsed_count, created_at)
                  VALUES (@id, @user, @source, @raw, @category, @days, @count, @created)",
                p =>
                {
                    p.AddWithValue("id", im.Id);
                    p.AddWithValue("user", userId);
                    p.AddWithValue("source", im.SourceType);
                    p.AddWithValue("raw", im.RawText);
                    p.AddWithValue("category", im.CategoryName);
                    p.Add(new NpgsqlParameter("days", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    {
                        Value = (im.DaysPerWeek ?? []).ToArray()
                    });
                    p.AddWithValue("count", im.ParsedCount);
                    p.AddWithValue("created", im.CreatedAt);
                });

            var drafts = im.DraftItems.OrderBy(d => d.Order).ToList();
            for (var i = 0; i < drafts.Count; i++)
            {
                var d = drafts[i];
                var order = d.Order < 1 ? i + 1 : d.Order;
                Exec(conn, tx,
                    @"INSERT INTO checklist_draft_items (id, checklist_import_id, order_index, text, week_number, sub_category_name, created_at)
                      VALUES (@id, @import, @order, @text, @week, @sub, NOW())",
                    p =>
                    {
                        p.AddWithValue("id", d.Id);
                        p.AddWithValue("import", im.Id);
                        p.AddWithValue("order", order);
                        p.AddWithValue("text", d.Text);
                        p.AddWithValue("week", (object?)d.WeekNumber ?? DBNull.Value);
                        p.AddWithValue("sub", (object?)d.SubCategoryName ?? DBNull.Value);
                    });
            }
        }

        // Week plans.
        foreach (var w in data.WeekPlans.Values)
        {
            Exec(conn, tx,
                @"INSERT INTO week_plans (id, user_id, week_start_date, workload_mode, notes, created_at, updated_at)
                  VALUES (@id, @user, @week, @mode, @notes, @created, @updated)",
                p =>
                {
                    p.AddWithValue("id", Guid.NewGuid());
                    p.AddWithValue("user", userId);
                    p.AddWithValue("week", w.WeekStartDate);
                    p.AddWithValue("mode", w.WorkloadMode.ToString());
                    p.AddWithValue("notes", (object?)w.Notes ?? DBNull.Value);
                    p.AddWithValue("created", w.CreatedAt);
                    p.AddWithValue("updated", w.UpdatedAt);
                });
        }

        // Rules.
        foreach (var r in data.Rules.Values)
        {
            Exec(conn, tx,
                @"INSERT INTO rule_definitions (id, user_id, name, rule_type, severity, is_active, rule_config_json, created_at, updated_at)
                  VALUES (@id, @user, @name, @type, @severity, @active, @config, @created, @updated)",
                p =>
                {
                    p.AddWithValue("id", r.Id);
                    p.AddWithValue("user", userId);
                    p.AddWithValue("name", r.Name);
                    p.AddWithValue("type", r.RuleType);
                    p.AddWithValue("severity", r.Severity.ToString());
                    p.AddWithValue("active", r.IsActive);
                    p.Add(new NpgsqlParameter("config", NpgsqlDbType.Jsonb)
                    {
                        Value = (object?)r.RuleConfigJson ?? DBNull.Value
                    });
                    p.AddWithValue("created", r.CreatedAt);
                    p.AddWithValue("updated", r.UpdatedAt);
                });
        }

        // Notification settings (upsert).
        Exec(conn, tx,
            @"INSERT INTO notification_settings (user_id, daily_digest_time, evening_reminder_time, teams_destination, updated_at)
              VALUES (@user, @daily, @evening, @teams, NOW())
              ON CONFLICT (user_id) DO UPDATE SET
                daily_digest_time = EXCLUDED.daily_digest_time,
                evening_reminder_time = EXCLUDED.evening_reminder_time,
                teams_destination = EXCLUDED.teams_destination,
                updated_at = NOW()",
            p =>
            {
                p.AddWithValue("user", userId);
                p.AddWithValue("daily", data.NotificationSettings.DailyDigestTime);
                p.AddWithValue("evening", data.NotificationSettings.EveningReminderTime);
                p.AddWithValue("teams", data.NotificationSettings.TeamsDestination);
            });

        // Notification schedules.
        foreach (var sc in data.NotificationSchedules)
        {
            Exec(conn, tx,
                @"INSERT INTO notification_schedules (id, user_id, task_item_id, channel, scheduled_at, status, sent_at)
                  VALUES (@id, @user, @task, @channel, @scheduled, @status, @sent)",
                p =>
                {
                    p.AddWithValue("id", sc.Id);
                    p.AddWithValue("user", userId);
                    p.AddWithValue("task", (object?)sc.TaskId ?? DBNull.Value);
                    p.AddWithValue("channel", sc.Channel);
                    p.AddWithValue("scheduled", sc.ScheduledAt);
                    p.AddWithValue("status", sc.Status);
                    p.AddWithValue("sent", (object?)sc.SentAt ?? DBNull.Value);
                });
        }

        // Integrations.
        foreach (var ig in data.Integrations.Values)
        {
            Exec(conn, tx,
                @"INSERT INTO integration_settings
                    (id, user_id, provider, account_identifier, access_token_ref, refresh_token_ref,
                     token_expiry_utc, is_connected, last_sync_at, created_at, updated_at)
                  VALUES
                    (@id, @user, @provider, @account, @access, @refresh, @expiry, @connected, @sync, NOW(), NOW())",
                p =>
                {
                    p.AddWithValue("id", ig.Id == Guid.Empty ? Guid.NewGuid() : ig.Id);
                    p.AddWithValue("user", userId);
                    p.AddWithValue("provider", ig.Provider);
                    p.AddWithValue("account", ig.AccountIdentifier);
                    p.AddWithValue("access", ig.AccessTokenRef);
                    p.AddWithValue("refresh", ig.RefreshTokenRef);
                    p.AddWithValue("expiry", ig.TokenExpiryUtc);
                    p.AddWithValue("connected", ig.IsConnected);
                    p.AddWithValue("sync", ig.LastSyncAt);
                });
        }

        tx.Commit();
    }

    private static void Exec(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, Action<NpgsqlParameterCollection> bind)
    {
        using var cmd = new NpgsqlCommand(sql, conn, tx);
        bind(cmd.Parameters);
        cmd.ExecuteNonQuery();
    }

    // Row DTOs used for Dapper reads (string columns for enums; mapped to domain types above).
    // Mutable classes so Dapper's MatchNamesWithUnderscores maps snake_case columns to properties.
    private sealed class CategoryRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public Guid? ParentCategoryId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class TaskRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? SubCategoryId { get; set; }
        public DateTime PlannedWeekStart { get; set; }
        public string AssignedTo { get; set; } = "";
        public string? Priority { get; set; }
        public string Status { get; set; } = "";
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? StartAt { get; set; }
        public DateTimeOffset? EndAt { get; set; }
        public string? CalendarEventId { get; set; }
        public DateTimeOffset? ReminderAt { get; set; }
        public string? QuestionAndReasoning { get; set; }
        public string? LogicNotes { get; set; }
        public string? AlgorithmNotes { get; set; }
        public string? DiagramContent { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int RowVersion { get; set; }
    }

    private sealed class SubStepRow
    {
        public Guid Id { get; set; }
        public Guid TaskItemId { get; set; }
        public string Title { get; set; } = "";
        public bool IsDone { get; set; }
        public int OrderIndex { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public int RowVersion { get; set; }
    }

    private sealed class LinkRow
    {
        public Guid Id { get; set; }
        public Guid TaskItemId { get; set; }
        public string Url { get; set; } = "";
        public string? Label { get; set; }
        public string? SourceType { get; set; }
    }

    private sealed class AssetRow
    {
        public Guid Id { get; set; }
        public Guid TaskItemId { get; set; }
        public string AssetType { get; set; } = "";
        public string StoragePathOrUrl { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? ContentType { get; set; }
        public long? SizeBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class StatusEventRow
    {
        public Guid Id { get; set; }
        public Guid TaskItemId { get; set; }
        public string FromStatus { get; set; } = "";
        public string ToStatus { get; set; } = "";
        public DateTimeOffset ChangedAt { get; set; }
    }

    private sealed class SpilloverRow
    {
        public Guid Id { get; set; }
        public Guid TaskItemId { get; set; }
        public DateTime FromWeekStart { get; set; }
        public DateTime ToWeekStart { get; set; }
        public string Reason { get; set; } = "";
        public DateTimeOffset MovedAt { get; set; }
    }

    private sealed class ImportRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string RawText { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string[] DaysPerWeek { get; set; } = Array.Empty<string>();
        public int ParsedCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class DraftRow
    {
        public Guid Id { get; set; }
        public Guid ChecklistImportId { get; set; }
        public int OrderIndex { get; set; }
        public string Text { get; set; } = "";
        public int? WeekNumber { get; set; }
        public string? SubCategoryName { get; set; }
    }

    private sealed class WeekPlanRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public DateTime WeekStartDate { get; set; }
        public string WorkloadMode { get; set; } = "";
        public string? Notes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class RuleRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RuleType { get; set; } = "";
        public string Severity { get; set; } = "";
        public bool IsActive { get; set; }
        public string? RuleConfigJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class NotifSettingsRow
    {
        public TimeSpan DailyDigestTime { get; set; }
        public TimeSpan EveningReminderTime { get; set; }
        public string TeamsDestination { get; set; } = "";
    }

    private sealed class ScheduleRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public Guid? TaskItemId { get; set; }
        public string Channel { get; set; } = "";
        public DateTimeOffset ScheduledAt { get; set; }
        public string Status { get; set; } = "";
        public DateTimeOffset? SentAt { get; set; }
    }

    private sealed class IntegrationRow
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string Provider { get; set; } = "";
        public string AccountIdentifier { get; set; } = "";
        public string AccessTokenRef { get; set; } = "";
        public string RefreshTokenRef { get; set; } = "";
        public DateTimeOffset TokenExpiryUtc { get; set; }
        public bool IsConnected { get; set; }
        public DateTimeOffset LastSyncAt { get; set; }
    }
}
