using RoraQuest.Api.Progress;
using Xunit;

public class ProgressProjectionTests
{
    private static DateTimeOffset At(string instant) => DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
    private static long Us(string instant) => ReportingDates.Micros(At(instant));
    private static ProgressEvidence Evidence(string start, string asOf, params ProgressInterval[] intervals) =>
        new(At(asOf), 0, Us(start), true, intervals.Length == 0 ? [new(Us(start), Us(asOf))] : intervals);

    [Theory]
    [InlineData("Asia/Kolkata", "2026-10-06T18:29:59Z", "2026-10-06")]
    [InlineData("Asia/Kolkata", "2026-10-06T18:30:00Z", "2026-10-07")]
    [InlineData("UTC", "2026-10-06T18:30:00Z", "2026-10-06")]
    [InlineData("America/New_York", "2026-11-01T05:30:00Z", "2026-11-01")]
    [InlineData("America/New_York", "2026-11-01T06:30:00Z", "2026-11-01")]
    [InlineData("Asia/Kolkata", "2026-12-31T18:30:00Z", "2027-01-01")]
    public void InstantToDate_IsExplicitAndDstSafe(string zone, string instant, string date) =>
        Assert.Equal(date, ReportingDates.Key(ReportingDates.LocalDate(At(instant), ReportingDates.Zone(zone))));

    [Theory]
    [InlineData("2026-03-08", 23)]
    [InlineData("2026-11-01", 25)]
    [InlineData("2028-02-29", 24)]
    public void LocalDayBoundaries_AreNotFixedDurations(string key, int hours)
    {
        var zone = ReportingDates.Zone("America/New_York");
        var day = ReportingDates.Parse(key, "from");
        var start = ReportingDates.BoundaryTicks(day, zone);
        var end = ReportingDates.BoundaryTicks(day.AddDays(1), zone);
        Assert.Equal(hours, TimeSpan.FromTicks(end - start).TotalHours);
        var evidence = new ProgressEvidence(new DateTimeOffset(end, TimeSpan.Zero), 0, start / 10, true, [new(start / 10, end / 10)]);
        Assert.Equal("inactive", ProgressProjection.Day(day, zone, evidence).Status);
    }

    [Fact]
    public void DefaultRangeAndDateLimits_AreStrict()
    {
        var result = ProgressQuery.Parse(ProgressTestStores.Query(), At("2026-10-06T12:00:00Z"));
        Assert.Equal(new DateOnly(2026, 9, 14), result.From);
        Assert.Equal(new DateOnly(2026, 10, 11), result.To);
        Assert.Equal(new DateOnly(2026, 10, 6), result.SelectedDate);
        foreach (var zoneId in ReportingDates.ZoneIds)
        {
            var zone = ReportingDates.Zone(zoneId);
            var evidence = Evidence("2026-10-01T00:00:00Z", "2026-10-06T12:00:00Z");
            Assert.Equal("unknown", ProgressProjection.Day(ReportingDates.Min, zone, evidence).Status);
            Assert.Equal("future", ProgressProjection.Day(ReportingDates.Max, zone, evidence).Status);
            var boundary = ProgressQuery.Parse(ProgressTestStores.Query($"from=0001-01-01&to=0001-01-01&timeZone={zoneId}"), evidence.AsOf);
            Assert.Equal(ReportingDates.Min, boundary.SelectedDate);
        }
    }

    [Fact]
    public void PartialInitialDayAndInterruption_KeepPositiveEvidenceButBreakGaps()
    {
        var zone = ReportingDates.Zone("UTC");
        var evidence = Evidence("2026-10-01T10:00:00Z", "2026-10-06T12:00:00Z",
            new(Us("2026-10-01T10:00:00Z"), Us("2026-10-03T12:00:00Z")),
            new(Us("2026-10-04T00:00:00Z"), Us("2026-10-06T12:00:00Z")));
        var initial = ProgressProjection.Day(new(2026, 10, 1), zone, evidence);
        Assert.Equal("partial", initial.Status);
        Assert.Equal(new[] { "trackingStartedDuringDay" }, initial.CoverageReasons);
        var interrupted = ProgressProjection.Day(new(2026, 10, 3), zone, evidence, new("2026-10-03", 2, 1, 1, 1));
        Assert.Equal("active", interrupted.Status);
        Assert.Equal("partial", interrupted.Coverage);
        Assert.Contains("captureInterruption", interrupted.CoverageReasons);
        Assert.Equal("todayPending", ProgressProjection.Day(new(2026, 10, 6), zone, evidence).Status);
        Assert.Equal("future", ProgressProjection.Day(new(2026, 10, 7), zone, evidence).Status);
    }

    [Fact]
    public void Longest_LatestTieClippingAndUnknownVariants()
    {
        var zone = ReportingDates.Zone("UTC");
        var evidence = Evidence("2026-10-01T00:00:00Z", "2026-10-06T12:00:00Z");
        ProgressDay Day(DateOnly d) => ProgressProjection.Day(d, zone, evidence,
            d.Day == 3 ? new(ReportingDates.Key(d), 1, 1, 0, 0) : null);
        var days = Enumerable.Range(1, 5).Select(n => Day(new(2026, 10, n))).ToArray();
        var gap = ProgressProjection.Longest(days, Day);
        Assert.Equal(2, gap.Days);
        Assert.Equal("2026-10-04", gap.From);
        Assert.Equal(new[] { "rangeEnd" }, gap.Boundaries);
        gap = ProgressProjection.Longest(days[3..], Day);
        Assert.Equal(new[] { "rangeStart", "rangeEnd" }, gap.Boundaries);
        Assert.Equal("none", ProgressProjection.Longest([days[2]], Day).State);
        Assert.Equal("insufficientCoverage", ProgressProjection.Longest([Day(new(2026, 9, 30))], Day).State);
    }

    [Fact]
    public void Current_IsGlobalStopsAtPartialActiveDayAndResetsToday()
    {
        var zone = ReportingDates.Zone("UTC");
        var evidence = Evidence("2026-10-01T10:00:00Z", "2026-10-06T12:00:00Z");
        ProgressStoredUnit Event(string at) => new(1, "task:00000000-0000-0000-0000-000000000001", "task", Us(at),
            "00000000-0000-0000-0000-000000000001", null, "Work", null);
        var current = ProgressProjection.Current(evidence, zone, Event("2026-10-01T11:00:00Z"));
        Assert.Equal(4, current.Days);
        Assert.False(current.LowerBound);
        Assert.Equal("sinceLastActivity", current.Reason);
        Assert.Equal("2026-10-02", current.From);
        Assert.Equal("trackingStart", ProgressProjection.Current(evidence, zone, null).Reason);
        Assert.True(ProgressProjection.Current(evidence, zone, null).LowerBound);
        Assert.Equal("activityYesterday", ProgressProjection.Current(evidence, zone, Event("2026-10-05T12:00:00Z")).Reason);
        Assert.Equal("activityToday", ProgressProjection.Current(evidence with { CaptureReliableNow = false }, zone, Event("2026-10-06T10:00:00Z")).Reason);
        Assert.Equal("captureUnavailable", ProgressProjection.Current(evidence with { CaptureReliableNow = false }, zone, null).Reason);
    }

    [Fact]
    public void CoverageUnion_SubtractsOverlappingInterruptionsAndDoesNotBridgeDowntime()
    {
        var actual = ProgressProjection.NormalizeCoverage([new(0, 10), new(8, 20), new(25, 40)],
            [new(5, 8), new(6, 12), new(30, 32)], 2, 35);
        Assert.Equal([new ProgressInterval(2, 5), new(12, 20), new(25, 30), new(32, 35)], actual);
        Assert.True(ProgressProjection.Covered(actual, 12, 20));
        Assert.False(ProgressProjection.Covered(actual, 12, 25));
    }

    [Fact]
    public void Current_UsesDaySpansRatherThanScanningMillennia()
    {
        var evidence = Evidence("0001-01-01T00:00:00Z", "9999-12-26T12:00:00Z");
        var current = ProgressProjection.Current(evidence, ReportingDates.Zone("UTC"), null);
        Assert.Equal(new DateOnly(9999, 12, 26).DayNumber, current.Days);
        Assert.Equal("0001-01-01", current.From);
    }

    [Fact]
    public void Longest_PartialActiveAdjacentDateIsAnActivityBoundaryNotUnknown()
    {
        var zone = ReportingDates.Zone("UTC");
        var evidence = Evidence("2026-10-01T10:00:00Z", "2026-10-04T12:00:00Z");
        ProgressDay Day(DateOnly date) => ProgressProjection.Day(date, zone, evidence,
            date.Day == 1 ? new(ReportingDates.Key(date), 1, 1, 0, 0) : null);
        var gap = ProgressProjection.Longest([Day(new(2026, 10, 2)), Day(new(2026, 10, 3))], Day);
        Assert.Equal(2, gap.Days);
        Assert.DoesNotContain("coverageStart", gap.Boundaries);
        Assert.Equal(new[] { "rangeStart", "rangeEnd" }, gap.Boundaries);
    }
}
