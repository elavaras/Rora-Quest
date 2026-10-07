using RoraQuest.Api.Progress;
using Xunit;

public class ProgressRegressionTests
{
    private static ProgressTestClock Clock() => new(DateTimeOffset.Parse("2026-10-06T06:00:00Z"));
    private static ProgressReport Report(RoraQuestService service) => service.GetActivityProgress("one", ProgressTestStores.Query("timeZone=UTC"));

    [Fact]
    public void ZeroWeightNearHundredOverridesAndTerminalStates_UseUnchangedWeightedHelper()
    {
        var service = new RoraQuestService(ProgressTestStores.Create(clock: Clock()));
        var week = new DateOnly(2026, 10, 5);
        var count = service.CreateTask("one", ProgressTestStores.Task("Count", week: week));
        var done = service.CreateSubstep("one", count.Id, new("Done", 0)).Value!;
        service.CreateSubstep("one", count.Id, new("Not yet", 0));
        service.UpdateSubstep("one", count.Id, done.Id, new(null, true, null));
        service.UpdateTaskStatus("one", count.Id, new(TaskStatus.Done, true, null));
        var near = service.CreateTask("one", ProgressTestStores.Task("Near", TaskStatus.Skipped, week));
        var almost = service.CreateSubstep("one", near.Id, new("Most", 999)).Value!;
        service.CreateSubstep("one", near.Id, new("Remainder", 1));
        service.UpdateSubstep("one", near.Id, almost.Id, new(null, true, null));
        var report = Report(service);
        var row = report.WeeklyPlan.Tasks.Single(t => t.TaskId == count.Id.ToString());
        Assert.Equal(50, row.ProgressPercent);
        Assert.Equal("substepCount", row.ProgressBasis);
        Assert.False(row.IsComplete);
        row = report.WeeklyPlan.Tasks.Single(t => t.TaskId == near.Id.ToString());
        Assert.Equal(99.9, row.ProgressPercent);
        Assert.Equal("Skipped", row.Status);
        Assert.False(row.IsComplete);
        Assert.Equal(0, report.WeeklyPlan.CompleteTasks);
        Assert.Equal(2, report.SelectedDay.TotalCount);
    }

    [Fact]
    public void MetadataScheduleWeightsSeededDoneAndParentOverride_DoNotCreateActivity()
    {
        var state = new AppState();
        var service = new RoraQuestService(ProgressTestStores.Create(state, Clock()));
        var task = service.CreateTask("one", ProgressTestStores.Task("Created done", TaskStatus.Done));
        var first = service.CreateSubstep("one", task.Id, new("Part", 1)).Value!;
        // Legacy/harness state is deliberately populated with old completion metadata.
        first.IsDone = true;
        first.CompletedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        task.StatusEvents.Add(new(Guid.NewGuid(), TaskStatus.Todo, TaskStatus.Done, first.CompletedAt.Value));
        task.Title = "Edit";
        task.PlannedDate = new(2026, 10, 7);
        first.Weight = 99;
        Assert.Equal("none", Report(service).LastActivity.State);
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, true, null));
        Assert.Equal("0", Report(service).SnapshotSequence);
        service.UpdateSubstep("one", task.Id, first.Id, new("Metadata only", null, null));
        Assert.Equal("0", Report(service).SnapshotSequence);
        service.UpdateSubstep("one", task.Id, first.Id, new(null, false, null));
        service.UpdateSubstep("one", task.Id, first.Id, new(null, true, null));
        Assert.Single(Report(service).SelectedDay.Events); // genuine first observed transition after rollout
    }

    [Fact]
    public void DuplicateHealingDoesNotRunOnProgressRead_AndCannotEraseHistory()
    {
        var service = new RoraQuestService(ProgressTestStores.Create(clock: Clock()));
        var week = new DateOnly(2026, 10, 5);
        var first = service.CreateTask("one", ProgressTestStores.Task("Repeated", week: week, date: week));
        var second = service.CreateTask("one", ProgressTestStores.Task("Repeated", week: week, date: week));
        service.UpdateTaskStatus("one", first.Id, new(TaskStatus.Done, false, null));
        service.UpdateTaskStatus("one", second.Id, new(TaskStatus.Done, false, null));
        Assert.Equal(2, Report(service).WeeklyPlan.TotalTasks);
        Assert.Equal(2, Report(service).SelectedDay.TotalCount);
        Assert.Single(service.GetTasks("one", TaskQuery.FromHttp(ProgressTestStores.Query())));
        var after = Report(service);
        Assert.Equal(2, after.SelectedDay.TotalCount);
        Assert.Single(after.SelectedDay.Events.Where(e => e.Availability == "taskRemoved"));
        Assert.Single(after.SelectedDay.Events.Where(e => e.Availability == "available"));
    }

    [Fact]
    public void DsaRejectionAndSubstepNoop_DoNotCreateEventsOrChangeVersions()
    {
        var service = new RoraQuestService(ProgressTestStores.Create(clock: Clock()));
        var category = service.CreateCategory("one", new("DSA", null));
        var request = ProgressTestStores.Task() with { CategoryId = category.Id };
        var task = service.CreateTask("one", request);
        Assert.Equal(400, service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, true, null)).StatusCode);
        var step = task.SubSteps[0];
        var taskVersion = task.RowVersion;
        var stepVersion = step.RowVersion;
        Assert.Equal(200, service.UpdateSubstep("one", task.Id, step.Id, new(null, false, null)).StatusCode);
        Assert.Equal(taskVersion, task.RowVersion);
        Assert.Equal(stepVersion, step.RowVersion);
        Assert.Equal(400, service.UpdateSubstep("one", task.Id, step.Id, new("Disallowed rename", true, null)).StatusCode);
        Assert.Empty(Report(service).SelectedDay.Events);
    }

    [Fact]
    public void BackwardClockFailsClosed_AndLongHeartbeatGapCreatesNewSession()
    {
        var clock = Clock();
        var store = ProgressTestStores.Create(clock: clock);
        var service = new RoraQuestService(store);
        var task = service.CreateTask("one", ProgressTestStores.Task());
        Report(service);
        var start = clock.UtcNow;
        clock.UtcNow = start.AddSeconds(-1);
        Assert.Throws<ProgressUnavailableException>(() => service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null)));
        Assert.Equal(TaskStatus.Todo, task.Status);
        clock.UtcNow = start.AddSeconds(91);
        store.Progress.Heartbeat();
        Assert.True(Report(service).Coverage.HasInterruptions);
        Assert.Empty(Report(service).SelectedDay.Events);
    }

    [Fact]
    public void CurrentGapUnavailableForYesterdayUnknown_EvenWithFullToday()
    {
        var asOf = DateTimeOffset.Parse("2026-10-06T12:00:00Z");
        long Us(string s) => ReportingDates.Micros(DateTimeOffset.Parse(s));
        var evidence = new ProgressEvidence(asOf, 0, Us("2026-10-01T00:00:00Z"), true,
            [new(Us("2026-10-01T00:00:00Z"), Us("2026-10-03T00:00:00Z")), new(Us("2026-10-06T00:00:00Z"), ReportingDates.Micros(asOf))]);
        var current = ProgressProjection.Current(evidence, ReportingDates.Zone("UTC"), null);
        Assert.Equal("unavailable", current.State);
        Assert.Equal("yesterdayUnknown", current.Reason);
        Assert.Null(current.Days);
        Assert.Equal("2026-10-05", current.ThroughDate);
    }
}
