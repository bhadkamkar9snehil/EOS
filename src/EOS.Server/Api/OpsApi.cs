using System.Reflection;
using EOS.Domain.Configuration;
using EOS.Domain.Settings;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.Operations;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Reports;
using EOS.Infrastructure.Security;
using EOS.Server.Hosting;
using Microsoft.EntityFrameworkCore;

namespace EOS.Server.Api;

public sealed record SettingBody(string Key, SettingScopeKind ScopeKind, string? ScopeId, string JsonValue, DateOnly? EffectiveFrom, string Reason);
public sealed record VocabBody(string Kind, string Value, string? Color);
public sealed record ActiveBody(bool Active);
public sealed record CalendarBody(int? Id, string Name, decimal HoursPerDay, string WorkDays, bool IsDefault);
public sealed record HolidayBody(DateOnly Date, bool IsWorking, string? Note);
public sealed record HostLinkBody(int ProjectId, int HostId, string Role);
public sealed record TokenBody(string Name);

public static class OpsApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (DashboardService svc, DateOnly? asOf) => await svc.GetAsync(asOf ?? PlanningApi.Today));
        api.MapGet("/alerts", async (AlertService svc, DateOnly? asOf) => await svc.EvaluateAsync(asOf ?? PlanningApi.Today));

        api.MapGet("/hosts", async (HostService svc, int? projectId, bool? includeRemoved) => await svc.ListAsync(projectId, includeRemoved ?? false));
        api.MapGet("/hosts/reclaim", async (HostService svc) => await svc.ReclaimListAsync());
        api.MapGet("/hosted-systems", async (HostService svc, int? projectId) => await svc.SystemsAsync(projectId));
        api.MapPatch("/hosts/{id:int}", async (HttpContext http, HostService svc, int id, HostPatch patch) => { await svc.UpdateAsync(id, patch, http.Actor()); return Results.NoContent(); });
        api.MapPost("/host-links", async (HttpContext http, HostService svc, HostLinkBody b) => { await svc.LinkAsync(b.ProjectId, b.HostId, b.Role, http.Actor()); return Results.NoContent(); });
        api.MapDelete("/host-links", async (HttpContext http, HostService svc, int projectId, int hostId, string role) => { await svc.UnlinkAsync(projectId, hostId, role, http.Actor()); return Results.NoContent(); });

        api.MapGet("/reports", () => ReportService.Catalogue);
        api.MapGet("/reports/{id}", async (ReportService svc, string id, string? format, DateOnly? asOf, DateOnly? from, DateOnly? to, int? projectId, int? teamId, int? months) =>
        {
            var table = await svc.RunAsync(id, new ReportParameters(asOf ?? PlanningApi.Today, from, to, projectId, teamId, months ?? 3));
            return (format ?? "json").ToLowerInvariant() switch
            {
                "csv" => Results.File(System.Text.Encoding.UTF8.GetBytes(ReportService.ToCsv(table)), "text/csv", $"{id}.csv"),
                "xlsx" => Results.File(ReportService.ToXlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{id}.xlsx"),
                _ => Results.Ok(table),
            };
        });

        api.MapPost("/imports/{kind}", async (HttpContext http, EosDbContext db, string kind, IFormFile file, bool? commit, string? month) =>
        {
            var temp = Path.Combine(Path.GetTempPath(), $"eos-import-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            try
            {
                await using (var fs = File.Create(temp)) await file.CopyToAsync(fs);
                var actor = http.Actor();
                var doCommit = commit ?? false;
                var report = kind.ToLowerInvariant() switch
                {
                    "agility" => await new AgilityWorkbookImporter(db).ImportAsync(temp, doCommit, actor),
                    "training" => await new TrainingWorkbookImporter(db).ImportAsync(temp, doCommit, actor),
                    "erp" => await new ErpProjectImporter(db).ImportAsync(temp, doCommit, actor),
                    "tracker" => await new TrackerImporter(db).ImportAsync(temp, doCommit, actor),
                    "leave" => await new LeaveImporter(db).ImportAsync(temp, doCommit, actor),
                    "vms" => await new InfrastructureImporter(db).ImportAsync(temp, doCommit, actor),
                    "files" => await new PerformanceFilesImporter(db).ImportAsync(temp, doCommit, actor,
                        DateTime.TryParseExact(month, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var ym) ? ym.Year : null,
                        ym.Month == 0 ? null : ym.Month),
                    _ => throw new ArgumentException($"Unknown import kind '{kind}'. Use agility, training, erp, tracker, leave, vms or files."),
                };
                return Results.Ok(new { report.Kind, File = file.FileName, report.Committed, Counts = report.Counts, report.Warnings, report.Unresolved });
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }).DisableAntiforgery();

        api.MapGet("/settings", async (SettingsStore store, EosDbContext db) =>
        {
            var overrides = await db.Settings.AsNoTracking().OrderBy(s => s.Key).ThenBy(s => s.EffectiveFrom).ToListAsync();
            var resolver = await store.LoadResolverAsync();
            var today = PlanningApi.Today;
            return new
            {
                settings = SettingDefaults.All.OrderBy(kv => kv.Key).Select(kv => new { key = kv.Key, seedDefault = kv.Value, effective = resolver.ResolveJson(kv.Key, SettingsResolver.Chain(), today) }),
                overrides = overrides.Select(o => new { o.Key, scopeKind = ((SettingScopeKind)o.ScopeKind).ToString(), o.ScopeId, o.JsonValue, o.EffectiveFrom, o.EffectiveTo, o.ChangedBy, o.Reason }),
            };
        });
        api.MapPut("/settings", async (HttpContext http, SettingsStore store, SettingBody b) =>
        {
            await store.SetAsync(b.Key, new SettingScope(b.ScopeKind, string.IsNullOrEmpty(b.ScopeId) ? null : b.ScopeId), b.JsonValue, b.EffectiveFrom ?? PlanningApi.Today, http.Actor(), b.Reason);
            return Results.NoContent();
        });
        api.MapGet("/vocabularies", async (AdminService svc) => await svc.VocabulariesAsync());
        api.MapPost("/vocabularies", async (HttpContext http, AdminService svc, VocabBody b) => await svc.AddValueAsync(b.Kind, b.Value, b.Color, http.Actor()));
        api.MapPut("/vocabularies/{id:int}/active", async (HttpContext http, AdminService svc, int id, ActiveBody b) => { await svc.SetActiveAsync(id, b.Active, http.Actor()); return Results.NoContent(); });
        api.MapGet("/calendars", async (AdminService svc) => (await svc.CalendarsAsync()).Select(c => new { calendar = c.Calendar, exceptions = c.Exceptions }));
        api.MapPut("/calendars", async (HttpContext http, AdminService svc, CalendarBody b) =>
            await svc.SaveCalendarAsync(new WorkCalendarDef { Id = b.Id ?? 0, Name = b.Name, HoursPerDay = b.HoursPerDay, WorkDays = b.WorkDays, IsDefault = b.IsDefault }, http.Actor()));
        api.MapPut("/calendars/{id:int}/exceptions", async (HttpContext http, AdminService svc, int id, HolidayBody b) => { await svc.SetHolidayAsync(id, b.Date, b.IsWorking, b.Note, http.Actor()); return Results.NoContent(); });
        api.MapDelete("/calendar-exceptions/{id:int}", async (HttpContext http, AdminService svc, int id) => { await svc.RemoveHolidayAsync(id, http.Actor()); return Results.NoContent(); });

        api.MapPost("/backups", async (HttpContext http, BackupService svc, EosOptions o) => await svc.CreateAsync(o.BackupDirectory, http.Actor()));
        api.MapGet("/backups", (EosOptions o) => BackupService.List(o.BackupDirectory));
        api.MapGet("/audit", async (EosDbContext db, int? take, string? actor, string? action) =>
            await db.AuditEvents.AsNoTracking().Where(a => (actor == null || a.Actor == actor) && (action == null || a.Action.StartsWith(action)))
                .OrderByDescending(a => a.Id).Take(Math.Clamp(take ?? 100, 1, 1000)).ToListAsync());
        api.MapGet("/diagnostics", async (EosDbContext db, EosOptions o) => new
        {
            version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3), databasePath = o.DatabasePath,
            databaseBytes = File.Exists(o.DatabasePath) ? new FileInfo(o.DatabasePath).Length : 0L,
            counts = new { people = await db.People.CountAsync(), projects = await db.Projects.CountAsync(), tasks = await db.Tasks.CountAsync(), hosts = await db.Hosts.CountAsync(), audit = await db.AuditEvents.CountAsync() },
            utc = DateTime.UtcNow,
        });

        api.MapGet("/tokens", async (TokenService svc) => (await svc.ListAsync()).Select(t => new { t.Id, t.Name, t.CreatedUtc, t.LastUsedUtc, t.Revoked }));
        api.MapPost("/tokens", async (HttpContext http, TokenService svc, TokenBody b) => new { token = await svc.CreateAsync(b.Name, http.Actor()), note = "Store this token now; it is shown only once." });
        api.MapDelete("/tokens/{id:int}", async (HttpContext http, TokenService svc, int id) => { await svc.RevokeAsync(id, http.Actor()); return Results.NoContent(); });
    }
}
