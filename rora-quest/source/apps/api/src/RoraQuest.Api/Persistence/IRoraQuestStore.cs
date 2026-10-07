/// <summary>
/// Persistence boundary for the Rora Quest aggregate. Implementations load a
/// fully-hydrated <see cref="UserData"/> graph for a user and persist the whole
/// graph atomically. Two implementations exist:
///   - <see cref="InMemoryRoraQuestStore"/> (volatile tasks, durable Progress-only SQLite)
///   - <see cref="PostgresRoraQuestStore"/> (used when a connection string is configured)
/// </summary>
public interface IRoraQuestStore
{
    ProgressLedger Progress { get; }
    IDisposable AcquireOwnerScope(string userId);
    bool CommitTaskMutation(string userId, UserData prospective, ProgressUnit? unit, DateTimeOffset now, Action<DateTimeOffset> stamp);
    T ReadProgress<T>(string userId, Func<ProgressRead, UserData, T> project);
    /// <summary>Loads (or creates an empty) fully-hydrated aggregate for the given user.</summary>
    UserData Load(string userId);

    /// <summary>Persists the entire aggregate for the given user.</summary>
    void Save(string userId, UserData data);

    /// <summary>
    /// Deletes the given tasks (and their cascaded children) for a user with a single targeted
    /// database delete, avoiding a full-aggregate delete-then-reinsert. Callers remove
    /// tasks from the in-memory graph only after this operation commits.
    /// Returns the number of task rows removed from the database.
    /// </summary>
    int DeleteTasks(string userId, IReadOnlyCollection<System.Guid> taskIds);

    /// <summary>Persists notification settings for a user without rewriting the full aggregate.</summary>
    void SaveNotificationSettings(string userId, NotificationSettings settings);

    /// <summary>Upserts a provider integration for a user without rewriting the full aggregate.</summary>
    void UpsertIntegration(string userId, IntegrationSetting setting);

    /// <summary>Marks a provider integration as disconnected; returns false when no row matched.</summary>
    bool DisconnectIntegration(string userId, string provider);

    /// <summary>Appends one notification schedule row without rewriting the full aggregate.</summary>
    void AddNotificationSchedule(string userId, NotificationSchedule schedule);

    /// <summary>Returns user ids known to the store, used by background schedulers.</summary>
    IReadOnlyCollection<string> GetKnownUserIds();
}

/// <summary>
/// Default store used when no PostgreSQL connection string is configured.
/// Backed by the in-process <see cref="AppState"/> dictionaries. Mutations happen
/// in place on the cached graph, so <see cref="Save"/> only needs to ensure the
/// reference is registered.
/// </summary>
public sealed class InMemoryRoraQuestStore(AppState state, ProgressLedger progress) : IRoraQuestStore
{
    private readonly object _ownerGate = new();
    public ProgressLedger Progress => progress;
    public IDisposable AcquireOwnerScope(string userId)
    {
        Monitor.Enter(_ownerGate);
        try
        {
            progress.InitializeOwner(userId);
            return new ProgressScope(() => Monitor.Exit(_ownerGate));
        }
        catch { Monitor.Exit(_ownerGate); throw; }
    }
    public bool CommitTaskMutation(string userId, UserData prospective, ProgressUnit? unit, DateTimeOffset now, Action<DateTimeOffset> stamp) =>
        progress.CommitMutation(userId, unit, now, stamp: stamp);
    public T ReadProgress<T>(string userId, Func<ProgressRead, UserData, T> project) =>
        progress.Read(userId, read => project(read, Load(userId)));
    public UserData Load(string userId)
    {
        if (!state.Users.TryGetValue(userId, out var user))
        {
            user = new UserData();
            state.Users[userId] = user;
        }
        return user;
    }

    public void Save(string userId, UserData data)
    {
        state.Users[userId] = data;
    }

    public int DeleteTasks(string userId, IReadOnlyCollection<System.Guid> taskIds)
    {
        // No persistent task graph in this mode. Publish only after the service's successful call.
        if (!state.Users.TryGetValue(userId, out var user)) return 0;
        return taskIds.Count(user.Tasks.ContainsKey);
    }

    public void SaveNotificationSettings(string userId, NotificationSettings settings)
    {
        // In-memory graph is authoritative.
    }

    public void UpsertIntegration(string userId, IntegrationSetting setting)
    {
        // In-memory graph is authoritative.
    }

    public bool DisconnectIntegration(string userId, string provider)
    {
        // Service mutates the graph before calling this method.
        return true;
    }

    public void AddNotificationSchedule(string userId, NotificationSchedule schedule)
    {
        // In-memory graph is authoritative.
    }

    public IReadOnlyCollection<string> GetKnownUserIds()
    {
        return state.Users.Keys.ToArray();
    }
}

internal sealed class ProgressScope(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
