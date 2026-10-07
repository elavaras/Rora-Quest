using System.Data.Common;
using Dapper;
using RoraQuest.Api.Progress;

public sealed record ProgressUnit(string UnitKey, string Kind, DateTimeOffset OccurredAt,
    Guid TaskId, Guid? SubstepId, string TaskTitle, string? SubstepTitle)
{
    public static ProgressUnit Task(TaskItem task, DateTimeOffset now) =>
        new($"task:{task.Id:D}", "task", now, task.Id, null, task.Title, null);
    public static ProgressUnit Substep(TaskItem task, TaskSubStep step, DateTimeOffset now) =>
        new($"substep:{task.Id:D}:{step.Id:D}", "substep", now, task.Id, step.Id, task.Title, step.Title);
}
public sealed record ProgressStoredUnit(long Sequence, string UnitKey, string Kind, long OccurredAt,
    string TaskId, string? SubstepId, string TaskTitle, string? SubstepTitle)
{
    public ProgressStoredUnit() : this(0, "", "", 0, "", null, "", null) { }
}
public sealed record ProgressInterval(long Start, long End)
{
    public ProgressInterval() : this(0, 0) { }
}
public sealed record ProgressCounts(string Date, int UnitCount, int TaskCount, int SubstepCount, int SubstepTaskCount)
{
    public ProgressCounts() : this("", 0, 0, 0, 0) { }
}
public sealed record ProgressEvidence(DateTimeOffset AsOf, long Sequence, long TrackingStart,
    bool CaptureReliableNow, IReadOnlyList<ProgressInterval> ReliableIntervals);

/// <summary>Fault boundary used by actual-store tests; production has no configured faults.</summary>
public class ProgressFaults
{
    public virtual void At(string boundary) { }
}

/// <summary>
/// Relational evidence operations shared by SQLite and Postgres. The local gate protects this process's
/// certification state; the database control lock serializes all writers, including legacy triggers.
/// </summary>
public sealed class ProgressLedger(Func<DbConnection> open, bool postgres, TimeProvider clock, ProgressFaults? faults = null)
{
    public const long ControlLock = 726578430019;
    private readonly object _gate = new();
    private Guid? _session;
    private DateTimeOffset? _lastVerified;
    private bool _started;
    private readonly HashSet<string> _fenced = new(StringComparer.Ordinal);
    public TimeProvider Clock => clock;
    public DateTimeOffset Now => ReportingDates.Microseconds(clock.GetUtcNow());
    internal bool Postgres => postgres;
    internal object TimeValue(DateTimeOffset time) => postgres ? time : ReportingDates.Micros(time);
    internal object IdValue(Guid id) => postgres ? id : id.ToString("D");
    internal string MicrosSql(string column) => postgres
        ? $"(extract(epoch from {column}) * 1000000)::bigint + 62135596800000000" : column;
    internal string IdSql(string column) => postgres ? $"{column}::text" : column;
    internal void Fault(string boundary) => faults?.At(boundary);
    internal void Control(DbConnection conn, DbTransaction tx)
    {
        if (postgres) conn.Execute($"SELECT pg_advisory_xact_lock({ControlLock}); SET LOCAL roraquest.progress_capture_version='1';", transaction: tx);
    }
    public void AssertOwnerAvailable(string owner)
    {
        lock (_gate)
            if (_fenced.Contains(owner)) throw new ProgressUnavailableException("Mutation outcome requires reconciliation or restart.");
    }
    public void InitializeOwner(string owner, DbConnection? connection = null)
    {
        AssertOwnerAvailable(owner);
        Execute(connection, (conn, tx) =>
        {
            Fault("owner");
            conn.Execute("""
                INSERT INTO progress_owners(owner_id,tracking_started_at) VALUES (@owner,@now)
                ON CONFLICT(owner_id) DO NOTHING
                """, new { owner, now = TimeValue(Now) }, tx);
            return 0;
        });
    }
    public void Start()
    {
        lock (_gate)
        {
            foreach (var zone in ReportingDates.ZoneIds) _ = ReportingDates.Zone(zone);
            Execute(null, (conn, tx) =>
            {
                _started = true;
                Certify(conn, tx, Now, false);
                return 0;
            });
        }
    }
    public void Heartbeat() => Execute(null, (conn, tx) => { Certify(conn, tx, Now, false); return 0; });
    public void Stop()
    {
        Execute(null, (conn, tx) =>
        {
            var now = Now;
            Certify(conn, tx, now, false);
            if (_session is { } session)
                conn.Execute("UPDATE progress_capture_sessions SET stopped_at=@now WHERE session_id=@id",
                    new { now = TimeValue(now), id = IdValue(session) }, tx);
            _session = null;
            _started = false;
            return 0;
        });
    }
    internal bool IsSuspended(DbConnection conn, DbTransaction? tx = null) =>
        conn.ExecuteScalar<long>("SELECT count(*) FROM progress_capture_interruptions WHERE end_at IS NULL", transaction: tx) != 0;

    internal bool Certify(DbConnection conn, DbTransaction tx, DateTimeOffset now, bool requireCapture)
    {
        Fault("certification");
        if (!_started) throw new ProgressUnavailableException("Capture has not reached readiness.");
        if (_lastVerified is { } last && now < last) throw new ProgressUnavailableException("Capture clock moved backwards.");
        CheckStoredClock(conn, tx, now);
        if (IsSuspended(conn, tx))
        {
            _session = null;
            _lastVerified = null;
            if (requireCapture) throw new ProgressUnavailableException("Capture is suspended.");
            return false;
        }
        if (_session is null || _lastVerified is null || now - _lastVerified > TimeSpan.FromSeconds(90))
        {
            _session = Guid.NewGuid();
            conn.Execute("""
                INSERT INTO progress_capture_sessions(session_id,started_at,verified_through)
                VALUES (@id,@now,@now)
                """, new { id = IdValue(_session.Value), now = TimeValue(now) }, tx);
        }
        else
        {
            var changed = conn.Execute("""
                UPDATE progress_capture_sessions SET verified_through=@now
                WHERE session_id=@id AND stopped_at IS NULL AND verified_through<=@now
                """, new { id = IdValue(_session.Value), now = TimeValue(now) }, tx);
            if (changed != 1) throw new ProgressUnavailableException("Capture session is missing or stopped.");
        }
        _lastVerified = now;
        return true;
    }

    internal long Revision(DbConnection conn, string owner, DbTransaction? tx = null) =>
        conn.QuerySingle<long>("SELECT aggregate_revision FROM progress_owners WHERE owner_id=@owner", new { owner }, tx);
    internal long IncrementRevision(DbConnection conn, DbTransaction tx, string owner) =>
        conn.QuerySingle<long>("UPDATE progress_owners SET aggregate_revision=aggregate_revision+1 WHERE owner_id=@owner RETURNING aggregate_revision", new { owner }, tx);

    /// <returns>True if the prepared aggregate is still the latest revision; false if another writer superseded it.</returns>
    public bool CommitMutation(string owner, ProgressUnit? unit, DateTimeOffset now,
        Action<DbConnection, DbTransaction>? persist = null, DbConnection? connection = null,
        Func<DbConnection>? reacquire = null, Action<DateTimeOffset>? stamp = null)
    {
        var commitId = Guid.NewGuid();
        var commitAttempted = false;
        try
        {
            Execute(connection, (conn, tx) =>
            {
                // Choose the authoritative instant only AFTER the control lock, so a heartbeat
                // or another replica cannot certify a later instant while this writer is waiting.
                now = Now;
                stamp?.Invoke(now);
                if (unit is not null) unit = unit with { OccurredAt = now };
                // Suspension rejects completion candidates, not unrelated accepted workflow
                // transitions such as reopening or a title-only edit. Those still get receipts.
                Certify(conn, tx, now, unit is not null);
                Fault("beforeEvent");
                if (unit is not null)
                    conn.Execute("""
                        INSERT INTO progress_completions(owner_id,unit_key,kind,occurred_at,task_id,substep_id,task_title,substep_title)
                        VALUES (@owner,@key,@kind,@at,@task,@sub,@title,@subtitle)
                        ON CONFLICT(owner_id,unit_key) DO NOTHING
                        """, new
                    {
                        owner, key = unit.UnitKey, kind = unit.Kind, at = TimeValue(unit.OccurredAt),
                        task = IdValue(unit.TaskId), sub = unit.SubstepId is { } id ? IdValue(id) : null,
                        title = unit.TaskTitle, subtitle = unit.SubstepTitle
                    }, tx);
                Fault("afterEvent");
                persist?.Invoke(conn, tx);
                Fault("afterAggregate");
                var revision = IncrementRevision(conn, tx, owner);
                conn.Execute("""
                    INSERT INTO progress_mutation_receipts(commit_id,owner_id,committed_at,aggregate_revision)
                    VALUES (@id,@owner,@now,@revision)
                    """, new { id = IdValue(commitId), owner, now = TimeValue(now), revision }, tx);
                Fault("beforeCommit");
                commitAttempted = true;
                return 0;
            }, afterCommit: () => Fault("afterCommit"));
            return true;
        }
        catch (Exception ex)
        {
            if (!commitAttempted) throw new ProgressUnavailableException("Progress mutation rolled back.", ex);
            try
            {
                // Postgres drops/reacquires the owner advisory lock before proving absence. SQLite's
                // process lock and service gate still fence its sole writer.
                var fresh = reacquire?.Invoke();
                using var own = fresh is null ? open() : null;
                var conn = fresh ?? own!;
                Fault("resolve");
                var receipt = conn.QuerySingleOrDefault<long?>("""
                    SELECT aggregate_revision FROM progress_mutation_receipts WHERE commit_id=@id AND owner_id=@owner
                    """, new { id = IdValue(commitId), owner });
                if (receipt is not null) return Revision(conn, owner) == receipt.Value;
            }
            catch (Exception resolution)
            {
                lock (_gate) _fenced.Add(owner);
                throw new ProgressUnavailableException("Progress mutation outcome cannot be resolved.", resolution);
            }
            throw new ProgressUnavailableException("Progress mutation rolled back.", ex);
        }
    }

    public T Read<T>(string owner, Func<ProgressRead, T> project, DbConnection? connection = null)
    {
        AssertOwnerAvailable(owner);
        return Execute(connection, (conn, tx) =>
        {
            // Control acquired by Execute before choosing as-of, with READ COMMITTED on PostgreSQL.
            var now = Now;
            var reliable = Certify(conn, tx, now, false);
            var start = conn.QuerySingle<long>($"SELECT {MicrosSql("tracking_started_at")} FROM progress_owners WHERE owner_id=@owner", new { owner }, tx);
            var sequence = conn.ExecuteScalar<long>("SELECT COALESCE(max(sequence),0) FROM progress_completions WHERE owner_id=@owner AND occurred_at<=@now",
                new { owner, now = TimeValue(now) }, tx);
            var sessions = conn.Query<ProgressInterval>($"SELECT {MicrosSql("started_at")} AS Start, {MicrosSql("verified_through")} AS End FROM progress_capture_sessions", transaction: tx).ToArray();
            var interruptions = conn.Query<ProgressInterval>($"SELECT {MicrosSql("start_at")} AS Start, {MicrosSql("COALESCE(end_at,@now)")} AS End FROM progress_capture_interruptions",
                new { now = TimeValue(now) }, tx).ToArray();
            var intervals = ProgressProjection.NormalizeCoverage(sessions, interruptions, start, ReportingDates.Micros(now));
            return project(new ProgressRead(this, conn, tx, owner, new(now, sequence, start, reliable, intervals)));
        });
    }

    public string Maintenance(bool resume, DateTimeOffset? unknownSince = null)
    {
        return Execute(null, (conn, tx) =>
        {
            var now = Now;
            CheckStoredClock(conn, tx, now);
            if (unknownSince > now) throw new ArgumentException("unknown-since must not be in the future.");
            var existing = conn.QuerySingleOrDefault<string>($"SELECT {IdSql("id")} FROM progress_capture_interruptions WHERE end_at IS NULL", transaction: tx);
            var boundary = unknownSince ?? now;
            if (existing is null && (!resume || unknownSince is not null))
            {
                existing = Guid.NewGuid().ToString("D");
                conn.Execute("INSERT INTO progress_capture_interruptions(id,start_at,reason) VALUES (@id,@start,@reason)",
                    new { id = IdValue(Guid.Parse(existing)), start = TimeValue(boundary), reason = unknownSince is null ? "maintenance" : "recovery" }, tx);
            }
            else if (existing is not null && unknownSince is not null)
                conn.Execute("UPDATE progress_capture_interruptions SET start_at=CASE WHEN start_at>@start THEN @start ELSE start_at END WHERE id=@id",
                    new { id = IdValue(Guid.Parse(existing)), start = TimeValue(boundary) }, tx);
            if (resume)
            {
                if (existing is null) throw new InvalidOperationException("No interruption is open; supply --unknown-since for recovery.");
                var closed = conn.Execute("UPDATE progress_capture_interruptions SET end_at=@now WHERE id=@id AND start_at<=@now",
                    new { id = IdValue(Guid.Parse(existing)), now = TimeValue(now) }, tx);
                if (closed != 1) throw new ProgressUnavailableException("Interruption boundary is later than the current clock.");
            }
            var persistedStart = conn.QuerySingle<long>($"SELECT {MicrosSql("start_at")} FROM progress_capture_interruptions WHERE id=@id",
                new { id = IdValue(Guid.Parse(existing!)) }, tx);
            var startText = ReportingDates.Instant(ReportingDates.FromMicros(persistedStart));
            return $"{(resume ? "resumed" : "suspended")} operation={existing} boundary={(resume ? ReportingDates.Instant(now) : startText)} start={startText} end={(resume ? ReportingDates.Instant(now) : "open")}";
        });
    }

    private T Execute<T>(DbConnection? connection, Func<DbConnection, DbTransaction, T> action, Action? afterCommit = null)
    {
        lock (_gate)
        {
            var priorSession = _session;
            var priorVerified = _lastVerified;
            try
            {
                using var owned = connection is null ? open() : null;
                var conn = connection ?? owned!;
                using var tx = conn.BeginTransaction();
                Control(conn, tx);
                var result = action(conn, tx);
                tx.Commit();
                afterCommit?.Invoke();
                return result;
            }
            catch (ProgressQueryException)
            {
                // A rejected continuation is not a storage/capture outage. Its uncommitted
                // certificate is discarded without invalidating the previously healthy session.
                _session = priorSession;
                _lastVerified = priorVerified;
                throw;
            }
            catch
            {
                // A rollback/failed heartbeat can never extend a previous certificate across recovery.
                _session = null;
                // Keep the last instant to detect backward clocks even after failure.
                throw;
            }
        }
    }

    private void CheckStoredClock(DbConnection conn, DbTransaction tx, DateTimeOffset now)
    {
        var latest = conn.ExecuteScalar<long?>($"SELECT {MicrosSql("max(verified_through)")} FROM progress_capture_sessions", transaction: tx);
        var interruption = conn.ExecuteScalar<long?>($"SELECT {MicrosSql("max(COALESCE(end_at,start_at))")} FROM progress_capture_interruptions", transaction: tx);
        if (latest > ReportingDates.Micros(now) || interruption > ReportingDates.Micros(now))
            throw new ProgressUnavailableException("Capture clock precedes durable certification.");
    }
}

public sealed class ProgressRead(ProgressLedger ledger, DbConnection conn, DbTransaction tx, string owner, ProgressEvidence evidence)
{
    public ProgressEvidence Evidence => evidence;
    private object Bound(long ticks) => ledger.TimeValue(new DateTimeOffset(Math.Clamp(ticks, 0, DateTimeOffset.MaxValue.UtcTicks) / 10 * 10, TimeSpan.Zero));
    private string Fields => $"sequence,unit_key AS UnitKey,kind,{ledger.MicrosSql("occurred_at")} AS OccurredAt,{ledger.IdSql("task_id")} AS TaskId,{ledger.IdSql("substep_id")} AS SubstepId,task_title AS TaskTitle,substep_title AS SubstepTitle";
    public ProgressStoredUnit? Latest(long? sequence = null, DateTimeOffset? asOf = null) =>
        conn.QuerySingleOrDefault<ProgressStoredUnit>($"SELECT {Fields} FROM progress_completions WHERE owner_id=@owner AND sequence<=@seq AND occurred_at<=@now ORDER BY occurred_at DESC,unit_key DESC LIMIT 1",
            new { owner, seq = sequence ?? evidence.Sequence, now = ledger.TimeValue(asOf ?? evidence.AsOf) }, tx);
    public ProgressCounts[] Counts(DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var day = ledger.Postgres ? "to_char(occurred_at AT TIME ZONE @zone,'YYYY-MM-DD')" : "progress_date(occurred_at,@zone)";
        return conn.Query<ProgressCounts>($"""
            SELECT {day} AS Date, CAST(count(*) AS int) AS UnitCount,
                CAST(sum(CASE WHEN kind='task' THEN 1 ELSE 0 END) AS int) AS TaskCount,
                CAST(sum(CASE WHEN kind='substep' THEN 1 ELSE 0 END) AS int) AS SubstepCount,
                CAST(count(DISTINCT CASE WHEN kind='substep' THEN task_id END) AS int) AS SubstepTaskCount
            FROM progress_completions WHERE owner_id=@owner AND occurred_at>=@start AND occurred_at<@end
                AND occurred_at<=@now AND sequence<=@seq GROUP BY {day}
            """, new { owner, zone = zone.Id, start = Bound(ReportingDates.BoundaryTicks(from, zone)),
                end = Bound(ReportingDates.BoundaryTicks(to.AddDays(1), zone)), now = ledger.TimeValue(evidence.AsOf), seq = evidence.Sequence }, tx).ToArray();
    }
    public (int Total, ProgressStoredUnit[] Events) Page(DateOnly date, TimeZoneInfo zone, long sequence, DateTimeOffset asOf, long? after = null, string? key = null)
    {
        var args = new { owner, seq = sequence, now = ledger.TimeValue(asOf),
            start = Bound(ReportingDates.BoundaryTicks(date, zone)), end = Bound(ReportingDates.BoundaryTicks(date.AddDays(1), zone)),
            after = ledger.TimeValue(ReportingDates.FromMicros(after ?? 0)), key = key ?? "" };
        const string filter = "owner_id=@owner AND sequence<=@seq AND occurred_at<=@now AND occurred_at>=@start AND occurred_at<@end";
        var count = conn.ExecuteScalar<int>($"SELECT count(*) FROM progress_completions WHERE {filter}", args, tx);
        var rows = conn.Query<ProgressStoredUnit>($"SELECT {Fields} FROM progress_completions WHERE {filter} AND (occurred_at>@after OR (occurred_at=@after AND unit_key>@key)) ORDER BY occurred_at,unit_key LIMIT 101", args, tx).ToArray();
        return (count, rows);
    }
}
