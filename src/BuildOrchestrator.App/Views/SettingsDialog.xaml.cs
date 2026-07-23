using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D7/T66] Settings modal diyaloğu (ince view). LAYERS bölümü <see cref="LayerEditorViewModel"/>'e (test
/// edilebilir taslak) bağlıdır; REPOSITORY bölümü <see cref="RunViewModel"/>'e. Save = commit (konsol notu +
/// LayerPatterns + UiState persist), Cancel/scrim/Esc = taslağı at. Diyalog MainWindow'un shell overlay'inde
/// (RowSpan) durur; <see cref="Open"/> ile açılır.
/// </summary>
public partial class SettingsDialog : UserControl
{
    private LayerEditorViewModel? _editor;
    private RunViewModel? _run;
    private IUiStateStore? _store;
    private Func<string?>? _pickFolder;

    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>[D7] Diyaloğu açar: canlı pattern'lerin bir TASLAK kopyasını kurar (LayerEditorViewModel),
    /// repo yolunu gösterir ve görünür kılar. <paramref name="pickFolder"/> klasör seçici seam'idir (testler
    /// gerçek diyalog açmaz — E1'deki IOsActions.PickFolder gelene dek OpenFolderDialog doğrudan çağrılır).</summary>
    public void Open(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        _run = run;
        _store = store;
        _pickFolder = pickFolder;
        _editor = new LayerEditorViewModel(run.LayerPatterns);
        DataContext = _editor;
        UpdateRepoLabel();
        Visibility = Visibility.Visible;
        Focus(); // Esc yakalanabilsin
    }

    private void Close() => Visibility = Visibility.Collapsed;

    private void UpdateRepoLabel() =>
        RepoPathText.Text = _run is { RootPath.Length: > 0 } r ? r.RootPath : "no repository";

    // ---- Layers ----

    private void OnAddLayer(object sender, RoutedEventArgs e) => _editor?.AddLayer();

    private void OnRemoveLayer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayerRowViewModel row }) _editor?.RemoveLayer(row);
    }

    private void OnLoadSampleLayers(object sender, RoutedEventArgs e) => _editor?.LoadSampleLayers();

    // ---- Repository (K10) ----

    private async void OnChangeRepository(object sender, RoutedEventArgs e)
    {
        if (_pickFolder?.Invoke() is not { Length: > 0 } path || _run is null) return;
        await _run.ChangeRepositoryAsync(path); // kök değişir, durumlar sıfırlanır, otomatik Sync
        UpdateRepoLabel();
    }

    // ---- Save / Cancel ----

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_editor is null || _run is null || _store is null || !_editor.CanSave) return;
        _editor.Commit(_run, _store); // konsol notu + LayerPatterns + UiState persist
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close(); // taslak (kopya) atılır

    // Scrim tıklaması kapatır (Cancel); diyaloğun kendi içine tıklama scrim'e ULAŞMAZ.
    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();
    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; } // BuildApp.jsx:1312
    }
}
