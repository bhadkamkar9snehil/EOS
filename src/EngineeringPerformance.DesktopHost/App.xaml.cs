using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Velopack;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace EngineeringPerformance.DesktopHost;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private readonly string _dataDirectory;
    private readonly ILogger _log;
    private readonly bool _visualCapture;
    private readonly string? _visualOutputDirectory;

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
        _visualCapture = string.Equals(Environment.GetEnvironmentVariable("EOS_VISUAL_CAPTURE"), "1", StringComparison.Ordinal);
        _visualOutputDirectory = Environment.GetEnvironmentVariable("EOS_VISUAL_OUTPUT");

        // Visual capture is intentionally isolated from the user's normal LocalApplicationData.
        // It must never read, mutate or package real employee data just to validate presentation.
        _dataDirectory = _visualCapture && !string.IsNullOrWhiteSpace(_visualOutputDirectory)
            ? Path.Combine(_visualOutputDirectory, "appdata")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngineeringPerformance");
        Directory.CreateDirectory(_dataDirectory);

        var logDirectory = Path.Combine(_dataDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "eos-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        _log = Log.Logger;

        // Catches crashes on background threads (Task.Run, timers, thread-pool work) that never
        // reach the WPF dispatcher — without this handler they were completely uncaught and lost.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        // Catches exceptions from fire-and-forget/unawaited async Tasks that get garbage collected
        // without ever being observed — a known pitfall for the app's "await X; await Y" style code.
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddWpfBlazorWebView();
            services.AddLocalInfrastructure(_dataDirectory);
            if (_visualCapture)
            {
                // Register last so single-service resolution uses the synthetic read-only dataset,
                // while all other infrastructure services remain exactly the production services.
                services.AddSingleton<IApplicationDatabase, VisualCaptureApplicationDatabase>();
            }
            services.AddSingleton<IFileDialogService, WindowsFileDialogService>();
            services.AddSingleton<AppState>();
            services.AddSingleton<MainWindow>();
        }).UseSerilog().Build();
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

            if (_visualCapture)
            {
                var outputDirectory = string.IsNullOrWhiteSpace(_visualOutputDirectory)
                    ? Path.Combine(Path.GetTempPath(), "eos-visual-evidence")
                    : _visualOutputDirectory;
                var passed = await MainWindow.CaptureVisualEvidenceAsync(outputDirectory);
                Shutdown(passed ? 0 : 2);
                return;
            }

            // Fire-and-forget: never delay app launch waiting on a network/file-share round trip.
            _ = CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            _log.Fatal(exception, "The application failed to start.");
            Log.CloseAndFlush();
            var logDirectory = Path.Combine(_dataDirectory, "logs");

            // CI capture must fail mechanically rather than block forever behind a modal dialog in
            // a non-interactive build session. Normal interactive launches keep the user-facing UI.
            if (_visualCapture)
            {
                try
                {
                    var outputDirectory = string.IsNullOrWhiteSpace(_visualOutputDirectory)
                        ? Path.Combine(Path.GetTempPath(), "eos-visual-evidence")
                        : _visualOutputDirectory;
                    Directory.CreateDirectory(outputDirectory);
                    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "startup-failure.txt"), exception.ToString());
                }
                catch
                {
                    // Best effort only; the Serilog file above remains the primary crash record.
                }
                Shutdown(-1);
                return;
            }

            MessageBox.Show($"The application could not start.\n\nDetails were saved to:\n{logDirectory}", "Engineering Performance Analyzer", MessageBoxButton.OK, MessageBoxImage.Error);
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
            _log.Error(e.Exception, "Unhandled exception on the UI dispatcher.");
            if (!_visualCapture)
            {
                var logDirectory = Path.Combine(_dataDirectory, "logs");
                MessageBox.Show(
                    $"Something went wrong on this page.\n\n{e.Exception.Message}\n\nDetails were saved to:\n{logDirectory}\n\nYou can keep using the app — try a different page or reload.",
                    "Engineering Performance Analyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _log.Fatal(e.ExceptionObject as Exception, "Unhandled exception on a background thread. IsTerminating={IsTerminating}", e.IsTerminating);
            Log.CloseAndFlush();
        }
        catch
        {
            // Logging itself must never be why the crash handler crashes.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _log.Error(e.Exception, "An unobserved Task exception was raised.");
        }
        catch
        {
            // Logging itself must never be why the crash handler crashes.
        }
        // Without this the finalizer thread rethrows, crashing the process.
        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
