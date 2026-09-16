using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Logs;

namespace BuildOrchestrator.App.Views;

/// <summary>[About] Shortcuts sekmesinin bir satırı (görünüm modeli).</summary>
internal readonly record struct ShortcutRow(string Description, IReadOnlyList<string> Gestures, bool Unavailable);

/// <summary>[design v1.19.0 §2.10] Shortcuts sekmesinin bir caps grubu (görünüm modeli).</summary>
internal sealed record ShortcutGroupRows(string Title, IReadOnlyList<ShortcutRow> Rows);

/// <summary>
/// [design v1.19.0 §2.10] About: ürün kimliği + About / Environment / Shortcuts sekmeleri. Kabuk (scrim, çerçeve,
/// odak tuzağı, Esc/scrim ile kapanma, giriş) üç dialogun ORTAK kabuğudur: <see cref="Controls.ModalDialog"/>.
///
/// <para><b>İnce view:</b> gösterilen her şey saf tiplerden gelir — <see cref="AppIdentity"/>,
/// <see cref="ShortcutCatalog"/>, <see cref="DiagnosticsReport"/>, <see cref="ReleaseNotes"/>. Burada hiçbir metin,
/// sürüm ya da yol YENİDEN YAZILMAZ. About sekmesinin Version/Engine/Copyright satırları, Environment sekmesinin
/// iki grubu ve "Copy diagnostics" metni AYNI <see cref="DiagnosticsSnapshot"/>'tan okunur.</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> Third-party sekmesi (ve atıf tablosu
/// <c>ThirdPartyNotices</c>) KALDIRILDI — bilinçli tasarım kararı; Geist lisans metni <c>Assets/GEIST-LICENSE.txt</c>
/// olarak dağıtımda kalır.</para>
///
/// <para><b>MSBuild LAZY çözülür:</b> <c>vswhere</c> bir child process başlatır ve About'u AÇMAK bunu
/// tetiklememelidir. Çözüm Environment sekmesi İLK kez seçildiğinde başlar; sonucu diyalog ömrü boyunca
/// cache'lenir.</para>
/// </summary>
public partial class AboutDialog : Controls.ModalDialog
{
    // Görünür etiket ve UIA adı AYNI sabitten (kopya YASAK) — bkz. AccessibilityNames.CopyDiagnostics.
    private const string CopyLabel = AccessibilityNames.CopyDiagnostics;
    private const string CopiedLabel = "Copied";

    // [design-v1.2.1 §2.10] Kopyalandı geri bildirimi GÖRSELDİR: ikon ✓'ye, renk başarı tonuna döner.
    // Konsolun copy-log butonuyla AYNI ikon çifti ve AYNI süre (CopyLogFeedback.RevertMs) kullanılır.
    private const string CopyIconKey = "Icon.Copy";
    private const string CheckIconKey = "Icon.Check";
    private const string CopyIdleBrushKey = "Brush.TextSecondary";
    private const string CopiedBrushKey = "Brush.StatusSuccessText";

    // [kopya YASAK] Konsolun copy butonuyla AYNI geri-bildirim saati (CopyLogFeedback.RevertMs) — About kendi
    // süresini uydurmaz.
    private readonly CopyLogFeedback _copyFeedback = new();
    private DispatcherTimer? _copyRevertTimer;
    private Stopwatch? _copyClock;

    private RunViewModel? _run;
    private Func<Task<string>>? _resolveMsBuild;
    private string _msBuild = DiagnosticsReport.Resolving;
    private bool _msBuildRequested;

    public AboutDialog()
    {
        InitializeComponent();
        string whatsNew = ReleaseNotes.WhatsNewInLabel(AppIdentity.Version);
        WhatsNewLabel.Text = whatsNew;
        AutomationProperties.SetName(WhatsNewButton, whatsNew); // içerik StackPanel → ad AÇIKÇA verilir
        ResetCopyVisual();
    }

    /// <summary>[T56/3b deseni] Panoya yazma yolu — üretimde retry sarmalayıcı, testte enjekte edilir
    /// (gerçek panoya dokunmadan geri bildirim doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>[test yüzeyi] O ANDA çizilen tanı modeli — About sekmesi, Environment sekmesi ve "Copy
    /// diagnostics" AYNI nesneden okur.</summary>
    internal DiagnosticsSnapshot? Diagnostics { get; private set; }

    /// <summary>[design v1.19.0 §2.10] About sekmesinin <c>What's new in {sürüm}</c> butonuna basıldı — diyalog
    /// kendini KAPATMIŞTIR. What's new'i açmak MainWindow'un işidir (sparkle butonuyla AYNI yol; okunmadı noktası
    /// orada söner) — diyalog kalıcı durumu ve diğer katmanları BİLMEZ.</summary>
    public event Action? WhatsNewRequested;

    /// <summary>[test yüzeyi] "Copied" geri bildirimi görünür mü.</summary>
    internal bool IsShowingCopied => _copyFeedback.Copied;

    /// <summary>[test yüzeyi] Butonun ikonu ✓ mü — GERÇEK <c>Path.Data</c> okunur. Icons.xaml bu kontrolün
    /// kaynak kapsamında değilse (merge yok) her iki durumda da null olurdu, bu yüzden çözülemeyen ikon
    /// açıkça "kopyalanmadı" sayılır (ConsoleHeader.IsShowingCopied deseni).</summary>
    internal bool IsShowingCheckIcon
        => TryFindResource(CheckIconKey) is Geometry check && ReferenceEquals(CopyGlyph.Data, check);

    /// <summary>[test yüzeyi] Butonun o anki ön plan fırçası (başarıda başarı tonuna döner).</summary>
    internal Brush? CopyButtonForeground => CopyButton.Foreground;

    /// <summary>
    /// Diyaloğu açar.
    /// <paramref name="hotkeyRegistered"/> global kısayolun GERÇEKTEN kayıtlı olup olmadığıdır (çakışmada
    /// sessiz devre dışı — bkz. <see cref="HotkeyRegistration"/>); <c>false</c> ise o satır "unavailable"
    /// işaretlenir. <paramref name="resolveMsBuild"/> vswhere seam'idir (testler process başlatmaz).
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.0 §2.10]</b> ESKİ İMZA bir <c>openOnWhatsNew</c> parametresi
    /// taşıyordu: görülmemiş bir sürüm varsa diyalog DOĞRUDAN What's new sekmesinde açılırdı. What's new
    /// kendi diyaloguna (<see cref="NotesDialog"/>) taşındığı için bu yönlendirme kalktı (yönlendirme MainWindow'da
    /// sparkle butonuna/Ctrl+F1'e gider). <b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> Her açılış ilk sekmede
    /// başlar — ilk sekme artık Shortcuts değil <b>About</b>'tur.</para>
    /// </summary>
    public void Open(RunViewModel run, bool hotkeyRegistered, Func<Task<string>> resolveMsBuild)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(resolveMsBuild);
        _run = run;
        _resolveMsBuild = resolveMsBuild;
        _msBuild = DiagnosticsReport.Resolving;
        _msBuildRequested = false;

        // [design v1.19.0 §2.10] Grup bilgisi ve sırası katalogdadır; burada yalnız görünüm modeline çevrilir.
        ShortcutGroups.ItemsSource = ShortcutCatalog.GroupOrder
            .Select(g => new ShortcutGroupRows(ShortcutCatalog.GroupTitle(g), ShortcutCatalog.All
                .Where(e => e.Group == g)
                .Select(e => new ShortcutRow(e.Description, e.Gestures,
                    Unavailable: e.Id == ShortcutId.RestoreFromTray && !hotkeyRegistered))
                .ToList()))
            .ToList();

        RefreshDiagnostics();

        // [design v1.19.0 §2.10] ⓘ ve F1 her zaman About sekmesinde açar — What's new'e yönlendirme YOKTUR
        // (o dialog kendi butonundan, Ctrl+F1'den ya da About sekmesindeki butondan açılır).
        AboutTab.IsChecked = true; // her açılış ilk sekmeden başlar
        ResetCopyVisual();
        // [design-v1.2.1 §2.10] 180ms fade + 6px yukarı, odak dialogun içine — ortak kabuk (ModalDialog).
        ShowDialog();
    }

    // ---------------------------------------------------------------- tanı

    /// <summary>Tanı modelini TEK yerden (<see cref="DiagnosticsReport"/>) yeniden kurar ve üç satır listesini
    /// ondan besler. Kimlik değerleri <see cref="AppIdentity"/>'den, motor değerleri motorun KENDİ bildiriminden
    /// (<see cref="RunViewModel.EngineVersion"/>, <see cref="RunViewModel.EnginePid"/>), yol metinleri üretimin
    /// kendi static'lerinden gelir — burada YENİDEN YAZILMAZ.</summary>
    private void RefreshDiagnostics()
    {
        if (_run is not { } run) return;
        var diagnostics = DiagnosticsReport.Compose(new DiagnosticsInput(
            Product: AppIdentity.Product,
            Version: AppIdentity.Version,
            Copyright: AppIdentity.Copyright,
            EngineVersion: run.EngineVersion,
            EnginePid: run.EnginePid,
            Runtime: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            MsBuild: _msBuild,
            RepositoryRoot: run.RootPath,
            StateFile: JsonUiStateStore.DefaultPath,
            LogsRoot: RunLogPaths.DefaultLogsRoot,
            WorktreePool: WorktreeManager.DefaultPoolRoot));
        Diagnostics = diagnostics;
        IdentityRows.ItemsSource = diagnostics.Identity;
        RuntimeRows.ItemsSource = diagnostics.Runtime;
        PathsRows.ItemsSource = diagnostics.Paths;
    }

    // Environment sekmesi İLK kez seçildiğinde vswhere'i başlatır; sonuç cache'lenir (ikinci seçim çözmez).
    private async void OnEnvironmentTabChecked(object sender, RoutedEventArgs e)
    {
        if (_msBuildRequested || _resolveMsBuild is not { } resolve) return;
        _msBuildRequested = true;
        _msBuild = await resolve();
        RefreshDiagnostics();
    }

    // ---------------------------------------------------------------- environment değer kaydırma

    /// <summary>
    /// [DEĞİŞEN KURAL — design v1.13.1 §2.10] Environment satırının DEĞER hücresi artık kırpılmaz (bkz.
    /// AboutDialog.xaml'deki DataTemplate yorumu) — onun yerine yatay kayar, ve bu metot o kaydırmanın
    /// tekerlek yönlendirmesidir. Normal (dikey) fare tekerleği, hücre GERÇEKTEN taşıyorsa
    /// (<c>ScrollableWidth &gt; 0</c>) yatay ofsete uygulanır ve olay burada durur. Taşmıyorsa yatay ofsete
    /// DOKUNULMAZ ama olay yine de elden geçer: ebeveyne DEVREDİLEREK kendi dikey yoluna (Environment
    /// sekmesinin ScrollViewer'ı) ulaştırılır — bkz. <see cref="ForwardWheelToParent"/> ve aşağıdaki ölçüm
    /// paragrafı. Devir olmadan iç ScrollViewer olayı yutar ve sekme HİÇ kaymaz.
    ///
    /// <para><b>Neden <see cref="Controls.HorizontalWheelScroll"/> DEĞİL:</b> o sınıf farklı bir sorunu çözer —
    /// GERÇEKTEN yatay bir tekerlek/touchpad sinyali (<c>WM_MOUSEHWHEEL</c>) WPF'e HİÇ ulaşmaz, bu yüzden
    /// pencerenin HWND mesaj yoluna kanca gerekir (+ bir Dispatcher turu ertelemesi, çünkü istek WndProc'un
    /// İÇİNDEN yapılır). Buradaki istek FARKLI: prototipin <c>onWheel → scrollLeft += deltaY</c>'i — DÜZ dikey
    /// tekerlek, ki WPF onu zaten normal bir <c>MouseWheel</c> routed event'i olarak dağıtır. HWND kancası ya
    /// da erteleme YOKTUR: istek senkron, doğrudan <see cref="ScrollViewer.ScrollToHorizontalOffset"/> ile
    /// uygulanır.</para>
    ///
    /// <para><b>Taşmayan hücrede olay ELDEN GEÇİRİLİR (ÖLÇÜLDÜ):</b> <c>VerticalScrollBarVisibility="Disabled"</c>
    /// olması TEK BAŞINA YETMEZ. Ölçüm: bu hücrenin üzerinde BALONCUK fazındaki <c>MouseWheel</c> olayı
    /// <c>Handled=True</c> ile dönüyor ve dış panelin <c>VerticalOffset</c>'i 0'da kalıyor —
    /// <see cref="ScrollViewer"/> kendi <c>OnMouseWheel</c> class handler'ında olayı, dikeyde kaydıracak bir
    /// şeyi OLUP OLMADIĞINA BAKMADAN yutuyor. Değer hücresi satırın <c>DockPanel</c>'inde <c>LastChildFill</c>
    /// olduğu için bu, Environment yüzeyinin çoğunda tekerleği ÖLDÜRÜRDÜ. Çözüm WPF'in standart iç-içe
    /// ScrollViewer deseni: preview'da olayı yut (böylece class handler hiç koşmaz) ve ebeveynden yeni bir
    /// baloncuk olayı yayınla — bkz. <see cref="ForwardWheelToParent"/>. Test dış panelin
    /// <c>VerticalOffset</c>'inin gerçekten ARTTIĞINI pinler
    /// (<c>The_wheel_over_a_non_overflowing_environment_value_still_scrolls_the_tab</c>); <c>Handled</c> tek
    /// başına bu davranışı pinlemez.</para>
    /// </summary>
    private void OnEnvironmentValueWheel(object sender, MouseWheelEventArgs e)
    {
        var scroller = (ScrollViewer)sender;
        if (scroller.ScrollableWidth <= 0) { ForwardWheelToParent(scroller, e); return; }
        scroller.ScrollToHorizontalOffset(
            EnvironmentValueWheelOffset(scroller.HorizontalOffset, e.Delta, scroller.ScrollableWidth));
        e.Handled = true;
    }

    /// <summary>Taşmayan hücrenin tekerleğini ebeveyne devreder: preview'daki olay YUTULUR (iç ScrollViewer'ın
    /// yutan class handler'ı böylece hiç koşmaz) ve ebeveynden AYNI delta'yla yeni bir baloncuk
    /// <c>MouseWheel</c> yayınlanır — dış (sekme) ScrollViewer'a ulaşan olay budur. <c>Source</c> hücrenin
    /// KENDİSİ kalır: olay yolun ilerisinde hâlâ nereden geldiğini söyler.
    /// <para>Ebeveyn yoksa (hücre ağaçtan koparılmışsa) olay YUTULMAZ — devredilemeyen bir olayı yutmak, onu
    /// sessizce yok etmek olurdu.</para></summary>
    private static void ForwardWheelToParent(ScrollViewer scroller, MouseWheelEventArgs e)
    {
        if (VisualTreeHelper.GetParent(scroller) is not UIElement parent) return;
        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            { RoutedEvent = MouseWheelEvent, Source = scroller });
    }

    /// <summary>SAF karar: bir dikey tekerlek notch'unun (WPF <c>Delta</c>) yatay ofsete karşılığı, içeriğin
    /// sınırlarına kelepçeli. WPF'in dikey ScrollViewer'ı pozitif <c>Delta</c>'yı YUKARI sayar (ofset AZALIR —
    /// <c>e.Delta &gt; 0 ⇒ LineUp</c>); prototipin <c>scrollLeft += deltaY</c> hissiyle (tekerlek AŞAĞI ⇒ yol
    /// SAĞA) aynı fiziksel yöne ulaşmak için işaret WPF'in KENDİ dikey kuralıyla aynı çevrilir:
    /// <c>ofset -= delta</c>.</summary>
    internal static double EnvironmentValueWheelOffset(double currentOffset, double delta, double scrollableWidth) =>
        Math.Clamp(currentOffset - delta, 0, Math.Max(0, scrollableWidth));

    // ---------------------------------------------------------------- copy diagnostics

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e) => CopyDiagnostics();

    /// <summary>Tanı raporunu panoya yazar. Başarıda buton etiketi <see cref="CopyLogFeedback.RevertMs"/>
    /// boyunca "Copied" olur — süre sabiti konsolun copy butonuyla PAYLAŞILIR (kopya YASAK).</summary>
    public void CopyDiagnostics()
    {
        if (!ClipboardWriter(DiagnosticsText())) return; // kalıcı pano kilidi: sessiz

        _copyClock = Stopwatch.StartNew();
        _copyFeedback.MarkCopied(TimeSpan.Zero);
        SetCopyVisual(CheckIconKey, CopiedLabel, CopiedBrushKey);

        _copyRevertTimer?.Stop();
        _copyRevertTimer ??= CreateRevertTimer();
        _copyRevertTimer.Start();
    }

    /// <summary>[design v1.19.0 §2.10] Panoya giden metin — biçimi <see cref="DiagnosticsReport.ToText"/>'indir
    /// (başlık satırı + hizalı Engine/Runtime/Paths), girdisi ekrandaki AYNI modeldir.</summary>
    internal string DiagnosticsText() => Diagnostics is { } d ? DiagnosticsReport.ToText(d) : "";

    private DispatcherTimer CreateRevertTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (_copyClock is not null && _copyFeedback.ShouldRevert(_copyClock.Elapsed)) ResetCopyVisual();
        };
        return timer;
    }

    private void ResetCopyVisual()
    {
        _copyRevertTimer?.Stop();
        _copyClock?.Stop();
        _copyClock = null;
        _copyFeedback.Revert();
        SetCopyVisual(CopyIconKey, CopyLabel, CopyIdleBrushKey);
    }

    /// <summary>Butonun görselini (ikon geometrisi + boya semantiği + etiket + renk) TEK yerden sürer —
    /// <see cref="Console.ConsoleHeader.SetCopyIcon"/> ile aynı desen; boya <see cref="Controls.IconPaint"/>
    /// üzerinden sözlükten gelir (kalınlık/dolgu kararı Icons.xaml'indir).</summary>
    private void SetCopyVisual(string iconKey, string label, string brushKey)
    {
        Controls.IconPaint.Apply(CopyGlyph, this, iconKey, brushKey);
        CopyLabelText.Text = label;
        CopyButton.SetResourceReference(ForegroundProperty, brushKey);
    }

    // ---------------------------------------------------------------- kapatma

    // Scrim tıklaması ve Esc ortak kabuktadır (ModalDialog).
    private void OnClose(object sender, RoutedEventArgs e) => CloseDialog();

    /// <summary>[design v1.19.0 §2.10] Önce About kapanır, sonra istek bildirilir: What's new açılırken About artık
    /// görünür bir katman değildir (Esc zinciri tek katman iner).</summary>
    private void OnWhatsNew(object sender, RoutedEventArgs e)
    {
        CloseDialog();
        WhatsNewRequested?.Invoke();
    }
}
