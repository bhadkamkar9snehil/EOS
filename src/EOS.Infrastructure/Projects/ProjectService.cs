using EOS.Domain.Audit;
using EOS.Domain.Configuration;
using EOS.Domain.Projects;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Projects;

public sealed record ProjectSummary(
    int Id, string ProjectNo, string Name, string? Customer, string? ProductLine, string? ErpStatus, string? TrackerStatus,
    string? ProjectManager, string? TechnicalManager, DateOnly? PlannedEnd, int TaskCount, bool HasPlan);

/// <summary>Project master maintenance. Statuses and types are validated against the editable vocabularies (spec §8.7).</summary>
public sealed class ProjectService(EosDbContext db)
{
    public async Task<Project> CreateAsync(Project project, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectNo)) throw new PlanException("A project needs a project number.");
        if (string.IsNullOrWhiteSpace(project.Name)) throw new PlanException("A project needs a name.");
        if (await db.Projects.AnyAsync(p => p.ProjectNo == project.ProjectNo, ct)) throw new PlanException($"Project {project.ProjectNo} already exists.");
        await ValidateAsync(project, ct);
        db.Projects.Add(project);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "project.create", SubjectKind = "project", SubjectId = project.ProjectNo, Detail = project.Name });
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task UpdateAsync(Project changes, string actor, CancellationToken ct = default)
    {
        var current = await db.Projects.SingleOrDefaultAsync(p => p.Id == changes.Id, ct) ?? throw new PlanException($"Project {changes.Id} not found.");
        await ValidateAsync(changes, ct);
        db.Entry(current).CurrentValues.SetValues(changes);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "project.update", SubjectKind = "project", SubjectId = current.ProjectNo });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(string? search = null, bool withPlanOnly = false, CancellationToken ct = default)
    {
        var counts = await db.Tasks.GroupBy(t => t.ProjectId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var query = db.Projects.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.ProjectNo.ToLower().Contains(s) || p.Name.ToLower().Contains(s) || (p.Customer ?? "").ToLower().Contains(s));
        }
        var rows = await query.OrderBy(p => p.ProjectNo).ToListAsync(ct);
        return rows.Select(p => new ProjectSummary(p.Id, p.ProjectNo, p.Name, p.Customer, p.ProductLine, p.ErpStatus, p.TrackerStatus,
                p.ProjectManager, p.TechnicalManager, p.PlannedEnd, counts.GetValueOrDefault(p.Id), counts.ContainsKey(p.Id)))
            .Where(p => !withPlanOnly || p.HasPlan).ToList();
    }

    private async Task ValidateAsync(Project p, CancellationToken ct)
    {
        await Check(Vocabularies.TrackerStatus, p.TrackerStatus, ct);
        await Check(Vocabularies.ErpStatus, p.ErpStatus, ct);
        await Check(Vocabularies.ProjectType, p.ProjectType, ct);
        await Check(Vocabularies.Health, p.HealthOverride, ct);
    }

    private async Task Check(string kind, string? value, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(value) && !await ConfigurationService.IsValidAsync(db, kind, value, ct))
            throw new PlanException($"'{value}' is not an allowed value for {kind}. Add it under Settings > Vocabularies first.");
    }
}
