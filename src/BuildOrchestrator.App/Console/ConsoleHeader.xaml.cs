using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Console;

/// <summary>[T56/3a+3b → v1.18.0 §9] Konsol panel başlığının iki modu. Kod-tarafı sürülür (DP/binding şişkinliği yerine
/// küçük, test edilebilir yüzey): <see cref="ShowNarrative"/> / <see cref="ShowProjectLog"/> modu değiştirir,
/// <see cref="SetLineCount"/> sağdaki "N lines" sayacını günceller.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-25]</b> Proje-log modu yalnız proje adını gösterir. Eskiden adın
/// solunda Back, sağında statü glyph'i + koşu statüsü sözcüğü + dependency issue / dependency cycle rozetleri dururdu ve
/// başlık seçili satırı canlı izlerdi (<c>MainWindow.TrackHeaderRow</c> → <c>RefreshStatus</c>). Kullanıcı proje sayfasında
/// bunlara gerek görmedi; durum, uyarı ve döngü bilgisi listede ve grafta kalır. Başlık bu yüzden satırı hiç okumaz: adı
/// alır, o kadar. Eski iddialar ve gerekçe: <c>ConsoleHeaderDesignTests</c>'in sınıf özeti.</para>
///
/// <para>[3b] Copy-log butonu (Ek A #3): yalnız proje-log modunda (log varken) görünür; <see cref="LogTextProvider"/>'ın
/// döndürdüğü TAM log metnini <see cref="ClipboardWriter"/> (retry sarmalayıcı) ile panoya yazar; başarıda ikon
/// 1400ms ✓ + "Copied" tooltip (<see cref="CopyLogFeedback"/>).</para>
///
/// <para>[idle-memory · dalga 2 · spec §6.5] Anlatı kompakt yazılırken sayacın yanında <c>compact</c> etiketi
/// (<see cref="SetCompact"/>) — yalnız anlatı modunda ve workspace varken.</para></summary>
public partial class ConsoleHeader : UserControl
{
    /// <summary>UIA rolü — gerekçe ve ölçüm <see cref="UserControlRolePeer"/>'de.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new UserControlRolePeer(this, AutomationControlType.Header);

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

    /// <summary>[3b] Copy-log'un kopyalayacağı TAM aktif log metnini döndürür (MainWindow: VM'in tam tamponu —
    /// render dilimi DEĞİL). null ise boş metin kopyalanır.</summary>
    public Func<string>? LogTextProvider { get; set; }

    /// <summary>[3b] Panoya yazma yolu — üretimde <see cref="ClipboardRetry.SetText"/> (CLIPBRD_E_CANT_OPEN retry).
    /// Testte fail/success enjekte edilir (gerçek panoya dokunmadan görsel toggle doğrulanır — D8).</summary>
    public Func<string, bool> ClipboardWriter { get; set; } = ClipboardRetry.SetText;

    /// <summary>Anlatı modu: caps "CONSOLE" etiketi + N lines (+ anlatı kompakt yazılıyorsa <c>compact</c>); proje adı ve
    /// copy butonu gizli.</summary>
    public void ShowNarrative(int lineCount)
    {
        Mode = HeaderMode.Narrative;
        ConsoleLabel.Visibility = Visibility.Visible;
        ProjectNameText.Visibility = Visibility.Collapsed;
        ResetCopyVisual();
        CopyLogButton.Visibility = Visibility.Collapsed;
        ApplyCompactTag();
        SetLineCount(lineCount);
    }

    /// <summary>Proje-log modu: proje adı (mono, panel daralınca kısalan tek öğe) + copy + N lines.</summary>
    /// <param name="projectName">Seçili projenin TAM adı — kısaltılmaz; sığmazsa yerleşim kırpar.</param>
    /// <param name="lineCount">Sağdaki "N lines" sayacı.</param>
    public void ShowProjectLog(string projectName, int lineCount)
    {
        Mode = HeaderMode.ProjectLog;
        ConsoleLabel.Visibility = Visibility.Collapsed;
        ProjectNameText.Text = projectName;
        ProjectNameText.Visibility = Visibility.Visible;
        ApplyCompactTag(); // proje sayfası her zaman tamdır: etiket düşer

        // Copy log yalnız gerçekten log varken (Ek A #3 / prototip: selSt.log.length > 0). Görünürlük TEK yerde —
        // SetLineCount, proje-log modunda lineCount>0'a göre karar verir (M-3 ile satır geldikçe tazelenir).
        ResetCopyVisual();
        SetLineCount(lineCount);
    }

    /// <summary>[design v1.8.0 §3.1 · prototip panel başlığı] Workspace yokken <c>N lines</c> sayacı gizlidir — konsol
    /// yalnız workspace bekler. Kabuk bunu <c>RunViewModel.HasWorkspace</c>'ten sürer (<c>ShellRoot.SetHasWorkspace</c>). Sayaçla
    /// birlikte <c>compact</c> etiketi de gider (<see cref="ApplyCompactTag"/>).</summary>
    public void SetHasWorkspace(bool hasWorkspace)
    {
        _hasWorkspace = hasWorkspace;
        LinesText.Visibility = hasWorkspace ? Visibility.Visible : Visibility.Collapsed;
        ApplyCompactTag();
    }

    /// <summary>Workspace var mı — sayacın görünürlüğüyle aynı başlangıç (XAML'de sayaç görünür).</summary>
    private bool _hasWorkspace = true;

    /// <summary>Konsol bundan sonra kompakt mı yazıyor (<see cref="SetCompact"/>).</summary>
    private bool _compact;

    /// <summary>[idle-memory · dalga 2 · spec §6.5 · karar 204] Konsolun proje çıktısını BUNDAN SONRA yazacağı kip kompakt mı — kabuk bunu
    /// <c>RunViewModel.CompactConsoleInEffect</c>'ten sürer: ayarın kendisi değil, etkin kip. İşlem uçuştayken o işlemin başında
    /// yakaladığı kiptir; uçuşta işlem yokken ayarın kendisidir (boştaki Save'de hemen, işlem sürerken yapılan Save'de o işlem bitince).
    /// Ekrandaki metnin hangi kiple yazıldığını söylemez: özet satırları kendini anlatır.</summary>
    public void SetCompact(bool compact)
    {
        _compact = compact;
        ApplyCompactTag();
    }

    /// <summary>[karar 12 · preflight C8] <c>compact</c> etiketi yalnız anlatı modunda ve workspace varken görünür: "N lines" orada
    /// anlatının (kompakt) satırlarını sayar. Proje sayfası her zaman tamdır — orada etiket yalan olurdu; workspace yokken sayacın
    /// kendisi de yoktur.</summary>
    private void ApplyCompactTag() =>
        CompactText.Visibility = _compact && _hasWorkspace && Mode == HeaderMode.Narrative
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>[perf Faz A · A6 test yüzeyi] <see cref="SetLineCount"/> çağrı sayacı — gizliyken 200 ms'lik tikin sayacı
    /// yazmadığını, dönüşte TEK kez yenilediğini pinler.</summary>
    internal int SetLineCountCalls { get; private set; }

    /// <summary>Sağdaki mono "N lines" sayacı — TAM tampon uzunluğu (render dilimi DEĞİL, Ek A #23). [3b M-3]
    /// Proje-log modunda copy-log görünürlüğü de burada (satır sayısıyla birlikte) yeniden değerlendirilir:
    /// seçim anında boş olan bir log akış başlayınca (~200ms sayaç tazelemesi) copy butonu görünür olur — yalnız
    /// <c>ShowProjectLog</c>'ta bir kez değil.</summary>
    public void SetLineCount(int lineCount)
    {
        SetLineCountCalls++;
        // [M-4] Global Constraint: kullanıcıya gösterilen sayı biçimlemesi InvariantCulture (locale'e göre
        // basamak gruplama/rakam değişmesin).
        //
        // DEĞİŞMEDİYSE YAZILMAZ: bu metot 200ms'lik tick'ten KOŞULSUZ çağrılır, yani boşta da saniyede beş kez.
        // Aynı metni yeniden atamak taze bir string ayırır ve TextBlock'u ölçüm/çizim için geçersiz kılar —
        // uygulama hiç iş yokken bile boş kareye inemezdi.
        string text = string.Format(CultureInfo.InvariantCulture, "{0} lines", lineCount);
        if (!string.Equals(LinesText.Text, text, StringComparison.Ordinal)) LinesText.Text = text;
        if (Mode != HeaderMode.ProjectLog) return;

        bool shouldShowCopy = lineCount > 0;
        if ((CopyLogButton.Visibility == Visibility.Visible) == shouldShowCopy) return; // DEĞİŞMEDİYSE YAZILMAZ
        CopyLogButton.Visibility = shouldShowCopy ? Visibility.Visible : Visibility.Collapsed;
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
