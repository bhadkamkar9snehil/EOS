using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Velopack;

namespace EngineeringPerformance.DesktopHost;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;

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
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance");
        _host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddWpfBlazorWebView();
            services.AddLocalInfrastructure(dataDirectory);
            services.AddSingleton<IFileDialogService, WindowsFileDialogService>();
            services.AddSingleton<AppState>();
            services.AddSingleton<MainWindow>();
        }).Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        try
        {
            await _host.StartAsync();
            await _host.Services.GetRequiredService<IApplicationDatabase>().InitializeAsync();
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Fire-and-forget: never delay app launch waiting on a network/file-share round trip.
            _ = CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance");
            Directory.CreateDirectory(dataDirectory);
            var logPath = Path.Combine(dataDirectory, "startup-error.log");
            await File.WriteAllTextAsync(logPath, exception.ToString());
            MessageBox.Show($"The application could not start.\n\nDetails were saved to:\n{logPath}", "Engineering Performance Analyzer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// Without this, any exception raised while rendering a page (a bad component parameter, a
    /// null somewhere) reaches the WPF dispatcher unhandled and kills the whole process instantly
    /// with no on-screen message — the only trace is a .NET Runtime crash event in the Windows
    /// Application log. Logging it here and letting the user decide whether to carry on gives a
    /// real chance to recover (or at least see what broke) instead of a silent vanish.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance");
            Directory.CreateDirectory(dataDirectory);
            var logPath = Path.Combine(dataDirectory, "runtime-error.log");
            File.AppendAllText(logPath, $"{DateTime.Now:O}\n{e.Exception}\n\n");
            MessageBox.Show(
                $"Something went wrong on this page.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{logPath}\n\nYou can keep using the app — try a different page or reload.",
                "Engineering Performance Analyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // Logging itself must never be why the crash handler crashes.
        }
        e.Handled = true;
    }

    /// <summary>
    /// Checks the configured Velopack feed (see <see cref="UpdateSettings.FeedUrl"/>) for a newer
    /// release and, if found, downloads it and offers to restart into it. Runs after the main
    /// window is already showing and is deliberately best-effort: a missing/unreachable feed (the
    /// common case until a real feed is configured) is swallowed silently rather than surfaced as
    /// an error, since "no update source configured yet" isn't a fault.
    /// </summary>
    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateManager = new UpdateManager(UpdateSettings.FeedUrl);
            if (!updateManager.IsInstalled)
            {
                // Running from source/publish output rather than a Velopack-installed copy; nothing to check.
                return;
            }

            var newVersion = await updateManager.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                return;
            }

            await updateManager.DownloadUpdatesAsync(newVersion);

            var result = MessageBox.Show(
                $"A new version ({newVersion.TargetFullRelease.Version}) has been downloaded.\n\nRestart now to apply it?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                updateManager.ApplyUpdatesAndRestart(newVersion);
            }
        }
        catch
        {
            // Best-effort: no update feed configured yet, feed unreachable, offline, etc. are all
            // expected/non-fatal states, not something to interrupt the user about.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
