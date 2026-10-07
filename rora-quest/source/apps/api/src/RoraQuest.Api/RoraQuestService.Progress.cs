using System.Globalization;
using RoraQuest.Api.Progress;

public sealed partial class RoraQuestService
{
    private IDisposable EnterOwnerScope(string owner)
    {
        Monitor.Enter(_gate);
        try
        {
            var scope = store.AcquireOwnerScope(owner);
            return new ProgressScope(() =>
            {
                try { scope.Dispose(); }
                finally { Monitor.Exit(_gate); }
            });
        }
        catch { Monitor.Exit(_gate); throw; }
    }

    private bool CommitProgressMutation(string owner, UserData user, TaskItem live, TaskItem staged, ProgressUnit? unit, DateTimeOffset now)
    {
        var tasks = new Dictionary<Guid, TaskItem>(user.Tasks) { [staged.Id] = staged };
        live.StatusEvents.EnsureCapacity(staged.StatusEvents.Count);
        if (!store.CommitTaskMutation(owner, user.WithTasks(tasks), unit, now, instant =>
        {
            staged.UpdatedAt = instant;
            for (var i = 0; i < staged.SubSteps.Count; i++)
                if (staged.SubSteps[i].IsDone && !live.SubSteps[i].IsDone) staged.SubSteps[i].CompletedAt = instant;
            for (var i = live.StatusEvents.Count; i < staged.StatusEvents.Count; i++) staged.StatusEvents[i].ChangedAt = instant;
        })) return false;
        // Publish into existing references only after durability. No I/O or allocation is needed here.
        live.Status = staged.Status;
        live.RowVersion = staged.RowVersion;
        live.UpdatedAt = staged.UpdatedAt;
        for (var i = 0; i < live.SubSteps.Count; i++)
        {
            var target = live.SubSteps[i];
            var source = staged.SubSteps[i];
            target.Title = source.Title;
            target.IsDone = source.IsDone;
            target.CompletedAt = source.CompletedAt;
            target.RowVersion = source.RowVersion;
        }
        for (var i = live.StatusEvents.Count; i < staged.StatusEvents.Count; i++)
            live.StatusEvents.Add(staged.StatusEvents[i]);
        return true;
    }

    public ProgressReport GetActivityProgress(string owner, IQueryCollection query)
    {
        // Validate even if storage is down, and then resolve defaults again at the locked snapshot.
        _ = ProgressQuery.Parse(query, store.Progress.Now);
        using var scope = EnterOwnerScope(owner);
        return store.ReadProgress(owner, (read, user) =>
        {
            var evidence = read.Evidence;
            var request = ProgressQuery.Parse(query, evidence.AsOf);
            var zone = ReportingDates.Zone(request.TimeZone);
            // Adjacent positive days disambiguate a coverage boundary from an activity boundary.
            // The public range remains exactly 1–84 dates; this internal aggregation is at most 86.
            var countFrom = request.From > ReportingDates.Min ? request.From.AddDays(-1) : request.From;
            var countTo = request.To < ReportingDates.Max ? request.To.AddDays(1) : request.To;
            var counts = read.Counts(countFrom, countTo, zone).ToDictionary(c => c.Date, StringComparer.Ordinal);
            ProgressDay Day(DateOnly date) => ProgressProjection.Day(date, zone, evidence, counts.GetValueOrDefault(ReportingDates.Key(date)));
            var days = Enumerable.Range(request.From.DayNumber, request.To.DayNumber - request.From.DayNumber + 1)
                .Select(n => Day(DateOnly.FromDayNumber(n))).ToArray();
            var today = ReportingDates.LocalDate(evidence.AsOf, zone);
            var latest = read.Latest();
            var asOf = ReportingDates.Instant(evidence.AsOf);
            var hasInterruptions = !ProgressProjection.Covered(evidence.ReliableIntervals, evidence.TrackingStart, ReportingDates.Micros(evidence.AsOf));
            return new ProgressReport(asOf, evidence.Sequence.ToString(CultureInfo.InvariantCulture), request.TimeZone,
                ReportingDates.Key(today), ReportingDates.Instant(new DateTimeOffset(ReportingDates.BoundaryTicks(today.AddDays(1), zone), TimeSpan.Zero)),
                ReportingDates.Key(request.From), ReportingDates.Key(request.To), ReportingDates.Key(request.SelectedDate), request.IsDefaultRange,
                new(ReportingDates.Instant(ReportingDates.FromMicros(evidence.TrackingStart)), evidence.CaptureReliableNow, hasInterruptions), days,
                new(days.Count(d => d.Status == "active"), days.Count(d => d.Position == "elapsed" && d.Coverage == "full"),
                    days.Count(d => d.Position == "elapsed" && d.Coverage != "full"), days.Any(d => d.Position == "today"),
                    days.Count(d => d.Position == "future")),
                ProgressProjection.Longest(days, Day), ProgressProjection.Current(evidence, zone, latest),
                new("allRecordedHistory", latest is null ? "none" : "recorded", latest is null ? null : ProgressProjection.Completion(latest, zone, user)),
                EventPage(owner, read, user, request.SelectedDate, request.TimeZone, evidence.AsOf, evidence.Sequence),
                WeeklyPlan(user, request, asOf));
        });
    }
    public ProgressEventPage GetActivityProgressEvents(string owner, IQueryCollection query)
    {
        var cursor = ProgressCursor.Parse(query, owner, store.Progress.Now);
        using var scope = EnterOwnerScope(owner);
        return store.ReadProgress(owner, (read, user) =>
        {
            var sequence = long.Parse(cursor.Sequence, CultureInfo.InvariantCulture);
            var asOf = ProgressCursor.ParseInstant(cursor.AsOf);
            if (sequence > read.Evidence.Sequence || asOf > read.Evidence.AsOf) throw ProgressCursor.Invalid();
            return EventPage(owner, read, user, ReportingDates.Parse(cursor.Date, "cursor"), cursor.Zone, asOf, sequence,
                ReportingDates.Micros(ProgressCursor.ParseInstant(cursor.After)), cursor.Key);
        });
    }
    private static ProgressEventPage EventPage(string owner, ProgressRead read, UserData user, DateOnly date, string zoneId,
        DateTimeOffset asOf, long sequence, long? after = null, string? key = null)
    {
        var zone = ReportingDates.Zone(zoneId);
        var (total, rows) = read.Page(date, zone, sequence, asOf, after, key);
        var events = rows.Take(100).Select(e => ProgressProjection.Completion(e, zone, user)).ToArray();
        return new(ReportingDates.Key(date), zoneId, ReportingDates.Instant(asOf), sequence.ToString(CultureInfo.InvariantCulture),
            total, events, rows.Length > 100 ? ProgressCursor.Encode(owner, date, zoneId, asOf, sequence, rows[99]) : null);
    }
    private static ProgressWeeklyPlan WeeklyPlan(UserData user, ProgressQuery request, string asOf)
    {
        var monday = ReportingDates.Monday(request.SelectedDate);
        var sunday = monday.AddDays(6);
        var tasks = user.Tasks.Values.Where(t => t.PlannedWeekStart == monday || (t.PlannedDate >= monday && t.PlannedDate <= sunday))
            .DistinctBy(t => t.Id).OrderBy(t => t.PlannedDate ?? DateOnly.MaxValue).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t =>
            {
                var progress = GetTaskProgress(t);
                var totalWeight = t.SubSteps.Sum(s => s.Weight);
                return new ProgressWeeklyTask(t.Id.ToString("D"), t.Title, t.Status.ToString(), ReportingDates.Key(t.PlannedWeekStart),
                    t.PlannedDate is { } date ? ReportingDates.Key(date) : null, $"/tasks/{t.Id:D}", progress, progress >= 100,
                    t.SubSteps.Count == 0 ? "taskStatus" : totalWeight > 0 ? "weightedSubsteps" : "substepCount",
                    t.SubSteps.Where(s => s.IsDone).Sum(s => s.Weight), totalWeight, t.SubSteps.Count(s => s.IsDone), t.SubSteps.Count);
            }).ToArray();
        return new(ReportingDates.Key(monday), ReportingDates.Key(sunday), asOf, monday < request.From || sunday > request.To,
            tasks.Length, tasks.Count(t => t.IsComplete), tasks.Length == 0 ? null : Math.Round(tasks.Average(t => t.ProgressPercent), 2),
            $"/tasks?weekStart={ReportingDates.Key(monday)}", tasks);
    }
}
