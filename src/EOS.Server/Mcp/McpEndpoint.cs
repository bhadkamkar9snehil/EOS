using System.Text.Json;
using System.Text.Json.Nodes;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Hosting;
using EOS.Infrastructure.Insights;
using EOS.Infrastructure.People;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Projects;
using EOS.Infrastructure.Reports;
using EOS.Server.Api;
using Microsoft.EntityFrameworkCore;

namespace EOS.Server.Mcp;

/// <summary>
/// Model Context Protocol server (JSON-RPC 2.0 over HTTP POST at /mcp), same bearer-token auth as the API.
/// Read tools answer questions; the two write tools (scenario analyse, scenario apply) leave the plan untouched unless confirm=true.
/// Skill ratings are deliberately not exposed: they change only by the owner in the UI or CLI.
/// </summary>
public static class McpEndpoint
{
    private const string ProtocolVersion = "2025-03-26";
    private static readonly JsonSerializerOptions Out = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private sealed record Tool(string Name, string Description, JsonObject Schema, Func<IServiceProvider, JsonObject, string, Task<object?>> Run);

    private static JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] props)
    {
        var p = new JsonObject();
        foreach (var x in props) p[x.Name] = new JsonObject { ["type"] = x.Type, ["description"] = x.Description };
        return new JsonObject { ["type"] = "object", ["properties"] = p, ["required"] = new JsonArray(props.Where(x => x.Required).Select(x => (JsonNode)x.Name).ToArray()) };
    }

    private static string Str(JsonObject a, string name) => a[name]?.GetValue<string>() ?? throw new ArgumentException($"'{name}' is required.");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static async Task<int> ProjectIdAsync(IServiceProvider sp, string projectNo) =>
        (await sp.GetRequiredService<EosDbContext>().Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectNo == projectNo)
            ?? throw new ArgumentException($"No project '{projectNo}'.")).Id;

    private static async Task<int> PersonIdAsync(IServiceProvider sp, string codeOrName)
    {
        var db = sp.GetRequiredService<EosDbContext>();
        var p = await db.People.AsNoTracking().FirstOrDefaultAsync(x => x.Code == codeOrName || x.FullName == codeOrName)
            ?? throw new ArgumentException($"No person '{codeOrName}'. Use the employee code or the exact full name.");
        return p.Id;
    }

    private static readonly Tool[] Tools =
    [
        new("dashboard", "Command Center: KPIs, project health, alerts, 8-week load, supply vs demand, upcoming milestones.", Schema(),
            async (sp, _, _) => await sp.GetRequiredService<DashboardService>().GetAsync(Today)),
        new("list_projects", "Projects (ERP master + delivery). Optional search text; withPlan limits to projects with a task plan.",
            Schema(("search", "string", "match on number, name or customer", false), ("withPlan", "boolean", "only projects that have a plan", false)),
            async (sp, a, _) => await sp.GetRequiredService<ProjectService>().ListAsync(a["search"]?.GetValue<string>(), a["withPlan"]?.GetValue<bool>() ?? false)),
        new("get_plan", "The scheduled plan of one project: tasks, dates, float, critical path, assignments, links.",
            Schema(("projectNo", "string", "e.g. PRO2503040", true)),
            async (sp, a, _) => await sp.GetRequiredService<PlanService>().GetPlanAsync(await ProjectIdAsync(sp, Str(a, "projectNo")))),
        new("project_health", "Health (On Track / Moderate Risk / At Risk) of every project with its reasons.", Schema(),
            async (sp, _, _) => await sp.GetRequiredService<HealthService>().GetAllAsync(Today)),
        new("list_people", "Engineers with team, role and availability.", Schema(),
            async (sp, _, _) => await sp.GetRequiredService<PeopleService>().ListAsync()),
        new("person_profile", "One engineer: skills, projects, capacity, leave.", Schema(("person", "string", "employee code or full name", true)),
            async (sp, a, _) => await sp.GetRequiredService<PeopleService>().GetProfileAsync(await PersonIdAsync(sp, Str(a, "person")), Today)),
        new("weekly_load", "Allocated hours per engineer per week against capacity.", Schema(("weeks", "integer", "how many weeks ahead (default 8)", false)),
            async (sp, a, _) => await sp.GetRequiredService<CapacityService>().GetWeeklyLoadAsync(Today, Today.AddDays((a["weeks"]?.GetValue<int>() ?? 8) * 7))),
        new("alerts", "Current alerts from the configured rules.", Schema(),
            async (sp, _, _) => await sp.GetRequiredService<AlertService>().EvaluateAsync(Today)),
        new("list_hosts", "Servers and VMs with disks and linked projects. Optional projectNo.", Schema(("projectNo", "string", "limit to one project", false)),
            async (sp, a, _) => await sp.GetRequiredService<HostService>().ListAsync(a["projectNo"]?.GetValue<string>() is { } no ? await ProjectIdAsync(sp, no) : null)),
        new("list_reports", "Report catalogue.", Schema(), (_, _, _) => Task.FromResult<object?>(ReportService.Catalogue)),
        new("run_report", "Run a report by id (see list_reports) and return its table.", Schema(("id", "string", "report id", true)),
            async (sp, a, _) => await sp.GetRequiredService<ReportService>().RunAsync(Str(a, "id"), new ReportParameters(Today))),
        new("release_analysis", "Which engineers can be freed to help a target project, and what it costs the projects they leave.",
            Schema(("projectNo", "string", "target project", true), ("date", "string", "analysis date yyyy-MM-dd", true)),
            async (sp, a, _) => await sp.GetRequiredService<ScenarioService>().ReleaseAnalysisAsync(await ProjectIdAsync(sp, Str(a, "projectNo")), DateOnly.Parse(Str(a, "date")), Today)),
        new("scenario_analyse", "What-if: move an engineer between projects from a date. Saves an analysed scenario (no plan is changed). Returns finish-date impact on every project.",
            Schema(("person", "string", "employee code or full name", true), ("fromProjectNo", "string", "", true), ("toProjectNo", "string", "", true),
                   ("date", "string", "effective date yyyy-MM-dd", true), ("percent", "number", "share of the engineer, 1-100 (default 100)", false)),
            async (sp, a, actor) =>
            {
                var move = new MoveInput(await PersonIdAsync(sp, Str(a, "person")), await ProjectIdAsync(sp, Str(a, "fromProjectNo")), await ProjectIdAsync(sp, Str(a, "toProjectNo")),
                    DateOnly.Parse(Str(a, "date")), (a["percent"]?.GetValue<decimal>() ?? 100m) / 100m);
                return await sp.GetRequiredService<ScenarioService>().AnalyseAsync([move], "MCP what-if " + DateTime.Now.ToString("dd MMM HH:mm"), actor, today: Today);
            }),
        new("scenario_apply", "Apply an analysed scenario to the plans (rewrites assignments, recomputes). Requires confirm=true; without it only a preview of the scenario is returned.",
            Schema(("scenarioId", "integer", "id from scenario_analyse", true), ("confirm", "boolean", "must be true to change plans", false)),
            async (sp, a, actor) =>
            {
                var svc = sp.GetRequiredService<ScenarioService>();
                var id = a["scenarioId"]?.GetValue<int>() ?? throw new ArgumentException("'scenarioId' is required.");
                if (a["confirm"]?.GetValue<bool>() != true)
                    return new { applied = false, note = "Nothing changed. Ask the owner, then call again with confirm=true.", scenario = await svc.GetAsync(id) };
                return new { applied = true, scenario = await svc.ApplyAsync(id, actor, Today) };
            }),
    ];

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/mcp", async (HttpContext http) =>
        {
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(http.Request.Body); }
            catch (JsonException) { return Results.Json(Error(null, -32700, "Parse error"), statusCode: 400); }
            if (body is JsonArray batch)
            {
                var replies = new JsonArray();
                foreach (var item in batch) if (await HandleAsync(http, item?.AsObject()) is { } r) replies.Add(r);
                return replies.Count == 0 ? Results.Accepted() : Results.Json(replies);
            }
            var reply = await HandleAsync(http, body?.AsObject());
            return reply is null ? Results.Accepted() : Results.Json(reply);
        }).RequireAuthorization();

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static async Task<JsonObject?> HandleAsync(HttpContext http, JsonObject? req)
    {
        if (req is null) return Error(null, -32600, "Invalid request");
        var id = req["id"];
        var method = req["method"]?.GetValue<string>();
        if (id is null) return null;                                      // notification (e.g. notifications/initialized): no reply

        switch (method)
        {
            case "initialize":
                return Ok(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "eos", ["version"] = "2.0" },
                    ["instructions"] = "EOS V2: engineering operations (projects, plans, engineers, capacity, skills, infrastructure). Read tools are free to call. Changes to plans need confirm=true after the owner agrees. Skill ratings cannot be changed here.",
                });
            case "ping":
                return Ok(id, new JsonObject());
            case "tools/list":
                return Ok(id, new JsonObject
                {
                    ["tools"] = new JsonArray(Tools.Select(t => (JsonNode)new JsonObject
                    { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.Schema.DeepClone() }).ToArray()),
                });
            case "tools/call":
            {
                var p = req["params"]?.AsObject();
                var tool = Tools.FirstOrDefault(t => t.Name == p?["name"]?.GetValue<string>());
                if (tool is null) return Error(id, -32602, $"Unknown tool '{p?["name"]}'.");
                try
                {
                    await using var scope = http.RequestServices.CreateAsyncScope();
                    var result = await tool.Run(scope.ServiceProvider, p?["arguments"]?.AsObject() ?? [], http.Actor());
                    return Ok(id, Content(JsonSerializer.Serialize(result, Out), false));
                }
                catch (Exception ex) when (ex is ArgumentException or PlanException or ScenarioException or InvalidOperationException or FormatException)
                {
                    return Ok(id, Content(ex.Message, true));
                }
            }
            default:
                return Error(id, -32601, $"Method not found: {method}");
        }
    }

    private static JsonObject Ok(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };

    private static JsonObject Content(string text, bool isError) =>
        new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError };
}
