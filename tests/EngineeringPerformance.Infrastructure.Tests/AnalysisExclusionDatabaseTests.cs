using EngineeringPerformance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EngineeringPerformance.Infrastructure.Tests;

public sealed class AnalysisExclusionDatabaseTests
{
    [Fact]
    public async Task ExclusionsUseCanonicalNameMatchingForAddAndRemove()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"epa-exclusions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var databasePath = Path.Combine(folder, "exclusions.db");

        try
        {
            var options = new DbContextOptionsBuilder<PerformanceDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var setup = new PerformanceDbContext(options))
                await setup.Database.EnsureCreatedAsync();

            var database = new LocalApplicationDatabase(new TestContextFactory(options), new WorkbookService());

            await database.SetExclusionAsync("  Jane   DOE  ", true);
            Assert.Equal(["Jane DOE"], await database.GetExcludedNamesAsync());

            // Same identity despite case and internal-spacing differences.
            await database.SetExclusionAsync("jane doe", false);
            Assert.Empty(await database.GetExcludedNamesAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private sealed class TestContextFactory(DbContextOptions<PerformanceDbContext> options) : IDbContextFactory<PerformanceDbContext>
    {
        public PerformanceDbContext CreateDbContext() => new(options);
    }
}
