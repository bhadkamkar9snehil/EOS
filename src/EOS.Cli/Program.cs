using System.Reflection;
using System.Text.Json;
using EOS.Domain.Settings;
using EOS.Infrastructure.Importing;
using EOS.Infrastructure.Persistence;
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

        default:
            Console.Error.WriteLine($"Unknown command: {string.Join(' ', argv)}");
            PrintHelp();
            return 2;
    }
}
catch (SkillRatingException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (FileNotFoundException ex) { Console.Error.WriteLine($"File not found: {ex.FileName}"); return 3; }

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
    Console.WriteLine("  eos training summary                 Know / don't know / need-to-discuss per person");
    Console.WriteLine("  eos alias list");
}
