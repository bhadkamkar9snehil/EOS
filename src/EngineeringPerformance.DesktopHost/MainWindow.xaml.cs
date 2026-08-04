using System.Windows;

namespace EngineeringPerformance.DesktopHost;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        Services = services;
        InitializeComponent();
        DataContext = this;
    }

    public IServiceProvider Services { get; }
}
