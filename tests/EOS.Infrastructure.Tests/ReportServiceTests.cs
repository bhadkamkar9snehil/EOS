using EOS.Domain.Capacity;
using EOS.Domain.Hosting;
using EOS.Domain.People;
using EOS.Domain.Projects;
using EOS.Domain.Skills;
using EOS.Infrastructure.Capacity;
using EOS.Infrastructure.Persistence;
using EOS.Infrastructure.Planning;
using EOS.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Tests;

public sealed class ReportServiceTests : IDisposable
{
    private readonly EosDbContext _db;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private static DateOnly D(int m, int d) => new(2026, m, d);
    private static readonly ReportParameters P = new(D(10, 14));

    public ReportServiceTests()
    {
        (_db, _connection) = EosDatabase.InMemory();
        Seed().GetAwaiter().GetResult();
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task Seed()
    {
        var anna = new Person { Code = "E1", FullName = "Anna, \"Lead\"" };      // a name that needs CSV escaping
        var sql = new Skill { Code = "SQL", Name = "SQL" };
        var project = new Project { ProjectNo = "PRO1", Name = "Project One", PoStart = D(10, 5), PlannedEnd = D(11, 30), TrackerStatus = "Under Development", Customer = "Alpha" };
        _db.AddRange(anna, sql, project);
        await _db.SaveChangesAsync();
        _db.PersonSkills.Add(new PersonSkill { PersonId = anna.Id, SkillId = sql.Id, Rating = 4 });
        _db.Demands.Add(new Demand { ProjectId = project.Id, SkillId = sql.Id, MinLevel = 3, HoursPerWeek = 20, StartDate = D(10, 1), EndDate = D(12, 31) });
        _db.Opportunities.Add(new Opportunity { Code = "UP1", Name = "Next", ExpectedHours = 100, WinProbability = 0.5m, RequiredSkills = "SQL", LikelyBookingMonth = D(11, 1), DurationMonths = 1 });
        _db.Hosts.AddRange(new Host { Kind = "physical", Ip = "10.0.0.1", Name = "Base" }, new Host { Kind = "vm", Name = "VM1", State = "Off", Purpose = "Unknown" });
        _db.AmcTickets.Add(new AmcTicket { Issue = "Report not showing", ProjectNo = "A_1", RaisedOn = D(10, 1), InAmc = true });
        await _db.SaveChangesAsync();
        var plans = new PlanService(_db);
        var t = await plans.AddTaskAsync(project.Id, "Build", "t", workHours: 40);
        await plans.AssignAsync(t.Id, anna.Id, 1m, "t");
        await plans.RecomputeAsync(project.Id, D(10, 5));
    }

    [Fact]
    public async Task Every_Catalogue_Report_Runs_And_Every_Row_Matches_The_Column_Count()
    {
        var svc = new ReportService(_db);
        foreach (var def in ReportService.Catalogue)
        {
            var table = await svc.RunAsync(def.Id, P);
            Assert.Equal(def.Name, table.Name);
            Assert.NotEmpty(table.Columns);
            Assert.All(table.Rows, r => Assert.Equal(table.Columns.Count, r.Count));
        }
        Assert.Equal(ReportService.Catalogue.Count, ReportService.Catalogue.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Reports_Contain_The_Data_They_Promise()
    {
        var svc = new ReportService(_db);
        Assert.Equal("PRO1", (await svc.RunAsync("portfolio-status", P)).Rows.Single()[0]);
        var matrix = await svc.RunAsync("skill-matrix", P);
        Assert.Equal(["Engineer", "Team", "SQL", "Average", "Skills at deployable level"], matrix.Columns);
        Assert.Equal((4, 4.00m, 1), (matrix.Rows.Single()[2], matrix.Rows.Single()[3], matrix.Rows.Single()[4]));
        var summary = (await svc.RunAsync("skill-summary", P)).Rows.Single();
        Assert.Equal(("SQL", 1, 1, 1), ((string)summary[0]!, (int)summary[1]!, (int)summary[2]!, (int)summary[3]!));
        Assert.Single((await svc.RunAsync("demand-register", P)).Rows);
        Assert.Equal("VM1", (await svc.RunAsync("reclaim", P)).Rows.Single()[2]);
        Assert.Equal(3, (await svc.RunAsync("supply-demand", P with { AsOf = D(10, 1), Months = 3 })).Rows.Count);   // one skill x 3 months
        Assert.Single((await svc.RunAsync("tickets", P)).Rows);
        Assert.Single((await svc.RunAsync("pipeline", P)).Rows);
    }

    [Fact]
    public async Task Csv_Escapes_Commas_And_Quotes_And_Xlsx_Is_A_Workbook()
    {
        var table = await new ReportService(_db).RunAsync("capacity", P);
        var csv = ReportService.ToCsv(table);
        Assert.Contains("\"Anna, \"\"Lead\"\"\"", csv);                          // RFC 4180 escaping
        Assert.StartsWith("Engineer,Team,Month", csv);

        var bytes = ReportService.ToXlsx(table);
        Assert.Equal((byte)'P', bytes[0]);                                       // zip header "PK"
        Assert.Equal((byte)'K', bytes[1]);
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Engineer", wb.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal(table.Rows.Count + 1, wb.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Unknown_Report_Is_Rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ReportService(_db).RunAsync("nope", P));
    }
}
