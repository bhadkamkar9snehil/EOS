using System.Text.Json;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Adds persisted operational-score configuration and recalculation to the local database
/// without changing the existing SQLite schema. Settings are stored beside the database.
/// </summary>
public sealed class ConfigurableApplicationDatabase(
    LocalApplicationDatabase inner,
    IDbContextFactory<PerformanceDbContext> contextFactory,
    string dataDirectory) : IApplicationDatabase
{
    private readonly string _settingsPath = Path.Combine(dataDirectory, "operational-scoring.json");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await inner.InitializeAsync(cancellationToken);
        if (!File.Exists(_settingsPath))
            await WriteSettingsAsync(OperationalScoringSettings.Default, cancellationToken);
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

        await WriteSettingsAsync(settings, cancellationToken);
        return await RecalculateAllAsync(settings, cancellationToken);
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
            var metrics = new[]
            {
                new MetricInput("timesheet", row.TimesheetCompletionScore, settings.TimesheetCompletionWeight / 100m, row.ComplianceHours > 0),
                new MetricInput("approval", row.ApprovalScore, settings.ApprovalCompletionWeight / 100m, row.EnteredHours > 0),
                new MetricInput("attendance", row.AttendanceDisciplineScore, settings.AttendanceDisciplineWeight / 100m, row.ExpectedTimesheetDays > 0)
            };
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

    public async Task<int> ImportPackageAsync(int year, int month, string zipPath, CancellationToken cancellationToken = default)
    {
        var count = await inner.ImportPackageAsync(year, month, zipPath, cancellationToken);
        await RecalculateAfterImportAsync(cancellationToken);
        return count;
    }

    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetMonthlyPerformanceAsync(int year, int month, CancellationToken cancellationToken = default) => inner.GetMonthlyPerformanceAsync(year, month, cancellationToken);
    public Task<IReadOnlyList<MonthlyPerformanceItem>> GetPerformanceHistoryAsync(int year, int month, int monthsBack, CancellationToken cancellationToken = default) => inner.GetPerformanceHistoryAsync(year, month, monthsBack, cancellationToken);
    public Task<int> ImportEngineerReviewsAsync(int year, int month, string path, CancellationToken cancellationToken = default) => inner.ImportEngineerReviewsAsync(year, month, path, cancellationToken);
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
}
