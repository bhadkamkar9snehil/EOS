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

    /// <summary>Imports one review workbook, or a ZIP of them, replacing the month's peer feedback.</summary>
    Task<int> ImportEngineerReviewsAsync(int year, int month, string path, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default);
    Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the ERP's employee roster export: creates any employee not yet in the master and
    /// syncs seniority, email, consultant and probation status from the roster. Not month-scoped —
    /// this is a live snapshot. Returns the number of employees created or updated.
    /// </summary>
    Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default);

    Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default);
    Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default);
    Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default);
    Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default);
    /// <summary>Deletes the team; members are unassigned, not removed.</summary>
    Task DeleteTeamAsync(int teamId, CancellationToken cancellationToken = default);
}

public interface IFileDialogService
{
    string? PickWorkbookOrZip();
    string? PickWorkbook();
    string? PickSaveWorkbook(string suggestedFileName);
    string? PickFolder(string title);
}

public sealed record EmployeeListItem(
    int Id, string EmployeeCode, string Name, int SeniorityLevel, bool IsExcluded,
    string? Email, bool IsConsultant, bool IsOnProbation, bool IsNonBillable,
    int? TeamId, string? TeamName);

public sealed record TeamItem(int Id, string Name, int MemberCount);
public sealed record MonthlyPerformanceItem(string EmployeeName, string? EmployeeCode, decimal OperationalScore,
    decimal TimesheetCompletionScore, decimal ApprovalScore, decimal AttendanceDisciplineScore,
    decimal EnteredHours, decimal ComplianceHours, decimal BillableHours, decimal DetailedHours,
    int DetailedEntries, int UniqueProjects, decimal AttendanceDays, decimal LeaveDays,
    int MissingPunchDays, int LateDays, int EarlyDays, int LessDurationDays,
    int Year, int Month, decimal PunchHours, decimal AttendanceTimesheetHours,
    int TimesheetFilledDays, int ExpectedTimesheetDays, decimal NonBillableHours, decimal TrainingHours,
    decimal ApprovedHours = 0)
{
    /// <summary>The monthly utilization export is the only source of compliance hours.</summary>
    public bool HasSummaryData => ComplianceHours > 0;

    /// <summary>Punch hours booked against hours recorded on the timesheet, in hours.</summary>
    public decimal ReconciliationVariance => PunchHours - AttendanceTimesheetHours;

    /// <summary>Share of compliance capacity actually entered, uncapped so overrun stays visible.</summary>
    public decimal Utilization => ComplianceHours <= 0 ? 0 : decimal.Round(EnteredHours / ComplianceHours * 100m, 1);
}

public sealed record PeerReviewItem(
    string ReviewerName, string ReviewerCode, string SubjectName, string SubjectCode,
    decimal Collaboration, decimal Communication, decimal Reliability, decimal TechnicalHelp,
    decimal Average, string? Comment);

public interface IWorkbookService
{
    WorkbookInspection Inspect(string filePath);
    ReportType DetectReportType(string filePath);
    IReadOnlyList<EmployeeMonthlyPerformance> ReadPerformance(string filePath, ReportType reportType, int year, int month);

    /// <summary>Reads the completed peer review sheet out of a generated review workbook.</summary>
    IReadOnlyList<PeerReview> ReadPeerReviews(string filePath, int year, int month);

    /// <summary>Reads the ERP's employee roster export (code, name, seniority derived from Band Level).</summary>
    IReadOnlyList<RosterEntry> ReadEmployeeRoster(string filePath);

    void GenerateEngineerTemplate(string destinationPath, Employee employee, int year, int month, IReadOnlyList<Employee>? peers = null);
    IReadOnlyList<string> GenerateEngineerTemplates(string destinationFolder, IReadOnlyList<Employee> employees, int year, int month);

    /// <summary>Formatted single-employee performance report, covering the score, category breakdown, peer feedback and alerts for that month.</summary>
    void GenerateEmployeeReport(string destinationPath, EmployeeReportData data);

    /// <summary>Formatted whole-team performance report for the month.</summary>
    void GenerateTeamReport(string destinationPath, TeamReportData data);
}

public sealed record WorkbookInspection(string FileName, IReadOnlyList<string> SheetNames, long Length);
public sealed record RosterEntry(string EmployeeCode, string Name, int SeniorityLevel, string? Email, bool IsConsultant, bool IsOnProbation);

/// <summary>
/// Everything <see cref="IWorkbookService.GenerateEmployeeReport"/> needs, gathered by the
/// caller from plain Application/Domain types so report layout stays out of the UI project.
/// </summary>
public sealed record EmployeeReportData(
    string EmployeeName, string EmployeeCode, int SeniorityLevel, int Year, int Month,
    MonthlyPerformanceItem? Current, IReadOnlyList<MonthlyPerformanceItem> History,
    IReadOnlyList<PeerReviewItem> PeerReviews, IReadOnlyList<string> AlertLines);

public sealed record TeamReportData(
    int Year, int Month, IReadOnlyList<MonthlyPerformanceItem> Items,
    IReadOnlyList<PeerReviewItem> PeerReviews, IReadOnlyList<string> AlertLines, int ExcludedCount);
