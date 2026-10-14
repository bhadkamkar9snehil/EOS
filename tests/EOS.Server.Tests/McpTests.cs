using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EOS.Server.Tests;

public sealed class McpTests : IClassFixture<EosFactory>
{
    private readonly EosFactory _factory;
    public McpTests(EosFactory factory) => _factory = factory;

    private static async Task<JsonElement> Call(HttpClient c, object request)
    {
        var r = await c.PostAsJsonAsync("/mcp", request);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Mcp_Needs_A_Token()
    {
        await _factory.ClientAsync();                      // once a token exists, anonymous calls are refused
        var r = await _factory.CreateClient().PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Initialize_Lists_Tools_And_Never_Exposes_Skill_Rating()
    {
        var c = await _factory.ClientAsync();
        var init = await Call(c, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { } });
        Assert.Equal("eos", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        var list = await Call(c, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var names = list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("dashboard", names);
        Assert.Contains("scenario_apply", names);
        Assert.DoesNotContain(names, n => n!.Contains("rate") || n.Contains("skill"));
    }

    [Fact]
    public async Task A_Read_Tool_Returns_Text_Content_And_Bad_Arguments_Are_Tool_Errors()
    {
        var c = await _factory.ClientAsync();
        var ok = await Call(c, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "dashboard", arguments = new { } } });
        Assert.False(ok.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("kpis", ok.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

        var bad = await Call(c, new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "get_plan", arguments = new { projectNo = "NOPE" } } });
        Assert.True(bad.GetProperty("result").GetProperty("isError").GetBoolean());

        var unknown = await Call(c, new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "nope" } });
        Assert.Equal(-32602, unknown.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Notifications_Get_No_Reply()
    {
        var c = await _factory.ClientAsync();
        var r = await c.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", method = "notifications/initialized" });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
    }
}
