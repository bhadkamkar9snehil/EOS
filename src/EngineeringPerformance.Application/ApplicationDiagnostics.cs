namespace EngineeringPerformance.Application;

/// <summary>Human-readable byte-size formatting shared by every page and service that reports file sizes.</summary>
public static class FileSizeFormat
{
    public static string Format(long bytes)
    {
        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}

/// <summary>
/// Snapshot of the local runtime information shown on the Diagnostics page. The UI deliberately
/// consumes this contract instead of knowing where the desktop host stores databases or log files.
/// </summary>
public sealed record ApplicationDiagnosticsInfo(
    string ApplicationVersion,
    string DatabasePath,
    string DatabaseSize,
    string LogDirectory,
    string CurrentLogPath);

/// <summary>
/// Local diagnostics operations exposed to the UI. Implementations own filesystem details,
/// log-file discovery and support-bundle creation so presentation code never depends on a logging
/// backend or a concrete filename convention.
/// </summary>
public interface IApplicationDiagnostics
{
    ApplicationDiagnosticsInfo GetInfo();

    Task<string> ReadRecentLogAsync(
        int maxLines,
        CancellationToken cancellationToken = default);

    Task<string> CreateBundleAsync(
        int days = 5,
        CancellationToken cancellationToken = default);
}
