using EngineeringPerformance.Infrastructure;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace EngineeringPerformance.DesktopHost;

/// <summary>
/// Desktop-host logging composition root. Serilog is intentionally confined to this file: EOS
/// application/infrastructure/UI code logs exclusively through Microsoft.Extensions.Logging.
/// Serilog is the provider/sink implementation, not a second application logging API.
/// </summary>
internal static class EosLogging
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static IHostBuilder UseEosLogging(this IHostBuilder builder, LocalApplicationPaths paths)
    {
        paths.EnsureDirectories();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "EOS")
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .WriteTo.File(
                paths.LogFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 25 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: OutputTemplate)
            .CreateLogger();

        return builder.UseSerilog(logger, dispose: true);
    }
}
