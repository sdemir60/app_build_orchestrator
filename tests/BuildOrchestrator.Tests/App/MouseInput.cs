using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1] Testlerde GERÇEK bir fare girdisi — basış ve tekerlek — yükseltmenin TEK yeri.
///
/// <para><b>Neden doğrudan handler çağrılmıyor:</b> WPF'te <see cref="UIElement.MouseLeftButtonDown"/>
/// <b>Direct</b> yönlendirmelidir; "kabarma" görüntüsünü <see cref="Mouse.MouseDownEvent"/> (bubbling)
/// üzerindeki sınıf handler'ı üretir — kabarma yolundaki her öğede tek tek yeniden yükseltir ve
/// <c>Handled</c>'ı geri kopyalar. Bu yüzden testler <see cref="Mouse.MouseDownEvent"/> yükseltir: yol
/// üstündeki handler'lar üretimde hangi sırayla/koşulla koşuyorsa testte de öyle koşar.</para>
///
/// <para><b>Neden ortak:</b> aynı beş satır <c>GraphClickTests</c> (düğüm/zemin tıklaması) ve
/// <c>GraphPanZoomTests</c> (sinema modunda zemin basışının seçimi KALDIRMADIĞI) tarafından kullanılır —
/// kopya YASAK (CLAUDE.md). Tekerlek de öyle: iki fazlı jesti (<see cref="Wheel"/>) <c>AboutDialogTests</c> (Environment hücreleri)
/// ve <c>RealizedNarrative</c> (konsol), yalnız tünel yarısını (<see cref="PreviewWheel"/>) listenin takip, animasyon iptali ve
/// başlık bandı testleri kullanır; yatay tekerleği (<see cref="HorizontalWheel"/>) yönlendiricinin kendi testleri
/// (<c>HorizontalWheelScrollTests</c>) ve konsolun yana jest testleri (<c>RealizedNarrative</c>).</para>
/// </summary>
internal static class MouseInput
{
    /// <summary>Sol tuş basışı. Dönen <see cref="MouseButtonEventArgs"/> ile <c>Handled</c> sınanabilir.</summary>
    public static MouseButtonEventArgs PressLeft(UIElement target) => Press(target, MouseButton.Left);

    /// <summary>
    /// Verilen tuşun basışı (<see cref="Mouse.MouseDownEvent"/> — gerekçe sınıf özetinde). <paramref name="clickCount"/>:
    /// WPF'in art arda tık sayısı (2 = çift tık). <c>MouseButtonEventArgs.ClickCount</c>'ın setter'ı <c>internal</c>'dır — girdi
    /// sistemi onu kendisi yazar; headless'ta yalnız yansımayla verilir. <c>0</c> = hiç yazılmaz (kurucunun varsayılanı).
    /// </summary>
    public static MouseButtonEventArgs Press(UIElement target, MouseButton button, int clickCount = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.MouseDownEvent,
        };
        if (clickCount != 0)
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clickCount);
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>
    /// Sol tuş basışının TÜNELLEYEN yarısı. <see cref="UIElement.PreviewMouseLeftButtonDown"/> de
    /// <b>Direct</b>'tir ve tünelleme görüntüsünü <see cref="Mouse.PreviewMouseDownEvent"/> üzerindeki sınıf
    /// handler'ı üretir — <see cref="PressLeft"/> ile AYNI gerekçe, yalnız yön ters.
    ///
    /// <para>Ayrı bir metot olmasının nedeni: bir <c>ButtonBase</c>'in tıkı YUTULABİLİR olması yalnız önizleme
    /// yolunda karara bağlanır (handled bir önizleme basışında ButtonBase mouse'u hiç yakalamaz, dolayısıyla
    /// bırakmada <c>Click</c> de doğmaz). Kabarma yarısını yükselten test bunu göremez.</para>
    /// </summary>
    public static MouseButtonEventArgs PreviewPressLeft(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Sol tuş BIRAKMA. [quiet] Grafta "boş zemine tıklama" kararı artık bırakmaya aittir
    /// (basış bir sürüklemenin başı olabilir), dolayısıyla tam jesti sürmek için gerekir.</summary>
    public static MouseButtonEventArgs ReleaseLeft(UIElement target) => Release(target, MouseButton.Left);

    /// <summary>Verilen tuşun BIRAKILMASI (<see cref="Mouse.MouseUpEvent"/>, bubbling) — sağ tuş bırakma, bağlam menüsü / sistem menüsü
    /// jestlerinin tetikleyicisidir.</summary>
    public static MouseButtonEventArgs Release(UIElement target, MouseButton button)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.MouseUpEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>
    /// Tekerleğin bir hareketi (<paramref name="delta"/>: pozitif yukarı). HWND/SendMessage gerekmez — WPF düz dikey tekerleği zaten
    /// routed event olarak dağıtır (yatay tekerlek ayrıdır: <see cref="HorizontalWheel"/>).
    ///
    /// <para><b>İKİ faz raise edilir, çünkü WPF'in <c>InputManager</c>'ı da öyle yapar:</b> önce tünel (<c>PreviewMouseWheel</c>),
    /// önizleme YUTMADIYSA baloncuk (<c>MouseWheel</c>). Baloncuk fazı ŞARTTIR: <c>ScrollViewer</c>'ın olayı işleyip yutan class
    /// handler'ı (<c>OnMouseWheel</c>) YALNIZ orada koşar — tek başına önizleme raise etmek kaydırmayı hiç sürmez. Dönen önizleme
    /// argümanıyla <c>Handled</c> sınanabilir.</para>
    /// </summary>
    public static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var preview = PreviewWheel(target, delta);
        if (!preview.Handled)
        {
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
            });
        }
        return preview;
    }

    /// <summary>
    /// Tekerleğin YALNIZ tünel yarısı (<c>PreviewMouseWheel</c>) — BİLEREK tek faz. Bunu kullanan testler tünelde dinleyen üretim
    /// kablosunu tek başına sınar: kullanıcı-kaydırma sinyali (takibi duraklatır), uçuştaki animasyonun iptali, yığılmış başlık
    /// bandının tekerleği listeye iletmesi. Baloncuk fazı ScrollViewer'ın kendi kaydırmasını da koştururdu (<see cref="Wheel"/>) ve
    /// ölçülen şeye ikinci bir neden karışırdı. Tam jest gereken yerde <see cref="Wheel"/>.
    /// </summary>
    public static MouseWheelEventArgs PreviewWheel(UIElement target, int delta)
    {
        ArgumentNullException.ThrowIfNull(target);
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };
        target.RaiseEvent(preview);
        return preview;
    }

    /// <summary>
    /// Yatay tekerleğin bir hareketi (<paramref name="delta"/>: pozitif sağa) — GERÇEK bir pencere mesajı (<c>WM_MOUSEHWHEEL</c>): WPF onu
    /// routed event'e çevirmez, uygulamanın kancası (<c>HorizontalWheelScroll</c>) pencerenin mesaj yolundan yakalar. HIWORD(wParam)=delta,
    /// lParam=<paramref name="screenPoint"/> (EKRAN koordinatı, işaretli 16-bit yarımlar — test penceresi ekran dışında olduğundan ikisi de
    /// NEGATİFtir, üretimdeki işaretli çözümlemeyi de sürer).
    /// <para>Kaydırma bir Dispatcher turu SONRA istenir (üretim gerekçesi: <c>HorizontalWheelScroll</c> XML-doc'u) ve viewer onu bir layout
    /// turunda uygular — ikisi de pompalanmadan gözlemlenemez. [son temizlik · madde 1] Eskiden <c>HorizontalWheelScrollTests</c>'in özel
    /// yardımcısıydı; konsolun yana jest testleri de aynı mesajı gönderir.</para>
    /// </summary>
    public static void HorizontalWheel(Window window, int delta, Point screenPoint)
    {
        ArgumentNullException.ThrowIfNull(window);
        nint hwnd = new WindowInteropHelper(window).Handle;
        nint wParam = (nint)((delta & 0xFFFF) << 16);
        nint lParam = (nint)((((int)screenPoint.Y & 0xFFFF) << 16) | ((int)screenPoint.X & 0xFFFF));
        SendMessage(hwnd, Win32.WM_MOUSEHWHEEL, wParam, lParam);
        DispatcherPump.PumpUntil(() => false, TimeSpan.FromMilliseconds(60));
        window.UpdateLayout();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    /// <summary>Capture'ın DÜŞMESİ (Alt+Tab, popup, başka öğenin capture alması). Bu bir bırakma DEĞİL
    /// iptaldir — <c>GraphView</c> onu tuş bırakmadan ayrı bir yola bağlar.</summary>
    public static void LoseCapture(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = Mouse.LostMouseCaptureEvent,
        });
    }
}
