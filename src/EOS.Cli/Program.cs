using System.Reflection;
using System.Text.Json;
using EOS.Domain.Settings;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Projects;
using EOS.Infrastructure.Skills;
using Microsoft.EntityFrameworkCore;

// `eos` — command line for EOS V2. Grammar: eos <noun> <verb> [args] [--json] (spec §25.7).
// Phase 1: local mode against a database file (--db or EOS_DB). The REST client replaces this when the API lands.
var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
var argv = args.ToList();

string? TakeOption(string name)
{
    var i = argv.IndexOf(name);
    if (i < 0) return null;
    var value = i + 1 < argv.Count ? argv[i + 1] : null;
    argv.RemoveRange(i, value is null ? 1 : 2);
    return value;
}

bool TakeFlag(string name) => argv.Remove(name);

var dbPath = TakeOption("--db") ?? Environment.GetEnvironmentVariable("EOS_DB")
             ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EOS_V2", "eos.db");
var actor = TakeOption("--actor") ?? Environment.UserName;
var json = TakeFlag("--json");
var commit = TakeFlag("--commit");
var reason = TakeOption("--reason");

if (argv.Count == 0 || argv[0] is "-h" or "--help" or "help") { PrintHelp(); return 0; }

try
{
    switch (argv[0], argv.Count > 1 ? argv[1] : "")
    {
        case ("version", _):
            Console.WriteLine(version);
            return 0;

        case ("settings", "defaults"):
            if (json) Console.WriteLine(JsonSerializer.Serialize(SettingDefaults.All, new JsonSerializerOptions { WriteIndented = true }));
            else foreach (var (key, value) in SettingDefaults.All.OrderBy(x => x.Key)) Console.WriteLine($"{key,-40} {value}");
            return 0;

        case ("settings", "get") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var resolver = await new SettingsStore(db).LoadResolverAsync();
            Console.WriteLine(resolver.ResolveJson(argv[2], SettingsResolver.Chain(), DateOnly.FromDateTime(DateTime.Today)) ?? "(no such setting)");
            return 0;
        }

        case ("settings", "set") when argv.Count >= 4:
        {
            using var db = EosDatabase.Open(dbPath);
            await new SettingsStore(db).SetAsync(argv[2], SettingScope.Global, argv[3], DateOnly.FromDateTime(DateTime.Today), actor, reason ?? "");
            Console.WriteLine($"{argv[2]} = {argv[3]} (global, from today)");
            return 0;
        }

        case ("migrate", _):
        {
            using var db = EosDatabase.Open(dbPath);
            Console.WriteLine($"Database ready: {Path.GetFullPath(dbPath)}");
            return 0;
        }

        case ("import", "agility") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var report = await new AgilityWorkbookImporter(db).ImportAsync(argv[2], commit, actor);
            Console.Write(report.ToText());
            if (!commit) Console.WriteLine("Re-run with --commit to save.");
            return 0;
        }

        case ("import", "training") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var report = await new TrainingWorkbookImporter(db).ImportAsync(argv[2], commit, actor);
            Console.Write(report.ToText());
            if (!commit) Console.WriteLine("Re-run with --commit to save.");
            return 0;
        }

        case ("person", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            var rows = await db.People.Include(p => p.Team).OrderBy(p => p.Code).ToListAsync();
            if (json) Console.WriteLine(JsonSerializer.Serialize(rows.Select(p => new { p.Code, p.FullName, Team = p.Team?.Name, p.RoleTitle, p.BaseLocation, p.AvailabilityFraction, p.IsUnverified })));
            else
            {
                Console.WriteLine($"{"Code",-16}{"Name",-26}{"Team",-22}{"Role",-26}{"Avail",6}  Unverified");
                foreach (var p in rows)
                    Console.WriteLine($"{p.Code,-16}{p.FullName,-26}{p.Team?.Name,-22}{p.RoleTitle,-26}{p.AvailabilityFraction,6:P0}  {(p.IsUnverified ? "yes" : "")}");
                Console.WriteLine($"{rows.Count} people");
            }
            return 0;
        }

        case ("skill", "matrix"):
        {
            using var db = EosDatabase.Open(dbPath);
            var skills = await db.Skills.OrderBy(s => s.SortOrder).ToListAsync();
            var people = await db.People.Where(p => db.PersonSkills.Any(x => x.PersonId == p.Id)).OrderBy(p => p.Code).ToListAsync();
            var ratings = await db.PersonSkills.ToDictionaryAsync(x => (x.PersonId, x.SkillId), x => x.Rating);
            Console.WriteLine($"{"Person",-26}" + string.Join(" ", skills.Select(s => s.Code.PadLeft(4))) + "   Avg");
            foreach (var p in people)
            {
                var cells = skills.Select(s => ratings.TryGetValue((p.Id, s.Id), out var r) ? r : (int?)null).ToList();
                var avg = cells.Where(c => c is not null).Select(c => c!.Value).DefaultIfEmpty().Average();
                Console.WriteLine($"{p.FullName,-26}" + string.Join(" ", cells.Select(c => (c?.ToString() ?? "-").PadLeft(4))) + $"  {avg,4:0.0}");
            }
            return 0;
        }

        // The ONLY command that changes a rating after the initial import: an explicit human decision (spec §9.5).
        case ("skill", "rate") when argv.Count >= 5:
        {
            using var db = EosDatabase.Open(dbPath);
            var person = await db.People.SingleOrDefaultAsync(p => p.Code == argv[2]) ?? throw new SkillRatingException($"No person with code {argv[2]}.");
            var skill = await db.Skills.SingleOrDefaultAsync(s => s.Code == argv[3]) ?? throw new SkillRatingException($"No skill with code {argv[3]}.");
            await new SkillRatingService(db).SetRatingAsync(person.Id, skill.Id, int.Parse(argv[4]), actor, reason);
            Console.WriteLine($"{person.FullName} / {skill.Name} = {argv[4]} (recorded by {actor})");
            return 0;
        }

        case ("import", "erp") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var report = await new ErpProjectImporter(db).ImportAsync(argv[2], commit, actor);
            Console.Write(report.ToText());
            if (!commit) Console.WriteLine("Re-run with --commit to save.");
            return 0;
        }

        case ("import", "tracker") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var report = await new TrackerImporter(db).ImportAsync(argv[2], commit, actor);
            Console.Write(report.ToText());
            if (!commit) Console.WriteLine("Re-run with --commit to save.");
            return 0;
        }

        case ("import", "vms") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var report = await new InfrastructureImporter(db).ImportAsync(argv[2], commit, actor);
            Console.Write(report.ToText());
            if (!commit) Console.WriteLine("Re-run with --commit to save.");
            return 0;
        }

        case ("project", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            var search = TakeOption("--search");
            var rows = await new ProjectService(db).ListAsync(search, withPlanOnly: TakeFlag("--with-plan"));
            if (json) Console.WriteLine(JsonSerializer.Serialize(rows));
            else
            {
                Console.WriteLine($"{"Project",-18}{"Name",-44}{"Product",-10}{"ERP",-7}{"Tracker",-18}{"Tasks",6}");
                foreach (var p in rows)
                    Console.WriteLine($"{p.ProjectNo,-18}{Trim(p.Name, 42),-44}{p.ProductLine,-10}{p.ErpStatus,-7}{p.TrackerStatus,-18}{p.TaskCount,6}");
                Console.WriteLine($"{rows.Count} projects");
            }
            return 0;
        }

        case ("plan", "show" or "recompute") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var project = await db.Projects.SingleOrDefaultAsync(p => p.ProjectNo == argv[2]) ?? throw new ArgumentException($"No project {argv[2]}.");
            var plans = new PlanService(db);
            var plan = argv[1] == "recompute" ? await plans.RecomputeAsync(project.Id) : await plans.GetPlanAsync(project.Id);
            if (json) { Console.WriteLine(JsonSerializer.Serialize(plan)); return 0; }
            Console.WriteLine($"{plan.ProjectNo}  {plan.ProjectName}   {plan.Start:dd MMM yyyy} -> {plan.Finish:dd MMM yyyy}   progress {plan.Progress:P0}");
            foreach (var t in plan.Tasks)
                Console.WriteLine($"{new string(' ', (t.Level - 1) * 2)}{t.Wbs,-6}{Trim(t.Name, 46 - (t.Level - 1) * 2),-46} {t.Start:dd-MMM-yy} {t.Finish:dd-MMM-yy} {t.PercentComplete,5:P0} {t.Status,-12}{(t.IsCritical ? "CRIT" : ""),-5}{string.Join(",", t.Assignments.Select(a => a.PersonName?.Split(' ')[0]))}");
            foreach (var w in plan.Warnings) Console.WriteLine($"  WARN {w}");
            return 0;
        }

        case ("host", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            var hosts = await db.Hosts.OrderBy(h => h.Kind).ThenBy(h => h.Ip).ToListAsync();
            var disks = await db.HostDisks.ToListAsync();
            Console.WriteLine($"{"Kind",-9}{"IP",-16}{"Name",-40}{"State",-10}{"Owner",-22}Notes");
            foreach (var h in hosts.Where(h => !h.Removed))
            {
                var low = disks.Where(d => d.HostId == h.Id && d.TotalGb > 0 && d.FreeGb / d.TotalGb < 0.10m).Select(d => $"{d.Drive}: {d.FreeGb:0.#}/{d.TotalGb:0.#} GB free").ToList();
                Console.WriteLine($"{h.Kind,-9}{h.Ip,-16}{Trim(h.Name, 38),-40}{h.State,-10}{Trim(h.OwnerName ?? "", 20),-22}{(low.Count > 0 ? "LOW DISK " + string.Join(", ", low) : "")}{(h.MarkedForDeletion == true ? " reclaim?" : "")}");
            }
            return 0;
        }

        case ("alias", "resolve") when argv.Count >= 4:
        {
            using var db = EosDatabase.Open(dbPath);
            var person = await db.People.SingleOrDefaultAsync(p => p.Code == argv[3]) ?? throw new ArgumentException($"No person with code {argv[3]}.");
            var source = TakeOption("--source") ?? "tracker";
            var alias = await db.PersonAliases.SingleOrDefaultAsync(a => a.Alias == argv[2] && a.Source == source);
            if (alias is null) db.PersonAliases.Add(new EOS.Domain.People.PersonAlias { Alias = argv[2], Source = source, Kind = EOS.Domain.People.AliasKind.Initials, PersonId = person.Id });
            else alias.PersonId = person.Id;
            await db.SaveChangesAsync();
            Console.WriteLine($"{argv[2]} ({source}) -> {person.FullName}. Re-run the import to apply it.");
            return 0;
        }

        case ("training", "summary"):
        {
            using var db = EosDatabase.Open(dbPath);
            var totalModules = await db.Modules.CountAsync();
            var bands = await new SettingsStore(db).LoadResolverAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var good = bands.Get<decimal>(SettingKeys.KnowledgeGoodAtLeast, SettingsResolver.Chain(), today);
            var medium = bands.Get<decimal>(SettingKeys.KnowledgeMediumAtLeast, SettingsResolver.Chain(), today);
            var weak = bands.Get<decimal>(SettingKeys.KnowledgeWeakAtLeast, SettingsResolver.Chain(), today);
            var counts = (await db.PersonModules.GroupBy(x => new { x.PersonId, x.State }).Select(g => new { g.Key.PersonId, g.Key.State, N = g.Count() }).ToListAsync())
                .GroupBy(x => x.PersonId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.State, x => x.N));
            var people = await db.People.Where(p => db.PersonModules.Any(x => x.PersonId == p.Id)).ToListAsync();
            var all = new Dictionary<EOS.Domain.Training.KnowledgeState, int>();
            Console.WriteLine($"{"Person",-26}{"Know",6}{"Don't",6}{"Disc.",6}{"Know %",9}  Level (bands {good:P0}/{medium:P0}/{weak:P0})");
            foreach (var row in people.Select(p =>
            {
                counts.TryGetValue(p.Id, out var c);
                int Get(EOS.Domain.Training.KnowledgeState s) => c is not null && c.TryGetValue(s, out var n) ? n : 0;
                return (Person: p, Know: Get(EOS.Domain.Training.KnowledgeState.Know), Dont: Get(EOS.Domain.Training.KnowledgeState.DontKnow), Need: Get(EOS.Domain.Training.KnowledgeState.NeedToDiscuss));
            }).OrderByDescending(r => r.Know))
            {
                all[EOS.Domain.Training.KnowledgeState.Know] = all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.Know) + row.Know;
                all[EOS.Domain.Training.KnowledgeState.DontKnow] = all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.DontKnow) + row.Dont;
                all[EOS.Domain.Training.KnowledgeState.NeedToDiscuss] = all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.NeedToDiscuss) + row.Need;
                var pct = totalModules == 0 ? 0m : (decimal)row.Know / totalModules;
                var level = pct >= good ? "Good" : pct >= medium ? "Medium" : pct >= weak ? "Weak" : "Critical";
                Console.WriteLine($"{row.Person.FullName,-26}{row.Know,6}{row.Dont,6}{row.Need,6}{pct,9:P1}  {level}");
            }
            Console.WriteLine($"Modules: {totalModules}; people: {people.Count}; Know {all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.Know)}, " +
                              $"don't know {all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.DontKnow)}, need to discuss {all.GetValueOrDefault(EOS.Domain.Training.KnowledgeState.NeedToDiscuss)}");
            return 0;
        }

        case ("alias", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            foreach (var a in await db.PersonAliases.OrderBy(a => a.Source).ThenBy(a => a.Alias).ToListAsync())
                Console.WriteLine($"{a.Source,-10} {a.Alias,-18} -> {(a.PersonId is null ? "(unresolved)" : db.People.Single(p => p.Id == a.PersonId).FullName)}");
            return 0;
        }

        case ("token", "create") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            Console.WriteLine(await new EOS.Infrastructure.Security.TokenService(db).CreateAsync(argv[2], actor));
            Console.Error.WriteLine("Store this token now; it is shown once.");
            return 0;
        }

        case ("token", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            foreach (var t in await new EOS.Infrastructure.Security.TokenService(db).ListAsync())
                Console.WriteLine($"{t.Id,-4}{t.Name,-24}{t.CreatedUtc:yyyy-MM-dd}  last used {(t.LastUsedUtc?.ToString("yyyy-MM-dd HH:mm") ?? "never"),-18}{(t.Revoked ? " REVOKED" : "")}");
            return 0;
        }

        case ("token", "revoke") when argv.Count >= 3 && int.TryParse(argv[2], out var tokenId):
        {
            using var db = EosDatabase.Open(dbPath);
            await new EOS.Infrastructure.Security.TokenService(db).RevokeAsync(tokenId, actor);
            Console.WriteLine($"Token {tokenId} revoked.");
            return 0;
        }

        case ("dashboard", _):
        {
            using var db = EosDatabase.Open(dbPath);
            var cc = await new EOS.Infrastructure.Insights.DashboardService(db).GetAsync(DateOnly.FromDateTime(DateTime.Today));
            if (json) { Console.WriteLine(JsonSerializer.Serialize(cc, new JsonSerializerOptions { WriteIndented = true })); return 0; }
            var k = cc.Kpis;
            Console.WriteLine($"Projects {k.ActiveProjects} active, {k.AtRiskProjects} at risk, {k.DelayedTasks} delayed tasks");
            Console.WriteLine($"People   {k.OverAllocatedEngineers} over-allocated, {k.FreePlannedHoursNext4Weeks:N0} free planned h next 4 weeks, {k.OpenGaps} open gaps, {k.OpenDoubts} open doubts");
            Console.WriteLine($"Infra    {k.VmsRunning} VMs running / {k.VmsRunning + k.VmsOff}, {k.HostsLowOnDisk} hosts low on disk");
            foreach (var h in cc.Projects) Console.WriteLine($"  {h.ProjectNo,-12}{h.Health,-14}late {h.DelayedTasks}/{h.OpenTasks}  progress {h.ProgressActual * 100:0}%");
            return 0;
        }

        case ("alert", "list"):
        {
            using var db = EosDatabase.Open(dbPath);
            var alerts = await new EOS.Infrastructure.Insights.AlertService(db).EvaluateAsync(DateOnly.FromDateTime(DateTime.Today));
            if (json) { Console.WriteLine(JsonSerializer.Serialize(alerts, new JsonSerializerOptions { WriteIndented = true })); return 0; }
            foreach (var a in alerts) Console.WriteLine($"{a.Rule,-5}{a.Severity,-9}{Trim(a.Subject, 30),-32}{a.Message}");
            return 0;
        }

        case ("capacity", _):
        {
            using var db = EosDatabase.Open(dbPath);
            var weeks = int.TryParse(TakeOption("--weeks"), out var wk) ? wk : 8;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var rows = await new EOS.Infrastructure.Capacity.CapacityService(db).GetWeeklyLoadAsync(today, today.AddDays(weeks * 7));
            if (json) { Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true })); return 0; }
            var starts = rows.Select(r => r.WeekStart).Distinct().OrderBy(d => d).ToList();
            Console.WriteLine($"{"",-24}" + string.Join(" ", starts.Select(s => s.ToString("dd/MM").PadLeft(6))));
            foreach (var g in rows.GroupBy(r => r.PersonName).OrderBy(g => g.Key))
                Console.WriteLine($"{Trim(g.Key, 22),-24}" + string.Join(" ", starts.Select(s => (g.FirstOrDefault(r => r.WeekStart == s)?.AllocatedHours ?? 0).ToString("0").PadLeft(6))));
            return 0;
        }

        case ("report", "list"):
            foreach (var r in EOS.Infrastructure.Reports.ReportService.Catalogue) Console.WriteLine($"{r.Id,-24}{r.Code,-7}{r.Name}");
            return 0;

        case ("report", "run") when argv.Count >= 3:
        {
            using var db = EosDatabase.Open(dbPath);
            var svc = new EOS.Infrastructure.Reports.ReportService(db);
            var table = await svc.RunAsync(argv[2], new EOS.Infrastructure.Reports.ReportParameters(DateOnly.FromDateTime(DateTime.Today)));
            var xlsx = TakeOption("--xlsx"); var csv = TakeOption("--csv");
            if (xlsx is not null) { File.WriteAllBytes(xlsx, EOS.Infrastructure.Reports.ReportService.ToXlsx(table)); Console.WriteLine($"Wrote {xlsx} ({table.Rows.Count} rows)"); }
            else if (csv is not null) { File.WriteAllText(csv, EOS.Infrastructure.Reports.ReportService.ToCsv(table)); Console.WriteLine($"Wrote {csv} ({table.Rows.Count} rows)"); }
            else Console.WriteLine(EOS.Infrastructure.Reports.ReportService.ToCsv(table));
            return 0;
        }

        case ("scenario", "analyse") when argv.Count >= 3:
        {
            // eos scenario analyse <personCode>:<fromProjectNo>:<toProjectNo>:<yyyy-MM-dd>[:percent] [...] [--name text]
            using var db = EosDatabase.Open(dbPath);
            var moves = new List<EOS.Infrastructure.Capacity.MoveInput>();
            foreach (var spec in argv.Skip(2))
            {
                var p = spec.Split(':');
                if (p.Length < 4) throw new ArgumentException($"Bad move '{spec}'. Use personCode:fromProjectNo:toProjectNo:yyyy-MM-dd[:percent].");
                var person = await db.People.SingleOrDefaultAsync(x => x.Code == p[0]) ?? throw new ArgumentException($"No person with code {p[0]}.");
                var from = await db.Projects.SingleOrDefaultAsync(x => x.ProjectNo == p[1]) ?? throw new ArgumentException($"No project {p[1]}.");
                var to = await db.Projects.SingleOrDefaultAsync(x => x.ProjectNo == p[2]) ?? throw new ArgumentException($"No project {p[2]}.");
                moves.Add(new(person.Id, from.Id, to.Id, DateOnly.Parse(p[3]), p.Length > 4 ? decimal.Parse(p[4]) / 100m : 1m));
            }
            var s = await new EOS.Infrastructure.Capacity.ScenarioService(db).AnalyseAsync(moves, TakeOption("--name") ?? "CLI what-if", actor);
            if (json) { Console.WriteLine(JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true })); return 0; }
            Console.WriteLine($"Scenario #{s.Id} {s.Name} [{s.Status}]");
            foreach (var v in s.Verdict) Console.WriteLine("  " + v);
            foreach (var p in s.Projects.Where(p => p.SlipDays != 0)) Console.WriteLine($"  {p.ProjectNo}: {p.BaselineFinish:dd MMM yyyy} -> {p.ScenarioFinish:dd MMM yyyy} ({p.SlipDays:+0;-0})");
            return 0;
        }

        case ("scenario", "approve" or "reject" or "apply") when argv.Count >= 3 && int.TryParse(argv[2], out var scenarioId):
        {
            using var db = EosDatabase.Open(dbPath);
            var svc = new EOS.Infrastructure.Capacity.ScenarioService(db);
            var s = argv[1] == "apply" ? await svc.ApplyAsync(scenarioId, actor) : await svc.DecideAsync(scenarioId, argv[1] == "approve", actor, reason);
            Console.WriteLine($"Scenario #{s.Id} is now {s.Status}.");
            return 0;
        }

        case ("backup", "create"):
        {
            using var db = EosDatabase.Open(dbPath);
            var dir = TakeOption("--dir") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups");
            var file = await new EOS.Infrastructure.Operations.BackupService(db).CreateAsync(dir, actor);
            EOS.Infrastructure.Operations.BackupService.ApplyRetention(dir);
            Console.WriteLine($"{file.Path} ({file.SizeBytes / 1024 / 1024.0:0.0} MB)");
            return 0;
        }

        default:
            Console.Error.WriteLine($"Unknown command: {string.Join(' ', argv)}");
            PrintHelp();
            return 2;
    }
}
catch (SkillRatingException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (FileNotFoundException ex) { Console.Error.WriteLine($"File not found: {ex.FileName}"); return 3; }

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "~";

void PrintHelp()
{
    Console.WriteLine($"eos {version} — EOS V2 command line (local mode; database: {dbPath})");
    Console.WriteLine();
    Console.WriteLine("  global options: --db <file>  --actor <name>  --json  --commit  --reason <text>");
    Console.WriteLine("  eos version");
    Console.WriteLine("  eos migrate                          create/upgrade the database");
    Console.WriteLine("  eos settings defaults|get <key>|set <key> <json> --reason <text>");
    Console.WriteLine("  eos import agility <file.xlsx> [--commit]    people, skills, skill matrix (preview unless --commit)");
    Console.WriteLine("  eos import training <file.xlsx> [--commit]   modules, knowledge states, doubts");
    Console.WriteLine("  eos person list [--json]");
    Console.WriteLine("  eos skill matrix");
    Console.WriteLine("  eos skill rate <personCode> <skillCode> <rating> --reason <text>   (manual rating change)");
    Console.WriteLine("  eos import erp <file.xlsx> [--commit]        ERP project master (454 projects)");
    Console.WriteLine("  eos import tracker <file.xlsx> [--commit]    LPP projects, task plans, AMC tickets");
    Console.WriteLine("  eos import vms <file.xlsx> [--commit]        servers, VMs, disks, demo systems (credentials never read)");
    Console.WriteLine("  eos project list [--search text] [--with-plan] [--json]");
    Console.WriteLine("  eos plan show|recompute <projectNo> [--json]");
    Console.WriteLine("  eos host list");
    Console.WriteLine("  eos alias resolve <alias> <personCode> [--source tracker]   map initials to a person");
    Console.WriteLine("  eos training summary                 Know / don't know / need-to-discuss per person");
    Console.WriteLine("  eos alias list");
    Console.WriteLine("  eos token create <name>|list|revoke <id>       access tokens (the name is the actor on every change)");
    Console.WriteLine("  eos dashboard [--json]                         the Command Center in text");
    Console.WriteLine("  eos alert list [--json]");
    Console.WriteLine("  eos capacity [--weeks 8] [--json]              weekly allocated hours per engineer");
    Console.WriteLine("  eos report list | run <id> [--csv f | --xlsx f]");
    Console.WriteLine("  eos scenario analyse <personCode:fromNo:toNo:yyyy-MM-dd[:pct]>... [--name t]");
    Console.WriteLine("  eos scenario approve|reject|apply <id> [--reason t]");
    Console.WriteLine("  eos backup create [--dir path]");
}
