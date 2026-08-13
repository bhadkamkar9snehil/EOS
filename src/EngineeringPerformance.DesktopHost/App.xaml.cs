using System.Threading.Tasks;
using System.Windows;
using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;

namespace EngineeringPerformance.DesktopHost;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private readonly LocalApplicationPaths _paths;
    private readonly ILogger<App> _log;

    static App()
    {
        // Must run before ANY other app code, per Velopack's WPF integration docs: this handles
        // Velopack's special install-time/update-time process invocations (e.g. creating shortcuts
        // during install, running app updates) and exits the process immediately when one of those
        // is detected, so nothing below the App type's static initializer/constructor may run first.
        VelopackApp.Build().Run();
    }

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _paths = LocalApplicationPaths.ForCurrentUser();

        _host = Host.CreateDefaultBuilder()
            .UseEosLogging(_paths)
            .ConfigureServices(services =>
            {
                services.AddWpfBlazorWebView();
                services.AddLocalInfrastructure(_paths);
                services.AddSingleton<IFileDialogService, WindowsFileDialogService>();
                services.AddSingleton<AppState>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _log = _host.Services.GetRequiredService<ILogger<App>>();

        // Catch process-level failures that do not flow through normal awaited application code.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _log.LogInformation("Starting EOS. DataDirectory={DataDirectory}", _paths.DataDirectory);
            await _host.StartAsync();
            await _host.Services.GetRequiredService<IApplicationDatabase>().InitializeAsync();

            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _log.LogInformation("EOS startup completed.");

            // Fire-and-forget: never delay app launch waiting on a network/file-share round trip.
            _ = CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            _log.LogCritical(exception, "The application failed to start.");
            MessageBox.Show(
                $"The application could not start.\n\nDetails were saved to:\n{_paths.LogDirectory}",
                "Engineering Performance Analyzer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// Logs exceptions raised while rendering/dispatching UI work before showing a recoverable
    /// message. This handler is intentionally narrow: ordinary application failures should be
    /// handled and logged at the operation boundary rather than reaching the dispatcher.
    /// </summary>
    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _log.LogError(e.Exception, "Unhandled exception on the UI dispatcher.");
            MessageBox.Show(
                $"Something went wrong on this page.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{_paths.LogDirectory}\n\nYou can keep using the app — try a different page or reload.",
                "Engineering Performance Analyzer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }

        e.Handled = true;
    }

    /// <summary>
    /// Checks the configured Velopack feed for a newer release. Update availability is useful
    /// operational information; an unreachable/not-yet-configured feed is expected in development
    /// and is logged at Debug rather than surfaced as an application error.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateManager = new UpdateManager(UpdateSettings.FeedUrl);
            if (!updateManager.IsInstalled)
            {
                _log.LogDebug("Skipping update check because EOS is not running from a Velopack installation.");
                return;
            }

            var newVersion = await updateManager.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                _log.LogDebug("Update check completed; no newer EOS release is available.");
                return;
            }

            _log.LogInformation("Downloading EOS update {Version}.", newVersion.TargetFullRelease.Version);
            await updateManager.DownloadUpdatesAsync(newVersion);

            var result = MessageBox.Show(
                $"A new version ({newVersion.TargetFullRelease.Version}) has been downloaded.\n\nRestart now to apply it?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                _log.LogInformation("Applying EOS update {Version} and restarting.", newVersion.TargetFullRelease.Version);
                updateManager.ApplyUpdatesAndRestart(newVersion);
            }
        }
        catch (Exception exception)
        {
            _log.LogDebug(exception, "Update check could not complete; continuing without interrupting the user.");
        }
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _log.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled exception on a background thread. IsTerminating={IsTerminating}",
                e.IsTerminating);
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _log.LogError(e.Exception, "An unobserved Task exception was raised.");
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }

        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        try
        {
            _log.LogInformation("Stopping EOS.");
            await _host.StopAsync();
        }
        finally
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
