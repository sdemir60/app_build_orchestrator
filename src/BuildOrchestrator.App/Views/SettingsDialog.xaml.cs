using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [D7/T66] Settings modal diyaloğu (ince view). <b>WORKSPACE</b> (design v1.8.0 §2.9) ve <b>LAYERS</b>
/// bölümlerinin ikisi de <see cref="SettingsDraftViewModel"/>'e (test edilebilir taslak) bağlıdır — Save'e
/// kadar canlı <see cref="RunViewModel"/>'e dokunulmaz. Save = commit (persist + katmanlar + bekleyen repo
/// kökü + TEK Sync, <see cref="SettingsDraftViewModel.CommitAsync"/>), Cancel/scrim/Esc = taslağı at.
///
/// <para>[design v1.10.0 §2.9] Footer ayrıca <b>Export / Import / Clear</b> taşır. Üçü de yalnız FORMU
/// değiştirir: Save'e kadar hiçbir şey uygulanmaz ve onay dialogu yoktur — Clear'ın "onayı" iki aşamalı
/// düğmenin kendisidir.</para>
/// </summary>
public partial class SettingsDialog : UserControl
{
    /// <summary>[§2.9] Footer geri bildiriminin ve Clear'ın iki-aşamalı penceresinin süresi.</summary>
    internal const double FeedbackMs = 2400;

    private SettingsDraftViewModel? _draft;
    private RunViewModel? _run;
    private IUiStateStore? _store;
    private Func<string?>? _pickFolder;
    private readonly DispatcherTimer _feedbackTimer = new();
    private bool _clearArmed;

    public SettingsDialog()
    {
        InitializeComponent();
        _feedbackTimer.Interval = TimeSpan.FromMilliseconds(FeedbackMs);
        _feedbackTimer.Tick += (_, _) => ResetFeedback();
    }

    /// <summary>[§2.9] Dosya seçici seam'leri — testler gerçek diyalog açmaz. <c>MainWindow</c> gerçek Win32
    /// diyaloglarını bağlar.</summary>
    public Func<string?>? PickExportPath { get; set; }
    public Func<string?>? PickImportPath { get; set; }

    /// <summary>[test yüzeyi] Dosya okuma/yazma seam'i — varsayılan gerçek diski kullanır.</summary>
    internal Func<string, string> ReadFile { get; set; } = File.ReadAllText;
    internal Action<string, string> WriteFile { get; set; } = File.WriteAllText;

    // ---------------------------------------------------------------- test yüzeyi
    internal SettingsDraftViewModel? Draft => _draft;
    internal Button Export => ExportButton;
    internal Button Import => ImportButton;
    internal Button Clear => ClearButton;
    internal Button Save => SaveButton;
    internal Button SampleLayers => SampleLayersButton;
    internal TextBox RootInput => RepoRootInput;
    internal TextBlock Feedback => FeedbackText;
    internal ShapePath ClearIcon => ClearGlyph;
    internal bool IsClearArmed => _clearArmed;

    /// <summary>[D7] Diyaloğu açar: canlı pattern'lerin bir TASLAK kopyasını kurar (SettingsDraftViewModel),
    /// repo yolunu gösterir ve görünür kılar. <paramref name="pickFolder"/> klasör seçici seam'idir (testler
    /// gerçek diyalog açmaz — E1'deki IOsActions.PickFolder gelene dek OpenFolderDialog doğrudan çağrılır).</summary>
    public void Open(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        _run = run;
        _store = store;
        _pickFolder = pickFolder;
        _draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath);
        DataContext = _draft;
        ResetFeedback();
        RefreshSaveLabel();
        Visibility = Visibility.Visible;
        Focus(); // Esc HER durumda yakalanabilsin (MoveFocus altta bulamazsa bile odak burada kalır)
        // [D7 re-review][Fix1] Odağı UserControl'ün KENDİSİNDEN diyaloğun İÇİNE taşı (ilk input tercih edilir) —
        // Scrim bir FocusManager.IsFocusScope olduğundan bu arama diyalog alt-ağacıyla SINIRLIdır.
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>[design v1.10.0 §2.4] First run'daki <c>Import settings…</c> kısayolu: diyaloğu açar ve dosya
    /// seçiciyi HEMEN tetikler — hazır bir ayar dosyası olan developer tek adımda başlar.</summary>
    public void OpenForImport(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        Open(run, store, pickFolder);
        OnImport(this, new RoutedEventArgs());
    }

    private void Close()
    {
        ResetFeedback();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>[E5/T46] Esc zincirinin dialog katmanı için dışarıdan kapatma (MainWindow güvenlik ağı — odak
    /// dialog dışındayken). Dialog odaklıyken Esc'i zaten <see cref="OnKeyDown"/> yakalar (handled).</summary>
    public void CloseDialog() => Close();

    /// <summary>[design v1.8.0 §2.9] First run'da (henüz workspace yok) kaydetmek AYNI ZAMANDA kurulumdur —
    /// düğme bunu söyler: <c>Save and sync</c>. Sonrasında yalnız <c>Save</c>.</summary>
    private void RefreshSaveLabel() =>
        SaveButton.Content = _run?.HasWorkspace == true ? "Save" : "Save and sync";

    // ---- Layers ----

    private void OnAddLayer(object sender, RoutedEventArgs e) => _draft?.AddLayer();

    private void OnRemoveLayer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayerRowViewModel row }) _draft?.RemoveLayer(row);
    }

    private void OnLoadSampleLayers(object sender, RoutedEventArgs e)
    {
        _draft?.LoadSampleLayers();
        DisarmClear();
    }

    // ---- Workspace (design v1.8.0 §2.9) ----

    // "Browse…" YALNIZ taslağa yazar: kök değişimi, satır reset'i ve Sync Save'e ertelenir (Cancel her şeyi atar).
    private void OnChangeRepository(object sender, RoutedEventArgs e)
    {
        if (_pickFolder?.Invoke() is not { Length: > 0 } path || _draft is null) return;
        _draft.RepositoryRoot = path;
        DisarmClear();
    }

    // ---- Export / Import / Clear (design v1.10.0 §2.9) ----

    private void OnExport(object sender, RoutedEventArgs e)
    {
        DisarmClear();
        if (_draft is null || PickExportPath?.Invoke() is not { Length: > 0 } path) return;
        try
        {
            WriteFile(path, _draft.ToFile().ToJson());
            ShowFeedback("Exported " + System.IO.Path.GetFileName(path), success: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowFeedback("Could not write the settings file", success: false);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        DisarmClear();
        if (_draft is null || PickImportPath?.Invoke() is not { Length: > 0 } path) return;
        SettingsFile? file;
        try { file = SettingsFile.TryParse(ReadFile(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { file = null; }

        if (file is null) { ShowFeedback("Invalid settings file", success: false); return; }
        _draft.LoadFrom(file);
        ShowFeedback(file.ImportedMessage(), success: true);
    }

    /// <summary>[§2.9] Clear İKİ AŞAMALIDIR: ilk tık ikonu kırmızıya çevirip uyarıyı basar ve 2.4s sonra
    /// KENDİNİ İPTAL EDER; ikinci tık formu boşaltır. Ayrı bir onay dialogu YOKTUR — düğmenin kendisi onaydır.</summary>
    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_draft is null) return;
        if (!_clearArmed)
        {
            _clearArmed = true;
            ClearGlyph.SetResourceReference(Shape.StrokeProperty, "Brush.StatusFailText");
            ShowFeedback("Click again to clear root and all layers", success: false);
            return;
        }

        DisarmClear();
        _draft.ClearAll();
        ShowFeedback("Cleared — nothing is applied until you save", success: true);
    }

    private void DisarmClear()
    {
        if (!_clearArmed) return;
        _clearArmed = false;
        // İkon kendi (animasyonlu) Foreground bağını geri alır — Ds.IconButton şablonunun kuralı.
        ClearGlyph.SetBinding(Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = ClearButton });
    }

    private void ShowFeedback(string text, bool success)
    {
        FeedbackText.Text = text;
        FeedbackText.SetResourceReference(TextBlock.ForegroundProperty,
            success ? "Brush.StatusSuccessText" : "Brush.StatusFailText");
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void ResetFeedback()
    {
        _feedbackTimer.Stop();
        FeedbackText.Text = "";
        DisarmClear();
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
