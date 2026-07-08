using System.Globalization;
using System.Text.RegularExpressions;

public static class ApiEndpoints
{
    public static void MapRoraQuestEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        MapChecklist(api);
        MapCategories(api);
        MapTasks(api);
        MapPlanningRules(api);
        MapCalendar(api);
        MapNotifications(api);
        MapIntegrationSettings(api);
        MapReports(api);
    }

    private static void MapChecklist(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/checklists/imports");

        group.MapPost("/bulk-text", (BulkChecklistImportRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var userId = UserScope.GetUserId(http);
            return Results.Ok(svc.CreateChecklistImport(userId, req));
        });

        group.MapPost("/{importId:guid}/commit", (Guid importId, CommitChecklistImportRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var userId = UserScope.GetUserId(http);
            var result = svc.CommitChecklistImport(userId, importId, req.SelectedDraftIds);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/{importId:guid}", (Guid importId, RoraQuestService svc, HttpContext http) =>
        {
            var userId = UserScope.GetUserId(http);
            var result = svc.GetChecklistImport(userId, importId);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static void MapCategories(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/categories");

        group.MapGet("", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetCategories(UserScope.GetUserId(http))));

        group.MapPost("", (CreateCategoryRequest req, RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.CreateCategory(UserScope.GetUserId(http), req));
        });

        group.MapPatch("/{categoryId:guid}", (Guid categoryId, UpdateCategoryRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var updated = svc.UpdateCategory(UserScope.GetUserId(http), categoryId, req);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("/{categoryId:guid}", (Guid categoryId, RoraQuestService svc, HttpContext http) =>
        {
            return svc.DeleteCategory(UserScope.GetUserId(http), categoryId) ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/tree", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetCategoryTree(UserScope.GetUserId(http))));
    }

    private static void MapTasks(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tasks");

        group.MapGet("", (HttpContext http, RoraQuestService svc) =>
        {
            var q = TaskQuery.FromHttp(http.Request.Query);
            return Results.Ok(svc.GetTasks(UserScope.GetUserId(http), q));
        });

        group.MapPost("", (CreateTaskRequest req, RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.CreateTask(UserScope.GetUserId(http), req));
        });

        group.MapGet("/{taskId:guid}", (Guid taskId, RoraQuestService svc, HttpContext http) =>
        {
            var task = svc.GetTask(UserScope.GetUserId(http), taskId);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        group.MapPatch("/{taskId:guid}", (Guid taskId, UpdateTaskRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.UpdateTask(UserScope.GetUserId(http), taskId, req);
            return result.ToResult();
        });

        group.MapPatch("/{taskId:guid}/status", (Guid taskId, UpdateTaskStatusRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.UpdateTaskStatus(UserScope.GetUserId(http), taskId, req);
            return result.ToResult();
        });

        group.MapGet("/{taskId:guid}/substeps", (Guid taskId, RoraQuestService svc, HttpContext http) =>
        {
            var data = svc.GetSubsteps(UserScope.GetUserId(http), taskId);
            return data is null ? Results.NotFound() : Results.Ok(data);
        });

        group.MapPost("/{taskId:guid}/substeps", (Guid taskId, CreateSubstepRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.CreateSubstep(UserScope.GetUserId(http), taskId, req);
            return result.ToResult();
        });

        group.MapPatch("/{taskId:guid}/substeps/{subStepId:guid}", (Guid taskId, Guid subStepId, UpdateSubstepRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.UpdateSubstep(UserScope.GetUserId(http), taskId, subStepId, req);
            return result.ToResult();
        });

        group.MapDelete("/{taskId:guid}/substeps/{subStepId:guid}", (Guid taskId, Guid subStepId, RoraQuestService svc, HttpContext http) =>
        {
            return svc.DeleteSubstep(UserScope.GetUserId(http), taskId, subStepId) ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{taskId:guid}/links", (Guid taskId, CreateLinkRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.CreateLink(UserScope.GetUserId(http), taskId, req);
            return result.ToResult();
        });

        group.MapPatch("/{taskId:guid}/links/{linkId:guid}", (Guid taskId, Guid linkId, UpdateLinkRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.UpdateLink(UserScope.GetUserId(http), taskId, linkId, req);
            return result.ToResult();
        });

        group.MapDelete("/{taskId:guid}/links/{linkId:guid}", (Guid taskId, Guid linkId, RoraQuestService svc, HttpContext http) =>
        {
            return svc.DeleteLink(UserScope.GetUserId(http), taskId, linkId) ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{taskId:guid}/assets", (Guid taskId, CreateAssetRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.CreateAsset(UserScope.GetUserId(http), taskId, req);
            return result.ToResult();
        });

        group.MapPost("/spillover", (SpilloverRequest req, RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.MoveSpillover(UserScope.GetUserId(http), req));
        });

        group.MapGet("/spillover/history", (RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.GetSpilloverHistory(UserScope.GetUserId(http), null));
        });

        group.MapGet("/{taskId:guid}/spillover/history", (Guid taskId, RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.GetSpilloverHistory(UserScope.GetUserId(http), taskId));
        });
    }

    private static void MapPlanningRules(RouteGroupBuilder api)
    {
        api.MapGet("/week-plans/{weekStart}", (string weekStart, RoraQuestService svc, HttpContext http) =>
        {
            if (!DateOnly.TryParse(weekStart, out var week))
            {
                return Results.BadRequest("Invalid weekStart date.");
            }

            var result = svc.GetWeekPlan(UserScope.GetUserId(http), week);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        api.MapPut("/week-plans/{weekStart}", (string weekStart, UpsertWeekPlanRequest req, RoraQuestService svc, HttpContext http) =>
        {
            if (!DateOnly.TryParse(weekStart, out var week))
            {
                return Results.BadRequest("Invalid weekStart date.");
            }

            return Results.Ok(svc.UpsertWeekPlan(UserScope.GetUserId(http), week, req));
        });

        var rules = api.MapGroup("/rules");
        rules.MapGet("", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetRules(UserScope.GetUserId(http))));
        rules.MapPost("", (CreateRuleRequest req, RoraQuestService svc, HttpContext http) => Results.Ok(svc.CreateRule(UserScope.GetUserId(http), req)));
        rules.MapPatch("/{ruleId:guid}", (Guid ruleId, UpdateRuleRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.UpdateRule(UserScope.GetUserId(http), ruleId, req);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        rules.MapPost("/evaluate", (EvaluateRulesRequest req, RoraQuestService svc, HttpContext http) =>
        {
            return Results.Ok(svc.EvaluateRules(UserScope.GetUserId(http), req));
        });
    }

    private static void MapCalendar(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/calendar");
        group.MapPost("/sync/tasks/{taskId:guid}", (Guid taskId, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.SyncCalendarTask(UserScope.GetUserId(http), taskId)));
        group.MapDelete("/sync/tasks/{taskId:guid}", (Guid taskId, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.UnsyncCalendarTask(UserScope.GetUserId(http), taskId)));
        group.MapPost("/sync/batch", (SyncBatchRequest req, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.SyncCalendarBatch(UserScope.GetUserId(http), req.TaskIds)));
        group.MapGet("/conflicts", (RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.GetCalendarConflicts(UserScope.GetUserId(http))));
    }

    private static void MapNotifications(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications");
        group.MapGet("/settings", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetNotificationSettings(UserScope.GetUserId(http))));
        group.MapPut("/settings", (UpdateNotificationSettingsRequest req, RoraQuestService svc, HttpContext http) => Results.Ok(svc.UpdateNotificationSettings(UserScope.GetUserId(http), req)));
        group.MapPost("/daily-digest/trigger", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.TriggerDailyDigest(UserScope.GetUserId(http))));
        group.MapGet("/schedules", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetNotificationSchedules(UserScope.GetUserId(http))));
    }

    private static void MapIntegrationSettings(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings/integrations");

        group.MapGet("", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetIntegrations(UserScope.GetUserId(http))));
        group.MapPost("/microsoft/connect", (ConnectMicrosoftRequest req, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.ConnectMicrosoft(UserScope.GetUserId(http), req)));
        group.MapPost("/microsoft/callback", (MicrosoftCallbackRequest req, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.HandleMicrosoftCallback(UserScope.GetUserId(http), req)));
        group.MapPost("/{provider}/disconnect", (string provider, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.DisconnectIntegration(UserScope.GetUserId(http), provider)));
        group.MapPost("/{provider}/test", (string provider, RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.TestIntegration(UserScope.GetUserId(http), provider)));
    }

    private static void MapReports(RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports");
        reports.MapGet("/progress", (HttpContext http, RoraQuestService svc) =>
        {
            var window = ReportWindow.FromHttp(http.Request.Query);
            return Results.Ok(svc.GetProgressReport(UserScope.GetUserId(http), window));
        });
        reports.MapGet("/timeline", (HttpContext http, RoraQuestService svc) =>
        {
            var window = ReportWindow.FromHttp(http.Request.Query);
            return Results.Ok(svc.GetTimelineReport(UserScope.GetUserId(http), window));
        });

        api.MapGet("/scorecard", (HttpContext http, RoraQuestService svc) =>
        {
            var window = ReportWindow.FromHttp(http.Request.Query);
            return Results.Ok(svc.GetScorecard(UserScope.GetUserId(http), window));
        });

        var tracking = api.MapGroup("/tracking");
        tracking.MapGet("/streaks", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetStreaks(UserScope.GetUserId(http))));
        tracking.MapGet("/consistency", (RoraQuestService svc, HttpContext http) => Results.Ok(svc.GetConsistency(UserScope.GetUserId(http))));

        api.MapGet("/planning/recommendation", (RoraQuestService svc, HttpContext http) =>
            Results.Ok(svc.GetPlanningRecommendation(UserScope.GetUserId(http))));
    }
}

public static class UserScope
{
    public static string GetUserId(HttpContext ctx) => ctx.Request.Headers["X-User-Id"].FirstOrDefault() ?? "demo-user";
}

public enum TaskStatus
{
    Todo,
    InProgress,
    Done,
    Cancelled,
    Skipped
}

public enum WorkloadMode
{
    Green,
    Yellow,
    Red
}

public enum RuleSeverity
{
    Warning,
    Block,
    AutoFix
}

public sealed class AppState
{
    public object Gate { get; } = new();
    public Dictionary<string, UserData> Users { get; } = new();
}

public sealed class UserData
{
    public Dictionary<Guid, Category> Categories { get; } = new();
    public Dictionary<Guid, TaskItem> Tasks { get; } = new();
    public Dictionary<Guid, ChecklistImport> Imports { get; } = new();
    public Dictionary<DateOnly, WeekPlan> WeekPlans { get; } = new();
    public Dictionary<Guid, RuleDefinition> Rules { get; } = new();
    public Dictionary<string, IntegrationSetting> Integrations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NotificationSchedule> NotificationSchedules { get; } = [];
    public NotificationSettings NotificationSettings { get; set; } = new();
}

public sealed class RoraQuestService(IRoraQuestStore store)
{
    private readonly object _gate = new();

    private static readonly Regex WeekHeadingRegex = new(
        @"^Week\s+(?<week>\d+)\s*:\s*(?<subcategory>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ChecklistImport CreateChecklistImport(string userId, BulkChecklistImportRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var draftItems = new List<ChecklistDraftItem>();
            int? weekNumber = null;
            string? subCategory = null;
            var order = 1;

            foreach (var rawLine in req.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var heading = WeekHeadingRegex.Match(line);
                if (heading.Success)
                {
                    weekNumber = int.Parse(heading.Groups["week"].Value, CultureInfo.InvariantCulture);
                    subCategory = heading.Groups["subcategory"].Value.Trim();
                    continue;
                }

                var normalizedText = NormalizeChecklistLine(line);
                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    continue;
                }

                draftItems.Add(new ChecklistDraftItem(Guid.NewGuid(), order++, normalizedText, weekNumber, subCategory));
            }

            var import = new ChecklistImport
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SourceType = "BulkText",
                RawText = req.RawText,
                CategoryName = req.CategoryName.Trim(),
                DaysPerWeek = req.DaysPerWeek ?? [],
                ParsedCount = draftItems.Count,
                CreatedAt = DateTimeOffset.UtcNow,
                DraftItems = draftItems
            };
            user.Imports[import.Id] = import;
            store.Save(userId, user);
            return import;
        }
    }

    public object? CommitChecklistImport(string userId, Guid importId, List<Guid>? selectedDraftIds)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Imports.TryGetValue(importId, out var import))
            {
                return null;
            }

            var selected = import.DraftItems
                .Where(d => selectedDraftIds is null || selectedDraftIds.Count == 0 || selectedDraftIds.Contains(d.Id))
                .ToList();

            var categoryId = EnsureCategory(user, userId, import.CategoryName, null)?.Id;
            var created = new List<TaskItem>();
            foreach (var item in selected)
            {
                var subCategoryId = EnsureCategory(user, userId, item.SubCategoryName, categoryId)?.Id;
                var task = new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = item.Text,
                    CategoryId = categoryId,
                    SubCategoryId = subCategoryId,
                    Status = TaskStatus.Todo,
                    PlannedWeekStart = ResolveWeekStart(item.WeekNumber),
                    AssignedTo = userId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                user.Tasks[task.Id] = task;
                created.Add(task);
            }

            store.Save(userId, user);
            return new { importId, createdCount = created.Count, tasks = created };
        }
    }

    public ChecklistImport? GetChecklistImport(string userId, Guid importId)
    {
        lock (_gate)
        {
            return GetUser(userId).Imports.GetValueOrDefault(importId);
        }
    }

    public List<Category> GetCategories(string userId)
    {
        lock (_gate) return GetUser(userId).Categories.Values.OrderBy(x => x.Name).ToList();
    }

    public Category CreateCategory(string userId, CreateCategoryRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var item = new Category
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = req.Name.Trim(),
                ParentCategoryId = req.ParentCategoryId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.Categories[item.Id] = item;
            store.Save(userId, user);
            return item;
        }
    }

    public Category? UpdateCategory(string userId, Guid categoryId, UpdateCategoryRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Categories.TryGetValue(categoryId, out var item))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(req.Name))
            {
                item.Name = req.Name.Trim();
            }
            item.ParentCategoryId = req.ParentCategoryId;
            store.Save(userId, user);
            return item;
        }
    }

    public bool DeleteCategory(string userId, Guid categoryId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var removed = user.Categories.Remove(categoryId);
            if (removed) store.Save(userId, user);
            return removed;
        }
    }

    public object GetCategoryTree(string userId)
    {
        lock (_gate)
        {
            var cats = GetUser(userId).Categories.Values.ToList();
            return cats.Select(c => new
            {
                c.Id,
                c.Name,
                c.ParentCategoryId,
                Children = cats.Where(x => x.ParentCategoryId == c.Id).Select(x => new { x.Id, x.Name }).ToList()
            }).Where(x => x.ParentCategoryId is null).ToList();
        }
    }

    public List<TaskItem> GetTasks(string userId, TaskQuery query)
    {
        lock (_gate)
        {
            var tasks = GetUser(userId).Tasks.Values.AsEnumerable();
            if (query.CategoryId is not null) tasks = tasks.Where(x => x.CategoryId == query.CategoryId);
            if (query.SubCategoryId is not null) tasks = tasks.Where(x => x.SubCategoryId == query.SubCategoryId);
            if (query.Status is not null) tasks = tasks.Where(x => x.Status == query.Status);
            if (query.WeekStart is not null) tasks = tasks.Where(x => x.PlannedWeekStart == query.WeekStart);
            if (query.From is not null) tasks = tasks.Where(x => x.StartAt?.Date >= query.From.Value.ToDateTime(TimeOnly.MinValue));
            if (query.To is not null) tasks = tasks.Where(x => x.EndAt?.Date <= query.To.Value.ToDateTime(TimeOnly.MaxValue));
            return tasks.OrderByDescending(x => x.UpdatedAt).ToList();
        }
    }

    public TaskItem CreateTask(string userId, CreateTaskRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = req.Title.Trim(),
                Description = req.Description,
                CategoryId = req.CategoryId,
                SubCategoryId = req.SubCategoryId,
                PlannedWeekStart = req.PlannedWeekStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                Status = req.Status ?? TaskStatus.Todo,
                StartAt = req.StartAt,
                EndAt = req.EndAt,
                DueDate = req.DueDate,
                Priority = req.Priority,
                AssignedTo = req.AssignedTo ?? userId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            user.Tasks[task.Id] = task;
            store.Save(userId, user);
            return task;
        }
    }

    public TaskItem? GetTask(string userId, Guid taskId)
    {
        lock (_gate) return GetUser(userId).Tasks.GetValueOrDefault(taskId);
    }

    public ServiceResult<TaskItem> UpdateTask(string userId, Guid taskId, UpdateTaskRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskItem>.NotFound();
            if (req.IfMatchVersion is not null && req.IfMatchVersion != task.RowVersion) return ServiceResult<TaskItem>.Conflict("Row version mismatch.");

            if (req.Title is not null) task.Title = req.Title.Trim();
            if (req.Description is not null) task.Description = req.Description;
            if (req.CategoryId is not null) task.CategoryId = req.CategoryId;
            task.SubCategoryId = req.SubCategoryId;
            if (req.PlannedWeekStart is not null) task.PlannedWeekStart = req.PlannedWeekStart.Value;
            if (req.StartAt is not null) task.StartAt = req.StartAt;
            if (req.EndAt is not null) task.EndAt = req.EndAt;
            if (req.DueDate is not null) task.DueDate = req.DueDate;
            if (req.Priority is not null) task.Priority = req.Priority;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskItem>.Ok(task);
        }
    }

    public ServiceResult<TaskItem> UpdateTaskStatus(string userId, Guid taskId, UpdateTaskStatusRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskItem>.NotFound();
            if (req.IfMatchVersion is not null && req.IfMatchVersion != task.RowVersion) return ServiceResult<TaskItem>.Conflict("Row version mismatch.");

            var old = task.Status;
            if (req.Status == TaskStatus.Done && task.SubSteps.Count > 0 && task.SubSteps.Any(s => !s.IsDone) && !req.OverrideIncompleteSubsteps)
            {
                return ServiceResult<TaskItem>.Validation("All substeps must be completed to mark task Done.");
            }

            task.Status = req.Status;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            task.RowVersion++;
            task.StatusEvents.Add(new TaskStatusEvent(Guid.NewGuid(), old, req.Status, DateTimeOffset.UtcNow));
            store.Save(userId, user);
            return ServiceResult<TaskItem>.Ok(task);
        }
    }

    public List<TaskSubStep>? GetSubsteps(string userId, Guid taskId)
    {
        lock (_gate)
        {
            var task = GetUser(userId).Tasks.GetValueOrDefault(taskId);
            return task?.SubSteps.OrderBy(x => x.OrderIndex).ToList();
        }
    }

    public ServiceResult<TaskSubStep> CreateSubstep(string userId, Guid taskId, CreateSubstepRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskSubStep>.NotFound();
            var nextOrder = task.SubSteps.Count == 0 ? 1 : task.SubSteps.Max(s => s.OrderIndex) + 1;
            var step = new TaskSubStep(Guid.NewGuid(), req.Title.Trim(), false, nextOrder, null, 1);
            task.SubSteps.Add(step);
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskSubStep>.Ok(step);
        }
    }

    public ServiceResult<TaskSubStep> UpdateSubstep(string userId, Guid taskId, Guid subStepId, UpdateSubstepRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskSubStep>.NotFound();
            var sub = task.SubSteps.FirstOrDefault(x => x.Id == subStepId);
            if (sub is null) return ServiceResult<TaskSubStep>.NotFound();
            if (req.IfMatchVersion is not null && req.IfMatchVersion != sub.RowVersion) return ServiceResult<TaskSubStep>.Conflict("Substep row version mismatch.");

            if (req.Title is not null) sub.Title = req.Title.Trim();
            if (req.IsDone is not null)
            {
                sub.IsDone = req.IsDone.Value;
                sub.CompletedAt = req.IsDone.Value ? DateTimeOffset.UtcNow : null;
            }
            sub.RowVersion++;
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskSubStep>.Ok(sub);
        }
    }

    public bool DeleteSubstep(string userId, Guid taskId, Guid subStepId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return false;
            var removed = task.SubSteps.RemoveAll(x => x.Id == subStepId) > 0;
            if (removed)
            {
                task.RowVersion++;
                store.Save(userId, user);
            }
            return removed;
        }
    }

    public ServiceResult<TaskLink> CreateLink(string userId, Guid taskId, CreateLinkRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskLink>.NotFound();
            var link = new TaskLink(Guid.NewGuid(), req.Url, req.Label, req.SourceType);
            task.Links.Add(link);
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskLink>.Ok(link);
        }
    }

    public ServiceResult<TaskLink> UpdateLink(string userId, Guid taskId, Guid linkId, UpdateLinkRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskLink>.NotFound();
            var link = task.Links.FirstOrDefault(x => x.Id == linkId);
            if (link is null) return ServiceResult<TaskLink>.NotFound();
            link.Url = req.Url ?? link.Url;
            link.Label = req.Label ?? link.Label;
            link.SourceType = req.SourceType ?? link.SourceType;
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskLink>.Ok(link);
        }
    }

    public bool DeleteLink(string userId, Guid taskId, Guid linkId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return false;
            var removed = task.Links.RemoveAll(x => x.Id == linkId) > 0;
            if (removed)
            {
                task.RowVersion++;
                store.Save(userId, user);
            }
            return removed;
        }
    }

    public ServiceResult<TaskAsset> CreateAsset(string userId, Guid taskId, CreateAssetRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskAsset>.NotFound();
            var asset = new TaskAsset(Guid.NewGuid(), req.AssetType, req.StoragePathOrUrl, req.FileName, req.ContentType, req.SizeBytes, DateTimeOffset.UtcNow);
            task.Assets.Add(asset);
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskAsset>.Ok(asset);
        }
    }

    public object MoveSpillover(string userId, SpilloverRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var moved = new List<object>();
            foreach (var taskId in req.TaskIds.Distinct())
            {
                if (!user.Tasks.TryGetValue(taskId, out var task)) continue;
                var from = task.PlannedWeekStart;
                task.PlannedWeekStart = req.ToWeekStart;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                task.RowVersion++;
                var ev = new TaskSpilloverEvent(Guid.NewGuid(), from, req.ToWeekStart, req.Reason, DateTimeOffset.UtcNow);
                task.Spillovers.Add(ev);
                moved.Add(new { taskId, from, to = req.ToWeekStart, req.Reason });
            }
            if (moved.Count > 0) store.Save(userId, user);
            return new { movedCount = moved.Count, moved };
        }
    }

    public List<object> GetSpilloverHistory(string userId, Guid? taskId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var tasks = taskId is null ? user.Tasks.Values : user.Tasks.Values.Where(t => t.Id == taskId.Value);
            return tasks.SelectMany(t => t.Spillovers.Select(s => new
            {
                taskId = t.Id,
                t.Title,
                s.Id,
                s.FromWeekStart,
                s.ToWeekStart,
                s.Reason,
                s.MovedAt
            })).OrderByDescending(x => x.MovedAt).Cast<object>().ToList();
        }
    }

    public WeekPlan? GetWeekPlan(string userId, DateOnly weekStart)
    {
        lock (_gate) return GetUser(userId).WeekPlans.GetValueOrDefault(weekStart);
    }

    public WeekPlan UpsertWeekPlan(string userId, DateOnly weekStart, UpsertWeekPlanRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.WeekPlans.TryGetValue(weekStart, out var plan))
            {
                plan = new WeekPlan(weekStart, req.WorkloadMode, req.Notes, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                user.WeekPlans[weekStart] = plan;
            }
            else
            {
                plan.WorkloadMode = req.WorkloadMode;
                plan.Notes = req.Notes;
                plan.UpdatedAt = DateTimeOffset.UtcNow;
            }
            store.Save(userId, user);
            return plan;
        }
    }

    public List<RuleDefinition> GetRules(string userId)
    {
        lock (_gate) return GetUser(userId).Rules.Values.OrderBy(x => x.Name).ToList();
    }

    public RuleDefinition CreateRule(string userId, CreateRuleRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var rule = new RuleDefinition
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                RuleType = req.RuleType,
                Severity = req.Severity,
                IsActive = req.IsActive,
                RuleConfigJson = req.RuleConfigJson,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            user.Rules[rule.Id] = rule;
            store.Save(userId, user);
            return rule;
        }
    }

    public RuleDefinition? UpdateRule(string userId, Guid ruleId, UpdateRuleRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Rules.TryGetValue(ruleId, out var rule)) return null;
            rule.Name = req.Name ?? rule.Name;
            rule.RuleType = req.RuleType ?? rule.RuleType;
            rule.RuleConfigJson = req.RuleConfigJson ?? rule.RuleConfigJson;
            rule.IsActive = req.IsActive ?? rule.IsActive;
            rule.Severity = req.Severity ?? rule.Severity;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            store.Save(userId, user);
            return rule;
        }
    }

    public object EvaluateRules(string userId, EvaluateRulesRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var ruleResults = new List<object>();
            foreach (var r in user.Rules.Values.Where(x => x.IsActive))
            {
                var severity = r.Severity;
                var status = "pass";
                var message = "Rule satisfied.";
                if (r.RuleType.Contains("interruption", StringComparison.OrdinalIgnoreCase) && req.InterruptedByPriorityWork)
                {
                    severity = RuleSeverity.AutoFix;
                    status = "auto-fix";
                    message = "Interruption rule applied: switch week to Red and add one review task.";
                }
                ruleResults.Add(new { r.Id, r.Name, severity, status, message });
            }
            return new { evaluatedCount = ruleResults.Count, results = ruleResults };
        }
    }

    public object SyncCalendarTask(string userId, Guid taskId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return new { synced = false, reason = "task_not_found" };
            task.CalendarEventId = $"outlook-{task.Id:N}";
            task.RowVersion++;
            store.Save(userId, user);
            return new { synced = true, task.Id, task.CalendarEventId };
        }
    }

    public object UnsyncCalendarTask(string userId, Guid taskId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return new { unsynced = false, reason = "task_not_found" };
            task.CalendarEventId = null;
            task.RowVersion++;
            store.Save(userId, user);
            return new { unsynced = true, task.Id };
        }
    }

    public object SyncCalendarBatch(string userId, List<Guid> taskIds)
    {
        var results = taskIds.Select(id => SyncCalendarTask(userId, id)).ToList();
        return new { count = results.Count, results };
    }

    public object GetCalendarConflicts(string userId)
    {
        lock (_gate)
        {
            var tasks = GetUser(userId).Tasks.Values.Where(x => x.StartAt is not null && x.EndAt is not null).ToList();
            var conflicts = new List<object>();
            for (var i = 0; i < tasks.Count; i++)
            {
                for (var j = i + 1; j < tasks.Count; j++)
                {
                    if (tasks[i].StartAt < tasks[j].EndAt && tasks[j].StartAt < tasks[i].EndAt)
                    {
                        conflicts.Add(new { taskA = tasks[i].Id, taskB = tasks[j].Id, reason = "time_overlap" });
                    }
                }
            }
            return conflicts;
        }
    }

    public NotificationSettings GetNotificationSettings(string userId)
    {
        lock (_gate) return GetUser(userId).NotificationSettings;
    }

    public NotificationSettings UpdateNotificationSettings(string userId, UpdateNotificationSettingsRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            user.NotificationSettings = new NotificationSettings
            {
                DailyDigestTime = req.DailyDigestTime ?? user.NotificationSettings.DailyDigestTime,
                EveningReminderTime = req.EveningReminderTime ?? user.NotificationSettings.EveningReminderTime,
                TeamsDestination = req.TeamsDestination ?? user.NotificationSettings.TeamsDestination
            };
            store.Save(userId, user);
            return user.NotificationSettings;
        }
    }

    public object TriggerDailyDigest(string userId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var schedule = new NotificationSchedule(Guid.NewGuid(), null, "Teams", DateTimeOffset.UtcNow, "Triggered", DateTimeOffset.UtcNow);
            user.NotificationSchedules.Add(schedule);
            store.Save(userId, user);
            return schedule;
        }
    }

    public List<NotificationSchedule> GetNotificationSchedules(string userId)
    {
        lock (_gate) return GetUser(userId).NotificationSchedules.OrderByDescending(x => x.ScheduledAt).ToList();
    }

    public List<IntegrationSetting> GetIntegrations(string userId)
    {
        lock (_gate) return GetUser(userId).Integrations.Values.ToList();
    }

    public IntegrationSetting ConnectMicrosoft(string userId, ConnectMicrosoftRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var item = new IntegrationSetting
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Provider = req.Provider,
                AccountIdentifier = req.AccountIdentifier,
                AccessTokenRef = "token-ref",
                RefreshTokenRef = "refresh-ref",
                TokenExpiryUtc = DateTimeOffset.UtcNow.AddHours(1),
                IsConnected = true,
                LastSyncAt = DateTimeOffset.UtcNow
            };
            user.Integrations[item.Provider] = item;
            store.Save(userId, user);
            return item;
        }
    }

    public object HandleMicrosoftCallback(string userId, MicrosoftCallbackRequest req)
    {
        return new { handled = true, userId, req.Provider, req.Code };
    }

    public object DisconnectIntegration(string userId, string provider)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Integrations.TryGetValue(provider, out var setting))
            {
                return new { disconnected = false, reason = "not_found" };
            }

            setting.IsConnected = false;
            store.Save(userId, user);
            return new { disconnected = true, provider };
        }
    }

    public object TestIntegration(string userId, string provider)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var ok = user.Integrations.TryGetValue(provider, out var setting) && setting.IsConnected;
            return new { provider, ok };
        }
    }

    public object GetProgressReport(string userId, ReportWindow window)
    {
        lock (_gate)
        {
            var tasks = FilterByWindow(GetUser(userId).Tasks.Values, window).ToList();
            var progressValues = tasks.Select(GetTaskProgress).ToList();
            var avg = progressValues.Count == 0 ? 0 : progressValues.Average();
            return new { window, plannedTasks = tasks.Count, avgProgressPercent = Math.Round(avg, 2) };
        }
    }

    public object GetTimelineReport(string userId, ReportWindow window)
    {
        lock (_gate)
        {
            var tasks = FilterByWindow(GetUser(userId).Tasks.Values, window)
                .OrderBy(x => x.PlannedWeekStart)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Status,
                    t.PlannedWeekStart,
                    ProgressPercent = GetTaskProgress(t)
                }).ToList();
            return new { window, items = tasks };
        }
    }

    public object GetScorecard(string userId, ReportWindow window)
    {
        lock (_gate)
        {
            var tasks = FilterByWindow(GetUser(userId).Tasks.Values, window).ToList();
            var planned = tasks.Count;
            var completed = tasks.Count(t => GetTaskProgress(t) >= 100);
            var carryOverMoved = tasks.Count(t => GetTaskProgress(t) < 100 && t.Spillovers.Count > 0);
            var carryOverPending = tasks.Count(t => GetTaskProgress(t) < 100 && t.Spillovers.Count == 0);
            var rate = planned == 0 ? 0 : (double)completed / planned * 100;
            return new
            {
                window,
                plannedTasks = planned,
                completedTasks = completed,
                completionRatePercent = Math.Round(rate, 2),
                carryOverMoved,
                carryOverPending
            };
        }
    }

    public object GetStreaks(string userId)
    {
        lock (_gate)
        {
            var completedDays = GetUser(userId).Tasks.Values
                .Where(t => GetTaskProgress(t) >= 100)
                .Select(t => t.UpdatedAt.Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var current = 0;
            var day = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            while (completedDays.Contains(day.ToDateTime(TimeOnly.MinValue)))
            {
                current++;
                day = day.AddDays(-1);
            }
            return new { currentStreakDays = current, totalCompletedDays = completedDays.Count };
        }
    }

    public object GetConsistency(string userId)
    {
        lock (_gate)
        {
            var tasks = GetUser(userId).Tasks.Values.ToList();
            var avg = tasks.Count == 0 ? 0 : tasks.Average(GetTaskProgress);
            return new { avgProgressPercent = Math.Round(avg, 2), taskCount = tasks.Count };
        }
    }

    public object GetPlanningRecommendation(string userId)
    {
        lock (_gate)
        {
            var tasks = GetUser(userId).Tasks.Values.ToList();
            var completionRate = tasks.Count == 0 ? 0 : (double)tasks.Count(t => GetTaskProgress(t) >= 100) / tasks.Count * 100;
            var suggestion = completionRate switch
            {
                >= 80 => WorkloadMode.Green,
                >= 50 => WorkloadMode.Yellow,
                _ => WorkloadMode.Red
            };
            return new { profile = "Balanced", suggestedMode = suggestion, completionRatePercent = Math.Round(completionRate, 2) };
        }
    }

    private static IEnumerable<TaskItem> FilterByWindow(IEnumerable<TaskItem> tasks, ReportWindow window)
    {
        if (window.From is null && window.To is null) return tasks;
        return tasks.Where(t =>
        {
            var date = t.PlannedWeekStart.ToDateTime(TimeOnly.MinValue).Date;
            if (window.From is not null && date < window.From.Value.ToDateTime(TimeOnly.MinValue).Date) return false;
            if (window.To is not null && date > window.To.Value.ToDateTime(TimeOnly.MinValue).Date) return false;
            return true;
        });
    }

    private static double GetTaskProgress(TaskItem task)
    {
        if (task.SubSteps.Count > 0)
        {
            return Math.Round((double)task.SubSteps.Count(s => s.IsDone) / task.SubSteps.Count * 100, 2);
        }
        return task.Status switch
        {
            TaskStatus.Done => 100,
            TaskStatus.Cancelled => 0,
            TaskStatus.Skipped => 0,
            _ => 0
        };
    }

    private UserData GetUser(string userId) => store.Load(userId);

    private static string NormalizeChecklistLine(string line)
    {
        var normalized = Regex.Replace(line, @"^[-*]\s+", "");
        normalized = Regex.Replace(normalized, @"^\d+[\)\.\-\s]+", "");
        return normalized.Trim();
    }

    private static DateOnly ResolveWeekStart(int? weekNumber)
    {
        var baselineWeek = StartOfWeek(DateOnly.FromDateTime(DateTime.UtcNow.Date), DayOfWeek.Monday);
        if (weekNumber is > 1)
        {
            return baselineWeek.AddDays((weekNumber.Value - 1) * 7);
        }

        return baselineWeek;
    }

    private static DateOnly StartOfWeek(DateOnly value, DayOfWeek startOfWeek)
    {
        var diff = (7 + (value.DayOfWeek - startOfWeek)) % 7;
        return value.AddDays(-diff);
    }

    private static Category? EnsureCategory(UserData user, string userId, string? name, Guid? parentCategoryId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var normalized = name.Trim();
        var existing = user.Categories.Values.FirstOrDefault(c =>
            c.ParentCategoryId == parentCategoryId &&
            string.Equals(c.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = normalized,
            ParentCategoryId = parentCategoryId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.Categories[category.Id] = category;
        return category;
    }
}

public sealed class ServiceResult<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public int StatusCode { get; init; }

    public static ServiceResult<T> Ok(T value) => new() { Value = value, StatusCode = 200 };
    public static ServiceResult<T> NotFound() => new() { Error = "Not found.", StatusCode = 404 };
    public static ServiceResult<T> Conflict(string error) => new() { Error = error, StatusCode = 409 };
    public static ServiceResult<T> Validation(string error) => new() { Error = error, StatusCode = 400 };

    public IResult ToResult() =>
        StatusCode switch
        {
            200 => Results.Ok(Value),
            404 => Results.NotFound(new { error = Error }),
            409 => Results.Conflict(new { error = Error }),
            _ => Results.BadRequest(new { error = Error })
        };
}

public sealed class Category
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ParentCategoryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TaskItem
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public DateOnly PlannedWeekStart { get; set; }
    public string AssignedTo { get; set; } = "";
    public string? Priority { get; set; }
    public TaskStatus Status { get; set; }
    public DateOnly? DueDate { get; set; }
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
    public int RowVersion { get; set; } = 1;
    public List<TaskSubStep> SubSteps { get; } = [];
    public List<TaskLink> Links { get; } = [];
    public List<TaskAsset> Assets { get; } = [];
    public List<TaskStatusEvent> StatusEvents { get; } = [];
    public List<TaskSpilloverEvent> Spillovers { get; } = [];
}

public sealed class TaskSubStep(Guid id, string title, bool isDone, int orderIndex, DateTimeOffset? completedAt, int rowVersion)
{
    public Guid Id { get; set; } = id;
    public string Title { get; set; } = title;
    public bool IsDone { get; set; } = isDone;
    public int OrderIndex { get; set; } = orderIndex;
    public DateTimeOffset? CompletedAt { get; set; } = completedAt;
    public int RowVersion { get; set; } = rowVersion;
}

public sealed class TaskLink(Guid id, string url, string? label, string? sourceType)
{
    public Guid Id { get; set; } = id;
    public string Url { get; set; } = url;
    public string? Label { get; set; } = label;
    public string? SourceType { get; set; } = sourceType;
}

public sealed class TaskAsset(Guid id, string assetType, string storagePathOrUrl, string fileName, string? contentType, long? sizeBytes, DateTimeOffset createdAt)
{
    public Guid Id { get; set; } = id;
    public string AssetType { get; set; } = assetType;
    public string StoragePathOrUrl { get; set; } = storagePathOrUrl;
    public string FileName { get; set; } = fileName;
    public string? ContentType { get; set; } = contentType;
    public long? SizeBytes { get; set; } = sizeBytes;
    public DateTimeOffset CreatedAt { get; set; } = createdAt;
}

public sealed class TaskStatusEvent(Guid id, TaskStatus fromStatus, TaskStatus toStatus, DateTimeOffset changedAt)
{
    public Guid Id { get; set; } = id;
    public TaskStatus FromStatus { get; set; } = fromStatus;
    public TaskStatus ToStatus { get; set; } = toStatus;
    public DateTimeOffset ChangedAt { get; set; } = changedAt;
}

public sealed class TaskSpilloverEvent(Guid id, DateOnly fromWeekStart, DateOnly toWeekStart, string reason, DateTimeOffset movedAt)
{
    public Guid Id { get; set; } = id;
    public DateOnly FromWeekStart { get; set; } = fromWeekStart;
    public DateOnly ToWeekStart { get; set; } = toWeekStart;
    public string Reason { get; set; } = reason;
    public DateTimeOffset MovedAt { get; set; } = movedAt;
}

public sealed class ChecklistImport
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string RawText { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public List<string> DaysPerWeek { get; set; } = [];
    public int ParsedCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ChecklistDraftItem> DraftItems { get; set; } = [];
}

public sealed class ChecklistDraftItem(Guid id, int order, string text, int? weekNumber, string? subCategoryName)
{
    public Guid Id { get; set; } = id;
    public int Order { get; set; } = order;
    public string Text { get; set; } = text;
    public int? WeekNumber { get; set; } = weekNumber;
    public string? SubCategoryName { get; set; } = subCategoryName;
}

public sealed class WeekPlan(DateOnly weekStartDate, WorkloadMode workloadMode, string? notes, DateTimeOffset createdAt, DateTimeOffset updatedAt)
{
    public DateOnly WeekStartDate { get; set; } = weekStartDate;
    public WorkloadMode WorkloadMode { get; set; } = workloadMode;
    public string? Notes { get; set; } = notes;
    public DateTimeOffset CreatedAt { get; set; } = createdAt;
    public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
}

public sealed class RuleDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string RuleType { get; set; } = "";
    public RuleSeverity Severity { get; set; } = RuleSeverity.Warning;
    public bool IsActive { get; set; } = true;
    public string? RuleConfigJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class NotificationSettings
{
    public TimeOnly DailyDigestTime { get; set; } = new(9, 0);
    public TimeOnly EveningReminderTime { get; set; } = new(19, 30);
    public string TeamsDestination { get; set; } = "personal-chat";
}

public sealed class NotificationSchedule(Guid id, Guid? taskId, string channel, DateTimeOffset scheduledAt, string status, DateTimeOffset? sentAt)
{
    public Guid Id { get; set; } = id;
    public Guid? TaskId { get; set; } = taskId;
    public string Channel { get; set; } = channel;
    public DateTimeOffset ScheduledAt { get; set; } = scheduledAt;
    public string Status { get; set; } = status;
    public DateTimeOffset? SentAt { get; set; } = sentAt;
}

public sealed class IntegrationSetting
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

public sealed class ReportWindow
{
    public string RangeType { get; set; } = "Weekly";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    public static ReportWindow FromHttp(IQueryCollection q)
    {
        DateOnly? ParseDate(string key) =>
            DateOnly.TryParseExact(q[key].FirstOrDefault(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : null;

        return new ReportWindow
        {
            RangeType = q["rangeType"].FirstOrDefault() ?? "Weekly",
            From = ParseDate("from"),
            To = ParseDate("to")
        };
    }
}

public sealed class TaskQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateOnly? WeekStart { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public TaskStatus? Status { get; set; }

    public static TaskQuery FromHttp(IQueryCollection q)
    {
        Guid? ParseGuid(string key) => Guid.TryParse(q[key].FirstOrDefault(), out var g) ? g : null;
        DateOnly? ParseDate(string key) => DateOnly.TryParse(q[key].FirstOrDefault(), out var d) ? d : null;
        TaskStatus? ParseStatus()
        {
            return Enum.TryParse<TaskStatus>(q["status"].FirstOrDefault(), true, out var s) ? s : null;
        }

        return new TaskQuery
        {
            From = ParseDate("from"),
            To = ParseDate("to"),
            WeekStart = ParseDate("weekStart"),
            CategoryId = ParseGuid("categoryId"),
            SubCategoryId = ParseGuid("subCategoryId"),
            Status = ParseStatus()
        };
    }
}

public sealed record BulkChecklistImportRequest(string RawText, string CategoryName, List<string>? DaysPerWeek);
public sealed record CommitChecklistImportRequest(List<Guid> SelectedDraftIds);
public sealed record CreateCategoryRequest(string Name, Guid? ParentCategoryId);
public sealed record UpdateCategoryRequest(string? Name, Guid? ParentCategoryId);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    Guid? CategoryId,
    Guid? SubCategoryId,
    DateOnly? PlannedWeekStart,
    DateOnly? DueDate,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Priority,
    TaskStatus? Status,
    string? AssignedTo);

public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    Guid? CategoryId,
    Guid? SubCategoryId,
    DateOnly? PlannedWeekStart,
    DateOnly? DueDate,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Priority,
    int? IfMatchVersion);

public sealed record UpdateTaskStatusRequest(TaskStatus Status, bool OverrideIncompleteSubsteps, int? IfMatchVersion);
public sealed record CreateSubstepRequest(string Title);
public sealed record UpdateSubstepRequest(string? Title, bool? IsDone, int? IfMatchVersion);
public sealed record CreateLinkRequest(string Url, string? Label, string? SourceType);
public sealed record UpdateLinkRequest(string? Url, string? Label, string? SourceType);
public sealed record CreateAssetRequest(string AssetType, string StoragePathOrUrl, string FileName, string? ContentType, long? SizeBytes);
public sealed record SpilloverRequest(List<Guid> TaskIds, DateOnly ToWeekStart, string Reason);

public sealed record UpsertWeekPlanRequest(WorkloadMode WorkloadMode, string? Notes);
public sealed record CreateRuleRequest(string Name, string RuleType, RuleSeverity Severity, bool IsActive, string? RuleConfigJson);
public sealed record UpdateRuleRequest(string? Name, string? RuleType, RuleSeverity? Severity, bool? IsActive, string? RuleConfigJson);
public sealed record EvaluateRulesRequest(bool InterruptedByPriorityWork);

public sealed record SyncBatchRequest(List<Guid> TaskIds);
public sealed record UpdateNotificationSettingsRequest(TimeOnly? DailyDigestTime, TimeOnly? EveningReminderTime, string? TeamsDestination);
public sealed record ConnectMicrosoftRequest(string Provider, string AccountIdentifier);
public sealed record MicrosoftCallbackRequest(string Provider, string Code);
