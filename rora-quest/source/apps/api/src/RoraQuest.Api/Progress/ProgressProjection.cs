using System.Globalization;

namespace RoraQuest.Api.Progress;

/// <summary>Pure evidence projection. All intervals use UTC microseconds and half-open bounds.</summary>
public static class ProgressProjection
{
    public static IReadOnlyList<ProgressInterval> NormalizeCoverage(IEnumerable<ProgressInterval> sessions,
        IEnumerable<ProgressInterval> interruptions, long trackingStart, long asOf)
    {
        static List<ProgressInterval> Union(IEnumerable<ProgressInterval> input)
        {
            var result = new List<ProgressInterval>();
            foreach (var interval in input.Where(i => i.End > i.Start).OrderBy(i => i.Start))
            {
                if (result.Count == 0 || result[^1].End < interval.Start) result.Add(interval);
                else result[^1] = result[^1] with { End = Math.Max(result[^1].End, interval.End) };
            }
            return result;
        }
        var reliable = Union(sessions.Select(i => new ProgressInterval(Math.Max(i.Start, trackingStart), Math.Min(i.End, asOf))));
        foreach (var excluded in Union(interruptions))
        {
            var next = new List<ProgressInterval>();
            foreach (var interval in reliable)
            {
                if (excluded.End <= interval.Start || excluded.Start >= interval.End) next.Add(interval);
                else
                {
                    if (excluded.Start > interval.Start) next.Add(new(interval.Start, excluded.Start));
                    if (excluded.End < interval.End) next.Add(new(excluded.End, interval.End));
                }
            }
            reliable = next;
        }
        return reliable;
    }
    public static bool Covered(IReadOnlyList<ProgressInterval> intervals, long start, long end) =>
        end <= start || intervals.Any(i => i.Start <= start && i.End >= end);
    private static long Boundary(DateOnly date, TimeZoneInfo zone) => ReportingDates.BoundaryTicks(date, zone) / 10;

    public static ProgressDay Day(DateOnly date, TimeZoneInfo zone, ProgressEvidence evidence, ProgressCounts? counts = null)
    {
        var today = ReportingDates.LocalDate(evidence.AsOf, zone);
        var key = ReportingDates.Key(date);
        if (date > today) return new(key, "future", "future", "notApplicable", ["futureDate"], 0, 0, 0, 0);
        var start = Boundary(date, zone);
        var dayEnd = Boundary(date.AddDays(1), zone);
        var end = Math.Min(dayEnd, ReportingDates.Micros(evidence.AsOf));
        var full = Covered(evidence.ReliableIntervals, start, end) && start >= evidence.TrackingStart;
        var some = evidence.ReliableIntervals.Any(i => i.Start < end && i.End > start);
        var coverage = full ? date == today ? "reliableSoFar" : "full" : some ? "partial" : "none";
        var reasons = new List<string>();
        if (dayEnd <= evidence.TrackingStart) reasons.Add("beforeTracking");
        if (evidence.TrackingStart > start && evidence.TrackingStart < dayEnd) reasons.Add("trackingStartedDuringDay");
        if (!Covered(evidence.ReliableIntervals, Math.Max(start, evidence.TrackingStart), end)) reasons.Add("captureInterruption");
        if (date == today) reasons.Add("dayInProgress");
        var status = counts?.UnitCount > 0 ? "active" : date == today ? "todayPending" : full ? "inactive" : some ? "partial" : "unknown";
        return new(key, date == today ? "today" : "elapsed", status, coverage, reasons.ToArray(),
            counts?.UnitCount ?? 0, counts?.TaskCount ?? 0, counts?.SubstepCount ?? 0, counts?.SubstepTaskCount ?? 0);
    }

    public static ProgressLongestGap Longest(ProgressDay[] days, Func<DateOnly, ProgressDay> adjacent)
    {
        int bestStart = -1, bestEnd = -1, current = -1;
        for (var i = 0; i < days.Length; i++)
        {
            if (days[i].Status != "inactive") { current = -1; continue; }
            if (current < 0) current = i;
            if (bestStart < 0 || i - current >= bestEnd - bestStart) { bestStart = current; bestEnd = i; }
        }
        if (bestStart < 0)
        {
            var any = days.Any(d => d.Position == "elapsed" && d.Coverage == "full");
            return new("selectedRange", any ? "none" : "insufficientCoverage", any ? 0 : null, null, null, []);
        }
        var boundaries = new List<string>();
        if (bestStart == 0) boundaries.Add("rangeStart");
        if (bestEnd == days.Length - 1) boundaries.Add("rangeEnd");
        var from = ReportingDates.Parse(days[bestStart].Date, "date");
        var to = ReportingDates.Parse(days[bestEnd].Date, "date");
        static bool Unknown(ProgressDay day) => day.Position == "elapsed" && day.Status != "active" && day.Coverage is "partial" or "none";
        if (from == ReportingDates.Min || Unknown(adjacent(from.AddDays(-1)))) boundaries.Add("coverageStart");
        if (to < ReportingDates.Max && Unknown(adjacent(to.AddDays(1)))) boundaries.Add("coverageEnd");
        return new("selectedRange", "gap", bestEnd - bestStart + 1, days[bestStart].Date, days[bestEnd].Date, boundaries.ToArray());
    }

    private static (int From, int To)[] FullSpans(ProgressEvidence evidence, TimeZoneInfo zone)
    {
        return evidence.ReliableIntervals.Select(i =>
        {
            var first = ReportingDates.LocalDate(ReportingDates.FromMicros(i.Start), zone);
            var from = first.DayNumber + (Boundary(first, zone) < i.Start ? 1 : 0);
            var endDate = ReportingDates.LocalDate(ReportingDates.FromMicros(i.End), zone);
            return (From: from, To: endDate.DayNumber - 1);
        }).Where(s => s.From <= s.To).ToArray();
    }
    public static ProgressCurrentGap Current(ProgressEvidence evidence, TimeZoneInfo zone, ProgressStoredUnit? last)
    {
        var today = ReportingDates.LocalDate(evidence.AsOf, zone);
        DateOnly? yesterday = today > ReportingDates.Min ? today.AddDays(-1) : null;
        var through = yesterday is { } y ? ReportingDates.Key(y) : null;
        var lastDate = last is null ? (DateOnly?)null : ReportingDates.LocalDate(ReportingDates.FromMicros(last.OccurredAt), zone);
        ProgressCurrentGap None(string reason) => new("allReliableHistory", "none", 0, null, null, through, false, reason);
        ProgressCurrentGap Unavailable(string reason) => new("allReliableHistory", "unavailable", null, null, null, through, false, reason);
        if (lastDate == today) return None("activityToday");
        if (!evidence.CaptureReliableNow || !Covered(evidence.ReliableIntervals, Boundary(today, zone), ReportingDates.Micros(evidence.AsOf))
            || Boundary(today, zone) < evidence.TrackingStart) return Unavailable("captureUnavailable");
        if (yesterday is null) return Unavailable("noElapsedDay");
        if (lastDate == yesterday) return None("activityYesterday");
        var spans = FullSpans(evidence, zone);
        var containing = spans.Where(s => s.From <= yesterday.Value.DayNumber && s.To >= yesterday.Value.DayNumber).ToArray();
        if (containing.Length == 0) return Unavailable(spans.Length == 0 ? "noElapsedDay" : "yesterdayUnknown");
        var first = containing[0].From;
        var exact = lastDate is { } active && active.DayNumber >= first - 1;
        if (exact) first = lastDate!.Value.DayNumber + 1;
        var from = DateOnly.FromDayNumber(first);
        var trackingDate = ReportingDates.LocalDate(ReportingDates.FromMicros(evidence.TrackingStart), zone);
        var initialFullDay = trackingDate.DayNumber + (Boundary(trackingDate, zone) < evidence.TrackingStart ? 1 : 0);
        return new("allReliableHistory", "gap", yesterday.Value.DayNumber - first + 1, ReportingDates.Key(from), through,
            through, !exact, exact ? "sinceLastActivity" : first == initialFullDay ? "trackingStart" : "coverageBoundary");
    }

    public static ProgressCompletion Completion(ProgressStoredUnit unit, TimeZoneInfo zone, UserData user)
    {
        var taskId = Guid.Parse(unit.TaskId);
        var available = user.Tasks.TryGetValue(taskId, out var task) ? "available" : "taskRemoved";
        if (task is not null && unit.SubstepId is not null && !task.SubSteps.Any(s => s.Id == Guid.Parse(unit.SubstepId)))
            available = "substepRemoved";
        var instant = ReportingDates.FromMicros(unit.OccurredAt);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new(unit.UnitKey, unit.Sequence.ToString(CultureInfo.InvariantCulture), unit.Kind, ReportingDates.Instant(instant),
            local.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture),
            ReportingDates.Key(DateOnly.FromDateTime(local.DateTime)), taskId.ToString("D"),
            unit.SubstepId is null ? null : Guid.Parse(unit.SubstepId).ToString("D"),
            unit.TaskTitle, unit.SubstepTitle, available, available == "available" ? $"/tasks/{taskId:D}" : null);
    }
}
