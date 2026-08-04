using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Yatay tekerlek/touchpad girdisi (<see cref="HorizontalWheelScroll"/>).
///
/// <para><b>Kök neden (ölçüldü — aşağıdaki mesaj testi fix'ten önce KIRMIZIydı):</b> WPF'in girdi yığını yalnız
/// <c>WM_MOUSEWHEEL</c>'i bir routed event'e çevirir; <c>WM_MOUSEHWHEEL</c> HİÇ dağıtılmaz. Ne precision
/// touchpad'in iki parmakla yatay kaydırması ne de yatay tekerlekli bir farenin sinyali hiçbir elemente
/// ulaşıyordu; konsolda uzun MSBuild satırlarına ulaşmanın tek yolu yatay barı sürüklemekti.</para>
///
/// <para><b>Neden mesaj testi düz bir <see cref="ScrollViewer"/> ile kuruluyor:</b> pinlenen şey girdi YOLUdur
/// (pencere mesajı → panel sınırı → viewer → offset), ve o yol viewer'dan bağımsızdır. Konsolun kendi kablajı
/// (AvalonEdit şablonundaki viewer'ın BULUNABİLİRLİĞİ) ayrı bir testte pinlenir: AvalonEdit görsel satırlarını
/// gerçek render'a bağlı kurar ve ekran dışı, hiç render etmeyen bir test penceresinde piksel sonucu kararsızdır —
/// oradan bir ŞART çıkarmak testi kusura değil harness'a bağlardı.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class HorizontalWheelScrollTests
{
    private const int WM_MOUSEHWHEEL = 0x020E;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    /// <summary>Gerçek bir yatay tekerlek mesajı: HIWORD(wParam)=delta, lParam=EKRAN koordinatı (işaretli 16-bit
    /// yarımlar — test penceresi ekran dışında olduğundan ikisi de NEGATİFtir, üretimdeki işaretli çözümlemeyi
    /// de sürer).</summary>
    private static void SendHorizontalWheel(Window window, int delta, Point screenPoint)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        nint wParam = (nint)((delta & 0xFFFF) << 16);
        nint lParam = (nint)((((int)screenPoint.Y & 0xFFFF) << 16) | ((int)screenPoint.X & 0xFFFF));
        SendMessage(hwnd, WM_MOUSEHWHEEL, wParam, lParam);
        // Kaydırma bir Dispatcher turu SONRA istenir (üretim gerekçesi: HorizontalWheelScroll XML-doc'u) ve
        // viewer onu bir layout turunda uygular — ikisi de pompalanmadan gözlemlenemez.
        DispatcherPump.PumpUntil(() => false, TimeSpan.FromMilliseconds(60));
        window.UpdateLayout();
    }

    /// <summary>Yatay taşması olan, <see cref="HorizontalWheelScroll.Enable"/> edilmiş bir panel — üretimdeki
    /// konsolun kablajının birebir aynısı, ama viewer'ı stok.</summary>
    private static (ScrollViewer viewer, Border panel) OverflowingPanel()
    {
        var viewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Rectangle { Width = 4000, Height = 40, Fill = Brushes.Gray },
        };
        var panel = new Border { Child = viewer };
        HorizontalWheelScroll.Enable(panel);
        return (viewer, panel);
    }

    [StaFact]
    public void A_horizontal_wheel_over_the_panel_scrolls_it_sideways()
    {
        var (viewer, panel) = OverflowingPanel();
        var window = AnimationHost.ShowOffscreen(panel, width: 300, height: 120);
        window.UpdateLayout();
        Assert.True(viewer.ScrollableWidth > 0); // ön-koşul: gerçekten yatay taşma var
        Assert.Equal(0, viewer.HorizontalOffset);

        SendHorizontalWheel(window, Mouse.MouseWheelDeltaForOneLine, // sağa (tilt right / iki parmakla yana)
            panel.PointToScreen(new Point(panel.ActualWidth / 2, panel.ActualHeight / 2)));

        Assert.True(viewer.HorizontalOffset > 0);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_wheel_scrolls_back_and_never_past_the_left_edge()
    {
        var (viewer, panel) = OverflowingPanel();
        var window = AnimationHost.ShowOffscreen(panel, width: 300, height: 120);
        window.UpdateLayout();
        var center = panel.PointToScreen(new Point(panel.ActualWidth / 2, panel.ActualHeight / 2));

        SendHorizontalWheel(window, Mouse.MouseWheelDeltaForOneLine, center);
        Assert.True(viewer.HorizontalOffset > 0); // ön-koşul

        SendHorizontalWheel(window, -Mouse.MouseWheelDeltaForOneLine, center); // sola: aynı adım kadar geri
        Assert.Equal(0, viewer.HorizontalOffset);

        SendHorizontalWheel(window, -Mouse.MouseWheelDeltaForOneLine, center); // sol kenarda ısrar: kelepçe
        Assert.Equal(0, viewer.HorizontalOffset);
        GC.KeepAlive(window);
    }

    /// <summary>Yönlendirme imlecin ÜSTÜNDEKİ panele bakar: mesaj PENCEREYE gelir, "hangi panel" sorusunu
    /// uygulama cevaplar. Cevaplamazsa konsol, kullanıcı bambaşka bir panelin üzerindeyken kayardı.</summary>
    [StaFact]
    public void A_wheel_outside_the_panel_leaves_it_alone()
    {
        var (viewer, panel) = OverflowingPanel();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(panel); // üst yarı: panel; alt yarı: boş
        Grid.SetRow(panel, 0);
        var window = AnimationHost.ShowOffscreen(grid, width: 300, height: 160);
        window.UpdateLayout();
        Assert.True(viewer.ScrollableWidth > 0); // ön-koşul

        SendHorizontalWheel(window, Mouse.MouseWheelDeltaForOneLine,
            grid.PointToScreen(new Point(grid.ActualWidth / 2, grid.ActualHeight * 0.9))); // alt yarı = panel DEĞİL

        Assert.Equal(0, viewer.HorizontalOffset);
        GC.KeepAlive(window);
    }

    /// <summary>Panel ağaçtan ayrıldığında kanca bırakılır: aksi halde kapatılmış bir panelin kancası pencerenin
    /// mesaj yolunda kalır ve ölü bir görsel üzerinde koordinat testi yapmaya çalışırdı.</summary>
    [StaFact]
    public void Unloading_the_panel_releases_the_hook()
    {
        var (viewer, panel) = OverflowingPanel();
        var host = new Grid { Children = { panel } };
        var window = AnimationHost.ShowOffscreen(host, width: 300, height: 120);
        window.UpdateLayout();
        var center = panel.PointToScreen(new Point(panel.ActualWidth / 2, panel.ActualHeight / 2));

        host.Children.Remove(panel); // panel ağaçtan çıktı → Unloaded
        window.UpdateLayout();
        SendHorizontalWheel(window, Mouse.MouseWheelDeltaForOneLine, center);

        Assert.Equal(0, viewer.HorizontalOffset);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- saf karar (offset aritmetiği)

    [Fact]
    public void One_notch_moves_the_user_configured_number_of_lines()
    {
        // Dikeyin ikizi: notch başına WheelScrollLines × 16px. 3 satır → 48px.
        Assert.Equal(48, HorizontalWheelScroll.TargetOffset(0, Mouse.MouseWheelDeltaForOneLine, 1000, 3));
        Assert.Equal(16, HorizontalWheelScroll.TargetOffset(0, Mouse.MouseWheelDeltaForOneLine, 1000, 1));
    }

    [Fact]
    public void A_partial_delta_moves_proportionally_because_touchpads_send_small_deltas()
    {
        // Precision touchpad tam notch göndermez; büyüklüğü yok saymak (WPF'in dikey yolu gibi) parmağın
        // hareketiyle orantısız, zıplayan bir kaydırma üretirdi.
        Assert.Equal(12, HorizontalWheelScroll.TargetOffset(0, Mouse.MouseWheelDeltaForOneLine / 4, 1000, 3));
    }

    [Fact]
    public void The_offset_is_clamped_to_the_content()
    {
        Assert.Equal(0, HorizontalWheelScroll.TargetOffset(10, -Mouse.MouseWheelDeltaForOneLine, 1000, 3));
        Assert.Equal(100, HorizontalWheelScroll.TargetOffset(90, Mouse.MouseWheelDeltaForOneLine, 100, 3));
        Assert.Equal(0, HorizontalWheelScroll.TargetOffset(0, Mouse.MouseWheelDeltaForOneLine, 0, 3)); // taşma yok
    }

    [Fact]
    public void An_unusable_wheel_scroll_lines_setting_falls_back_to_the_windows_default()
    {
        // SystemParameters.WheelScrollLines "bir ekran" için ≤0 döner; yatayda o geleneğin karşılığı yoktur.
        Assert.Equal(HorizontalWheelScroll.FallbackWheelScrollLines * HorizontalWheelScroll.ScrollLineDelta,
            HorizontalWheelScroll.TargetOffset(0, Mouse.MouseWheelDeltaForOneLine, 1000, -1));
    }

    /// <summary>ÖLÇÜLDÜ (gerçek, render eden bir pencerede): viewer istenen offset'i bir sonraki layout turunda
    /// uygular ve yayınlanmış <c>HorizontalOffset</c>'i ancak ondan sonra tazeler. Her mesaj onu taban alsaydı bir
    /// hareketin ardışık mesajları hep aynı bayat tabandan başlardı — dört notch sonunda ekranda üç notch'luk
    /// kayma vardı. Hedef bu yüzden hareket boyunca BİRİKTİRİLİR.</summary>
    [Fact]
    public void Messages_of_one_gesture_accumulate_even_when_the_viewer_has_not_caught_up()
    {
        double desired = 0;
        long last = 0;
        const long start = 10_000;

        Assert.Equal(48, HorizontalWheelScroll.NextOffset(ref desired, ref last, start, 0, 120, 1000, 3));
        // viewer HÂLÂ 0 yayınlıyor (bayat) — birikim onu beklemez
        Assert.Equal(96, HorizontalWheelScroll.NextOffset(ref desired, ref last, start + 8, 0, 120, 1000, 3));
        // viewer bir adım geriden geldi; hedef yine de kendi üzerine ekler
        Assert.Equal(144, HorizontalWheelScroll.NextOffset(ref desired, ref last, start + 16, 48, 120, 1000, 3));
    }

    /// <summary>Birikim SONSUZA DEK sürmez: yatay bar sürüklenmiş olabilir. Hareketler arası sessizlik ayraçtır —
    /// yeni bir hareket viewer'ın GERÇEK offset'inden başlar.</summary>
    [Fact]
    public void A_new_gesture_starts_from_the_viewers_real_offset()
    {
        double desired = 0;
        long last = 0;
        const long start = 10_000;
        HorizontalWheelScroll.NextOffset(ref desired, ref last, start, 0, 120, 1000, 3); // → 48

        // kullanıcı beklerken barı 500'e sürükledi; sonraki notch oradan devam etmeli
        Assert.Equal(548, HorizontalWheelScroll.NextOffset(
            ref desired, ref last, start + HorizontalWheelScroll.BurstWindowMs + 1, 500, 120, 1000, 3));
    }

    [Fact]
    public void The_message_parameters_are_decoded_as_signed_halves()
    {
        Assert.Equal(-120, HorizontalWheelScroll.DeltaOf((nint)((-120 & 0xFFFF) << 16)));
        Assert.Equal(120, HorizontalWheelScroll.DeltaOf((nint)(120 << 16)));
        // Çoklu monitörde imleç NEGATİF ekran koordinatında olabilir — işaretsiz okunursa 60 000 piksel sağda sanılırdı.
        Assert.Equal(new Point(-5000, -6000),
            HorizontalWheelScroll.ScreenPointOf((nint)((((-6000) & 0xFFFF) << 16) | ((-5000) & 0xFFFF))));
    }

    // ---------------------------------------------------------------- konsolun kendi kablajı

    /// <summary>Konsol, yatay tekerleği <see cref="HorizontalWheelScroll.Enable"/> eden TEK paneldir ve
    /// yönlendiricinin arayacağı viewer AvalonEdit ŞABLONUNUN İÇİNDEDİR (XAML'de görünmez) — arama şablona
    /// inmezse konsol sessizce kapsam dışı kalırdı.</summary>
    [StaFact]
    public void The_console_exposes_a_horizontally_scrollable_viewer_to_the_router()
    {
        var view = new ConsoleView();
        view.AppendBatch(new string('x', 2000) + "\n");
        var window = AnimationHost.ShowOffscreen(view, width: 320, height: 200);
        DispatcherPump.PumpUntil(() => HorizontalWheelScroll.FindHorizontalTarget(view) is not null,
            TimeSpan.FromSeconds(2));

        var target = HorizontalWheelScroll.FindHorizontalTarget(view);
        Assert.NotNull(target);
        Assert.True(target.ScrollableWidth > 0);
        GC.KeepAlive(window);
    }
}
