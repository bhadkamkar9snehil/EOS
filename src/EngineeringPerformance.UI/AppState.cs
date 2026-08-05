using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.UI;

/// <summary>
/// Single shared source of the current reporting month and everything loaded for it — employees,
/// performance, history, teams, exclusions, peer reviews — plus the toast message shown in the
/// shell. Registered once for the app's lifetime (this is a single-window desktop app, not a
/// multi-user server, so there is exactly one of these). Pages inject it directly instead of
/// each declaring the same half-dozen [Parameter]s and having App.razor pass them all down.
/// </summary>
public sealed class AppState(IApplicationDatabase database)
{
    public DateTime SelectedMonth { get; private set; } = DefaultReportingMonth();
    public DashboardSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<EmployeeListItem> Employees { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> Performance { get; private set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> History { get; private set; } = [];
    public IReadOnlyList<string> ExcludedNames { get; private set; } = [];
    public IReadOnlyList<PeerReviewItem> PeerReviews { get; private set; } = [];
    public IReadOnlyList<TeamItem> Teams { get; private set; } = [];
    public DateTime LastRefresh { get; private set; } = DateTime.Now;

    public string Message { get; private set; } = string.Empty;
    public bool IsError { get; private set; }
    public bool Busy { get; private set; }

    public int ReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded) ?? 0;
    public int SystemReadyCount => Snapshot?.SourceSlots.Count(x => x.Status == SourceStatus.Uploaded && x.ReportType != ReportType.EngineerReviewWorkbook) ?? 0;
    public bool ReviewsUploaded => Snapshot?.SourceSlots.Any(x => x.ReportType == ReportType.EngineerReviewWorkbook && x.Status == SourceStatus.Uploaded) ?? false;

    /// <summary>Raised after any state change, so components outside the normal parameter-cascade know to re-render.</summary>
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        Snapshot = await database.GetDashboardAsync(SelectedMonth.Year, SelectedMonth.Month);
        Employees = await database.GetEmployeesAsync();
        Performance = await database.GetMonthlyPerformanceAsync(SelectedMonth.Year, SelectedMonth.Month);
        History = await database.GetPerformanceHistoryAsync(SelectedMonth.Year, SelectedMonth.Month, 6);
        ExcludedNames = await database.GetExcludedNamesAsync();
        PeerReviews = await database.GetPeerReviewsAsync(SelectedMonth.Year, SelectedMonth.Month);
        Teams = await database.GetTeamsAsync();
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

    /// <summary>Runs an action with the busy flag held and the message cleared/reported, mirroring the pattern every page's own RunAsync used to hand-roll.</summary>
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
