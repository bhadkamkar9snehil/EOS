using EngineeringPerformance.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EngineeringPerformance.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLocalInfrastructure(this IServiceCollection services, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "engineering-performance.db");
        services.AddPooledDbContextFactory<PerformanceDbContext>(options => options.UseSqlite($"Data Source={databasePath};Cache=Shared"));
        services.AddSingleton<IWorkbookService, WorkbookService>();
        services.AddSingleton<LocalApplicationDatabase>();
        services.AddSingleton<IApplicationDatabase>(sp => new ConfigurableApplicationDatabase(
            sp.GetRequiredService<LocalApplicationDatabase>(),
            sp.GetRequiredService<IDbContextFactory<PerformanceDbContext>>(),
            sp.GetRequiredService<IWorkbookService>(),
            dataDirectory,
            sp.GetService<ILogger<ConfigurableApplicationDatabase>>()));
        services.AddSingleton<IBackupService>(sp => new BackupService(
            sp.GetRequiredService<IDbContextFactory<PerformanceDbContext>>(),
            dataDirectory,
            databasePath,
            defaultBackupDirectory: null,
            sp.GetService<ILogger<BackupService>>()));
        services.AddSingleton<IScoringPresetService>(sp => new ScoringPresetService(dataDirectory, sp.GetService<ILogger<ScoringPresetService>>()));
        return services;
    }
}
