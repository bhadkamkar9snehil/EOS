using EOS.Domain.Projects;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;

namespace EOS.Server.Api;

public sealed record ProjectBody(
    string ProjectNo, string Name, string? Customer, string? ProductLine, string? ProjectType, string? ErpStatus, string? TrackerStatus,
    string? ProjectManager, string? TechnicalManager, decimal? OrderValue, DateOnly? PoStart, DateOnly? PoEnd, DateOnly? PlannedEnd,
    string? SiteLocation, string? Notes, string? HealthOverride, string? HealthOverrideReason, int? CalendarId);

public sealed record TaskBody(string Name, int? ParentId, int? AfterTaskId, decimal WorkHours, string? Kind);
public sealed record MoveBody(MoveDirection Direction);
public sealed record LinkBody(int PredecessorId, int SuccessorId, int Type, int LagDays);
public sealed record AssignBody(int? PersonId, decimal Units, DateOnly? From, DateOnly? To, int? PlaceholderSkillId, int? PlaceholderMinLevel);
public sealed record BaselineBody(string? Name);
public sealed record RecomputeQuery(DateOnly? StatusDate);

public static class PlanningApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/projects", async (ProjectService svc, string? search, bool? withPlan) => await svc.ListAsync(search, withPlan ?? false));
        api.MapGet("/projects/{id:int}", async (EosDbContext db, int id) =>
            await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id) is { } p ? Results.Ok(p) : Results.NotFound());
        api.MapPost("/projects", async (HttpContext http, ProjectService svc, ProjectBody b) =>
        {
            var p = await svc.CreateAsync(ToEntity(b, 0), http.Actor());
            return Results.Created($"/api/v1/projects/{p.Id}", p);
        });
        api.MapPut("/projects/{id:int}", async (HttpContext http, ProjectService svc, int id, ProjectBody b) =>
        {
            await svc.UpdateAsync(ToEntity(b, id), http.Actor());
            return Results.NoContent();
        });

        api.MapGet("/projects/{id:int}/plan", async (PlanService svc, int id) => await svc.GetPlanAsync(id));
        api.MapPost("/projects/{id:int}/plan/recompute", async (PlanService svc, int id, DateOnly? statusDate) => await svc.RecomputeAsync(id, statusDate));
        api.MapPost("/projects/{id:int}/tasks", async (HttpContext http, PlanService svc, int id, TaskBody b) =>
        {
            var t = await svc.AddTaskAsync(id, b.Name, http.Actor(), b.ParentId, b.AfterTaskId, b.WorkHours, b.Kind ?? "task");
            return Results.Created($"/api/v1/tasks/{t.Id}", new { t.Id, t.Name });
        });
        api.MapPatch("/tasks/{id:int}", async (HttpContext http, PlanService svc, int id, TaskPatch patch) =>
        {
            await svc.UpdateTaskAsync(id, patch, http.Actor());
            return Results.NoContent();
        });
        api.MapDelete("/tasks/{id:int}", async (HttpContext http, PlanService svc, int id) => { await svc.DeleteTaskAsync(id, http.Actor()); return Results.NoContent(); });
        api.MapPost("/tasks/{id:int}/move", async (HttpContext http, PlanService svc, int id, MoveBody b) => { await svc.MoveTaskAsync(id, b.Direction, http.Actor()); return Results.NoContent(); });

        api.MapPost("/links", async (HttpContext http, PlanService svc, LinkBody b) =>
        {
            var l = await svc.AddLinkAsync(b.PredecessorId, b.SuccessorId, b.Type, b.LagDays, http.Actor());
            return Results.Created($"/api/v1/links/{l.Id}", new { l.Id });
        });
        api.MapDelete("/links/{id:int}", async (HttpContext http, PlanService svc, int id) => { await svc.RemoveLinkAsync(id, http.Actor()); return Results.NoContent(); });

        api.MapPost("/tasks/{id:int}/assignments", async (HttpContext http, PlanService svc, int id, AssignBody b) =>
        {
            var a = await svc.AssignAsync(id, b.PersonId, b.Units == 0 ? 1m : b.Units, http.Actor(), b.From, b.To, b.PlaceholderSkillId, b.PlaceholderMinLevel);
            return Results.Created($"/api/v1/assignments/{a.Id}", new { a.Id });
        });
        api.MapDelete("/assignments/{id:int}", async (HttpContext http, PlanService svc, int id) => { await svc.UnassignAsync(id, http.Actor()); return Results.NoContent(); });

        api.MapPost("/projects/{id:int}/baselines", async (HttpContext http, PlanService svc, int id, BaselineBody b) =>
        {
            var baseline = await svc.CreateBaselineAsync(id, b.Name ?? "", http.Actor());
            return Results.Created($"/api/v1/projects/{id}/baselines/{baseline.Number}", new { baseline.Id, baseline.Number, baseline.Name });
        });
        api.MapGet("/projects/{id:int}/baselines", async (EosDbContext db, int id) =>
            await db.Baselines.AsNoTracking().Where(b => b.ProjectId == id).OrderBy(b => b.Number).Select(b => new { b.Number, b.Name, b.CreatedUtc, b.CreatedBy }).ToListAsync());
        api.MapGet("/projects/{id:int}/baseline-variance", async (PlanService svc, int id, int? baseline) => await svc.GetBaselineVarianceAsync(id, baseline));

        api.MapGet("/projects/{id:int}/health", async (HealthService svc, int id, DateOnly? asOf) => await svc.GetAsync(id, asOf ?? Today));
        api.MapGet("/health/projects", async (HealthService svc, DateOnly? asOf) => await svc.GetAllAsync(asOf ?? Today));

        api.MapGet("/projects/{id:int}/hosts", async (EOS.Infrastructure.Hosting.HostService svc, int id) => await svc.ListAsync(projectId: id));
        api.MapGet("/projects/{id:int}/demands", async (EOS.Infrastructure.Capacity.DemandService svc, int id) => await svc.ListAsync(projectId: id));
        api.MapGet("/projects/{id:int}/risks", async (EosDbContext db, int id) => await db.RiskIssues.AsNoTracking().Where(r => r.ProjectId == id).ToListAsync());
    }

    internal static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static Project ToEntity(ProjectBody b, int id) => new()
    {
        Id = id, ProjectNo = b.ProjectNo, Name = b.Name, Customer = b.Customer, ProductLine = b.ProductLine, ProjectType = b.ProjectType, ErpStatus = b.ErpStatus,
        TrackerStatus = b.TrackerStatus, ProjectManager = b.ProjectManager, TechnicalManager = b.TechnicalManager, OrderValue = b.OrderValue, PoStart = b.PoStart,
        PoEnd = b.PoEnd, PlannedEnd = b.PlannedEnd, SiteLocation = b.SiteLocation, Notes = b.Notes, HealthOverride = b.HealthOverride,
        HealthOverrideReason = b.HealthOverrideReason, CalendarId = b.CalendarId,
    };
}
