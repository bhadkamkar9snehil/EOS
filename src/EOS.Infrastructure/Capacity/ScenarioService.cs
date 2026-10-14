using System.Text.Json;
using EOS.Domain.Audit;
using EOS.Domain.Capacity;
using EOS.Domain.Planning;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Capacity;

/// <summary>
/// What-if analysis on real plans (spec §15): moves of engineers between projects, release analysis for a major push,
/// a greedy push plan, and the approve / apply workflow that updates assignments, the movement ledger, the change log and
/// baselines. Analysis never mutates plans; only <see cref="ApplyAsync"/> does, and only for an approved scenario.
/// </summary>
public sealed class ScenarioService(EosDbContext db)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // ---- analyse -------------------------------------------------------------------------------------------

    public async Task<ScenarioView> AnalyseAsync(
        IReadOnlyList<MoveInput> moves, string name, string actor, string? notes = null, DateOnly? today = null,
        int? scenarioId = null, CancellationToken ct = default)
    {
        if (moves.Count == 0) throw new ScenarioException("A scenario needs at least one move.");
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);

        Scenario scenario;
        if (scenarioId is { } id)
        {
            scenario = await db.Scenarios.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new ScenarioException($"Scenario {id} not found.");
            if (scenario.Status is "Applied") throw new ScenarioException("An applied scenario cannot be re-analysed.");
        }
        else
        {
            scenario = new Scenario { CreatedBy = actor, CreatedUtc = DateTime.UtcNow };
            db.Scenarios.Add(scenario);
        }
        scenario.Name = string.IsNullOrWhiteSpace(name) ? "Untitled scenario" : name.Trim();
        scenario.Notes = notes;
        scenario.MovesJson = JsonSerializer.Serialize(moves, Json);

        var result = await ComputeAsync(moves, day, ct);
        scenario.ResultJson = JsonSerializer.Serialize(result, Json);
        scenario.Status = "Analysed";
        scenario.DecisionBy = null; scenario.DecisionUtc = null; scenario.DecisionNote = null;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "scenario.analyse", SubjectKind = "scenario", SubjectId = scenario.Name });
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(scenario, ct);
    }

    public async Task<ScenarioView> GetAsync(int id, CancellationToken ct = default) =>
        await ToViewAsync(await db.Scenarios.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new ScenarioException($"Scenario {id} not found."), ct);

    public async Task<IReadOnlyList<ScenarioView>> ListAsync(CancellationToken ct = default)
    {
        var all = await db.Scenarios.AsNoTracking().OrderByDescending(s => s.CreatedUtc).ToListAsync(ct);
        var views = new List<ScenarioView>();
        foreach (var s in all) views.Add(await ToViewAsync(s, ct));
        return views;
    }

    // ---- decide and apply ----------------------------------------------------------------------------------

    public async Task<ScenarioView> DecideAsync(int id, bool approve, string by, string? note, CancellationToken ct = default)
    {
        var s = await db.Scenarios.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScenarioException($"Scenario {id} not found.");
        if (s.Status != "Analysed") throw new ScenarioException($"Only an analysed scenario can be decided (status is {s.Status}).");
        if (string.IsNullOrWhiteSpace(by)) throw new ScenarioException("A decision needs a named approver.");
        s.Status = approve ? "Approved" : "Rejected";
        s.DecisionBy = by; s.DecisionUtc = DateTime.UtcNow; s.DecisionNote = note;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = by, Action = approve ? "scenario.approve" : "scenario.reject", SubjectKind = "scenario", SubjectId = s.Name, Detail = note });
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(s, ct);
    }

    /// <summary>
    /// Applies an APPROVED scenario: rewrites the movers' assignments, records the movement ledger and a change-log decision,
    /// recomputes the affected plans and promotes each to a new baseline (spec §15.9).
    /// </summary>
    public async Task<ScenarioView> ApplyAsync(int id, string by, DateOnly? today = null, CancellationToken ct = default)
    {
        var s = await db.Scenarios.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScenarioException($"Scenario {id} not found.");
        if (s.Status != "Approved") throw new ScenarioException($"Only an approved scenario can be applied (status is {s.Status}).");
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var moves = JsonSerializer.Deserialize<List<MoveInput>>(s.MovesJson, Json)!;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (built, outcome, cal, options) = await RunEngineAsync(moves, day, ct);
        var involved = moves.Select(m => m.PersonId.ToString()).ToHashSet();

        foreach (var plan in outcome.ScenarioPlans)
        {
            var projectId = int.Parse(plan.Id);
            foreach (var scheduleTask in plan.Tasks)
            {
                var taskId = int.Parse(scheduleTask.Id);
                var existing = await db.TaskAssignments.Where(a => a.TaskId == taskId && a.PersonId != null).ToListAsync(ct);
                db.TaskAssignments.RemoveRange(existing.Where(a => involved.Contains(a.PersonId!.Value.ToString())));
                foreach (var c in scheduleTask.Crew.Where(c => involved.Contains(c.PersonId)))
                    db.TaskAssignments.Add(new TaskAssignment { TaskId = taskId, PersonId = int.Parse(c.PersonId), Units = c.Units, FromDate = c.From, ToDate = c.To });
            }
            _ = projectId;
        }
        await db.SaveChangesAsync(ct);

        var plans = new PlanService(db);
        var maxSlip = 0;
        var hours = 0m;
        foreach (var impact in outcome.Projects.Where(p => p.SlipWorkingDays != 0))
            maxSlip = Math.Max(maxSlip, Math.Abs(impact.SlipWorkingDays));
        foreach (var projectId in moves.SelectMany(m => new[] { m.FromProjectId, m.ToProjectId }).Distinct())
        {
            await plans.RecomputeAsync(projectId, day, ct);
            await plans.CreateBaselineAsync(projectId, $"After scenario: {s.Name}", by, ct);
        }
        foreach (var m in moves)
        {
            db.Movements.Add(new ResourceMovement
            {
                PersonId = m.PersonId, FromProjectId = m.FromProjectId, ToProjectId = m.ToProjectId, EffectiveDate = m.Date, Percent = m.Percent,
                ScenarioId = s.Id, Reason = s.Name, RequestedBy = s.CreatedBy, ApprovedBy = s.DecisionBy, ApprovedOn = DateOnly.FromDateTime(s.DecisionUtc ?? DateTime.UtcNow),
                Status = m.Date <= day ? "Executed" : "Approved", CreatedUtc = DateTime.UtcNow,
            });
            hours += 0m;
        }
        db.ChangeGapLog.Add(new ChangeGapEntry
        {
            Kind = "Decision", RaisedBy = s.CreatedBy, RaisedOn = DateOnly.FromDateTime(s.CreatedUtc), Description = $"Scenario applied: {s.Name}",
            ImpactDays = maxSlip, ImpactHours = hours, Decision = s.DecisionNote, Approver = s.DecisionBy, DecisionOn = day, Status = "Closed",
        });
        s.Status = "Applied";
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = by, Action = "scenario.apply", SubjectKind = "scenario", SubjectId = s.Name });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _ = built; _ = cal; _ = options;
        return await ToViewAsync(s, ct);
    }

    // ---- major push -------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ReleaseRowView>> ReleaseAnalysisAsync(
        int targetProjectId, DateOnly analysisDate, DateOnly? today = null, CancellationToken ct = default)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var ids = await ProjectsWithAssignmentsAsync(ct);
        ids.Add(targetProjectId);
        var built = await LoadPlansAsync(ids, day, ct);
        var cal = await ConfigurationService.LoadCalendarAsync(db, null, ct);
        var options = built[targetProjectId].Options;
        var plans = ids.OrderBy(i => i).Select(i => built[i].Plan).ToList();

        var rows = WhatIf.ReleaseAnalysis(plans, targetProjectId.ToString(), analysisDate, cal, options);
        var names = await PersonNamesAsync(ct);
        var targetFinish = Scheduler.Compute(built[targetProjectId].Plan.Tasks, cal, built[targetProjectId].Plan.Start, options).Finish;
        var result = new List<ReleaseRowView>();
        foreach (var r in rows)
        {
            var personId = int.Parse(r.PersonId);
            var (eligible, note) = await EligibilityAsync(personId, targetProjectId, ct);
            var projectId = int.Parse(r.ProjectId);
            var tooLate = r.EarliestNoDelayMove is { } e && e > targetFinish;
            result.Add(new ReleaseRowView(personId, names.GetValueOrDefault(personId, "?"), projectId, built[projectId].Project.ProjectNo,
                r.LastDayOnCurrentWork, r.EarliestNoDelayMove, r.CostIfMovedOnAnalysisDate, r.GainToTargetIfMovedOnAnalysisDate, eligible, note, tooLate));
        }
        return result.OrderBy(r => r.EarliestNoDelayMove ?? DateOnly.MaxValue).ThenBy(r => r.PersonName).ToList();
    }

    /// <summary>
    /// Greedy push plan: take the cheapest eligible engineers first (earliest no-delay move), recompute after each (gains are not
    /// additive) and stop when the target stops improving or another project would slip. Also yields the finish-vs-engineers curve.
    /// </summary>
    public async Task<PushPlanView> PushPlanAsync(
        int targetProjectId, DateOnly analysisDate, int maxMoves = 10, DateOnly? today = null, CancellationToken ct = default)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);
        var rows = (await ReleaseAnalysisAsync(targetProjectId, analysisDate, day, ct))
            .Where(r => r.Eligible && r.EarliestNoDelayMove is not null && !r.TooLateToHelp && r.GainToTargetIfMovedOnAnalysisDate < 0)
            .OrderBy(r => r.EarliestNoDelayMove).ThenBy(r => r.PersonName).ToList();

        var ids = await ProjectsWithAssignmentsAsync(ct);
        ids.Add(targetProjectId);
        var built = await LoadPlansAsync(ids, day, ct);
        var cal = await ConfigurationService.LoadCalendarAsync(db, null, ct);
        var options = built[targetProjectId].Options;
        var plans = ids.OrderBy(i => i).Select(i => built[i].Plan).ToList();

        var baselineFinish = Scheduler.Compute(built[targetProjectId].Plan.Tasks, cal, built[targetProjectId].Plan.Start, options).Finish;
        var accepted = new List<MoveInput>();
        var steps = new List<PushStep>();
        var bestSlip = 0;
        var names = await PersonNamesAsync(ct);

        foreach (var row in rows)
        {
            if (accepted.Count >= maxMoves) break;
            var move = new MoveInput(row.PersonId, row.ProjectId, targetProjectId, row.EarliestNoDelayMove!.Value);
            var trial = accepted.Append(move).ToList();
            var outcome = WhatIf.Run(plans, trial.Select(Map).ToList(), cal, options);
            var target = outcome.Projects.Single(p => p.ProjectId == targetProjectId.ToString());
            var othersSlip = outcome.Projects.Where(p => p.ProjectId != targetProjectId.ToString()).Any(p => p.SlipWorkingDays > 0);
            if (target.SlipWorkingDays >= bestSlip || othersSlip) continue;          // no improvement, or it hurts someone else

            accepted = trial;
            bestSlip = target.SlipWorkingDays;
            steps.Add(new PushStep(steps.Count + 1, names.GetValueOrDefault(row.PersonId, "?"), row.ProjectNo, move.Date, target.ScenarioFinish, target.SlipWorkingDays));
        }
        return new PushPlanView(targetProjectId, built[targetProjectId].Project.ProjectNo, baselineFinish, steps, accepted);
    }

    // ---- internals --------------------------------------------------------------------------------------------

    private static MoveRequest Map(MoveInput m) => new(m.PersonId.ToString(), m.FromProjectId.ToString(), m.ToProjectId.ToString(), m.Date, m.Percent);

    private async Task<(Dictionary<int, BuiltPlan> Built, ScenarioOutcome Outcome, WorkCalendar Cal, SchedulingOptions Options)> RunEngineAsync(
        IReadOnlyList<MoveInput> moves, DateOnly day, CancellationToken ct)
    {
        var ids = moves.SelectMany(m => new[] { m.FromProjectId, m.ToProjectId }).Distinct().ToList();
        var built = await LoadPlansAsync(ids, day, ct);
        var cal = await ConfigurationService.LoadCalendarAsync(db, null, ct);
        var options = built[ids[0]].Options;
        var plans = ids.OrderBy(i => i).Select(i => built[i].Plan).ToList();
        try { return (built, WhatIf.Run(plans, moves.Select(Map).ToList(), cal, options), cal, options); }
        catch (SchedulingException ex) { throw new ScenarioException(ex.Message); }
    }

    private async Task<Dictionary<int, BuiltPlan>> LoadPlansAsync(IEnumerable<int> projectIds, DateOnly day, CancellationToken ct)
    {
        var result = new Dictionary<int, BuiltPlan>();
        foreach (var id in projectIds.Distinct())
        {
            try { result[id] = await PlanScheduleBuilder.BuildAsync(db, id, day, ct); }
            catch (PlanException ex) { throw new ScenarioException(ex.Message); }
            if (result[id].Plan.Tasks.Count == 0) throw new ScenarioException($"Project {result[id].Project.ProjectNo} has no plan to analyse.");
        }
        return result;
    }

    private async Task<HashSet<int>> ProjectsWithAssignmentsAsync(CancellationToken ct) =>
        (await db.TaskAssignments.Where(a => a.PersonId != null)
            .Join(db.Tasks, a => a.TaskId, t => t.Id, (a, t) => t.ProjectId).Distinct().ToListAsync(ct)).ToHashSet();

    private async Task<Dictionary<int, string>> PersonNamesAsync(CancellationToken ct) =>
        await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.FullName, ct);

    private async Task<ScenarioStored> ComputeAsync(IReadOnlyList<MoveInput> moves, DateOnly day, CancellationToken ct)
    {
        var (built, outcome, cal, _) = await RunEngineAsync(moves, day, ct);
        var warnings = new List<string>();
        var names = await PersonNamesAsync(ct);

        foreach (var m in moves)
        {
            var (eligible, note) = await EligibilityAsync(m.PersonId, m.ToProjectId, ct);
            if (!eligible) warnings.Add($"{names.GetValueOrDefault(m.PersonId, "?")}: {note}");
            var hasWork = built[m.FromProjectId].Plan.Tasks.Any(t => t.Crew.Any(c => c.PersonId == m.PersonId.ToString()));
            if (!hasWork) warnings.Add($"{names.GetValueOrDefault(m.PersonId, "?")} has no assignments in {built[m.FromProjectId].Project.ProjectNo}; nothing leaves that project.");
            if (m.Date < day) warnings.Add($"The move of {names.GetValueOrDefault(m.PersonId, "?")} is dated in the past ({m.Date:dd MMM yyyy}).");
        }
        foreach (var b in built.Values.Where(b => b.Plan.Tasks.Any(t => t.Crew.Any(c => c.PersonId.StartsWith("unassigned-", StringComparison.Ordinal)))))
            warnings.Add($"{b.Project.ProjectNo}: tasks without named engineers use an assumed crew; results are indicative.");
        foreach (var b in built.Values.SelectMany(b => b.Warnings.Select(w => $"{b.Project.ProjectNo}: {w}")).Where(w => w.Contains("estimated", StringComparison.OrdinalIgnoreCase)).Take(1))
            warnings.Add(b);

        var projects = new List<ProjectImpactView>();
        foreach (var p in outcome.Projects)
        {
            var id = int.Parse(p.ProjectId);
            var taskNames = built[id].Tasks.ToDictionary(t => t.Id.ToString(), t => t.Name);
            var slips = p.Tasks.Where(t => t.SlipWorkingDays != 0).OrderByDescending(t => Math.Abs(t.SlipWorkingDays)).Take(8)
                .Select(t => new TaskSlip(taskNames.GetValueOrDefault(t.TaskId, t.TaskId), t.BaselineFinish, t.ScenarioFinish, t.SlipWorkingDays)).ToList();
            projects.Add(new ProjectImpactView(id, built[id].Project.ProjectNo, built[id].Project.Name, p.BaselineFinish, p.ScenarioFinish, p.SlipWorkingDays, slips));
        }

        var verdict = new List<string>();
        foreach (var m in moves)
        {
            var who = names.GetValueOrDefault(m.PersonId, "?");
            var from = built[m.FromProjectId].Project.ProjectNo;
            var to = built[m.ToProjectId].Project.ProjectNo;
            verdict.Add($"{who} moves from {from} to {to} on {m.Date:dd MMM yyyy}" + (m.Percent < 1m ? $" ({m.Percent:P0} of their time)" : "") + ".");
        }
        foreach (var p in projects)
        {
            if (p.SlipDays > 0) verdict.Add($"{p.ProjectNo} finishes {p.SlipDays} working day(s) later ({p.BaselineFinish:dd MMM} to {p.ScenarioFinish:dd MMM yyyy}).");
            else if (p.SlipDays < 0) verdict.Add($"{p.ProjectNo} finishes {-p.SlipDays} working day(s) earlier ({p.BaselineFinish:dd MMM} to {p.ScenarioFinish:dd MMM yyyy}).");
        }
        if (projects.All(p => p.SlipDays == 0)) verdict.Add("No project finish date changes.");
        _ = cal;
        return new ScenarioStored(projects, warnings, verdict, projects.Any(p => p.SlipDays != 0));
    }

    private sealed record ScenarioStored(List<ProjectImpactView> Projects, List<string> Warnings, List<string> Verdict, bool RequiresLeadershipApproval);

    private async Task<(bool Eligible, string? Note)> EligibilityAsync(int personId, int targetProjectId, CancellationToken ct)
    {
        var demands = await db.Demands.AsNoTracking().Where(d => d.ProjectId == targetProjectId).ToListAsync(ct);
        if (demands.Count == 0) return (true, "no skill demand defined on the target project");
        var ratings = await db.PersonSkills.AsNoTracking().Where(r => r.PersonId == personId).ToDictionaryAsync(r => r.SkillId, r => r.Rating, ct);
        var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var short_ = demands.Where(d => ratings.GetValueOrDefault(d.SkillId, 0) < d.MinLevel)
            .Select(d => $"{skills.GetValueOrDefault(d.SkillId, "skill")} {ratings.GetValueOrDefault(d.SkillId, 0)} < {d.MinLevel}").ToList();
        return short_.Count == 0 ? (true, null) : (false, "not eligible (skill): " + string.Join(", ", short_));
    }

    private async Task<ScenarioView> ToViewAsync(Scenario s, CancellationToken ct)
    {
        var moves = JsonSerializer.Deserialize<List<MoveInput>>(s.MovesJson, Json) ?? [];
        var names = await PersonNamesAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);
        var moveViews = moves.Select(m => new MoveView(m.PersonId, names.GetValueOrDefault(m.PersonId, "?"), m.FromProjectId, projects.GetValueOrDefault(m.FromProjectId, "?"),
            m.ToProjectId, projects.GetValueOrDefault(m.ToProjectId, "?"), m.Date, m.Percent)).ToList();
        var stored = s.ResultJson is null ? null : JsonSerializer.Deserialize<ScenarioStored>(s.ResultJson, Json);
        return new ScenarioView(s.Id, s.Name, s.Status, s.Notes, moveViews, stored?.Projects ?? [], stored?.Warnings ?? [], stored?.Verdict ?? [],
            stored?.RequiresLeadershipApproval ?? false, s.CreatedBy, s.CreatedUtc, s.DecisionBy, s.DecisionNote);
    }
}
