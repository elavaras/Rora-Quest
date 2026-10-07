using Dapper;
using RoraQuest.Api.Progress;
using Xunit;

public class ProgressStoreTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-06T06:00:00Z");
    private static ProgressReport Report(RoraQuestService service, string owner = "one") =>
        service.GetActivityProgress(owner, ProgressTestStores.Query("timeZone=UTC"));

    [Fact]
    public void FirstTransitionsOnly_SnapshotRenameDeleteAndForeverDedup()
    {
        var clock = new ProgressTestClock(Start);
        var store = ProgressTestStores.Create(clock: clock);
        var service = new RoraQuestService(store);
        var task = service.CreateTask("one", ProgressTestStores.Task("Original"));
        var step = service.CreateSubstep("one", task.Id, new("Before", 1)).Value!;
        service.UpdateSubstep("one", task.Id, step.Id, new("At completion", true, null));
        Assert.True(step.IsDone);
        Assert.Equal(TaskStatus.Done, task.Status);
        var first = Report(service);
        Assert.Single(first.SelectedDay.Events);
        Assert.Equal("substep", first.SelectedDay.Events[0].Kind);
        Assert.Equal("At completion", first.SelectedDay.Events[0].SubstepTitle);
        service.UpdateSubstep("one", task.Id, step.Id, new("Renamed", false, null));
        clock.UtcNow = clock.UtcNow.AddDays(1);
        service.UpdateSubstep("one", task.Id, step.Id, new(null, true, null));
        Assert.Equal(first.LastActivity.Event, Report(service).LastActivity.Event);
        service.DeleteSubstep("one", task.Id, step.Id);
        Assert.Equal("substepRemoved", Report(service).LastActivity.Event!.Availability);
        Assert.Null(Report(service).LastActivity.Event!.TaskHref);
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Todo, false, null));
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null));
        Assert.Equal("task", Report(service).LastActivity.Event!.Kind);
        Assert.Equal(2, Report(service).Days.Sum(d => d.UnitCount));
        Assert.True(long.Parse(Report(service).SnapshotSequence) > long.Parse(first.SnapshotSequence)); // identity sequences may have conflict gaps
        service.DeleteTask("one", task.Id);
        Assert.Equal("taskRemoved", Report(service).LastActivity.Event!.Availability);
        Assert.Equal("Original", Report(service).LastActivity.Event!.TaskTitle);
        Assert.Empty(Report(service, "another").SelectedDay.Events);
    }

    [Theory]
    [InlineData("beforeEvent")]
    [InlineData("afterEvent")]
    [InlineData("afterAggregate")]
    [InlineData("beforeCommit")]
    [InlineData("certification")]
    public void KnownRollback_DoesNotPublishIntoHeldTaskOrSubstepReferences(string boundary)
    {
        var faults = new ThrowProgressFaults();
        var store = ProgressTestStores.Create(clock: new ProgressTestClock(Start), faults: faults);
        var service = new RoraQuestService(store);
        var task = service.CreateTask("one", ProgressTestStores.Task());
        var step = service.CreateSubstep("one", task.Id, new("Step", 1)).Value!;
        var version = task.RowVersion;
        faults.Boundary = boundary;
        Assert.Throws<ProgressUnavailableException>(() => service.UpdateSubstep("one", task.Id, step.Id, new("Changed", true, null)));
        Assert.False(step.IsDone);
        Assert.Equal("Step", step.Title);
        Assert.Equal(TaskStatus.Todo, task.Status);
        Assert.Equal(version, task.RowVersion);
        Assert.Empty(task.StatusEvents);
        faults.Boundary = null;
        Assert.Empty(Report(service).SelectedDay.Events);
        Assert.Same(task, service.GetTask("one", task.Id));
    }

    [Fact]
    public void LostCommitResponse_ResolvesReceiptAndRetryCannotDuplicate()
    {
        var faults = new ThrowProgressFaults();
        var service = new RoraQuestService(ProgressTestStores.Create(clock: new ProgressTestClock(Start), faults: faults));
        var task = service.CreateTask("one", ProgressTestStores.Task());
        faults.Boundary = "afterCommit";
        Assert.Equal(200, service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null)).StatusCode);
        Assert.Equal(TaskStatus.Done, task.Status);
        faults.Boundary = null;
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Todo, false, null));
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null));
        Assert.Single(Report(service).SelectedDay.Events);
    }

    [Fact]
    public void UnresolvableCommit_FencesOwnerAndFreshStoreRetainsCommittedEvidence()
    {
        var path = ProgressTestStores.NewDirectory();
        var clock = new ProgressTestClock(Start);
        using (var sidecar = new ProgressSqliteStore(path))
        {
            var faults = new ThrowProgressFaults();
            var ledger = new ProgressLedger(sidecar.Open, false, clock, faults);
            ledger.Start();
            var service = new RoraQuestService(new InMemoryRoraQuestStore(new(), ledger));
            var task = service.CreateTask("one", ProgressTestStores.Task());
            faults.Boundary = "afterCommit";
            faults.FailResolution = true;
            Assert.Throws<ProgressUnavailableException>(() => service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null)));
            Assert.Equal(TaskStatus.Todo, task.Status);
            faults.Boundary = null;
            faults.FailResolution = false;
            Assert.Throws<ProgressUnavailableException>(() => service.GetTask("one", task.Id));
            Assert.Throws<ProgressUnavailableException>(() => Report(service));
        }
        using var fresh = new ProgressSqliteStore(path);
        var freshLedger = new ProgressLedger(fresh.Open, false, clock);
        freshLedger.Start();
        var report = Report(new(new InMemoryRoraQuestStore(new(), freshLedger)));
        Assert.Single(report.SelectedDay.Events);
        Assert.Equal("taskRemoved", report.SelectedDay.Events[0].Availability);
    }

    [Fact]
    public void InitializationFailure_DoesNotCacheOwnerStart()
    {
        var path = ProgressTestStores.NewDirectory();
        using var sidecar = new ProgressSqliteStore(path);
        var faults = new ThrowProgressFaults { Boundary = "owner" };
        var clock = new ProgressTestClock(Start);
        var ledger = new ProgressLedger(sidecar.Open, false, clock, faults);
        ledger.Start();
        var service = new RoraQuestService(new InMemoryRoraQuestStore(new(), ledger));
        Assert.Throws<IOException>(() => Report(service));
        clock.UtcNow = Start.AddMinutes(1);
        faults.Boundary = null;
        Assert.Equal(ReportingDates.Instant(clock.UtcNow), Report(service).Coverage.TrackingStartedAtUtc);
    }

    [Fact]
    public void FiniteCertification_HeartbeatFailureSuspendResumeAndRestartLeaveUnknownTails()
    {
        var path = ProgressTestStores.NewDirectory();
        var clock = new ProgressTestClock(Start);
        using (var sidecar = new ProgressSqliteStore(path))
        {
            var faults = new ThrowProgressFaults();
            var ledger = new ProgressLedger(sidecar.Open, false, clock, faults);
            ledger.Start();
            var service = new RoraQuestService(new InMemoryRoraQuestStore(new(), ledger));
            Report(service);
            clock.UtcNow = Start.AddSeconds(30);
            ledger.Heartbeat(); // independent of owner visits/activity
            clock.UtcNow = Start.AddSeconds(60);
            faults.Boundary = "certification";
            Assert.Throws<IOException>(ledger.Heartbeat);
            clock.UtcNow = Start.AddSeconds(90);
            faults.Boundary = null;
            ledger.Heartbeat();
            Assert.True(Report(service).Coverage.HasInterruptions);
            using var conn = sidecar.Open();
            Assert.Equal(2, conn.ExecuteScalar<int>("SELECT count(*) FROM progress_capture_sessions"));
        }
        clock.UtcNow = Start.AddHours(1);
        using var fresh = new ProgressSqliteStore(path);
        var resumed = new ProgressLedger(fresh.Open, false, clock);
        resumed.Maintenance(false);
        resumed.Start();
        var svc = new RoraQuestService(new InMemoryRoraQuestStore(new(), resumed));
        var task = svc.CreateTask("one", ProgressTestStores.Task());
        Assert.False(Report(svc).Coverage.CaptureReliableNow);
        Assert.Throws<ProgressUnavailableException>(() => svc.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null)));
        clock.UtcNow = Start.AddHours(2);
        resumed.Maintenance(true);
        resumed.Heartbeat();
        Assert.True(Report(svc).Coverage.HasInterruptions);
        Assert.True(Report(svc).Coverage.CaptureReliableNow);
    }

    [Fact]
    public void MissingConflictingOrCorruptSidecars_AreNeverEmptyHistory()
    {
        var path = ProgressTestStores.NewDirectory();
        using (var sidecar = new ProgressSqliteStore(path))
        {
            Assert.ThrowsAny<IOException>(() => new ProgressSqliteStore(path));
            Assert.ThrowsAny<IOException>(() => ProgressSqliteStore.Initialize(path));
        }
        Assert.Throws<InvalidOperationException>(() => ProgressSqliteStore.Initialize(path));
        File.Move(Path.Combine(path, "progress.sqlite"), Path.Combine(path, "retained.sqlite"));
        Assert.ThrowsAny<Exception>(() => new ProgressSqliteStore(path));
        Assert.ThrowsAny<Exception>(() => new ProgressSqliteStore(path + "-missing"));
    }

    [Fact]
    public void WeightedWeeklyPlan_UsesOrMembershipAndCurrentProgressNotStatus()
    {
        var service = new RoraQuestService(ProgressTestStores.Create(clock: new ProgressTestClock(Start)));
        var monday = new DateOnly(2026, 10, 5);
        var full = service.CreateTask("one", ProgressTestStores.Task("First", TaskStatus.Done, monday));
        var terminal = service.CreateTask("one", ProgressTestStores.Task("Terminal", TaskStatus.Cancelled, monday.AddDays(-7), monday.AddDays(1)));
        var tstep = service.CreateSubstep("one", terminal.Id, new("Done", 1)).Value!;
        service.UpdateSubstep("one", terminal.Id, tstep.Id, new(null, true, null));
        var partial = service.CreateTask("one", ProgressTestStores.Task("Partial", TaskStatus.Done, monday));
        var pstep = service.CreateSubstep("one", partial.Id, new("Quarter", 1)).Value!;
        service.CreateSubstep("one", partial.Id, new("Rest", 3));
        service.UpdateSubstep("one", partial.Id, pstep.Id, new(null, true, null));
        var report = service.GetActivityProgress("one", ProgressTestStores.Query("from=2026-10-06&to=2026-10-06&timeZone=UTC"));
        Assert.Equal(3, report.WeeklyPlan.TotalTasks);
        Assert.Equal(2, report.WeeklyPlan.CompleteTasks);
        Assert.Equal(75, report.WeeklyPlan.ProgressPercent);
        Assert.Equal(25, report.WeeklyPlan.Tasks.Single(t => t.TaskId == partial.Id.ToString()).ProgressPercent);
        Assert.True(report.WeeklyPlan.ExtendsOutsideRange);
        Assert.Equal("Cancelled", report.WeeklyPlan.Tasks.Single(t => t.TaskId == terminal.Id.ToString()).Status);
        Assert.Equal(2, report.SelectedDay.TotalCount); // creation as Done never creates history
        Assert.Empty(service.GetActivityProgress("one", ProgressTestStores.Query("from=2026-01-01&to=2026-01-01")).WeeklyPlan.Tasks);
        Assert.Equal(100, report.WeeklyPlan.Tasks.Single(t => t.TaskId == full.Id.ToString()).ProgressPercent);
    }

    [Theory]
    [InlineData(90, false)]
    [InlineData(91, true)]
    public void HeartbeatTimeout_HasAnExactFiniteThreshold(int seconds, bool interruption)
    {
        var clock = new ProgressTestClock(Start);
        var store = ProgressTestStores.Create(clock: clock);
        var service = new RoraQuestService(store);
        Report(service);
        clock.UtcNow = Start.AddSeconds(seconds);
        store.Progress.Heartbeat();
        Assert.Equal(interruption, Report(service).Coverage.HasInterruptions);
        Assert.Empty(Report(service).SelectedDay.Events);
    }

    [Fact]
    public void CorruptSidecarAndMissingExpectedRecords_FailStartup()
    {
        var path = ProgressTestStores.NewDirectory();
        using (var sidecar = new ProgressSqliteStore(path))
        {
            using var conn = sidecar.Open();
            conn.Execute("UPDATE progress_registration SET session_count=1");
        }
        Assert.Throws<ProgressUnavailableException>(() => new ProgressSqliteStore(path));
        var corrupt = ProgressTestStores.NewDirectory();
        File.WriteAllBytes(Path.Combine(corrupt, "progress.sqlite"), [0, 1, 2, 3, 4]);
        Assert.ThrowsAny<Exception>(() => new ProgressSqliteStore(corrupt));
    }

    [Fact]
    public void Suspension_AllowsReopeningButRejectsCompletionCandidates()
    {
        var store = ProgressTestStores.Create(clock: new ProgressTestClock(Start));
        var service = new RoraQuestService(store);
        var task = service.CreateTask("one", ProgressTestStores.Task());
        service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null));
        store.Progress.Maintenance(false);
        Assert.Equal(200, service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Todo, false, null)).StatusCode);
        Assert.Equal(TaskStatus.Todo, task.Status);
        Assert.Throws<ProgressUnavailableException>(() => service.UpdateTaskStatus("one", task.Id, new(TaskStatus.Done, false, null)));
        Assert.Single(Report(service).SelectedDay.Events);
        Assert.False(Report(service).Coverage.CaptureReliableNow);
    }
}
