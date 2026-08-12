using System.Text.Json;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class ConfigurableApplicationDatabase(
    LocalApplicationDatabase inner,
    IDbContextFactory<PerformanceDbContext> contextFactory,
    IWorkbookService workbookService,
    string dataDirectory,
    ILogger<ConfigurableApplicationDatabase>? logger = null) : IApplicationDatabase
{
    private readonly string _settingsPath = Path.Combine(dataDirectory, "operational-scoring.json");
    private readonly ILogger<ConfigurableApplicationDatabase> _logger = logger ?? NullLogger<ConfigurableApplicationDatabase>.Instance;
    private readonly string _disciplineSettingsPath = Path.Combine(dataDirectory, "execution-discipline.json");
    private readonly string _disciplineExceptionsPath = Path.Combine(dataDirectory, "execution-discipline-exceptions.json");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await inner.InitializeAsync(cancellationToken);
        if (!File.Exists(_settingsPath))
            await WriteSettingsAsync(OperationalScoringSettings.Default, cancellationToken);
        if (!File.Exists(_disciplineSettingsPath))
            await WriteDisciplineSettingsAsync(ExecutionDisciplineSettings.Default, cancellationToken);
    }

    public async Task<OperationalScoringSettings> GetOperationalScoringSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return OperationalScoringSettings.Default;
        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<OperationalScoringSettings>(stream, cancellationToken: cancellationToken)
                   ?? OperationalScoringSettings.Default;
        }
        catch (JsonException)
        {
            return OperationalScoringSettings.Default;
        }
    }

    public async Task<int> SaveOperationalScoringSettingsAsync(OperationalScoringSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.IsValid)
            throw new InvalidOperationException("Operational scoring weights must be non-negative and total exactly 100%.");
        var previous = await GetOperationalScoringSettingsAsync(cancellationToken);
        await WriteSettingsAsync(settings, cancellationToken);
        var affected = await RecalculateAllAsync(settings, cancellationToken);
        _logger.LogInformation(
            "Operational scoring weights changed: Timesheet {OldTimesheet}->{NewTimesheet}, Approval {OldApproval}->{NewApproval}, Attendance {OldAttendance}->{NewAttendance}. {AffectedCount} rows recalculated.",
            previous.TimesheetCompletionWeight, settings.TimesheetCompletionWeight,
            previous.ApprovalCompletionWeight, settings.ApprovalCompletionWeight,
            previous.AttendanceDisciplineWeight, settings.AttendanceDisciplineWeight,
            affected);
        return affected;
    }

    private async Task WriteSettingsAsync(OperationalScoringSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temporary, _settingsPath, true);
    }

    private async Task<int> RecalculateAllAsync(OperationalScoringSettings settings, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.EmployeeMonthlyPerformances.ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            MetricInput[] metrics =
            [
                new("timesheet", row.TimesheetCompletionScore, settings.TimesheetCompletionWeight / 100m, row.ComplianceHours > 0),
                new("approval", row.ApprovalScore, settings.ApprovalCompletionWeight / 100m, row.EnteredHours > 0),
                new("attendance", row.AttendanceDisciplineScore, settings.AttendanceDisciplineWeight / 100m, row.ExpectedTimesheetDays > 0)
            ];
            row.OperationalScore = metrics.Any(x => x.IsApplicable)
                ? WeightedScoreCalculator.Calculate(metrics)
                : 0m;
        }
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private async Task RecalculateAfterImportAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOperationalScoringSettingsAsync(cancellationToken);
        await RecalculateAllAsync(settings, cancellationToken);
    }

    public async Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExcludedNamesAsync(context, cancellationToken);
        var rows = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year == year && x.Month == month)
            .OrderByDescending(x => x.OperationalScore).ThenBy(x => x.EmployeeName)
            .ToListAsync(cancellationToken);
        return rows.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(Project).ToArray();
    }

    public async Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default)
    {
        var newest = new DateTime(year, month, 1);
        var oldest = newest.AddMonths(-Math.Max(0, monthsBack - 1));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExcludedNamesAsync(context, cancellationToken);
        var rows = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year * 100 + x.Month >= oldest.Year * 100 + oldest.Month &&
                        x.Year * 100 + x.Month <= newest.Year * 100 + newest.Month)
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.EmployeeName)
            .ToListAsync(cancellationToken);
        return rows.Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName))).Select(Project).ToArray();
    }

    /// <summary>
    /// Reads dated detailed-timesheet and attendance rows from the source files already stored by
    /// the import pipeline. Monthly utilization is intentionally excluded: it is one monthly total
    /// and cannot be divided into weeks without inventing values.
    /// </summary>
    public async Task<IReadOnlyList<WeeklyPerformanceItem>> GetWeeklyPerformanceAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExcludedNamesAsync(context, cancellationToken);
        var sources = await context.ImportedSourceFiles
            .Where(x => x.Year == year && x.Month == month &&
                        (x.ReportType == ReportType.DetailedTimesheetTransactions ||
                         x.ReportType == ReportType.AttendanceLeaveUaaTimesheet))
            .OrderBy(x => x.ReportType)
            .ToListAsync(cancellationToken);

        var merged = new Dictionary<(string Name, DateTime WeekStart), WeeklyAccumulator>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(source.StoredPath)) continue;

            var rows = workbookService.ReadWeeklyPerformance(source.StoredPath, source.ReportType, year, month);
            foreach (var row in rows)
            {
                var normalizedName = PersonName.Normalize(row.EmployeeName);
                if (excluded.Contains(normalizedName)) continue;

                var key = (normalizedName.ToUpperInvariant(), row.WeekStart.Date);
                if (!merged.TryGetValue(key, out var item))
                    merged[key] = item = new WeeklyAccumulator(normalizedName, row.WeekStart.Date);
                item.Merge(row);
            }
        }

        return merged.Values
            .Select(x => x.ToItem())
            .OrderBy(x => x.WeekStart)
            .ThenBy(x => x.EmployeeName)
            .ToArray();
    }

    private static async Task<HashSet<string>> ReadExcludedNamesAsync(PerformanceDbContext context, CancellationToken cancellationToken)
    {
        var names = await context.AnalysisExclusions.Select(x => x.EmployeeName).ToListAsync(cancellationToken);
        return new HashSet<string>(names.Select(PersonName.Normalize), StringComparer.OrdinalIgnoreCase);
    }

    private static MonthlyPerformanceItem Project(EmployeeMonthlyPerformance x) => new(
        x.EmployeeName, x.EmployeeCode, x.OperationalScore, x.TimesheetCompletionScore,
        x.ApprovalScore, x.AttendanceDisciplineScore, x.EnteredHours, x.ComplianceHours,
        x.BillableHours, x.DetailedHours, x.DetailedEntries, x.UniqueProjects,
        x.AttendanceDays, x.LeaveDays, x.MissingPunchDays, x.LateDays, x.EarlyDays,
        x.LessDurationDays, x.Year, x.Month, x.PunchHours, x.AttendanceTimesheetHours,
        x.TimesheetFilledDays, x.ExpectedTimesheetDays, x.NonBillableHours,
        x.TrainingHours, x.ApprovedHours, x.OfficeHours, x.Utilization);

    public Task<DashboardSnapshot> GetDashboardAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default) => inner.GetDashboardAsync(year, month, cancellationToken);
    public Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default) => inner.GetEmployeesAsync(cancellationToken);
    public Task AddEmployeeAsync(string employeeCode, string name, int seniorityLevel, CancellationToken cancellationToken = default) => inner.AddEmployeeAsync(employeeCode, name, seniorityLevel, cancellationToken);
    public Task UpdateEmployeeAsync(int employeeId, string name, int seniorityLevel, CancellationToken cancellationToken = default) => inner.UpdateEmployeeAsync(employeeId, name, seniorityLevel, cancellationToken);
    public Task RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken = default) => inner.RemoveEmployeeAsync(employeeId, cancellationToken);

    public async Task ImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default)
    {
        await inner.ImportSourceAsync(reportType, year, month, sourcePath, cancellationToken);
        await RecalculateAfterImportAsync(cancellationToken);
    }

    public Task<ImportPreview> PreviewImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default) =>
        inner.PreviewImportSourceAsync(reportType, year, month, sourcePath, cancellationToken);

    public async Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default)
    {
        var count = await inner.ImportPackageAsync(year, month, zipPath, cancellationToken);
        await RecalculateAfterImportAsync(cancellationToken);
        return count;
    }

    public Task<ReviewImportResult> ImportEngineerReviewsAsync(int year, int month, IReadOnlyList<string> paths, ReviewImportMode mode = ReviewImportMode.MergeReviewers, CancellationToken cancellationToken = default) =>
        inner.ImportEngineerReviewsAsync(year, month, paths, mode, cancellationToken);
    public Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default) => inner.GetPeerReviewsAsync(year, month, cancellationToken);
    public Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default) => inner.GetExcludedNamesAsync(cancellationToken);
    public Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default) => inner.SetExclusionAsync(employeeName, excluded, cancellationToken);
    public Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default) => inner.ImportEmployeeRosterAsync(filePath, cancellationToken);
    public Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default) => inner.SetNonBillableAsync(employeeId, value, cancellationToken);
    public Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default) => inner.AssignTeamAsync(employeeId, teamId, cancellationToken);
    public Task SetProbationAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => inner.SetProbationAsync(employeeId, value, cancellationToken);
    public Task SetUpdownAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => inner.SetUpdownAsync(employeeId, value, cancellationToken);
    public Task<IReadOnlyList<ImportHistoryItem>> GetImportHistoryAsync(int? year = null, int? month = null, int take = 200, CancellationToken cancellationToken = default) => inner.GetImportHistoryAsync(year, month, take, cancellationToken);
    public Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default) => inner.GetTeamsAsync(cancellationToken);
    public Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default) => inner.AddTeamAsync(name, cancellationToken);
    public Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default) => inner.RenameTeamAsync(teamId, name, cancellationToken);
    public Task DeleteTeamAsync(int teamId, CancellationToken cancellationToken = default) => inner.DeleteTeamAsync(teamId, cancellationToken);

    private sealed class WeeklyAccumulator(string employeeName, DateTime weekStart)
    {
        private string? _employeeCode;
        private decimal _detailedHours;
        private int _detailedEntries;
        private int _uniqueProjects;
        private decimal _punchHours;
        private decimal _timesheetHours;
        private decimal _filledDays;
        private decimal _expectedDays;
        private int _missingPunchDays;
        private int _lateDays;
        private int _earlyDays;
        private int _lessDurationDays;

        public void Merge(WeeklyPerformanceItem row)
        {
            _employeeCode ??= row.EmployeeCode;
            _detailedHours += row.DetailedHours;
            _detailedEntries += row.DetailedEntries;
            _uniqueProjects = Math.Max(_uniqueProjects, row.UniqueProjects);
            _punchHours += row.PunchHours;
            _timesheetHours += row.TimesheetHours;
            _filledDays += row.FilledDays;
            _expectedDays += row.ExpectedDays;
            _missingPunchDays += row.MissingPunchDays;
            _lateDays += row.LateDays;
            _earlyDays += row.EarlyDays;
            _lessDurationDays += row.LessDurationDays;
        }

        public WeeklyPerformanceItem ToItem() => new(
            employeeName,
            _employeeCode,
            weekStart,
            _detailedHours,
            _detailedEntries,
            _uniqueProjects,
            _punchHours,
            _timesheetHours,
            _filledDays,
            _expectedDays,
            _missingPunchDays,
            _lateDays,
            _earlyDays,
            _lessDurationDays);
    }
}
