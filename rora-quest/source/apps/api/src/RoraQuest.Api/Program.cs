using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
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
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var entra = builder.Configuration.GetSection("EntraAuth").Get<EntraAuthOptions>() ?? new EntraAuthOptions();
var authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";
var oauthEnabled = !string.IsNullOrWhiteSpace(entra.ClientId) && !string.IsNullOrWhiteSpace(entra.ClientSecret);

if (oauthEnabled)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "roraquest.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                }
            };
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.Authority = authority;
            options.ClientId = entra.ClientId;
            options.ClientSecret = entra.ClientSecret;
            options.CallbackPath = entra.CallbackPath;
            options.SignedOutCallbackPath = entra.SignedOutCallbackPath;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = ctx =>
                {
                    var path = ctx.Request.Path;
                    var isApiPath = path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
                    var isInteractiveAuthRoute =
                        path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase);

                    if (isApiPath && !isInteractiveAuthRoute)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.HandleResponse();
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = ctx =>
                {
                    AuthIdentity.AttachAppUserIdClaim(ctx);
                    return Task.CompletedTask;
                }
            };
        });
}

builder.Services.AddAuthorization();

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
builder.Services.AddHttpClient<ITeamsDigestSender, TeamsDigestSender>();
builder.Services.AddSingleton<DailyDigestDispatcher>();
builder.Services.AddHostedService<DailyDigestScheduler>();

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
app.UseForwardedHeaders();
if (oauthEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "RoraQuest.Api" }));
app.MapRoraQuestEndpoints(oauthEnabled);

app.Run();
