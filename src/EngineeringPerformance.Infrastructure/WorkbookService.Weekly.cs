using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class WorkbookService
{
    public IReadOnlyList<WeeklyPerformanceItem> ReadWeeklyPerformance(
        string filePath,
        ReportType reportType,
        int year,
        int month)
    {
        if (reportType is not (ReportType.DetailedTimesheetTransactions or ReportType.AttendanceLeaveUaaTimesheet))
            return [];

        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var headerRow = FindHeaderRow(sheet, reportType);
        var columns = sheet.Row(headerRow).CellsUsed()
            .ToDictionary(x => Text(x), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        return reportType == ReportType.DetailedTimesheetTransactions
            ? ReadWeeklyDetails(sheet, headerRow, columns, year, month)
            : ReadWeeklyAttendance(sheet, headerRow, columns, year, month);
    }

    private static IReadOnlyList<WeeklyPerformanceItem> ReadWeeklyDetails(
        IXLWorksheet sheet,
        int headerRow,
        Dictionary<string, int> columns,
        int year,
        int month)
    {
        var groups = new Dictionary<(string Name, DateTime WeekStart), WeeklyAccumulator>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var dateCell = sheet.Cell(row, Col(columns, "Date"));
            if (!dateCell.TryGetValue<DateTime>(out var date) || date.Year != year || date.Month != month)
                continue;

            var weekStart = StartOfWeek(date);
            var key = (name, weekStart);
            if (!groups.TryGetValue(key, out var item))
                groups[key] = item = new WeeklyAccumulator(name, weekStart);

            item.DetailedEntries++;
            item.DetailedHours += Number(sheet.Cell(row, Col(columns, "Total work Hours")));
            var project = Text(sheet.Cell(row, Col(columns, "Project No")));
            if (!string.IsNullOrWhiteSpace(project)) item.Projects.Add(project);
        }

        return groups.Values.Select(x => x.ToItem()).ToArray();
    }

    private static IReadOnlyList<WeeklyPerformanceItem> ReadWeeklyAttendance(
        IXLWorksheet sheet,
        int headerRow,
        Dictionary<string, int> columns,
        int year,
        int month)
    {
        var groups = new Dictionary<(string Name, DateTime WeekStart), WeeklyAccumulator>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;

            var dateCell = sheet.Cell(row, Col(columns, "Date"));
            if (!dateCell.TryGetValue<DateTime>(out var date) || date.Year != year || date.Month != month)
                continue;

            var weekStart = StartOfWeek(date);
            var key = (name, weekStart);
            if (!groups.TryGetValue(key, out var item))
                groups[key] = item = new WeeklyAccumulator(name, weekStart);

            if (columns.TryGetValue("Emp No", out var employeeCodeColumn))
            {
                var code = Text(sheet.Cell(row, employeeCodeColumn));
                if (!string.IsNullOrWhiteSpace(code)) item.EmployeeCode ??= code;
            }

            var attendDay = Number(sheet.Cell(row, Col(columns, "Attend Day")));
            var position = Text(sheet.Cell(row, Col(columns, "Position")));
            var duty = Text(sheet.Cell(row, Col(columns, "Duty Type")));
            var approvedLeave = Text(sheet.Cell(row, Col(columns, "Leave status")))
                .Equals("Approved", StringComparison.OrdinalIgnoreCase);

            var accountable = attendDay > 0 &&
                              !approvedLeave &&
                              !duty.Equals("woff", StringComparison.OrdinalIgnoreCase) &&
                              !position.Equals("Leave", StringComparison.OrdinalIgnoreCase);
            if (!accountable) continue;

            item.ExpectedDays++;
            item.PunchHours += Number(sheet.Cell(row, Col(columns, "Punch Duration")));
            item.TimesheetHours += Number(sheet.Cell(row, Col(columns, "Timesheet Hrs")));

            if (Text(sheet.Cell(row, Col(columns, "Timesheet"))).Equals("Filled", StringComparison.OrdinalIgnoreCase))
                item.FilledDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Punch not Found")))) item.MissingPunchDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Late Coming")))) item.LateDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Early Going")))) item.EarlyDays++;
            if (Flag(sheet.Cell(row, Col(columns, "Flg Less Duration")))) item.LessDurationDays++;
        }

        return groups.Values.Select(x => x.ToItem()).ToArray();
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.Date.AddDays(-offset);
    }

    private sealed class WeeklyAccumulator(string employeeName, DateTime weekStart)
    {
        public string EmployeeName { get; } = employeeName;
        public string? EmployeeCode { get; set; }
        public DateTime WeekStart { get; } = weekStart;
        public decimal DetailedHours { get; set; }
        public int DetailedEntries { get; set; }
        public HashSet<string> Projects { get; } = new(StringComparer.OrdinalIgnoreCase);
        public decimal PunchHours { get; set; }
        public decimal TimesheetHours { get; set; }
        public int FilledDays { get; set; }
        public int ExpectedDays { get; set; }
        public int MissingPunchDays { get; set; }
        public int LateDays { get; set; }
        public int EarlyDays { get; set; }
        public int LessDurationDays { get; set; }

        public WeeklyPerformanceItem ToItem() => new(
            EmployeeName,
            EmployeeCode,
            WeekStart,
            DetailedHours,
            DetailedEntries,
            Projects.Count,
            PunchHours,
            TimesheetHours,
            FilledDays,
            ExpectedDays,
            MissingPunchDays,
            LateDays,
            EarlyDays,
            LessDurationDays);
    }
}
