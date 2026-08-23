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

    private static readonly ReportType[] SnapshotPerformanceSourceTypes =
    [
        ReportType.DetailedTimesheetTransactions,
        ReportType.AttendanceLeaveUaaTimesheet
    ];

    private static bool IsSnapshotReportType(ReportType reportType) =>
        SnapshotPerformanceSourceTypes.Contains(reportType);

    private async Task<ImportPreview> PreviewCanonicalImportAsync(
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
        var incomingMonths = incoming
            .Select(x => (x.Year, x.Month))
            .Distinct()
            .DefaultIfEmpty((year, month))
            .ToArray();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var incomingYears = incomingMonths.Select(x => x.Year).Distinct().ToArray();
        var slots = IsSnapshotReportType(reportType)
            ? await context.ImportedSourceFiles
                .Where(x => x.ReportType == reportType)
                .ToListAsync(cancellationToken)
            : await context.ImportedSourceFiles
                .Where(x => x.ReportType == reportType && incomingYears.Contains(x.Year))
                .ToListAsync(cancellationToken);

        var replacementMonths = (IsSnapshotReportType(reportType)
                ? slots.Select(x => (x.Year, x.Month)).Concat(incomingMonths)
                : incomingMonths)
            .Distinct()
            .ToArray();
        var replacementMonthSet = replacementMonths.ToHashSet();
        var replacementYears = replacementMonths.Select(x => x.Year).Distinct().ToArray();
        var existing = await context.EmployeeMonthlyPerformances
            .Where(x => replacementYears.Contains(x.Year))
            .ToListAsync(cancellationToken);

        var authoritativeSnapshotPath = IsSnapshotReportType(reportType)
            ? slots.OrderByDescending(x => x.ImportedUtc).ThenByDescending(x => x.Id).Select(x => x.StoredPath).FirstOrDefault()
            : null;
        var currentSource = new Dictionary<(int Year, int Month, string Name), EmployeeMonthlyPerformance>();
        foreach (var slot in slots)
        {
            if (authoritativeSnapshotPath is not null &&
                !string.Equals(slot.StoredPath, authoritativeSnapshotPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!File.Exists(slot.StoredPath)) continue;

            try
            {
                foreach (var row in workbookService.ReadPerformance(slot.StoredPath, reportType, slot.Year, slot.Month)
                             .Where(x => x.Year == slot.Year && x.Month == slot.Month)
                             .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
                             .Select(group => CombineSourceRows(group, reportType)))
                {
                    currentSource[(row.Year, row.Month, IdentityKey(row.EmployeeName))] = row;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not replay current {ReportType} source {StoredPath} while previewing canonical import identity.",
                    reportType,
                    slot.StoredPath);
            }
        }

        var existingByIdentity = existing
            .GroupBy(x => (x.Year, x.Month, Name: IdentityKey(x.EmployeeName)))
            .ToDictionary(x => x.Key, x => x.ToArray());
        var incomingKeys = incoming
            .Select(x => (x.Year, x.Month, Name: IdentityKey(x.EmployeeName)))
            .ToHashSet();
        var removedRows = currentSource
            .Where(pair => replacementMonthSet.Contains((pair.Key.Year, pair.Key.Month)) && !incomingKeys.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToArray();

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

            var current = currentSource.GetValueOrDefault(key) ?? SelectLegacySource(legacyRows, reportType);
            if (current is null || !SameSourceEvidence(current, row, reportType))
            {
                updated++;
                if (sampleUpdated.Count < 10) sampleUpdated.Add(PersonName.Normalize(row.EmployeeName));
            }
            else
            {
                unchanged++;
            }
        }

        var sampleRemoved = removedRows
            .Select(x => PersonName.Normalize(x.EmployeeName))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        return new ImportPreview(
            reportType,
            year,
            month,
            incoming.Length,
            added,
            updated,
            unchanged,
            sampleAdded,
            sampleUpdated,
            removedRows.Length,
            sampleRemoved);
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

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingRows = await context.EmployeeMonthlyPerformances
            .Where(x => x.Year == year && x.Month == month)
            .ToListAsync(cancellationToken);
        requestedKeys.UnionWith(existingRows.Select(x => IdentityKey(x.EmployeeName)).Where(x => x.Length > 0));
        if (requestedKeys.Count == 0) return;

        var existingByIdentity = existingRows
            .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
            .Where(group => requestedKeys.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.OrderBy(x => x.Id).ToArray(), StringComparer.Ordinal);

        var snapshotSlots = await context.ImportedSourceFiles.AsNoTracking()
            .Where(x => SnapshotPerformanceSourceTypes.Contains(x.ReportType))
            .ToListAsync(cancellationToken);
        var authoritativeSnapshotPaths = snapshotSlots
            .GroupBy(x => x.ReportType)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(x => x.ImportedUtc).ThenByDescending(x => x.Id).First().StoredPath);
        var slots = await context.ImportedSourceFiles.AsNoTracking()
            .Where(x => x.Year == year && x.Month == month && PerformanceSourceTypes.Contains(x.ReportType))
            .OrderBy(x => x.ImportedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var currentByType = new Dictionary<ReportType, Dictionary<string, EmployeeMonthlyPerformance>>();
        var replayedTypes = new HashSet<ReportType>();

        foreach (var slot in slots)
        {
            var sourcePath = slot.StoredPath;
            if (IsSnapshotReportType(slot.ReportType) &&
                authoritativeSnapshotPaths.TryGetValue(slot.ReportType, out var authoritativePath) &&
                !string.Equals(sourcePath, authoritativePath, StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = authoritativePath;
            }

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning(
                    "Current {ReportType} source is missing at {StoredPath}; preserving latest persisted evidence for that source while reconciling identities.",
                    slot.ReportType,
                    sourcePath);
                continue;
            }

            try
            {
                currentByType[slot.ReportType] = workbookService.ReadPerformance(sourcePath, slot.ReportType, year, month)
                    .Where(x => x.Year == year && x.Month == month)
                    .GroupBy(x => IdentityKey(x.EmployeeName), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => CombineSourceRows(group, slot.ReportType),
                        StringComparer.Ordinal);
                replayedTypes.Add(slot.ReportType);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not replay current {ReportType} source {StoredPath}; preserving latest persisted evidence for that source while reconciling identities.",
                    slot.ReportType,
                    sourcePath);
            }
        }

        var replacements = new List<EmployeeMonthlyPerformance>();
        var removals = new List<EmployeeMonthlyPerformance>();
        foreach (var key in requestedKeys)
        {
            existingByIdentity.TryGetValue(key, out var legacyRows);
            legacyRows ??= [];
            if (legacyRows.Length == 0 && currentByType.Values.All(map => !map.ContainsKey(key))) continue;

            var replacement = new EmployeeMonthlyPerformance
            {
                Year = year,
                Month = month,
                EmployeeName = legacyRows
                    .OrderByDescending(x => x.Id)
                    .Select(x => PersonName.Normalize(x.EmployeeName))
                    .FirstOrDefault(x => x.Length > 0) ?? key,
                EmployeeCode = legacyRows
                    .OrderByDescending(x => x.Id)
                    .Select(x => x.EmployeeCode)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            };
            var hasAnySource = false;

            foreach (var type in PerformanceSourceTypes)
            {
                if (replayedTypes.Contains(type))
                {
                    if (currentByType[type].TryGetValue(key, out var current))
                    {
                        CopySource(replacement, current, type);
                        hasAnySource = true;
                        replacement.EmployeeName = PersonName.Normalize(current.EmployeeName);
                        if (!string.IsNullOrWhiteSpace(current.EmployeeCode)) replacement.EmployeeCode = current.EmployeeCode;
                    }
                    continue;
                }

                var legacy = SelectLegacySource(legacyRows, type);
                if (legacy is not null)
                {
                    CopySource(replacement, legacy, type);
                    hasAnySource = true;
                }
            }

            if (!hasAnySource)
            {
                removals.AddRange(legacyRows);
                continue;
            }

            Recalculate(replacement, settings);
            if (legacyRows.Length == 1 && SamePerformanceRow(legacyRows[0], replacement)) continue;

            removals.AddRange(legacyRows);
            replacements.Add(replacement);
        }

        if (removals.Count == 0 && replacements.Count == 0) return;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (removals.Count > 0)
        {
            context.EmployeeMonthlyPerformances.RemoveRange(removals);
            await context.SaveChangesAsync(cancellationToken);
        }
        if (replacements.Count > 0)
        {
            context.EmployeeMonthlyPerformances.AddRange(replacements);
            await context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Reconciled {IdentityCount} canonical performance identities for {Year:D4}-{Month:D2} from current source slots.",
            requestedKeys.Count,
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
        if (reportType == ReportType.MonthlyTimesheetSummary) return rows[^1];

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
            combined.ExpectedTimesheetDays = rows.Sum(x => x.ExpectedTimesheetDays);
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
        ReportType type) => rows
            .Where(row => HasSourceEvidence(row, type))
            .OrderByDescending(row => row.Id)
            .FirstOrDefault();

    private static bool HasSourceEvidence(EmployeeMonthlyPerformance row, ReportType type) => type switch
    {
        ReportType.MonthlyTimesheetSummary =>
            row.ComplianceHours != 0m || row.EnteredHours != 0m || row.ApprovedHours != 0m ||
            row.BillableHours != 0m || row.NonBillableHours != 0m || row.TrainingHours != 0m ||
            row.OfficeHours != 0m || row.Utilization != 0m,
        ReportType.DetailedTimesheetTransactions =>
            row.DetailedHours != 0m || row.DetailedEntries != 0 || row.UniqueProjects != 0,
        ReportType.AttendanceLeaveUaaTimesheet =>
            row.AttendanceDays != 0m || row.LeaveDays != 0m || row.PunchHours != 0m ||
            row.AttendanceTimesheetHours != 0m || row.TimesheetFilledDays != 0m ||
            row.ExpectedTimesheetDays != 0m || row.MissingPunchDays != 0 || row.LateDays != 0 ||
            row.EarlyDays != 0 || row.LessDurationDays != 0,
        _ => false
    };

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

    private static bool SamePerformanceRow(EmployeeMonthlyPerformance left, EmployeeMonthlyPerformance right) =>
        left.Year == right.Year &&
        left.Month == right.Month &&
        string.Equals(left.EmployeeName, right.EmployeeName, StringComparison.Ordinal) &&
        string.Equals(left.EmployeeCode, right.EmployeeCode, StringComparison.OrdinalIgnoreCase) &&
        left.OperationalScore == right.OperationalScore &&
        left.TimesheetCompletionScore == right.TimesheetCompletionScore &&
        left.ApprovalScore == right.ApprovalScore &&
        left.AttendanceDisciplineScore == right.AttendanceDisciplineScore &&
        left.ComplianceHours == right.ComplianceHours &&
        left.EnteredHours == right.EnteredHours &&
        left.ApprovedHours == right.ApprovedHours &&
        left.BillableHours == right.BillableHours &&
        left.NonBillableHours == right.NonBillableHours &&
        left.TrainingHours == right.TrainingHours &&
        left.OfficeHours == right.OfficeHours &&
        left.Utilization == right.Utilization &&
        left.DetailedHours == right.DetailedHours &&
        left.DetailedEntries == right.DetailedEntries &&
        left.UniqueProjects == right.UniqueProjects &&
        left.AttendanceDays == right.AttendanceDays &&
        left.LeaveDays == right.LeaveDays &&
        left.PunchHours == right.PunchHours &&
        left.AttendanceTimesheetHours == right.AttendanceTimesheetHours &&
        left.TimesheetFilledDays == right.TimesheetFilledDays &&
        left.ExpectedTimesheetDays == right.ExpectedTimesheetDays &&
        left.MissingPunchDays == right.MissingPunchDays &&
        left.LateDays == right.LateDays &&
        left.EarlyDays == right.EarlyDays &&
        left.LessDurationDays == right.LessDurationDays;

    private static bool SameSourceEvidence(
        EmployeeMonthlyPerformance left,
        EmployeeMonthlyPerformance right,
        ReportType type) => type switch
    {
        ReportType.MonthlyTimesheetSummary =>
            left.ComplianceHours == right.ComplianceHours &&
            left.EnteredHours == right.EnteredHours &&
            left.ApprovedHours == right.ApprovedHours &&
            left.BillableHours == right.BillableHours &&
            left.NonBillableHours == right.NonBillableHours &&
            left.TrainingHours == right.TrainingHours &&
            left.OfficeHours == right.OfficeHours &&
            left.Utilization == right.Utilization &&
            IncomingCodeMatches(left.EmployeeCode, right.EmployeeCode),
        ReportType.DetailedTimesheetTransactions =>
            left.DetailedHours == right.DetailedHours &&
            left.DetailedEntries == right.DetailedEntries &&
            left.UniqueProjects == right.UniqueProjects &&
            IncomingCodeMatches(left.EmployeeCode, right.EmployeeCode),
        ReportType.AttendanceLeaveUaaTimesheet =>
            left.AttendanceDays == right.AttendanceDays &&
            left.LeaveDays == right.LeaveDays &&
            left.PunchHours == right.PunchHours &&
            left.AttendanceTimesheetHours == right.AttendanceTimesheetHours &&
            left.TimesheetFilledDays == right.TimesheetFilledDays &&
            left.ExpectedTimesheetDays == right.ExpectedTimesheetDays &&
            left.MissingPunchDays == right.MissingPunchDays &&
            left.LateDays == right.LateDays &&
            left.EarlyDays == right.EarlyDays &&
            left.LessDurationDays == right.LessDurationDays &&
            IncomingCodeMatches(left.EmployeeCode, right.EmployeeCode),
        _ => true
    };

    private static bool IncomingCodeMatches(string? storedCode, string? incomingCode) =>
        string.IsNullOrWhiteSpace(incomingCode) ||
        string.Equals(storedCode, incomingCode, StringComparison.OrdinalIgnoreCase);
}
