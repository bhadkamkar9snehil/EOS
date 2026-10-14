using System.Reflection;
using System.Text.Json.Serialization;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.Operations;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Projects;
using EOS.Infrastructure.Reports;
using EOS.Infrastructure.Security;
using EOS.Infrastructure.Skills;
using EOS.Infrastructure.Training;
using EOS.Server.Api;
using EOS.Server.Hosting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// Lets `dotnet run` outside Development (and any non-published start) find the framework's blazor script; harmless when published.
builder.WebHost.UseStaticWebAssets();

// Listen address: explicit config (ASPNETCORE_URLS / --urls) wins. Production binds to the Tailscale and LAN addresses only (spec §25.9).
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://127.0.0.1:8095");

var options = EosOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);

builder.Services.AddDbContext<EosDbContext>(o => o.UseSqlite($"Data Source={options.DatabasePath}"));
foreach (var service in new[]
{
    typeof(PlanService), typeof(ProjectService), typeof(CapacityService), typeof(ScenarioService), typeof(DemandService), typeof(PipelineService),
    typeof(HealthService), typeof(AlertService), typeof(DashboardService), typeof(PeopleService), typeof(TrainingService), typeof(HostService),
    typeof(AdminService), typeof(ReportService), typeof(BackupService), typeof(TokenService), typeof(SkillRatingService), typeof(SettingsStore),
})
    builder.Services.AddScoped(service);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddEosAuthentication();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<EOS.Server.Ui.UiState>();

var app = builder.Build();

// Database: migrate, WAL, seed defaults.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EosDbContext>();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!);
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    ConfigurationService.SeedDefaults(db);
    if (!await scope.ServiceProvider.GetRequiredService<TokenService>().AnyActiveAsync())
        app.Logger.LogWarning("No API tokens exist. Create one: `eos token create <name> --db \"{Db}\"` (or open the web UI on this machine and use 'Create first token').", options.DatabasePath);
}

app.UseExceptionHandler(builder => builder.Run(ErrorMapping.HandleAsync));
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
app.MapGet("/health", async (EosDbContext db) =>
{
    var ok = await db.Database.CanConnectAsync();
    return ok ? Results.Ok(new { status = "ok", product = "EOS V2", version }) : Results.Json(new { status = "database unavailable" }, statusCode: 503);
}).AllowAnonymous();

AuthApi.Map(app);
var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapGet("/", () => new { product = "EOS V2", version, docs = "/openapi/v1.json" });
PlanningApi.Map(api);
PeopleApi.Map(api);
CapacityApi.Map(api);
OpsApi.Map(api);
app.MapGet("/openapi/v1.json", (EndpointDataSource endpoints) => Results.Json(ApiCatalog.Build(endpoints, version))).AllowAnonymous();

// MapStaticAssets serves wwwroot and the framework scripts (blazor.web.js) in every environment, not only Development.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<EOS.Server.Ui.App>().AddInteractiveServerRenderMode().RequireAuthorization();

app.Run();

public partial class Program;
