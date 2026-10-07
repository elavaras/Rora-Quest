using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace RoraQuest.Api.Progress;

public sealed record ProgressCursor(int Version, string Owner, string Date, string Zone, string AsOf,
    string Sequence, string After, string Key)
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static string Binding(string owner) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner)));
    public static string Encode(string owner, DateOnly date, string zone, DateTimeOffset asOf, long sequence, ProgressStoredUnit last) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new ProgressCursor(1, Binding(owner), ReportingDates.Key(date),
            zone, ReportingDates.Instant(asOf), sequence.ToString(CultureInfo.InvariantCulture),
            ReportingDates.Instant(ReportingDates.FromMicros(last.OccurredAt)), last.UnitKey), Json));
    public static ProgressCursor Parse(IQueryCollection query, string owner, DateTimeOffset now)
    {
        ProgressQuery.ValidateKeys(query, ["cursor"]);
        if (!query.TryGetValue("cursor", out var value) || value.ToString().Length is 0 or > 2048) throw Invalid();
        try
        {
            var cursor = JsonSerializer.Deserialize<ProgressCursor>(WebEncoders.Base64UrlDecode(value.ToString()), Json) ?? throw Invalid();
            if (cursor.Version != 1 || cursor.Owner != Binding(owner) || cursor.Date is null || cursor.Zone is null
                || cursor.AsOf is null || cursor.After is null || cursor.Sequence is null || cursor.Key is null) throw Invalid();
            var date = ReportingDates.Parse(cursor.Date, "cursor");
            var zone = ReportingDates.Zone(cursor.Zone);
            var asOf = ParseInstant(cursor.AsOf);
            var after = ParseInstant(cursor.After);
            if (asOf > now || after > asOf || ReportingDates.LocalDate(after, zone) != date
                || !long.TryParse(cursor.Sequence, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq < 1
                || !Regex.IsMatch(cursor.Key, @"^(task:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|substep:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$"))
                throw Invalid();
            return cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or ProgressQueryException)
        { throw Invalid(); }
    }
    public static DateTimeOffset ParseInstant(string value)
    {
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)) throw Invalid();
        return result;
    }
    public static ProgressQueryException Invalid() => new("cursor", "The cursor is malformed, belongs to another owner, or has an invalid snapshot.");
}
