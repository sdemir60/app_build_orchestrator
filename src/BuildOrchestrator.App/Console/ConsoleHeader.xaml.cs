using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>[T56/3a+3b] Konsol panel başlığının iki modu (design-v1 §2.5). Kod-tarafı sürülür (DP/binding şişkinliği
/// yerine küçük, test edilebilir yüzey): <see cref="ShowNarrative"/> / <see cref="ShowProjectLog"/> modu değiştirir,
/// <see cref="SetLineCount"/> sağdaki "N lines" sayacını günceller. Statü rengi token ANAHTARIndan
/// (<see cref="ConsoleStatus.BrushKey"/>) SetResourceReference ile canlı çözülür (hardcode YASAK).
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

    private readonly CopyLogFeedback _copyFeedback = new();
    private DispatcherTimer? _copyRevertTimer;
    private Stopwatch? _copyClock;

    public ConsoleHeader()
    {
        InitializeComponent();
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
        BackButton.Visibility = Visibility.Collapsed;
        ProjectNameText.Visibility = Visibility.Collapsed;
        StatusGlyphText.Visibility = Visibility.Collapsed;
        StatusNameText.Visibility = Visibility.Collapsed;
        DepIssueBadge.Visibility = Visibility.Collapsed;
        ResetCopyVisual();
        CopyLogButton.Visibility = Visibility.Collapsed;
        SetLineCount(lineCount);
    }

    /// <summary>Proje-log modu: ← Back + proje adı (mono) + statü glyph/adı + (varsa) ▲ dependency issue + copy + N lines.</summary>
    public void ShowProjectLog(string projectName, ProjectRowState state, bool hasDepIssue, int lineCount)
    {
        Mode = HeaderMode.ProjectLog;
        ConsoleLabel.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;

        ProjectNameText.Text = projectName;
        ProjectNameText.Visibility = Visibility.Visible;

        StatusGlyphText.Text = ConsoleStatus.Glyph(state);
        StatusGlyphText.SetResourceReference(ForegroundProperty, ConsoleStatus.BrushKey(state));
        StatusGlyphText.Visibility = Visibility.Visible;

        StatusNameText.Text = ConsoleStatus.Name(state);
        StatusNameText.SetResourceReference(ForegroundProperty, ConsoleStatus.BrushKey(state));
        StatusNameText.Visibility = Visibility.Visible;

        DepIssueBadge.Visibility = hasDepIssue ? Visibility.Visible : Visibility.Collapsed;
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
        LinesText.Text = string.Format(CultureInfo.InvariantCulture, "{0} lines", lineCount);
        if (Mode == HeaderMode.ProjectLog)
            CopyLogButton.Visibility = lineCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

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
        SetCopyIcon(CopyIconKey, "Copy log", "Brush.TextSecondary");
    }

    /// <summary>Copy-log butonunun görselini (ikon geometrisi + boya + tooltip + renk) tek yerden sürer.
    /// [T60] Geometri VE boya semantiği (kontur/dolgu + kalınlık) <see cref="IconPaint"/> üzerinden sözlükten
    /// gelir: sözlük merge edilmemişse sessizce çözümsüz kalır (<c>SetResourceReference</c> deseni).</summary>
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
