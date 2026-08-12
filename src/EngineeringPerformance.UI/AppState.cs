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

    /// <summary>"light", "dark", or "system" — mirrors the [data-theme] attribute wwwroot/theme.js
    /// manages, kept here purely so every component showing the current choice (header toggle,
    /// Settings picker) re-renders together via <see cref="Changed"/>.</summary>
    public string ThemeMode { get; private set; } = "system";

    public async Task SetThemeAsync(IJSRuntime js, string mode)
    {
        await js.InvokeVoidAsync("epaTheme.set", mode);
        ThemeMode = mode;
        Changed?.Invoke();
    }

    public async Task LoadThemeAsync(IJSRuntime js)
    {
        ThemeMode = await js.InvokeAsync<string>("epaTheme.get");
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
        InteractionLog.Write("state.refresh.started", $"version={version}; month={year:D4}-{month:D2}");

        try
        {
            var dashboardTask = database.GetDashboardAsync(year, month);
            var employeesTask = database.GetEmployeesAsync();
            var performanceTask = database.GetMonthlyPerformanceAsync(year, month);
            var historyTask = database.GetPerformanceHistoryAsync(year, month, FiscalMonthsThrough(SelectedMonth));
            var exclusionsTask = database.GetExcludedNamesAsync();
            var reviewsTask = database.GetPeerReviewsAsync(year, month);
            var teamsTask = database.GetTeamsAsync();

            await Task.WhenAll(dashboardTask, employeesTask, performanceTask, historyTask, exclusionsTask, reviewsTask, teamsTask);
            if (version != Volatile.Read(ref _refreshVersion))
            {
                InteractionLog.Write("state.refresh.superseded", $"version={version}; month={year:D4}-{month:D2}");
                return;
            }

            Snapshot = await dashboardTask;
            Employees = await employeesTask;
            Performance = await performanceTask;
            History = (await historyTask)
                .Where(x => IsFiscalMonth(new DateTime(x.Year, x.Month, 1)))
                .ToArray();
            ExcludedNames = await exclusionsTask;
            PeerReviews = await reviewsTask;
            Teams = await teamsTask;
            LastRefresh = DateTime.Now;
            InteractionLog.Write("state.refresh.completed", $"version={version}; month={year:D4}-{month:D2}; people={Performance.Count}; reviews={PeerReviews.Count}");
            Changed?.Invoke();

            _ = LoadWeeklyPerformanceAsync(version, year, month);
            _ = LoadTimesheetFilingAsync(version, year, month);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "AppState.RefreshAsync failed for {Year:D4}-{Month:D2}.", year, month);
            InteractionLog.Write("state.refresh.failed", $"version={version}; month={year:D4}-{month:D2}", exception);
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
            InteractionLog.Write(
                "timesheet-filing.refresh.completed",
                $"version={version}; month={year:D4}-{month:D2}; people={snapshot.Rows.Count}; avgDelayDays={snapshot.AverageDelayDays}");
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _refreshVersion)) return;
            InteractionLog.Write("timesheet-filing.refresh.failed", $"version={version}; month={year:D4}-{month:D2}", exception);
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

    private static DateTime DefaultReportingMonth()
    {
        var lastCompleted = DateTime.Today.AddMonths(-1);
        var normalized = new DateTime(lastCompleted.Year, lastCompleted.Month, 1);
        return normalized < FiscalYearStart ? FiscalYearStart : normalized > FiscalYearEnd ? FiscalYearEnd : normalized;
    }
}
