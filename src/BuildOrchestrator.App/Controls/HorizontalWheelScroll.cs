using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Yatay kaydırma girdisi — dikeyin (<see cref="ScrollAnimator"/>, <see cref="BottomAnchorBehavior"/>) yanındaki
/// eksik yarım.
///
/// <para><b>Neden elle yapılıyor:</b> WPF'in girdi yığını yalnız <c>WM_MOUSEWHEEL</c>'i bir routed event'e
/// (<c>MouseWheel</c>) çevirir; <c>WM_MOUSEHWHEEL</c> HİÇ dağıtılmaz ve <c>MouseWheelEventArgs</c>'ın yatay bir
/// karşılığı yoktur. Ölçülen sonuç: konsol <c>WordWrap=False</c> ile çalıştığından uzun MSBuild satırları sağa
/// taşar, ama ne precision touchpad'in iki parmakla yatay kaydırması ne de yatay tekerlekli bir farenin sinyali
/// hiçbir elemente ulaşırdı — tek yol yatay barı SÜRÜKLEMEKTİ.</para>
///
/// <para><b>Kapsam:</b> mesaj bir elemente değil PENCEREYE gelir; "hangi panel" sorusunu uygulama cevaplar.
/// <see cref="Enable"/> edilen panel, mesajın EKRAN koordinatını kendi sınırlarında test eder ve yalnız imleç
/// kendi üzerindeyken kaydırır. Yatay taşması olmayan paneller (yatay barı kapalı listeler) <see cref="Enable"/>
/// EDİLMEZ: onlarda kaydırılacak bir şey yoktur.</para>
///
/// <para><b>Neden bir Dispatcher turu erteleniyor (ÖLÇÜLDÜ):</b> kaydırmayı WndProc'un İÇİNDEN istemek sessizce
/// kaybolur — viewer isteği bir sonraki layout turuna kuyruklar ve o tur mesaj bağlamında isteği yutar; konsol
/// GERÇEK, render eden bir pencerede bile hiç kaymıyordu. Bir <c>Dispatcher.BeginInvoke(Input)</c> turu isteği
/// normal girdi akışına sokar.</para>
///
/// <para><b>Neden <see cref="ScrollViewer"/>, alttaki <c>IScrollInfo</c> DEĞİL (ÖLÇÜLDÜ):</b> scroll istemcisini
/// (konsolda AvalonEdit'in <c>TextArea</c>'sı) doğrudan sürmek offset'i o an değiştirir ama bir sonraki layout
/// turunda viewer KENDİ önbelleğini geri iter ve kaydırma SIFIRLANIR. Kaydırmanın kalıcı olduğu tek yol viewer'ın
/// kendi API'sidir — kullanıcının bugün çalışan tek yolu, yatay barı sürüklemek, zaten aynı mekanizmadır.</para>
///
/// <para><b>Adım:</b> dikeydeki WPF davranışının yatay ikizi — bir notch (<see cref="Mouse.MouseWheelDeltaForOneLine"/>)
/// başına <c>WheelScrollLines × satır</c> piksel. Delta'nın BÜYÜKLÜĞÜ onurlandırılır (WPF'in dikey yolu onu yok
/// sayıp her mesajda tam notch kaydırır): precision touchpad küçük deltalarla sık mesaj gönderir, büyüklüğü yok
/// saymak parmağın hareketiyle orantısız, zıplayan bir kaydırma üretirdi.</para>
/// </summary>
public static class HorizontalWheelScroll
{
    /// <summary>Bir "satır" kaç piksel — WPF <c>ScrollViewer</c>'ının kendi dikey sabitiyle (<c>_scrollLineDelta</c>)
    /// AYNI değer; yatayın dikeyle aynı hızda gitmesi için başka bir kaynak icat edilmez.</summary>
    internal const double ScrollLineDelta = 16.0;

    /// <summary>Kullanıcı ayarı "bir ekran" anlamına gelen ≤0 döndüğünde kullanılan notch başına satır sayısı —
    /// Windows varsayılanı. (Yatayda "bir ekran" kaydırma diye bir gelenek yoktur.)</summary>
    internal const int FallbackWheelScrollLines = 3;

    /// <summary>Bu panelin üzerindeyken gelen yatay tekerlek mesajlarını panelin yatay kaydırılabilir içeriğine
    /// bağlar. Panel canlı bir pencereye (HWND) bağlandığında kancalanır, ayrıldığında bırakılır; HWND yokken
    /// (headless test/ölçüm) sessizce no-op'tur.</summary>
    public static void Enable(FrameworkElement panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        HwndSource? source = null;
        double desired = 0;     // bu hareketin (burst) hedef offset'i
        long lastMessageMs = 0; // son yatay tekerlek mesajının anı

        HwndSourceHook hook = (nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg != Win32.WM_MOUSEHWHEEL) return 0;
            int delta = DeltaOf(wParam);
            if (delta == 0 || !panel.IsVisible || !Contains(panel, ScreenPointOf(lParam))) return 0;
            if (FindHorizontalTarget(panel) is not { } target) return 0;

            double offset = NextOffset(ref desired, ref lastMessageMs, Environment.TickCount64,
                target.HorizontalOffset, delta, target.ScrollableWidth, SystemParameters.WheelScrollLines);

            panel.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => target.ScrollToHorizontalOffset(offset));
            handled = true;
            return 0;
        };

        panel.Loaded += (_, _) =>
        {
            if (source is not null) return; // kancalanma bir kez (Loaded yeniden-parent'lamada tekrar gelir)
            if (PresentationSource.FromVisual(panel) is not HwndSource live) return;
            source = live;
            live.AddHook(hook);
        };
        panel.Unloaded += (_, _) =>
        {
            source?.RemoveHook(hook);
            source = null;
        };
    }

    /// <summary>Bir hareketin (burst) sürdüğü kabul edilen sessizlik payı. Touchpad tek bir parmak hareketinde
    /// milisaniyeler arayla ONLARCA mesaj gönderir; bu payın altındaki her mesaj AYNI hareketin parçasıdır.</summary>
    internal const long BurstWindowMs = 400;

    /// <summary>SAF karar: bu mesajın götürdüğü hedef offset.
    ///
    /// <para><b>Neden viewer'ın o anki offset'i her seferinde okunmuyor (ÖLÇÜLDÜ):</b> viewer istenen offset'i
    /// bir sonraki layout turunda uygular ve <see cref="ScrollViewer.HorizontalOffset"/> ancak ondan SONRA
    /// tazelenir. Her mesaj onu taban alsaydı, bir hareketin ardışık mesajları hep aynı bayat tabandan başlar ve
    /// aradaki adımlar KAYBOLURDU (ölçüm: dört notch sonunda üç notch'luk kayma). Bu yüzden hedef bir hareket
    /// boyunca BİRİKTİRİLİR.</para>
    ///
    /// <para><b>Neden yine de gerçeğe dönülüyor:</b> yatay bar sürüklenmiş ya da başka bir yol offset'i
    /// değiştirmiş olabilir. Hareketler arası sessizlik (<see cref="BurstWindowMs"/>) bunun ayracıdır: yeni bir
    /// hareket her zaman viewer'ın GERÇEK offset'inden başlar.</para></summary>
    internal static double NextOffset(ref double desired, ref long lastMessageMs, long nowMs,
        double publishedOffset, int delta, double scrollableWidth, int wheelScrollLines)
    {
        if (nowMs - lastMessageMs > BurstWindowMs) desired = publishedOffset; // yeni hareket → gerçekten başla
        lastMessageMs = nowMs;
        desired = TargetOffset(desired, delta, scrollableWidth, wheelScrollLines);
        return desired;
    }

    /// <summary>Ekran (fiziksel piksel) noktası panelin sınırları içinde mi. <see cref="Visual.PointFromScreen"/>
    /// DPI dönüşümünü kendi yapar — ölçek başına ayrı bir hesap YOKTUR.</summary>
    private static bool Contains(FrameworkElement panel, Point screenPoint)
    {
        var local = panel.PointFromScreen(screenPoint);
        return local.X >= 0 && local.Y >= 0 && local.X < panel.ActualWidth && local.Y < panel.ActualHeight;
    }

    /// <summary>Panelin içindeki İLK gerçekten yatay kaydırılabilir viewer (görsel ağaç, derinlik öncelikli).
    /// <c>ScrollableWidth &gt; 0</c> koşulu hem yatayı kapalı viewer'ları hem de taşması olmayanları eler.
    /// Şablonun içine de bakar — konsolun viewer'ı AvalonEdit şablonundadır, XAML'de görünmez.</summary>
    internal static ScrollViewer? FindHorizontalTarget(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is ScrollViewer { ScrollableWidth: > 0 } viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindHorizontalTarget(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    /// <summary>SAF karar: bir yatay tekerlek delta'sının götürdüğü yeni offset (içeriğin sınırlarına kelepçeli).
    /// Pozitif delta = sağa (Win32 sözleşmesi: "tekerlek sağa yatırıldı"), yani dikeyin AKSİNE offset ARTAR.</summary>
    internal static double TargetOffset(double currentOffset, int delta, double scrollableWidth, int wheelScrollLines)
    {
        int lines = wheelScrollLines > 0 ? wheelScrollLines : FallbackWheelScrollLines;
        double pixels = delta / (double)Mouse.MouseWheelDeltaForOneLine * lines * ScrollLineDelta;
        return Math.Clamp(currentOffset + pixels, 0, Math.Max(0, scrollableWidth));
    }

    /// <summary>HIWORD(wParam) — İŞARETLİ 16-bit tekerlek delta'sı.</summary>
    internal static int DeltaOf(nint wParam) => (short)((wParam >> 16) & 0xFFFF);

    /// <summary>lParam'ın iki İŞARETLİ 16-bit yarımı: imlecin EKRAN koordinatı (çoklu monitörde negatif olabilir).</summary>
    internal static Point ScreenPointOf(nint lParam) => new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
}
