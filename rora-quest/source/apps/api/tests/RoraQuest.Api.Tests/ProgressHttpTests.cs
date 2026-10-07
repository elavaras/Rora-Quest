using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoraQuest.Api.Progress;
using Xunit;

internal sealed class ProgressHttpFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = ProgressTestStores.NewDirectory();
    public ProgressTestClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-06T06:00:00Z"));
    public ThrowProgressFaults Faults { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("EntraAuth:ClientId", "");
        builder.UseSetting("EntraAuth:ClientSecret", "");
        builder.UseSetting("ConnectionStrings:Postgres", "");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<ProgressSqliteStore>();
            services.RemoveAll<ProgressLedger>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(_ => new ProgressSqliteStore(DataDirectory));
            services.AddSingleton(sp => new ProgressLedger(sp.GetRequiredService<ProgressSqliteStore>().Open, false, Clock, Faults));
        });
    }
    public HttpClient Client(string owner = "one")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", owner);
        return client;
    }
}

public class ProgressHttpTests
{
    internal static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    internal static async Task<string> CreateTask(HttpClient client, string title = "Task")
    {
        var body = await Json(await client.PostAsJsonAsync("/api/tasks", new { title, plannedWeekStart = "2026-10-05" }));
        return body.GetProperty("id").GetString()!;
    }
    internal static async Task Complete(HttpClient client, string id) =>
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/status", new { status = "Done", overrideIncompleteSubsteps = false }));

    [Theory]
    [InlineData("?from=2026-10-06")]
    [InlineData("?to=2026-10-06")]
    [InlineData("?from=2026-02-30&to=2026-03-01")]
    [InlineData("?from=2026-10-07&to=2026-10-06")]
    [InlineData("?from=2026-01-01&to=2026-03-26")]
    [InlineData("?from=2026-10-06T00:00:00Z&to=2026-10-06")]
    [InlineData("?from=9999-12-27&to=9999-12-27")]
    [InlineData("?timeZone=utc")]
    [InlineData("?timeZone=")]
    [InlineData("?timeZone=UTC&timeZone=UTC")]
    [InlineData("?from=2026-10-06&from=2026-10-06&to=2026-10-06")]
    [InlineData("?owner=another")]
    [InlineData("?asOf=2026-10-06")]
    [InlineData("?From=2026-10-06&to=2026-10-06")]
    [InlineData("?selectedDate=2020-01-01")]
    public async Task StrictQueryErrors_Are400NotSuccessfulDefaults(string query)
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var response = await client.GetAsync("/api/progress" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalidProgressQuery", error.GetProperty("code").GetString());
        Assert.NotEmpty(error.GetProperty("errors").EnumerateObject());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("?from=0001-01-01&to=0001-01-01", 1)]
    [InlineData("?from=9999-12-26&to=9999-12-26&timeZone=America/New_York", 1)]
    [InlineData("?from=2026-01-01&to=2026-03-25", 84)]
    [InlineData("", 28)]
    public async Task ExactResponseContractAndValidBounds(string query, int count)
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var response = await client.GetAsync("/api/progress" + query);
        var body = await Json(response);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(count, body.GetProperty("days").GetArrayLength());
        Assert.Equal("0", body.GetProperty("snapshotSequence").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("lastActivity").GetProperty("event").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("selectedDay").GetProperty("nextCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("weeklyPlan").GetProperty("progressPercent").ValueKind);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{6}Z$", body.GetProperty("asOfUtc").GetString()!);
        Assert.All(body.EnumerateObject(), p => Assert.True(char.IsLower(p.Name[0])));
        foreach (var area in new[] { "days", "selectedDay", "participation", "weeklyPlan", "longestGap", "currentGap", "lastActivity" })
            Assert.True(body.TryGetProperty(area, out _));
    }

    [Fact]
    public async Task ActualRoutes_RecognizeOnlyAcceptedFirstTransitionsPreservingTerminalPolicy()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var id = await CreateTask(client, "Parent");
        var step = await Json(await client.PostAsJsonAsync($"/api/tasks/{id}/substeps", new { title = "Part", weight = 1 }));
        var stepId = step.GetProperty("id").GetString();
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/status", new { status = "Cancelled" }));
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/substeps/{stepId}", new { title = "Snapshot", isDone = true }));
        var report = await Json(await client.GetAsync("/api/progress?timeZone=UTC"));
        Assert.Equal(1, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        var task = await Json(await client.GetAsync($"/api/tasks/{id}"));
        Assert.Equal("Cancelled", task.GetProperty("status").GetString());
        var conflict = await client.PatchAsJsonAsync($"/api/tasks/{id}/substeps/{stepId}", new { isDone = false, ifMatchVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/substeps/{stepId}", new { isDone = false }));
        await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/substeps/{stepId}", new { isDone = true }));
        var taskOnly = await CreateTask(client, "No substeps");
        await Task.WhenAll(Complete(client, taskOnly), Complete(client, taskOnly));
        report = await Json(await client.GetAsync("/api/progress?timeZone=UTC"));
        Assert.Equal(2, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        using var other = factory.Client("other");
        var isolated = await Json(await other.GetAsync("/api/progress"));
        Assert.Equal(0, isolated.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/tasks/{id}")).StatusCode);
        foreach (var path in new[] { "/api/scorecard", "/api/reports/progress", "/api/reports/timeline", "/api/tracking/streaks", "/api/tracking/consistency", "/api/planning/recommendation" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Paging_IsBoundedStableAndOwnerBoundDespiteConcurrentAdditions()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var id = await CreateTask(client, "Many units");
        var stepIds = new List<string>();
        for (var i = 0; i < 103; i++)
        {
            var step = await Json(await client.PostAsJsonAsync($"/api/tasks/{id}/substeps", new { title = $"Unit {i}", weight = 1 }));
            var sid = step.GetProperty("id").GetString()!;
            stepIds.Add(sid);
            await Json(await client.PatchAsJsonAsync($"/api/tasks/{id}/substeps/{sid}", new { isDone = true }));
        }
        var report = await Json(await client.GetAsync("/api/progress?timeZone=UTC"));
        var first = report.GetProperty("selectedDay");
        Assert.Equal(103, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, first.GetProperty("events").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString()!;
        var extra = await CreateTask(client, "Later");
        await Complete(client, extra);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tasks/{id}")).StatusCode);
        var second = await Json(await client.GetAsync("/api/progress/events?cursor=" + Uri.EscapeDataString(cursor)));
        Assert.Equal(103, second.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, second.GetProperty("events").GetArrayLength());
        Assert.Equal(first.GetProperty("snapshotSequence").GetString(), second.GetProperty("snapshotSequence").GetString());
        Assert.Equal(first.GetProperty("asOfUtc").GetString(), second.GetProperty("asOfUtc").GetString());
        Assert.All(second.GetProperty("events").EnumerateArray(), e => Assert.Equal("taskRemoved", e.GetProperty("availability").GetString()));
        var keys = first.GetProperty("events").EnumerateArray().Concat(second.GetProperty("events").EnumerateArray())
            .Select(e => e.GetProperty("unitKey").GetString()!).ToArray();
        Assert.Equal(103, keys.Distinct().Count());
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        using var other = factory.Client("other");
        Assert.Equal(HttpStatusCode.BadRequest, (await other.GetAsync("/api/progress/events?cursor=" + cursor)).StatusCode);
        foreach (var suffix in new[] { "", "?cursor=not-json", "?cursor=" + cursor + "&extra=1", "?cursor=" + cursor + "&cursor=" + cursor })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/progress/events" + suffix)).StatusCode);
    }

    [Fact]
    public async Task FailureIsUnavailable_NotAnEmptySuccess_AndMutationIsNotAcknowledged()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var id = await CreateTask(client);
        factory.Faults.Boundary = "afterEvent";
        // TestServer propagates the existing unhandled persistence-error path; Kestrel returns 500.
        // Do not alter the legacy mutation error contract to accommodate the test transport.
        await Assert.ThrowsAsync<ProgressUnavailableException>(() =>
            client.PatchAsJsonAsync($"/api/tasks/{id}/status", new { status = "Done" }));
        factory.Faults.Boundary = null;
        var task = await Json(await client.GetAsync($"/api/tasks/{id}"));
        Assert.Equal("Todo", task.GetProperty("status").GetString());
        var report = await Json(await client.GetAsync("/api/progress"));
        Assert.Equal(0, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        factory.Faults.Boundary = "certification";
        var failed = await client.GetAsync("/api/progress");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var error = JsonDocument.Parse(await failed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("progressUnavailable", error.GetProperty("code").GetString());
        Assert.Empty(error.GetProperty("errors").EnumerateObject());
        factory.Faults.Boundary = null;
    }
}
