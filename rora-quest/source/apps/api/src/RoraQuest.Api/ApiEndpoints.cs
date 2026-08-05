using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

public static class ApiEndpoints
{
    public static void MapRoraQuestEndpoints(this WebApplication app, bool oauthEnabled)
    {
        var auth = app.MapGroup("/api/auth");
        MapAuth(auth, oauthEnabled);

        var api = oauthEnabled
            ? app.MapGroup("/api").RequireAuthorization()
            : app.MapGroup("/api");

        MapChecklist(api);
        MapCategories(api);
        MapTasks(api);
        MapPlanningRules(api);
        MapCalendar(api);
        MapNotifications(api);
        MapIntegrationSettings(api);
        MapReports(api);
    }

    private static void MapAuth(RouteGroupBuilder auth, bool oauthEnabled)
    {
        auth.MapGet("/login", (HttpContext http, string? returnUrl) =>
        {
            if (!oauthEnabled)
            {
                return Results.Problem(
                    title: "OAuth is not configured.",
                    detail: "Set EntraAuth__ClientId and EntraAuth__ClientSecret to enable Microsoft sign-in.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            var props = new AuthenticationProperties { RedirectUri = redirect };
            return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        auth.MapGet("/logout", (string? returnUrl) =>
        {
            if (!oauthEnabled)
            {
                return Results.NoContent();
            }

            var redirect = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
            var props = new AuthenticationProperties { RedirectUri = redirect };
            return Results.SignOut(
                props,
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        }).AllowAnonymous();

        var meEndpoint = auth.MapGet("/me", (HttpContext http) =>
        {
            var userId = oauthEnabled ? AuthIdentity.ResolveUserId(http.User) : UserScope.GetUserId(http);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var email =
                http.User.FindFirstValue("preferred_username") ??
                http.User.FindFirstValue(ClaimTypes.Upn) ??
                http.User.FindFirstValue(ClaimTypes.Email);
            var displayName =
                http.User.FindFirstValue("name") ??
                http.User.FindFirstValue(ClaimTypes.GivenName) ??
                http.User.FindFirstValue(ClaimTypes.Name);
            return Results.Ok(new AuthMeResponse(userId, displayName, email));
        });

        if (oauthEnabled)
        {
            meEndpoint.RequireAuthorization();
        }
        else
        {
            meEndpoint.AllowAnonymous();
        }
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
            var result = svc.CommitChecklistImport(userId, importId, req.SelectedDraftIds, req.SelectedConfidenceIds, req.StartWeekDate);
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
            try
            {
                return Results.Ok(svc.CreateTask(UserScope.GetUserId(http), req));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Must be registered before /{taskId:guid} to prevent route ambiguity
        group.MapGet("/week-summary", (HttpContext http, RoraQuestService svc) =>
        {
            var weekStartStr = http.Request.Query["weekStart"].FirstOrDefault();
            if (!DateOnly.TryParse(weekStartStr, out var weekStart))
                return Results.BadRequest("weekStart query parameter is required (yyyy-MM-dd).");
            return Results.Ok(svc.GetWeekActualHoursSummary(UserScope.GetUserId(http), weekStart));
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

        group.MapDelete("/{taskId:guid}", (Guid taskId, RoraQuestService svc, HttpContext http) =>
        {
            var deleted = svc.DeleteTask(UserScope.GetUserId(http), taskId);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/bulk-delete", (BulkDeleteTasksRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var ids = req.TaskIds ?? [];
            if (ids.Count == 0) return Results.BadRequest("No task ids provided.");
            var deleted = svc.DeleteTasks(UserScope.GetUserId(http), ids);
            return Results.Ok(new { deleted });
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

        group.MapPost("/{taskId:guid}/assets", async (Guid taskId, HttpRequest request, RoraQuestService svc, HttpContext http, CancellationToken cancellationToken) =>
        {
            var userId = UserScope.GetUserId(http);

            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (file is null)
                {
                    return Results.BadRequest("A file upload is required.");
                }

                var assetType = form["assetType"].ToString();
                var fileName = string.IsNullOrWhiteSpace(form["fileName"].ToString()) ? file.FileName : form["fileName"].ToString();
                var contentType = string.IsNullOrWhiteSpace(form["contentType"].ToString()) ? file.ContentType : form["contentType"].ToString();
                var sizeBytes = long.TryParse(form["sizeBytes"], out var parsedSize) ? parsedSize : file.Length;

                await using var stream = file.OpenReadStream();
                var result = await svc.CreateAssetAsync(userId, taskId, assetType, fileName, contentType, sizeBytes, stream, cancellationToken);
                return result.ToResult();
            }

            var req = await request.ReadFromJsonAsync<CreateAssetRequest>(cancellationToken);
            if (req is null)
            {
                return Results.BadRequest("Request body is required.");
            }

            var jsonResult = await svc.CreateAssetAsync(userId, taskId, req, cancellationToken);
            return jsonResult.ToResult();
        });

        group.MapDelete("/{taskId:guid}/assets/{assetId:guid}", async (Guid taskId, Guid assetId, RoraQuestService svc, HttpContext http, CancellationToken cancellationToken) =>
        {
            return await svc.DeleteAssetAsync(UserScope.GetUserId(http), taskId, assetId, cancellationToken) ? Results.NoContent() : Results.NotFound();
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
            if (result is null)
            {
                var now = DateTimeOffset.UtcNow;
                return Results.Ok(new WeekPlan(week, WorkloadMode.Yellow, null, now, now));
            }

            return Results.Ok(result);
        });

        api.MapPut("/week-plans/{weekStart}", (string weekStart, UpsertWeekPlanRequest req, RoraQuestService svc, HttpContext http) =>
        {
            if (!DateOnly.TryParse(weekStart, out var week))
            {
                return Results.BadRequest("Invalid weekStart date.");
            }

            return Results.Ok(svc.UpsertWeekPlan(UserScope.GetUserId(http), week, req));
        });

        api.MapGet("/week-confidence/{weekStart}", (string weekStart, RoraQuestService svc, HttpContext http) =>
        {
            if (!DateOnly.TryParse(weekStart, out var week))
            {
                return Results.BadRequest("Invalid weekStart date.");
            }

            return Results.Ok(svc.GetWeekConfidence(UserScope.GetUserId(http), week));
        });

        api.MapPatch("/week-confidence/{itemId:guid}", (Guid itemId, ToggleWeekConfidenceRequest req, RoraQuestService svc, HttpContext http) =>
        {
            var result = svc.ToggleWeekConfidence(UserScope.GetUserId(http), itemId, req.IsDone);
            return result is null ? Results.NotFound() : Results.Ok(result);
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
        group.MapGet("/daily-digest/payload", (HttpContext http, RoraQuestService svc) =>
        {
            var dateQuery = http.Request.Query["date"].FirstOrDefault();
            DateOnly? onDate = null;
            if (!string.IsNullOrWhiteSpace(dateQuery))
            {
                if (!DateOnly.TryParse(dateQuery, out var parsed))
                {
                    return Results.BadRequest("Invalid date. Use yyyy-MM-dd.");
                }
                onDate = parsed;
            }

            return Results.Ok(svc.GetDailyDigestPayload(UserScope.GetUserId(http), onDate));
        });
        group.MapPost("/daily-digest/trigger", async (DailyDigestDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendForUserAsync(UserScope.GetUserId(http), null, true, ct)));
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
    public static string GetUserId(HttpContext ctx)
    {
        return AuthIdentity.ResolveUserId(ctx.User)
            ?? ctx.Request.Headers["X-User-Id"].FirstOrDefault()
            ?? "demo-user";
    }
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

public enum Difficulty
{
    Easy,
    Medium,
    Hard
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
    public Dictionary<Guid, WeekConfidenceItem> ConfidenceItems { get; } = new();
    public Dictionary<DateOnly, WeekPlan> WeekPlans { get; } = new();
    public Dictionary<Guid, RuleDefinition> Rules { get; } = new();
    public Dictionary<string, IntegrationSetting> Integrations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NotificationSchedule> NotificationSchedules { get; } = [];
    public NotificationSettings NotificationSettings { get; set; } = new();
}

public sealed class RoraQuestService(IRoraQuestStore store, ITaskAssetStorage? assetStorage = null)
{
    private readonly ITaskAssetStorage _assetStorage = assetStorage ?? new NullTaskAssetStorage();
    private readonly object _gate = new();
    private const string DsaRootCategoryName = "DSA";

    private static readonly Regex WeekHeadingRegex = new(
        @"^Week\s+(?<week>\d+)\s*:\s*(?<subcategory>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MonthHeadingRegex = new(
        @"^Month\s+\d+\s*:\s*.+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConfidenceHeadingRegex = new(
        @"^Pattern\s+confidence\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ChecklistImport CreateChecklistImport(string userId, BulkChecklistImportRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var draftItems = new List<ChecklistDraftItem>();
            var confidenceItems = new List<ConfidenceDraftItem>();
            int? weekNumber = null;
            string? subCategory = null;
            string? monthLabel = null;
            var inConfidence = false;
            var order = 1;
            var confOrder = 1;

            foreach (var rawLine in req.RawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Month headings are visual-only: recognized, tracked for preview grouping, but not turned into tasks.
                if (MonthHeadingRegex.IsMatch(line))
                {
                    monthLabel = line;
                    inConfidence = false;
                    continue;
                }

                var heading = WeekHeadingRegex.Match(line);
                if (heading.Success)
                {
                    weekNumber = int.Parse(heading.Groups["week"].Value, CultureInfo.InvariantCulture);
                    subCategory = heading.Groups["subcategory"].Value.Trim();
                    inConfidence = false;
                    continue;
                }

                // "Pattern confidence:" starts a self-assessment block for the current week.
                if (ConfidenceHeadingRegex.IsMatch(line))
                {
                    inConfidence = true;
                    continue;
                }

                var normalizedText = NormalizeChecklistLine(line);
                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    continue;
                }

                if (inConfidence)
                {
                    confidenceItems.Add(new ConfidenceDraftItem(Guid.NewGuid(), confOrder++, normalizedText, weekNumber, subCategory, monthLabel));
                }
                else
                {
                    draftItems.Add(new ChecklistDraftItem(Guid.NewGuid(), order++, normalizedText, weekNumber, subCategory, monthLabel));
                }
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
                DraftItems = draftItems,
                ConfidenceItems = confidenceItems
            };
            user.Imports[import.Id] = import;
            store.Save(userId, user);
            return import;
        }
    }

    public object? CommitChecklistImport(string userId, Guid importId, List<Guid>? selectedDraftIds, List<Guid>? selectedConfidenceIds, DateOnly? startWeekDate = null)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Imports.TryGetValue(importId, out var import))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var selected = import.DraftItems
                .Where(d => selectedDraftIds is null || selectedDraftIds.Count == 0 || selectedDraftIds.Contains(d.Id))
                .OrderBy(d => d.WeekNumber ?? int.MaxValue)
                .ThenBy(d => d.Order)
                .ToList();

            var baselineWeek = StartOfWeek(
                startWeekDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                DayOfWeek.Monday);

            var categoryId = EnsureCategory(user, userId, import.CategoryName, null)?.Id;
            // Cap each calendar week at 3 problems (one per Mon/Wed/Fri slot). When a parsed
            // week yields more than 3 problems, the overflow cascades forward into the next
            // week(s) that still have a free slot, never landing earlier than the problem's
            // own target week.
            var weekFill = new Dictionary<DateOnly, int>();
            // Seed the per-week fill from tasks already scheduled so re-committing an import
            // (or importing into weeks that already hold tasks) cascades past filled weeks
            // instead of stacking duplicates on the same day.
            foreach (var existing in user.Tasks.Values)
            {
                if (existing.PlannedDate is null) continue;
                var wk = existing.PlannedWeekStart;
                weekFill[wk] = (weekFill.TryGetValue(wk, out var c) ? c : 0) + 1;
            }
            // Guard against re-import: skip any problem whose title already exists in this
            // category so committing the same checklist twice does not duplicate tasks.
            var existingTitles = new HashSet<string>(
                user.Tasks.Values.Where(t => t.CategoryId == categoryId).Select(t => t.Title),
                StringComparer.OrdinalIgnoreCase);
            var created = new List<TaskItem>();
            foreach (var item in selected)
            {
                if (!existingTitles.Add(item.Text))
                {
                    continue;
                }
                var subCategoryId = EnsureCategory(user, userId, item.SubCategoryName, categoryId)?.Id;
                var targetWeek = ResolveWeekStart(item.WeekNumber, baselineWeek);
                var weekStart = NextWeekWithFreeSlot(targetWeek, weekFill);
                var slot = weekFill.TryGetValue(weekStart, out var f) ? f : 0;
                weekFill[weekStart] = slot + 1;
                var plannedDate = weekStart.AddDays(ScheduleDayOffsets[slot]);
                var task = new TaskItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = item.Text,
                    CategoryId = categoryId,
                    SubCategoryId = subCategoryId,
                    Status = TaskStatus.Todo,
                    PlannedWeekStart = weekStart,
                    PlannedDate = plannedDate,
                    AssignedTo = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                if (IsDsaFlowCategory(user, task.CategoryId, task.SubCategoryId))
                {
                    SeedStandardSubSteps(task);
                }
                user.Tasks[task.Id] = task;
                created.Add(task);
            }

            var selectedConfidence = import.ConfidenceItems
                .Where(c => selectedConfidenceIds is null || selectedConfidenceIds.Count == 0 || selectedConfidenceIds.Contains(c.Id))
                .ToList();

            var createdConfidence = new List<WeekConfidenceItem>();
            var orderByWeek = new Dictionary<DateOnly, int>();
            // Guard against re-import: skip confidence items identical to ones already stored
            // (same week + label + text) so repeated commits do not duplicate the checklist.
            var existingConfidenceKeys = new HashSet<string>(
                user.ConfidenceItems.Values.Select(c => ConfidenceKey(c.WeekStart, c.Label, c.Text)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var c in selectedConfidence)
            {
                var weekStart = ResolveWeekStart(c.WeekNumber, baselineWeek);
                var label = c.SubCategoryName ?? "";
                if (!existingConfidenceKeys.Add(ConfidenceKey(weekStart, label, c.Text)))
                {
                    continue;
                }
                // Link to the real sub-category entity (name kept as denormalized fallback).
                var confSubCategoryId = EnsureCategory(user, userId, c.SubCategoryName, categoryId)?.Id;
                var idx = orderByWeek.TryGetValue(weekStart, out var n) ? n : 1;
                orderByWeek[weekStart] = idx + 1;
                var confidence = new WeekConfidenceItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    WeekStart = weekStart,
                    SubCategoryId = confSubCategoryId,
                    Label = label,
                    Text = c.Text,
                    IsDone = false,
                    OrderIndex = idx,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                user.ConfidenceItems[confidence.Id] = confidence;
                createdConfidence.Add(confidence);
            }

            store.Save(userId, user);
            return new
            {
                importId,
                createdCount = created.Count,
                confidenceCount = createdConfidence.Count,
                tasks = created,
                confidenceItems = createdConfidence
            };
        }
    }

    private static string ConfidenceKey(DateOnly weekStart, string? label, string text) =>
        $"{weekStart:yyyy-MM-dd}\u0001{label ?? ""}\u0001{text}";

    public List<WeekConfidenceItem> GetWeekConfidence(string userId, DateOnly weekStart)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var items = user.ConfidenceItems.Values
                .Where(c => c.WeekStart == weekStart)
                .OrderBy(c => c.Label)
                .ThenBy(c => c.OrderIndex)
                .ToList();

            // Resolve the display label from the linked sub-category's CURRENT name so the
            // confidence checklist survives sub-category renames. The stored `Label` is kept as
            // a denormalized fallback (used when the FK is null or the category was deleted), and
            // is refreshed here to the latest name so future reads/dedup stay consistent.
            var relabelled = false;
            foreach (var c in items)
            {
                if (c.SubCategoryId is Guid subId
                    && user.Categories.TryGetValue(subId, out var cat)
                    && !string.IsNullOrWhiteSpace(cat.Name)
                    && !string.Equals(c.Label, cat.Name, StringComparison.Ordinal))
                {
                    c.Label = cat.Name;
                    c.UpdatedAt = DateTimeOffset.UtcNow;
                    relabelled = true;
                }
            }

            // Self-heal duplicates accumulated from repeated imports: collapse items with the
            // same label + text within the week, keep the first, OR-in the done state, and
            // physically drop the extras so the count stays correct going forward.
            var kept = new Dictionary<string, WeekConfidenceItem>(StringComparer.OrdinalIgnoreCase);
            var duplicateIds = new List<Guid>();
            foreach (var c in items)
            {
                var key = $"{c.Label}\u0001{c.Text}";
                if (kept.TryGetValue(key, out var keep))
                {
                    if (c.IsDone && !keep.IsDone)
                    {
                        keep.IsDone = true;
                    }
                    duplicateIds.Add(c.Id);
                }
                else
                {
                    kept[key] = c;
                }
            }

            if (duplicateIds.Count > 0)
            {
                foreach (var id in duplicateIds)
                {
                    user.ConfidenceItems.Remove(id);
                }
            }

            if (relabelled || duplicateIds.Count > 0)
            {
                store.Save(userId, user);
            }

            return kept.Values
                .OrderBy(c => c.Label)
                .ThenBy(c => c.OrderIndex)
                .ToList();
        }
    }

    public WeekConfidenceItem? ToggleWeekConfidence(string userId, Guid itemId, bool isDone)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.ConfidenceItems.TryGetValue(itemId, out var item))
            {
                return null;
            }

            item.IsDone = isDone;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            store.Save(userId, user);
            return item;
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
            var user = GetUser(userId);
            HealDuplicateTasks(user, userId);
            var tasks = user.Tasks.Values.AsEnumerable();
            if (query.CategoryId is not null) tasks = tasks.Where(x => x.CategoryId == query.CategoryId);
            if (query.SubCategoryId is not null) tasks = tasks.Where(x => x.SubCategoryId == query.SubCategoryId);
            if (query.Status is not null) tasks = tasks.Where(x => x.Status == query.Status);
            if (query.WeekStart is not null)
            {
                var weekStart = query.WeekStart.Value;
                var weekEnd = weekStart.AddDays(6);
                tasks = tasks.Where(x =>
                    x.PlannedWeekStart == weekStart
                    || (x.PlannedDate is not null && x.PlannedDate.Value >= weekStart && x.PlannedDate.Value <= weekEnd));
            }
            if (query.From is not null) tasks = tasks.Where(x => x.StartAt?.Date >= query.From.Value.ToDateTime(TimeOnly.MinValue));
            if (query.To is not null) tasks = tasks.Where(x => x.EndAt?.Date <= query.To.Value.ToDateTime(TimeOnly.MaxValue));
            return tasks
                .OrderBy(x => x.PlannedWeekStart)
                .ThenBy(x => x.PlannedDate ?? DateOnly.MaxValue)
                .ThenBy(x => x.CreatedAt)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public WeekActualHoursSummary GetWeekActualHoursSummary(string userId, DateOnly weekStart)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var weekEnd = weekStart.AddDays(6);
            var total = user.Tasks.Values
                .Where(t =>
                    t.PlannedWeekStart == weekStart
                    || (t.PlannedDate is not null && t.PlannedDate.Value >= weekStart && t.PlannedDate.Value <= weekEnd))
                .Where(t => t.ActualHours is not null)
                .Sum(t => t.ActualHours!.Value);
            return new WeekActualHoursSummary(weekStart, total);
        }
    }

    // Self-heal exact re-import duplicates: tasks sharing the same category, sub-category,
    // title AND planned date are artifacts of committing the same checklist more than once.
    // Keep the most-progressed copy (most completed sub-step weight, then earliest created)
    // and physically remove the rest so the week view stops showing duplicates.
    private void HealDuplicateTasks(UserData user, string userId)
    {
        var groups = user.Tasks.Values
            .Where(t => t.PlannedDate is not null)
            .GroupBy(t => $"{t.CategoryId}\u0001{t.SubCategoryId}\u0001{t.Title}\u0001{t.PlannedDate:yyyy-MM-dd}",
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0)
        {
            return;
        }

        foreach (var group in groups)
        {
            var keep = group
                .OrderByDescending(t => t.SubSteps.Where(s => s.IsDone).Sum(s => s.Weight))
                .ThenBy(t => t.CreatedAt)
                .First();
            foreach (var dup in group.Where(t => t.Id != keep.Id))
            {
                user.Tasks.Remove(dup.Id);
            }
        }

        store.Save(userId, user);
    }

    public TaskItem CreateTask(string userId, CreateTaskRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (IsDsaFlowCategory(user, req.CategoryId, req.SubCategoryId))
            {
                throw new InvalidOperationException("Manual task creation is disabled for the DSA category.");
            }
            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = req.Title.Trim(),
                Description = req.Description,
                CategoryId = req.CategoryId,
                SubCategoryId = req.SubCategoryId,
                PlannedWeekStart = req.PlannedWeekStart
                    ?? (req.PlannedDate is { } pd
                        ? StartOfWeek(pd, DayOfWeek.Monday)
                        : StartOfWeek(DateOnly.FromDateTime(DateTime.UtcNow.Date), DayOfWeek.Monday)),
                PlannedDate = req.PlannedDate,
                Status = req.Status ?? TaskStatus.Todo,
                StartAt = req.StartAt,
                EndAt = req.EndAt,
                DueDate = req.DueDate,
                Priority = req.Priority,
                AssignedTo = req.AssignedTo ?? userId,
                Pattern = req.Pattern,
                Difficulty = req.Difficulty,
                EstimatedHours = req.EstimatedHours,
                ActualHours = req.ActualHours,
                StoryPoints = req.StoryPoints,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            if (IsDsaFlowCategory(user, task.CategoryId, task.SubCategoryId))
            {
                SeedStandardSubSteps(task);
            }
            user.Tasks[task.Id] = task;
            store.Save(userId, user);
            return task;
        }
    }

    public static readonly (string Title, int Weight)[] StandardSubStepTemplate =
    [
        ("Understand Problem", 5),
        ("Corner Cases", 10),
        ("Brute Force", 10),
        ("Optimized Solution", 25),
        ("Time Complexity", 10),
        ("Space Complexity", 10),
        ("Coding", 20),
        ("Testing", 5),
        ("Revision", 5)
    ];

    private static void SeedStandardSubSteps(TaskItem task)
    {
        if (task.SubSteps.Count > 0) return;
        var order = 1;
        foreach (var (title, weight) in StandardSubStepTemplate)
        {
            task.SubSteps.Add(new TaskSubStep(Guid.NewGuid(), title, false, order++, null, 1, weight));
        }
    }

    public TaskItem? GetTask(string userId, Guid taskId)
    {
        lock (_gate) return GetUser(userId).Tasks.GetValueOrDefault(taskId);
    }

    public bool DeleteTask(string userId, Guid taskId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.Remove(taskId)) return false;
            store.DeleteTasks(userId, new[] { taskId });
            return true;
        }
    }

    public int DeleteTasks(string userId, IReadOnlyCollection<Guid> taskIds)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var removedIds = new List<Guid>(taskIds.Count);
            foreach (var id in taskIds)
            {
                if (user.Tasks.Remove(id)) removedIds.Add(id);
            }
            if (removedIds.Count > 0) store.DeleteTasks(userId, removedIds);
            return removedIds.Count;
        }
    }

    public ServiceResult<TaskItem> UpdateTask(string userId, Guid taskId, UpdateTaskRequest req)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task)) return ServiceResult<TaskItem>.NotFound();
            if (req.IfMatchVersion is not null && req.IfMatchVersion != task.RowVersion) return ServiceResult<TaskItem>.Conflict("Row version mismatch.");

            // Non-negative guards for effort fields (mirrors DB CHECK constraints)
            if (req.EstimatedHours is < 0) return ServiceResult<TaskItem>.Validation("EstimatedHours must be >= 0.");
            if (req.ActualHours is < 0) return ServiceResult<TaskItem>.Validation("ActualHours must be >= 0.");
            if (req.StoryPoints is < 0) return ServiceResult<TaskItem>.Validation("StoryPoints must be >= 0.");
            if (req.CategoryId is not null || req.SubCategoryId is not null)
            {
                return ServiceResult<TaskItem>.Validation("Task category updates are currently disabled.");
            }

            if (req.Title is not null) task.Title = req.Title.Trim();
            if (req.Description is not null) task.Description = req.Description;
            if (req.PlannedWeekStart is not null) task.PlannedWeekStart = req.PlannedWeekStart.Value;
            if (req.PlannedDate is not null)
            {
                task.PlannedDate = req.PlannedDate;
                if (req.PlannedWeekStart is null)
                    task.PlannedWeekStart = StartOfWeek(req.PlannedDate.Value, DayOfWeek.Monday);
            }
            if (req.StartAt is not null) task.StartAt = req.StartAt;
            if (req.EndAt is not null) task.EndAt = req.EndAt;
            if (req.DueDate is not null) task.DueDate = req.DueDate;
            if (req.Priority is not null) task.Priority = req.Priority;
            if (req.Pattern is not null) task.Pattern = req.Pattern;
            if (req.Difficulty is not null) task.Difficulty = req.Difficulty;
            if (req.QuestionAndReasoning is not null) task.QuestionAndReasoning = req.QuestionAndReasoning;
            if (req.LogicNotes is not null) task.LogicNotes = req.LogicNotes;
            if (req.AlgorithmNotes is not null) task.AlgorithmNotes = req.AlgorithmNotes;
            if (req.DiagramContent is not null) task.DiagramContent = req.DiagramContent;
            if (req.EstimatedHours is not null) task.EstimatedHours = req.EstimatedHours;
            if (req.ActualHours is not null) task.ActualHours = req.ActualHours;
            if (req.StoryPoints is not null) task.StoryPoints = req.StoryPoints;
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
            if (IsDsaTask(user, task))
            {
                return ServiceResult<TaskItem>.Validation("Manual task status updates are disabled for the DSA category.");
            }

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
            if (IsDsaTask(user, task))
            {
                return ServiceResult<TaskSubStep>.Validation("Manual subtask creation is disabled for the DSA category.");
            }
            var nextOrder = task.SubSteps.Count == 0 ? 1 : task.SubSteps.Max(s => s.OrderIndex) + 1;
            var step = new TaskSubStep(Guid.NewGuid(), req.Title.Trim(), false, nextOrder, null, 1, req.Weight);
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
            if (req.Title is not null && IsDsaTask(user, task))
            {
                return ServiceResult<TaskSubStep>.Validation("Manual subtask title updates are disabled for the DSA category.");
            }

            var title = req.Title?.Trim();
            var titleChanged = title is not null && title != sub.Title;
            var completionChanged = req.IsDone is not null && req.IsDone.Value != sub.IsDone;
            if (!titleChanged && !completionChanged)
            {
                return ServiceResult<TaskSubStep>.Ok(sub);
            }

            var now = DateTimeOffset.UtcNow;
            if (titleChanged) sub.Title = title!;

            TaskStatus? automaticStatus = null;
            if (completionChanged)
            {
                sub.IsDone = req.IsDone!.Value;
                sub.CompletedAt = sub.IsDone ? now : null;

                if (sub.IsDone &&
                    task.SubSteps.All(step => step.IsDone) &&
                    task.Status is TaskStatus.Todo or TaskStatus.InProgress)
                {
                    automaticStatus = TaskStatus.Done;
                }
                else if (!sub.IsDone && task.Status == TaskStatus.Done)
                {
                    automaticStatus = TaskStatus.InProgress;
                }
            }

            if (automaticStatus is not null)
            {
                var oldStatus = task.Status;
                task.Status = automaticStatus.Value;
                task.StatusEvents.Add(new TaskStatusEvent(Guid.NewGuid(), oldStatus, automaticStatus.Value, now));
            }

            sub.RowVersion++;
            task.UpdatedAt = now;
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
        => CreateAssetAsync(userId, taskId, req).GetAwaiter().GetResult();

    public async Task<ServiceResult<TaskAsset>> CreateAssetAsync(
        string userId,
        Guid taskId,
        CreateAssetRequest req,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.AssetType))
        {
            return ServiceResult<TaskAsset>.Validation("assetType is required.");
        }

        if (string.IsNullOrWhiteSpace(req.FileName))
        {
            return ServiceResult<TaskAsset>.Validation("fileName is required.");
        }

        if (TryDecodeInlineDataUrl(req.StoragePathOrUrl, out var inlineContentType, out var inlineBytes))
        {
            var uploadContentType = string.IsNullOrWhiteSpace(req.ContentType) ? inlineContentType : req.ContentType;
            var uploadSize = req.SizeBytes ?? inlineBytes.LongLength;
            await using var content = new MemoryStream(inlineBytes, writable: false);
            return await CreateAssetAsync(
                userId,
                taskId,
                req.AssetType,
                req.FileName,
                uploadContentType,
                uploadSize,
                content,
                cancellationToken);
        }

        if (!Uri.TryCreate(req.StoragePathOrUrl, UriKind.Absolute, out _))
        {
            return ServiceResult<TaskAsset>.Validation("storagePathOrUrl must be a blob URL or data URL.");
        }

        return CreateStoredAsset(userId, taskId, req.AssetType, req.StoragePathOrUrl, req.FileName, req.ContentType, req.SizeBytes);
    }

    public async Task<ServiceResult<TaskAsset>> CreateAssetAsync(
        string userId,
        Guid taskId,
        string assetType,
        string fileName,
        string? contentType,
        long? sizeBytes,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetType))
        {
            return ServiceResult<TaskAsset>.Validation("assetType is required.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ServiceResult<TaskAsset>.Validation("fileName is required.");
        }

        var storagePathOrUrl = await _assetStorage.UploadAsync(userId, taskId, fileName, contentType, content, cancellationToken)
            .ConfigureAwait(false);

        var result = CreateStoredAsset(userId, taskId, assetType, storagePathOrUrl, fileName, contentType, sizeBytes);
        if (result.StatusCode == StatusCodes.Status404NotFound)
        {
            await _assetStorage.DeleteAsync(storagePathOrUrl, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public bool DeleteAsset(string userId, Guid taskId, Guid assetId)
        => DeleteAssetAsync(userId, taskId, assetId).GetAwaiter().GetResult();

    public async Task<bool> DeleteAssetAsync(string userId, Guid taskId, Guid assetId, CancellationToken cancellationToken = default)
    {
        string? storagePathOrUrl = null;
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task))
            {
                return false;
            }

            var asset = task.Assets.FirstOrDefault(x => x.Id == assetId);
            if (asset is null)
            {
                return false;
            }

            storagePathOrUrl = asset.StoragePathOrUrl;
        }

        if (storagePathOrUrl is not null)
        {
            await _assetStorage.DeleteAsync(storagePathOrUrl, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task))
            {
                return true;
            }

            var removed = task.Assets.RemoveAll(x => x.Id == assetId) > 0;
            if (removed)
            {
                task.RowVersion++;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                store.Save(userId, user);
            }

            return removed;
        }
    }

    private ServiceResult<TaskAsset> CreateStoredAsset(
        string userId,
        Guid taskId,
        string assetType,
        string storagePathOrUrl,
        string fileName,
        string? contentType,
        long? sizeBytes)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            if (!user.Tasks.TryGetValue(taskId, out var task))
            {
                return ServiceResult<TaskAsset>.NotFound();
            }

            var asset = new TaskAsset(Guid.NewGuid(), assetType, storagePathOrUrl, fileName, contentType, sizeBytes, DateTimeOffset.UtcNow);
            task.Assets.Add(asset);
            task.RowVersion++;
            store.Save(userId, user);
            return ServiceResult<TaskAsset>.Ok(asset);
        }
    }

    private static bool TryDecodeInlineDataUrl(string value, out string contentType, out byte[] bytes)
    {
        contentType = "application/octet-stream";
        bytes = [];

        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = value.IndexOf(',');
        if (comma <= 5)
        {
            return false;
        }

        var metadata = value[5..comma];
        if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var inlineContentType = metadata[..^7];
        if (!string.IsNullOrWhiteSpace(inlineContentType))
        {
            contentType = inlineContentType;
        }

        try
        {
            bytes = Convert.FromBase64String(value[(comma + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
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
                var dayOffset = task.PlannedDate is null
                    ? 0
                    : (int)(task.PlannedDate.Value.DayNumber - task.PlannedWeekStart.DayNumber);
                dayOffset = Math.Clamp(dayOffset, 0, 6);
                task.PlannedWeekStart = req.ToWeekStart;
                task.PlannedDate = req.ToWeekStart.AddDays(dayOffset);
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
            store.SaveNotificationSettings(userId, user.NotificationSettings);
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
            store.AddNotificationSchedule(userId, schedule);
            return schedule;
        }
    }

    public List<NotificationSchedule> GetNotificationSchedules(string userId)
    {
        lock (_gate) return GetUser(userId).NotificationSchedules.OrderByDescending(x => x.ScheduledAt).ToList();
    }

    public IReadOnlyCollection<string> GetKnownUserIds()
    {
        lock (_gate) return store.GetKnownUserIds();
    }

    public bool IsTeamsConnected(string userId)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var key = user.Integrations.Keys.FirstOrDefault(k => string.Equals(k, "Teams", StringComparison.OrdinalIgnoreCase));
            return key is not null && user.Integrations.TryGetValue(key, out var setting) && setting.IsConnected;
        }
    }

    public DailyDigestPayload GetDailyDigestPayload(string userId, DateOnly? onDate)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var date = onDate ?? DateOnly.FromDateTime(DateTime.Now);
            var tasks = user.Tasks.Values
                .Where(t => t.PlannedDate == date && t.Status is not TaskStatus.Done and not TaskStatus.Cancelled and not TaskStatus.Skipped)
                .OrderBy(t => t.StartAt ?? DateTimeOffset.MaxValue)
                .ThenBy(t => GetPriorityRank(t.Priority))
                .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Select(t => new DailyDigestTaskItem(
                    t.Id,
                    t.Title,
                    t.Status,
                    t.Priority,
                    t.PlannedDate,
                    t.StartAt,
                    t.DueDate))
                .ToList();

            var destination = string.IsNullOrWhiteSpace(user.NotificationSettings.TeamsDestination)
                ? "personal-chat"
                : user.NotificationSettings.TeamsDestination.Trim();
            var text = BuildDailyDigestMessage(date, tasks);
            return new DailyDigestPayload(userId, date, destination, tasks.Count, tasks, text);
        }
    }

    public bool HasDailyDigestAttemptForDate(string userId, DateOnly localDate, TimeZoneInfo timeZone)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            return user.NotificationSchedules.Any(s =>
                string.Equals(s.Channel, DailyDigestChannel, StringComparison.OrdinalIgnoreCase)
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.ScheduledAt, timeZone).DateTime) == localDate);
        }
    }

    public NotificationSchedule RecordDailyDigestAttempt(string userId, string status, DateTimeOffset? sentAt)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var schedule = new NotificationSchedule(
                Guid.NewGuid(),
                null,
                DailyDigestChannel,
                DateTimeOffset.UtcNow,
                status,
                sentAt);
            user.NotificationSchedules.Add(schedule);
            store.AddNotificationSchedule(userId, schedule);
            return schedule;
        }
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
            store.UpsertIntegration(userId, item);
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
            var integrationKey = user.Integrations.Keys.FirstOrDefault(k =>
                string.Equals(k, provider, StringComparison.OrdinalIgnoreCase));
            if (integrationKey is null || !user.Integrations.TryGetValue(integrationKey, out var setting))
            {
                return new { disconnected = false, reason = "not_found" };
            }

            setting.IsConnected = false;
            if (!store.DisconnectIntegration(userId, provider))
            {
                store.UpsertIntegration(userId, setting);
            }
            return new { disconnected = true, provider };
        }
    }

    public object TestIntegration(string userId, string provider)
    {
        lock (_gate)
        {
            var user = GetUser(userId);
            var integrationKey = user.Integrations.Keys.FirstOrDefault(k =>
                string.Equals(k, provider, StringComparison.OrdinalIgnoreCase));
            var ok = integrationKey is not null
                && user.Integrations.TryGetValue(integrationKey, out var setting)
                && setting.IsConnected;
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

    private const string DailyDigestChannel = "TeamsDailyDigest";

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
            var totalWeight = task.SubSteps.Sum(s => s.Weight);
            if (totalWeight > 0)
            {
                var doneWeight = task.SubSteps.Where(s => s.IsDone).Sum(s => s.Weight);
                return Math.Round((double)doneWeight / totalWeight * 100, 2);
            }
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

    private static int GetPriorityRank(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "p0" or "critical" or "high" => 0,
            "p1" or "medium" => 1,
            "p2" or "low" => 2,
            _ => 3
        };
    }

    private static string BuildDailyDigestMessage(DateOnly date, IReadOnlyCollection<DailyDigestTaskItem> tasks)
    {
        var heading = $"Good morning! Here is your plan for {date:yyyy-MM-dd}:";
        if (tasks.Count == 0)
        {
            return $"{heading}{Environment.NewLine}- No planned tasks for today.";
        }

        var lines = tasks.Select((task, index) =>
        {
            var parts = new List<string> { $"{index + 1}. {task.Title}" };
            if (!string.IsNullOrWhiteSpace(task.Priority)) parts.Add($"Priority: {task.Priority}");
            if (task.StartAt is not null) parts.Add($"Start: {task.StartAt:HH:mm}");
            if (task.DueDate is not null) parts.Add($"Due: {task.DueDate:yyyy-MM-dd}");
            return $"{string.Join(" | ", parts)}";
        });
        return $"{heading}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string NormalizeChecklistLine(string line)
    {
        var normalized = Regex.Replace(line, @"^[-*]\s+", "");
        // Strip leading checkbox markers: [ ], [x], [X], and ballot-box glyphs ☐ ☑ ☒.
        normalized = Regex.Replace(normalized, @"^\[\s*[xX]?\s*\]\s*", "");
        normalized = Regex.Replace(normalized, @"^[\u2610\u2611\u2612]\s*", "");
        normalized = Regex.Replace(normalized, @"^\d+[\)\.\-\s]+", "");
        return normalized.Trim();
    }

    private static readonly int[] ScheduleDayOffsets = [0, 2, 4]; // Monday, Wednesday, Friday

    private static DateOnly ResolveWeekStart(int? weekNumber, DateOnly baselineWeek)
    {
        if (weekNumber is > 1)
        {
            return baselineWeek.AddDays((weekNumber.Value - 1) * 7);
        }

        return baselineWeek;
    }

    /// <summary>
    /// Returns the first calendar week at or after <paramref name="targetWeek"/> whose
    /// Mon/Wed/Fri slots are not yet full (fewer than <see cref="ScheduleDayOffsets"/> tasks),
    /// so overflow beyond 3-per-week cascades into subsequent weeks.
    /// </summary>
    private static DateOnly NextWeekWithFreeSlot(DateOnly targetWeek, Dictionary<DateOnly, int> weekFill)
    {
        var week = targetWeek;
        while ((weekFill.TryGetValue(week, out var count) ? count : 0) >= ScheduleDayOffsets.Length)
        {
            week = week.AddDays(7);
        }

        return week;
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

    private static bool IsDsaTask(UserData user, TaskItem task) =>
        IsDsaFlowCategory(user, task.CategoryId, task.SubCategoryId);

    private static bool IsDsaFlowCategory(UserData user, Guid? categoryId, Guid? subCategoryId)
    {
        return IsDsaCategory(user, categoryId) || IsDsaCategory(user, subCategoryId);
    }

    private static bool IsDsaCategory(UserData user, Guid? categoryId)
    {
        if (categoryId is null)
        {
            return false;
        }

        var currentId = categoryId.Value;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentId) && user.Categories.TryGetValue(currentId, out var category))
        {
            if (string.Equals(category.Name, DsaRootCategoryName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (category.ParentCategoryId is null)
            {
                break;
            }

            currentId = category.ParentCategoryId.Value;
        }

        return false;
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
    public DateOnly? PlannedDate { get; set; }
    public string? Pattern { get; set; }
    public Difficulty? Difficulty { get; set; }
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
    public decimal? EstimatedHours { get; set; }
    public decimal? ActualHours { get; set; }
    public int? StoryPoints { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; } = 1;
    public List<TaskSubStep> SubSteps { get; } = [];
    public List<TaskLink> Links { get; } = [];
    public List<TaskAsset> Assets { get; } = [];
    public List<TaskStatusEvent> StatusEvents { get; } = [];
    public List<TaskSpilloverEvent> Spillovers { get; } = [];
}

public sealed class TaskSubStep(Guid id, string title, bool isDone, int orderIndex, DateTimeOffset? completedAt, int rowVersion, int weight = 0)
{
    public Guid Id { get; set; } = id;
    public string Title { get; set; } = title;
    public bool IsDone { get; set; } = isDone;
    public int OrderIndex { get; set; } = orderIndex;
    public DateTimeOffset? CompletedAt { get; set; } = completedAt;
    public int RowVersion { get; set; } = rowVersion;
    public int Weight { get; set; } = weight;
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
    public List<ConfidenceDraftItem> ConfidenceItems { get; set; } = [];
}

public sealed class ChecklistDraftItem(Guid id, int order, string text, int? weekNumber, string? subCategoryName, string? monthLabel = null)
{
    public Guid Id { get; set; } = id;
    public int Order { get; set; } = order;
    public string Text { get; set; } = text;
    public int? WeekNumber { get; set; } = weekNumber;
    public string? SubCategoryName { get; set; } = subCategoryName;
    public string? MonthLabel { get; set; } = monthLabel;
}

public sealed class ConfidenceDraftItem(Guid id, int order, string text, int? weekNumber, string? subCategoryName, string? monthLabel = null)
{
    public Guid Id { get; set; } = id;
    public int Order { get; set; } = order;
    public string Text { get; set; } = text;
    public int? WeekNumber { get; set; } = weekNumber;
    public string? SubCategoryName { get; set; } = subCategoryName;
    public string? MonthLabel { get; set; } = monthLabel;
}

public sealed class WeekConfidenceItem
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public DateOnly WeekStart { get; set; }
    public Guid? SubCategoryId { get; set; }
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsDone { get; set; }
    public int OrderIndex { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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

public sealed record DailyDigestTaskItem(
    Guid TaskId,
    string Title,
    TaskStatus Status,
    string? Priority,
    DateOnly? PlannedDate,
    DateTimeOffset? StartAt,
    DateOnly? DueDate);

public sealed record DailyDigestPayload(
    string UserId,
    DateOnly Date,
    string TeamsDestination,
    int PlannedTaskCount,
    IReadOnlyList<DailyDigestTaskItem> Tasks,
    string Message);

public sealed record DailyDigestSendResult(
    string UserId,
    DateOnly Date,
    bool Sent,
    string Status,
    string TeamsDestination,
    int PlannedTaskCount);

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
public sealed record CommitChecklistImportRequest(List<Guid> SelectedDraftIds, List<Guid>? SelectedConfidenceIds = null, DateOnly? StartWeekDate = null);
public sealed record ToggleWeekConfidenceRequest(bool IsDone);
public sealed record CreateCategoryRequest(string Name, Guid? ParentCategoryId);
public sealed record UpdateCategoryRequest(string? Name, Guid? ParentCategoryId);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    Guid? CategoryId,
    Guid? SubCategoryId,
    DateOnly? PlannedWeekStart,
    DateOnly? PlannedDate,
    DateOnly? DueDate,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Priority,
    TaskStatus? Status,
    string? AssignedTo,
    string? Pattern = null,
    Difficulty? Difficulty = null,
    decimal? EstimatedHours = null,
    decimal? ActualHours = null,
    int? StoryPoints = null);

public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    Guid? CategoryId,
    Guid? SubCategoryId,
    DateOnly? PlannedWeekStart,
    DateOnly? PlannedDate,
    DateOnly? DueDate,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? Priority,
    int? IfMatchVersion,
    string? Pattern = null,
    Difficulty? Difficulty = null,
    string? QuestionAndReasoning = null,
    string? LogicNotes = null,
    string? AlgorithmNotes = null,
    string? DiagramContent = null,
    decimal? EstimatedHours = null,
    decimal? ActualHours = null,
    int? StoryPoints = null);

public sealed record UpdateTaskStatusRequest(TaskStatus Status, bool OverrideIncompleteSubsteps, int? IfMatchVersion);
public sealed record BulkDeleteTasksRequest(List<Guid>? TaskIds);
public sealed record CreateSubstepRequest(string Title, int Weight = 0);
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
public sealed record WeekActualHoursSummary(DateOnly WeekStart, decimal TotalActualHours);
