using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using RoraQuest.Api.Progress;

public sealed partial class PostgresRoraQuestStore
{
    public ProgressLedger Progress { get; }
    private readonly ConcurrentDictionary<string, long> _revisions = new(StringComparer.Ordinal);
    // Service methods are synchronous while scoped, including the two short sections of asset I/O.
    private readonly ThreadLocal<OwnerLease?> _owner = new();
    private sealed class OwnerLease(string owner, long key, NpgsqlConnection connection)
    {
        public string Owner { get; } = owner;
        public long Key { get; } = key;
        public NpgsqlConnection Connection { get; set; } = connection;
        public int Depth { get; set; } = 1;
    }
    public IDisposable AcquireOwnerScope(string userId)
    {
        Progress.AssertOwnerAvailable(userId);
        if (_owner.Value is { } nested)
        {
            if (nested.Owner != userId) throw new InvalidOperationException("Cannot nest scopes for different owners.");
            nested.Depth++;
            return new ProgressScope(ReleaseOwner);
        }
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(userId)));
        var conn = _dataSource.OpenConnection();
        try
        {
            conn.Execute("SELECT pg_advisory_lock(@key)", new { key });
            _owner.Value = new(userId, key, conn);
            Progress.InitializeOwner(userId, conn);
            return new ProgressScope(ReleaseOwner);
        }
        catch
        {
            _owner.Value = null;
            // Explicit unlock also covers pooled connections, whose Reset runs only on next checkout.
            try { conn.Execute("SELECT pg_advisory_unlock(@key)", new { key }); }
            finally { conn.Dispose(); }
            throw;
        }
    }
    private void ReleaseOwner()
    {
        var scope = _owner.Value!;
        if (--scope.Depth != 0) return;
        _owner.Value = null;
        try
        {
            if (scope.Connection.State == System.Data.ConnectionState.Open)
                scope.Connection.Execute("SELECT pg_advisory_unlock(@key)", new { key = scope.Key });
        }
        finally { scope.Connection.Dispose(); }
    }
    private NpgsqlConnection OwnerConnection(string owner)
    {
        var scope = _owner.Value;
        if (scope?.Owner != owner) throw new InvalidOperationException("A scoped owner operation is required.");
        return scope.Connection;
    }
    private NpgsqlConnection ReacquireOwner(string owner)
    {
        var scope = _owner.Value!;
        Evict(owner);
        try
        {
            if (scope.Connection.State == System.Data.ConnectionState.Open)
                scope.Connection.Execute("SELECT pg_advisory_unlock(@key)", new { key = scope.Key });
        }
        finally { scope.Connection.Dispose(); }
        scope.Connection = _dataSource.OpenConnection();
        scope.Connection.Execute("SELECT pg_advisory_lock(@key)", new { key = scope.Key });
        return scope.Connection;
    }
    private void Evict(string owner)
    {
        _cache.TryRemove(owner, out _);
        _revisions.TryRemove(owner, out _);
    }
    public bool CommitTaskMutation(string userId, UserData prospective, ProgressUnit? unit, DateTimeOffset now, Action<DateTimeOffset> stamp)
    {
        try
        {
            var latest = Progress.CommitMutation(userId, unit, now,
                (conn, tx) => Persist(userId, prospective, (NpgsqlConnection)conn, (NpgsqlTransaction)tx),
                OwnerConnection(userId), () => ReacquireOwner(userId), stamp);
            if (latest)
            {
                // No fallible I/O after a known commit: the caller publishes prepared live objects.
                _revisions.AddOrUpdate(userId, 1, (_, value) => value + 1);
            }
            else Evict(userId);
            return latest;
        }
        catch { Evict(userId); throw; }
    }
    public T ReadProgress<T>(string userId, Func<ProgressRead, UserData, T> project) =>
        Progress.Read(userId, read => project(read, Load(userId)), OwnerConnection(userId));

    private WriteLease BeginWrite(string owner) => new(this, owner);
    private sealed class WriteLease : IDisposable
    {
        private readonly PostgresRoraQuestStore _store;
        private readonly string _owner;
        private bool _committed;
        public NpgsqlConnection Connection { get; }
        public NpgsqlTransaction Transaction { get; }
        public WriteLease(PostgresRoraQuestStore store, string owner)
        {
            _store = store;
            _owner = owner;
            Connection = store.OwnerConnection(owner);
            Transaction = Connection.BeginTransaction();
            try { store.Progress.Control(Connection, Transaction); }
            catch { Transaction.Dispose(); store.Evict(owner); throw; }
        }
        public void Commit()
        {
            var revision = _store.Progress.IncrementRevision(Connection, Transaction, _owner);
            Transaction.Commit();
            _store._revisions[_owner] = revision;
            _committed = true;
        }
        public void Dispose()
        {
            try { Transaction.Dispose(); }
            finally { if (!_committed) _store.Evict(_owner); }
        }
    }
}
