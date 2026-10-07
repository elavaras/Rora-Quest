using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Xunit;

internal sealed class ProgressApiProcess : IAsyncDisposable
{
    private readonly Process _process;
    public HttpClient Client { get; }
    private readonly StringBuilder _output;
    public static string ApiRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoraQuest.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Cannot locate API solution.");
        }
    }
    private ProgressApiProcess(Process process, int port, StringBuilder output)
    {
        _process = process;
        _output = output;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20) };
        Client.DefaultRequestHeaders.Add("X-User-Id", "process-owner");
    }
    public static async Task<ProgressApiProcess> Start(string? directory, string? postgres = null, object? fixture = null, bool production = false)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var dll = production
            ? Path.Combine(ApiRoot, "src", "RoraQuest.Api", "bin", "Release", "net8.0", "RoraQuest.Api.dll")
            : Path.Combine(ApiRoot, "tests", "RoraQuest.Progress.TestHost", "bin", "Release", "net8.0", "RoraQuest.Progress.TestHost.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ApiRoot };
        start.ArgumentList.Add(dll);
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["EntraAuth__ClientId"] = "";
        start.Environment["EntraAuth__ClientSecret"] = "";
        start.Environment["ConnectionStrings__Postgres"] = production ? postgres ?? "" : "";
        start.Environment["RORAQUEST_PROGRESS_TEST_POSTGRES"] = postgres ?? "";
        start.Environment["Progress__DataDirectory"] = directory ?? "";
        start.Environment["Postgres__MigrationsPath"] = Path.GetFullPath(Path.Combine(ApiRoot, "..", "..", "..", "infra", "sql"));
        start.Environment["Postgres__RunSeed"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment.Remove("RORAQUEST_PROGRESS_TEST_FIXTURE");
        if (fixture is not null) start.Environment["RORAQUEST_PROGRESS_TEST_FIXTURE"] = JsonSerializer.Serialize(fixture);
        var output = new StringBuilder();
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var host = new ProgressApiProcess(process, port, output);
        try
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (process.HasExited) throw new InvalidOperationException("Child API exited: " + output);
                try { if ((await host.Client.GetAsync("/health")).IsSuccessStatusCode) return host; }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Child API failed readiness: " + output);
        }
        catch { await host.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true); // Only the precise child owned by this fixture.
            await _process.WaitForExitAsync();
        }
        _process.Dispose();
    }

    public static async Task<(int ExitCode, string Output)> Offline(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = ApiRoot
        };
        start.ArgumentList.Add(Path.Combine(ApiRoot, "src", "RoraQuest.Api", "bin", "Release", "net8.0", "RoraQuest.Api.dll"));
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["Progress__DataDirectory"] = directory;
        start.Environment["ConnectionStrings__Postgres"] = "";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, (await stdout) + (await stderr));
    }
}

public class ProgressProcessTests
{
    [Fact]
    public async Task ProductionApi_FreshProcessPreservesHistoryAndZeroEventOwnerButNotVolatileTasks()
    {
        var directory = ProgressTestStores.NewDirectory();
        JsonElement before;
        string taskId, zeroStart;
        await using (var first = await ProgressApiProcess.Start(directory, production: true))
        {
            taskId = await ProgressHttpTests.CreateTask(first.Client, "Retained across crash");
            await ProgressHttpTests.Complete(first.Client, taskId);
            before = await ProgressHttpTests.Json(await first.Client.GetAsync("/api/progress"));
            first.Client.DefaultRequestHeaders.Remove("X-User-Id");
            first.Client.DefaultRequestHeaders.Add("X-User-Id", "no-events");
            var zero = await ProgressHttpTests.Json(await first.Client.GetAsync("/api/progress"));
            zeroStart = zero.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
        }
        await using var fresh = await ProgressApiProcess.Start(directory, production: true);
        var after = await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"));
        Assert.Equal(before.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString(),
            after.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.Equal(before.GetProperty("snapshotSequence").GetString(), after.GetProperty("snapshotSequence").GetString());
        var activity = after.GetProperty("lastActivity").GetProperty("event");
        Assert.Equal("Retained across crash", activity.GetProperty("taskTitle").GetString());
        Assert.Equal("taskRemoved", activity.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, activity.GetProperty("taskHref").ValueKind);
        Assert.True(after.GetProperty("coverage").GetProperty("hasInterruptions").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await fresh.Client.GetAsync("/api/tasks/" + taskId)).StatusCode);
        fresh.Client.DefaultRequestHeaders.Remove("X-User-Id");
        fresh.Client.DefaultRequestHeaders.Add("X-User-Id", "no-events");
        var zeroAfter = await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"));
        Assert.Equal(zeroStart, zeroAfter.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.Equal("none", zeroAfter.GetProperty("lastActivity").GetProperty("state").GetString());
    }

    [Fact]
    public async Task FreshProcess_TestOnlyRestoredIdentityCannotReearnActivity()
    {
        var directory = ProgressTestStores.NewDirectory();
        var fixture = new { Owner = "process-owner", TaskId = Guid.NewGuid(), SubstepId = (Guid?)Guid.NewGuid(), Title = "Same identity" };
        string sequence, occurred;
        var path = $"/api/tasks/{fixture.TaskId}/substeps/{fixture.SubstepId}";
        await using (var first = await ProgressApiProcess.Start(directory, fixture: fixture))
        {
            await ProgressHttpTests.Json(await first.Client.PatchAsJsonAsync(path, new { isDone = true }));
            var report = await ProgressHttpTests.Json(await first.Client.GetAsync("/api/progress"));
            sequence = report.GetProperty("snapshotSequence").GetString()!;
            occurred = report.GetProperty("lastActivity").GetProperty("event").GetProperty("occurredAtUtc").GetString()!;
        }
        await using var fresh = await ProgressApiProcess.Start(directory, fixture: fixture);
        await ProgressHttpTests.Json(await fresh.Client.PatchAsJsonAsync(path, new { isDone = true }));
        var after = await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"));
        Assert.Equal(sequence, after.GetProperty("snapshotSequence").GetString());
        Assert.Equal(occurred, after.GetProperty("lastActivity").GetProperty("event").GetProperty("occurredAtUtc").GetString());
        Assert.Equal(1, after.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
        var different = await ProgressHttpTests.CreateTask(fresh.Client, "Same identity");
        await ProgressHttpTests.Complete(fresh.Client, different);
        Assert.Equal(2, (await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"))).GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task OfflineCommands_RequireStoppedWritersAndPreserveEvidenceAcrossSuspension()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoraQuestProgressTests", Guid.NewGuid().ToString("N"));
        Assert.Equal(0, (await ProgressApiProcess.Offline(directory, "--progress-store-init")).ExitCode);
        Assert.NotEqual(0, (await ProgressApiProcess.Offline(directory, "--progress-store-init")).ExitCode);
        string trackingStart;
        await using (var live = await ProgressApiProcess.Start(directory, production: true))
        {
            var id = await ProgressHttpTests.CreateTask(live.Client, "Before maintenance");
            await ProgressHttpTests.Complete(live.Client, id);
            var report = await ProgressHttpTests.Json(await live.Client.GetAsync("/api/progress"));
            trackingStart = report.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString()!;
            Assert.NotEqual(0, (await ProgressApiProcess.Offline(directory, "--progress-capture-suspend", "--confirm-writers-stopped")).ExitCode);
        }
        Assert.NotEqual(0, (await ProgressApiProcess.Offline(directory, "--progress-capture-suspend")).ExitCode);
        Assert.NotEqual(0, (await ProgressApiProcess.Offline(directory, "--progress-capture-suspend", "--confirm-writers-stopped", "--unknown-sinze", trackingStart)).ExitCode);
        var suspended = await ProgressApiProcess.Offline(directory, "--progress-capture-suspend", "--confirm-writers-stopped", "--unknown-since", trackingStart);
        Assert.Equal(0, suspended.ExitCode);
        Assert.Contains("operation=", suspended.Output);
        Assert.Contains("boundary=" + trackingStart, suspended.Output);
        await using (var reader = await ProgressApiProcess.Start(directory, production: true))
        {
            var report = await ProgressHttpTests.Json(await reader.Client.GetAsync("/api/progress"));
            Assert.False(report.GetProperty("coverage").GetProperty("captureReliableNow").GetBoolean());
            Assert.Equal(1, report.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
            var task = await ProgressHttpTests.CreateTask(reader.Client);
            var rejected = await reader.Client.PatchAsJsonAsync("/api/tasks/" + task + "/status", new { status = "Done" });
            Assert.False(rejected.IsSuccessStatusCode);
        }
        var resumed = await ProgressApiProcess.Offline(directory, "--progress-capture-resume", "--confirm-writers-stopped");
        Assert.Equal(0, resumed.ExitCode);
        await using var fresh = await ProgressApiProcess.Start(directory, production: true);
        var after = await ProgressHttpTests.Json(await fresh.Client.GetAsync("/api/progress"));
        Assert.True(after.GetProperty("coverage").GetProperty("captureReliableNow").GetBoolean());
        Assert.True(after.GetProperty("coverage").GetProperty("hasInterruptions").GetBoolean());
        Assert.Equal(trackingStart, after.GetProperty("coverage").GetProperty("trackingStartedAtUtc").GetString());
        Assert.Equal(1, after.GetProperty("selectedDay").GetProperty("totalCount").GetInt32());
    }
}
