using System.Globalization;
using Npgsql;

namespace RoraQuest.Api.Progress;

public static class ProgressMaintenance
{
    public static bool TryRun(string[] args)
    {
        var commands = args.Where(a => a is "--progress-store-init" or "--progress-capture-suspend" or "--progress-capture-resume").ToArray();
        if (commands.Length == 0)
        {
            if (args.Any(a => a.StartsWith("--progress-", StringComparison.Ordinal)))
                throw new ArgumentException("Unknown Progress offline command.");
            return false;
        }
        if (commands.Length != 1) throw new ArgumentException("Specify exactly one Progress offline command.");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == commands[0]) continue;
            if (commands[0] != "--progress-store-init" && args[i] == "--confirm-writers-stopped") continue;
            if (commands[0] != "--progress-store-init" && args[i] == "--unknown-since" && i + 1 < args.Length)
            {
                if (Array.IndexOf(args, "--unknown-since") != i) throw new ArgumentException("Supply --unknown-since once.");
                i++;
                continue;
            }
            throw new ArgumentException("Unknown or incomplete Progress offline argument.");
        }
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var directory = config["Progress:DataDirectory"] ?? ProgressSqliteStore.DefaultDirectory;
        if (commands[0] == "--progress-store-init")
        {
            ProgressSqliteStore.Initialize(directory);
            Console.WriteLine("Progress store initialized. Tasks in InMemory mode remain volatile.");
            return true;
        }
        if (!args.Contains("--confirm-writers-stopped", StringComparer.Ordinal))
            throw new ArgumentException("Drain and stop ALL writers, then supply --confirm-writers-stopped.");
        DateTimeOffset? unknown = null;
        var index = Array.IndexOf(args, "--unknown-since");
        if (index >= 0)
        {
            if (index + 1 >= args.Length || !args[index + 1].EndsWith('Z')
                || !DateTimeOffset.TryParse(args[index + 1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                throw new ArgumentException("--unknown-since requires an explicit UTC instant ending in Z.");
            unknown = ReportingDates.Microseconds(parsed);
        }
        var connection = config.GetConnectionString("Postgres");
        using var sqlite = string.IsNullOrWhiteSpace(connection) ? new ProgressSqliteStore(directory) : null;
        using var pg = string.IsNullOrWhiteSpace(connection) ? null : NpgsqlDataSource.Create(connection);
        if (pg is not null) new DatabaseMigrator(pg, config["Postgres:MigrationsPath"], false).Run();
        var ledger = pg is null ? new ProgressLedger(sqlite!.Open, false, TimeProvider.System)
            : new ProgressLedger(() => pg.OpenConnection(), true, TimeProvider.System);
        Console.WriteLine(ledger.Maintenance(commands[0] == "--progress-capture-resume", unknown));
        return true;
    }
}
