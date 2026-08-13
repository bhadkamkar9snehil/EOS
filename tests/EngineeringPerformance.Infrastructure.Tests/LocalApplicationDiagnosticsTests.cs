using System.IO.Compression;
using EngineeringPerformance.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class LocalApplicationDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"eos-diagnostics-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Paths_define_one_canonical_local_layout()
    {
        var paths = new LocalApplicationPaths(_root);

        Assert.Equal(Path.GetFullPath(_root), paths.DataDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "engineering-performance.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "logs", "eos-.log"), paths.LogFilePattern);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "logs", $"eos-{DateTime.Today:yyyyMMdd}.log"), paths.GetLogPath(DateTime.Today));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "interaction.log"), paths.LegacyInteractionLogPath);
    }

    [Fact]
    public async Task ReadRecentLogAsync_returns_only_requested_tail()
    {
        var paths = new LocalApplicationPaths(_root);
        paths.EnsureDirectories();
        var today = paths.GetLogPath(DateTime.Now);
        await File.WriteAllLinesAsync(today, Enumerable.Range(1, 8).Select(x => $"line-{x}"));

        var diagnostics = new LocalApplicationDiagnostics(paths, NullLogger<LocalApplicationDiagnostics>.Instance);

        var tail = await diagnostics.ReadRecentLogAsync(3);

        Assert.Equal(string.Join(Environment.NewLine, ["line-6", "line-7", "line-8"]), tail);
    }

    [Fact]
    public async Task CreateBundleAsync_includes_unified_logs_and_preserves_legacy_log_as_read_only_evidence()
    {
        var paths = new LocalApplicationPaths(_root);
        paths.EnsureDirectories();
        await File.WriteAllTextAsync(paths.GetLogPath(DateTime.Now), "current log");
        await File.WriteAllTextAsync(paths.LegacyInteractionLogPath, "historical interaction log");

        var diagnostics = new LocalApplicationDiagnostics(paths, NullLogger<LocalApplicationDiagnostics>.Instance);

        var bundlePath = await diagnostics.CreateBundleAsync();

        try
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var names = archive.Entries.Select(x => x.FullName).ToArray();
            Assert.Contains($"eos-{DateTime.Now:yyyyMMdd}.log", names);
            Assert.Contains("legacy-interaction.log", names);
            Assert.Contains("info.txt", names);
        }
        finally
        {
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
