using System.Text.Json;
using Npgsql;
using RoraQuest.Api.Progress;

// Test-only executable. The optional fixture restores volatile task identities BEFORE startup,
// never ledger rows; there is deliberately no restore HTTP endpoint in either executable.
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.AddEnvironmentVariables();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
var state = new AppState();
var fixtureJson = Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_TEST_FIXTURE");
if (fixtureJson is not null)
{
    var fixture = JsonSerializer.Deserialize<TaskFixture>(fixtureJson)!;
    var user = new UserData();
    var task = new TaskItem
    {
        Id = fixture.TaskId, UserId = fixture.Owner, AssignedTo = fixture.Owner, Title = fixture.Title,
        PlannedWeekStart = ReportingDates.Monday(DateOnly.FromDateTime(DateTime.UtcNow)), CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    if (fixture.SubstepId is { } step) task.SubSteps.Add(new(step, "Restored substep", false, 1, null, 1, 1));
    user.Tasks[task.Id] = task;
    state.Users[fixture.Owner] = user;
}
builder.Services.AddSingleton(state);
var connection = Environment.GetEnvironmentVariable("RORAQUEST_PROGRESS_TEST_POSTGRES");
if (string.IsNullOrWhiteSpace(connection))
{
    builder.Services.AddSingleton(_ => new ProgressSqliteStore(builder.Configuration["Progress:DataDirectory"]!));
    builder.Services.AddSingleton(sp => new ProgressLedger(sp.GetRequiredService<ProgressSqliteStore>().Open, false, TimeProvider.System));
    builder.Services.AddSingleton<IRoraQuestStore, InMemoryRoraQuestStore>();
}
else
{
    var source = NpgsqlDataSource.Create(connection);
    new DatabaseMigrator(source, builder.Configuration["Postgres:MigrationsPath"], false).Run();
    builder.Services.AddSingleton(source);
    builder.Services.AddSingleton(_ => new ProgressLedger(() => source.OpenConnection(), true, TimeProvider.System));
    builder.Services.AddSingleton<IRoraQuestStore, PostgresRoraQuestStore>();
}
builder.Services.AddSingleton<RoraQuestService>();
builder.Services.AddHostedService<ProgressCaptureLifecycle>();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok());
app.MapRoraQuestEndpoints(false);
app.Run();

internal sealed record TaskFixture(string Owner, Guid TaskId, Guid? SubstepId, string Title);
