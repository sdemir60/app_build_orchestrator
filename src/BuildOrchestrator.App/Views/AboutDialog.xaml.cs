using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
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

/// <summary>[About] Third-party sekmesinin bir satırı — sürüm çalışma zamanında çözülür, boş olabilir.</summary>
internal readonly record struct NoticeRow(string DisplayName, string Version, string License);

/// <summary>
/// [About] İkinci modal diyalog: ürün kimliği + klavye kısayolları + ortam/tanı + üçüncü-taraf lisansları.
/// Kabuk <see cref="SettingsDialog"/> ile AYNIdır (scrim, 620px Ds.Dialog, odak tuzağı, Esc/scrim ile kapanma).
///
/// <para><b>İnce view:</b> gösterilen her şey saf tiplerden gelir — <see cref="AppIdentity"/>,
/// <see cref="ShortcutCatalog"/>, <see cref="DiagnosticsReport"/>, <see cref="ThirdPartyNotices"/>. Burada
/// hiçbir metin, sürüm ya da yol YENİDEN YAZILMAZ.</para>
///
/// <para><b>MSBuild LAZY çözülür:</b> <c>vswhere</c> bir child process başlatır ve About'u AÇMAK bunu
/// tetiklememelidir. Çözüm Environment sekmesi İLK kez seçildiğinde başlar; sonucu diyalog ömrü boyunca
/// cache'lenir.</para>
/// </summary>
public partial class AboutDialog : UserControl
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
        ResetCopyVisual();
    }

    /// <summary>[T56/3b deseni] Panoya yazma yolu — üretimde retry sarmalayıcı, testte enjekte edilir
    /// (gerçek panoya dokunmadan geri bildirim doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>[test yüzeyi] Environment sekmesinin O ANDA çizdiği satırlar — "Copy diagnostics" de AYNI
    /// listeyi metne çevirir.</summary>
    internal IReadOnlyList<DiagnosticsLine> DiagnosticsLines { get; private set; } = [];

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
    /// </summary>
    /// <param name="openOnWhatsNew">[design v1.9.0 §2.10] Görülmemiş bir sürüm varsa diyalog DOĞRUDAN
    /// <i>What's new</i> sekmesinde açılır.</param>
    public void Open(RunViewModel run, bool hotkeyRegistered, Func<Task<string>> resolveMsBuild,
        bool openOnWhatsNew = false)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(resolveMsBuild);
        _run = run;
        _resolveMsBuild = resolveMsBuild;
        _msBuild = DiagnosticsReport.Resolving;
        _msBuildRequested = false;

        ProductText.Text = AppIdentity.Product;
        TaglineText.Text = AppIdentity.Tagline;
        // [design-v1.1.0] TEK sürüm satırı. Eskiden burada `{app} · engine {engine} · {telif}` vardı; motor
        // sürümünün yeri Environment sekmesidir, başlıkta tekrarı gürültüydü.
        IdentityText.Text = string.Format(CultureInfo.InvariantCulture, "{0} · {1}",
            AppIdentity.Version, AppIdentity.Copyright);

        ShortcutRows.ItemsSource = ShortcutCatalog.All
            .Select(e => new ShortcutRow(e.Description, e.Gestures,
                Unavailable: e.Id == ShortcutId.RestoreFromTray && !hotkeyRegistered))
            .ToList();

        ThirdPartyRows.ItemsSource = ThirdPartyNotices.All
            .Select(c => new NoticeRow(c.DisplayName, ThirdPartyNotices.ResolveVersion(c) ?? "", c.License))
            .ToList();
        FontLicenseNoteText.Text = ThirdPartyNotices.FontLicenseNote;

        RefreshDiagnostics();
        BuildWhatsNew(showAll: false); // [§2.10] katlama her açılışta 3'e döner

        // [design v1.9.0 §2.10] Görülmemiş bir sürüm varsa About DOĞRUDAN What's new'da açılır — yönlendirme
        // bir açılış toast'ıyla değil, buraya yapılır (§8: karşılama pop-up'ı YOK).
        if (openOnWhatsNew) WhatsNewTab.IsChecked = true;
        else ShortcutsTab.IsChecked = true; // her açılış ilk sekmeden başlar
        ResetCopyVisual();
        Visibility = Visibility.Visible;
        // [design-v1.2.1 §2.10] 180ms fade + 6px yukarı. Visibility'den SONRA: animasyon görünür bir öğe
        // üzerinde kurulur (reduced-motion'da PlayDialog son duruma SNAP eder).
        Controls.PopIn.PlayDialog(DialogShell);
        Focus(); // Esc HER durumda yakalanabilsin (MoveFocus altta bir şey bulamazsa bile odak burada kalır)
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Close() => Visibility = Visibility.Collapsed;

    /// <summary>Esc zincirinin dialog katmanı için dışarıdan kapatma (MainWindow güvenlik ağı — odak dialog
    /// dışındayken). Dialog odaklıyken Esc'i <see cref="OnKeyDown"/> yakalar (handled).</summary>
    public void CloseDialog() => Close();

    // ---------------------------------------------------------------- tanı

    /// <summary>Satırları TEK yerden (<see cref="DiagnosticsReport"/>) yeniden kurar. Yol metinleri üretimin
    /// kendi static'lerinden gelir — burada YENİDEN YAZILMAZ.</summary>
    private void RefreshDiagnostics()
    {
        if (_run is not { } run) return;
        DiagnosticsLines = DiagnosticsReport.Compose(new DiagnosticsInput(
            AppVersion: AppIdentity.Version,
            EngineVersion: run.EngineVersion,
            EnginePid: run.EnginePid,
            Runtime: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            MsBuild: _msBuild,
            RepositoryRoot: run.RootPath,
            StateFile: JsonUiStateStore.DefaultPath,
            LogsRoot: RunLogPaths.DefaultLogsRoot,
            WorktreePool: WorktreeManager.DefaultPoolRoot));
        EnvironmentRows.ItemsSource = DiagnosticsLines;
    }

    // Environment sekmesi İLK kez seçildiğinde vswhere'i başlatır; sonuç cache'lenir (ikinci seçim çözmez).
    private async void OnEnvironmentTabChecked(object sender, RoutedEventArgs e)
    {
        if (_msBuildRequested || _resolveMsBuild is not { } resolve) return;
        _msBuildRequested = true;
        _msBuild = await resolve();
        RefreshDiagnostics();
    }

    // ---------------------------------------------------------------- [design v1.9.0 §2.10] What's new

    /// <summary>Sekme GÖRÜLDÜ — title bar'daki "görülmemiş sürüm" noktası söner. Kablo <c>MainWindow</c>'da
    /// (kalıcı duruma yazma orada; diyalog yalnız olguyu bildirir).</summary>
    public event Action? NotesSeen;

    /// <summary>[test yüzeyi] Çizilmiş sürüm blokları.</summary>
    internal IReadOnlyList<FrameworkElement> WhatsNewBlocks => [.. WhatsNewRows.Children.Cast<FrameworkElement>()];
    internal Button EarlierVersions => EarlierVersionsButton;
    internal RadioButton WhatsNew => WhatsNewTab;

    private void OnWhatsNewTabChecked(object sender, RoutedEventArgs e) => NotesSeen?.Invoke();

    /// <summary>
    /// [§2.10] Sürüm listesini kurar: en yeni üstte, <b>son 3 sürüm açık</b>, gerisi ghost bir düğmenin
    /// altında katlı. Katlama diyalog her açılışında 3'e döner (geri katlama düğmesi YOKTUR — açtıysan
    /// okuyorsundur).
    /// </summary>
    private void BuildWhatsNew(bool showAll)
    {
        WhatsNewRows.Children.Clear(); // minik, non-virtualized liste (BuildMenu deseni)
        var all = ReleaseNotes.All;
        int shown = showAll ? all.Count : Math.Min(ReleaseNotes.OpenByDefault, all.Count);
        for (int i = 0; i < shown; i++) WhatsNewRows.Children.Add(BuildVersionBlock(all[i], first: i == 0));

        int hidden = all.Count - shown;
        EarlierVersionsButton.Content = ReleaseNotes.EarlierVersionsLabel(hidden);
        EarlierVersionsButton.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowEarlierVersions(object sender, RoutedEventArgs e) => BuildWhatsNew(showAll: true);

    /// <summary>[§2.10] Bir sürüm bloğu: mono numara + (güncelse) sessiz caps <c>CURRENT</c> etiketi + sağa
    /// yaslı tarih; altında kategori BLOKLARI. Sürümler arasında 14px boşluk + 1px ayraç.</summary>
    private FrameworkElement BuildVersionBlock(ReleaseEntry entry, bool first)
    {
        var block = new StackPanel { Margin = new Thickness(0, first ? 0 : 14, 0, 0) };
        if (!first)
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) };
            divider.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
            block.Children.Insert(0, divider);
        }

        var header = new DockPanel();
        var date = new TextBlock { Text = entry.Date, VerticalAlignment = VerticalAlignment.Center, FontFamily = Controls.AppFonts.Mono };
        date.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        date.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        DockPanel.SetDock(date, Dock.Right);
        header.Children.Add(date);

        var version = new TextBlock { Text = entry.Version, VerticalAlignment = VerticalAlignment.Center, FontFamily = Controls.AppFonts.Mono };
        version.SetResourceReference(FontSizeProperty, "FontSize.Sm");
        version.SetResourceReference(FontWeightProperty, "FontWeight.Emphasis");
        version.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        header.Children.Add(version);

        // [§2.10] CURRENT etiketi SESSİZDİR: yalnız text-faint metin — zemin/çerçeve YOK (amber rozet göze
        // batıyordu).
        if (string.Equals(entry.Version, AppIdentity.Version, StringComparison.Ordinal))
        {
            var current = new Controls.TrackedTextBlock
            {
                Text = "CURRENT",
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            current.SetResourceReference(FontSizeProperty, "FontSize.2xs");
            current.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
            header.Children.Add(current);
        }
        block.Children.Add(header);

        // [§2.10] Kategori BLOK başlığıdır (satır başına ikon/sigil YOK); boş kategori hiç çizilmez.
        foreach (var kind in ReleaseNotes.KindOrder)
        {
            var items = entry.Notes.Where(n => n.Kind == kind).ToList();
            if (items.Count == 0) continue;
            block.Children.Add(BuildCategory(kind, items));
        }
        return block;
    }

    private FrameworkElement BuildCategory(NoteKind kind, IReadOnlyList<ReleaseNote> items)
    {
        var group = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = 6,
            Height = 6,
            RadiusX = 1,
            RadiusY = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ReleaseNotes.SwatchBrushKey(kind));
        heading.Children.Add(swatch);

        var label = new Controls.TrackedTextBlock
        {
            Text = ReleaseNotes.Label(kind),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextDim");
        heading.Children.Add(label);
        group.Children.Add(heading);

        foreach (var note in items)
        {
            var text = new TextBlock
            {
                Text = note.Text,
                Margin = new Thickness(13, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            text.SetResourceReference(FontSizeProperty, "FontSize.Sm");
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            text.SetResourceReference(TextBlock.LineHeightProperty, "LineHeight.Snug13"); // 13px gövde → snug
            group.Children.Add(text);
        }
        return group;
    }

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

    /// <summary>[design-v1.2.1 §2.10] Panoya giden metin: ilk satır ürün + sürüm, ardından tüm Environment
    /// satırları. Başlık satırı olmadan çıktı, nereden geldiği belirsiz bir anahtar/değer yığınıdır.</summary>
    internal string DiagnosticsText() =>
        string.Format(CultureInfo.InvariantCulture, "{0} {1}", AppIdentity.Product, AppIdentity.Version)
        + Environment.NewLine
        + DiagnosticsReport.ToText(DiagnosticsLines);

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

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // Scrim tıklaması kapatır; diyaloğun kendi içine tıklama scrim'e ULAŞMAZ.
    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();
    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
