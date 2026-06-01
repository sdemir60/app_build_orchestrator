using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.Configuration;
using DrawingIcon = System.Drawing.Icon;

namespace BuildOrchestrator.App;

/// <summary>
/// Interaction logic for MainWindow.xaml. Handles tray interaction and minimize-to-tray.
/// </summary>
public partial class MainWindow : Window
{
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-scroll the project list so the card that just started building moves to the top.
        if (ViewModel != null)
            ViewModel.ScrollToCardRequested += card =>
                Dispatcher.BeginInvoke(() => ScrollCardToTop(card));

        // Provide a default tray icon from the running executable to avoid shipping a binary asset.
        try
        {
            var exe = Environment.ProcessPath;
            Tray.Icon = (exe != null ? DrawingIcon.ExtractAssociatedIcon(exe) : null)
                        ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            // Tray will fall back to no icon; non-fatal.
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && ViewModel?.Config.MinimizeToTray == true)
            Hide();
    }

    /// <summary>Scrolls the project list so the given card sits at the top (the list uses
    /// item-based scrolling, so the vertical offset is an item index).</summary>
    private void ScrollCardToTop(ProjectCardViewModel card)
    {
        if (ViewModel == null)
            return;
        int index = ViewModel.Projects.IndexOf(card);
        if (index < 0)
            return;
        var sv = FindScrollViewer(ProjectList);
        sv?.ScrollToVerticalOffset(index);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }
        return null;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Tray_OnDoubleClick(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayOpen_OnClick(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayExit_OnClick(object sender, RoutedEventArgs e)
    {
        _exitRequested = true;
        // Stop/kill any in-progress build before tearing everything down.
        ViewModel?.StopForExit();
        Tray.Dispose();
        Application.Current.Shutdown();
    }

    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        var configVm = new ConfigViewModel(ViewModel.Config);
        var window = new ConfigWindow(configVm) { Owner = this };
        if (window.ShowDialog() == true)
        {
            AppConfig updated = configVm.ToConfig();
            ViewModel.ApplyConfig(updated);
            Services.AutoStartService.Set(updated.AutoStart);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window minimizes to the tray instead of exiting; only the tray "Exit"
        // (which sets _exitRequested) actually shuts the app down.
        if (!_exitRequested && ViewModel?.Config.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        ViewModel?.StopForExit();
        Tray.Dispose();
        base.OnClosing(e);
    }
}