using System.Reflection;
using EOS.Domain.Settings;

var builder = WebApplication.CreateBuilder(args);

// Default listen address; production overrides via ASPNETCORE_URLS (bind to LAN + Tailscale only, spec §25.9).
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://127.0.0.1:8095");

var app = builder.Build();
var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

app.MapGet("/health", () => Results.Ok(new { status = "ok", product = "EOS V2", version }));

app.MapGet("/api/v1/settings/defaults", () => Results.Ok(SettingDefaults.All));

app.Run();

public partial class Program;
