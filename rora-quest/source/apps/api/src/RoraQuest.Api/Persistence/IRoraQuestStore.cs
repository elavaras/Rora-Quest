/// <summary>
/// Persistence boundary for the Rora Quest aggregate. Implementations load a
/// fully-hydrated <see cref="UserData"/> graph for a user and persist the whole
/// graph atomically. Two implementations exist:
///   - <see cref="InMemoryRoraQuestStore"/> (default, no database required)
///   - <see cref="PostgresRoraQuestStore"/> (used when a connection string is configured)
/// </summary>
public interface IRoraQuestStore
{
    /// <summary>Loads (or creates an empty) fully-hydrated aggregate for the given user.</summary>
    UserData Load(string userId);

    /// <summary>Persists the entire aggregate for the given user.</summary>
    void Save(string userId, UserData data);
}

/// <summary>
/// Default store used when no PostgreSQL connection string is configured.
/// Backed by the in-process <see cref="AppState"/> dictionaries. Mutations happen
/// in place on the cached graph, so <see cref="Save"/> only needs to ensure the
/// reference is registered.
/// </summary>
public sealed class InMemoryRoraQuestStore(AppState state) : IRoraQuestStore
{
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
}
