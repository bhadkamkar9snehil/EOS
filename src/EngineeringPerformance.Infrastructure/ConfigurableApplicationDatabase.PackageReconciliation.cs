using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class ConfigurableApplicationDatabase
{
    private async Task ReconcileAllSourceMonthsAsync(
        OperationalScoringSettings settings,
        CancellationToken cancellationToken)
    {
        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var months = await context.ImportedSourceFiles.AsNoTracking()
                .Where(x => PerformanceSourceTypes.Contains(x.ReportType))
                .Select(x => new { x.Year, x.Month })
                .Distinct()
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToArrayAsync(cancellationToken);

            foreach (var month in months)
            {
                await ReconcileMonthAsync(
                    month.Year,
                    month.Month,
                    Array.Empty<string>(),
                    settings,
                    cancellationToken);
            }
        }

        await RemoveSupersededSnapshotSlotsAsync(cancellationToken);
    }

    private async Task RemoveSupersededSnapshotSlotsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var slots = await context.ImportedSourceFiles
            .Where(x => SnapshotPerformanceSourceTypes.Contains(x.ReportType))
            .OrderBy(x => x.ReportType)
            .ThenBy(x => x.ImportedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (slots.Count == 0) return;

        var obsolete = new List<ImportedSourceFile>();
        foreach (var group in slots.GroupBy(x => x.ReportType))
        {
            var authoritative = group
                .OrderByDescending(x => x.ImportedUtc)
                .ThenByDescending(x => x.Id)
                .First();
            if (!File.Exists(authoritative.StoredPath))
            {
                _logger.LogWarning(
                    "Cannot retire superseded {ReportType} source slots because the authoritative source is missing at {StoredPath}.",
                    group.Key,
                    authoritative.StoredPath);
                continue;
            }

            HashSet<(int Year, int Month)> sourceMonths;
            try
            {
                sourceMonths = workbookService.ReadPerformance(
                        authoritative.StoredPath,
                        group.Key,
                        authoritative.Year,
                        authoritative.Month)
                    .Select(x => (x.Year, x.Month))
                    .ToHashSet();
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Cannot retire superseded {ReportType} source slots because the authoritative source could not be replayed from {StoredPath}.",
                    group.Key,
                    authoritative.StoredPath);
                continue;
            }

            foreach (var slot in group)
            {
                if (slot.Id == authoritative.Id) continue;
                if (!string.Equals(slot.StoredPath, authoritative.StoredPath, StringComparison.OrdinalIgnoreCase) ||
                    !sourceMonths.Contains((slot.Year, slot.Month)))
                {
                    obsolete.Add(slot);
                }
            }
        }

        if (obsolete.Count == 0) return;
        context.ImportedSourceFiles.RemoveRange(obsolete);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Retired {Count} superseded full-history source slots after canonical reconciliation.", obsolete.Count);
    }
}
