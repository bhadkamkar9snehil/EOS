using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;

namespace EOS.Server.Ui.Components;

public static class GanttExport
{
    private const int RowH = 22, HeadH = 46, Pad = 24, TitleH = 64, LegendH = 44;

    private static string Esc(string s) => WebUtility.HtmlEncode(s);
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public static string FileName(string title, string ext) =>
        new string((title.Length == 0 ? "gantt" : title).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-') + $"-gantt-{DateTime.Now:yyyyMMdd}." + ext;

    private static (DateOnly Min, DateOnly Max) Range(IReadOnlyList<GanttChart.Row> rows, DateOnly? today)
    {
        var dates = rows.SelectMany(r => new[] { r.Start, r.Finish, r.BaseStart, r.BaseFinish }).Where(d => d is not null).Select(d => d!.Value).ToList();
        if (today is { } t) dates.Add(t);
        if (dates.Count == 0) { var d = DateOnly.FromDateTime(DateTime.Today); return (d, d.AddDays(30)); }
        return (dates.Min().AddDays(-3), dates.Max().AddDays(10));
    }

    private static bool IsLate(GanttChart.Row r, DateOnly? today) => today is { } t && r.Finish is { } f && f < t && r.Progress < 1m && !r.IsMilestone;

    private static string HealthColour(string? h) => h switch { "At Risk" => "#c9302c", "Moderate Risk" => "#9a6200", "On Track" => "#17704a", _ => "#5a626d" };

    public static string BuildSvg(IReadOnlyList<GanttChart.Row> rows, IReadOnlyList<GanttChart.Link> links, DateOnly? today, string title, bool showBaseline, bool showLinks, out int width, out int height)
    {
        var (min, max) = Range(rows, today);
        var span = max.DayNumber - min.DayNumber + 1;
        var dw = Math.Clamp(1900.0 / span, 4.0, 24.0);
        var labelW = (int)Math.Clamp(rows.Select(r => r.Level * 14 + r.Label.Length * 6.2).DefaultIfEmpty(200).Max() + 40, 260, 620);
        var timeW = (int)Math.Ceiling(span * dw);
        width = Pad * 2 + labelW + timeW;
        height = TitleH + HeadH + rows.Count * RowH + LegendH + Pad;
        double X(DateOnly d) => labelW + Pad + (d.DayNumber - min.DayNumber) * dw;
        var x0 = Pad + labelW;
        var top = TitleH + HeadH;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        sb.Append("<defs><marker id=\"ar\" viewBox=\"0 0 8 8\" refX=\"7\" refY=\"4\" markerWidth=\"6\" markerHeight=\"6\" orient=\"auto\"><path d=\"M0,0 L8,4 L0,8 z\" fill=\"#5a626d\"/></marker></defs>");
        sb.Append($"<rect width=\"{width}\" height=\"{height}\" fill=\"#ffffff\"/>");
        sb.Append($"<text x=\"{Pad}\" y=\"34\" font-size=\"20\" font-weight=\"700\" fill=\"#14171a\">{Esc(title)}</text>");
        var first = rows.Where(r => r.Start is not null).Select(r => r.Start!.Value).DefaultIfEmpty().Min();
        var last = rows.Where(r => r.Finish is not null).Select(r => r.Finish!.Value).DefaultIfEmpty().Max();
        sb.Append($"<text x=\"{Pad}\" y=\"54\" font-size=\"12\" fill=\"#5a626d\">{rows.Count} rows · {first:dd MMM yyyy} to {last:dd MMM yyyy}{(today is { } td ? $" · as of {td:dd MMM yyyy}" : "")}</text>");

        // header
        sb.Append($"<rect x=\"{Pad}\" y=\"{TitleH}\" width=\"{labelW + timeW}\" height=\"{HeadH}\" fill=\"#f3f4f7\"/>");
        sb.Append($"<text x=\"{Pad + 10}\" y=\"{TitleH + HeadH - 10}\" font-size=\"12\" fill=\"#5a626d\">Task</text>");
        // weekends and week lines
        for (var d = min; d <= max; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday && dw >= 5)
                sb.Append($"<rect x=\"{N(X(d))}\" y=\"{top}\" width=\"{N(dw)}\" height=\"{rows.Count * RowH}\" fill=\"#f6f7f9\"/>");
        }
        for (var d = min; d <= max; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Monday)
            {
                sb.Append($"<line x1=\"{N(X(d))}\" x2=\"{N(X(d))}\" y1=\"{top - 14}\" y2=\"{top + rows.Count * RowH}\" stroke=\"#e6e9ee\"/>");
                if (dw >= 7) sb.Append($"<text x=\"{N(X(d) + 3)}\" y=\"{top - 4}\" font-size=\"10\" fill=\"#5a626d\">{d.Day}</text>");
            }
        }
        for (var m = new DateOnly(min.Year, min.Month, 1); m <= max; m = m.AddMonths(1))
        {
            var mx = Math.Max(x0, X(m));
            sb.Append($"<line x1=\"{N(mx)}\" x2=\"{N(mx)}\" y1=\"{TitleH}\" y2=\"{top + rows.Count * RowH}\" stroke=\"#c9ced6\"/>");
            sb.Append($"<text x=\"{N(mx + 5)}\" y=\"{TitleH + 15}\" font-size=\"12\" font-weight=\"600\" fill=\"#14171a\">{m.ToString("MMM yyyy", CultureInfo.InvariantCulture)}</text>");
        }

        var index = new Dictionary<string, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var y = top + i * RowH;
            if (r.Key is not null) index[r.Key] = i;
            if (r.Health is not null) sb.Append($"<rect x=\"{Pad}\" y=\"{y}\" width=\"{labelW + timeW}\" height=\"{RowH}\" fill=\"#f4f5f8\"/>");
            sb.Append($"<line x1=\"{Pad}\" x2=\"{Pad + labelW + timeW}\" y1=\"{y + RowH}\" y2=\"{y + RowH}\" stroke=\"#eceef2\"/>");
            var weight = r.IsSummary ? " font-weight=\"700\"" : "";
            sb.Append($"<text x=\"{Pad + 10 + r.Level * 14}\" y=\"{y + 15}\" font-size=\"12\" fill=\"#14171a\"{weight}>{Esc(Clip(r.Label, (int)((labelW - r.Level * 14 - 20) / 6.2)))}</text>");
            if (r.Start is not { } s || r.Finish is not { } f) continue;
            var x = X(s); var w = Math.Max(dw, X(f) - x + dw);
            if (showBaseline && r.BaseStart is { } bs && r.BaseFinish is { } bf)
                sb.Append($"<rect x=\"{N(X(bs))}\" y=\"{y + RowH - 5}\" width=\"{N(Math.Max(2, X(bf) - X(bs) + dw))}\" height=\"3\" fill=\"#8a929d\" opacity=\"0.8\"/>");
            if (r.IsMilestone)
                sb.Append($"<polygon points=\"{N(x + dw / 2)},{y + 4} {N(x + dw / 2 + 7)},{y + 11} {N(x + dw / 2)},{y + 18} {N(x + dw / 2 - 7)},{y + 11}\" fill=\"#5a626d\"/>");
            else if (r.Health is not null)
            {
                var c = HealthColour(r.Health);
                sb.Append($"<rect x=\"{N(x)}\" y=\"{y + 4}\" width=\"{N(w)}\" height=\"14\" rx=\"3\" fill=\"{c}\" fill-opacity=\"0.25\" stroke=\"{c}\"/>");
                if (r.Progress > 0) sb.Append($"<rect x=\"{N(x)}\" y=\"{y + 4}\" width=\"{N(Math.Max(3, w * (double)Math.Min(1m, r.Progress)))}\" height=\"14\" rx=\"3\" fill=\"{c}\"/>");
            }
            else if (r.IsSummary)
            {
                var c = IsLate(r, today) ? "#9a6200" : "#5a626d";
                sb.Append($"<rect x=\"{N(x)}\" y=\"{y + 6}\" width=\"{N(w)}\" height=\"6\" fill=\"{c}\"/>");
                sb.Append($"<polygon points=\"{N(x)},{y + 12} {N(x + 6)},{y + 12} {N(x)},{y + 18}\" fill=\"{c}\"/><polygon points=\"{N(x + w)},{y + 12} {N(x + w - 6)},{y + 12} {N(x + w)},{y + 18}\" fill=\"{c}\"/>");
            }
            else
            {
                var fill = r.Critical ? "#f2a29c" : r.Progress >= 1m ? "#cfe8d9" : IsLate(r, today) ? "#e8cd9a" : "#c3cbd9";
                var prog = r.Critical ? "#d9534f" : "#8d9bb4";
                sb.Append($"<rect x=\"{N(x)}\" y=\"{y + 4}\" width=\"{N(w)}\" height=\"14\" rx=\"3\" fill=\"{fill}\"/>");
                if (r.Progress > 0 && r.Progress < 1) sb.Append($"<rect x=\"{N(x)}\" y=\"{y + 4}\" width=\"{N(w * (double)r.Progress)}\" height=\"14\" rx=\"3\" fill=\"{prog}\"/>");
            }
            if (!r.IsMilestone)
            {
                var text = Clip(r.Meta ?? r.Label, 44);
                var estimate = text.Length * 5.6;
                var left = x + w + 6 + estimate > labelW + Pad + timeW && x - 6 - estimate > x0;
                sb.Append($"<text x=\"{N(left ? x - 6 : x + w + 6)}\" y=\"{y + 15}\" font-size=\"10.5\" fill=\"#5a626d\" text-anchor=\"{(left ? "end" : "start")}\" stroke=\"#ffffff\" stroke-width=\"3\" paint-order=\"stroke\" stroke-linejoin=\"round\">{Esc(text)}</text>");
            }
        }

        if (showLinks)
        {
            foreach (var l in links)
            {
                if (!index.TryGetValue(l.From, out var a) || !index.TryGetValue(l.To, out var b)) continue;
                var ra = rows[a]; var rb = rows[b];
                if (ra.Finish is null || rb.Start is null) continue;
                var x1 = X(ra.Finish.Value) + dw; var y1 = top + a * RowH + RowH / 2;
                var x2 = X(rb.Start.Value); var y2 = top + b * RowH + RowH / 2;
                var mid = Math.Max(x1 + 6, x2 - 8);
                sb.Append($"<path d=\"M{N(x1)},{y1} L{N(mid)},{y1} L{N(mid)},{y2} L{N(x2)},{y2}\" fill=\"none\" stroke=\"#5a626d\" stroke-width=\"1\" marker-end=\"url(#ar)\"/>");
            }
        }
        if (today is { } t && t >= min && t <= max)
        {
            var tx = X(t) + dw / 2;
            sb.Append($"<line x1=\"{N(tx)}\" x2=\"{N(tx)}\" y1=\"{top - 10}\" y2=\"{top + rows.Count * RowH}\" stroke=\"#c9302c\" stroke-width=\"1.5\"/>");
            sb.Append($"<rect x=\"{N(tx - 22)}\" y=\"{TitleH + 18}\" width=\"44\" height=\"14\" rx=\"7\" fill=\"#c9302c\"/><text x=\"{N(tx)}\" y=\"{TitleH + 28}\" font-size=\"10\" font-weight=\"700\" fill=\"#ffffff\" text-anchor=\"middle\">today</text>");
        }

        // legend
        var ly = top + rows.Count * RowH + 26;
        var lx = Pad;
        foreach (var (colour, label) in new[] { ("#c3cbd9", "task"), ("#f2a29c", "critical"), ("#cfe8d9", "complete"), ("#e8cd9a", "overdue"), ("#5a626d", "summary or milestone"), ("#8a929d", "baseline") })
        {
            sb.Append($"<rect x=\"{lx}\" y=\"{ly - 9}\" width=\"14\" height=\"9\" rx=\"2\" fill=\"{colour}\"/><text x=\"{lx + 20}\" y=\"{ly}\" font-size=\"11\" fill=\"#5a626d\">{label}</text>");
            lx += 40 + label.Length * 6;
        }
        sb.Append($"<text x=\"{width - Pad}\" y=\"{ly}\" font-size=\"10\" fill=\"#8a929d\" text-anchor=\"end\">EOS export {DateTime.Now:dd MMM yyyy HH:mm}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    public static byte[] BuildXlsx(IReadOnlyList<GanttChart.Row> rows, IReadOnlyList<GanttChart.Link> links, DateOnly? today, string title)
    {
        var (min, max) = Range(rows, today);
        var monday = min.AddDays(-(((int)min.DayOfWeek + 6) % 7));
        var weeks = new List<DateOnly>();
        for (var d = monday; d <= max; d = d.AddDays(7)) weeks.Add(d);
        const int firstWeekCol = 9;
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Gantt");
        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Value = $"{rows.Count} rows" + (today is { } td ? $" · as of {td:dd MMM yyyy}" : "") + $" · exported {DateTime.Now:dd MMM yyyy HH:mm}";
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#5a626d");
        var headers = new[] { "Task", "Start", "Finish", "% done", "Critical", "Status", "Baseline start", "Baseline finish" };
        for (var c = 0; c < headers.Length; c++) ws.Cell(5, c + 1).Value = headers[c];
        var head = ws.Range(4, 1, 5, firstWeekCol + weeks.Count - 1);
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#f3f4f7"); head.Style.Font.Bold = true;
        for (var i = 0; i < weeks.Count; i++)
        {
            var col = firstWeekCol + i;
            if (i == 0 || weeks[i].Month != weeks[i - 1].Month) ws.Cell(4, col).Value = weeks[i].ToString("MMM yy", CultureInfo.InvariantCulture);
            ws.Cell(5, col).Value = weeks[i].Day;
            ws.Cell(5, col).Style.Font.FontSize = 8; ws.Cell(5, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(col).Width = 3.4;
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = 6 + i;
            ws.Cell(row, 1).Value = r.Label;
            ws.Cell(row, 1).Style.Alignment.Indent = Math.Min(15, r.Level);
            if (r.IsSummary) { ws.Cell(row, 1).Style.Font.Bold = true; ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#f4f5f8"); }
            if (r.Start is { } s) { ws.Cell(row, 2).Value = s.ToDateTime(TimeOnly.MinValue); ws.Cell(row, 2).Style.DateFormat.Format = "dd-mmm-yy"; }
            if (r.Finish is { } f) { ws.Cell(row, 3).Value = f.ToDateTime(TimeOnly.MinValue); ws.Cell(row, 3).Style.DateFormat.Format = "dd-mmm-yy"; }
            ws.Cell(row, 4).Value = (double)r.Progress; ws.Cell(row, 4).Style.NumberFormat.Format = "0%";
            if (r.Critical) ws.Cell(row, 5).Value = "yes";
            var status = r.Health ?? r.Status ?? (r.Progress >= 1m ? "Completed" : IsLate(r, today) ? "Overdue" : "");
            if (status.Length > 0) ws.Cell(row, 6).Value = status;
            if (r.BaseStart is { } bs) { ws.Cell(row, 7).Value = bs.ToDateTime(TimeOnly.MinValue); ws.Cell(row, 7).Style.DateFormat.Format = "dd-mmm-yy"; }
            if (r.BaseFinish is { } bf) { ws.Cell(row, 8).Value = bf.ToDateTime(TimeOnly.MinValue); ws.Cell(row, 8).Style.DateFormat.Format = "dd-mmm-yy"; }
            if (r.Level > 0) ws.Row(row).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (r.Start is not { } rs || r.Finish is not { } rf) continue;
            var colour = r.IsMilestone ? "#5a626d" : r.Health is not null ? HealthColour(r.Health) : r.IsSummary ? "#5a626d" : r.Critical ? "#f2a29c" : r.Progress >= 1m ? "#cfe8d9" : IsLate(r, today) ? "#e8cd9a" : "#c3cbd9";
            for (var w = 0; w < weeks.Count; w++)
            {
                var ws0 = weeks[w]; var we = ws0.AddDays(6);
                if (rf < ws0 || rs > we) continue;
                ws.Cell(row, firstWeekCol + w).Style.Fill.BackgroundColor = XLColor.FromHtml(colour);
            }
        }
        if (today is { } t)
        {
            var wi = weeks.FindIndex(w => t >= w && t <= w.AddDays(6));
            if (wi >= 0)
            {
                var col = ws.Range(5, firstWeekCol + wi, 5 + rows.Count, firstWeekCol + wi);
                col.Style.Border.LeftBorder = XLBorderStyleValues.Medium; col.Style.Border.LeftBorderColor = XLColor.FromHtml("#c9302c");
                col.Style.Border.RightBorder = XLBorderStyleValues.Medium; col.Style.Border.RightBorderColor = XLColor.FromHtml("#c9302c");
            }
        }
        ws.Column(1).Width = Math.Clamp(rows.Select(r => r.Level * 2 + r.Label.Length).DefaultIfEmpty(30).Max() * 0.95, 30, 80);
        ws.Columns(2, 3).Width = 11; ws.Column(4).Width = 8; ws.Column(5).Width = 8; ws.Column(6).Width = 14; ws.Columns(7, 8).Width = 11;
        ws.SheetView.FreezeRows(5); ws.SheetView.FreezeColumns(3);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PagesWide = 1; ws.PageSetup.PagesTall = 0;
        ws.PageSetup.SetRowsToRepeatAtTop(4, 5);

        var legend = wb.AddWorksheet("Legend");
        var items = new[] { ("#c3cbd9", "Task"), ("#f2a29c", "Critical task"), ("#cfe8d9", "Complete"), ("#e8cd9a", "Overdue (finish before the as-of date, not complete)"), ("#5a626d", "Summary or milestone"), ("#c9302c", "Project at risk"), ("#9a6200", "Project at moderate risk"), ("#17704a", "Project on track") };
        for (var i = 0; i < items.Length; i++) { legend.Cell(i + 1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(items[i].Item1); legend.Cell(i + 1, 2).Value = items[i].Item2; }
        legend.Column(1).Width = 4; legend.Column(2).Width = 60;
        legend.Cell(items.Length + 2, 2).Value = "Each coloured cell in the Gantt sheet is one week (Monday to Sunday); the red-edged column is the week of the as-of date.";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
