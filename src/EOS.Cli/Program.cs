using System.Reflection;
using EOS.Domain.Settings;

// `eos` — command line for EOS V2. Grammar: eos <noun> <verb> [args] [--json] (spec §25.7).
// Phase 1 skeleton: the REST client comes with the API; for now only local, side-effect-free commands exist.
var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine($"eos {version} — EOS V2 command line");
    Console.WriteLine();
    Console.WriteLine("  eos version                    print the version");
    Console.WriteLine("  eos settings defaults [--json] list seed defaults for every setting");
    return 0;
}

switch (args[0])
{
    case "version":
        Console.WriteLine(version);
        return 0;

    case "settings" when args.Length >= 2 && args[1] == "defaults":
        if (args.Contains("--json"))
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(SettingDefaults.All, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        else
            foreach (var (key, value) in SettingDefaults.All.OrderBy(x => x.Key))
                Console.WriteLine($"{key,-40} {value}");
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command: {string.Join(' ', args)}");
        return 2;
}
