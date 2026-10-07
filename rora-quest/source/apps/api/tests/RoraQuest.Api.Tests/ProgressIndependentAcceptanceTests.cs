using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using RoraQuest.Api.Progress;
using Xunit;

// Independent acceptance additions. All storage is disposable; never read the app connection.
public class ProgressIndependentAcceptanceTests
{
    private static Task<JsonElement> Json(HttpResponseMessage response) => ProgressHttpTests.Json(response);
    private static async Task<JsonElement> Report(HttpClient client, string query = "") =>
        await Json(await client.GetAsync("/api/progress" + query));

    [Fact]
    public async Task NonActivityRoutes_IndividuallyPreserveRecordedInstantAndCount()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var id = await ProgressHttpTests.CreateTask(client, "Observed title");
        await ProgressHttpTests.Complete(client, id);
        var original = (await Report(client)).GetProperty("lastActivity").GetProperty("event");
        async Task Unchanged()
        {
            var report = await Report(client);
            Assert.Equal(1, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
            Assert.Equal(original.GetProperty("occurredAtUtc").GetString(),
                report.GetProperty("lastActivity").GetProperty("event").GetProperty("occurredAtUtc").GetString());
            Assert.Equal("Observed title", report.GetProperty("lastActivity").GetProperty("event").GetProperty("taskTitle").GetString());
        }
        foreach (var patch in new object[]
        {
            new { title = "Renamed now" }, new { description = "Changed notes" },
            new { logicNotes = "Logic", algorithmNotes = "Algorithm", questionAndReasoning = "Reasoning" },
            new { estimatedHours = 4, actualHours = 2, storyPoints = 3 },
            new { plannedWeekStart = "2026-10-12", plannedDate = "2026-10-13" },
            new { dueDate = "2026-10-15" }, new { aiReviewFeedback = "Review only" }
        })
        {
            await Json(await client.PatchAsJsonAsync("/api/tasks/" + id, patch));
            await Unchanged();
        }
        var link = await Json(await client.PostAsJsonAsync($"/api/tasks/{id}/links",
            new { url = "https://example.invalid/acceptance", label = "Reference" }));
        await Unchanged();
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/links/{link.GetProperty("id").GetString()}",
            new { label = "Changed reference" }));
        await Unchanged();
        await Json(await client.PostAsJsonAsync("/api/tasks",
            new { title = "Created already Done", status = "Done", plannedWeekStart = "2026-10-05" }));
        await Unchanged();
        var stepsTask = await ProgressHttpTests.CreateTask(client, "Structural changes");
        var step = await Json(await client.PostAsJsonAsync($"/api/tasks/{stepsTask}/substeps", new { title = "New weighted step", weight = 4 }));
        await Unchanged();
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{stepsTask}/substeps/{step.GetProperty("id").GetString()}",
            new { title = "Renamed step" }));
        await Unchanged();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{stepsTask}/substeps/{step.GetProperty("id").GetString()}")).StatusCode);
        await Unchanged();
        var import = await Json(await client.PostAsJsonAsync("/api/checklists/imports/bulk-text",
            new { rawText = "Week 1: Acceptance\n- [x] Imported checked item\n- [ ] Imported unchecked item\nPattern confidence:\n- [x] Understand the topic", categoryName = "Acceptance import" }));
        Assert.True(import.GetProperty("draftItems").GetArrayLength() >= 2);
        await Unchanged();
        await Json(await client.PostAsJsonAsync($"/api/checklists/imports/{import.GetProperty("id").GetString()}/commit",
            new { selectedDraftIds = Array.Empty<string>(), selectedConfidenceIds = Array.Empty<string>(), startWeekDate = "2026-10-05" }));
        await Unchanged();
        var confidence = await Json(await client.GetAsync("/api/week-confidence/2026-10-05"));
        Assert.NotEmpty(confidence.EnumerateArray());
        foreach (var item in confidence.EnumerateArray())
        {
            await Json(await client.PatchAsJsonAsync($"/api/week-confidence/{item.GetProperty("id").GetString()}", new { isDone = true }));
            await Unchanged();
        }
        foreach (var path in new[] { "/api/auth/me", "/api/tasks/" + id, "/api/scorecard", "/api/reports/progress" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
            await Unchanged();
        }
        var changedWeek = await Report(client);
        Assert.DoesNotContain(changedWeek.GetProperty("weeklyPlan").GetProperty("tasks").EnumerateArray(),
            row => row.GetProperty("taskId").GetString() == id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/tasks/" + id)).StatusCode);
        await Unchanged();
        Assert.Equal("taskRemoved", (await Report(client)).GetProperty("lastActivity").GetProperty("event").GetProperty("availability").GetString());
    }

    [Fact]
    public async Task ActualHttpInstants_KolkataBoundaryAndDifferentDayRecompletionDoNotMoveSchedulesOrHistory()
    {
        await using var factory = new ProgressHttpFactory();
        factory.Clock.UtcNow = DateTimeOffset.Parse("2026-10-05T18:29:59Z");
        using var client = factory.Client();
        var first = await Json(await client.PostAsJsonAsync("/api/tasks",
            new { title = "Before reporting midnight", plannedWeekStart = "2026-10-05", plannedDate = "2026-10-06" }));
        var id = first.GetProperty("id").GetString()!;
        await ProgressHttpTests.Complete(client, id);
        factory.Clock.UtcNow = DateTimeOffset.Parse("2026-10-05T18:30:00Z");
        var second = await ProgressHttpTests.CreateTask(client, "Exactly reporting midnight");
        await ProgressHttpTests.Complete(client, second);
        var kolkata = await Report(client, "?from=2026-10-05&to=2026-10-06&timeZone=Asia/Kolkata");
        Assert.Equal(2, kolkata.GetProperty("participation").GetProperty("activeDays").GetInt32());
        Assert.All(kolkata.GetProperty("days").EnumerateArray(), day => Assert.Equal(1, day.GetProperty("unitCount").GetInt32()));
        var utc = await Report(client, "?from=2026-10-05&to=2026-10-06&timeZone=UTC&selectedDate=2026-10-05");
        Assert.Equal(1, utc.GetProperty("participation").GetProperty("activeDays").GetInt32());
        Assert.Equal(2, utc.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        foreach (var report in new[] { utc, kolkata })
            Assert.Equal("2026-10-06", report.GetProperty("weeklyPlan").GetProperty("tasks").EnumerateArray()
                .Single(row => row.GetProperty("taskId").GetString() == id).GetProperty("plannedDate").GetString());
        var instant = utc.GetProperty("lastActivity").GetProperty("event").GetProperty("occurredAtUtc").GetString();
        factory.Clock.UtcNow = DateTimeOffset.Parse("2026-10-06T20:00:00Z");
        await Json(await client.PatchAsJsonAsync("/api/tasks/" + id + "/status", new { status = "Todo" }));
        var reopened = await Report(client, "?timeZone=UTC");
        Assert.Equal(0, reopened.GetProperty("weeklyPlan").GetProperty("tasks").EnumerateArray()
            .Single(row => row.GetProperty("taskId").GetString() == id).GetProperty("progressPercent").GetDouble());
        factory.Clock.UtcNow = DateTimeOffset.Parse("2026-10-07T20:00:00Z");
        await ProgressHttpTests.Complete(client, id);
        var recompleted = await Report(client, "?from=2026-10-05&to=2026-10-07&timeZone=UTC");
        Assert.Equal(1, recompleted.GetProperty("participation").GetProperty("activeDays").GetInt32());
        Assert.Equal(instant, recompleted.GetProperty("lastActivity").GetProperty("event").GetProperty("occurredAtUtc").GetString());
        Assert.Equal(0, recompleted.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/progress?timeZone=" + new string('x', 101))).StatusCode);
    }

    [ProgressPostgresFact]
    public async Task RealPostgres_PopulatedV8UpgradeHasNoBackfillAndRepeatedMigrationPreservesLegacyData()
    {
        var raw = Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_TEST_POSTGRES")!;
        var schema = "progress_acceptance_" + Guid.NewGuid().ToString("N");
        using (var root = new NpgsqlConnection(raw))
        {
            root.Open();
            root.Execute($"CREATE SCHEMA \"{schema}\"");
        }
        var options = new NpgsqlConnectionStringBuilder(raw) { SearchPath = schema };
        using var source = NpgsqlDataSource.Create(options.ToString());
        var migrations = Path.GetFullPath(Path.Combine(ProgressApiProcess.ApiRoot, "..", "..", "..", "infra", "sql"));
        using var conn = source.OpenConnection();
        foreach (var path in Directory.GetFiles(migrations, "V*__*.sql").Where(path => !Path.GetFileName(path).StartsWith("V9__")).Order())
            conn.Execute(File.ReadAllText(path));
        var task = Guid.NewGuid();
        var step = Guid.NewGuid();
        conn.Execute("""
            INSERT INTO users(id) VALUES ('process-owner');
            INSERT INTO task_items(id,user_id,title,planned_week_start,assigned_to,status,updated_at)
              VALUES (@task,'process-owner','Legacy Done','2025-01-06','process-owner','Done','2025-01-08T00:00:00Z');
            INSERT INTO task_sub_steps(id,task_item_id,title,is_done,order_index,completed_at)
              VALUES (@step,@task,'Legacy completed metadata',true,1,'2025-01-08T00:00:00Z');
            INSERT INTO task_status_events(id,task_item_id,from_status,to_status,changed_at)
              VALUES (@event,@task,'Todo','Done','2025-01-08T00:00:00Z');
            INSERT INTO checklist_imports(id,user_id,source_type,raw_text,category_name,parsed_count)
              VALUES (@import,'process-owner','BulkText','- [x] Historical import','Legacy',1);
            """, new { task, step, @event = Guid.NewGuid(), import = Guid.NewGuid() });
        new DatabaseMigrator(source, migrations, false).Run();
        new DatabaseMigrator(source, migrations, false).Run();
        Assert.Equal(9, conn.ExecuteScalar<int>("SELECT count(*) FROM schema_migrations"));
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_owners"));
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_completions"));
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_capture_sessions"));
        Assert.Equal("Done", conn.ExecuteScalar<string>("SELECT status FROM task_items WHERE id=@task", new { task }));
        // Exercise the real seed runner, before readiness/capture, rather than treating seeded rows as events.
        new DatabaseMigrator(source, migrations, true).Run();
        Assert.Equal(3, conn.ExecuteScalar<int>("SELECT count(*) FROM categories WHERE user_id='demo-user'"));
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT count(*) FROM week_plans WHERE user_id='demo-user'"));
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_owners"));
        Assert.Equal(0, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_completions"));
        await using var process = await ProgressApiProcess.Start(null, options.ToString());
        var report = await Report(process.Client, "?from=2025-01-01&to=2025-01-10");
        Assert.All(report.GetProperty("days").EnumerateArray(), day => Assert.Equal("unknown", day.GetProperty("status").GetString()));
        Assert.Equal("none", report.GetProperty("lastActivity").GetProperty("state").GetString());
        Assert.Equal("0", report.GetProperty("snapshotSequence").GetString());
        var legacy = await Json(await process.Client.GetAsync("/api/tasks/" + task));
        Assert.Equal("Done", legacy.GetProperty("status").GetString());
        await Json(await process.Client.PatchAsJsonAsync($"/api/tasks/{task}/substeps/{step}", new { isDone = false }));
        await Json(await process.Client.PatchAsJsonAsync($"/api/tasks/{task}/substeps/{step}", new { isDone = true }));
        Assert.Equal(1, (await Report(process.Client)).GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
    }

    [ProgressPostgresFact]
    public async Task RealPostgres_ZeroEventOwnerAndBothOwnersSurviveDeleteSuspensionAndFreshProcess()
    {
        using var fixture = new ProgressPostgresFixture();
        string firstStart, zeroStart, otherStart, firstId, otherId;
        await using (var first = await ProgressApiProcess.Start(null, fixture.ConnectionString))
        {
            firstId = await ProgressHttpTests.CreateTask(first.Client, "Same title");
            await ProgressHttpTests.Complete(first.Client, firstId);
            firstStart = (await Report(first.Client)).GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
            await Json(await first.Client.PutAsJsonAsync("/api/notifications/settings", new { timeZone = "UTC" }));
            await Json(await first.Client.PostAsJsonAsync("/api/tasks/bulk-delete", new { taskIds = new[] { firstId } }));
            first.Client.DefaultRequestHeaders.Remove("X-User-Id");
            first.Client.DefaultRequestHeaders.Add("X-User-Id", "other-owner");
            otherId = await ProgressHttpTests.CreateTask(first.Client, "Same title");
            await ProgressHttpTests.Complete(first.Client, otherId);
            otherStart = (await Report(first.Client)).GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
            first.Client.DefaultRequestHeaders.Remove("X-User-Id");
            first.Client.DefaultRequestHeaders.Add("X-User-Id", "zero-owner");
            zeroStart = (await Report(first.Client)).GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
        }
        var ledger = new ProgressLedger(() => fixture.Source.OpenConnection(), true, TimeProvider.System);
        ledger.Maintenance(false);
        using (var conn = fixture.Source.OpenConnection())
            conn.Execute("UPDATE task_items SET title='Changed by old writer' WHERE id=@id", new { id = Guid.Parse(otherId) });
        ledger.Maintenance(true);
        await using var fresh = await ProgressApiProcess.Start(null, fixture.ConnectionString);
        var after = await Report(fresh.Client);
        Assert.Equal(firstStart, after.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.True(after.GetProperty("coverage").GetProperty("hasInterruptions").GetBoolean());
        Assert.Equal(1, after.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        Assert.Equal("taskRemoved", after.GetProperty("lastActivity").GetProperty("event").GetProperty("availability").GetString());
        Assert.Equal(firstId, after.GetProperty("lastActivity").GetProperty("event").GetProperty("taskId").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await fresh.Client.GetAsync("/api/tasks/" + otherId)).StatusCode);
        fresh.Client.DefaultRequestHeaders.Remove("X-User-Id");
        fresh.Client.DefaultRequestHeaders.Add("X-User-Id", "other-owner");
        var other = await Report(fresh.Client, "?timeZone=America/New_York");
        Assert.Equal(otherStart, other.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.Equal(otherId, other.GetProperty("lastActivity").GetProperty("event").GetProperty("taskId").GetString());
        Assert.Equal("Same title", other.GetProperty("lastActivity").GetProperty("event").GetProperty("taskTitle").GetString());
        Assert.Equal("Changed by old writer", (await Json(await fresh.Client.GetAsync("/api/tasks/" + otherId))).GetProperty("title").GetString());
        await Json(await fresh.Client.PatchAsJsonAsync("/api/tasks/" + otherId + "/status", new { status = "Todo" }));
        await ProgressHttpTests.Complete(fresh.Client, otherId);
        Assert.Equal(1, (await Report(fresh.Client)).GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        fresh.Client.DefaultRequestHeaders.Remove("X-User-Id");
        fresh.Client.DefaultRequestHeaders.Add("X-User-Id", "zero-owner");
        var zero = await Report(fresh.Client);
        Assert.Equal(zeroStart, zero.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.True(zero.GetProperty("coverage").GetProperty("hasInterruptions").GetBoolean());
        Assert.Equal("0", zero.GetProperty("snapshotSequence").GetString());
        Assert.Equal("none", zero.GetProperty("lastActivity").GetProperty("state").GetString());
    }
}
