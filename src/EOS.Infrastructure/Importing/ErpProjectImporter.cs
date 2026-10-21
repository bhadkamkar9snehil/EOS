using System.Text.RegularExpressions;
using EOS.Domain.Projects;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed partial class ErpProjectImporter(EosDbContext db)
{
    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "erp project master", path, commit, actor, report => WorkAsync(path, report, ct), ct);

    internal async Task WorkAsync(string path, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(path);
        // The review workbook keeps the list on a sheet named ProjectList; the plain ERP export has it on its first sheet.
        var ws = wb.Sheet("ProjectList") ?? wb.Worksheets.FirstOrDefault(s => HeaderMap(s, 1).ContainsKey("project no") && HeaderMap(s, 1).ContainsKey("order amount"));
        if (ws is null) { report.Warnings.Add("No project list found (a sheet named ProjectList, or a first sheet with Project No and Order Amount columns)."); return; }
        var columns = HeaderMap(ws, 1);
        if (!columns.ContainsKey("project no")) { report.Warnings.Add("ProjectList: column 'Project No' not found."); return; }

        var projects = await db.Projects.ToDictionaryAsync(p => p.ProjectNo, StringComparer.OrdinalIgnoreCase, ct);
        var assignments = ReadAssignments(wb);
        string Get(int row, string header) => columns.TryGetValue(header, out var c) ? ws.Text(row, c) : "";
        decimal? Num(int row, string header) => columns.TryGetValue(header, out var c) ? ws.Number(row, c) : null;
        DateOnly? Dt(int row, string header) => columns.TryGetValue(header, out var c) ? ws.Date(row, c) : null;

        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var no = Get(r, "project no");
            if (no.Length == 0) continue;
            var name = Get(r, "project name");
            var margin = ParsePercent(Get(r, "oigm"));

            var isNew = !projects.TryGetValue(no, out var p);
            if (isNew)
            {
                p = new Project { ProjectNo = no, Name = name.Length == 0 ? no : name };
                db.Projects.Add(p);
                projects[no] = p;
                report.Added("Project");
            }

            if (name.Length > 0) p!.Name = name;
            p!.Customer = Nullable(Get(r, "accountname")) ?? p.Customer;
            p.ProjectType = Nullable(Get(r, "project type")) ?? p.ProjectType;
            p.ErpStatus = Nullable(Get(r, "main status")) ?? p.ErpStatus;
            p.OrderValue = Num(r, "order amount") ?? p.OrderValue;
            p.MarginPercent = margin ?? p.MarginPercent;
            p.PoStart = Dt(r, "po start date") ?? p.PoStart;
            p.PoEnd = Dt(r, "po end date") ?? p.PoEnd;
            p.ProjectManager = Nullable(Get(r, "project manager")) ?? p.ProjectManager;
            p.TechnicalManager = Nullable(Get(r, "technical manager")) ?? p.TechnicalManager;
            p.ProductLine ??= Nullable(Get(r, "business categories"));
            if (assignments.TryGetValue(no, out var a))
            {
                if (a.Product is not null) p.ProductLine = a.Product;
                if (a.Pm is not null) p.ProjectManager = a.Pm;
                if (a.Tm is not null) p.TechnicalManager = a.Tm;
            }

            if (!isNew)
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(p).State == EntityState.Modified) report.Updated("Project"); else report.Unchanged("Project");
            }
        }
        await db.SaveChangesAsync(ct);

        foreach (var p in projects.Values)
        {
            var m = VariantPrefix().Match(p.ProjectNo);
            if (!m.Success) continue;
            if (projects.TryGetValue(m.Groups["base"].Value, out var parent) && parent.Id != p.Id && p.ParentProjectId != parent.Id)
            { p.ParentProjectId = parent.Id; report.Updated("Project link"); }
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed record Assignment(string? Product, string? Pm, string? Tm);

    private static Dictionary<string, Assignment> ReadAssignments(ClosedXML.Excel.XLWorkbook wb)
    {
        var result = new Dictionary<string, Assignment>(StringComparer.OrdinalIgnoreCase);
        var ws = wb.Sheet("Sheet2");
        if (ws is null) return result;
        for (var r = 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var no = ws.Text(r, 1);
            if (no.Length == 0 || no.Equals("Project No", StringComparison.OrdinalIgnoreCase)) continue;
            result[no] = new Assignment(Nullable(ws.Text(r, 3)), Nullable(ws.Text(r, 4)), Nullable(ws.Text(r, 5)));
        }
        return result;
    }

    internal static Dictionary<string, int> HeaderMap(ClosedXML.Excel.IXLWorksheet ws, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 1; c <= ws.LastColumn(headerRow); c++)
        {
            var title = Regex.Replace(ws.Text(headerRow, c), @"\s+", " ").Trim().ToLowerInvariant();
            if (title.Length > 0) map.TryAdd(title, c);
        }
        return map;
    }

    internal static decimal? ParsePercent(string html)
    {
        var m = PercentValue().Match(html);
        return m.Success && decimal.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v / 100m : null;
    }

    private static string? Nullable(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"([-+]?\d+(?:\.\d+)?)\s*%")]
    private static partial Regex PercentValue();

    [GeneratedRegex(@"^[WV]_(?<base>.+?)(?:_\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantPrefix();
}
