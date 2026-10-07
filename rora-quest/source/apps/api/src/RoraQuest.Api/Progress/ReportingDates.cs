using System.Globalization;

namespace RoraQuest.Api.Progress;

public static class ReportingDates
{
    public static readonly DateOnly Min = new(1, 1, 1);
    public static readonly DateOnly Max = new(9999, 12, 26);
    public static readonly string[] ZoneIds = ["Asia/Kolkata", "UTC", "America/New_York"];
    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Instant(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    public static DateTimeOffset Microseconds(DateTimeOffset time) => new(time.UtcTicks / 10 * 10, TimeSpan.Zero);
    public static DateTimeOffset FromMicros(long micros) => new(micros * 10, TimeSpan.Zero);
    public static long Micros(DateTimeOffset time) => time.UtcTicks / 10;
    public static TimeZoneInfo Zone(string id)
    {
        if (id.Length > 100 || !ZoneIds.Contains(id, StringComparer.Ordinal))
            throw new ProgressQueryException("timeZone", "Use Asia/Kolkata, UTC, or America/New_York.");
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
    public static DateOnly Parse(string value, string field)
    {
        if (value.Length != 10 || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date < Min || date > Max)
            throw new ProgressQueryException(field, "Use a real date in YYYY-MM-DD format between 0001-01-01 and 9999-12-26.");
        return date;
    }
    public static DateOnly LocalDate(DateTimeOffset time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime);
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    // Signed ticks deliberately represent local year-0001 boundaries before the UTC instant domain.
    public static long BoundaryTicks(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return local.Ticks - offset.Ticks;
    }
}

public sealed record ProgressQuery(DateOnly From, DateOnly To, DateOnly SelectedDate, string TimeZone, bool IsDefaultRange)
{
    public static ProgressQuery Parse(IQueryCollection query, DateTimeOffset now)
    {
        ValidateKeys(query, ["from", "to", "selectedDate", "timeZone"]);
        var zoneId = query.TryGetValue("timeZone", out var tz) ? tz.ToString() : "Asia/Kolkata";
        var today = ReportingDates.LocalDate(now, ReportingDates.Zone(zoneId));
        var hasFrom = query.ContainsKey("from");
        var hasTo = query.ContainsKey("to");
        if (hasFrom != hasTo) throw new ProgressQueryException(hasFrom ? "to" : "from", "Both from and to are required together.");
        var monday = ReportingDates.Monday(today);
        var from = hasFrom ? ReportingDates.Parse(query["from"].ToString(), "from")
            : DateOnly.FromDayNumber(Math.Max(ReportingDates.Min.DayNumber, monday.DayNumber - 21));
        var to = hasTo ? ReportingDates.Parse(query["to"].ToString(), "to")
            : DateOnly.FromDayNumber(Math.Min(ReportingDates.Max.DayNumber, monday.DayNumber + 6));
        if (to < from || to.DayNumber - from.DayNumber >= 84)
            throw new ProgressQueryException("to", "The inclusive range must contain 1–84 dates, with from no later than to.");
        var selected = query.TryGetValue("selectedDate", out var s) ? ReportingDates.Parse(s.ToString(), "selectedDate")
            : today >= from && today <= to ? today : from;
        if (selected < from || selected > to) throw new ProgressQueryException("selectedDate", "selectedDate must be inside the requested range.");
        return new(from, to, selected, zoneId, !hasFrom);
    }
    public static void ValidateKeys(IQueryCollection query, string[] allowed)
    {
        foreach (var pair in query)
        {
            if (!allowed.Contains(pair.Key, StringComparer.Ordinal))
                throw new ProgressQueryException(pair.Key, "Unrecognized query parameter.");
            if (pair.Value.Count != 1) throw new ProgressQueryException(pair.Key, "Supply this parameter exactly once.");
        }
    }
}
