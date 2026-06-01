using System.IO;
using System.Windows;
using BuildOrchestrator.App.ViewModels;
using Microsoft.Win32;

namespace BuildOrchestrator.App;

/// <summary>Interaction logic for ConfigWindow.xaml (Section 3 settings).</summary>
public partial class ConfigWindow : Window
{
    private readonly ConfigViewModel _viewModel;

    public ConfigWindow(ConfigViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select workspace root" };
        if (Directory.Exists(_viewModel.RootPath))
            dialog.InitialDirectory = _viewModel.RootPath;
        if (dialog.ShowDialog(this) == true)
            _viewModel.RootPath = dialog.FolderName;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
