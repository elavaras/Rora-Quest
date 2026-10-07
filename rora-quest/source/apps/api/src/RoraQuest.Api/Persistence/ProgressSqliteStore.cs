using System.Data.Common;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using RoraQuest.Api.Progress;

/// <summary>A registered, exclusively owned, Progress-only sidecar. Never creates a database on startup.</summary>
public sealed class ProgressSqliteStore : IDisposable
{
    private readonly string _directory;
    private readonly string _identity;
    private readonly FileStream _lock;
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoraQuest", "Progress");
    public string DirectoryPath => _directory;

    public static void Initialize(string directory)
    {
        directory = ValidateDirectory(directory);
        Directory.CreateDirectory(directory);
        using var guard = new FileStream(Path.Combine(directory, "process.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var registration = Path.Combine(directory, "registration");
        var dbPath = Path.Combine(directory, "progress.sqlite");
        var unexpected = Directory.EnumerateFileSystemEntries(directory).Any(p =>
            Path.GetFileName(p) is not ("process.lock" or "registration" or "progress.sqlite" or "progress.sqlite-journal"));
        if (unexpected) throw new InvalidOperationException("Progress initialization requires an empty dedicated directory.");
        string identity;
        if (File.Exists(registration))
        {
            identity = File.ReadAllText(registration);
            ValidateIdentity(identity);
        }
        else
        {
            if (File.Exists(dbPath)) throw new InvalidOperationException("An unregistered Progress database must not be overwritten.");
            identity = $"roraquest-progress-v1:{Guid.NewGuid():D}";
            using var file = new FileStream(registration, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(identity));
            file.Flush(true);
        }
        using var conn = Connect(dbPath, SqliteOpenMode.ReadWriteCreate);
        if (conn.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'") != 0)
            throw new InvalidOperationException("Progress store already contains a schema; initialization never overwrites it.");
        using var tx = conn.BeginTransaction();
        conn.Execute(Schema, transaction: tx);
        conn.Execute("INSERT INTO progress_registration(identity, owner_count, session_count, event_count) VALUES (@identity,0,0,0)", new { identity }, tx);
        tx.Commit();
    }

    public ProgressSqliteStore(string directory)
    {
        _directory = ValidateDirectory(directory);
        if (!Directory.Exists(_directory)) throw new ProgressUnavailableException("Progress directory is missing; offline initialization is required.");
        _lock = new FileStream(Path.Combine(_directory, "process.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _identity = File.ReadAllText(Path.Combine(_directory, "registration"));
            ValidateIdentity(_identity);
            using var conn = Open();
            if (conn.ExecuteScalar<string>("PRAGMA integrity_check") != "ok"
                || conn.Query("PRAGMA foreign_key_check").Any()
                || conn.ExecuteScalar<long>("""
                    SELECT count(*) FROM progress_registration
                     WHERE owner_count = (SELECT count(*) FROM progress_owners)
                       AND session_count = (SELECT count(*) FROM progress_capture_sessions)
                       AND event_count = (SELECT count(*) FROM progress_completions)
                    """) != 1)
                throw new ProgressUnavailableException("Progress integrity validation failed.");
        }
        catch { _lock.Dispose(); throw; }
    }

    public DbConnection Open()
    {
        if (File.ReadAllText(Path.Combine(_directory, "registration")) != _identity)
            throw new ProgressUnavailableException("Progress registration changed.");
        var conn = Connect(Path.Combine(_directory, "progress.sqlite"), SqliteOpenMode.ReadWrite);
        try
        {
            if (conn.ExecuteScalar<string>("""
                SELECT identity FROM progress_registration
                 WHERE owner_count = (SELECT count(*) FROM progress_owners)
                   AND session_count = (SELECT count(*) FROM progress_capture_sessions)
                   AND event_count = (SELECT count(*) FROM progress_completions)
                """) != _identity)
                throw new ProgressUnavailableException("Progress database identity or retained-record count mismatch.");
            conn.CreateFunction<long, string, string>("progress_date", (micros, zone) =>
                ReportingDates.Key(ReportingDates.LocalDate(ReportingDates.FromMicros(micros), ReportingDates.Zone(zone))), isDeterministic: true);
            return conn;
        }
        catch { conn.Dispose(); throw; }
    }

    private static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false, ForeignKeys = true
        }.ToString());
        try
        {
            conn.Open();
            conn.Execute("PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE; PRAGMA busy_timeout=30000;");
            return conn;
        }
        catch { conn.Dispose(); throw; }
    }
    private static string ValidateDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("Progress:DataDirectory must be absolute.");
        var full = Path.GetFullPath(directory);
        // Avoid accidental history in a checkout or build output.
        for (var parent = new DirectoryInfo(full); parent is not null; parent = parent.Parent)
            if (Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git"))
                || parent.Name is "bin" or "obj")
                throw new InvalidOperationException("Progress storage must be outside checkout and build directories.");
        return full;
    }
    private static void ValidateIdentity(string identity)
    {
        const string prefix = "roraquest-progress-v1:";
        if (!identity.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(identity[prefix.Length..], "D", out _))
            throw new ProgressUnavailableException("Invalid Progress registration.");
    }
    public void Dispose() => _lock.Dispose();

    private const string Schema = """
        CREATE TABLE progress_registration(identity TEXT PRIMARY KEY, owner_count INTEGER NOT NULL, session_count INTEGER NOT NULL, event_count INTEGER NOT NULL);
        CREATE TABLE progress_owners(owner_id TEXT PRIMARY KEY, tracking_started_at INTEGER NOT NULL,
            aggregate_revision INTEGER NOT NULL DEFAULT 0 CHECK(aggregate_revision>=0));
        CREATE TABLE progress_completions(sequence INTEGER PRIMARY KEY AUTOINCREMENT,
            owner_id TEXT NOT NULL REFERENCES progress_owners(owner_id) ON DELETE RESTRICT,
            unit_key TEXT COLLATE BINARY NOT NULL, kind TEXT NOT NULL CHECK(kind IN ('task','substep')),
            occurred_at INTEGER NOT NULL, task_id TEXT NOT NULL, substep_id TEXT,
            task_title TEXT NOT NULL, substep_title TEXT,
            UNIQUE(owner_id,unit_key),
            CHECK((kind='task' AND substep_id IS NULL AND substep_title IS NULL)
               OR (kind='substep' AND substep_id IS NOT NULL AND substep_title IS NOT NULL)));
        CREATE INDEX ix_progress_owner_time ON progress_completions(owner_id,occurred_at,unit_key);
        CREATE INDEX ix_progress_owner_sequence ON progress_completions(owner_id,sequence);
        CREATE TABLE progress_mutation_receipts(commit_id TEXT PRIMARY KEY,
            owner_id TEXT NOT NULL REFERENCES progress_owners(owner_id) ON DELETE RESTRICT,
            committed_at INTEGER NOT NULL, aggregate_revision INTEGER NOT NULL);
        CREATE TABLE progress_capture_sessions(session_id TEXT PRIMARY KEY, started_at INTEGER NOT NULL,
            verified_through INTEGER NOT NULL, stopped_at INTEGER,
            CHECK(verified_through>=started_at), CHECK(stopped_at IS NULL OR stopped_at>=verified_through));
        CREATE TABLE progress_capture_interruptions(id TEXT PRIMARY KEY, start_at INTEGER NOT NULL, end_at INTEGER,
            reason TEXT NOT NULL CHECK(reason IN ('maintenance','unrecognizedWriter','recovery')),
            CHECK(end_at IS NULL OR end_at>=start_at));
        CREATE UNIQUE INDEX ix_progress_one_open_interruption ON progress_capture_interruptions((1)) WHERE end_at IS NULL;
        CREATE TRIGGER progress_owner_counter AFTER INSERT ON progress_owners BEGIN
            UPDATE progress_registration SET owner_count=owner_count+1; END;
        CREATE TRIGGER progress_session_counter AFTER INSERT ON progress_capture_sessions BEGIN
            UPDATE progress_registration SET session_count=session_count+1; END;
        CREATE TRIGGER progress_event_counter AFTER INSERT ON progress_completions BEGIN
            UPDATE progress_registration SET event_count=event_count+1; END;
        """;
}
