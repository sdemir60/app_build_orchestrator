using System.Windows;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>Configuration dialog (Section 3). Closes itself when the view model requests it.</summary>
public partial class ConfigWindow : Window
{
    public ConfigWindow(ConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(bool _)
    {
        // DialogResult can only be set when shown via ShowDialog; guard for safety.
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
