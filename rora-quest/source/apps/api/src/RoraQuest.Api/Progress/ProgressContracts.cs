namespace RoraQuest.Api.Progress;

public sealed record ProgressError(string Code, string Message, Dictionary<string, string[]> Errors);
public sealed record ProgressDay(string Date, string Position, string Status, string Coverage,
    string[] CoverageReasons, int UnitCount, int TaskCount, int SubstepCount, int SubstepTaskCount);
public sealed record ProgressCompletion(string UnitKey, string Sequence, string Kind,
    string OccurredAtUtc, string OccurredAtLocal, string LocalDate, string TaskId, string? SubstepId,
    string TaskTitle, string? SubstepTitle, string Availability, string? TaskHref);
public sealed record ProgressEventPage(string Date, string TimeZone, string AsOfUtc, string SnapshotSequence,
    int TotalCount, ProgressCompletion[] Events, string? NextCursor);
public sealed record ProgressLongestGap(string Scope, string State, int? Days, string? From, string? To, string[] Boundaries);
public sealed record ProgressCurrentGap(string Scope, string State, int? Days, string? From, string? To,
    string? ThroughDate, bool LowerBound, string Reason);
public sealed record ProgressWeeklyTask(string TaskId, string Title, string Status, string PlannedWeekStart,
    string? PlannedDate, string TaskHref, double ProgressPercent, bool IsComplete, string ProgressBasis,
    int DoneWeight, int TotalWeight, int DoneSubsteps, int TotalSubsteps);
public sealed record ProgressWeeklyPlan(string WeekStart, string WeekEnd, string AsOfUtc, bool ExtendsOutsideRange,
    int TotalTasks, int CompleteTasks, double? ProgressPercent, string TasksHref, ProgressWeeklyTask[] Tasks);
public sealed record ProgressCoverage(string TrackingStartedAtUtc, bool CaptureReliableNow, bool HasInterruptions);
public sealed record ProgressParticipation(int ActiveDays, int FullyTrackedElapsedDays,
    int UnknownOrPartialElapsedDays, bool IncludesToday, int FutureDays);
public sealed record ProgressLastActivity(string Scope, string State, ProgressCompletion? Event);
public sealed record ProgressReport(string AsOfUtc, string SnapshotSequence, string TimeZone, string Today,
    string NextMidnightUtc, string From, string To, string SelectedDate, bool IsDefaultRange,
    ProgressCoverage Coverage, ProgressDay[] Days, ProgressParticipation Participation,
    ProgressLongestGap LongestGap, ProgressCurrentGap CurrentGap, ProgressLastActivity LastActivity,
    ProgressEventPage SelectedDay, ProgressWeeklyPlan WeeklyPlan);

public sealed class ProgressQueryException(string field, string message) : Exception(message)
{
    public ProgressError Error { get; } = new("invalidProgressQuery", message, new() { [field] = [message] });
}

public sealed class ProgressUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
