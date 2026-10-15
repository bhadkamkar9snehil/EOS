using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.Projects;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Projects;

public sealed record RiskRow(int Id, int ProjectId, string ProjectNo, string Kind, string Text, string? Severity, string? Owner, DateOnly? DueOn, string Status, string? Mitigation);

public sealed record MovementRow(int Id, string Person, string FromProject, string ToProject, DateOnly EffectiveDate, decimal Percent, string Status, string? Reason, string RequestedBy, string? ApprovedBy);

public sealed class RegisterService(EosDbContext db)
{
    public async Task<IReadOnlyList<RiskRow>> RisksAsync(int? projectId = null, bool openOnly = false, CancellationToken ct = default) =>
        await (from r in db.RiskIssues.AsNoTracking()
               join p in db.Projects.AsNoTracking() on r.ProjectId equals p.Id
               where (projectId == null || r.ProjectId == projectId) && (!openOnly || r.Status != "Closed")
               orderby r.Status == "Closed", r.DueOn
               select new RiskRow(r.Id, r.ProjectId, p.ProjectNo, r.Kind, r.Text, r.Severity, r.Owner, r.DueOn, r.Status, r.Mitigation)).ToListAsync(ct);

    public async Task<RiskIssue> AddRiskAsync(RiskIssue risk, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(risk.Text)) throw new PlanException("Describe the risk or issue.");
        if (!await db.Projects.AnyAsync(p => p.Id == risk.ProjectId, ct)) throw new PlanException("Pick a project.");
        db.RiskIssues.Add(risk);
        db.AuditEvents.Add(Audit(actor, "risk.add", "risk", risk.Text));
        await db.SaveChangesAsync(ct);
        return risk;
    }

    public async Task SetRiskStatusAsync(int id, string status, string actor, CancellationToken ct = default)
    {
        var r = await db.RiskIssues.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PlanException("Risk not found.");
        r.Status = status;
        db.AuditEvents.Add(Audit(actor, "risk.status", "risk", $"{r.Text} -> {status}"));
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRiskAsync(int id, string actor, CancellationToken ct = default)
    {
        var r = await db.RiskIssues.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PlanException("Risk not found.");
        db.RiskIssues.Remove(r);
        db.AuditEvents.Add(Audit(actor, "risk.delete", "risk", r.Text));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AmcTicket>> TicketsAsync(bool openOnly = false, CancellationToken ct = default) =>
        await db.AmcTickets.AsNoTracking().Where(t => !openOnly || (t.Status != "Resolved" && t.Status != "Closed")).OrderByDescending(t => t.RaisedOn).ToListAsync(ct);

    public async Task<AmcTicket> AddTicketAsync(AmcTicket t, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(t.Issue)) throw new PlanException("Describe the issue.");
        if (t.ProjectNo is { Length: > 0 } no && await db.Projects.FirstOrDefaultAsync(p => p.ProjectNo == no, ct) is { } p) t.ProjectId = p.Id;
        t.RaisedOn ??= DateOnly.FromDateTime(DateTime.Today);
        db.AmcTickets.Add(t);
        db.AuditEvents.Add(Audit(actor, "ticket.add", "ticket", t.Issue));
        await db.SaveChangesAsync(ct);
        return t;
    }

    public async Task SetTicketAsync(int id, string status, string? engineers, string actor, DateOnly today, CancellationToken ct = default)
    {
        var t = await db.AmcTickets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PlanException("Ticket not found.");
        t.Status = status;
        if (engineers is not null) t.AssignedEngineers = engineers;
        if (status == "Assigned" && t.AssignedOn is null) { t.AssignedOn = today; t.AssignedBy = actor; }
        if (status is "Resolved" or "Closed") t.ResolvedOn ??= today;
        db.AuditEvents.Add(Audit(actor, "ticket.status", "ticket", $"{t.Issue} -> {status}"));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChangeGapEntry>> ChangesAsync(CancellationToken ct = default) =>
        await db.ChangeGapLog.AsNoTracking().OrderByDescending(g => g.RaisedOn).ThenByDescending(g => g.Id).ToListAsync(ct);

    public async Task<ChangeGapEntry> AddChangeAsync(ChangeGapEntry e, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(e.Description)) throw new PlanException("Describe the change or gap.");
        e.RaisedBy = actor; e.RaisedOn = DateOnly.FromDateTime(DateTime.Today);
        db.ChangeGapLog.Add(e);
        db.AuditEvents.Add(Audit(actor, "change.add", "change", e.Description));
        await db.SaveChangesAsync(ct);
        return e;
    }

    public async Task DecideChangeAsync(int id, string decision, string actor, CancellationToken ct = default)
    {
        var e = await db.ChangeGapLog.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PlanException("Entry not found.");
        e.Decision = decision; e.Approver = actor; e.DecisionOn = DateOnly.FromDateTime(DateTime.Today); e.Status = "Closed";
        db.AuditEvents.Add(Audit(actor, "change.decide", "change", $"{e.Description}: {decision}"));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MovementRow>> MovementsAsync(CancellationToken ct = default) =>
        await (from m in db.Movements.AsNoTracking()
               join p in db.People.AsNoTracking() on m.PersonId equals p.Id
               join f in db.Projects.AsNoTracking() on m.FromProjectId equals f.Id
               join t in db.Projects.AsNoTracking() on m.ToProjectId equals t.Id
               orderby m.EffectiveDate descending
               select new MovementRow(m.Id, p.FullName, f.ProjectNo, t.ProjectNo, m.EffectiveDate, m.Percent, m.Status, m.Reason, m.RequestedBy, m.ApprovedBy)).ToListAsync(ct);

    private static AuditEvent Audit(string actor, string action, string kind, string detail) =>
        new() { AtUtc = DateTime.UtcNow, Actor = actor, Action = action, SubjectKind = kind, Detail = detail };
}
