using Npgsql;

/// <summary>
/// Minimal, dependency-free migration runner. Applies <c>V*__*.sql</c> scripts from
/// <c>infra/sql</c> in version order, tracking applied versions in the
/// <c>schema_migrations</c> table. Each script is idempotent (IF NOT EXISTS DDL) and
/// self-registers its version, so re-runs are safe.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _migrationsPathOverride;
    private readonly bool _runSeed;

    public DatabaseMigrator(NpgsqlDataSource dataSource, string? migrationsPathOverride, bool runSeed)
    {
        _dataSource = dataSource;
        _migrationsPathOverride = migrationsPathOverride;
        _runSeed = runSeed;
    }

    public void Run()
    {
        var sqlDir = ResolveSqlDirectory();
        if (sqlDir is null)
        {
            throw new InvalidOperationException(
                "Could not locate the infra/sql migrations directory. Set 'Postgres:MigrationsPath' to an absolute path.");
        }

        using var conn = _dataSource.OpenConnection();

        EnsureMigrationsTable(conn);
        var applied = LoadAppliedVersions(conn);

        var scripts = Directory
            .EnumerateFiles(sqlDir, "V*__*.sql")
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .Where(x => x.Version is not null)
            .OrderBy(x => x.Version!.Value)
            .ToList();

        foreach (var script in scripts)
        {
            var versionKey = $"V{script.Version!.Value}";
            if (applied.Contains(versionKey))
            {
                continue;
            }

            var sql = File.ReadAllText(script.Path);
            using var tx = conn.BeginTransaction();
            using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.ExecuteNonQuery();
            }

            // Guarantee the version is recorded even if the script itself did not.
            using (var mark = new NpgsqlCommand(
                "INSERT INTO schema_migrations (version, description) VALUES (@v, @d) ON CONFLICT (version) DO NOTHING",
                conn, tx))
            {
                mark.Parameters.AddWithValue("v", versionKey);
                mark.Parameters.AddWithValue("d", Path.GetFileNameWithoutExtension(script.Path));
                mark.ExecuteNonQuery();
            }

            tx.Commit();
        }

        if (_runSeed)
        {
            var seedPath = Path.Combine(sqlDir, "seed.dev.sql");
            if (File.Exists(seedPath))
            {
                var seedSql = File.ReadAllText(seedPath);
                using var cmd = new NpgsqlCommand(seedSql, conn);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static void EnsureMigrationsTable(NpgsqlConnection conn)
    {
        const string sql = @"
CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    private static HashSet<string> LoadAppliedVersions(NpgsqlConnection conn)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            set.Add(reader.GetString(0));
        }
        return set;
    }

    private static int? ParseVersion(string fileName)
    {
        // Expected pattern: V<number>__<description>.sql
        if (fileName.Length < 2 || (fileName[0] != 'V' && fileName[0] != 'v'))
        {
            return null;
        }

        var i = 1;
        while (i < fileName.Length && char.IsDigit(fileName[i]))
        {
            i++;
        }

        return i > 1 && int.TryParse(fileName.AsSpan(1, i - 1), out var version) ? version : null;
    }

    private string? ResolveSqlDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_migrationsPathOverride) && Directory.Exists(_migrationsPathOverride))
        {
            return _migrationsPathOverride;
        }

        // Walk up from a few well-known roots looking for infra/sql.
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in roots)
        {
            var dir = new DirectoryInfo(root);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "infra", "sql");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }

        return null;
    }
}
