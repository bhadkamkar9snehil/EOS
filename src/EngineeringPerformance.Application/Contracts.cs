using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Application;

public sealed record SourceSlot(ReportType ReportType, SourceStatus Status, string? FileName = null);
public sealed record DashboardSnapshot(string ReportingMonth, int ActiveEmployees, IReadOnlyList<SourceSlot> SourceSlots, int OpenIssues, decimal? OperationalDisciplineScore);

public interface IApplicationDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<DashboardSnapshot> GetDashboardAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default);
    Task AddEmployeeAsync(string employeeCode, string name, int seniorityLevel, CancellationToken cancellationToken = default);
    Task UpdateEmployeeAsync(int employeeId, string name, int seniorityLevel, CancellationToken cancellationToken = default);
    Task RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken = default);
    Task ImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default);
    Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Performance rows for the given month and the months preceding it, oldest first.</summary>
    Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default);
    Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default);
}

public interface IFileDialogService
{
    string? PickWorkbookOrZip();
    string? PickWorkbook();
    string? PickSaveWorkbook(string suggestedFileName);
    string? PickFolder(string title);
}

public sealed record EmployeeListItem(int Id, string EmployeeCode, string Name, int SeniorityLevel, bool IsExcluded);
public sealed record MonthlyPerformanceItem(string EmployeeName, string? EmployeeCode, decimal OperationalScore,
    decimal TimesheetCompletionScore, decimal ApprovalScore, decimal AttendanceDisciplineScore,
    decimal EnteredHours, decimal ComplianceHours, decimal BillableHours, decimal DetailedHours,
    int DetailedEntries, int UniqueProjects, decimal AttendanceDays, decimal LeaveDays,
    int MissingPunchDays, int LateDays, int EarlyDays, int LessDurationDays,
    int Year, int Month, decimal PunchHours, decimal AttendanceTimesheetHours,
    int TimesheetFilledDays, int ExpectedTimesheetDays, decimal NonBillableHours, decimal TrainingHours)
{
    /// <summary>The monthly utilization export is the only source of compliance hours.</summary>
    public bool HasSummaryData => ComplianceHours > 0;

    /// <summary>Punch hours booked against hours recorded on the timesheet, in hours.</summary>
    public decimal ReconciliationVariance => PunchHours - AttendanceTimesheetHours;

    /// <summary>Share of compliance capacity actually entered, uncapped so overrun stays visible.</summary>
    public decimal Utilization => ComplianceHours <= 0 ? 0 : decimal.Round(EnteredHours / ComplianceHours * 100m, 1);
}

public interface IWorkbookService
{
    WorkbookInspection Inspect(string filePath);
    ReportType DetectReportType(string filePath);
    IReadOnlyList<EmployeeMonthlyPerformance> ReadPerformance(string filePath, ReportType reportType, int year, int month);
    void GenerateEngineerTemplate(string destinationPath, Employee employee, int year, int month);
    IReadOnlyList<string> GenerateEngineerTemplates(string destinationFolder, IReadOnlyList<Employee> employees, int year, int month);
}

public sealed record WorkbookInspection(string FileName, IReadOnlyList<string> SheetNames, long Length);
