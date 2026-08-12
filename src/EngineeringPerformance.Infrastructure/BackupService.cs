using System.IO.Compression;
using EngineeringPerformance.Application;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Exports the live SQLite database (checkpointed so the WAL is folded into the main file first)
/// plus the operational-scoring config into a single timestamped zip, and restores from one.
/// Restore always exports a safety backup of the current database first, so a bad restore can
/// itself be undone. Because the app holds pooled connections to the database file for its whole
/// lifetime, a restored file only takes full effect after the application is restarted — the
/// result flags that so the UI can tell the user.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<PerformanceDbContext> contextFactory,
    string dataDirectory,
    string databasePath) : IBackupService
{
    private const string DbEntryName = "engineering-performance.db";
    private const string ScoringEntryName = "operational-scoring.json";
    private const string PresetsEntryName = "scoring-presets.json";

    public string DefaultBackupDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EngineeringPerformance-Backups");

    private string SafetyBackupDirectory => Path.Combine(DefaultBackupDirectory, "pre-restore-safety");

    public async Task<BackupResult> ExportBackupAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        var targetDirectory = string.IsNullOrWhiteSpace(destinationDirectory) ? DefaultBackupDirectory : destinationDirectory;
        Directory.CreateDirectory(targetDirectory);

        // Fold the write-ahead log into the main database file so the zip contains a complete,
        // self-consistent snapshot without also having to ship the -wal/-shm side files.
        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(targetDirectory, $"eos-backup-{timestamp}.zip");
        // Extremely unlikely, but a second backup within the same second would otherwise clobber.
        var suffix = 1;
        while (File.Exists(zipPath))
            zipPath = Path.Combine(targetDirectory, $"eos-backup-{timestamp}-{suffix++}.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            if (!File.Exists(databasePath))
                throw new FileNotFoundException("The application database file could not be found.", databasePath);
            archive.CreateEntryFromFile(databasePath, DbEntryName, CompressionLevel.Optimal);

            var scoringPath = Path.Combine(dataDirectory, "operational-scoring.json");
            if (File.Exists(scoringPath))
                archive.CreateEntryFromFile(scoringPath, ScoringEntryName, CompressionLevel.Optimal);

            var presetsPath = Path.Combine(dataDirectory, "scoring-presets.json");
            if (File.Exists(presetsPath))
                archive.CreateEntryFromFile(presetsPath, PresetsEntryName, CompressionLevel.Optimal);
        }

        var info = new FileInfo(zipPath);
        return new BackupResult(zipPath, info.Length, DateTime.UtcNow);
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
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

            File.Copy(extractedDb, databasePath, overwrite: true);
            // Drop any stale WAL/SHM side files left over from before the restore — they refer to
            // the previous database's page layout and must not be replayed against the new one.
            foreach (var side in new[] { databasePath + "-wal", databasePath + "-shm" })
                if (File.Exists(side)) File.Delete(side);

            // Microsoft.Data.Sqlite pools native sqlite3 handles by connection string, independent
            // of EF's own DbContext pool — without this, any handle already opened against the old
            // file (by this app or a test) can keep serving stale pages after the copy above,
            // because it never re-reads the file from disk on its own. Restarting the app (the
            // documented follow-up step) would also clear this, but clearing explicitly here means
            // a fresh connection sees the restored data immediately rather than only after restart.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var extractedScoring = Path.Combine(temporaryDirectory, ScoringEntryName);
            if (File.Exists(extractedScoring))
                File.Copy(extractedScoring, Path.Combine(dataDirectory, "operational-scoring.json"), overwrite: true);

            var extractedPresets = Path.Combine(temporaryDirectory, PresetsEntryName);
            if (File.Exists(extractedPresets))
                File.Copy(extractedPresets, Path.Combine(dataDirectory, "scoring-presets.json"), overwrite: true);

            return new RestoreResult(safety.FilePath, DateTime.UtcNow, RequiresRestart: true);
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
