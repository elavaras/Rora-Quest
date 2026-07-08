using System.Text.Json.Serialization;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("web-dev", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000", "http://localhost:3001")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// AppState backs the in-memory store (default when no database is configured).
builder.Services.AddSingleton<AppState>();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration["ConnectionStrings:Postgres"];

if (!string.IsNullOrWhiteSpace(connectionString))
{
    // PostgreSQL-backed persistence.
    var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
    builder.Services.AddSingleton(dataSource);
    builder.Services.AddSingleton<IRoraQuestStore, PostgresRoraQuestStore>();
}
else
{
    // Default: in-memory store, no database required.
    builder.Services.AddSingleton<IRoraQuestStore, InMemoryRoraQuestStore>();
}

builder.Services.AddSingleton<RoraQuestService>();

var app = builder.Build();

// Run database migrations on startup when PostgreSQL is configured.
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
    var migrationsPath = app.Configuration["Postgres:MigrationsPath"];
    var runSeed = app.Configuration.GetValue("Postgres:RunSeed", false);
    var migrator = new DatabaseMigrator(dataSource, migrationsPath, runSeed);
    migrator.Run();
}

app.UseCors("web-dev");

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "RoraQuest.Api" }));
app.MapRoraQuestEndpoints();

app.Run();
