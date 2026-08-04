using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private const string CopyLabel = "Copy diagnostics";
    private const string CopiedLabel = "Copied";

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
        ResetCopyLabel();
    }

    /// <summary>[T56/3b deseni] Panoya yazma yolu — üretimde retry sarmalayıcı, testte enjekte edilir
    /// (gerçek panoya dokunmadan geri bildirim doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>[test yüzeyi] Environment sekmesinin O ANDA çizdiği satırlar — "Copy diagnostics" de AYNI
    /// listeyi metne çevirir.</summary>
    internal IReadOnlyList<DiagnosticsLine> DiagnosticsLines { get; private set; } = [];

    /// <summary>[test yüzeyi] "Copied" geri bildirimi görünür mü.</summary>
    internal bool IsShowingCopied => _copyFeedback.Copied;

    /// <summary>
    /// Diyaloğu açar.
    /// <paramref name="hotkeyRegistered"/> global kısayolun GERÇEKTEN kayıtlı olup olmadığıdır (çakışmada
    /// sessiz devre dışı — bkz. <see cref="HotkeyRegistration"/>); <c>false</c> ise o satır "unavailable"
    /// işaretlenir. <paramref name="resolveMsBuild"/> vswhere seam'idir (testler process başlatmaz).
    /// </summary>
    public void Open(RunViewModel run, bool hotkeyRegistered, Func<Task<string>> resolveMsBuild)
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

        ShortcutsTab.IsChecked = true; // her açılış ilk sekmeden başlar
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

    // ---------------------------------------------------------------- copy diagnostics

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e) => CopyDiagnostics();

    /// <summary>Tanı raporunu panoya yazar. Başarıda buton etiketi <see cref="CopyLogFeedback.RevertMs"/>
    /// boyunca "Copied" olur — süre sabiti konsolun copy butonuyla PAYLAŞILIR (kopya YASAK).</summary>
    public void CopyDiagnostics()
    {
        if (!ClipboardWriter(DiagnosticsReport.ToText(DiagnosticsLines))) return; // kalıcı pano kilidi: sessiz

        _copyClock = Stopwatch.StartNew();
        _copyFeedback.MarkCopied(TimeSpan.Zero);
        CopyButton.Content = CopiedLabel;

        _copyRevertTimer?.Stop();
        _copyRevertTimer ??= CreateRevertTimer();
        _copyRevertTimer.Start();
    }

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
        ResetCopyLabel();
    }

    private void ResetCopyLabel() => CopyButton.Content = CopyLabel;

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
