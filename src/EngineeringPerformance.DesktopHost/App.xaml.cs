using System.IO;
using System.Windows;
using EngineeringPerformance.Application;
using EngineeringPerformance.Infrastructure;
using EngineeringPerformance.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EngineeringPerformance.DesktopHost;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;

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

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
