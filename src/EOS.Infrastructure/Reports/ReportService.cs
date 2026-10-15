using ClosedXML.Excel;
using EOS.Domain.Settings;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Training;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Reports;

public sealed record ReportDefinition(string Id, string Code, string Name, string Description, IReadOnlyList<string> Parameters);

public sealed record ReportTable(string Id, string Name, DateTime GeneratedUtc, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

public sealed record ReportParameters(DateOnly AsOf, DateOnly? From = null, DateOnly? To = null, int? ProjectId = null, int? TeamId = null, int Months = 3);

public sealed class ReportService(EosDbContext db)
{
    public static IReadOnlyList<ReportDefinition> Catalogue { get; } =
    [
        new("portfolio-status", "R-P1", "Portfolio status", "Active projects with computed health, delayed tasks and forecast vs planned finish.", ["asOf"]),
        new("project-register", "R-P7", "Project register", "ERP project master: customer, status, manager, order value, margin.", []),
        new("pipeline", "R-P6", "Pipeline", "Opportunities by stage with weighted hours and required skills.", []),
        new("capacity", "R-C1", "Capacity vs commitment", "Available, planned, allocated, free hours and load band per engineer and month.", ["from", "to", "teamId"]),
        new("engineer-load", "R-C2", "Engineer load (weekly)", "Hours per engineer per week against capacity.", ["from", "to", "teamId"]),
        new("who-is-where", "R-C3", "Who is working where", "Hours per engineer per project in the month of asOf.", ["asOf"]),
        new("demand-register", "R-C4", "Demand and gap register", "Every demand line with fill status, eligible engineers and best-rated engineer.", ["projectId"]),
        new("supply-demand", "R-C5", "Forward supply vs demand", "Skill x month: demand, pipeline, pooled free supply and gap.", ["asOf", "months"]),
        new("movements", "R-C9", "Resource movements", "The movement ledger.", []),
        new("change-log", "R-C10", "Change and gap log", "Capacity gaps, change requests and decisions.", []),
        new("skill-matrix", "R-S1", "Skill matrix", "Engineers x skills with average rating and deployable count.", []),
        new("skill-summary", "R-S2", "Skill summary", "Per skill: resources, deployable engineers, competency %.", []),
        new("knowledge", "R-T2", "Team knowledge", "Know % per engineer with level bands.", ["asOf"]),
        new("doubts", "R-T3", "Doubts backlog", "Open and solved doubts with age and mentor.", ["asOf"]),
        new("vm-register", "R-I1", "VM register by project", "Servers and VMs with state, owner, project links (no credentials).", []),
        new("host-capacity", "R-I2", "Host capacity", "Disk free/total per drive and VM counts per host.", []),
        new("reclaim", "R-I3", "Reclaim list", "VMs that are off, unused and candidates for removal.", []),
        new("tickets", "R-A1", "AMC ticket register", "Support tickets with assignment and resolution.", []),
        new("alerts", "-", "Alerts", "Everything the rules flag right now.", ["asOf"]),
    ];

    public async Task<ReportTable> RunAsync(string id, ReportParameters p, CancellationToken ct = default)
    {
        var def = Catalogue.SingleOrDefault(r => r.Id == id) ?? throw new ArgumentException($"Unknown report '{id}'.");
        var from = p.From ?? new DateOnly(p.AsOf.Year, p.AsOf.Month, 1);
        var to = p.To ?? from.AddMonths(2).AddDays(-1);
        string[] cols;
        List<IReadOnlyList<object?>> rows;

        switch (id)
        {
            case "portfolio-status":
            {
                cols = ["Project", "Name", "Customer", "Product", "Tracker status", "Health", "Open tasks", "Delayed", "Planned end", "Forecast end", "Variance (working days)", "Progress", "Reasons"];
                var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
                rows = (await new HealthService(db).GetAllAsync(p.AsOf, ct)).Select(h => Row(h.ProjectNo, h.Name, projects[h.ProjectId].Customer, projects[h.ProjectId].ProductLine,
                    projects[h.ProjectId].TrackerStatus, h.Health + (h.IsOverride ? " (override)" : ""), h.OpenTasks, h.DelayedTasks, h.PlannedEnd, h.ForecastEnd,
                    h.ScheduleVarianceDays, h.ProgressActual, string.Join("; ", h.Reasons))).ToList();
                break;
            }
            case "project-register":
                cols = ["Project", "Name", "Customer", "Type", "ERP status", "Tracker status", "Project manager", "Technical manager", "Order value", "Margin", "PO start", "PO end"];
                rows = (await db.Projects.AsNoTracking().OrderBy(x => x.ProjectNo).ToListAsync(ct)).Select(x => Row(x.ProjectNo, x.Name, x.Customer, x.ProjectType, x.ErpStatus,
                    x.TrackerStatus, x.ProjectManager, x.TechnicalManager, x.OrderValue, x.MarginPercent, x.PoStart, x.PoEnd)).ToList();
                break;
            case "pipeline":
                cols = ["Code", "Name", "Lead", "Stage", "Status", "Expected hours", "Win probability", "Weighted hours", "Booking month", "Months", "Skills"];
                rows = (await new PipelineService(db).ListAsync(ct)).Select(o => Row(o.Code, o.Name, o.LeadName, o.Stage, o.Status, o.ExpectedHours, o.WinProbability, o.WeightedHours,
                    o.LikelyBookingMonth, o.DurationMonths, o.RequiredSkills)).ToList();
                break;
            case "capacity":
                cols = ["Engineer", "Team", "Month", "Available h", "Planned h", "Leave h", "Allocated h", "Proposed h", "Load vs planned", "Load vs available", "Band", "Projects"];
                rows = (await new CapacityService(db).GetCapacityAsync(from, to, p.TeamId, ct: ct)).Select(r => Row(r.PersonName, r.Team, r.Month, r.AvailableHours, r.PlannedHours,
                    r.LeaveHours, r.AllocatedHours, r.ProposedHours, r.LoadVsPlanned, r.LoadVsAvailable, r.Band.ToString(), string.Join(", ", r.ByProject.Select(x => $"{x.ProjectNo} {x.Hours:0.#}h")))).ToList();
                break;
            case "engineer-load":
                cols = ["Engineer", "Week starting", "Capacity h", "Allocated h", "Load", "Band", "Projects"];
                rows = (await new CapacityService(db).GetWeeklyLoadAsync(from, to, p.TeamId, ct: ct)).Select(r => Row(r.PersonName, r.WeekStart, r.CapacityHours, r.AllocatedHours, r.LoadVsCapacity,
                    r.Band.ToString(), string.Join(", ", r.ByProject.Select(x => $"{x.ProjectNo} {x.Hours:0.#}h")))).ToList();
                break;
            case "who-is-where":
            {
                cols = ["Engineer", "Project", "Hours (month)", "Source"];
                var month = new DateOnly(p.AsOf.Year, p.AsOf.Month, 1);
                rows = (await new CapacityService(db).GetCapacityAsync(month, month, p.TeamId, ct: ct)).SelectMany(r => r.ByProject.Select(x => Row(r.PersonName, x.ProjectNo, x.Hours, x.Source))).ToList();
                break;
            }
            case "demand-register":
                cols = ["Project", "Skill", "Min level", "Hours/week", "From", "To", "Location", "Status", "Supplied h/week", "Eligible", "Best-rated", "Best rating"];
                rows = (await new DemandService(db).ListAsync(p.ProjectId, ct: ct)).Select(d => Row(d.ProjectNo, d.SkillName, d.MinLevel, d.HoursPerWeek, d.Start, d.End, d.Location, d.Status,
                    d.SuppliedHoursPerWeek, d.EligibleCount, d.BestPerson, d.BestRating == 0 ? null : d.BestRating)).ToList();
                break;
            case "supply-demand":
                cols = ["Skill", "Month", "Project demand h", "Pipeline h", "Supply h (pooled)", "Gap h", "Deployable engineers"];
                rows = (await new PipelineService(db).SupplyDemandAsync(p.AsOf, p.Months, ct)).Select(r => Row(r.SkillName, r.Month, r.ProjectDemandHours, r.PipelineHours, r.SupplyHours, r.GapHours, r.DeployableCount)).ToList();
                break;
            case "movements":
            {
                cols = ["Date", "Engineer", "From", "To", "Percent", "Status", "Requested by", "Approved by", "Reason"];
                var people = await db.People.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
                var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.ProjectNo, ct);
                rows = (await db.Movements.AsNoTracking().OrderByDescending(m => m.EffectiveDate).ToListAsync(ct)).Select(m => Row(m.EffectiveDate, people.GetValueOrDefault(m.PersonId), projects.GetValueOrDefault(m.FromProjectId),
                    projects.GetValueOrDefault(m.ToProjectId), m.Percent, m.Status, m.RequestedBy, m.ApprovedBy, m.Reason)).ToList();
                break;
            }
            case "change-log":
                cols = ["Raised on", "Kind", "Raised by", "Description", "Gap type", "Impact h", "Impact days", "Decision", "Approver", "Decided on", "Status"];
                rows = (await db.ChangeGapLog.AsNoTracking().OrderByDescending(g => g.RaisedOn).ToListAsync(ct)).Select(g => Row(g.RaisedOn, g.Kind, g.RaisedBy, g.Description, g.GapType, g.ImpactHours,
                    g.ImpactDays, g.Decision, g.Approver, g.DecisionOn, g.Status)).ToList();
                break;
            case "skill-matrix":
            {
                var skills = await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
                var ratings = await db.PersonSkills.AsNoTracking().ToListAsync(ct);
                var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
                var deployable = resolver.Get<int>(SettingKeys.SkillDeployableMinLevel, SettingsResolver.Chain(), p.AsOf);
                cols = [.. new[] { "Engineer", "Team" }, .. skills.Select(s => s.Name), "Average", "Skills at deployable level"];
                var people = await db.People.AsNoTracking().Include(x => x.Team).Where(x => db.PersonSkills.Any(r => r.PersonId == x.Id)).OrderBy(x => x.FullName).ToListAsync(ct);
                rows = people.Select(person =>
                {
                    var mine = skills.Select(s => ratings.FirstOrDefault(r => r.PersonId == person.Id && r.SkillId == s.Id)?.Rating).ToList();
                    var rated = mine.Where(x => x is not null).Select(x => (decimal)x!.Value).ToList();
                    return Row([person.FullName, person.Team?.Name, .. mine.Cast<object?>(), rated.Count == 0 ? null : Math.Round(rated.Average(), 2), rated.Count(x => x >= deployable)]);
                }).ToList();
                break;
            }
            case "skill-summary":
            {
                var resolver = await new SettingsStore(db).LoadResolverAsync(ct);
                var resourceMin = resolver.Get<int>(SettingKeys.SkillResourceMinLevel, SettingsResolver.Chain(), p.AsOf);
                var deployableMin = resolver.Get<int>(SettingKeys.SkillDeployableMinLevel, SettingsResolver.Chain(), p.AsOf);
                var levels = resolver.Get<int>(SettingKeys.SkillLevelCount, SettingsResolver.Chain(), p.AsOf);
                var ratings = await db.PersonSkills.AsNoTracking().ToListAsync(ct);
                cols = ["Skill", "Rated engineers", $"Resources (>= {resourceMin})", $"Deployable (>= {deployableMin})", "Competency %"];
                rows = (await db.Skills.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct)).Select(s =>
                {
                    var r = ratings.Where(x => x.SkillId == s.Id).Select(x => x.Rating).ToList();
                    return Row(s.Name, r.Count, r.Count(x => x >= resourceMin), r.Count(x => x >= deployableMin), r.Count == 0 ? null : Math.Round(r.Average() / levels, 4));
                }).ToList();
                break;
            }
            case "knowledge":
                cols = ["Engineer", "Known", "Don't know", "Need to discuss", "Modules", "Know %", "Level"];
                rows = (await new TrainingService(db).SummaryAsync(p.AsOf, ct)).Select(r => Row(r.Name, r.Known, r.DontKnow, r.NeedToDiscuss, r.TotalModules, r.KnowFraction, r.Level)).ToList();
                break;
            case "doubts":
                cols = ["Raised", "Module", "Sub-module", "Doubt", "Raised by", "Mentor", "Assigned", "Solved", "Status", "Age (days)", "Remarks"];
                rows = (await new TrainingService(db).DoubtsAsync(ct: ct)).Select(d => Row(d.RaisedOn, d.Module, d.SubModule, d.Text, d.RaisedByName, d.AssignedToName, d.AssignedOn, d.SolvedOn,
                    d.Result.ToString(), d.RaisedOn is { } r && d.Result != EOS.Domain.Training.DoubtResult.Solved ? p.AsOf.DayNumber - r.DayNumber : null, d.Remarks)).ToList();
                break;
            case "vm-register":
                cols = ["Kind", "IP", "Name", "Purpose", "State", "Owner", "Host", "Projects", "Need backup", "Marked for deletion"];
                rows = (await new HostService(db).ListAsync(ct: ct)).Select(h => Row(h.Kind, h.Ip, h.Name, h.Purpose, h.State, h.OwnerName, h.ParentIp, string.Join(", ", h.Projects.Select(x => $"{x.ProjectNo} ({x.Role})")),
                    h.NeedBackup, h.MarkedForDeletion)).ToList();
                break;
            case "host-capacity":
                cols = ["Host", "Name", "RAM GB", "CPU cores", "VMs", "Drive", "Free GB", "Total GB", "Free %"];
                rows = (await new HostService(db).ListAsync(ct: ct)).Where(h => h.Kind == "physical").SelectMany(h => h.Disks.Count == 0
                    ? [Row(h.Ip, h.Name, h.RamGb, h.CpuCores, h.VmCount, null, null, null, null)]
                    : h.Disks.Select(d => Row(h.Ip, h.Name, h.RamGb, h.CpuCores, h.VmCount, d.Drive, d.FreeGb, d.TotalGb, d.FreeFraction))).ToList();
                break;
            case "reclaim":
                cols = ["Host", "VM IP", "Name", "Purpose", "Owner", "Need backup"];
                rows = (await new HostService(db).ReclaimListAsync(ct)).Select(h => Row(h.ParentIp, h.Ip, h.Name, h.Purpose, h.OwnerName, h.NeedBackup)).ToList();
                break;
            case "tickets":
                cols = ["Ticket", "Product", "In AMC", "Raised", "Raised by", "Issue", "Assigned", "Engineer(s)", "Resolved", "Root cause", "Status"];
                rows = (await db.AmcTickets.AsNoTracking().OrderByDescending(t => t.RaisedOn).ToListAsync(ct)).Select(t => Row(t.ProjectNo, t.Product, t.InAmc, t.RaisedOn, t.RaisedBy, t.Issue, t.AssignedOn,
                    t.AssignedEngineers, t.ResolvedOn, t.RootCause, t.Status)).ToList();
                break;
            case "alerts":
                cols = ["Rule", "Severity", "Subject kind", "Subject", "Message"];
                rows = (await new AlertService(db).EvaluateAsync(p.AsOf, ct)).Select(a => Row(a.Rule, a.Severity.ToString(), a.SubjectKind, a.Subject, a.Message)).ToList();
                break;
            default:
                throw new ArgumentException($"Report '{id}' is not implemented.");
        }
        return new ReportTable(def.Id, def.Name, DateTime.UtcNow, cols, rows);
    }

    private static IReadOnlyList<object?> Row(params object?[] values) => values;

    public static string ToCsv(ReportTable t)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(',', t.Columns.Select(Csv)));
        foreach (var r in t.Rows) sb.AppendLine(string.Join(',', r.Select(v => Csv(Format(v)))));
        return sb.ToString();
    }

    public static byte[] ToXlsx(ReportTable t)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(t.Name.Length > 31 ? t.Name[..31] : t.Name);
        for (var c = 0; c < t.Columns.Count; c++) { ws.Cell(1, c + 1).Value = t.Columns[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
        for (var r = 0; r < t.Rows.Count; r++)
            for (var c = 0; c < t.Rows[r].Count; c++)
            {
                var v = t.Rows[r][c];
                var cell = ws.Cell(r + 2, c + 1);
                switch (v)
                {
                    case null: break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd-mmm-yyyy"; break;
                    case decimal m: cell.Value = m; break;
                    case int i: cell.Value = i; break;
                    case bool b: cell.Value = b; break;
                    default: cell.Value = Format(v); break;
                }
            }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(t.Rows.Count + 1, 200));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd"),
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        _ => v.ToString() ?? "",
    };

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
