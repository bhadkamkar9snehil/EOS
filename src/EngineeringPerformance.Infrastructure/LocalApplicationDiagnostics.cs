using System.IO.Compression;
using System.Reflection;
using EngineeringPerformance.Application;
using Microsoft.Extensions.Logging;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Filesystem-backed diagnostics implementation for the local Windows application. This is the
/// only component that knows the concrete log filename convention; the Razor UI consumes only
/// <see cref="IApplicationDiagnostics"/>.
/// </summary>
public sealed class LocalApplicationDiagnostics(
    LocalApplicationPaths paths,
    ILogger<LocalApplicationDiagnostics> logger) : IApplicationDiagnostics
{
    public ApplicationDiagnosticsInfo GetInfo()
    {
        var databaseSize = File.Exists(paths.DatabasePath)
            ? FormatSize(new FileInfo(paths.DatabasePath).Length)
            : "(not found)";

        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString() ?? "unknown";

        return new ApplicationDiagnosticsInfo(
            version,
            paths.DatabasePath,
            databaseSize,
            paths.LogDirectory,
            paths.GetLogPath(DateTime.Now));
    }

    public async Task<string> ReadRecentLogAsync(
        int maxLines,
        CancellationToken cancellationToken = default)
    {
        if (maxLines <= 0) return string.Empty;

        var logPath = paths.GetLogPath(DateTime.Now);
        if (!File.Exists(logPath)) return "(no log file for today yet)";

        try
        {
            await using var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                useAsync: true);
            using var reader = new StreamReader(stream);

            var lines = new Queue<string>(Math.Min(maxLines, 256));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (lines.Count == maxLines) lines.Dequeue();
                lines.Enqueue(line);
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not read current EOS log file {LogPath}.", logPath);
            return $"(could not read log file: {exception.Message})";
        }
    }

    public Task<string> CreateBundleAsync(
        int days = 5,
        CancellationToken cancellationToken = default)
    {
        if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputDirectory = Path.Combine(Path.GetTempPath(), "EngineeringPerformance-Diagnostics");
            Directory.CreateDirectory(outputDirectory);
            var zipPath = Path.Combine(outputDirectory, $"eos-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var cutoff = DateTime.Now.AddDays(-days);

            if (Directory.Exists(paths.LogDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(paths.LogDirectory, "eos-*.log")
                    .Where(file => File.GetLastWriteTime(file) >= cutoff)
                    .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFileSnapshot(archive, file, Path.GetFileName(file));
                }
            }

            // Preserve the previous hand-written interaction log in support bundles if an install
            // still has one. Nothing writes to this file anymore after the unified logger ships.
            if (File.Exists(paths.LegacyInteractionLogPath))
            {
                AddFileSnapshot(archive, paths.LegacyInteractionLogPath, "legacy-interaction.log");
            }

            var info = GetInfo();
            var infoEntry = archive.CreateEntry("info.txt");
            using (var writer = new StreamWriter(infoEntry.Open()))
            {
                writer.WriteLine("EOS diagnostics bundle");
                writer.WriteLine($"Generated: {DateTime.Now:O}");
                writer.WriteLine($"App version: {info.ApplicationVersion}");
                writer.WriteLine($"Database path: {info.DatabasePath}");
                writer.WriteLine($"Database size: {info.DatabaseSize}");
                writer.WriteLine($"Log directory: {info.LogDirectory}");
                writer.WriteLine($"Machine: {Environment.MachineName}");
                writer.WriteLine($"OS: {Environment.OSVersion}");
            }

            logger.LogInformation(
                "Created diagnostics bundle {BundlePath} containing up to {Days} days of EOS logs.",
                zipPath,
                days);
            return zipPath;
        }, cancellationToken);
    }

    private static void AddFileSnapshot(ZipArchive archive, string sourcePath, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    private static string FormatSize(long bytes)
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
