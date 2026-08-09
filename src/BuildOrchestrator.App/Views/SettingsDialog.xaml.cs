using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D7/T66] Settings modal diyaloğu (ince view). LAYERS ve REPOSITORY (K10) bölümlerinin ikisi de
/// <see cref="SettingsDraftViewModel"/>'e (test edilebilir taslak) bağlıdır — Save'e kadar canlı
/// <see cref="RunViewModel"/>'e dokunulmaz. Save = commit (persist + katmanlar + bekleyen repo kökü + TEK
/// Sync, <see cref="SettingsDraftViewModel.CommitAsync"/>), Cancel/scrim/Esc = taslağı at. Diyalog
/// MainWindow'un shell overlay'inde (RowSpan) durur; <see cref="Open"/> ile açılır.
/// </summary>
public partial class SettingsDialog : UserControl
{
    private SettingsDraftViewModel? _draft;
    private RunViewModel? _run;
    private IUiStateStore? _store;
    private Func<string?>? _pickFolder;

    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>[D7] Diyaloğu açar: canlı pattern'lerin bir TASLAK kopyasını kurar (SettingsDraftViewModel),
    /// repo yolunu gösterir ve görünür kılar. <paramref name="pickFolder"/> klasör seçici seam'idir (testler
    /// gerçek diyalog açmaz — E1'deki IOsActions.PickFolder gelene dek OpenFolderDialog doğrudan çağrılır).</summary>
    public void Open(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        _run = run;
        _store = store;
        _pickFolder = pickFolder;
        // [Task 11] Anahtarın CANLI değeri taslağa kopyalanır — diyalog kendi varsayılanını İCAT ETMEZ.
        _draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath, run.BuildDependencyCycles);
        DataContext = _draft;
        UpdateRepoLabel();
        Visibility = Visibility.Visible;
        Focus(); // Esc HER durumda yakalanabilsin (MoveFocus altta bulamazsa bile odak burada kalır)
        // [D7 re-review][Fix1] Odağı UserControl'ün KENDİSİNDEN diyaloğun İÇİNE taşı (ilk input tercih edilir) —
        // Scrim bir FocusManager.IsFocusScope olduğundan bu arama diyalog alt-ağacıyla SINIRLIdır.
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Close() => Visibility = Visibility.Collapsed;

    /// <summary>[E5/T46] Esc zincirinin dialog katmanı için dışarıdan kapatma (MainWindow güvenlik ağı — odak
    /// dialog dışındayken). Dialog odaklıyken Esc'i zaten <see cref="OnKeyDown"/> yakalar (handled).</summary>
    public void CloseDialog() => Close();

    private void UpdateRepoLabel() =>
        RepoPathText.Text = _draft?.RepositoryRoot is { Length: > 0 } root ? root : "no repository";

    // ---- Layers ----

    private void OnAddLayer(object sender, RoutedEventArgs e) => _draft?.AddLayer();

    private void OnRemoveLayer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayerRowViewModel row }) _draft?.RemoveLayer(row);
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e) => _draft?.RestoreDefaults();

    // ---- Repository (K10) ----

    // "Change…" YALNIZ taslağa yazar: kök değişimi, satır reset'i ve Sync Save'e ertelenir (Cancel her şeyi atar).
    private void OnChangeRepository(object sender, RoutedEventArgs e)
    {
        if (_pickFolder?.Invoke() is not { Length: > 0 } path || _draft is null) return;
        _draft.RepositoryRoot = path;
        UpdateRepoLabel();
    }

    // ---- Save / Cancel ----

    // Diyalog Save'e basıldığı anda kapanır; commit (persist + katmanlar + kök + tek Sync) arkasından sürer.
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _run is null || _store is null || !_draft.CanSave) return;
        var (draft, run, store) = (_draft, _run, _store);
        Close();
        await draft.CommitAsync(run, store);
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
