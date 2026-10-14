using EOS.Domain.Capacity;
using EOS.Domain.Projects;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Server.Api;

public sealed record DemandBody(int? ProjectId, int? OpportunityId, int SkillId, int MinLevel, decimal HoursPerWeek, DateOnly StartDate, DateOnly EndDate, string? Location, string? StatusOverride, string? Notes);
public sealed record OpportunityBody(string Code, string Name, string? LeadName, decimal? ExpectedValueLakh, decimal? ExpectedHours, DateOnly? LikelyBookingMonth, int? DurationMonths, string? Stage, decimal WinProbability, string? RequiredSkills);
public sealed record ConvertBody(string ProjectNo);
public sealed record ScenarioBody(string Name, string? Notes, IReadOnlyList<MoveInput> Moves, int? ScenarioId, DateOnly? Today);
public sealed record DecisionBody(bool Approve, string? Note);
public sealed record ApplyBody(DateOnly? Today);
public sealed record LogBody(string Kind, int? ProjectId, int? DemandId, string Description, string? GapType, decimal ImpactHours, decimal ImpactDays, string? Decision, string? Approver, string? Status);
public sealed record MovementBody(int PersonId, int FromProjectId, int ToProjectId, DateOnly EffectiveDate, decimal? Percent, string? Reason);

/// <summary>Capacity, load, demand, pipeline, what-if scenarios, movements and the change log (spec §8, §12, §15, §17).</summary>
public static class CapacityApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/capacity", async (CapacityService svc, DateOnly? from, DateOnly? to, int? teamId, int? personId) =>
        {
            var f = from ?? new DateOnly(PlanningApi.Today.Year, PlanningApi.Today.Month, 1);
            return await svc.GetCapacityAsync(f, to ?? f.AddMonths(2).AddDays(-1), teamId, personId);
        });
        api.MapGet("/load/weekly", async (CapacityService svc, DateOnly? from, DateOnly? to, int? teamId, int? personId) =>
        {
            var f = from ?? PlanningApi.Today;
            return await svc.GetWeeklyLoadAsync(f, to ?? f.AddDays(8 * 7 - 1), teamId, personId);
        });

        api.MapGet("/demands", async (DemandService svc, int? projectId, int? opportunityId) => await svc.ListAsync(projectId, opportunityId));
        api.MapPost("/demands", async (HttpContext http, DemandService svc, DemandBody b) => Results.Created("/api/v1/demands", await svc.AddAsync(ToDemand(b, 0), http.Actor())));
        api.MapPut("/demands/{id:int}", async (HttpContext http, DemandService svc, int id, DemandBody b) => { await svc.UpdateAsync(ToDemand(b, id), http.Actor()); return Results.NoContent(); });
        api.MapDelete("/demands/{id:int}", async (HttpContext http, DemandService svc, int id) => { await svc.DeleteAsync(id, http.Actor()); return Results.NoContent(); });
        api.MapGet("/demands/{id:int}/candidates", async (DemandService svc, int id) => await svc.RankCandidatesAsync(id));

        api.MapGet("/supply-demand", async (PipelineService svc, DateOnly? from, int? months) => await svc.SupplyDemandAsync(from ?? PlanningApi.Today, months ?? 3));
        api.MapGet("/opportunities", async (PipelineService svc) => await svc.ListAsync());
        api.MapPost("/opportunities", async (HttpContext http, PipelineService svc, OpportunityBody b) =>
        {
            var o = await svc.AddAsync(new Opportunity
            {
                Code = b.Code, Name = b.Name, LeadName = b.LeadName, ExpectedValueLakh = b.ExpectedValueLakh, ExpectedHours = b.ExpectedHours, LikelyBookingMonth = b.LikelyBookingMonth,
                DurationMonths = b.DurationMonths, Stage = b.Stage, WinProbability = b.WinProbability, RequiredSkills = b.RequiredSkills,
            }, http.Actor());
            return Results.Created($"/api/v1/opportunities/{o.Id}", new { o.Id });
        });
        api.MapPost("/opportunities/{id:int}/convert", async (HttpContext http, PipelineService svc, int id, ConvertBody b) =>
        {
            var p = await svc.ConvertToProjectAsync(id, b.ProjectNo, http.Actor());
            return Results.Ok(new { p.Id, p.ProjectNo });
        });

        // ---- what-if (spec §15) ----
        api.MapGet("/scenarios", async (ScenarioService svc) => await svc.ListAsync());
        api.MapGet("/scenarios/{id:int}", async (ScenarioService svc, int id) => await svc.GetAsync(id));
        api.MapPost("/scenarios", async (HttpContext http, ScenarioService svc, ScenarioBody b) => await svc.AnalyseAsync(b.Moves, b.Name, http.Actor(), b.Notes, b.Today, b.ScenarioId));
        api.MapPost("/scenarios/{id:int}/decide", async (HttpContext http, ScenarioService svc, int id, DecisionBody b) => await svc.DecideAsync(id, b.Approve, http.Actor(), b.Note));
        api.MapPost("/scenarios/{id:int}/apply", async (HttpContext http, ScenarioService svc, int id, ApplyBody b) => await svc.ApplyAsync(id, http.Actor(), b.Today));
        api.MapGet("/release-analysis", async (ScenarioService svc, int targetProjectId, DateOnly? date) => await svc.ReleaseAnalysisAsync(targetProjectId, date ?? PlanningApi.Today));
        api.MapGet("/push-plan", async (ScenarioService svc, int targetProjectId, DateOnly? date, int? maxMoves) => await svc.PushPlanAsync(targetProjectId, date ?? PlanningApi.Today, maxMoves ?? 10));

        // ---- movements and the change log ----
        api.MapGet("/movements", async (EosDbContext db, int? personId, int? projectId) =>
            await db.Movements.AsNoTracking().Where(m => (personId == null || m.PersonId == personId) && (projectId == null || m.FromProjectId == projectId || m.ToProjectId == projectId))
                .OrderByDescending(m => m.EffectiveDate).ToListAsync());
        api.MapPost("/movements", async (HttpContext http, EosDbContext db, MovementBody b) =>
        {
            if (b.FromProjectId == b.ToProjectId) return Results.BadRequest(new { title = "From and to project must differ." });
            var m = new ResourceMovement
            {
                PersonId = b.PersonId, FromProjectId = b.FromProjectId, ToProjectId = b.ToProjectId, EffectiveDate = b.EffectiveDate, Percent = b.Percent ?? 1m,
                Reason = b.Reason, RequestedBy = http.Actor(), Status = "Requested", CreatedUtc = DateTime.UtcNow,
            };
            db.Movements.Add(m);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/movements/{m.Id}", new { m.Id });
        });

        api.MapGet("/change-log", async (EosDbContext db) => await db.ChangeGapLog.AsNoTracking().OrderByDescending(g => g.RaisedOn).ToListAsync());
        api.MapPost("/change-log", async (HttpContext http, EosDbContext db, LogBody b) =>
        {
            var e = new ChangeGapEntry
            {
                Kind = b.Kind, ProjectId = b.ProjectId, DemandId = b.DemandId, RaisedBy = http.Actor(), RaisedOn = PlanningApi.Today, Description = b.Description, GapType = b.GapType,
                ImpactHours = b.ImpactHours, ImpactDays = b.ImpactDays, Decision = b.Decision, Approver = b.Approver, Status = b.Status ?? "Open",
            };
            db.ChangeGapLog.Add(e);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/change-log/{e.Id}", new { e.Id });
        });
    }

    private static Demand ToDemand(DemandBody b, int id) => new()
    {
        Id = id, ProjectId = b.ProjectId, OpportunityId = b.OpportunityId, SkillId = b.SkillId, MinLevel = b.MinLevel, HoursPerWeek = b.HoursPerWeek,
        StartDate = b.StartDate, EndDate = b.EndDate, Location = b.Location, StatusOverride = b.StatusOverride, Notes = b.Notes,
    };
}
