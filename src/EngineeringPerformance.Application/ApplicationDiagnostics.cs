namespace EngineeringPerformance.Application;

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
