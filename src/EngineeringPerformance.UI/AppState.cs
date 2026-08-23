using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace EngineeringPerformance.UI;

/// <summary>
/// Single shared source of the current reporting month and everything loaded for it — employees,
/// performance, history, weekly detail, teams, exclusions, peer reviews — plus the toast message
/// shown in the shell. Registered once for the app's lifetime.
/// </summary>
public sealed class AppState(IApplicationDatabase database, ILogger<AppState>? logger = null)
{
    private readonly ILogger<AppState> _logger = logger ?? NullLogger<AppState>.Instance;
    public static DateTime FiscalYearStart { get; } = new(2026, 4, 1);
    public static DateTime FiscalYearEnd { get; } = new(2027, 3, 1);
    public static IReadOnlyList<DateTime> FiscalMonths { get; } = Enumerable.Range(0, 12).Select(offset => FiscalYearStart.AddMonths(offset)).ToArray();
    public static bool IsFiscalMonth(DateTime month)
    {
        var normalized = new DateTime(month.Year, month.Month, 1);
        return normalized >= FiscalYearStart && normalized <= FiscalYearEnd;
    }
    public static int FiscalMonthsThrough(DateTime month) => Math.Clamp(
        (month.Year - FiscalYearStart.Year) * 12 + month.Month - FiscalYearStart.Month + 1,
        1,
        FiscalMonths.Count);

    private int _refreshVersion;
    public DateTime SelectedMonth { get; private set; } = DefaultReportingMonth();
    public DashboardSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<EmployeeListItem> Employees { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> Performance { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> History { get; private set; } = [];
    public IReadOnlyList<WeeklyPerformanceItem> WeeklyPerformance { get; private set; } = [];
    public TimesheetFilingSnapshot? TimesheetFiling { get; private set; }
    public IReadOnlyList<string> ExcludedNames { get; private set; } = [];
    public IReadOnlyList<PeerReviewItem> PeerReviews { get; private set; } = [];
    public IReadOnlyList<TeamItem> Teams { get; private set; } = [];
    public DateTime LastRefresh { get; private set; } = DateTime.Now;

    public string Message { get; private set; } = string.Empty;
    public bool IsError { get; private set; }
    public bool Busy { get; private set; }

    public string? SpotlightName { get; set; }

    /// <summary>The user's stored choice: "system" or any theme name in wwwroot/theme.js's list
    /// ("light", "slate", "dark", "midnight", "contrast"). Kept here purely so every component
    /// showing the current choice (header toggle, Settings picker) re-renders together via
    /// <see cref="Changed"/>; theme.js owns the actual [data-theme] attribute.</summary>
    public string ThemeMode { get; private set; } = "system";

    /// <summary>The concrete theme actually applied. Differs from <see cref="ThemeMode"/> only when
    /// the choice is "system", which theme.js resolves through the OS preference. Components need
    /// this to answer "is the UI currently dark?" — a question ThemeMode alone can't answer.</summary>
    public string ResolvedTheme { get; private set; } = "light";

    public bool IsDarkTheme => ResolvedTheme is "dark" or "midnight";

    public async Task SetThemeAsync(IJSRuntime js, string mode)
    {
        await js.InvokeVoidAsync("epaTheme.set", mode);
        ThemeMode = mode;
        ResolvedTheme = await js.InvokeAsync<string>("epaTheme.resolved");
        Changed?.Invoke();
    }

    public async Task LoadThemeAsync(IJSRuntime js)
    {
        ThemeMode = await js.InvokeAsync<string>("epaTheme.get");
        ResolvedTheme = await js.InvokeAsync<string>("epaTheme.resolved");
        Changed?.Invoke();
    }

    public int ReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded) ?? 0;
    public int SystemReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded && x.ReportType != ReportType.EngineerReviewWorkbook) ?? 0;
    public bool ReviewsUploaded => Snapshot?.SourceSlots.Any(x => x.ReportType == ReportType.EngineerReviewWorkbook && x.Status == SourceStatus.Uploaded) ?? false;
    public bool CanGoPrevious => SelectedMonth > FiscalYearStart;
    public bool CanGoNext => SelectedMonth < FiscalYearEnd;

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var year = SelectedMonth.Year;
        var month = SelectedMonth.Month;
        _logger.LogDebug(
            "Application state refresh started. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2}",
            version,
            year,
            month);

        try
        {
            var dashboardTask = database.GetDashboardAsync(year, month);
            var employeesTask = database.GetEmployeesAsync();
            var performanceTask = database.GetMonthlyPerformanceAsync(year, month);
            var historyTask = database.GetPerformanceHistoryAsync(year, month, FiscalMonthsThrough(SelectedMonth));
            var exclusionsTask = database.GetExcludedNamesAsync();
            var reviewsTask = database.GetPeerReviewsAsync(year, month);
            var teamsTask = database.GetTeamsAsync();
            var scoringTask = database.GetOperationalScoringSettingsAsync();

            await Task.WhenAll(dashboardTask, employeesTask, performanceTask, historyTask, exclusionsTask, reviewsTask, teamsTask, scoringTask);
            if (version != Volatile.Read(ref _refreshVersion))
            {
                _logger.LogDebug(
                    "Application state refresh was superseded. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2}",
                    version,
                    year,
                    month);
                return;
            }

            var scoring = await scoringTask;
            Snapshot = await dashboardTask;
            Employees = await employeesTask;
            Performance = ConsolidatePerformance(await performanceTask, scoring);
            History = ConsolidatePerformance(
                (await historyTask).Where(x => IsFiscalMonth(new DateTime(x.Year, x.Month, 1))),
                scoring);
            ExcludedNames = await exclusionsTask;
            PeerReviews = await reviewsTask;
            Teams = await teamsTask;
            LastRefresh = DateTime.Now;

            _logger.LogDebug(
                "Application state refresh completed. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2} People={PeopleCount} Reviews={ReviewCount}",
                version,
                year,
                month,
                Performance.Count,
                PeerReviews.Count);
            Changed?.Invoke();

            _ = LoadWeeklyPerformanceAsync(version, year, month);
            _ = LoadTimesheetFilingAsync(version, year, month);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Application state refresh failed. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2}",
                version,
                year,
                month);
            throw;
        }
    }

    private async Task LoadWeeklyPerformanceAsync(int version, int year, int month)
    {
        try
        {
            var weekly = await Task.Run(() => database.GetWeeklyPerformanceAsync(year, month));
            if (version != Volatile.Read(ref _refreshVersion)) return;
            WeeklyPerformance = weekly;
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _refreshVersion)) return;
            _logger.LogError(
                exception,
                "Weekly performance load failed. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2}",
                version,
                year,
                month);
            Message = $"Weekly performance could not be loaded: {exception.Message}";
            IsError = true;
            Changed?.Invoke();
        }
    }

    private async Task LoadTimesheetFilingAsync(int version, int year, int month)
    {
        try
        {
            var snapshot = await Task.Run(() => database.GetTimesheetFilingAsync(year, month));
            if (version != Volatile.Read(ref _refreshVersion)) return;
            TimesheetFiling = snapshot;
            _logger.LogDebug(
                "Timesheet filing refresh completed. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2} People={PeopleCount} AverageDelayDays={AverageDelayDays}",
                version,
                year,
                month,
                snapshot.Rows.Count,
                snapshot.AverageDelayDays);
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _refreshVersion)) return;
            _logger.LogError(
                exception,
                "Timesheet filing refresh failed. RefreshVersion={RefreshVersion} Month={Year:D4}-{Month:D2}",
                version,
                year,
                month);
            Message = $"Timesheet filing delay could not be loaded: {exception.Message}";
            IsError = true;
            Changed?.Invoke();
        }
    }

    public Task SetMonthAsync(DateTime month)
    {
        var normalized = new DateTime(month.Year, month.Month, 1);
        if (!IsFiscalMonth(normalized))
        {
            ShowMessage($"Reporting months are limited to the 2026–27 fiscal year ({FiscalYearStart:MMM yyyy} to {FiscalYearEnd:MMM yyyy}).", true);
            return Task.CompletedTask;
        }
        SelectedMonth = normalized;
        return RefreshAsync();
    }

    public Task PreviousMonthAsync() => CanGoPrevious ? SetMonthAsync(SelectedMonth.AddMonths(-1)) : Task.CompletedTask;
    public Task NextMonthAsync() => CanGoNext ? SetMonthAsync(SelectedMonth.AddMonths(1)) : Task.CompletedTask;

    public void ShowMessage(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
        Changed?.Invoke();
    }

    public void ClearMessage()
    {
        Message = string.Empty;
        Changed?.Invoke();
    }

    public async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        IsError = false;
        Changed?.Invoke();
        try { await action(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An action run through AppState.RunAsync failed.");
            Message = ex.Message;
            IsError = true;
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    private static IReadOnlyList<MonthlyPerformanceItem> ConsolidatePerformance(
        IEnumerable<MonthlyPerformanceItem> source,
        OperationalScoringSettings scoring)
    {
        return source
            .Where(x => PersonName.Normalize(x.EmployeeName).Length > 0)
            .GroupBy(x => (x.Year, x.Month, Name: PersonName.Normalize(x.EmployeeName).ToUpperInvariant()))
            .Select(group => MergePerformanceGroup(group.ToArray(), scoring))
            .ToArray();
    }

    private static MonthlyPerformanceItem MergePerformanceGroup(
        IReadOnlyList<MonthlyPerformanceItem> rows,
        OperationalScoringSettings scoring)
    {
        var normalizedRows = rows.Select(NormalizePerformanceName).ToArray();
        if (normalizedRows.Length == 1) return normalizedRows[0];

        var name = PersonName.Normalize(normalizedRows[0].EmployeeName);
        var summary = normalizedRows
            .OrderByDescending(SummaryEvidence)
            .ThenByDescending(x => x.OperationalScore)
            .First();
        var attendance = normalizedRows
            .OrderByDescending(x => x.ExpectedTimesheetDays)
            .ThenByDescending(x => x.PunchHours)
            .First();
        var detailed = normalizedRows
            .OrderByDescending(x => x.DetailedEntries)
            .ThenByDescending(x => x.DetailedHours)
            .First();
        var employeeCode = normalizedRows
            .Select(x => x.EmployeeCode)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        MetricInput[] metrics =
        [
            new("timesheet", summary.TimesheetCompletionScore, scoring.TimesheetCompletionWeight, summary.ComplianceHours > 0),
            new("approval", summary.ApprovalScore, scoring.ApprovalCompletionWeight, summary.EnteredHours > 0),
            new("attendance", attendance.AttendanceDisciplineScore, scoring.AttendanceDisciplineWeight, attendance.ExpectedTimesheetDays > 0)
        ];
        var applicable = metrics.Where(x => x.IsApplicable && x.Weight > 0m).ToArray();
        var operationalScore = applicable.Length == 0 ? 0m : WeightedScoreCalculator.Calculate(applicable);

        return new MonthlyPerformanceItem(
            name,
            employeeCode,
            operationalScore,
            summary.TimesheetCompletionScore,
            summary.ApprovalScore,
            attendance.AttendanceDisciplineScore,
            summary.EnteredHours,
            summary.ComplianceHours,
            summary.BillableHours,
            detailed.DetailedHours,
            detailed.DetailedEntries,
            detailed.UniqueProjects,
            attendance.AttendanceDays,
            attendance.LeaveDays,
            attendance.MissingPunchDays,
            attendance.LateDays,
            attendance.EarlyDays,
            attendance.LessDurationDays,
            normalizedRows[0].Year,
            normalizedRows[0].Month,
            attendance.PunchHours,
            attendance.AttendanceTimesheetHours,
            attendance.TimesheetFilledDays,
            attendance.ExpectedTimesheetDays,
            summary.NonBillableHours,
            summary.TrainingHours,
            summary.ApprovedHours,
            summary.OfficeHours,
            summary.RawUtilization);
    }

    private static decimal SummaryEvidence(MonthlyPerformanceItem item) =>
        item.ComplianceHours + item.EnteredHours + item.ApprovedHours + item.BillableHours +
        item.NonBillableHours + item.TrainingHours + item.OfficeHours + item.RawUtilization;

    private static MonthlyPerformanceItem NormalizePerformanceName(MonthlyPerformanceItem item)
    {
        var normalized = PersonName.Normalize(item.EmployeeName);
        return string.Equals(normalized, item.EmployeeName, StringComparison.Ordinal)
            ? item
            : item with { EmployeeName = normalized };
    }

    private static DateTime DefaultReportingMonth()
    {
        var lastCompleted = DateTime.Today.AddMonths(-1);
        var normalized = new DateTime(lastCompleted.Year, lastCompleted.Month, 1);
        return normalized < FiscalYearStart ? FiscalYearStart : normalized > FiscalYearEnd ? FiscalYearEnd : normalized;
    }
}
