using System.Windows;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// The single application window (Section 7): left card list + right black console. Settings opens
/// the configuration dialog; closing is intercepted by <see cref="App"/> to hide to the tray.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main)
        {
            return;
        }

        var app = (App)Application.Current;
        var configVm = app.CreateConfigViewModel();
        var dialog = new ConfigWindow(configVm) { Owner = this };
        dialog.ShowDialog();

        if (configVm.Saved)
        {
            // Reflect possibly-changed reduced motion / root path immediately.
            main.ReducedMotion = configVm.ReducedMotion;
        }
    }
}
