namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Canonical local filesystem layout for the desktop application. Keeping these paths in one
/// object prevents the host, diagnostics UI and infrastructure services from independently
/// reconstructing slightly different locations and filename conventions.
/// </summary>
public sealed class LocalApplicationPaths
{
    public const string ProductDirectoryName = "EngineeringPerformance";
    public const string DatabaseFileName = "engineering-performance.db";
    public const string LogFilePrefix = "eos-";

    public LocalApplicationPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DataDirectory = Path.GetFullPath(dataDirectory);
        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
        LogDirectory = Path.Combine(DataDirectory, "logs");
        LogFilePattern = Path.Combine(LogDirectory, $"{LogFilePrefix}.log");
        LegacyInteractionLogPath = Path.Combine(DataDirectory, "interaction.log");
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string LogDirectory { get; }

    /// <summary>
    /// Serilog rolling-file pattern. With daily rolling it produces eos-YYYYMMDD.log.
    /// </summary>
    public string LogFilePattern { get; }

    /// <summary>
    /// Path used by the retired hand-written InteractionLog implementation. It is retained only
    /// so diagnostics bundles can preserve historical evidence after the new unified logger ships.
    /// </summary>
    public string LegacyInteractionLogPath { get; }

    public string GetLogPath(DateTime localDate) =>
        Path.Combine(LogDirectory, $"{LogFilePrefix}{localDate:yyyyMMdd}.log");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public static LocalApplicationPaths ForCurrentUser()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName);
        return new LocalApplicationPaths(root);
    }
}
