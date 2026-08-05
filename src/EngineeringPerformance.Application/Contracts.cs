using EngineeringPerformance.Domain;

namespace EngineeringPerformance.Application;

public sealed record SourceSlot(ReportType ReportType, SourceStatus Status, string? FileName = null);
public sealed record DashboardSnapshot(string ReportingMonth, int ActiveEmployees, IReadOnlyList<SourceSlot> SourceSlots, int OpenIssues, decimal? OperationalDisciplineScore);

public sealed record OperationalScoringSettings(
    decimal TimesheetCompletionWeight = 55m,
    decimal ApprovalCompletionWeight = 15m,
    decimal AttendanceDisciplineWeight = 30m)
{
    public decimal Total => TimesheetCompletionWeight + ApprovalCompletionWeight + AttendanceDisciplineWeight;
    public bool IsValid => TimesheetCompletionWeight >= 0m && ApprovalCompletionWeight >= 0m &&
                           AttendanceDisciplineWeight >= 0m && Total == 100m;
    public static OperationalScoringSettings Default { get; } = new();
}

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
    Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default);

    Task<OperationalScoringSettings> GetOperationalScoringSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationalScoringSettings.Default);

    Task<int> SaveOperationalScoringSettingsAsync(OperationalScoringSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromException<int>(new NotSupportedException("This database implementation does not support configurable operational scoring."));

    Task<int> ImportEngineerReviewsAsync(int year, int month, string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default);
    Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default);
    Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default);
    Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default);
    Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default);
    Task SetProbationAsync(int employeeId, bool? value, CancellationToken cancellationToken = default);
    Task SetUpdownAsync(int employeeId, bool? value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportHistoryItem>> GetImportHistoryAsync(int? year = null, int? month = null, int take = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default);
    Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default);
    Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default);
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
    string? Email, bool IsConsultant, bool IsOnProbation, bool? ProbationOverride, bool IsNonBillable,
    int? TeamId, string? TeamName, bool IsUpdown = false, bool? UpdownOverride = null);

public sealed record TeamItem(int Id, string Name, int MemberCount);

public sealed record ImportHistoryItem(
    int Id, ReportType ReportType, int Year, int Month,
    string OriginalFileName, int RowCount, bool ReplacedExisting, DateTime ImportedUtc)
{
    public DateTime ImportedLocal => DateTime.SpecifyKind(ImportedUtc, DateTimeKind.Utc).ToLocalTime();
}

public sealed record MonthlyPerformanceItem(string EmployeeName, string? EmployeeCode, decimal OperationalScore,
    decimal TimesheetCompletionScore, decimal ApprovalScore, decimal AttendanceDisciplineScore,
    decimal EnteredHours, decimal ComplianceHours, decimal BillableHours, decimal DetailedHours,
    int DetailedEntries, int UniqueProjects, decimal AttendanceDays, decimal LeaveDays,
    int MissingPunchDays, int LateDays, int EarlyDays, int LessDurationDays,
    int Year, int Month, decimal PunchHours, decimal AttendanceTimesheetHours,
    int TimesheetFilledDays, int ExpectedTimesheetDays, decimal NonBillableHours, decimal TrainingHours,
    decimal ApprovedHours = 0, decimal OfficeHours = 0)
{
    public bool HasSummaryData => ComplianceHours > 0;
    public decimal ReconciliationVariance => PunchHours - AttendanceTimesheetHours;
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
    IReadOnlyList<PeerReview> ReadPeerReviews(string filePath, int year, int month);
    IReadOnlyList<RosterEntry> ReadEmployeeRoster(string filePath);
    void GenerateEngineerTemplate(string destinationPath, Employee employee, int year, int month, IReadOnlyList<Employee>? peers = null);
    IReadOnlyList<string> GenerateEngineerTemplates(string destinationFolder, IReadOnlyList<Employee> employees, int year, int month);
    void GenerateEmployeeReport(string destinationPath, EmployeeReportData data);
    void GenerateTeamReport(string destinationPath, TeamReportData data);
}

public sealed record WorkbookInspection(string FileName, IReadOnlyList<string> SheetNames, long Length);
public sealed record RosterEntry(string EmployeeCode, string Name, int SeniorityLevel, string? Email, bool IsConsultant, bool IsOnProbation, bool IsUpdown = false);

public sealed record EmployeeReportData(
    string EmployeeName, string EmployeeCode, int SeniorityLevel, int Year, int Month,
    MonthlyPerformanceItem? Current, IReadOnlyList<MonthlyPerformanceItem> History,
    IReadOnlyList<PeerReviewItem> PeerReviews, IReadOnlyList<string> AlertLines);

public sealed record TeamReportData(
    int Year, int Month, IReadOnlyList<MonthlyPerformanceItem> Items,
    IReadOnlyList<PeerReviewItem> PeerReviews, IReadOnlyList<string> AlertLines, int ExcludedCount);
