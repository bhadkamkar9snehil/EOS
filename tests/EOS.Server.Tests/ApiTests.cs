using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EOS.Server.Tests;

public sealed class EosFactory : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"eos-api-{Guid.NewGuid():N}.db");
    public string Token { get; private set; } = "";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("EOS:DatabasePath", DatabasePath);
        builder.UseSetting("EOS:BackupDirectory", Path.Combine(Path.GetTempPath(), $"eos-api-bk-{Guid.NewGuid():N}"));
    }

    public async Task<HttpClient> ClientAsync(string name = "tester")
    {
        using var scope = Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        if (Token.Length == 0) Token = await tokens.CreateAsync(name, "test");
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var f in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
            try { File.Delete(f); } catch (IOException) { }
    }
}

public sealed class ApiTests : IClassFixture<EosFactory>
{
    private readonly EosFactory _factory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiTests(EosFactory factory) => _factory = factory;

    private static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Health_Is_Anonymous_And_The_Api_Needs_A_Token()
    {
        var anon = _factory.CreateClient();
        var health = await anon.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", (await Body(health)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/projects")).StatusCode);

        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eos_wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/projects")).StatusCode);

        var ok = await _factory.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await ok.GetAsync("/api/v1/projects")).StatusCode);
    }

    [Fact]
    public async Task Bootstrap_Is_Refused_When_Tokens_Exist_Or_The_Caller_Is_Not_Local()
    {
        await _factory.ClientAsync();
        var anon = _factory.CreateClient();
        var r = await anon.PostAsJsonAsync("/auth/bootstrap", new { name = "x" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.False((await Body(await anon.GetAsync("/auth/state"))).GetProperty("needsBootstrap").GetBoolean());
    }

    [Fact]
    public async Task Login_With_A_Valid_Token_Sets_The_Cookie_And_A_Bad_Token_Is_Rejected()
    {
        var client = await _factory.ClientAsync();
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new { token = "eos_nope" })).StatusCode);
        var ok = await anon.PostAsJsonAsync("/auth/login", new { token = _factory.Token });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"), c => c.StartsWith("eos_token=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        _ = client;
    }

    [Fact]
    public async Task Validation_Problems_Are_400_And_Unknown_Things_Are_404_With_Problem_Json()
    {
        var c = await _factory.ClientAsync();
        var bad = await c.PostAsJsonAsync("/api/v1/projects", new { projectNo = "", name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("application/problem+json", bad.Content.Headers.ContentType!.MediaType);

        var missing = await c.GetAsync("/api/v1/projects/999999/plan");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("not found", (await Body(missing)).GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Project_Plan_Can_Be_Built_Scheduled_And_Baselined_Over_Http()
    {
        var c = await _factory.ClientAsync();
        var person = await Body(await c.PostAsJsonAsync("/api/v1/people", new { code = "E1", fullName = "Anna Api" }));
        var personId = person.GetProperty("id").GetInt32();

        var project = await Body(await c.PostAsJsonAsync("/api/v1/projects", new { projectNo = "PRO-API", name = "API project", poStart = "2026-10-05", trackerStatus = "Under Development" }));
        var projectId = project.GetProperty("id").GetInt32();

        var t1 = (await Body(await c.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new { name = "Design", workHours = 40, kind = "task" }))).GetProperty("id").GetInt32();
        var t2 = (await Body(await c.PostAsJsonAsync($"/api/v1/projects/{projectId}/tasks", new { name = "Build", workHours = 80 }))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/links", new { predecessorId = t1, successorId = t2, type = 0, lagDays = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/links", new { predecessorId = t2, successorId = t1, type = 0, lagDays = 0 })).StatusCode);
        foreach (var t in new[] { t1, t2 })
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/api/v1/tasks/{t}/assignments", new { personId, units = 1 })).StatusCode);

        var plan = await Body(await c.PostAsync($"/api/v1/projects/{projectId}/plan/recompute?statusDate=2026-10-05", null));
        var tasks = plan.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal("2026-10-09", tasks[0].GetProperty("finish").GetString());
        Assert.Equal("2026-10-21", tasks[1].GetProperty("finish").GetString());
        Assert.Equal("2026-10-21", plan.GetProperty("finish").GetString());

        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/api/v1/projects/{projectId}/baselines", new { name = "v1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PatchAsJsonAsync($"/api/v1/tasks/{t2}", new { workHours = 160 })).StatusCode);
        await c.PostAsync($"/api/v1/projects/{projectId}/plan/recompute?statusDate=2026-10-05", null);
        var variance = await Body(await c.GetAsync($"/api/v1/projects/{projectId}/baseline-variance"));
        Assert.Equal(10, variance.GetProperty("finishVarianceDays").GetInt32());

        var capacity = await Body(await c.GetAsync($"/api/v1/capacity?from=2026-10-01&to=2026-10-31&personId={personId}"));
        Assert.True(capacity[0].GetProperty("allocatedHours").GetDecimal() > 100m);
        Assert.Equal("PRO-API", (await Body(await c.GetAsync("/api/v1/projects?search=api")))[0].GetProperty("projectNo").GetString());
    }

    [Fact]
    public async Task Skill_Ratings_Need_A_Reason_And_Are_Recorded_With_The_Token_Name()
    {
        var c = await _factory.ClientAsync();
        var anna = (await Body(await c.PostAsJsonAsync("/api/v1/people", new { code = "E-SK", fullName = "Skill Subject" }))).GetProperty("id").GetInt32();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EosDbContext>();
            db.Skills.Add(new EOS.Domain.Skills.Skill { Code = "SQLX", Name = "SQLX" });
            await db.SaveChangesAsync();
        }
        var skills = await Body(await c.GetAsync("/api/v1/skills"));
        var skillId = skills.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "SQLX").GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/v1/people/{anna}/skills/{skillId}", new { rating = 3, reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/v1/people/{anna}/skills/{skillId}", new { rating = 9, reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync($"/api/v1/people/{anna}/skills/{skillId}", new { rating = 4, reason = "Led the SQL migration" })).StatusCode);

        var history = await Body(await c.GetAsync($"/api/v1/people/{anna}/skills/{skillId}/history"));
        Assert.Equal("tester", history[0].GetProperty("changedBy").GetString());
        Assert.Equal(4, history[0].GetProperty("newRating").GetInt32());
    }

    [Fact]
    public async Task Settings_Can_Be_Changed_With_A_Reason_And_Show_As_Effective()
    {
        var c = await _factory.ClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/v1/settings", new { key = "time.hoursPerDay", scopeKind = "Global", jsonValue = "7.5", reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/v1/settings", new { key = "time.hoursPerDay", scopeKind = "Global", jsonValue = "7.5", reason = "Shorter day" })).StatusCode);
        var settings = await Body(await c.GetAsync("/api/v1/settings"));
        var hours = settings.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "time.hoursPerDay");
        Assert.Equal("8", hours.GetProperty("seedDefault").GetString());
        Assert.Equal("7.5", hours.GetProperty("effective").GetString());
        Assert.Equal("Shorter day", settings.GetProperty("overrides").EnumerateArray().Single(o => o.GetProperty("key").GetString() == "time.hoursPerDay").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Reports_Download_As_Csv_And_Xlsx_And_The_Catalogue_Is_Listed()
    {
        var c = await _factory.ClientAsync();
        var catalogue = await Body(await c.GetAsync("/api/v1/reports"));
        Assert.True(catalogue.GetArrayLength() >= 15);

        var csv = await c.GetAsync("/api/v1/reports/project-register?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("Project,Name,Customer", await csv.Content.ReadAsStringAsync());

        var xlsx = await c.GetAsync("/api/v1/reports/skill-summary?format=xlsx");
        var bytes = await xlsx.Content.ReadAsByteArrayAsync();
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Skill", wb.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/reports/nope")).StatusCode);
    }

    [Fact]
    public async Task A_Workbook_Upload_Is_Previewed_Then_Committed_Only_On_Request()
    {
        var c = await _factory.ClientAsync();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Engineers");
        ws.Cell(1, 1).Value = "Emp ID"; ws.Cell(1, 2).Value = "Full Name"; ws.Cell(1, 3).Value = "Team";
        ws.Cell(2, 1).Value = "E900"; ws.Cell(2, 2).Value = "Uploaded Person"; ws.Cell(2, 3).Value = "Upload team";
        using var ms = new MemoryStream(); wb.SaveAs(ms);

        HttpContent Form() => new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "file", "agility.xlsx" } };
        var preview = await Body(await c.PostAsync("/api/v1/imports/agility", Form()));
        Assert.False(preview.GetProperty("committed").GetBoolean());
        Assert.Equal(1, preview.GetProperty("counts").GetProperty("Person").GetProperty("added").GetInt32());
        Assert.DoesNotContain("E900", await (await c.GetAsync("/api/v1/people")).Content.ReadAsStringAsync());

        var commit = await Body(await c.PostAsync("/api/v1/imports/agility?commit=true", Form()));
        Assert.True(commit.GetProperty("committed").GetBoolean());
        Assert.Contains("E900", await (await c.GetAsync("/api/v1/people")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/v1/imports/bogus", Form())).StatusCode);
    }

    [Fact]
    public async Task Openapi_Document_Lists_The_Endpoints_Anonymously()
    {
        var doc = await Body(await _factory.CreateClient().GetAsync("/openapi/v1.json"));
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/projects", out _));
        Assert.True(paths.TryGetProperty("/api/v1/scenarios/{id}/apply", out _));
        Assert.True(paths.EnumerateObject().Count() > 60, $"only {paths.EnumerateObject().Count()} paths");
    }

    [Fact]
    public async Task Dashboard_Alerts_Diagnostics_And_Audit_Respond()
    {
        var c = await _factory.ClientAsync();
        var dashboard = await Body(await c.GetAsync("/api/v1/dashboard"));
        Assert.True(dashboard.GetProperty("kpis").TryGetProperty("activeProjects", out _));
        Assert.Equal(JsonValueKind.Array, dashboard.GetProperty("loadNext8Weeks").ValueKind);
        Assert.Equal(0, dashboard.GetProperty("loadNext8Weeks").GetArrayLength() % 8);
        Assert.Equal(JsonValueKind.Array, (await Body(await c.GetAsync("/api/v1/alerts"))).ValueKind);
        Assert.True((await Body(await c.GetAsync("/api/v1/diagnostics"))).GetProperty("counts").GetProperty("people").GetInt32() >= 0);
        var audit = await Body(await c.GetAsync("/api/v1/audit?take=5"));
        Assert.True(audit.GetArrayLength() <= 5);
        var backup = await c.PostAsync("/api/v1/backups", null);
        Assert.Equal(HttpStatusCode.OK, backup.StatusCode);
        Assert.True((await Body(backup)).GetProperty("sizeBytes").GetInt64() > 0);
    }
}
