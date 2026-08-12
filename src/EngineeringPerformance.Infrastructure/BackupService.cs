using System.IO.Compression;
using EngineeringPerformance.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Exports a consistent snapshot of the live SQLite database (via "VACUUM INTO", so it never
/// competes with the app's own pooled connection for a file lock) plus the operational-scoring
/// config into a single timestamped zip, and restores from one. Restore always exports a safety
/// backup of the current database first, so a bad restore can itself be undone. Because the app
/// holds pooled connections to the database file for its whole lifetime, a restored file only
/// takes full effect after the application is restarted — the result flags that so the UI can
/// tell the user.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<PerformanceDbContext> contextFactory,
    string dataDirectory,
    string databasePath,
    string? defaultBackupDirectory = null,
    ILogger<BackupService>? logger = null) : IBackupService
{
    private const string DbEntryName = "engineering-performance.db";
    private const string ScoringEntryName = "operational-scoring.json";
    private const string PresetsEntryName = "scoring-presets.json";

    private readonly ILogger<BackupService> _logger = logger ?? NullLogger<BackupService>.Instance;

    // Tests must pass defaultBackupDirectory explicitly (a temp path) — leaving it null here falls
    // through to the real user's Documents folder, which is only correct for the production wiring
    // in ServiceCollectionExtensions.
    public string DefaultBackupDirectory { get; } = defaultBackupDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EngineeringPerformance-Backups");

    private string SafetyBackupDirectory => Path.Combine(DefaultBackupDirectory, "pre-restore-safety");

    public async Task<BackupResult> ExportBackupAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var targetDirectory = string.IsNullOrWhiteSpace(destinationDirectory) ? DefaultBackupDirectory : destinationDirectory;
        Directory.CreateDirectory(targetDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(targetDirectory, $"eos-backup-{timestamp}.zip");
        // Extremely unlikely, but a second backup within the same second would otherwise clobber.
        var suffix = 1;
        while (File.Exists(zipPath))
            zipPath = Path.Combine(targetDirectory, $"eos-backup-{timestamp}-{suffix++}.zip");

        // The app holds a pooled, long-lived native sqlite3 handle open on databasePath for its
        // entire lifetime (see the ClearAllPools comment in RestoreBackupAsync below), so a plain
        // FileStream/ZipArchive read of that path from here reliably loses a Windows file-sharing
        // race against it — confirmed by reproduction, not theoretical. "VACUUM INTO" instead asks
        // SQLite itself, through the same already-open connection, to write a fresh consistent
        // snapshot to a brand-new path that nothing else has open, which a plain FileStream can
        // then safely read.
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"eos-backup-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
                await context.Database.ExecuteSqlAsync($"VACUUM INTO {snapshotPath}", cancellationToken);

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(snapshotPath, DbEntryName, CompressionLevel.Optimal);

                var scoringPath = Path.Combine(dataDirectory, "operational-scoring.json");
                if (File.Exists(scoringPath))
                    archive.CreateEntryFromFile(scoringPath, ScoringEntryName, CompressionLevel.Optimal);

                var presetsPath = Path.Combine(dataDirectory, "scoring-presets.json");
                if (File.Exists(presetsPath))
                    archive.CreateEntryFromFile(presetsPath, PresetsEntryName, CompressionLevel.Optimal);
            }
        }
        catch (Exception exception)
        {
            // A failed export must not leave a corrupt/empty zip behind that "Recent backups"
            // would otherwise list as if it were a real one.
            if (File.Exists(zipPath)) File.Delete(zipPath);
            _logger.LogError(exception, "Backup export to {TargetDirectory} failed.", targetDirectory);
            throw;
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }

        var info = new FileInfo(zipPath);
        _logger.LogInformation("Backup exported to {ZipPath} ({SizeBytes} bytes).", zipPath, info.Length);
        return new BackupResult(zipPath, info.Length, DateTime.UtcNow);
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Restore requested from {BackupFilePath}. This will replace the live database.", backupFilePath);
        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("The selected backup file could not be found.", backupFilePath);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"eos-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            ZipFile.ExtractToDirectory(backupFilePath, temporaryDirectory);
            var extractedDb = Path.Combine(temporaryDirectory, DbEntryName);
            if (!File.Exists(extractedDb))
                throw new InvalidDataException("This file is not a valid EOS backup — no database was found inside it.");

            // Safety net: back up the current database before it gets overwritten, in case the
            // restore turns out to be a mistake.
            var safety = await ExportBackupAsync(SafetyBackupDirectory, cancellationToken);

            await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);

            // Microsoft.Data.Sqlite pools native sqlite3 handles by connection string, independent
            // of EF's own DbContext pool — the checkpoint context above still leaves a pooled native
            // handle open on databasePath (and its -wal/-shm side files) after being disposed. This
            // must run *before* the copy/delete below, not after: with the handle still open, the
            // side-file deletes fail outright on Windows with a sharing violation (confirmed by
            // reproduction), and any handle serving stale pages would otherwise keep doing so after
            // the copy, because it never re-reads the file from disk on its own.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            File.Copy(extractedDb, databasePath, overwrite: true);
            // Drop any stale WAL/SHM side files left over from before the restore — they refer to
            // the previous database's page layout and must not be replayed against the new one.
            foreach (var side in new[] { databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(side)) File.Delete(side);

            var extractedScoring = Path.Combine(temporaryDirectory, ScoringEntryName);
            if (File.Exists(extractedScoring))
                File.Copy(extractedScoring, Path.Combine(dataDirectory, "operational-scoring.json"), overwrite: true);

            var extractedPresets = Path.Combine(temporaryDirectory, PresetsEntryName);
            if (File.Exists(extractedPresets))
                File.Copy(extractedPresets, Path.Combine(dataDirectory, "scoring-presets.json"), overwrite: true);

            _logger.LogWarning("Restore from {BackupFilePath} completed. Previous database saved to {SafetyBackupPath}. A restart is required for the restored data to take full effect.", backupFilePath, safety.FilePath);
            return new RestoreResult(safety.FilePath, DateTime.UtcNow, RequiresRestart: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Restore from {BackupFilePath} failed.", backupFilePath);
            throw;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    public Task<IReadOnlyList<BackupFileInfo>> ListBackupsAsync(string? directory = null, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(directory) ? DefaultBackupDirectory : directory;
        if (!Directory.Exists(target))
            return Task.FromResult<IReadOnlyList<BackupFileInfo>>([]);

        var files = Directory.EnumerateFiles(target, "eos-backup-*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(x => x.CreationTimeUtc)
            .Select(x => new BackupFileInfo(x.FullName, x.Name, x.Length, x.CreationTimeUtc))
            .ToArray();
        return Task.FromResult<IReadOnlyList<BackupFileInfo>>(files);
    }
}
