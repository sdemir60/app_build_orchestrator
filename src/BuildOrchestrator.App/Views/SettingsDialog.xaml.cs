using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>[design v1.19.0 §2.9] Settings rayının bölümleri, raydaki sırasıyla.</summary>
internal enum SettingsSection { General, Workspace, External, Layers }

/// <summary>
/// [D7/T66 · K5 · design v1.19.0 §2.9] Settings modal diyaloğu (ince view): sol raydan seçilen dört sayfa —
/// <b>General</b>, <b>Workspace</b>, <b>External projects</b>, <b>Layers</b>. Sayfaların hepsi
/// <see cref="SettingsDraftViewModel"/>'e (test edilebilir taslak) bağlıdır — Save'e kadar canlı
/// <see cref="RunViewModel"/>'e dokunulmaz. Save = commit (persist + katmanlar + harici projeler + bekleyen
/// repo kökü + TEK Sync, <see cref="SettingsDraftViewModel.CommitAsync"/>), Cancel/scrim/Esc = taslağı at.
///
/// <para>[design v1.10.0 §2.9] Footer ayrıca <b>Export / Import / Clear</b> taşır. Üçü de yalnız FORMU
/// değiştirir: Save'e kadar hiçbir şey uygulanmaz ve onay dialogu yoktur — Clear'ın "onayı" iki aşamalı
/// düğmenin kendisidir.</para>
/// </summary>
public partial class SettingsDialog : ModalDialog
{
    /// <summary>[§2.9] Footer geri bildiriminin ve Clear'ın iki-aşamalı penceresinin süresi.</summary>
    internal const double FeedbackMs = 2400;

    /// <summary>[design v1.13.1 §2.9] Clear armed olduğunda görünen metin — footer geri bildirimi VE ikonun
    /// tooltip'i AYNI cümleyi taşır (kopya YASAK, CLAUDE.md): kullanıcı "tekrar tıklarsam silinecek" uyarısını
    /// nerede okursa okusun aynı sözü görür (bkz. <see cref="OnClear"/>).
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.13.1]</b> ESKİ metin (yalnız footer'da) "Click again to clear root and all
    /// layers" idi; ikonun tooltip'i armed durumdan hiç ETKİLENMİYORDU (sabit "Clear settings" kalıyordu, iki
    /// tık arasında da). Gerekçe (tasarım v1.13.1): geri bildirim metinleri genel olarak kısaldı; armed
    /// tooltip'i de footer'la AYNI kısa metne bağlandı.</para></summary>
    internal const string ClearArmedText = "Click again to clear";

    /// <summary>[design v1.13.1 §2.9] İkinci tıktan sonraki (form boşaltıldı) geri bildirimi.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.1]</b> ESKİ metin "Cleared — nothing is applied until you save" idi.</para></summary>
    internal const string ClearedText = "Cleared — save to apply";

    private SettingsDraftViewModel? _draft;
    private RunViewModel? _run;
    private IUiStateStore? _store;
    private Func<string?>? _pickFolder;
    private readonly DispatcherTimer _feedbackTimer = new();
    private bool _clearArmed;
    /// <summary>Clear ikonunun TABAN (armed olmayan) tooltip'i — XAML'in kendi değeri (kopya YASAK: burada
    /// yeniden yazılmaz, yalnız <see cref="DisarmClear"/> geri yüklemek için OKUR).</summary>
    private readonly object? _clearBaseTooltip;

    public SettingsDialog()
    {
        InitializeComponent();
        _feedbackTimer.Interval = TimeSpan.FromMilliseconds(FeedbackMs);
        _feedbackTimer.Tick += (_, _) => ResetFeedback();
        _clearBaseTooltip = ClearButton.ToolTip;
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
    internal Button CloseButton => CloseSettingsButton;
    internal TextBox RootInput => RepoRootInput;
    internal TextBlock Feedback => FeedbackText;
    internal TextBlock BlockedReason => BlockedReasonText;
    internal ShapePath ClearIcon => ClearGlyph;
    internal bool IsClearArmed => _clearArmed;

    /// <summary>[design v1.19.0 §2.9] Bölüm ↔ XAML eşlemesinin TEK yeri: her bölümün ray satırı ve sayfası (sayfanın
    /// görünürlüğü XAML'de ray satırının seçimine bağlıdır).</summary>
    private (RadioButton RailItem, FrameworkElement Page) Parts(SettingsSection section) => section switch
    {
        SettingsSection.General => (GeneralRailItem, GeneralPage),
        SettingsSection.Workspace => (WorkspaceRailItem, WorkspacePage),
        SettingsSection.External => (ExternalRailItem, ExternalPage),
        SettingsSection.Layers => (LayersRailItem, LayersPage),
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    /// <summary>Bölümün ray satırı.</summary>
    internal RadioButton RailItem(SettingsSection section) => Parts(section).RailItem;

    /// <summary>Bölümün sayfası.</summary>
    internal FrameworkElement Page(SettingsSection section) => Parts(section).Page;

    /// <summary>Bölümü seçer — raydaki tıklamanın yaptığının aynısı.</summary>
    internal void ShowSection(SettingsSection section) => RailItem(section).IsChecked = true;

    /// <summary>[design v1.19.0 §2.9] Sayfalar TEK <c>Body</c> ScrollViewer'ını paylaşır: bölüm değişince (ray
    /// tıklaması ya da <see cref="ShowSection"/>) yeni sayfa en üstten başlar, önceki sayfanın kaydırma payı taşınmaz.</summary>
    private void OnSectionChecked(object sender, RoutedEventArgs e) => Body.ScrollToTop();

    /// <summary>[design v1.8.0 §2.9] First run: henüz workspace yok. Kaydetmek aynı zamanda kurulumdur (düğme
    /// <c>Save and sync</c> der) ve diyalog Workspace sayfasında açılır.</summary>
    private bool IsFirstRun => _run?.HasWorkspace != true;

    /// <summary>[design v1.19.0 §2.9] Açılış bölümü: first run'da Workspace (başlamak için gereken tek zorunlu ayar
    /// orada), sonrasında General.</summary>
    private SettingsSection OpeningSection => IsFirstRun ? SettingsSection.Workspace : SettingsSection.General;

    /// <summary>[design v1.19.0 §2.9] Açılışta odak, açılan sayfanın ilk girdisine gider (Workspace: repository root
    /// input'u, General: ilk switch) — başlık satırının kapat düğmesine değil.</summary>
    protected override UIElement InitialFocusScope => Page(OpeningSection);

    /// <summary>[D7] Diyaloğu açar: canlı pattern'lerin bir TASLAK kopyasını kurar (SettingsDraftViewModel),
    /// repo yolunu gösterir ve görünür kılar. <paramref name="pickFolder"/> klasör seçici seam'idir (testler
    /// gerçek diyalog açmaz — E1'deki IOsActions.PickFolder gelene dek OpenFolderDialog doğrudan çağrılır).</summary>
    public void Open(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        _run = run;
        _store = store;
        _pickFolder = pickFolder;
        _draft = new SettingsDraftViewModel(
            run.LayerPatterns, run.RootPath, run.ExternalProjects, run.UpdateExternals, run.StashOnBranchSwitch);
        DataContext = _draft;
        ResetFeedback();
        RefreshSaveLabel();
        // [design v1.19.0 §2.9] Açılış bölümü her açılışta yeniden seçilir (OpeningSection).
        ShowSection(OpeningSection);
        // [D7 re-review][Fix1 → design v1.19.0 ortak kabuk] Görünür kılma, giriş ve odağı diyaloğun İÇİNE taşıma
        // ModalDialog'dadır; odağın düşeceği yer açılan sayfadır (InitialFocusScope → sayfanın ilk girdisi).
        //
        // [DEĞİŞEN KURAL — design v1.19.0] ESKİ: Settings giriş animasyonu OYNATMAZDI (yalnız About ve What's
        // new oynatırdı); prototipin ortak DialogShell'i ds-dialog-in'i üçüne de takar — Settings de 180ms fade
        // + 6px yükselir (DialogShellTests.Settings_now_plays_the_dialog_entrance).
        ShowDialog();
    }

    /// <summary>[design v1.10.0 §2.4] First run'daki <c>Import settings…</c> kısayolu: diyaloğu açar ve dosya
    /// seçiciyi HEMEN tetikler — hazır bir ayar dosyası olan developer tek adımda başlar.</summary>
    public void OpenForImport(RunViewModel run, IUiStateStore store, Func<string?> pickFolder)
    {
        Open(run, store, pickFolder);
        OnImport(this, new RoutedEventArgs());
    }

    /// <summary>Her kapanış yolu (Cancel, Save, scrim, Esc, MainWindow'un Esc güvenlik ağı) geri bildirimi ve
    /// Clear'ın kurulu durumunu sıfırlar — taslak zaten bir kopyadır ve atılır.</summary>
    protected override void OnDialogClosing() => ResetFeedback();

    /// <summary>[design v1.8.0 §2.9] First run'da kaydetmek AYNI ZAMANDA kurulumdur — düğme bunu söyler:
    /// <c>Save and sync</c>. Sonrasında yalnız <c>Save</c>.</summary>
    private void RefreshSaveLabel() =>
        SaveButton.Content = IsFirstRun ? "Save and sync" : "Save";

    // ---- Layers ----

    private void OnAddLayer(object sender, RoutedEventArgs e) => _draft?.AddLayer();

    private void OnRemoveLayer(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayerRowViewModel row }) _draft?.RemoveLayer(row);
    }

    // ---- External projects (design v1.14.0 §9 · K5) ----

    private void OnAddExternal(object sender, RoutedEventArgs e) => _draft?.AddExternal();

    private void OnRemoveExternal(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExternalRowViewModel row }) _draft?.RemoveExternal(row);
    }

    // [design v1.19.0 §2.9] External projects'in alt satırındaki "Pull before build" düğmesi: anahtar General'dadır.
    private void OnShowGeneral(object sender, RoutedEventArgs e) => ShowSection(SettingsSection.General);

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
            ShowFeedback(SettingsFile.ExportedMessage, success: true);
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
            ClearButton.ToolTip = ClearArmedText;
            ShowFeedback(ClearArmedText, success: false);
            return;
        }

        DisarmClear();
        _draft.ClearAll();
        ShowFeedback(ClearedText, success: true);
    }

    private void DisarmClear()
    {
        if (!_clearArmed) return;
        _clearArmed = false;
        ClearButton.ToolTip = _clearBaseTooltip;
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
        CloseDialog();
        await draft.CommitAsync(run, store);
    }

    // Taslak (kopya) atılır. Scrim tıklaması ve Esc (BuildApp.jsx:1312) aynı Cancel anlamıyla ortak kabuktadır.
    private void OnCancel(object sender, RoutedEventArgs e) => CloseDialog();
}
