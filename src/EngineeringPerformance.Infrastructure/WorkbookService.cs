using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Infrastructure;

public sealed class WorkbookService : IWorkbookService
{
    public WorkbookInspection Inspect(string filePath)
    {
        var file = new FileInfo(filePath);
        using var workbook = new XLWorkbook(filePath);
        return new WorkbookInspection(file.Name, workbook.Worksheets.Select(x => x.Name).ToArray(), file.Length);
    }

    public ReportType DetectReportType(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var candidates = Enumerable.Range(1, Math.Min(10, sheet.LastRowUsed()?.RowNumber() ?? 1))
            .Select(row => Enumerable.Range(1, Math.Min(40, sheet.LastColumnUsed()?.ColumnNumber() ?? 1))
                .Select(column => Text(sheet.Cell(row, column))).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
        if (candidates.Any(x => x.Contains("Punch Duration") && x.Contains("UAA Status"))) return ReportType.AttendanceLeaveUaaTimesheet;
        if (candidates.Any(x => x.Contains("Total Month Hours") && x.Contains("Utilization"))) return ReportType.MonthlyTimesheetSummary;
        if (candidates.Any(x => x.Contains("Project No") && x.Contains("Total work Hours"))) return ReportType.DetailedTimesheetTransactions;
        if (workbook.Worksheets.Any(x => x.Name.Equals("Template Metadata", StringComparison.OrdinalIgnoreCase))) return ReportType.EngineerReviewWorkbook;
        throw new InvalidDataException("The workbook columns do not match any supported report.");
    }

    public IReadOnlyList<EmployeeMonthlyPerformance> ReadPerformance(string filePath, ReportType reportType, int year, int month)
    {
        if (reportType == ReportType.EngineerReviewWorkbook) return [];
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var headerRow = FindHeaderRow(sheet, reportType);
        var columns = sheet.Row(headerRow).CellsUsed().ToDictionary(x => Text(x), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        return reportType switch
        {
            ReportType.MonthlyTimesheetSummary => ReadSummary(sheet, headerRow, columns, year, month),
            ReportType.DetailedTimesheetTransactions => ReadDetails(sheet, headerRow, columns, year, month),
            ReportType.AttendanceLeaveUaaTimesheet => ReadAttendance(sheet, headerRow, columns, year, month),
            _ => []
        };
    }

    private static IReadOnlyList<EmployeeMonthlyPerformance> ReadSummary(IXLWorksheet sheet, int headerRow, Dictionary<string, int> columns, int year, int month)
    {
        var results = new List<EmployeeMonthlyPerformance>();
        for (var row = headerRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? headerRow); row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee Name"))));
            if (string.IsNullOrWhiteSpace(name) || name.Equals("No data available", StringComparison.OrdinalIgnoreCase)) continue;
            var item = New(name, year, month);
            item.ComplianceHours = Number(sheet.Cell(row, Col(columns, "Timsheet Compliance hours")));
            item.EnteredHours = Number(sheet.Cell(row, Col(columns, "Total\nEntered Timesheet Hours")));
            item.ApprovedHours = Number(sheet.Cell(row, Col(columns, "Approved Timesheet Hours")));
            item.BillableHours = Number(sheet.Cell(row, Col(columns, "Billable Hours")));
            item.NonBillableHours = Number(sheet.Cell(row, Col(columns, "Non Billable Hours")));
            item.TrainingHours = Number(sheet.Cell(row, Col(columns, "Sum of Training")));
            item.OfficeHours = Number(sheet.Cell(row, Col(columns, "Sum of Office Working Hours")));
            Calculate(item);
            results.Add(item);
        }
        return results;
    }

    private static IReadOnlyList<EmployeeMonthlyPerformance> ReadDetails(IXLWorksheet sheet, int headerRow, Dictionary<string, int> columns, int year, int month)
    {
        var groups = new Dictionary<string, (EmployeeMonthlyPerformance Item, HashSet<string> Projects)>(StringComparer.OrdinalIgnoreCase);
        for (var row = headerRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? headerRow); row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!groups.TryGetValue(name, out var group)) group = (New(name, year, month), new(StringComparer.OrdinalIgnoreCase));
            group.Item.DetailedEntries++;
            group.Item.DetailedHours += Number(sheet.Cell(row, Col(columns, "Total work Hours")));
            var project = Text(sheet.Cell(row, Col(columns, "Project No")));
            if (!string.IsNullOrWhiteSpace(project)) group.Projects.Add(project);
            groups[name] = group;
        }
        foreach (var group in groups.Values) { group.Item.UniqueProjects = group.Projects.Count; Calculate(group.Item); }
        return groups.Values.Select(x => x.Item).ToArray();
    }

    private static IReadOnlyList<EmployeeMonthlyPerformance> ReadAttendance(IXLWorksheet sheet, int headerRow, Dictionary<string, int> columns, int year, int month)
    {
        var groups = new Dictionary<string, EmployeeMonthlyPerformance>(StringComparer.OrdinalIgnoreCase);
        for (var row = headerRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? headerRow); row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!groups.TryGetValue(name, out var item)) groups[name] = item = New(name, year, month);
            item.EmployeeCode ??= Text(sheet.Cell(row, Col(columns, "Emp No")));
            var attendDay = Number(sheet.Cell(row, Col(columns, "Attend Day")));
            var position = Text(sheet.Cell(row, Col(columns, "Position")));
            var duty = Text(sheet.Cell(row, Col(columns, "Duty Type")));
            var approvedLeave = Text(sheet.Cell(row, Col(columns, "Leave status"))).Equals("Approved", StringComparison.OrdinalIgnoreCase);
            if (approvedLeave || position.Equals("Leave", StringComparison.OrdinalIgnoreCase)) item.LeaveDays += attendDay;
            var accountable = attendDay > 0 && !duty.Equals("woff", StringComparison.OrdinalIgnoreCase) && !position.Equals("Leave", StringComparison.OrdinalIgnoreCase);
            if (!accountable) continue;
            item.AttendanceDays += attendDay;
            item.ExpectedTimesheetDays++;
            item.PunchHours += Number(sheet.Cell(row, Col(columns, "Punch Duration")));
            item.AttendanceTimesheetHours += Number(sheet.Cell(row, Col(columns, "Timesheet Hrs")));
            if (Text(sheet.Cell(row, Col(columns, "Timesheet"))).Equals("Filled", StringComparison.OrdinalIgnoreCase)) item.TimesheetFilledDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Punch not Found")))) item.MissingPunchDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Late Coming")))) item.LateDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Early Going")))) item.EarlyDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Less Duration")))) item.LessDurationDays++;
        }
        foreach (var item in groups.Values) Calculate(item);
        return groups.Values.ToArray();
    }

    private static EmployeeMonthlyPerformance New(string name, int year, int month) =>
        new() { EmployeeName = PersonName.Normalize(name), Year = year, Month = month };
    private static void Calculate(EmployeeMonthlyPerformance item) => item.Recalculate();
    private static int FindHeaderRow(IXLWorksheet sheet, ReportType type)
    {
        var marker = type switch { ReportType.MonthlyTimesheetSummary => "Employee Name", ReportType.DetailedTimesheetTransactions => "Employee", _ => "Date" };
        for (var row = 1; row <= Math.Min(10, sheet.LastRowUsed()?.RowNumber() ?? 1); row++)
            if (sheet.Row(row).CellsUsed().Any(x => Text(x).Equals(marker, StringComparison.OrdinalIgnoreCase))) return row;
        throw new InvalidDataException($"Required header '{marker}' was not found.");
    }
    private static int Col(Dictionary<string, int> columns, string name) => columns.TryGetValue(name, out var column) ? column : throw new InvalidDataException($"Required column '{name}' was not found.");
    private static string Text(IXLCell cell) => cell.GetFormattedString().Trim();
    private static decimal Number(IXLCell cell) => cell.TryGetValue<decimal>(out var value) ? value : decimal.TryParse(Text(cell), out value) ? value : 0;
    private static bool Flag(IXLCell cell) => cell.TryGetValue<bool>(out var value) ? value : Text(cell).Equals("true", StringComparison.OrdinalIgnoreCase) || Text(cell) == "1";

    /// <summary>Column layout of the peer review sheet, shared by the writer and the reader.</summary>
    private const string PeerSheetName = "Peer Review";
    private const int PeerHeaderRow = 6;
    private static readonly string[] PeerHeaders =
        ["Peer Code", "Peer Name", "Collaboration (1-5)", "Communication (1-5)", "Reliability (1-5)", "Technical Help (1-5)", "Comment"];

    public IReadOnlyList<PeerReview> ReadPeerReviews(string filePath, int year, int month)
    {
        using var workbook = new XLWorkbook(filePath);
        if (!workbook.Worksheets.TryGetWorksheet("Template Metadata", out var metadata)) return [];
        if (!workbook.Worksheets.TryGetWorksheet(PeerSheetName, out var sheet)) return [];

        var reviewerCode = MetadataValue(metadata, "EmployeeCode");
        var reviewerName = MetadataValue(metadata, "EmployeeName");
        if (string.IsNullOrWhiteSpace(reviewerCode)) return [];

        var results = new List<PeerReview>();
        for (var row = PeerHeaderRow + 1; row <= (sheet.LastRowUsed()?.RowNumber() ?? PeerHeaderRow); row++)
        {
            var subjectCode = Text(sheet.Cell(row, 1));
            var subjectName = PersonName.Normalize(Text(sheet.Cell(row, 2)));
            if (string.IsNullOrWhiteSpace(subjectCode) && string.IsNullOrWhiteSpace(subjectName)) continue;

            var review = new PeerReview
            {
                Year = year,
                Month = month,
                ReviewerCode = reviewerCode,
                ReviewerName = PersonName.Normalize(reviewerName),
                SubjectCode = subjectCode,
                SubjectName = subjectName,
                Collaboration = Rating(sheet.Cell(row, 3)),
                Communication = Rating(sheet.Cell(row, 4)),
                Reliability = Rating(sheet.Cell(row, 5)),
                TechnicalHelp = Rating(sheet.Cell(row, 6)),
                Comment = Text(sheet.Cell(row, 7)) is { Length: > 0 } comment ? comment : null
            };
            // A row the reviewer left blank is not feedback.
            if (review.HasAnyRating) results.Add(review);
        }
        return results;
    }

    private static string MetadataValue(IXLWorksheet metadata, string key)
    {
        for (var row = 1; row <= (metadata.LastRowUsed()?.RowNumber() ?? 0); row++)
            if (Text(metadata.Cell(row, 1)).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Text(metadata.Cell(row, 2));
        return string.Empty;
    }

    /// <summary>Ratings outside 1-5 are treated as not given rather than clamped.</summary>
    private static decimal Rating(IXLCell cell)
    {
        var value = Number(cell);
        return value is >= 1 and <= 5 ? value : 0;
    }

    public void GenerateEngineerTemplate(string destinationPath, Employee employee, int year, int month, IReadOnlyList<Employee>? peers = null)
    {
        using var workbook = new XLWorkbook();
        var review = workbook.AddWorksheet("Self Review");
        review.Cell("A1").Value = "Engineering Performance Monthly Review";
        review.Cell("A3").Value = "Employee Code"; review.Cell("B3").Value = employee.EmployeeCode;
        review.Cell("A4").Value = "Employee"; review.Cell("B4").Value = employee.Name;
        review.Cell("A5").Value = "Seniority Level"; review.Cell("B5").Value = employee.SeniorityLevel;
        review.Cell("A6").Value = "Reporting Month"; review.Cell("B6").Value = new DateTime(year, month, 1);
        review.Cell("B6").Style.DateFormat.Format = "mmm yyyy";
        string[] headers = ["Metric", "Rating (1-5)", "Count", "Reason Code", "Optional Evidence"];
        for (var i = 0; i < headers.Length; i++) review.Cell(8, i + 1).Value = headers[i];
        string[] metrics = ["Ownership", "Commitment Reliability", "Communication", "Work Quality", "Documentation", "Collaboration", "Independence", "Learning Applied", "Improvement Contribution"];
        for (var i = 0; i < metrics.Length; i++) review.Cell(9 + i, 1).Value = metrics[i];
        review.Range("A1:E1").Merge().Style.Font.SetBold().Font.SetFontSize(16);
        review.Range("A8:E8").Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#1F4E78"));
        review.Range("A8:E8").Style.Font.SetFontColor(XLColor.White);
        review.Columns().AdjustToContents();

        WritePeerSheet(workbook, employee, year, month, peers);

        var metadata = workbook.AddWorksheet("Template Metadata");
        metadata.Cell("A1").Value = "SchemaVersion"; metadata.Cell("B1").Value = 2;
        metadata.Cell("A2").Value = "WorkbookId"; metadata.Cell("B2").Value = Guid.NewGuid().ToString("N");
        metadata.Cell("A3").Value = "EmployeeCode"; metadata.Cell("B3").Value = employee.EmployeeCode;
        metadata.Cell("A4").Value = "Year"; metadata.Cell("B4").Value = year;
        metadata.Cell("A5").Value = "Month"; metadata.Cell("B5").Value = month;
        metadata.Cell("A6").Value = "EmployeeName"; metadata.Cell("B6").Value = employee.Name;
        metadata.Visibility = XLWorksheetVisibility.VeryHidden;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        workbook.SaveAs(destinationPath);
    }

    /// <summary>
    /// Writes the sheet on which this engineer rates colleagues. The roster is
    /// pre-filled so a reviewer only enters ratings, and so the codes coming back
    /// match the employee master exactly.
    /// </summary>
    private static void WritePeerSheet(XLWorkbook workbook, Employee employee, int year, int month, IReadOnlyList<Employee>? peers)
    {
        var sheet = workbook.AddWorksheet(PeerSheetName);
        sheet.Cell("A1").Value = "Peer Review";
        sheet.Range("A1:G1").Merge().Style.Font.SetBold().Font.SetFontSize(16);
        sheet.Cell("A2").Value = $"Rate the colleagues you worked with during {new DateTime(year, month, 1):MMMM yyyy}.";
        sheet.Cell("A3").Value = "Use 1 (lowest) to 5 (highest). Leave a row blank if you did not work with that person.";
        sheet.Cell("A4").Value = "Reviewer"; sheet.Cell("B4").Value = $"{employee.Name} ({employee.EmployeeCode})";
        sheet.Cell("A4").Style.Font.SetBold();

        for (var i = 0; i < PeerHeaders.Length; i++) sheet.Cell(PeerHeaderRow, i + 1).Value = PeerHeaders[i];
        var header = sheet.Range(PeerHeaderRow, 1, PeerHeaderRow, PeerHeaders.Length);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#1F4E78"));
        header.Style.Font.SetFontColor(XLColor.White);

        var roster = (peers ?? [])
            .Where(x => !string.Equals(x.EmployeeCode, employee.EmployeeCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var row = PeerHeaderRow + 1;
        foreach (var peer in roster)
        {
            sheet.Cell(row, 1).Value = peer.EmployeeCode;
            sheet.Cell(row, 2).Value = peer.Name;
            row++;
        }

        // Keep usable rows even when no roster was supplied.
        var lastRow = Math.Max(row - 1, PeerHeaderRow + 10);
        var ratings = sheet.Range(PeerHeaderRow + 1, 3, lastRow, 6);
        var validation = ratings.CreateDataValidation();
        validation.WholeNumber.Between(1, 5);
        validation.ErrorTitle = "Rating out of range";
        validation.ErrorMessage = "Enter a whole number from 1 to 5, or leave the cell empty.";
        ratings.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        sheet.Column(7).Width = 46;
        sheet.Columns(1, 6).AdjustToContents();
        sheet.SheetView.FreezeRows(PeerHeaderRow);
    }

    public IReadOnlyList<string> GenerateEngineerTemplates(string destinationFolder, IReadOnlyList<Employee> employees, int year, int month)
    {
        if (employees.Count == 0) throw new InvalidOperationException("There are no employees to generate templates for.");
        Directory.CreateDirectory(destinationFolder);
        var generated = new List<string>(employees.Count);
        foreach (var employee in employees)
        {
            var path = Path.Combine(destinationFolder, $"{Sanitize(employee.EmployeeCode)}_{Sanitize(employee.Name)}_{year:D4}_{month:D2}_Review.xlsx");
            // Every workbook carries the full roster so peer feedback maps back by code.
            GenerateEngineerTemplate(path, employee, year, month, employees);
            generated.Add(path);
        }
        return generated;
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
}
