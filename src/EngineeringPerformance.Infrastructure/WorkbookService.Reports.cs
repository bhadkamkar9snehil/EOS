using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Professionally formatted, exportable performance reports — one engineer or the whole
/// team. Kept separate from the import/template half of <see cref="WorkbookService"/>
/// because the styling helpers here are report-specific and would otherwise crowd it.
/// </summary>
public sealed partial class WorkbookService
{
    private static readonly XLColor HeaderBand = XLColor.FromHtml("#1F4E78");
    private static readonly XLColor SubHeaderBand = XLColor.FromHtml("#EEF3FA");
    private static readonly XLColor RuleColor = XLColor.FromHtml("#D7E0EC");
    private static readonly XLColor AltRow = XLColor.FromHtml("#F7FAFD");
    private static readonly XLColor Good = XLColor.FromHtml("#0A8A0A");
    private static readonly XLColor Warning = XLColor.FromHtml("#A06F00");
    private static readonly XLColor Serious = XLColor.FromHtml("#C05F30");
    private static readonly XLColor Critical = XLColor.FromHtml("#C0392B");

    private static XLColor ScoreColor(decimal score) => score switch
    {
        >= 85 => Good,
        >= 70 => XLColor.FromHtml("#1F6FCE"),
        >= 55 => Serious,
        _ => Critical
    };

    private static void Title(IXLWorksheet sheet, string text, int columns)
    {
        sheet.Cell(1, 1).Value = text;
        sheet.Range(1, 1, 1, columns).Merge().Style.Font.SetBold().Font.SetFontSize(17).Font.SetFontColor(HeaderBand);
        sheet.Row(1).Height = 28;
    }

    private static void Subtitle(IXLWorksheet sheet, string text, int columns)
    {
        sheet.Cell(2, 1).Value = text;
        sheet.Range(2, 1, 2, columns).Merge().Style.Font.SetFontColor(XLColor.FromHtml("#52514E")).Font.SetFontSize(11);
    }

    private static int SectionHeader(IXLWorksheet sheet, int row, string text, int columns)
    {
        var range = sheet.Range(row, 1, row, columns);
        range.Merge().Value = text;
        range.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(HeaderBand);
        sheet.Row(row).Height = 20;
        return row + 1;
    }

    private static int TableHeader(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++) sheet.Cell(row, i + 1).Value = headers[i];
        var range = sheet.Range(row, 1, row, headers.Length);
        range.Style.Font.SetBold().Fill.SetBackgroundColor(SubHeaderBand).Font.SetFontColor(XLColor.FromHtml("#1F3A5C"));
        range.Style.Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(HeaderBand);
        return row + 1;
    }

    private static void ZebraRow(IXLWorksheet sheet, int row, int columns, bool alt)
    {
        if (!alt) return;
        sheet.Range(row, 1, row, columns).Style.Fill.SetBackgroundColor(AltRow);
    }

    private static void KeyValue(IXLWorksheet sheet, int row, string key, string value, XLColor? valueColor = null)
    {
        sheet.Cell(row, 1).Value = key;
        sheet.Cell(row, 1).Style.Font.SetFontColor(XLColor.FromHtml("#5C6675"));
        sheet.Cell(row, 2).Value = value;
        sheet.Cell(row, 2).Style.Font.SetBold();
        if (valueColor is not null) sheet.Cell(row, 2).Style.Font.SetFontColor(valueColor);
    }

    private static void Footer(IXLWorksheet sheet, int row, int columns)
    {
        sheet.Cell(row, 1).Value = $"Generated {DateTime.Now:d MMMM yyyy 'at' h:mm tt} — Engineering Performance Analyzer";
        sheet.Range(row, 1, row, columns).Merge().Style.Font.SetFontColor(XLColor.FromHtml("#9AA3AF")).Font.SetItalic().Font.SetFontSize(9);
    }

    private static void FinishSheet(IXLWorksheet sheet, int columns, int freezeRow = 3)
    {
        sheet.Columns(1, columns).AdjustToContents();
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 22);
        sheet.SheetView.FreezeRows(freezeRow);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.Margins.SetLeft(0.4).SetRight(0.4).SetTop(0.5).SetBottom(0.5);
    }

    // ---------- Employee report ----------

    public void GenerateEmployeeReport(string destinationPath, EmployeeReportData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Summary");
        const int columns = 5;
        Title(sheet, "Engineering Performance Report", columns);
        Subtitle(sheet, $"{data.EmployeeName} ({data.EmployeeCode}) — Level {data.SeniorityLevel} — {new DateTime(data.Year, data.Month, 1):MMMM yyyy}", columns);

        var row = 4;
        row = SectionHeader(sheet, row, "Operational performance", columns);
        if (data.Current is null)
        {
            sheet.Cell(row, 1).Value = "No imported rows for this employee this month.";
            row++;
        }
        else
        {
            var c = data.Current;
            KeyValue(sheet, row++, "Operational score", c.OperationalScore.ToString("0.0"), ScoreColor(c.OperationalScore));
            KeyValue(sheet, row++, "Timesheet completion", c.HasSummaryData ? c.TimesheetCompletionScore.ToString("0") + "%" : "Not in utilization export");
            KeyValue(sheet, row++, "Approval", c.HasSummaryData ? c.ApprovalScore.ToString("0") + "%" : "Not in utilization export");
            KeyValue(sheet, row++, "Attendance discipline", c.AttendanceDisciplineScore.ToString("0") + "%");
            KeyValue(sheet, row++, "Utilization", c.HasSummaryData ? c.Utilization.ToString("0") + "%" : "—");
            KeyValue(sheet, row++, "Workload", (c.ExpectedTimesheetDays <= 0 ? 0 : c.PunchHours / c.ExpectedTimesheetDays).ToString("0.0") + " h/day");
            KeyValue(sheet, row++, "Billable hours", c.BillableHours.ToString("0.0"));
            KeyValue(sheet, row++, "Detailed entries / projects", $"{c.DetailedEntries} / {c.UniqueProjects}");
            row++;

            row = SectionHeader(sheet, row, "Category breakdown", columns);
            row = TableHeader(sheet, row, "Dimension", "Value");
            decimal Pct(decimal value, decimal denom) => denom <= 0 ? 0 : Math.Clamp(Math.Round(value / denom * 100m, 1), 0, 100);
            var dims = new (string Label, decimal Value)[]
            {
                ("Timesheet fill", c.ExpectedTimesheetDays <= 0 ? 0 : Pct(c.TimesheetFilledDays, c.ExpectedTimesheetDays)),
                ("Approval", c.ApprovalScore),
                ("Punctuality", c.ExpectedTimesheetDays <= 0 ? 0 : 100m - Pct(c.LateDays + c.EarlyDays, c.ExpectedTimesheetDays * 2m)),
                ("Punch record", c.ExpectedTimesheetDays <= 0 ? 0 : 100m - Pct(c.MissingPunchDays, c.ExpectedTimesheetDays)),
                ("Full duration", c.ExpectedTimesheetDays <= 0 ? 0 : 100m - Pct(c.LessDurationDays, c.ExpectedTimesheetDays))
            };
            for (var i = 0; i < dims.Length; i++)
            {
                ZebraRow(sheet, row, 2, i % 2 == 1);
                sheet.Cell(row, 1).Value = dims[i].Label;
                sheet.Cell(row, 2).Value = dims[i].Value / 100m;
                sheet.Cell(row, 2).Style.NumberFormat.Format = "0.0%";
                row++;
            }
            row++;
        }

        if (data.History.Count > 1)
        {
            row = SectionHeader(sheet, row, "Score history", columns);
            row = TableHeader(sheet, row, "Month", "Operational score");
            var i = 0;
            foreach (var h in data.History.OrderBy(x => x.Year).ThenBy(x => x.Month))
            {
                ZebraRow(sheet, row, 2, i++ % 2 == 1);
                sheet.Cell(row, 1).Value = new DateTime(h.Year, h.Month, 1).ToString("MMMM yyyy");
                sheet.Cell(row, 2).Value = h.OperationalScore;
                sheet.Cell(row, 2).Style.Font.SetFontColor(ScoreColor(h.OperationalScore)).Font.SetBold();
                sheet.Cell(row, 2).Style.NumberFormat.Format = "0.0";
                row++;
            }
            row++;
        }

        var received = data.PeerReviews.Where(x => string.Equals(x.SubjectCode, data.EmployeeCode, StringComparison.OrdinalIgnoreCase)
            || PersonName.Matches(x.SubjectName, data.EmployeeName)).ToArray();
        var given = data.PeerReviews.Where(x => string.Equals(x.ReviewerCode, data.EmployeeCode, StringComparison.OrdinalIgnoreCase)
            || PersonName.Matches(x.ReviewerName, data.EmployeeName)).ToArray();

        row = SectionHeader(sheet, row, "Peer feedback", columns);
        KeyValue(sheet, row++, "Received / given", $"{received.Length} / {given.Length}");
        if (received.Length > 0)
        {
            KeyValue(sheet, row++, "Average rating received", received.Average(x => x.Average).ToString("0.00"));
            KeyValue(sheet, row++, "Collaboration", Avg(received, x => x.Collaboration));
            KeyValue(sheet, row++, "Communication", Avg(received, x => x.Communication));
            KeyValue(sheet, row++, "Reliability", Avg(received, x => x.Reliability));
            KeyValue(sheet, row++, "Technical help", Avg(received, x => x.TechnicalHelp));
        }
        row++;

        Footer(sheet, row + 1, columns);
        FinishSheet(sheet, columns);

        if (received.Length > 0 || given.Length > 0)
        {
            var peerSheet = workbook.AddWorksheet("Peer feedback detail");
            var r = 1;
            if (received.Length > 0)
            {
                r = SectionHeader(peerSheet, r, "Feedback received", 6);
                r = TableHeader(peerSheet, r, "Reviewer", "Collaboration", "Communication", "Reliability", "Technical help", "Comment");
                var i = 0;
                foreach (var review in received.OrderByDescending(x => x.Average))
                {
                    ZebraRow(peerSheet, r, 6, i++ % 2 == 1);
                    peerSheet.Cell(r, 1).Value = review.ReviewerName;
                    peerSheet.Cell(r, 2).Value = review.Collaboration;
                    peerSheet.Cell(r, 3).Value = review.Communication;
                    peerSheet.Cell(r, 4).Value = review.Reliability;
                    peerSheet.Cell(r, 5).Value = review.TechnicalHelp;
                    peerSheet.Cell(r, 6).Value = review.Comment ?? string.Empty;
                    r++;
                }
                r++;
            }
            if (given.Length > 0)
            {
                r = SectionHeader(peerSheet, r, "Feedback given", 6);
                r = TableHeader(peerSheet, r, "Colleague", "Collaboration", "Communication", "Reliability", "Technical help", "Comment");
                var i = 0;
                foreach (var review in given.OrderBy(x => x.SubjectName))
                {
                    ZebraRow(peerSheet, r, 6, i++ % 2 == 1);
                    peerSheet.Cell(r, 1).Value = review.SubjectName;
                    peerSheet.Cell(r, 2).Value = review.Collaboration;
                    peerSheet.Cell(r, 3).Value = review.Communication;
                    peerSheet.Cell(r, 4).Value = review.Reliability;
                    peerSheet.Cell(r, 5).Value = review.TechnicalHelp;
                    peerSheet.Cell(r, 6).Value = review.Comment ?? string.Empty;
                    r++;
                }
            }
            peerSheet.Column(6).Width = 50;
            FinishSheet(peerSheet, 6, 1);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        workbook.SaveAs(destinationPath);
    }

    private static string Avg(IReadOnlyList<PeerReviewItem> reviews, Func<PeerReviewItem, decimal> selector)
    {
        var rated = reviews.Select(selector).Where(x => x > 0).ToArray();
        return rated.Length == 0 ? "—" : rated.Average().ToString("0.00");
    }

    // ---------- Team report ----------

    public void GenerateTeamReport(string destinationPath, TeamReportData data)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Team summary");
        const int columns = 9;
        Title(sheet, "Engineering Performance Report — Team", columns);
        Subtitle(sheet, $"{new DateTime(data.Year, data.Month, 1):MMMM yyyy}", columns);

        var row = 4;
        row = SectionHeader(sheet, row, "Team KPIs", columns);
        var teamScore = data.Items.Count == 0 ? 0 : data.Items.Average(x => x.OperationalScore);
        var withSummary = data.Items.Where(x => x.HasSummaryData).ToArray();
        KeyValue(sheet, row++, "Team operational score", teamScore.ToString("0.0"), ScoreColor(teamScore));
        KeyValue(sheet, row++, "Average timesheet completion", (withSummary.Length == 0 ? 0 : withSummary.Average(x => x.TimesheetCompletionScore)).ToString("0.0") + "%");
        KeyValue(sheet, row++, "Average attendance discipline", (data.Items.Count == 0 ? 0 : data.Items.Average(x => x.AttendanceDisciplineScore)).ToString("0.0") + "%");
        KeyValue(sheet, row++, "Engineers scored", data.Items.Count.ToString());
        row++;

        row = SectionHeader(sheet, row, "Score distribution", columns);
        row = TableHeader(sheet, row, "Band", "Range", "Engineers");
        var bands = new (string Name, string Range, int Count)[]
        {
            ("Strong", "85+", data.Items.Count(x => x.OperationalScore >= 85)),
            ("On track", "70–84", data.Items.Count(x => x.OperationalScore is >= 70 and < 85)),
            ("At risk", "55–69", data.Items.Count(x => x.OperationalScore is >= 55 and < 70)),
            ("Critical", "<55", data.Items.Count(x => x.OperationalScore < 55))
        };
        for (var i = 0; i < bands.Length; i++)
        {
            ZebraRow(sheet, row, 3, i % 2 == 1);
            sheet.Cell(row, 1).Value = bands[i].Name;
            sheet.Cell(row, 2).Value = bands[i].Range;
            sheet.Cell(row, 3).Value = bands[i].Count;
            row++;
        }
        row++;

        row = SectionHeader(sheet, row, "Engineer performance", columns);
        row = TableHeader(sheet, row, "Engineer", "Code", "Score", "Timesheet", "Approval", "Attendance", "Billable h", "Entries", "Projects");
        var ix = 0;
        foreach (var item in data.Items.OrderByDescending(x => x.OperationalScore))
        {
            ZebraRow(sheet, row, columns, ix++ % 2 == 1);
            sheet.Cell(row, 1).Value = item.EmployeeName;
            sheet.Cell(row, 2).Value = item.EmployeeCode ?? "";
            sheet.Cell(row, 3).Value = item.OperationalScore;
            sheet.Cell(row, 3).Style.Font.SetFontColor(ScoreColor(item.OperationalScore)).Font.SetBold();
            sheet.Cell(row, 3).Style.NumberFormat.Format = "0.0";
            sheet.Cell(row, 4).Value = item.HasSummaryData ? item.TimesheetCompletionScore / 100m : (double?)null;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "0%";
            sheet.Cell(row, 5).Value = item.HasSummaryData ? item.ApprovalScore / 100m : (double?)null;
            sheet.Cell(row, 5).Style.NumberFormat.Format = "0%";
            sheet.Cell(row, 6).Value = item.AttendanceDisciplineScore / 100m;
            sheet.Cell(row, 6).Style.NumberFormat.Format = "0%";
            sheet.Cell(row, 7).Value = item.BillableHours;
            sheet.Cell(row, 7).Style.NumberFormat.Format = "0.0";
            sheet.Cell(row, 8).Value = item.DetailedEntries;
            sheet.Cell(row, 9).Value = item.UniqueProjects;
            row++;
        }
        row++;

        Footer(sheet, row + 1, columns);
        FinishSheet(sheet, columns);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        workbook.SaveAs(destinationPath);
    }
}
