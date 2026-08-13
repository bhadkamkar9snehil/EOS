using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.DesktopHost.Updates;
using EngineeringPerformance.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EngineeringPerformance.DesktopHost;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private readonly LocalApplicationPaths _paths;
    private readonly ILogger<App> _log;
    private readonly bool _visualCapture;
    private readonly string? _visualOutputDirectory;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _visualCapture = string.Equals(Environment.GetEnvironmentVariable("EOS_VISUAL_CAPTURE"), "1", StringComparison.Ordinal);
        _visualOutputDirectory = Environment.GetEnvironmentVariable("EOS_VISUAL_OUTPUT");
        _paths = _visualCapture && !string.IsNullOrWhiteSpace(_visualOutputDirectory)
            ? new LocalApplicationPaths(Path.Combine(_visualOutputDirectory, "appdata"))
            : LocalApplicationPaths.ForCurrentUser();

        _host = Host.CreateDefaultBuilder()
            .UseEosLogging(_paths)
            .ConfigureServices(services =>
            {
                services.AddWpfBlazorWebView();
                services.AddLocalInfrastructure(_paths);
                services.AddSingleton<IUpdateBackend>(_ => new VelopackUpdateBackend(UpdateSettings.RepositoryUrl));
                services.AddSingleton<VelopackUpdateService>(sp => new VelopackUpdateService(
                    sp.GetRequiredService<IUpdateBackend>(),
                    () => Dispatcher.BeginInvoke(new Action(() => Shutdown())),
                    sp.GetRequiredService<ILogger<VelopackUpdateService>>()));
                services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<VelopackUpdateService>());
                services.AddHostedService<UpdateCheckWorker>();
                if (_visualCapture)
                {
                    services.AddSingleton<IApplicationDatabase, VisualCaptureApplicationDatabase>();
                }
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

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _log.LogInformation("EOS startup completed.");

            if (_visualCapture)
            {
                var outputDirectory = string.IsNullOrWhiteSpace(_visualOutputDirectory)
                    ? Path.Combine(Path.GetTempPath(), "eos-visual-evidence")
                    : _visualOutputDirectory;
                var passed = await mainWindow.CaptureVisualEvidenceAsync(outputDirectory);
                Shutdown(passed ? 0 : 2);
                return;
            }

        }
        catch (Exception exception)
        {
            _log.LogCritical(exception, "The application failed to start.");
            if (_visualCapture)
            {
                await WriteCaptureFailureAsync(exception);
                Shutdown(-1);
                return;
            }

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
            if (_visualCapture)
            {
                e.Handled = true;
                return;
            }

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

    private async Task WriteCaptureFailureAsync(Exception exception)
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
            // Best effort only; the structured EOS log remains the primary crash record.
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
