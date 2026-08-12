using ClosedXML.Excel;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class WorkbookService
{
    public IReadOnlyList<TimesheetDayEvidence> ReadTimesheetDayEvidence(string filePath, int year, int month)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var headerRow = FindHeaderRow(sheet, ReportType.DetailedTimesheetTransactions);
        var columns = sheet.Row(headerRow).CellsUsed()
            .ToDictionary(x => Text(x), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        if (!columns.ContainsKey("Filled Date"))
            throw new InvalidDataException("The detailed work-log export does not contain a Filled Date column.");

        var rows = new Dictionary<(string Name, DateTime Date), DayEvidenceAccumulator>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!TryDate(sheet.Cell(row, Col(columns, "Date")), out var workDate) ||
                workDate.Year != year || workDate.Month != month)
                continue;
            if (!TryDate(sheet.Cell(row, Col(columns, "Filled Date")), out var filledAt))
                continue;

            var key = (name.ToUpperInvariant(), workDate.Date);
            if (!rows.TryGetValue(key, out var day))
                rows[key] = day = new DayEvidenceAccumulator(name, workDate.Date, Path.GetFileName(filePath));

            day.LastFilledAt = day.LastFilledAt is null || filledAt > day.LastFilledAt ? filledAt : day.LastFilledAt;
            day.RecordedHours += Number(sheet.Cell(row, Col(columns, "Total work Hours")));
            day.EntryCount++;
        }

        return rows.Values
            .Where(x => x.LastFilledAt is not null)
            .Select(x => new TimesheetDayEvidence(
                x.EmployeeName, null, x.WorkDate, x.LastFilledAt!.Value,
                x.RecordedHours, x.EntryCount, x.SourceFileName))
            .OrderBy(x => x.WorkDate)
            .ThenBy(x => x.EmployeeName)
            .ToArray();
    }

    public IReadOnlyList<AccountableWorkday> ReadAccountableWorkdays(string filePath, int year, int month)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(1);
        var headerRow = FindHeaderRow(sheet, ReportType.AttendanceLeaveUaaTimesheet);
        var columns = sheet.Row(headerRow).CellsUsed()
            .ToDictionary(x => Text(x), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<(string Name, DateTime Date), AccountableWorkday>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var name = PersonName.Normalize(Text(sheet.Cell(row, Col(columns, "Employee"))));
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!TryDate(sheet.Cell(row, Col(columns, "Date")), out var workDate) ||
                workDate.Year != year || workDate.Month != month)
                continue;

            var attendDay = Number(sheet.Cell(row, Col(columns, "Attend Day")));
            var position = Text(sheet.Cell(row, Col(columns, "Position")));
            var duty = Text(sheet.Cell(row, Col(columns, "Duty Type")));
            var approvedLeave = Text(sheet.Cell(row, Col(columns, "Leave status")))
                .Equals("Approved", StringComparison.OrdinalIgnoreCase);
            var accountable = attendDay > 0 &&
                              !approvedLeave &&
                              workDate.DayOfWeek != DayOfWeek.Sunday &&
                              !duty.Equals("woff", StringComparison.OrdinalIgnoreCase) &&
                              !position.Equals("Leave", StringComparison.OrdinalIgnoreCase);
            if (!accountable) continue;

            string? employeeCode = null;
            if (columns.TryGetValue("Emp No", out var employeeCodeColumn))
                employeeCode = Text(sheet.Cell(row, employeeCodeColumn));

            var weight = workDate.DayOfWeek == DayOfWeek.Saturday ? .5m : 1m;
            var key = (name.ToUpperInvariant(), workDate.Date);
            rows[key] = new AccountableWorkday(
                name,
                string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode,
                workDate.Date,
                weight,
                Path.GetFileName(filePath));
        }

        return rows.Values.OrderBy(x => x.WorkDate).ThenBy(x => x.EmployeeName).ToArray();
    }

    private static bool TryDate(IXLCell cell, out DateTime value)
    {
        if (cell.TryGetValue<DateTime>(out value)) return true;
        if (cell.TryGetValue<double>(out var serial))
        {
            value = DateTime.FromOADate(serial);
            return true;
        }
        return DateTime.TryParse(Text(cell), out value);
    }

    private sealed class DayEvidenceAccumulator(string employeeName, DateTime workDate, string sourceFileName)
    {
        public string EmployeeName { get; } = employeeName;
        public DateTime WorkDate { get; } = workDate;
        public string SourceFileName { get; } = sourceFileName;
        public DateTime? LastFilledAt { get; set; }
        public decimal RecordedHours { get; set; }
        public int EntryCount { get; set; }
    }
}
