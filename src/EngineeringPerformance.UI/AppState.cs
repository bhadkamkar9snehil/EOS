using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.UI;

/// <summary>
/// Single shared source of the current reporting month and everything loaded for it — employees,
/// performance, history, weekly detail, teams, exclusions, peer reviews — plus the toast message
/// shown in the shell. Registered once for the app's lifetime.
/// </summary>
public sealed class AppState(IApplicationDatabase database)
{
    public DateTime SelectedMonth { get; private set; } = DefaultReportingMonth();
    public DashboardSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<EmployeeListItem> Employees { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> Performance { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> History { get; private set; } = [];
    public IReadOnlyList<WeeklyPerformanceItem> WeeklyPerformance { get; private set; } = [];
    public IReadOnlyList<string> ExcludedNames { get; private set; } = [];
    public IReadOnlyList<PeerReviewItem> PeerReviews { get; private set; } = [];
    public IReadOnlyList<TeamItem> Teams { get; private set; } = [];
    public DateTime LastRefresh { get; private set; } = DateTime.Now;

    public string Message { get; private set; } = string.Empty;
    public bool IsError { get; private set; }
    public bool Busy { get; private set; }

    public string? SpotlightName { get; set; }

    public int ReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded) ?? 0;
    public int SystemReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded && x.ReportType != ReportType.EngineerReviewWorkbook) ?? 0;
    public bool ReviewsUploaded => Snapshot?.SourceSlots.Any(x => x.ReportType == ReportType.EngineerReviewWorkbook && x.Status == SourceStatus.Uploaded) ?? false;

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        var year = SelectedMonth.Year;
        var month = SelectedMonth.Month;

        var snapshotTask = database.GetDashboardAsync(year, month);
        var employeesTask = database.GetEmployeesAsync();
        var performanceTask = database.GetMonthlyPerformanceAsync(year, month);
        var historyTask = database.GetPerformanceHistoryAsync(year, month, 6);
        var weeklyTask = database.GetWeeklyPerformanceAsync(year, month);
        var excludedTask = database.GetExcludedNamesAsync();
        var peerReviewsTask = database.GetPeerReviewsAsync(year, month);
        var teamsTask = database.GetTeamsAsync();

        await Task.WhenAll(snapshotTask, employeesTask, performanceTask, historyTask,
            weeklyTask, excludedTask, peerReviewsTask, teamsTask);

        Snapshot = snapshotTask.Result;
        Employees = employeesTask.Result;
        Performance = performanceTask.Result;
        History = historyTask.Result;
        WeeklyPerformance = weeklyTask.Result;
        ExcludedNames = excludedTask.Result;
        PeerReviews = peerReviewsTask.Result;
        Teams = teamsTask.Result;
        LastRefresh = DateTime.Now;
        Changed?.Invoke();
    }

    public Task SetMonthAsync(DateTime month)
    {
        SelectedMonth = new DateTime(month.Year, month.Month, 1);
        return RefreshAsync();
    }

    public Task PreviousMonthAsync() => SetMonthAsync(SelectedMonth.AddMonths(-1));
    public Task NextMonthAsync() => SetMonthAsync(SelectedMonth.AddMonths(1));

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
        catch (Exception ex) { Message = ex.Message; IsError = true; }
        finally { Busy = false; Changed?.Invoke(); }
    }

    private static DateTime DefaultReportingMonth()
    {
        var lastCompleted = DateTime.Today.AddMonths(-1);
        return new DateTime(lastCompleted.Year, lastCompleted.Month, 1);
    }
}
