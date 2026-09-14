using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>[T56/3a+3b → v1.18.0 §9] Konsol panel başlığının iki modu (README §9 v1.18.0 "Konsol başlığı ve
/// `Back` satırı"). Kod-tarafı sürülür (DP/binding şişkinliği yerine küçük, test edilebilir yüzey):
/// <see cref="ShowNarrative"/> / <see cref="ShowProjectLog"/> modu değiştirir, <see cref="SetLineCount"/>
/// sağdaki "N lines" sayacını günceller. Statü rengi token ANAHTARIndan (<see cref="ConsoleStatus.BrushKey"/>)
/// SetResourceReference ile canlı çözülür (hardcode YASAK).
///
/// <para>[3b] Copy-log butonu (Ek A #3): yalnız proje-log modunda (log varken) görünür; <see cref="LogTextProvider"/>'ın
/// döndürdüğü TAM log metnini <see cref="ClipboardWriter"/> (retry sarmalayıcı) ile panoya yazar; başarıda ikon
/// 1400ms ✓ + "Copied" tooltip (<see cref="CopyLogFeedback"/>).</para></summary>
public partial class ConsoleHeader : UserControl
{
    public enum HeaderMode { Narrative, ProjectLog }

    // [T64] Çizilmiş ikonlar (Icons.xaml) — ikon fontu YOK.
    // [T60] Stroke kalınlığı ARTIK BURADA YAZILI DEĞİL: sözlüğün kardeş Icon.X.StrokeThickness anahtarından
    // gelir (IconPaint). Önceden buradaki 1.8/2.0 sabitleri sözlükten bağımsız ikinci bir otoriteydi.
    private const string CopyIconKey = "Icon.Copy";
    private const string CheckIconKey = "Icon.Check";

    // [v1.18.0] Back butonunun ikonu (lucide arrow-left) — 12px, hover'da butonla BİRLİKTE renk değiştirir.
    private const string BackIconKey = "Icon.Back";
    private const double BackIconSize = 12;
    private const double ButtonGap = 6; // _ds_bundle.js:104 DS button gap — ActionBar.ButtonGap ile AYNI sayı.

    private readonly CopyLogFeedback _copyFeedback = new();
    private DispatcherTimer? _copyRevertTimer;
    private Stopwatch? _copyClock;

    public ConsoleHeader()
    {
        InitializeComponent();
        BuildBackButtonContent();
        ShowNarrative(0);
    }

    /// <summary>Test/okuma için mevcut mod.</summary>
    public HeaderMode Mode { get; private set; }

    /// <summary>Back ghost butonuna tıklandığında — MainWindow bunu <c>ShowRun</c>+reseed'e bağlar.</summary>
    public event EventHandler? BackRequested;

    /// <summary>[3b] Copy-log'un kopyalayacağı TAM aktif log metnini döndürür (MainWindow: VM'in tam tamponu —
    /// render dilimi DEĞİL). null ise boş metin kopyalanır.</summary>
    public Func<string>? LogTextProvider { get; set; }

    /// <summary>[3b] Panoya yazma yolu — üretimde <see cref="ClipboardRetry.SetText"/> (CLIPBRD_E_CANT_OPEN retry).
    /// Testte fail/success enjekte edilir (gerçek panoya dokunmadan görsel toggle doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>Anlatı modu: caps "CONSOLE" etiketi + N lines; proje başlığı öğeleri + copy butonu gizli.</summary>
    public void ShowNarrative(int lineCount)
    {
        Mode = HeaderMode.Narrative;
        ConsoleLabel.Visibility = Visibility.Visible;
        ProjectLogGroup.Visibility = Visibility.Collapsed;
        ResetCopyVisual();
        CopyLogButton.Visibility = Visibility.Collapsed;
        SetLineCount(lineCount);
    }

    /// <summary>Proje-log modu: Back + proje adı (mono) + statü glyph/adı + (varsa) dependency-issue/cycle
    /// rozetleri + copy + N lines.</summary>
    /// <param name="projectName">Tam proje adı (kısaltılmaz — panel daralınca TEK kısalan öğe budur).</param>
    /// <param name="state">Motorun bu koşudaki statüsü — statü glyph'i/adı/rengi buradan türer.</param>
    /// <param name="inCycle">[v1.18.0] Bu proje bir bağımlılık döngüsünün üyesi mi — döngü rozetinin kapısı.</param>
    /// <param name="depIssues">[v1.18.0] Bu proje için tespit edilen dependency-uyarısı kök adları (tam liste —
    /// satırın "+N" kısaltmasının AKSİNE, tooltip'te HEPSİ yazılır). Boş/null ise rozet gizlenir.</param>
    /// <param name="namePrefix">Kısa-ad öneği (<see cref="Graph.GraphNode.CommonDotPrefix"/>) — dep-issue
    /// tooltip'i adları bununla kısaltır (tek otorite, kopya YASAK).</param>
    /// <param name="lineCount">Sağdaki "N lines" sayacı.</param>
    public void ShowProjectLog(string projectName, ProjectRowState state, bool inCycle,
        IReadOnlyList<string>? depIssues, string namePrefix, int lineCount)
    {
        Mode = HeaderMode.ProjectLog;
        ConsoleLabel.Visibility = Visibility.Collapsed;
        ProjectLogGroup.Visibility = Visibility.Visible;

        ProjectNameText.Text = projectName;

        StatusGlyphIcon.Status = ConsoleStatus.VisualStatus(state);

        StatusNameText.Text = ConsoleStatus.Name(state);
        StatusNameText.SetResourceReference(ForegroundProperty, ConsoleStatus.BrushKey(state));

        bool hasDepIssue = depIssues is { Count: > 0 };
        DepIssueBadge.Visibility = hasDepIssue ? Visibility.Visible : Visibility.Collapsed;
        DepIssueBadge.ToolTip = hasDepIssue ? RowWarning.DepIssueDetail(depIssues!, namePrefix) : null;

        CycleBadge.Visibility = inCycle ? Visibility.Visible : Visibility.Collapsed;
        CycleBadge.ToolTip = inCycle ? RowWarning.InCycle : null;

        // Copy log yalnız gerçekten log varken (Ek A #3 / prototip: selSt.log.length > 0). Görünürlük artık
        // TEK yerde — SetLineCount, proje-log modunda lineCount>0'a göre karar verir (M-3 ile satır geldikçe tazelenir).
        ResetCopyVisual();
        SetLineCount(lineCount);
    }

    /// <summary>Sağdaki mono "N lines" sayacı — TAM tampon uzunluğu (render dilimi DEĞİL, Ek A #23). [3b M-3]
    /// Proje-log modunda copy-log görünürlüğü de burada (satır sayısıyla birlikte) yeniden değerlendirilir:
    /// seçim anında boş olan bir log akış başlayınca (~200ms sayaç tazelemesi) copy butonu görünür olur — yalnız
    /// <c>ShowProjectLog</c>'ta bir kez değil.</summary>
    public void SetLineCount(int lineCount)
    {
        // [M-4] Global Constraint: kullanıcıya gösterilen sayı biçimlemesi InvariantCulture (locale'e göre
        // basamak gruplama/rakam değişmesin).
        //
        // DEĞİŞMEDİYSE YAZILMAZ: bu metot 200ms'lik tick'ten KOŞULSUZ çağrılır, yani boşta da saniyede beş kez.
        // Aynı metni yeniden atamak taze bir string ayırır ve TextBlock'u ölçüm/çizim için geçersiz kılar —
        // uygulama hiç iş yokken bile boş kareye inemezdi.
        string text = string.Format(CultureInfo.InvariantCulture, "{0} lines", lineCount);
        if (!string.Equals(LinesText.Text, text, StringComparison.Ordinal)) LinesText.Text = text;
        if (Mode == HeaderMode.ProjectLog)
            CopyLogButton.Visibility = lineCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- kuruluş (ctor)

    /// <summary>[v1.18.0] Back butonunun içeriği (Icon.Back + "Back") kod-tarafı kurulur: ikon butonun
    /// <b>animasyonlu</b> Foreground'unu İZLER (<see cref="IconVisual.BoundToForeground"/>, ActionBar'ın Sync/
    /// bakım ikonlarıyla AYNI desen) — ghost buton hover'da text-secondary→text-primary'ye geçerken ikon da
    /// birlikte geçer (prototip: <c>stroke="currentColor"</c>).</summary>
    private void BuildBackButtonContent()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(IconVisual.BoundToForeground(BackButton, BackIconKey, BackIconSize));
        content.Children.Add(new TextBlock
        {
            Text = "Back",
            Margin = new Thickness(ButtonGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        BackButton.Content = content;
    }

    // ---------------------------------------------------------------- copy log (Ek A #3)

    private void OnCopyLogClick(object sender, RoutedEventArgs e) => CopyLog();

    /// <summary>[3b] Aktif logu (<see cref="LogTextProvider"/>) satırlar '\n' ile panoya kopyalar (retry
    /// sarmalayıcıyla). Başarıda ✓ + "Copied" 1400ms görünür, sonra normale döner.</summary>
    public void CopyLog()
    {
        string text = LogTextProvider?.Invoke() ?? "";
        if (!ClipboardWriter(text)) return; // kalıcı pano kilidi — sessizce başarısız (UI çökmez)

        _copyClock = Stopwatch.StartNew();
        _copyFeedback.MarkCopied(TimeSpan.Zero);
        ShowCopiedVisual();

        _copyRevertTimer?.Stop();
        _copyRevertTimer ??= CreateRevertTimer();
        _copyRevertTimer.Start();
    }

    private DispatcherTimer CreateRevertTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) =>
        {
            if (_copyClock is not null && _copyFeedback.ShouldRevert(_copyClock.Elapsed))
                ResetCopyVisual();
        };
        return timer;
    }

    private void ShowCopiedVisual() => SetCopyIcon(CheckIconKey, "Copied", "Brush.StatusSuccessText");

    private void ResetCopyVisual()
    {
        _copyRevertTimer?.Stop();
        _copyClock?.Stop();
        _copyClock = null;
        _copyFeedback.Revert();
        // [A13/T5] Tooltip metni de UIA adıyla AYNI sabitten gelir (kopya YASAK) — ad SABİTTİR, yalnız tooltip
        // başarılı kopyada geçici olarak "Copied" olur.
        SetCopyIcon(CopyIconKey, AccessibilityNames.CopyLog, "Brush.TextSecondary");
    }

    /// <summary>Copy-log butonunun görselini (ikon geometrisi + boya + tooltip + renk) tek yerden sürer.
    /// [T60] Geometri VE boya semantiği (kontur/dolgu + kalınlık) <see cref="IconPaint"/> üzerinden sözlükten
    /// gelir: sözlük merge edilmemişse sessizce çözümsüz kalır (<c>SetResourceReference</c> deseni).
    ///
    /// <para>[v1.18.0] Buton artık <c>Ds.IconButton</c> stilini taşır (hover'da zemin/Foreground'u
    /// <c>DsTransition.AnimatedForeground</c> ile sürer) ama bu metot Foreground'a doğrudan yazmaya devam eder
    /// — <see cref="Views.AboutDialog"/>'un Copy diagnostics butonuyla AYNI kanıtlanmış desen (kopya YASAK):
    /// kalıcı geri bildirim (yeşil ✓) hover'ın 120ms geçişinden daha güçlü bir sinyaldir; hover bu pencerede
    /// üstüne binerse (nadir), DsTransition'ın kendi tetikleyicisi geri devralır.</para></summary>
    private void SetCopyIcon(string iconKey, string tooltip, string foregroundKey)
    {
        IconPaint.Apply(CopyLogGlyph, this, iconKey, foregroundKey);
        CopyLogButton.ToolTip = tooltip;
        CopyLogButton.SetResourceReference(ForegroundProperty, foregroundKey);
    }

    /// <summary>Test için: kopyalandı görsel durumunda mı (✓ ikonu + "Copied"). Gerçek <c>Path.Data</c>
    /// okunur — Icons.xaml bu kontrolün kaynak kapsamında değilse (merge yok) her iki durumda da
    /// <c>null</c> olurdu, bu yüzden çözülemeyen ikon açıkça "kopyalanmadı" sayılır.</summary>
    internal bool IsShowingCopied
        => TryFindResource(CheckIconKey) is Geometry check && ReferenceEquals(CopyLogGlyph.Data, check);
}
