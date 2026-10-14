using EOS.Domain.Audit;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Operations;

public sealed record BackupFile(string Name, string Path, long SizeBytes, DateTime CreatedUtc);

/// <summary>
/// Consistent online backups of the SQLite database (VACUUM INTO writes a complete, defragmented copy without stopping the server)
/// and retention (spec §25.9 item 6). Restore is deliberately an offline, documented step: stop the service, replace the file, start.
/// </summary>
public sealed class BackupService(EosDbContext db)
{
    public async Task<BackupFile> CreateAsync(string directory, string actor, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"eos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        if (File.Exists(path)) throw new IOException($"Backup file {path} already exists.");
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {path}", ct);          // the path is bound as a parameter, never concatenated
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "backup.create", SubjectKind = "backup", SubjectId = System.IO.Path.GetFileName(path) });
        await db.SaveChangesAsync(ct);
        var info = new FileInfo(path);
        return new BackupFile(info.Name, info.FullName, info.Length, info.CreationTimeUtc);
    }

    public static IReadOnlyList<BackupFile> List(string directory) =>
        !Directory.Exists(directory) ? [] :
        new DirectoryInfo(directory).GetFiles("eos-*.db").OrderByDescending(f => f.Name).Select(f => new BackupFile(f.Name, f.FullName, f.Length, f.CreationTimeUtc)).ToList();

    /// <summary>Keeps the newest <paramref name="keepDaily"/> backups plus the newest per month for <paramref name="keepMonthly"/> months.</summary>
    public static IReadOnlyList<string> ApplyRetention(string directory, int keepDaily = 14, int keepMonthly = 12)
    {
        var files = List(directory).ToList();
        var keep = files.Take(keepDaily).Select(f => f.Path).ToHashSet();
        foreach (var g in files.GroupBy(f => f.Name.Substring(4, 6)).OrderByDescending(g => g.Key).Take(keepMonthly))
            keep.Add(g.First().Path);
        var removed = new List<string>();
        foreach (var f in files.Where(f => !keep.Contains(f.Path))) { File.Delete(f.Path); removed.Add(f.Name); }
        return removed;
    }
}
