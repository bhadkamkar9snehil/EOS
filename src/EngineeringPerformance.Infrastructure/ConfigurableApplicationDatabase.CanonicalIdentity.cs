using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class ConfigurableApplicationDatabase
{
    private static readonly ReportType[] PerformanceSourceTypes =
    [
        ReportType.MonthlyTimesheetSummary,
        ReportType.DetailedTimesheetTransactions,
        ReportType.AttendanceLeaveUaaTimesheet
    ];

    async Task IApplicationDatabase.InitializeAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var settings = await GetOperationalScoringSettingsAsync(cancellationToken);
        await ReconcileProblemIdentitiesAsync(settings, cancellationToken);
    }

    async Task IApplicationDatabase.ImportSourceAsync(
        ReportType reportType,
        int year,
        int month,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var incoming = workbookService.ReadPerformance(sourcePath, reportType, year, month);
        await ImportSourceAsync(reportType, year, month, sourcePath, cancellationToken);

        var settings = await GetOperationalScoringSettingsAsync(cancellationToken);
        foreach (var monthGroup in incoming.GroupBy(x => (x.Year, x.Month)))
        {
            await ReconcileMonthAsync(
                monthGroup.Key.Year,
                monthGroup.Key.Month,
                monthGroup.Select(x => x.EmployeeName),
                settings,
                cancellationToken);
        }
    }

    async Task<ImportPreview> IApplicationDatabase.PreviewImportSourceAsync(
        ReportType reportType,
        int year,
        int month,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var detectedType = workbookService.DetectReportType(sourcePath);
        if (detectedType != reportType)
            throw new InvalidDataException($"This file is {detectedType}, not {reportType}.");

        var incoming = workbookService.ReadPerformance(sourcePath, reportType, year, month)
            .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
            .Select(group => CombineSourceRows(group, reportType))
            .ToArray();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var coveredMonths = incoming.Select(x => (x.Year, x.Month)).Distinct().ToArray();
        var existing = coveredMonths.Length == 0
            ? []
            : await context.EmployeeMonthlyPerformances
                .Where(x => coveredMonths.Select(m => m.Year).Contains(x.Year))
                .ToListAsync(cancellationToken);

        var slots = coveredMonths.Length == 0
            ? []
            : await context.ImportedSourceFiles
                .Where(x => x.ReportType == reportType && coveredMonths.Select(m => m.Year).Contains(x.Year))
                .ToListAsync(cancellationToken);

        var currentSource = new Dictionary<(int Year, int Month, string Name), EmployeeMonthlyPerformance>();
        foreach (var slot in slots)
        {
            if (!File.Exists(slot.StoredPath)) continue;
            try
            {
                var rows = workbookService.ReadPerformance(slot.StoredPath, reportType, slot.Year, slot.Month)
                    .Where(x => x.Year == slot.Year && x.Month == slot.Month)
                    .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
                    .Select(group => CombineSourceRows(group, reportType));
                foreach (var row in rows)
                    currentSource[(row.Year, row.Month, IdentityKey(row.EmployeeName))] = row;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not replay current {ReportType} source {StoredPath} while previewing canonical import identity.", reportType, slot.StoredPath);
            }
        }

        var existingByIdentity = existing
            .GroupBy(x => (x.Year, x.Month, Name: IdentityKey(x.EmployeeName)))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var added = 0;
        var updated = 0;
        var unchanged = 0;
        var sampleAdded = new List<string>();
        var sampleUpdated = new List<string>();

        foreach (var row in incoming)
        {
            var key = (row.Year, row.Month, IdentityKey(row.EmployeeName));
            if (!existingByIdentity.TryGetValue(key, out var legacyRows))
            {
                added++;
                if (sampleAdded.Count < 10) sampleAdded.Add(PersonName.Normalize(row.EmployeeName));
                continue;
            }

            var current = currentSource.GetValueOrDefault(key)
                ?? SelectLegacySource(legacyRows, reportType);
            if (current is null || !SourceSnapshot(current, reportType).Equals(SourceSnapshot(row, reportType)))
            {
                updated++;
                if (sampleUpdated.Count < 10) sampleUpdated.Add(PersonName.Normalize(row.EmployeeName));
            }
            else
            {
                unchanged++;
            }
        }

        return new ImportPreview(
            reportType,
            year,
            month,
            incoming.Length,
            added,
            updated,
            unchanged,
            sampleAdded,
            sampleUpdated);
    }

    async Task<int> IApplicationDatabase.ImportPackageAsync(
        int year,
        int month,
        string zipPath,
        CancellationToken cancellationToken)
    {
        var imported = await ImportPackageAsync(year, month, zipPath, cancellationToken);
        var settings = await GetOperationalScoringSettingsAsync(cancellationToken);
        await ReconcileProblemIdentitiesAsync(settings, cancellationToken);
        return imported;
    }

    private async Task ReconcileProblemIdentitiesAsync(
        OperationalScoringSettings settings,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.EmployeeMonthlyPerformances.AsNoTracking().ToListAsync(cancellationToken);
        var problemGroups = rows
            .GroupBy(x => (x.Year, x.Month, Name: IdentityKey(x.EmployeeName)))
            .Where(group => group.Count() > 1 || group.Any(x => !string.Equals(x.EmployeeName, PersonName.Normalize(x.EmployeeName), StringComparison.Ordinal)))
            .GroupBy(group => (group.Key.Year, group.Key.Month))
            .ToArray();

        foreach (var month in problemGroups)
        {
            await ReconcileMonthAsync(
                month.Key.Year,
                month.Key.Month,
                month.Select(group => group.First().EmployeeName),
                settings,
                cancellationToken);
        }
    }

    private async Task ReconcileMonthAsync(
        int year,
        int month,
        IEnumerable<string> employeeNames,
        OperationalScoringSettings settings,
        CancellationToken cancellationToken)
    {
        var requestedKeys = employeeNames
            .Select(IdentityKey)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (requestedKeys.Count == 0) return;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingRows = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year == year && x.Month == month)
            .ToListAsync(cancellationToken);
        var existingByIdentity = existingRows
            .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
            .Where(group => requestedKeys.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.OrderBy(x => x.Id).ToArray(), StringComparer.Ordinal);

        var slots = await context.ImportedSourceFiles.AsNoTracking()
            .Where(x => x.Year == year && x.Month == month && PerformanceSourceTypes.Contains(x.ReportType))
            .OrderBy(x => x.ImportedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var slotsByType = slots.ToDictionary(x => x.ReportType);
        var currentByType = new Dictionary<ReportType, Dictionary<string, EmployeeMonthlyPerformance>>();
        var replayedTypes = new HashSet<ReportType>();

        foreach (var slot in slots)
        {
            if (!File.Exists(slot.StoredPath))
            {
                _logger.LogWarning("Current {ReportType} source is missing at {StoredPath}; preserving legacy evidence for that source while reconciling identities.", slot.ReportType, slot.StoredPath);
                continue;
            }

            try
            {
                var rows = workbookService.ReadPerformance(slot.StoredPath, slot.ReportType, year, month)
                    .Where(x => x.Year == year && x.Month == month)
                    .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => CombineSourceRows(group, slot.ReportType),
                        StringComparer.Ordinal);
                currentByType[slot.ReportType] = rows;
                replayedTypes.Add(slot.ReportType);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Could not replay current {ReportType} source {StoredPath}; preserving legacy evidence for that source while reconciling identities.", slot.ReportType, slot.StoredPath);
            }
        }

        var replacements = new List<EmployeeMonthlyPerformance>();
        var removals = new List<EmployeeMonthlyPerformance>();
        foreach (var key in requestedKeys)
        {
            existingByIdentity.TryGetValue(key, out var legacyRows);
            legacyRows ??= [];
            if (legacyRows.Length == 0 && currentByType.Values.All(map => !map.ContainsKey(key))) continue;

            var replacement = new EmployeeMonthlyPerformance { Year = year, Month = month };
            var fallbackName = legacyRows.Select(x => PersonName.Normalize(x.EmployeeName)).FirstOrDefault(x => x.Length > 0) ?? key;
            replacement.EmployeeName = fallbackName;
            replacement.EmployeeCode = legacyRows.Select(x => x.EmployeeCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            foreach (var type in PerformanceSourceTypes)
            {
                if (replayedTypes.Contains(type))
                {
                    if (currentByType[type].TryGetValue(key, out var current))
                    {
                        CopySource(replacement, current, type);
                        replacement.EmployeeName = PersonName.Normalize(current.EmployeeName);
                        if (!string.IsNullOrWhiteSpace(current.EmployeeCode)) replacement.EmployeeCode = current.EmployeeCode;
                    }
                    continue;
                }

                var legacy = SelectLegacySource(legacyRows, type);
                if (legacy is not null) CopySource(replacement, legacy, type);
            }

            Recalculate(replacement, settings);
            replacements.Add(replacement);
            removals.AddRange(legacyRows);
        }

        if (replacements.Count == 0) return;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (removals.Count > 0)
        {
            context.EmployeeMonthlyPerformances.RemoveRange(removals);
            await context.SaveChangesAsync(cancellationToken);
        }
        context.EmployeeMonthlyPerformances.AddRange(replacements);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Reconciled {IdentityCount} canonical performance identities for {Year:D4}-{Month:D2} from current source slots.",
            replacements.Count,
            year,
            month);
    }

    private static string IdentityKey(string? name) => PersonName.Normalize(name).ToUpperInvariant();

    private static EmployeeMonthlyPerformance CombineSourceRows(
        IEnumerable<EmployeeMonthlyPerformance> source,
        ReportType reportType)
    {
        var rows = source.ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("At least one source row is required.");
        if (rows.Length == 1) return rows[0];

        if (reportType == ReportType.MonthlyTimesheetSummary)
            return rows[^1];

        var combined = new EmployeeMonthlyPerformance
        {
            Year = rows[0].Year,
            Month = rows[0].Month,
            EmployeeName = PersonName.Normalize(rows[^1].EmployeeName),
            EmployeeCode = rows.Select(x => x.EmployeeCode).LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
        };

        if (reportType == ReportType.DetailedTimesheetTransactions)
        {
            combined.DetailedHours = rows.Sum(x => x.DetailedHours);
            combined.DetailedEntries = rows.Sum(x => x.DetailedEntries);
            combined.UniqueProjects = rows.Max(x => x.UniqueProjects);
        }
        else if (reportType == ReportType.AttendanceLeaveUaaTimesheet)
        {
            combined.AttendanceDays = rows.Sum(x => x.AttendanceDays);
            combined.LeaveDays = rows.Sum(x => x.LeaveDays);
            combined.PunchHours = rows.Sum(x => x.PunchHours);
            combined.AttendanceTimesheetHours = rows.Sum(x => x.AttendanceTimesheetHours);
            combined.TimesheetFilledDays = rows.Sum(x => x.TimesheetFilledDays);
            combined.ExpectedTimesheetDays = rows.Max(x => x.ExpectedTimesheetDays);
            combined.MissingPunchDays = rows.Sum(x => x.MissingPunchDays);
            combined.LateDays = rows.Sum(x => x.LateDays);
            combined.EarlyDays = rows.Sum(x => x.EarlyDays);
            combined.LessDurationDays = rows.Sum(x => x.LessDurationDays);
        }

        combined.Recalculate();
        return combined;
    }

    private static EmployeeMonthlyPerformance? SelectLegacySource(
        IReadOnlyCollection<EmployeeMonthlyPerformance> rows,
        ReportType type) => type switch
    {
        ReportType.MonthlyTimesheetSummary => rows
            .OrderByDescending(SummaryEvidence)
            .ThenBy(x => x.Id)
            .FirstOrDefault(),
        ReportType.DetailedTimesheetTransactions => rows
            .OrderByDescending(x => x.DetailedEntries)
            .ThenByDescending(x => x.DetailedHours)
            .ThenBy(x => x.Id)
            .FirstOrDefault(),
        ReportType.AttendanceLeaveUaaTimesheet => rows
            .OrderByDescending(x => x.ExpectedTimesheetDays)
            .ThenByDescending(x => x.PunchHours)
            .ThenBy(x => x.Id)
            .FirstOrDefault(),
        _ => null
    };

    private static decimal SummaryEvidence(EmployeeMonthlyPerformance item) =>
        item.ComplianceHours + item.EnteredHours + item.ApprovedHours + item.BillableHours +
        item.NonBillableHours + item.TrainingHours + item.OfficeHours + item.Utilization;

    private static void CopySource(
        EmployeeMonthlyPerformance target,
        EmployeeMonthlyPerformance source,
        ReportType type)
    {
        if (type == ReportType.MonthlyTimesheetSummary)
        {
            target.ComplianceHours = source.ComplianceHours;
            target.EnteredHours = source.EnteredHours;
            target.ApprovedHours = source.ApprovedHours;
            target.BillableHours = source.BillableHours;
            target.NonBillableHours = source.NonBillableHours;
            target.TrainingHours = source.TrainingHours;
            target.OfficeHours = source.OfficeHours;
            target.Utilization = source.Utilization;
        }
        else if (type == ReportType.DetailedTimesheetTransactions)
        {
            target.DetailedHours = source.DetailedHours;
            target.DetailedEntries = source.DetailedEntries;
            target.UniqueProjects = source.UniqueProjects;
        }
        else if (type == ReportType.AttendanceLeaveUaaTimesheet)
        {
            target.AttendanceDays = source.AttendanceDays;
            target.LeaveDays = source.LeaveDays;
            target.PunchHours = source.PunchHours;
            target.AttendanceTimesheetHours = source.AttendanceTimesheetHours;
            target.TimesheetFilledDays = source.TimesheetFilledDays;
            target.ExpectedTimesheetDays = source.ExpectedTimesheetDays;
            target.MissingPunchDays = source.MissingPunchDays;
            target.LateDays = source.LateDays;
            target.EarlyDays = source.EarlyDays;
            target.LessDurationDays = source.LessDurationDays;
        }
    }

    private static void Recalculate(EmployeeMonthlyPerformance row, OperationalScoringSettings settings)
    {
        row.Recalculate();
        MetricInput[] metrics =
        [
            new("timesheet", row.TimesheetCompletionScore, settings.TimesheetCompletionWeight, row.ComplianceHours > 0),
            new("approval", row.ApprovalScore, settings.ApprovalCompletionWeight, row.EnteredHours > 0),
            new("attendance", row.AttendanceDisciplineScore, settings.AttendanceDisciplineWeight, row.ExpectedTimesheetDays > 0)
        ];
        var applicable = metrics.Where(x => x.IsApplicable && x.Weight > 0m).ToArray();
        row.OperationalScore = applicable.Length == 0 ? 0m : WeightedScoreCalculator.Calculate(applicable);
    }

    private static object SourceSnapshot(EmployeeMonthlyPerformance row, ReportType type) => type switch
    {
        ReportType.MonthlyTimesheetSummary => new
        {
            row.ComplianceHours,
            row.EnteredHours,
            row.ApprovedHours,
            row.BillableHours,
            row.NonBillableHours,
            row.TrainingHours,
            row.OfficeHours,
            row.Utilization,
            row.EmployeeCode
        },
        ReportType.DetailedTimesheetTransactions => new
        {
            row.DetailedHours,
            row.DetailedEntries,
            row.UniqueProjects,
            row.EmployeeCode
        },
        ReportType.AttendanceLeaveUaaTimesheet => new
        {
            row.AttendanceDays,
            row.LeaveDays,
            row.PunchHours,
            row.AttendanceTimesheetHours,
            row.TimesheetFilledDays,
            row.ExpectedTimesheetDays,
            row.MissingPunchDays,
            row.LateDays,
            row.EarlyDays,
            row.LessDurationDays,
            row.EmployeeCode
        },
        _ => new { row.EmployeeCode }
    };
}