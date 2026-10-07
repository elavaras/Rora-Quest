using System.Net.Http.Json;
using Dapper;
using Npgsql;
using RoraQuest.Api.Progress;
using Xunit;

public sealed class ProgressPostgresFactAttribute : FactAttribute
{
    public ProgressPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_TEST_POSTGRES")))
            Skip = "Not run: supply RORAQUEST_PROGRESS_TEST_POSTGRES for a dedicated disposable PostgreSQL database. Mocks are not persistence proof.";
    }
}

internal sealed class ProgressPostgresFixture : IDisposable
{
    public string ConnectionString { get; }
    public NpgsqlDataSource Source { get; }
    public string Migrations => Path.GetFullPath(Path.Combine(ProgressApiProcess.ApiRoot, "..", "..", "..", "infra", "sql"));
    public ProgressPostgresFixture()
    {
        var raw = Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Dedicated PostgreSQL test connection is required.");
        var options = new NpgsqlConnectionStringBuilder(raw);
        if (string.IsNullOrWhiteSpace(options.Database)) throw new InvalidOperationException("Specify a dedicated test database.");
        // Never use ordinary app ConnectionStrings configuration or share the application's schema.
        var schema = "progress_test_" + Guid.NewGuid().ToString("N");
        using (var conn = new NpgsqlConnection(raw))
        {
            conn.Open();
            conn.Execute($"CREATE SCHEMA \"{schema}\"");
        }
        options.SearchPath = schema;
        ConnectionString = options.ToString();
        Source = NpgsqlDataSource.Create(ConnectionString);
        new DatabaseMigrator(Source, Migrations, false).Run();
    }
    public (RoraQuestService Service, ProgressLedger Ledger) Service(ProgressFaults? faults = null)
    {
        var ledger = new ProgressLedger(() => Source.OpenConnection(), true, TimeProvider.System, faults);
        ledger.Start();
        return (new RoraQuestService(new PostgresRoraQuestStore(Source, ledger)), ledger);
    }
    public void Dispose() => Source.Dispose(); // Schema is retained for diagnostics; operator owns cleanup.
}

public class ProgressPostgresTests
{
    private static ProgressReport Report(RoraQuestService service) => service.GetActivityProgress("pg-owner", ProgressTestStores.Query("timeZone=UTC"));

    [ProgressPostgresFact]
    public void RealPostgres_AtomicRollbackReceiptsRevisionAndLegacyWriterTaint()
    {
        using var fixture = new ProgressPostgresFixture();
        var faults = new ThrowProgressFaults();
        var (first, ledger) = fixture.Service(faults);
        var (second, _) = fixture.Service();
        var task = first.CreateTask("pg-owner", ProgressTestStores.Task("Atomic"));
        Assert.Equal(TaskStatus.Todo, second.GetTask("pg-owner", task.Id)!.Status); // warms an independent cache
        foreach (var point in new[] { "beforeEvent", "afterEvent", "afterAggregate", "beforeCommit", "certification" })
        {
            faults.Boundary = point;
            Assert.Throws<ProgressUnavailableException>(() => first.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Done, false, null)));
            Assert.Equal(TaskStatus.Todo, task.Status);
            faults.Boundary = null;
            Assert.Equal(TaskStatus.Todo, second.GetTask("pg-owner", task.Id)!.Status);
            Assert.Empty(Report(second).SelectedDay.Events);
        }
        faults.Boundary = "afterCommit";
        Assert.Equal(200, first.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Done, false, null)).StatusCode);
        faults.Boundary = null;
        Assert.Equal(TaskStatus.Done, second.GetTask("pg-owner", task.Id)!.Status);
        first.UpdateNotificationSettings("pg-owner", new(null, null, "test-only"));
        first.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Todo, false, null));
        second.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Done, false, null));
        Assert.Single(Report(first).SelectedDay.Events);
        using var conn = fixture.Source.OpenConnection();
        Assert.Equal(3, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_mutation_receipts WHERE owner_id='pg-owner'"));
        var started = Report(first).Coverage.TrackingStartedAtUtc;
        // Run V9 again on populated state, then the normal migrator again. Neither backfills nor resets.
        using (var tx = conn.BeginTransaction())
        {
            conn.Execute(File.ReadAllText(Path.Combine(fixture.Migrations, "V9__progress_activity.sql")), transaction: tx);
            tx.Commit();
        }
        new DatabaseMigrator(fixture.Source, fixture.Migrations, false).Run();
        Assert.Equal(started, Report(first).Coverage.TrackingStartedAtUtc);
        conn.Execute("UPDATE task_items SET title='Old writer title' WHERE id=@id", new { id = task.Id });
        Assert.False(Report(first).Coverage.CaptureReliableNow);
        Assert.True(Report(first).Coverage.HasInterruptions);
        Assert.Equal("Old writer title", first.GetTask("pg-owner", task.Id)!.Title); // bypasses stale cache under taint
        Assert.Equal("Atomic", Report(first).LastActivity.Event!.TaskTitle);
        Assert.Equal(200, first.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Todo, false, null)).StatusCode);
        Assert.Throws<ProgressUnavailableException>(() => first.UpdateTaskStatus("pg-owner", task.Id, new(TaskStatus.Done, false, null)));
        // Offline maintenance only after all services are no longer used; fresh service caches after resume.
        ledger.Maintenance(true);
        var (resumed, _) = fixture.Service();
        Assert.True(Report(resumed).Coverage.CaptureReliableNow);
        resumed.DeleteTask("pg-owner", task.Id);
        Assert.Single(Report(resumed).SelectedDay.Events);
        Assert.Equal("taskRemoved", Report(resumed).LastActivity.Event!.Availability);
    }

    [ProgressPostgresFact]
    public async Task RealPostgres_TwoProcessesSerializeRevisionAndPreserveRestartHistory()
    {
        using var fixture = new ProgressPostgresFixture();
        string id;
        string firstStart, firstSequence;
        await using (var first = await ProgressApiProcess.Start(null, fixture.ConnectionString))
        await using (var second = await ProgressApiProcess.Start(null, fixture.ConnectionString))
        {
            id = await ProgressHttpTests.CreateTask(first.Client, "Two processes");
            await ProgressHttpTests.Json(await second.Client.GetAsync("/api/tasks/" + id)); // independent process cache
            await Task.WhenAll(ProgressHttpTests.Complete(first.Client, id), ProgressHttpTests.Complete(second.Client, id));
            var report = await ProgressHttpTests.Json(await first.Client.GetAsync("/api/progress"));
            Assert.Equal(1, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
            firstStart = report.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
            firstSequence = report.GetProperty("snapshotSequence").GetString()!;
            var cached = await ProgressHttpTests.Json(await second.Client.GetAsync("/api/tasks/" + id));
            Assert.Equal("Done", cached.GetProperty("status").GetString());
        }
        await using var fresh = await ProgressApiProcess.Start(null, fixture.ConnectionString);
        var after = await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"));
        Assert.Equal(firstStart, after.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.Equal(firstSequence, after.GetProperty("snapshotSequence").GetString());
        Assert.Equal("available", after.GetProperty("lastActivity").GetProperty("event").GetProperty("availability").GetString());
        await ProgressHttpTests.Json(await fresh.Client.PatchAsJsonAsync($"/api/tasks/{id}/status", new { status = "Todo" }));
        await ProgressHttpTests.Complete(fresh.Client, id);
        Assert.Equal(1, (await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"))).GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
    }

    [ProgressPostgresFact]
    public void RealPostgres_TargetedDeleteAndDuplicateCleanupRetainIndependentLedger()
    {
        using var fixture = new ProgressPostgresFixture();
        var (service, _) = fixture.Service();
        var date = new DateOnly(2026, 10, 6);
        var one = service.CreateTask("pg-owner", ProgressTestStores.Task("Duplicate", date: date));
        var two = service.CreateTask("pg-owner", ProgressTestStores.Task("Duplicate", date: date));
        service.UpdateTaskStatus("pg-owner", one.Id, new(TaskStatus.Done, false, null));
        service.UpdateTaskStatus("pg-owner", two.Id, new(TaskStatus.Done, false, null));
        service.GetTasks("pg-owner", TaskQuery.FromHttp(ProgressTestStores.Query()));
        Assert.Equal(2, Report(service).SelectedDay.TotalCount);
        var ids = Report(service).SelectedDay.Events.Select(e => Guid.Parse(e.TaskId)).ToArray();
        service.DeleteTasks("pg-owner", ids);
        var (fresh, _) = fixture.Service();
        Assert.Equal(2, Report(fresh).SelectedDay.TotalCount);
        Assert.All(Report(fresh).SelectedDay.Events, e => Assert.Null(e.TaskHref));
    }
}
