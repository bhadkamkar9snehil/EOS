namespace EngineeringPerformance.Domain;

public sealed class Employee
{
    private Employee() { }

    public Employee(string employeeCode, string name, int seniorityLevel)
    {
        EmployeeCode = RequireText(employeeCode, nameof(employeeCode));
        Name = RequireText(name, nameof(name));
        SetSeniorityLevel(seniorityLevel);
    }

    public int Id { get; private set; }
    public string EmployeeCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int SeniorityLevel { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Rename(string name) => Name = RequireText(name, nameof(name));

    public void SetSeniorityLevel(int level)
    {
        if (level < 1 || level > 99)
            throw new ArgumentOutOfRangeException(nameof(level), "Seniority level must be between 1 and 99.");
        SeniorityLevel = level;
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", parameterName) : value.Trim();
}

public sealed class ReportingMonth
{
    private ReportingMonth() { }
    public ReportingMonth(int year, int month)
    {
        if (year is < 2000 or > 2200) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        Year = year;
        Month = month;
    }
    public int Id { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public DateTime CreatedUtc { get; private set; } = DateTime.UtcNow;
}

public enum ReportType { MonthlyTimesheetSummary = 1, DetailedTimesheetTransactions = 2, AttendanceLeaveUaaTimesheet = 3, EngineerReviewWorkbook = 4 }
public enum SourceStatus { NotUploaded = 0, Uploaded = 1, Warnings = 2, BlockingErrors = 3, Superseded = 4 }

public sealed class ImportedSourceFile
{
    private ImportedSourceFile() { }
    public ImportedSourceFile(ReportType reportType, int year, int month, string originalFileName, string storedPath, int sheetCount)
    {
        ReportType = reportType; Year = year; Month = month; OriginalFileName = originalFileName;
        StoredPath = storedPath; SheetCount = sheetCount; ImportedUtc = DateTime.UtcNow;
    }
    public int Id { get; private set; }
    public ReportType ReportType { get; private set; }
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredPath { get; private set; } = string.Empty;
    public int SheetCount { get; private set; }
    public DateTime ImportedUtc { get; private set; }
}

public sealed class EmployeeMonthlyPerformance
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public decimal ComplianceHours { get; set; }
    public decimal EnteredHours { get; set; }
    public decimal ApprovedHours { get; set; }
    public decimal BillableHours { get; set; }
    public decimal NonBillableHours { get; set; }
    public decimal TrainingHours { get; set; }
    public decimal OfficeHours { get; set; }
    public decimal DetailedHours { get; set; }
    public int DetailedEntries { get; set; }
    public int UniqueProjects { get; set; }
    public decimal AttendanceDays { get; set; }
    public decimal LeaveDays { get; set; }
    public decimal PunchHours { get; set; }
    public decimal AttendanceTimesheetHours { get; set; }
    public int TimesheetFilledDays { get; set; }
    public int ExpectedTimesheetDays { get; set; }
    public int MissingPunchDays { get; set; }
    public int LateDays { get; set; }
    public int EarlyDays { get; set; }
    public int LessDurationDays { get; set; }
    public decimal TimesheetCompletionScore { get; set; }
    public decimal ApprovalScore { get; set; }
    public decimal AttendanceDisciplineScore { get; set; }
    public decimal OperationalScore { get; set; }
}

public sealed record MetricInput(string Code, decimal Score, decimal Weight, bool IsApplicable = true);

public static class WeightedScoreCalculator
{
    public static decimal Calculate(IEnumerable<MetricInput> inputs)
    {
        var applicable = inputs.Where(x => x.IsApplicable).ToArray();
        if (applicable.Length == 0) throw new InvalidOperationException("At least one applicable metric is required.");
        if (applicable.Any(x => x.Score is < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(inputs), "Scores must be between 0 and 100.");
        if (applicable.Any(x => x.Weight < 0)) throw new ArgumentOutOfRangeException(nameof(inputs), "Weights cannot be negative.");
        var totalWeight = applicable.Sum(x => x.Weight);
        if (totalWeight <= 0) throw new InvalidOperationException("Applicable weights must total more than zero.");
        return decimal.Round(applicable.Sum(x => x.Score * x.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero);
    }
}
