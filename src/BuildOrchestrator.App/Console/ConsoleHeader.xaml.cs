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
/// sağdaki "N lines" sayacını günceller. Statü rengi token ANAHTARIndan (<see cref="StatusGlyph.BrushKeyFor"/>)
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

    private readonly CopyLogFeedback _copyFeedback = new();
    private DispatcherTimer? _copyRevertTimer;
    private Stopwatch? _copyClock;

    public ConsoleHeader()
    {
        InitializeComponent();
        BuildBackButtonContent();
        ShowNarrative(0);
        // [v1.18.0 review R1 finding 1] Panel canlı yeniden boyutlanırken (splitter sürüklenirken) de proje adı
        // payını tazeler — yalnız proje-log modundayken anlamlıdır, ShowNarrative'de ProjectLogGroup zaten
        // Collapsed'tır ve ApplyProjectNameShrink kendi kapısında (Mode kontrolü) no-op döner.
        SizeChanged += (_, _) => ApplyProjectNameShrink();
        // [Final review M-1] Sağ blok (Copy log + "N lines") genişliğini değiştirdiğinde sol bloğun payı da
        // değişir — Copy log görünürlük geçişi de, sayaç metninin genişlemesi de (999 → 1000). TEK tetik budur;
        // SetLineCount ayrıca çağırmaz. Boşta tick metni değiştirmediği için bu olay boşta ateşlenmez.
        RightBlock.SizeChanged += (_, _) => ApplyProjectNameShrink();
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
    /// <param name="row">Seçili satır — tam adı (kısaltılmaz; panel daralınca TEK kısalan öğe budur), statü
    /// glyph'i (<see cref="ProjectRowViewModel.Status"/>), statü adı/rengi (motor durumu), döngü üyeliği ve
    /// dependency-issue listesi + kısa-ad öneği buradan okunur (bkz. <see cref="ApplyStatus"/>).</param>
    /// <param name="lineCount">Sağdaki "N lines" sayacı.</param>
    public void ShowProjectLog(ProjectRowViewModel row, int lineCount)
    {
        Mode = HeaderMode.ProjectLog;
        ConsoleLabel.Visibility = Visibility.Collapsed;
        ProjectLogGroup.Visibility = Visibility.Visible;

        ProjectNameText.Text = row.Name;
        ApplyStatus(row);

        // Copy log yalnız gerçekten log varken (Ek A #3 / prototip: selSt.log.length > 0). Görünürlük artık
        // TEK yerde — SetLineCount, proje-log modunda lineCount>0'a göre karar verir (M-3 ile satır geldikçe tazelenir).
        ResetCopyVisual();
        SetLineCount(lineCount);
        ApplyProjectNameShrink();
    }

    /// <summary>[v1.18.0 review R1 finding 2] Statü glyph'i/adı + rozetleri TEK BAŞINA tazeler — proje adını,
    /// modu ya da copy-log görselini SIFIRLAMAZ. <see cref="ShowProjectLog"/> yalnız SEÇİM değiştiğinde
    /// çağrılır; seçili proje AYNI kalırken kendi statüsü değiştiğinde (ör. Started→Succeeded, ya da
    /// dependency-issue/cycle üyeliği geldiğinde) MainWindow bunu çağırır — aksi halde başlık bir kez
    /// kurulduktan sonra donuyordu (spinner sonsuza dek dönerdi, rozetler bayatlardı).</summary>
    public void RefreshStatus(ProjectRowViewModel row)
    {
        if (Mode != HeaderMode.ProjectLog) return; // anlatıdayken görünmez bir başlığı boşuna tazeleme
        ApplyStatus(row);
        ApplyProjectNameShrink(); // rozetlerin görünürlüğü değişmiş olabilir — sol bloğun payı da değişir
    }

    /// <summary>[Final review I-2 · kullanıcı kararı] Glyph, yazı VE renk satırın KENDİ
    /// <see cref="ProjectRowViewModel.Status"/>'unu okur — satır ve graf da onu okur; başlık ikinci bir
    /// durum→glyph/ad/renk eşlemesi KURMAZ (Started ama derlenmeyen döngü üyesi satırda Queued ise başlıkta da
    /// Queued'dır — hem ikon hem yazı). Ad/renk tablosu <see cref="StatusGlyph.RunLabelFor"/>/
    /// <see cref="StatusGlyph.BrushKeyFor"/>'dur — glyph'in KENDİ tablosu; ikinci bir kopya AÇILMAZ.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Yazı ve rengi eskiden <c>ConsoleStatus.Name/BrushKey(row.State)</c> ile
    /// motorun dar <c>ProjectRowState</c> sözlüğünden geliyordu: bir döngü grubunda sırası kendisinde olmayan
    /// Started üye satırda/ikonda Queued görünürken yazı hâlâ "Building" diyordu (State hâlâ Started) — ikon ile
    /// yazı ayrışıyordu. Kullanıcı kararıyla üçü de tek kaynaktan okunur; kullanılmaz kalan <c>ConsoleStatus</c>
    /// sınıfı silindi.</para></summary>
    private void ApplyStatus(ProjectRowViewModel row)
    {
        // [design v1.20.0 §1.4] Başlık bir RUN-STORY yüzeyidir: koşu sonucunu gösterir (atlanan proje — ile).
        // Koşu statüsü → görsel durum eşlemesi TEK yerdedir (VisualStatuses.OfRun) — burada kurulmaz.
        var glyph = VisualStatuses.OfRun(row.Status);
        StatusGlyphIcon.Status = glyph;

        StatusNameText.Text = StatusGlyph.RunLabelFor(row.Status); // run-story: koşunun sonucu ("Skipped" burada kalır)
        StatusNameText.SetResourceReference(ForegroundProperty, StatusGlyph.BrushKeyFor(glyph));

        var depIssues = row.DepIssues;
        bool hasDepIssue = depIssues is { Count: > 0 };
        DepIssueBadge.Visibility = hasDepIssue ? Visibility.Visible : Visibility.Collapsed;
        // [final review M-6] Tooltip nesneleri XAML'dedir (AppTooltip.Side="Bottom"); burada yalnız içerik yazılır.
        DepIssueTooltip.Content = hasDepIssue ? RowWarning.DepIssueDetail(depIssues!, row.NamePrefix) : null;

        CycleBadge.Visibility = row.InCycle ? Visibility.Visible : Visibility.Collapsed;
        CycleTooltip.Content = row.InCycle ? RowWarning.InCycle : null;
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
        if (Mode != HeaderMode.ProjectLog) return;

        bool shouldShowCopy = lineCount > 0;
        if ((CopyLogButton.Visibility == Visibility.Visible) == shouldShowCopy) return; // DEĞİŞMEDİYSE YAZILMAZ
        // Sağ bloğun genişliği değişir → ad payı RightBlock.SizeChanged üzerinden tazelenir (ctor).
        CopyLogButton.Visibility = shouldShowCopy ? Visibility.Visible : Visibility.Collapsed;
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
            // [review R1 finding 3 — kopya YASAK] Görünür etiket UIA adıyla AYNI sabitten gelir
            // (AboutDialog.CopyLabel/CopyDiagnostics ile aynı desen) — "Back" iki yerde ayrı ayrı yazılmaz.
            Text = AccessibilityNames.BackButton,
            Margin = new Thickness(IconVisual.LabelGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        BackButton.Content = content;
    }

    /// <summary>[v1.18.0 review R1 finding 1] Proje adı panel daralınca kısalan TEK öğedir, ama büyümemelidir:
    /// prototipte (<c>BuildApp.jsx:2612</c>) ad `white-space:nowrap` bir <c>span</c>'dır — kısalır, asla
    /// büyümez; Back/glyph/statü/rozetler her zaman adın HEMEN sağındadır, aralarında boşluk yoktur. WPF
    /// Grid'in <c>"*"</c> sütunu bunu VEREMEZ: kalan alanın TAMAMINI (mevcut siblings ihtiyacından bağımsız)
    /// o sütuna verir, sonraki Auto sütunlar da bu sütunun TAM payının bitiminden başlar — geniş bir panelde
    /// kısa bir adla bile glyph/statü/rozetler adın metninden çok sonra, büyük bir boşlukla başlıyordu
    /// (review R1 bulgu 1). Bu yüzden ad sütunu Auto'dur (ConsoleHeader.xaml) ve gerçek "kısalma" burada,
    /// <see cref="TextBlock.MaxWidth"/>'i ELDE hesaplayarak sağlanır: mevcut kardeşlerin (Back/glyph/statü/
    /// görünür rozetler) GERÇEK genişliği + kendi marjları, sol bloğun TOPLAM payından düşülür, kalan ad'a
    /// verilir.
    ///
    /// <para><b>[review R1 fix-1 · ölçülen WPF gerçeği] Sol bloğun payı <see cref="ProjectLogGroup"/>'un
    /// KENDİ <c>ActualWidth</c>'inden OKUNAMAZ.</b> <c>ProjectLogGroup</c>'un ALTI sütunu da Auto'dur; ad
    /// kısıtlanmadan ÖNCE (ilk çağrı, ya da <see cref="ClearValue"/>'dan hemen sonra) bu Grid'in DOĞAL
    /// (muhtemelen taşan) <c>DesiredSize</c>'ı devasadır — ve WPF'in Arrange'i <c>Stretch</c> bir öğeyi ASLA
    /// DesiredSize'ının ALTINA küçültmez (yalnız büyütür): <c>ActualWidth</c> bu yüzden verilen hücrenin GERÇEK
    /// payını değil, taşan doğal ihtiyacı yansıtır (ölçüldü: 260px genişliğinde bir başlıkta
    /// <c>ProjectLogGroup.ActualWidth</c> ≈ 692px çıktı — verilen pay yalnızca ≈226px'ti). Güvenilir tek ölçü,
    /// içinde taşan içerik OLMAYAN <see cref="RootGrid"/>'in kendisidir: sol bloğun payı
    /// <c>RootGrid.ActualWidth − RightBlock.ActualWidth</c>'tir (10px'lik yatay Margin RootGrid'in KENDİSİNDEN
    /// okunur — literal bir "10" ikinci kaynak AÇILMAZ).</para>
    ///
    /// <para><b>Neden <see cref="UIElement.UpdateLayout"/> gerekir:</b> bu çağrılan an (Visibility/Text
    /// değişiminin hemen ardından) kardeşlerin <see cref="FrameworkElement.ActualWidth"/>'i henüz BAYATTIR —
    /// WPF bir Measure/Arrange geçişi olmadan onu güncellemez. <c>ConsoleView</c>'ın kaydırma-pin mantığı da
    /// AYNI gerekçeyle <c>UpdateLayout()</c> çağırır (kopya değil, aynı zorunlu idiom).</para></summary>
    private void ApplyProjectNameShrink()
    {
        if (Mode != HeaderMode.ProjectLog) return;

        ProjectNameText.ClearValue(MaxWidthProperty); // önceki kısıtlama doğal genişliği maskelemesin
        UpdateLayout();

        double leftBlockWidth = RootGrid.ActualWidth - RightBlock.ActualWidth;
        if (leftBlockWidth <= 0) return; // henüz hiç yerleşmedi (ör. gerçek pencere olmadan çağrılan testler)

        double used = OuterWidth(BackButton) + OuterWidth(StatusGlyphIcon) + OuterWidth(StatusNameText)
            + OuterWidth(DepIssueBadge) + OuterWidth(CycleBadge)
            + ProjectNameText.Margin.Left + ProjectNameText.Margin.Right;
        ProjectNameText.MaxWidth = Math.Max(0, leftBlockWidth - used);
    }

    /// <summary>Bir kardeşin Grid Auto sütununda GERÇEKTEN kapladığı yer (kendi marjı dahil) — görünür
    /// değilse (Collapsed) 0, çünkü Auto sütun o zaman hiç yer ayırmaz.</summary>
    private static double OuterWidth(FrameworkElement element) => element.Visibility == Visibility.Visible
        ? element.ActualWidth + element.Margin.Left + element.Margin.Right
        : 0;

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
