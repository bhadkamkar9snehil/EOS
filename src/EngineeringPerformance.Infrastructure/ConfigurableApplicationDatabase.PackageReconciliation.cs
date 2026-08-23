using EngineeringPerformance.Application;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

public sealed partial class ConfigurableApplicationDatabase
{
    private async Task ReconcileAllSourceMonthsAsync(
        OperationalScoringSettings settings,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
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
}
