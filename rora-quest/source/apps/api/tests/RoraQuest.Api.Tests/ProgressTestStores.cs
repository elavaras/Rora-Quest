using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using RoraQuest.Api.Progress;

internal static class ProgressTestStores
{
    private static readonly ConcurrentBag<ProgressSqliteStore> Stores = new();
    static ProgressTestStores()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var store in Stores) store.Dispose(); };
    }
    public static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RoraQuestProgressTests", Guid.NewGuid().ToString("N"));
        ProgressSqliteStore.Initialize(path);
        return path;
    }
    public static InMemoryRoraQuestStore Create(AppState? state = null, TimeProvider? clock = null, ProgressFaults? faults = null)
    {
        var sidecar = new ProgressSqliteStore(NewDirectory());
        Stores.Add(sidecar);
        var ledger = new ProgressLedger(sidecar.Open, false, clock ?? TimeProvider.System, faults);
        ledger.Start();
        return new(state ?? new AppState(), ledger);
    }
    public static IQueryCollection Query(string query = "") => new QueryCollection(QueryHelpers.ParseQuery(query));
    public static CreateTaskRequest Task(string title = "Task", TaskStatus? status = null, DateOnly? week = null, DateOnly? date = null) =>
        new(title, null, null, null, week, date, null, null, null, null, status, null);
}

internal sealed class ProgressTestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class ThrowProgressFaults : ProgressFaults
{
    public string? Boundary { get; set; }
    public bool FailResolution { get; set; }
    public override void At(string boundary)
    {
        if (boundary == Boundary || (FailResolution && boundary == "resolve"))
            throw new IOException($"Injected failure at {boundary}");
    }
}
