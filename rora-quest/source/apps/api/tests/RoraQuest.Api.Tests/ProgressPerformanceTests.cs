using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

public sealed class ProgressPerformanceFactAttribute : FactAttribute
{
    public ProgressPerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_RUN_PERFORMANCE") != "1")
            Skip = "Opt-in 1,000-task / 10,000-completion real SQLite HTTP benchmark: set RORAQUEST_PROGRESS_RUN_PERFORMANCE=1.";
    }
}

public class ProgressPerformanceTests(ITestOutputHelper output)
{
    [ProgressPerformanceFact]
    public async Task DefaultAnd84DayReports_StayBoundedAtTenThousandRecordedUnits()
    {
        await using var factory = new ProgressHttpFactory();
        using var client = factory.Client();
        var service = factory.Services.GetRequiredService<RoraQuestService>();
        var fixture = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            var task = service.CreateTask("one", ProgressTestStores.Task($"Fixture {i}", week: new(2026, 10, 5)));
            for (var step = 0; step < 10; step++)
            {
                var unit = service.CreateSubstep("one", task.Id, new($"Unit {step}", 1)).Value!;
                service.UpdateSubstep("one", task.Id, unit.Id, new(null, true, null));
            }
        }
        output.WriteLine($"Environment: {Environment.OSVersion}; .NET {Environment.Version}; logical CPUs {Environment.ProcessorCount}. Fixture: {fixture.Elapsed}.");
        foreach (var path in new[] { "/api/progress", "/api/progress?from=2026-07-20&to=2026-10-11" })
        {
            for (var warm = 0; warm < 2; warm++) (await client.GetAsync(path)).EnsureSuccessStatusCode();
            var samples = new List<double>();
            long bytes = 0;
            for (var sample = 0; sample < 20; sample++)
            {
                var watch = Stopwatch.StartNew();
                using var response = await client.GetAsync(path);
                response.EnsureSuccessStatusCode();
                bytes = (await response.Content.ReadAsByteArrayAsync()).LongLength;
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            var p95 = samples[18];
            output.WriteLine($"{path}: n=20; median={samples[10]:F2}ms; p95={p95:F2}ms; bytes={bytes}.");
            Assert.True(p95 <= 2000, $"p95 {p95} ms exceeds the 2s acceptance target.");
        }
        var report = await ProgressHttpTests.Json(await client.GetAsync("/api/progress"));
        Assert.Equal(10000, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        Assert.Equal(100, report.GetProperty("selectedDay").GetProperty("events").GetArrayLength());
        Assert.Equal(1000, report.GetProperty("weeklyPlan").GetProperty("totalTasks").GetInt32());
    }
}
