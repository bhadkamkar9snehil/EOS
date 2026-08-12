using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.UI.Tests;

/// <summary>
/// A minimal, fully in-memory stand-in for <see cref="IApplicationDatabase"/> used by bUnit
/// component tests instead of a real EF Core / SQLite database. Members not needed by the
/// components under test throw <see cref="NotSupportedException"/> so an accidental dependency
/// on unwired behavior fails loudly rather than silently returning empty data.
/// </summary>
public sealed class FakeApplicationDatabase : IApplicationDatabase
{
    public OperationalScoringSettings ScoringSettings { get; set; } = OperationalScoringSettings.Default;
    public Exception? SaveScoringSettingsThrows { get; set; }
    public int SavedScoringRowCount { get; set; } = 3;
    public List<OperationalScoringSettings> SavedScoringCalls { get; } = [];

    public IReadOnlyList<EmployeeListItem> Employees { get; set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> MonthlyPerformance { get; set; } = [];
    public IReadOnlyList<MonthlyPerformanceItem> History { get; set; } = [];
    public DashboardSnapshot Dashboard { get; set; } = new("Jan 2026", 0, [], 0, null);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<DashboardSnapshot> GetDashboardAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default) => Task.FromResult(Dashboard);
    public Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Employees);
    public Task AddEmployeeAsync(string employeeCode, string name, int seniorityLevel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateEmployeeAsync(int employeeId, string name, int seniorityLevel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult(MonthlyPerformance);
    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default) => Task.FromResult(History);
    public Task<IReadOnlyList<WeeklyPerformanceItem>> GetWeeklyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WeeklyPerformanceItem>>([]);

    public Task<OperationalScoringSettings> GetOperationalScoringSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ScoringSettings);

    public Task<int> SaveOperationalScoringSettingsAsync(OperationalScoringSettings settings, CancellationToken cancellationToken = default)
    {
        SavedScoringCalls.Add(settings);
        if (SaveScoringSettingsThrows is not null) return Task.FromException<int>(SaveScoringSettingsThrows);
        ScoringSettings = settings;
        return Task.FromResult(SavedScoringRowCount);
    }

    public Task<ReviewImportResult> ImportEngineerReviewsAsync(int year, int month, IReadOnlyList<string> paths, ReviewImportMode mode = ReviewImportMode.MergeReviewers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PeerReviewItem>>([]);
    public Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetProbationAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetUpdownAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ImportHistoryItem>> GetImportHistoryAsync(int? year = null, int? month = null, int take = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImportHistoryItem>>([]);
    public Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TeamItem>>([]);
    public Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteTeamAsync(int teamId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
