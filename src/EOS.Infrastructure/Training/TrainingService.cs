using EOS.Domain.Audit;
using EOS.Domain.Settings;
using EOS.Domain.Training;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Training;

public sealed class TrainingException(string message) : Exception(message);

public sealed record KnowledgeRow(int PersonId, string Name, int Known, int DontKnow, int NeedToDiscuss, int TotalModules, decimal KnowFraction, string Level);

public sealed record ModuleCoverage(int ModuleId, string Product, string Module, string SubModule, int KnownBy, int People);

/// <summary>Knowledge summary with configurable bands, module coverage, doubts lifecycle (spec §10).</summary>
public sealed class TrainingService(EosDbContext db)
{
    public async Task<IReadOnlyList<KnowledgeRow>> SummaryAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
        var chain = SettingsResolver.Chain();
        var good = resolver.Get<decimal>(SettingKeys.KnowledgeGoodAtLeast, chain, asOf);
        var medium = resolver.Get<decimal>(SettingKeys.KnowledgeMediumAtLeast, chain, asOf);
        var weak = resolver.Get<decimal>(SettingKeys.KnowledgeWeakAtLeast, chain, asOf);
        var total = await db.Modules.CountAsync(ct);
        var states = await db.PersonModules.AsNoTracking().GroupBy(x => new { x.PersonId, x.State }).Select(g => new { g.Key.PersonId, g.Key.State, N = g.Count() }).ToListAsync(ct);
        var people = await db.People.AsNoTracking().Where(p => db.PersonModules.Any(m => m.PersonId == p.Id)).ToListAsync(ct);

        return people.Select(p =>
        {
            int N(KnowledgeState s) => states.FirstOrDefault(x => x.PersonId == p.Id && x.State == s)?.N ?? 0;
            var known = N(KnowledgeState.Know);
            var f = total == 0 ? 0m : (decimal)known / total;
            return new KnowledgeRow(p.Id, p.FullName, known, N(KnowledgeState.DontKnow), N(KnowledgeState.NeedToDiscuss), total, Math.Round(f, 4),
                f >= good ? "Good" : f >= medium ? "Medium" : f >= weak ? "Weak" : "Critical");
        }).OrderByDescending(r => r.KnowFraction).ToList();
    }

    /// <summary>Modules known by the fewest people first: where a single absence creates a training or delivery gap.</summary>
    public async Task<IReadOnlyList<ModuleCoverage>> CoverageAsync(int take = 40, CancellationToken ct = default)
    {
        var people = await db.PersonModules.Select(m => m.PersonId).Distinct().CountAsync(ct);
        var known = await db.PersonModules.Where(m => m.State == KnowledgeState.Know).GroupBy(m => m.ModuleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.Modules.AsNoTracking().ToListAsync(ct))
            .Select(m => new ModuleCoverage(m.Id, m.Product, m.Name, m.SubModule, known.GetValueOrDefault(m.Id), people))
            .OrderBy(c => c.KnownBy).ThenBy(c => c.Product).ThenBy(c => c.Module).Take(take).ToList();
    }

    public async Task SetStateAsync(int personId, int moduleId, KnowledgeState state, string actor, CancellationToken ct = default)
    {
        if (!await db.People.AnyAsync(p => p.Id == personId, ct)) throw new TrainingException($"Person {personId} not found.");
        if (!await db.Modules.AnyAsync(m => m.Id == moduleId, ct)) throw new TrainingException($"Module {moduleId} not found.");
        var row = await db.PersonModules.SingleOrDefaultAsync(x => x.PersonId == personId && x.ModuleId == moduleId, ct);
        var old = row?.State;
        if (row is null) db.PersonModules.Add(new PersonModule { PersonId = personId, ModuleId = moduleId, State = state }); else row.State = state;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "training.state", SubjectKind = "person", SubjectId = personId.ToString(), Detail = $"module {moduleId}: {old?.ToString() ?? "-"} -> {state}" });
        await db.SaveChangesAsync(ct);
    }

    // ---- doubts -------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<Doubt>> DoubtsAsync(bool openOnly = false, CancellationToken ct = default) =>
        await db.Doubts.AsNoTracking().Where(d => !openOnly || d.Result != DoubtResult.Solved).OrderByDescending(d => d.RaisedOn).ThenBy(d => d.Id).ToListAsync(ct);

    public async Task<Doubt> RaiseAsync(string module, string subModule, string text, int? raisedByPersonId, DateOnly on, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new TrainingException("A doubt needs a description.");
        var name = raisedByPersonId is { } id ? (await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct))?.FullName ?? throw new TrainingException($"Person {id} not found.") : null;
        var next = (await db.Doubts.Select(d => (int?)d.SourceRow).MaxAsync(ct) ?? 0) + 1;
        var d = new Doubt { Module = module, SubModule = subModule, Text = text, RaisedOn = on, RaisedByPersonId = raisedByPersonId, RaisedByName = name, Result = DoubtResult.Open, SourceRow = next };
        db.Doubts.Add(d);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "doubt.raise", SubjectKind = "doubt", SubjectId = $"{module}/{subModule}" });
        await db.SaveChangesAsync(ct);
        return d;
    }

    public async Task AssignAsync(int doubtId, int mentorPersonId, DateOnly on, string actor, CancellationToken ct = default)
    {
        var d = await db.Doubts.SingleOrDefaultAsync(x => x.Id == doubtId, ct) ?? throw new TrainingException($"Doubt {doubtId} not found.");
        var mentor = await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == mentorPersonId, ct) ?? throw new TrainingException($"Person {mentorPersonId} not found.");
        d.AssignedToPersonId = mentor.Id; d.AssignedToName = mentor.FullName; d.AssignedOn = on;
        if (d.Result == DoubtResult.Open) d.Result = DoubtResult.Assigned;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "doubt.assign", SubjectKind = "doubt", SubjectId = doubtId.ToString(), Detail = mentor.FullName });
        await db.SaveChangesAsync(ct);
    }

    public async Task ResolveAsync(int doubtId, DoubtResult result, DateOnly on, string? remarks, string actor, CancellationToken ct = default)
    {
        var d = await db.Doubts.SingleOrDefaultAsync(x => x.Id == doubtId, ct) ?? throw new TrainingException($"Doubt {doubtId} not found.");
        if (result is DoubtResult.Open) throw new TrainingException("Use a result of Assigned, Hold or Solved.");
        d.Result = result; d.Remarks = remarks ?? d.Remarks;
        d.SolvedOn = result == DoubtResult.Solved ? on : null;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "doubt.resolve", SubjectKind = "doubt", SubjectId = doubtId.ToString(), Detail = result.ToString() });
        await db.SaveChangesAsync(ct);
    }
}
