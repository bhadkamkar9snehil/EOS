using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;

namespace EngineeringPerformance.DesktopHost;

/// <summary>
/// Deterministic, synthetic data used only when EOS_VISUAL_CAPTURE=1. The goal is to render the
/// real WPF + Blazor/WebView2 product with enough variation to exercise charts, tables, semantic
/// states and density without copying any user's local SQLite database into CI.
/// </summary>
internal sealed class VisualCaptureApplicationDatabase : IApplicationDatabase
{
    private static readonly DateTime CaptureMonth = new(2026, 7, 1);

    private static readonly Person[] People =
    [
        new(1, "E001", "Asha Nair",       5, 1, "Platform", 92m,  1m),
        new(2, "E002", "Rohan Mehta",     4, 1, "Platform", 86m,  2m),
        new(3, "E003", "Meera Iyer",      6, 2, "Atlas",    81m, -1m),
        new(4, "E004", "Kabir Shah",      3, 2, "Atlas",    77m,  4m),
        new(5, "E005", "Nisha Kulkarni",  4, 3, "Systems",  73m, -3m),
        new(6, "E006", "Arjun Rao",       5, 3, "Systems",  68m, -6m),
        new(7, "E007", "Priya Menon",     3, 1, "Platform", 64m,  5m),
        new(8, "E008", "Dev Patel",       2, 2, "Atlas",    59m, -5m),
        new(9, "E009", "Ishita Bose",     4, 3, "Systems",  54m, -8m),
        new(10,"E010", "Vikram Singh",    2, 1, "Platform", 49m, -3m),
        new(11,"E011", "Tara Desai",      3, 2, "Atlas",    44m, -7m),
        new(12,"E012", "Neel Joshi",      2, 3, "Systems",  38m, -9m),
    ];

    private readonly IReadOnlyList<EmployeeListItem> _employees = People.Select((p, index) =>
        new EmployeeListItem(
            p.Id,
            p.Code,
            p.Name,
            p.Level,
            false,
            $"{p.Code.ToLowerInvariant()}@example.invalid",
            false,
            index == 10,
            null,
            index == 11,
            p.TeamId,
            p.TeamName,
            index == 9,
            null)).ToArray();

    private readonly IReadOnlyList<MonthlyPerformanceItem> _monthly = People
        .Select((p, index) => Performance(p, CaptureMonth, p.Score, index))
        .ToArray();

    private readonly IReadOnlyList<MonthlyPerformanceItem> _history = BuildHistory();
    private readonly IReadOnlyList<WeeklyPerformanceItem> _weekly = BuildWeekly();
    private readonly IReadOnlyList<PeerReviewItem> _reviews = BuildPeerReviews();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<DashboardSnapshot> GetDashboardAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DashboardSnapshot(
            "July 2026",
            People.Length,
            [
                new SourceSlot(ReportType.MonthlyTimesheetSummary, SourceStatus.Uploaded, "monthly-summary.xlsx"),
                new SourceSlot(ReportType.DetailedTimesheetTransactions, SourceStatus.Uploaded, "detailed-timesheets.xlsx"),
                new SourceSlot(ReportType.AttendanceLeaveUaaTimesheet, SourceStatus.Uploaded, "attendance.xlsx"),
                new SourceSlot(ReportType.EngineerReviewWorkbook, SourceStatus.Uploaded, "peer-reviews.zip")
            ],
            7,
            decimal.Round(_monthly.Average(x => x.OperationalScore), 1)));

    public Task<IReadOnlyList<EmployeeListItem>> GetEmployeesAsync(CancellationToken cancellationToken = default) => Task.FromResult(_employees);
    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult(_monthly);
    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default) => Task.FromResult(_history);
    public Task<IReadOnlyList<WeeklyPerformanceItem>> GetWeeklyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult(_weekly);
    public Task<IReadOnlyList<PeerReviewItem>> GetPeerReviewsAsync(int year, int month, CancellationToken cancellationToken = default) => Task.FromResult(_reviews);
    public Task<IReadOnlyList<string>> GetExcludedNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<IReadOnlyList<TeamItem>> GetTeamsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TeamItem>>(
        [new(1, "Platform", 4), new(2, "Atlas", 4), new(3, "Systems", 4)]);

    public Task<TimesheetFilingSnapshot> GetTimesheetFilingAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var rows = People.Select((p, index) =>
            new TimesheetFilingRow(
                p.Name,
                p.Code,
                21 - (index % 3),
                decimal.Round(.25m + index * .16m, 1),
                decimal.Round(.8m + index * .37m, 1),
                new DateTime(2026, 7, Math.Clamp(3 + index, 1, 28)))).ToArray();
        return Task.FromResult(new TimesheetFilingSnapshot(2026, 7, rows, decimal.Round(rows.Average(x => x.AverageDelayDays), 1), null));
    }

    public Task<OperationalScoringSettings> GetOperationalScoringSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationalScoringSettings.Default);

    public Task<IReadOnlyList<ImportHistoryItem>> GetImportHistoryAsync(int? year = null, int? month = null, int take = 200, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImportHistoryItem>>(
        [
            new(4, ReportType.EngineerReviewWorkbook, 2026, 7, "peer-reviews.zip", 36, true, new DateTime(2026, 8, 1, 6, 12, 0, DateTimeKind.Utc)),
            new(3, ReportType.AttendanceLeaveUaaTimesheet, 2026, 7, "attendance.xlsx", 254, true, new DateTime(2026, 8, 1, 6, 8, 0, DateTimeKind.Utc)),
            new(2, ReportType.DetailedTimesheetTransactions, 2026, 7, "detailed-timesheets.xlsx", 982, true, new DateTime(2026, 8, 1, 6, 5, 0, DateTimeKind.Utc)),
            new(1, ReportType.MonthlyTimesheetSummary, 2026, 7, "monthly-summary.xlsx", 12, true, new DateTime(2026, 8, 1, 6, 2, 0, DateTimeKind.Utc))
        ]);

    public Task AddEmployeeAsync(string employeeCode, string name, int seniorityLevel, CancellationToken cancellationToken = default) => Unsupported();
    public Task UpdateEmployeeAsync(int employeeId, string name, int seniorityLevel, CancellationToken cancellationToken = default) => Unsupported();
    public Task RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken = default) => Unsupported();
    public Task ImportSourceAsync(ReportType reportType, int year, int month, string sourcePath, CancellationToken cancellationToken = default) => Unsupported();
    public Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default) => Unsupported<int>();
    public Task<int> SaveOperationalScoringSettingsAsync(OperationalScoringSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(3);
    public Task<ReviewImportResult> ImportEngineerReviewsAsync(int year, int month, IReadOnlyList<string> paths, ReviewImportMode mode = ReviewImportMode.MergeReviewers, CancellationToken cancellationToken = default) => Unsupported<ReviewImportResult>();
    public Task SetExclusionAsync(string employeeName, bool excluded, CancellationToken cancellationToken = default) => Unsupported();
    public Task<int> ImportEmployeeRosterAsync(string filePath, CancellationToken cancellationToken = default) => Unsupported<int>();
    public Task SetNonBillableAsync(int employeeId, bool value, CancellationToken cancellationToken = default) => Unsupported();
    public Task AssignTeamAsync(int employeeId, int? teamId, CancellationToken cancellationToken = default) => Unsupported();
    public Task SetProbationAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => Unsupported();
    public Task SetUpdownAsync(int employeeId, bool? value, CancellationToken cancellationToken = default) => Unsupported();
    public Task<int> AddTeamAsync(string name, CancellationToken cancellationToken = default) => Unsupported<int>();
    public Task RenameTeamAsync(int teamId, string name, CancellationToken cancellationToken = default) => Unsupported();
    public Task DeleteTeamAsync(int teamId, CancellationToken cancellationToken = default) => Unsupported();

    private static IReadOnlyList<MonthlyPerformanceItem> BuildHistory()
    {
        var months = new[] { new DateTime(2026, 4, 1), new DateTime(2026, 5, 1), new DateTime(2026, 6, 1), CaptureMonth };
        var rows = new List<MonthlyPerformanceItem>(People.Length * months.Length);
        foreach (var (month, monthIndex) in months.Select((value, index) => (value, index)))
        {
            foreach (var (person, personIndex) in People.Select((value, index) => (value, index)))
            {
                var score = Math.Clamp(person.Score - person.Delta * (months.Length - 1 - monthIndex) - ((personIndex + monthIndex) % 3 - 1), 28m, 96m);
                rows.Add(Performance(person, month, score, personIndex));
            }
        }
        return rows;
    }

    private static IReadOnlyList<WeeklyPerformanceItem> BuildWeekly()
    {
        var rows = new List<WeeklyPerformanceItem>();
        var weeks = new[] { new DateTime(2026, 7, 6), new DateTime(2026, 7, 13), new DateTime(2026, 7, 20), new DateTime(2026, 7, 27) };
        foreach (var (person, personIndex) in People.Select((value, index) => (value, index)))
        {
            foreach (var (week, weekIndex) in weeks.Select((value, index) => (value, index)))
            {
                rows.Add(new WeeklyPerformanceItem(
                    person.Name,
                    person.Code,
                    week,
                    35m + ((personIndex + weekIndex) % 5) * 1.8m,
                    18 + ((personIndex + weekIndex) % 8),
                    2 + ((personIndex + weekIndex) % 4),
                    38m + ((personIndex + weekIndex) % 6),
                    37m + ((personIndex + weekIndex) % 5),
                    4.5m - ((personIndex + weekIndex) % 3) * .25m,
                    5m,
                    (personIndex + weekIndex) % 4 == 0 ? 1 : 0,
                    (personIndex + weekIndex) % 5 == 0 ? 1 : 0,
                    (personIndex + weekIndex) % 7 == 0 ? 1 : 0,
                    (personIndex + weekIndex) % 6 == 0 ? 1 : 0));
            }
        }
        return rows;
    }

    private static IReadOnlyList<PeerReviewItem> BuildPeerReviews()
    {
        var rows = new List<PeerReviewItem>();
        for (var reviewer = 0; reviewer < 6; reviewer++)
        {
            for (var subject = 0; subject < 8; subject++)
            {
                if (reviewer == subject) continue;
                var baseScore = 3.2m + ((reviewer * 3 + subject) % 8) * .2m;
                rows.Add(new PeerReviewItem(
                    People[reviewer].Name,
                    People[reviewer].Code,
                    People[subject].Name,
                    People[subject].Code,
                    Math.Clamp(baseScore + .2m, 1m, 5m),
                    Math.Clamp(baseScore, 1m, 5m),
                    Math.Clamp(baseScore + .1m, 1m, 5m),
                    Math.Clamp(baseScore - .1m, 1m, 5m),
                    Math.Clamp(baseScore + .05m, 1m, 5m),
                    subject % 3 == 0 ? "Clear handoffs and dependable follow-through." : null));
            }
        }
        return rows;
    }

    private static MonthlyPerformanceItem Performance(Person p, DateTime month, decimal score, int index)
    {
        var completion = Math.Clamp(score + 5m - (index % 3), 35m, 99m);
        var approval = Math.Clamp(score + 2m + (index % 4), 35m, 99m);
        var attendance = Math.Clamp(score - 4m + (index % 5), 30m, 99m);
        var complianceHours = 176m;
        var enteredHours = 154m + (index % 6) * 6.4m;
        var utilization = decimal.Round(Math.Clamp(58m + score * .42m + (index % 4) * 2m, 48m, 98m), 1);
        var punchHours = 166m + (index % 5) * 3.2m;
        var timesheetHours = punchHours - (index % 4) * 1.7m + .8m;
        var expectedDays = 22m;
        var filledDays = Math.Max(15m, 22m - (index % 5) * .75m);

        return new MonthlyPerformanceItem(
            p.Name,
            p.Code,
            decimal.Round(score, 1),
            decimal.Round(completion, 1),
            decimal.Round(approval, 1),
            decimal.Round(attendance, 1),
            enteredHours,
            complianceHours,
            decimal.Round(enteredHours * utilization / 100m, 1),
            enteredHours - 2m,
            78 + index * 5,
            2 + index % 5,
            20.5m - (index % 4) * .5m,
            (index % 4) * .5m,
            index % 5 == 0 ? 2 : index % 3,
            index % 4,
            index % 3,
            index % 5,
            month.Year,
            month.Month,
            punchHours,
            timesheetHours,
            filledDays,
            expectedDays,
            index == 11 ? 28m : 6m + (index % 4) * 2m,
            2m + index % 3,
            decimal.Round(enteredHours * .94m, 1),
            decimal.Round(10m + index * .8m, 1),
            utilization);
    }

    private static Task Unsupported() => Task.FromException(new NotSupportedException("Visual-capture data is read-only."));
    private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException("Visual-capture data is read-only."));

    private sealed record Person(int Id, string Code, string Name, int Level, int TeamId, string TeamName, decimal Score, decimal Delta);
}
