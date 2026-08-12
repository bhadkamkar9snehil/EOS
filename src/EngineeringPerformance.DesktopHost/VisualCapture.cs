using System.Globalization;
using EngineeringPerformance.Application;
using EngineeringPerformance.Domain;
using Serilog;

namespace EngineeringPerformance.DesktopHost;

internal sealed record VisualCaptureOptions(
    string OutputFile,
    string Route,
    string Theme,
    int Width,
    int Height,
    string? FixtureDirectory)
{
    public static VisualCaptureOptions? FromEnvironment()
    {
        var outputFile = Environment.GetEnvironmentVariable("EOS_VISUAL_CAPTURE_FILE");
        if (string.IsNullOrWhiteSpace(outputFile)) return null;

        var route = Environment.GetEnvironmentVariable("EOS_VISUAL_ROUTE")?.Trim();
        route = string.IsNullOrWhiteSpace(route) ? "/overview" : route;
        if (!route.StartsWith('/')) route = "/" + route;

        var theme = Environment.GetEnvironmentVariable("EOS_VISUAL_THEME")?.Trim().ToLowerInvariant();
        theme = theme is "light" or "dark" or "system" ? theme : "light";

        var width = ParseDimension("EOS_VISUAL_WIDTH", 1600, 960, 3840);
        var height = ParseDimension("EOS_VISUAL_HEIGHT", 1000, 680, 2160);
        var fixtureDirectory = Environment.GetEnvironmentVariable("EOS_VISUAL_FIXTURE_DIR")?.Trim();

        return new VisualCaptureOptions(
            Path.GetFullPath(outputFile),
            route,
            theme,
            width,
            height,
            string.IsNullOrWhiteSpace(fixtureDirectory) ? null : Path.GetFullPath(fixtureDirectory));
    }

    private static int ParseDimension(string name, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}

internal static class VisualCaptureSeeder
{
    private const int FixtureYear = 2026;
    private const int FixtureMonth = 7;
    private const string MarkerName = "visual-fixtures-2026-07-v1.marker";

    public static async Task SeedAsync(
        IApplicationDatabase database,
        string dataDirectory,
        string fixtureDirectory,
        ILogger log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataDirectory);
        var marker = Path.Combine(dataDirectory, MarkerName);
        if (File.Exists(marker)) return;

        string Fixture(string name)
        {
            var path = Path.Combine(fixtureDirectory, name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Visual QA fixture was not found: {path}", path);
            return path;
        }

        log.Information("Seeding visual QA fixture data from {FixtureDirectory}", fixtureDirectory);

        await database.ImportEmployeeRosterAsync(
            Fixture("sample-EmployeeRoster.xlsx"), cancellationToken);

        await database.ImportSourceAsync(
            ReportType.MonthlyTimesheetSummary,
            FixtureYear,
            FixtureMonth,
            Fixture("sample-RPwiseTimesheetUtilazationReport-Jul2026.xlsx"),
            cancellationToken);

        await database.ImportSourceAsync(
            ReportType.DetailedTimesheetTransactions,
            FixtureYear,
            FixtureMonth,
            Fixture("sample-LV_Timesheet_ManagerHead_Rpt.xlsx"),
            cancellationToken);

        await database.ImportSourceAsync(
            ReportType.AttendanceLeaveUaaTimesheet,
            FixtureYear,
            FixtureMonth,
            Fixture("sample-LV_LeaveSummaryforRP.xlsx"),
            cancellationToken);

        await database.ImportEngineerReviewsAsync(
            FixtureYear,
            FixtureMonth,
            [Fixture("sample-2001_Asha_Kapoor_2026_07_Review.xlsx")],
            ReviewImportMode.ReplaceMonth,
            cancellationToken);

        File.WriteAllText(marker, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        log.Information("Visual QA fixture data seeded successfully.");
    }
}
