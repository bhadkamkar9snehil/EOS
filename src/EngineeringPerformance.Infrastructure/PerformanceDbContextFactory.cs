using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EngineeringPerformance.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations` construct a context at design time, when no DI container or
/// running app exists to supply one. The connection string here is never used at runtime —
/// the real one comes from <see cref="ServiceCollectionExtensions.AddLocalInfrastructure"/>.
/// </summary>
public sealed class PerformanceDbContextFactory : IDesignTimeDbContextFactory<PerformanceDbContext>
{
    public PerformanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PerformanceDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new PerformanceDbContext(options);
    }
}
