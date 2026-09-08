using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Bir popover AÇIKKEN tetikleyicisine basmak onu <b>KAPATIR</b> — tasarımın kuralı budur: chip'ler
/// <c>setBranchPop(!branchPop)</c> ile DEĞİŞTİRİR (BuildApp.jsx:2399, :2404, :2420), satır menüsü de erken
/// çıkışla aynı şeyi söyler: <c>if (menuOpen) { setMenu(null); return; }</c> (BuildApp.jsx:657).
///
/// <para><b>WPF'in araya giren davranışı.</b> <c>StaysOpen="False"</c> bir <see cref="Popup"/>, dışına düşen
/// bir fare basışında KENDİ kapanma yolundan geçer (<c>IsOpen=false</c>) ve iki-yönlü bağ üzerinden
/// tetikleyicinin <c>IsChecked</c>'ını da düşürür. Basış oradan tetikleyiciye ULAŞIR ve onu yeniden
/// işaretler: popover kapanır ve AYNI jestte tekrar açılır. Yani jestin iki yarısı birbirini iptal eder ve
/// kullanıcı hiç kapatamaz.</para>
///
/// <para><b>Kapı burada, tek yerde.</b> Uygulamada beş popover aynı deseni kullanır (branch · worktree ·
/// Build chevron'u · satır ⋯ menüsü · VS seçici); her biri kendi kapısını yazsaydı beş kopya olurdu
/// (kopya YASAK, CLAUDE.md). Kapı iki katmanlıdır ve katmanlar BİRBİRİNİ DIŞLAR
/// (<see cref="GestureGuard.Consume"/> damgayı tüketir):</para>
/// <list type="number">
///   <item><b>Önizleme yutma</b> — jestin tünelleyen yarısında karar verilir. Handled bir
///     <see cref="UIElement.PreviewMouseLeftButtonDown"/>'da <see cref="ButtonBase"/> fareyi hiç yakalamaz,
///     dolayısıyla bırakmada <c>Click</c> de doğmaz: popover kapalı KALIR, ekranda hiçbir çakma olmaz. Bu
///     katman düz bir <c>Button</c> için de çalışır (VS seçicisini açan <c>Click</c> handler'ı hiç koşmaz).</item>
///   <item><b>Geri alma</b> — basış önizleme yolundan GELMEDİYSE (klavye, otomasyon, WPF'in girdi yolunun
///     başka bir varyantı) ve tetikleyici bir <see cref="ToggleButton"/> ise, işaretlenme aynı pencere içinde
///     geri alınır. Görsel sonuç aynıdır; tek fark bir yerleşim geçişi boyunca sürmesidir.</item>
/// </list>
///
/// <para><b>Neden ZAMAN penceresi.</b> Kapının kapsaması gereken şey TEK BİR JESTTİR. Bayrağı yalnız bir
/// sonraki basışta temizleyen bir çözüm, popover başka bir nedenle kapandığında (Esc, başka bir yere tık)
/// kullanıcıyı bir tık boyunca sağır bırakırdı — o tık popover'ı açmalıdır. Pencere bu yüzden kısadır ve
/// zaman kaynağı (<see cref="Now"/>) enjekte edilebilir: testler pencereyi deterministik olarak geçebilir.</para>
/// </summary>
internal static class PopoverToggle
{
    /// <summary>Tek bir fare jestinin kapanma ile basış arasında geçirebileceği en uzun süre (ms). Bir jest
    /// değil, ARDIŞIK iki tık bunun dışında kalır.</summary>
    internal const double GuardMs = 250;

    /// <summary>Üretimin zaman kaynağı — duvar saati değil, monoton bir sayaç (saat değişimlerinden etkilenmez).</summary>
    internal static readonly Func<double> DefaultClock = () => Environment.TickCount64;

    /// <summary>[test seam] Zaman kaynağı. Testler pencereyi geçmek için değiştirir ve geri koyar.</summary>
    internal static Func<double> Now { get; set; } = DefaultClock;

    /// <summary>
    /// <paramref name="trigger"/>'a basmanın AÇIK <paramref name="popup"/>'ı kapalı bırakmasını sağlar.
    /// Çağıran, ikisini birbirine bağlayan kablajın (iki-yönlü <c>IsChecked</c>/<c>IsOpen</c> ya da bir
    /// <c>Click</c> handler'ı) sahibidir; bu metot ona dokunmaz, yalnız jestin ikinci yarısını keser.
    /// </summary>
    public static void Bind(ButtonBase trigger, Popup popup)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(popup);

        var guard = new GestureGuard();
        trigger.SetValue(GuardProperty, guard);
        // Kapanışın sinyali <see cref="Popup.IsOpen"/>'ın DEĞİŞİMİdir, <c>Popup.Closed</c> olayı DEĞİL:
        // Closed ASENKRON ateşlenir (ÖLÇÜLDÜ — popup penceresi bir dispatcher turu sonra yıkılır), yani
        // damga jesti KAÇIRIR ve basış damgadan önce gelir. IsOpen ise WPF'in capture yolu tarafından
        // basış işlenirken SENKRON düşürülür — kapının dayanabileceği tek an budur.
        BindingOperations.SetBinding(trigger, PopupOpenProperty,
            new Binding(nameof(Popup.IsOpen)) { Source = popup, Mode = BindingMode.OneWay });

        trigger.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (guard.Consume(Now())) e.Handled = true;
        };
        if (trigger is ToggleButton toggle)
            toggle.Checked += (_, _) =>
            {
                if (guard.Consume(Now())) toggle.IsChecked = false;
            };
    }

    /// <summary>Tetikleyicinin kendi jest damgası. Attached property, çünkü tetikleyiciler paylaşılan
    /// şablon/XAML öğeleridir; alan taşıyacak bir sahipleri yok.</summary>
    private static readonly DependencyProperty GuardProperty = DependencyProperty.RegisterAttached(
        "Guard", typeof(GestureGuard), typeof(PopoverToggle));

    /// <summary>
    /// Tetikleyiciye bağlanan popover'ın <c>IsOpen</c>'ının aynası. Değeri kimse OKUMAZ — varlık nedeni
    /// değişim callback'idir: bir <see cref="Binding"/> kaynaktaki değişimi SENKRON buraya taşır ve damga
    /// tam o anda vurulur.
    /// </summary>
    private static readonly DependencyProperty PopupOpenProperty = DependencyProperty.RegisterAttached(
        "PopupOpen", typeof(bool), typeof(PopoverToggle), new PropertyMetadata(false, OnPopupOpenChanged));

    private static void OnPopupOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) return;                       // yalnız KAPANIŞ bir jestin yarısı olabilir
        if (d.GetValue(GuardProperty) is GestureGuard guard) guard.Stamp(Now());
    }

    /// <summary>Kapanışın damgası. Damga bir kez TÜKETİLİR: aynı jest iki kez kesilmez ve iki katman
    /// birbirini dışlar.</summary>
    private sealed class GestureGuard
    {
        private double _closedAt = double.NegativeInfinity;

        public void Stamp(double now) => _closedAt = now;

        public bool Consume(double now)
        {
            if (now - _closedAt >= GuardMs) return false;
            _closedAt = double.NegativeInfinity;
            return true;
        }
    }
}
