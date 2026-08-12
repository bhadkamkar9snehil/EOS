using System.Text.Json;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class ConfigurableApplicationDatabase
{
    private static readonly DirectiveItem TimesheetFilingDirective = new(
        "timesheet-filing",
        "Timesheet filing",
        "Accountable workday",
        "Next working day at the configured cutoff",
        "Latest Filled Date across all work-log rows for the day",
        "XInfoNxt detailed work-log export",
        "Medium",
        true);

    private static readonly DirectiveItem PeerReviewDirective = new(
        "peer-review-submission",
        "Peer review submission",
        "Review assigned",
        "Assignment plus 48 hours",
        "Review submitted event",
        "Review system event feed",
        "Medium",
        false);

    public async Task<ExecutionDisciplineSettings> GetExecutionDisciplineSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_disciplineSettingsPath)) return ExecutionDisciplineSettings.Default;
        try
        {
            await using var stream = File.OpenRead(_disciplineSettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<ExecutionDisciplineSettings>(stream, cancellationToken: cancellationToken);
            return settings?.IsValid == true ? settings : ExecutionDisciplineSettings.Default;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read {SettingsPath}; falling back to default execution-discipline settings.", _disciplineSettingsPath);
            return ExecutionDisciplineSettings.Default;
        }
    }

    public async Task SaveExecutionDisciplineSettingsAsync(ExecutionDisciplineSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.IsValid)
            throw new InvalidOperationException("The cutoff, grace period, and required daily hours must be within their allowed ranges.");
        await WriteDisciplineSettingsAsync(settings, cancellationToken);
    }

    private async Task WriteDisciplineSettingsAsync(ExecutionDisciplineSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_disciplineSettingsPath)!);
        var temporary = _disciplineSettingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temporary, _disciplineSettingsPath, true);
    }

    public async Task SetObligationExceptionAsync(
        string obligationKey,
        ObligationOutcome? outcome,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(obligationKey)) throw new ArgumentException("An obligation key is required.", nameof(obligationKey));
        if (outcome is not null && outcome is not (ObligationOutcome.Excused or ObligationOutcome.NotApplicable or ObligationOutcome.Waived))
            throw new ArgumentOutOfRangeException(nameof(outcome), "Only Excused, Not Applicable, and Waived are exception outcomes.");
        if (outcome is not null && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Record a reason before applying an exception.");

        var exceptions = (await ReadExceptionsAsync(cancellationToken)).ToDictionary(x => x.ObligationKey, StringComparer.OrdinalIgnoreCase);
        if (outcome is null)
            exceptions.Remove(obligationKey);
        else
            exceptions[obligationKey] = new ObligationExceptionItem(obligationKey, outcome.Value, reason!.Trim(), DateTime.Now);

        Directory.CreateDirectory(Path.GetDirectoryName(_disciplineExceptionsPath)!);
        var temporary = _disciplineExceptionsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, exceptions.Values.OrderBy(x => x.ObligationKey).ToArray(), new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        File.Move(temporary, _disciplineExceptionsPath, true);
    }

    private async Task<IReadOnlyList<ObligationExceptionItem>> ReadExceptionsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_disciplineExceptionsPath)) return [];
        try
        {
            await using var stream = File.OpenRead(_disciplineExceptionsPath);
            return await JsonSerializer.DeserializeAsync<ObligationExceptionItem[]>(stream, cancellationToken: cancellationToken) ?? [];
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Could not read {ExceptionsPath}; treating as no recorded obligation exceptions.", _disciplineExceptionsPath);
            return [];
        }
    }

    public async Task<ExecutionDisciplineSnapshot> GetExecutionDisciplineAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetExecutionDisciplineSettingsAsync(cancellationToken);
        var exceptions = (await ReadExceptionsAsync(cancellationToken)).ToDictionary(x => x.ObligationKey, StringComparer.OrdinalIgnoreCase);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var excluded = await ReadExcludedNamesAsync(context, cancellationToken);
        var sources = await context.ImportedSourceFiles
            .Where(x => x.Year == year && x.Month == month &&
                        (x.ReportType == ReportType.DetailedTimesheetTransactions ||
                         x.ReportType == ReportType.AttendanceLeaveUaaTimesheet))
            .ToListAsync(cancellationToken);

        var detailSource = sources.SingleOrDefault(x => x.ReportType == ReportType.DetailedTimesheetTransactions);
        var attendanceSource = sources.SingleOrDefault(x => x.ReportType == ReportType.AttendanceLeaveUaaTimesheet);
        var evidence = detailSource is not null && File.Exists(detailSource.StoredPath)
            ? workbookService.ReadTimesheetDayEvidence(detailSource.StoredPath, year, month)
            : [];
        var accountableDays = attendanceSource is not null && File.Exists(attendanceSource.StoredPath)
            ? workbookService.ReadAccountableWorkdays(attendanceSource.StoredPath, year, month)
            : [];

        var evidenceByDay = evidence
            .Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName)))
            .GroupBy(x => (Name: PersonName.Normalize(x.EmployeeName).ToUpperInvariant(), Date: x.WorkDate.Date))
            .ToDictionary(x => x.Key, x => x.OrderByDescending(row => row.LastFilledAt).First());

        var obligationDays = accountableDays
            .Where(x => !excluded.Contains(PersonName.Normalize(x.EmployeeName)))
            .ToDictionary(
                x => (Name: PersonName.Normalize(x.EmployeeName).ToUpperInvariant(), Date: x.WorkDate.Date),
                x => x);

        // A detailed row is still system evidence even when the attendance export has not arrived
        // yet. Include it as a completed obligation, then enrich it with roster/code data when the
        // attendance feed is available.
        foreach (var row in evidenceByDay)
        {
            if (!obligationDays.ContainsKey(row.Key))
                obligationDays[row.Key] = new AccountableWorkday(
                    row.Value.EmployeeName,
                    row.Value.EmployeeCode,
                    row.Value.WorkDate,
                    1m,
                    row.Value.SourceFileName);
        }

        var asOf = DateTime.Now;
        var obligations = new List<ObligationItem>(obligationDays.Count);
        foreach (var (key, day) in obligationDays.OrderBy(x => x.Key.Date).ThenBy(x => x.Value.EmployeeName))
        {
            evidenceByDay.TryGetValue(key, out var dayEvidence);
            var obligationKey = $"timesheet-filing:{key.Name}:{key.Date:yyyy-MM-dd}";
            exceptions.TryGetValue(obligationKey, out var exception);
            var dueAt = ObligationEvaluator.NextWorkingDayDeadline(
                day.WorkDate,
                settings.TimesheetDueHour,
                settings.TimesheetDueMinute,
                settings.SaturdayIsWorkingDay);
            var evaluation = ObligationEvaluator.Evaluate(
                dueAt,
                dayEvidence?.LastFilledAt,
                asOf,
                settings.GraceMinutes,
                exception?.Outcome);

            obligations.Add(new ObligationItem(
                obligationKey,
                TimesheetFilingDirective.Code,
                TimesheetFilingDirective.Name,
                day.EmployeeName,
                day.EmployeeCode ?? dayEvidence?.EmployeeCode,
                day.WorkDate,
                dueAt,
                dayEvidence?.LastFilledAt,
                evaluation.Outcome,
                evaluation.DelayMinutes,
                evaluation.MinutesRelativeToDeadline,
                settings.RequiredDailyHours * day.ExpectedDayWeight,
                dayEvidence?.RecordedHours ?? 0m,
                dayEvidence?.EntryCount ?? 0,
                dayEvidence?.SourceFileName ?? day.SourceFileName,
                exception?.Reason,
                exception?.RecordedAt));
        }

        string? notice = null;
        if (detailSource is null && attendanceSource is null)
            notice = "Import the detailed timesheet and attendance exports to generate daily obligations.";
        else if (detailSource is null)
            notice = "Accountable days are loaded, but the detailed work-log export is missing; completed evidence cannot be matched yet.";
        else if (attendanceSource is null)
            notice = "Work-log evidence is loaded, but the attendance export is missing; overdue days cannot be determined yet.";

        return new ExecutionDisciplineSnapshot(
            year,
            month,
            [TimesheetFilingDirective, PeerReviewDirective],
            obligations,
            notice);
    }
}
