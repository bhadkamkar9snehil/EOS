using EngineeringPerformance.Domain;
using EngineeringPerformance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class BackupServiceTests
{
    private static (BackupService Service, string DataDirectory, string DbPath, ServiceProvider Provider) CreateSut()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"epa-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "engineering-performance.db");

        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<PerformanceDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<PerformanceDbContext>>();

        using (var context = factory.CreateDbContext())
            context.Database.EnsureCreated();

        var service = new BackupService(factory, dataDirectory, dbPath);
        return (service, dataDirectory, dbPath, provider);
    }

    [Fact]
    public async Task ExportBackupAsync_CreatesZipContainingDatabase()
    {
        var (service, dataDirectory, _, provider) = CreateSut();
        try
        {
            var exportDirectory = Path.Combine(dataDirectory, "export-target");
            var result = await service.ExportBackupAsync(exportDirectory);

            Assert.True(File.Exists(result.FilePath));
            Assert.True(result.SizeBytes > 0);
            using var archive = System.IO.Compression.ZipFile.OpenRead(result.FilePath);
            Assert.Contains(archive.Entries, e => e.Name == "engineering-performance.db");
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }

    [Fact]
    public async Task ExportBackupAsync_IncludesScoringSettingsWhenPresent()
    {
        var (service, dataDirectory, _, provider) = CreateSut();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dataDirectory, "operational-scoring.json"), "{}");
            var result = await service.ExportBackupAsync();

            using var archive = System.IO.Compression.ZipFile.OpenRead(result.FilePath);
            Assert.Contains(archive.Entries, e => e.Name == "operational-scoring.json");
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ReplacesDatabaseAndBacksUpTheOldOneFirst()
    {
        var (service, dataDirectory, dbPath, provider) = CreateSut();
        try
        {
            // Seed the "original" database with one team, then take a backup of that state.
            // A fresh, unpooled connection is opened for each step below so nothing here reads
            // through a cached SQLite page cache from an earlier step — each step must observe
            // exactly what is really on disk at that point, the same as a freshly launched app would.
            static PerformanceDbContext OpenOnce(string path) =>
                new(new DbContextOptionsBuilder<PerformanceDbContext>().UseSqlite($"Data Source={path}").Options);

            await using (var ctx = OpenOnce(dbPath))
            {
                ctx.Teams.Add(new Team("Original Team"));
                await ctx.SaveChangesAsync();
            }

            var backup = await service.ExportBackupAsync();

            // Mutate the "current" database so restore has something to undo.
            await using (var ctx = OpenOnce(dbPath))
            {
                ctx.Teams.Add(new Team("Mutated After Backup"));
                await ctx.SaveChangesAsync();
            }

            var restoreResult = await service.RestoreBackupAsync(backup.FilePath);

            Assert.True(File.Exists(restoreResult.SafetyBackupPath));
            Assert.True(restoreResult.RequiresRestart);

            // The restored file on disk should now match the original single-team snapshot.
            await using var verifyContext = OpenOnce(dbPath);
            var teamNames = await verifyContext.Teams.Select(t => t.Name).ToListAsync();
            Assert.Contains("Original Team", teamNames);
            Assert.DoesNotContain("Mutated After Backup", teamNames);
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ThrowsForNonBackupZip()
    {
        var (service, dataDirectory, _, provider) = CreateSut();
        try
        {
            var badZip = Path.Combine(dataDirectory, "not-a-backup.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(badZip, System.IO.Compression.ZipArchiveMode.Create))
                archive.CreateEntry("readme.txt");

            await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreBackupAsync(badZip));
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
        }
    }
}
