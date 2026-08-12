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

    /// <summary>
    /// Computes what ImportSourceAsync would change without committing anything, by reading the
    /// workbook and diffing it against the current database inside a context that is discarded
    /// instead of saved.
    /// </summary>
    Task<ImportPreview> PreviewImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default) =>
        Task.FromException<ImportPreview>(new NotSupportedException("This database implementation does not support import preview."));
    Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WeeklyPerformanceItem>> GetWeeklyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WeeklyPerformanceItem>>([]);

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
    string? PickBackupFile() => null;
}

/// <summary>
/// One-click export of the SQLite database (plus the operational-scoring config) into a single
/// zip, and the matching restore flow. Restore always takes a safety backup of the current
/// database before overwriting it, so an accidental or bad restore is itself recoverable.
/// </summary>
public interface IBackupService
{
    Task<BackupResult> ExportBackupAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);
    string DefaultBackupDirectory { get; }
    Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(string? directory = null, CancellationToken cancellationToken = default);
}

public sealed record BackupResult(string FilePath, long SizeBytes, DateTime CreatedUtc);
public sealed record RestoreResult(string SafetyBackupPath, DateTime RestoredUtc, bool RequiresRestart = true);
public sealed record BackupFileInfo(string FilePath, string FileName, long SizeBytes, DateTime CreatedUtc);

/// <summary>
/// Named, reusable sets of operational scoring weights (e.g. "Individual Contributor" vs.
/// "Team Lead"), saved and switched between the same way the live weights themselves are
/// persisted — a local JSON file next to operational-scoring.json — rather than a new database
/// table, since applying a preset is just writing new values through the existing
/// SaveOperationalScoringSettingsAsync path.
/// </summary>
public interface IScoringPresetService
{
    Task<IReadOnlyList<ScoringPreset>> GetPresetsAsync(CancellationToken cancellationToken = default);
    Task SavePresetAsync(string name, OperationalScoringSettings settings, CancellationToken cancellationToken = default);
    Task DeletePresetAsync(string name, CancellationToken cancellationToken = default);
}

public sealed record ScoringPreset(string Name, OperationalScoringSettings Settings, bool IsBuiltIn = false);

public sealed record ImportPreview(
    ReportType ReportType, int Year, int Month, int TotalRows,
    int RowsAdded, int RowsUpdated, int RowsUnchanged,
    IReadOnlyList<string> SampleAdded, IReadOnlyList<string> SampleUpdated);

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
    decimal TimesheetFilledDays, decimal ExpectedTimesheetDays, decimal NonBillableHours, decimal TrainingHours,
    decimal ApprovedHours = 0, decimal OfficeHours = 0, decimal RawUtilization = 0)
{
    public bool HasSummaryData => ComplianceHours > 0;
    public decimal ReconciliationVariance => PunchHours - AttendanceTimesheetHours;

    /// <summary>
    /// The ERP's own Utilization %, trusted verbatim when present. Months imported before this
    /// column was captured have RawUtilization stuck at its 0 default even when hours were
    /// entered — a state the real ERP figure can never produce (its own fallback rule guarantees
    /// a nonzero result whenever entered hours are nonzero), so that combination is the signal to
    /// fall back to a local Entered/Compliance approximation instead of showing a false 0%.
    /// </summary>
    public decimal Utilization =>
        RawUtilization > 0 || EnteredHours <= 0 || ComplianceHours <= 0
            ? RawUtilization
            : decimal.Round(EnteredHours / ComplianceHours * 100m, 1);
}

/// <summary>
/// A factual Monday-to-Sunday aggregation built only from dated detailed-timesheet and attendance rows.
/// Monthly utilization totals are deliberately not divided into weeks because that would be an estimate.
/// </summary>
public sealed record WeeklyPerformanceItem(
    string EmployeeName,
    string? EmployeeCode,
    DateTime WeekStart,
    decimal DetailedHours,
    int DetailedEntries,
    int UniqueProjects,
    decimal PunchHours,
    decimal TimesheetHours,
    decimal FilledDays,
    decimal ExpectedDays,
    int MissingPunchDays,
    int LateDays,
    int EarlyDays,
    int LessDurationDays)
{
    public DateTime WeekEnd => WeekStart.AddDays(6);
    public decimal MissingTimesheetDays => Math.Max(0m, ExpectedDays - FilledDays);
    public decimal ReconciliationVariance => PunchHours - TimesheetHours;
    public decimal TimesheetFillRate => ExpectedDays <= 0 ? 0m : decimal.Round(FilledDays * 100m / ExpectedDays, 1);

    public decimal AttendanceDisciplineScore
    {
        get
        {
            if (ExpectedDays <= 0) return 0m;
            var fill = Percentage(FilledDays, ExpectedDays);
            var punch = 100m - Percentage(MissingPunchDays, ExpectedDays);
            var duration = 100m - Percentage(LessDurationDays, ExpectedDays);
            var punctuality = 100m - Percentage(LateDays + EarlyDays, ExpectedDays * 2m);
            return decimal.Round(fill * .40m + punch * .25m + duration * .20m + punctuality * .15m, 1);
        }
    }

    private static decimal Percentage(decimal value, decimal denominator) =>
        denominator <= 0 ? 0m : Math.Clamp(decimal.Round(value / denominator * 100m, 1), 0m, 100m);
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
    IReadOnlyList<WeeklyPerformanceItem> ReadWeeklyPerformance(string filePath, ReportType reportType, int year, int month);
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
