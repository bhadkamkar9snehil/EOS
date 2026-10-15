using EOS.Domain.Capacity;
using EOS.Domain.People;
using EOS.Domain.Skills;
using EOS.Domain.Training;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Skills;
using EOS.Infrastructure.Training;
using Microsoft.EntityFrameworkCore;

namespace EOS.Server.Api;

public sealed record PersonBody(string Code, string FullName, string? Email, string? RoleTitle, string? BaseLocation, int? TeamId, decimal? AvailabilityFraction);
public sealed record TeamBody(string Name);
public sealed record LeaderBody(int? PersonId);
public sealed record LeaveBody(int PersonId, DateOnly FromDate, DateOnly ToDate, string? Kind, string? Note);
public sealed record AllocationBody(int PersonId, int ProjectId, DateOnly Month, decimal Hours, string? Status, string? Role, int? DemandId);
public sealed record RatingBody(int Rating, string? Reason);
public sealed record EvidenceBody(string Note);
public sealed record StateBody(KnowledgeState State);
public sealed record DoubtBody(string Module, string SubModule, string Text, int? RaisedByPersonId, DateOnly? RaisedOn);
public sealed record AssignDoubtBody(int MentorPersonId, DateOnly? On);
public sealed record ResolveDoubtBody(DoubtResult Result, string? Remarks, DateOnly? On);
public sealed record FieldValueBody(string? Value);

public static class PeopleApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/people", async (PeopleService svc, bool? includeInactive, int? teamId) => await svc.ListAsync(includeInactive ?? false, teamId));
        api.MapPost("/people", async (HttpContext http, PeopleService svc, PersonBody b) =>
        {
            var p = await svc.CreateAsync(new Person { Code = b.Code, FullName = b.FullName, Email = b.Email, RoleTitle = b.RoleTitle, BaseLocation = b.BaseLocation, TeamId = b.TeamId, AvailabilityFraction = b.AvailabilityFraction ?? 1m }, http.Actor());
            return Results.Created($"/api/v1/people/{p.Id}", new { p.Id });
        });
        api.MapGet("/people/{id:int}", async (PeopleService svc, int id, DateOnly? asOf) => await svc.GetProfileAsync(id, asOf ?? PlanningApi.Today));
        api.MapPatch("/people/{id:int}", async (HttpContext http, PeopleService svc, int id, PersonPatch patch) => { await svc.UpdateAsync(id, patch, http.Actor()); return Results.NoContent(); });

        api.MapGet("/teams", async (PeopleService svc) => (await svc.ListTeamsAsync()).Select(t => new { t.Id, t.Name, t.LeaderPersonId, t.LeaderName, t.Members }));
        api.MapPost("/teams", async (HttpContext http, PeopleService svc, TeamBody b) => { var t = await svc.CreateTeamAsync(b.Name, http.Actor()); return Results.Created($"/api/v1/teams/{t.Id}", new { t.Id, t.Name }); });
        api.MapPut("/teams/{id:int}/leader", async (HttpContext http, PeopleService svc, int id, LeaderBody b) => { await svc.SetTeamLeaderAsync(id, b.PersonId, http.Actor()); return Results.NoContent(); });

        api.MapGet("/leave", async (EosDbContext db, int? personId) => await db.Leaves.AsNoTracking().Where(l => personId == null || l.PersonId == personId).OrderBy(l => l.FromDate).ToListAsync());
        api.MapPost("/leave", async (HttpContext http, PeopleService svc, LeaveBody b) =>
        {
            var l = await svc.AddLeaveAsync(new Leave { PersonId = b.PersonId, FromDate = b.FromDate, ToDate = b.ToDate, Kind = b.Kind ?? "Leave", Note = b.Note }, http.Actor());
            return Results.Created($"/api/v1/leave/{l.Id}", new { l.Id });
        });
        api.MapDelete("/leave/{id:int}", async (HttpContext http, PeopleService svc, int id) => { await svc.RemoveLeaveAsync(id, http.Actor()); return Results.NoContent(); });

        api.MapGet("/allocations", async (EosDbContext db, int? personId, int? projectId) =>
            await db.Allocations.AsNoTracking().Where(a => (personId == null || a.PersonId == personId) && (projectId == null || a.ProjectId == projectId)).OrderBy(a => a.Month).ToListAsync());
        api.MapPut("/allocations", async (HttpContext http, PeopleService svc, AllocationBody b) =>
            await svc.SetAllocationAsync(new Allocation { PersonId = b.PersonId, ProjectId = b.ProjectId, Month = b.Month, Hours = b.Hours, Status = b.Status ?? "Confirmed", Role = b.Role ?? "Engineer", DemandId = b.DemandId }, http.Actor()));
        api.MapDelete("/allocations/{id:int}", async (HttpContext http, PeopleService svc, int id) => { await svc.RemoveAllocationAsync(id, http.Actor()); return Results.NoContent(); });

        api.MapGet("/skills", async (EosDbContext db) => await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync());
        api.MapGet("/skill-matrix", async (EosDbContext db) =>
        {
            var skills = await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync();
            var ratings = await db.PersonSkills.AsNoTracking().ToListAsync();
            var people = await db.People.AsNoTracking().Include(p => p.Team).Where(p => db.PersonSkills.Any(r => r.PersonId == p.Id)).OrderBy(p => p.FullName).ToListAsync();
            return new
            {
                skills,
                people = people.Select(p => new
                {
                    p.Id, p.FullName, Team = p.Team?.Name,
                    ratings = skills.ToDictionary(s => s.Id.ToString(), s => ratings.FirstOrDefault(r => r.PersonId == p.Id && r.SkillId == s.Id)?.Rating),
                }),
            };
        });
        api.MapPut("/people/{personId:int}/skills/{skillId:int}", async (HttpContext http, SkillRatingService svc, int personId, int skillId, RatingBody b) =>
        {
            await svc.SetRatingAsync(personId, skillId, b.Rating, http.Actor(), b.Reason);
            return Results.NoContent();
        });
        api.MapPost("/people/{personId:int}/skills/{skillId:int}/evidence", async (HttpContext http, SkillRatingService svc, int personId, int skillId, EvidenceBody b) =>
        {
            await svc.AddEvidenceNoteAsync(personId, skillId, b.Note, http.Actor());
            return Results.NoContent();
        });
        api.MapGet("/people/{personId:int}/skills/{skillId:int}/history", async (EosDbContext db, int personId, int skillId) =>
            await db.SkillTransactions.AsNoTracking().Where(t => t.PersonId == personId && t.SkillId == skillId).OrderByDescending(t => t.AtUtc).ToListAsync());

        api.MapGet("/training/summary", async (TrainingService svc, DateOnly? asOf) => await svc.SummaryAsync(asOf ?? PlanningApi.Today));
        api.MapGet("/training/coverage", async (TrainingService svc, int? take) => await svc.CoverageAsync(take ?? 40));
        api.MapGet("/modules", async (EosDbContext db) => await db.Modules.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync());
        api.MapPut("/people/{personId:int}/modules/{moduleId:int}", async (HttpContext http, TrainingService svc, int personId, int moduleId, StateBody b) =>
        {
            await svc.SetStateAsync(personId, moduleId, b.State, http.Actor());
            return Results.NoContent();
        });
        api.MapGet("/doubts", async (TrainingService svc, bool? openOnly) => await svc.DoubtsAsync(openOnly ?? false));
        api.MapPost("/doubts", async (HttpContext http, TrainingService svc, DoubtBody b) =>
        {
            var d = await svc.RaiseAsync(b.Module, b.SubModule, b.Text, b.RaisedByPersonId, b.RaisedOn ?? PlanningApi.Today, http.Actor());
            return Results.Created($"/api/v1/doubts/{d.Id}", new { d.Id });
        });
        api.MapPost("/doubts/{id:int}/assign", async (HttpContext http, TrainingService svc, int id, AssignDoubtBody b) => { await svc.AssignAsync(id, b.MentorPersonId, b.On ?? PlanningApi.Today, http.Actor()); return Results.NoContent(); });
        api.MapPost("/doubts/{id:int}/resolve", async (HttpContext http, TrainingService svc, int id, ResolveDoubtBody b) => { await svc.ResolveAsync(id, b.Result, b.On ?? PlanningApi.Today, b.Remarks, http.Actor()); return Results.NoContent(); });

        api.MapGet("/custom-fields", async (AdminService svc, string? entityKind) => await svc.FieldsAsync(entityKind));
        api.MapPost("/custom-fields", async (HttpContext http, AdminService svc, EOS.Domain.Configuration.CustomFieldDefinition f) => await svc.AddFieldAsync(f, http.Actor()));
        api.MapGet("/custom-fields/{kind}/{id:int}", async (AdminService svc, string kind, int id) => await svc.FieldValuesAsync(kind, id));
        api.MapPut("/custom-fields/{kind}/{id:int}/{key}", async (HttpContext http, AdminService svc, string kind, int id, string key, FieldValueBody b) =>
        {
            await svc.SetFieldValueAsync(kind, id, key, b.Value, http.Actor());
            return Results.NoContent();
        });
    }
}
