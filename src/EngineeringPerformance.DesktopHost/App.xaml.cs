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

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
