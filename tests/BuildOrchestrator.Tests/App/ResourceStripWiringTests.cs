using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Diagnostics;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1, §3.6 · plan R5, R6] Şeridin pencereye kablajı: örnekleyicinin <b>ne zaman</b> çalıştığı ve şeridin
/// caption davranışını (sürükleme, çift tık, sistem menüsü) <b>kendisinin</b> iletmesi.
///
/// <para><b>Etkinlik kuralı</b> tek yerdedir (<see cref="MainWindow.ShouldSampleResources"/>): pencere yüklü, yüzey gizli değil ve küçültülmüş değil.
/// Başsız süitte pencere gösterilemez; kablaj, üretimin kendi yoluyla — <c>Loaded</c> olayı, <see cref="MainWindow.SetSurfaceHidden"/> ve
/// <c>WindowState</c> değişimi — sürülür. Örnekleyicinin ritmi ve tazelenme kuralları <see cref="ResourceSamplerTests"/>'tedir; burada yalnız
/// kabuğun onu doğru anda açıp kapattığı sınanır. Hiçbir şey ayrıca zaman beklemez: örnekleyicinin zamanlayıcısı hiç pompalanmaz.</para>
///
/// <para><b>R6</b> (plan): şeridin hücreleri <c>IsHitTestVisibleInChrome</c> altında olduğu için hücrelerin üstü artık caption değildir; sol basış
/// sürüklemeyi, çift tık büyütme/geri almayı, sağ bırakış sistem menüsünü şerit KENDİ iletir. (Şeridin kökünün zemini yoktur: hücre arası boşluklar ve
/// hücrelerin üstü/altı yerel title bar'da kalır — o pin <see cref="ResourceStripRealizeTests"/>'tedir.) Pencere eylemleri test dikişlerinden geçer
/// (<see cref="MainWindow.CaptionDragMoveForTest"/> …): gerçek <c>DragMove</c> ve sistem menüsü bir HWND ve fare ister.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ResourceStripWiringTests
{
    /// <summary>Pencerenin <c>Loaded</c> olayını üretimdeki gibi yükseltir (başsız süitte pencere gösterilemez).</summary>
    private static void Load(MainWindow window) => window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, window));

    private static ResourceSampler SamplerOf(MainWindow window) =>
        Assert.IsType<ResourceSampler>(window.StripSampler);

    /// <summary>Açılmış bir örnekleyicinin zamanlayıcısı testten sonra da yaşar: STA dispatcher'ı testler arasında PAYLAŞILIR
    /// (<c>MainWindowHost.NewWithProjectsOn</c> aynı sızıntıyı kapatır) ve sonraki bir testin pompasında tikler. Açan her test onu bitirirken durdurur.</summary>
    private static void Disarm(MainWindow window) => window.StopResourceStrip();

    /// <summary>Realize edilmiş, <c>Loaded</c> olayı yükseltilmiş bir pencerede <paramref name="body"/>'yi koşturur ve HER yolda örnekleyiciyi
    /// durdurur (<see cref="Disarm"/>). <paramref name="normalState"/>: pencere XAML'de doğuştan Maximized'dır; küçültme/geri alma testleri Normal'den başlar.</summary>
    private static void WithLoadedWindow(bool normalState, Action<MainWindow, RunViewModel, ResourceSampler> body)
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        if (normalState) window.WindowState = WindowState.Normal;
        var sampler = SamplerOf(window);
        Load(window);
        try { body(window, vm, sampler); }
        finally
        {
            Disarm(window);
            GC.KeepAlive(window);
        }
    }

    // ---------------------------------------------------------------- örnekleyicinin etkinlik kuralı

    /// <summary>Kural tablosu — AÇIK satırlar (bir oracle ifadesi değil): yalnız yüklü + görünür + küçültülmemiş pencere örnekler.</summary>
    [Theory]
    [InlineData(true, false, WindowState.Normal, true)]
    [InlineData(true, false, WindowState.Maximized, true)]
    [InlineData(true, false, WindowState.Minimized, false)] // küçültülmüş
    [InlineData(true, true, WindowState.Normal, false)]     // tepside / gizli
    [InlineData(true, true, WindowState.Maximized, false)]
    [InlineData(true, true, WindowState.Minimized, false)]
    [InlineData(false, false, WindowState.Normal, false)]   // henüz yüklenmemiş
    [InlineData(false, false, WindowState.Maximized, false)]
    [InlineData(false, false, WindowState.Minimized, false)]
    [InlineData(false, true, WindowState.Normal, false)]
    [InlineData(false, true, WindowState.Maximized, false)]
    [InlineData(false, true, WindowState.Minimized, false)]
    public void The_sampler_runs_only_for_a_loaded_visible_unminimized_window(bool loaded, bool hidden, WindowState state, bool expected) =>
        Assert.Equal(expected, MainWindow.ShouldSampleResources(loaded, hidden, state));

    [StaFact]
    public void A_window_that_was_never_loaded_keeps_the_sampler_idle_whatever_else_happens()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        var sampler = SamplerOf(window);

        window.SetSurfaceHidden(true);
        window.SetSurfaceHidden(false);
        window.WindowState = WindowState.Minimized;
        window.WindowState = WindowState.Normal;

        Assert.False(sampler.IsActive);
        Assert.Equal(0, sampler.SampleCount); // hiçbir Win32 okuması örnek olmadı
        Assert.Equal(ResourceStripText.Unreadable, vm.StripCpuText);
        Assert.Equal(ResourceStripText.Unreadable, vm.StripMemoryText);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Loading_the_window_arms_the_sampler_with_a_baseline_but_no_sample_yet()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        var sampler = SamplerOf(window);
        Assert.False(sampler.IsActive); // ön-koşul: yüklenmeden açık değil

        try
        {
            Load(window);

            Assert.True(sampler.IsActive);
            Assert.Equal(0, sampler.SampleCount); // ilk örnek 0,5 sn sonra gelir; etkinleşmenin taban okuması örnek değildir
            Assert.Equal(ResourceStripText.Unreadable, vm.StripMemoryText);
        }
        finally { Disarm(window); }
    }

    /// <summary>Gizliyken örnek yok (zamanlayıcı durur); yeniden görününce HEMEN bir örnek (R5) ve örnek, bu pencerenin VM'ine ulaşır.</summary>
    [StaFact]
    public void Hiding_the_surface_stops_the_sampler_and_showing_it_again_takes_one_sample_at_once()
    {
        WithLoadedWindow(normalState: false, (window, vm, sampler) =>
        {
            window.SetSurfaceHidden(true);
            Assert.False(sampler.IsActive);
            Assert.Equal(0, sampler.SampleCount);

            window.SetSurfaceHidden(false);
            Assert.True(sampler.IsActive);
            Assert.Equal(1, sampler.SampleCount);                                  // dönüşte anında örnek
            Assert.NotEqual(ResourceStripText.Unreadable, vm.StripMemoryText);      // …ve bu pencerenin VM'ine yazıldı
            Assert.Contains(' ', vm.StripMemoryText);                               // "310 MB" / "1.2 GB" biçimi
        });
    }

    /// <summary>Üretimin gerçek zinciri uçtan uca: <c>Loaded</c> → taban okuması → 0,5 sn sonra GERÇEK <c>DispatcherTimer</c> tiki → pencerenin
    /// outer job'undan okuma → VM → şeridin hücreleri. Bekleme koşula bağlıdır (örnek sayısı), sabit bir uyku değil. Test penceresinin motoru hiç
    /// doğmaz: job boştur, RAM yalnız uygulamadır (<c>engine 0 MB · compilers 0 MB</c>).</summary>
    [StaFact]
    public void The_first_real_tick_after_loading_lands_in_the_strip_cells()
    {
        WithLoadedWindow(normalState: true, (window, vm, sampler) =>
        {
            var strip = window.ResourceStripView;
            Assert.Equal(ResourceStripText.Unreadable, strip.MemoryValue.Text); // ön-koşul: ilk örneğe dek —

            DispatcherPump.PumpUntil(() => sampler.SampleCount >= 1, TimeSpan.FromSeconds(10));

            Assert.True(sampler.SampleCount >= 1, "ilk gerçek tik gelmedi");
            Assert.NotEqual(ResourceStripText.Unreadable, strip.MemoryValue.Text);
            Assert.EndsWith("%", strip.CpuValue.Text, StringComparison.Ordinal); // job okunabilir → CPU bir yüzde
            Assert.Equal((vm.StripCpuText, vm.StripMemoryText), (strip.CpuValue.Text, strip.MemoryValue.Text));
            Assert.Contains("engine 0 MB · compilers 0 MB", vm.StripMemoryTooltip, StringComparison.Ordinal);
            Assert.Equal(vm.StripMemoryTooltip, ((System.Windows.Controls.ToolTip)strip.MemoryCell.ToolTip).Content);
        });
    }

    [StaFact]
    public void Minimizing_stops_the_sampler_and_restoring_resumes_it_with_one_sample_at_once()
    {
        WithLoadedWindow(normalState: true, (window, _, sampler) =>
        {
            Assert.True(sampler.IsActive); // ön-koşul: Normal durumda açık

            window.WindowState = WindowState.Minimized;
            Assert.False(sampler.IsActive);
            Assert.Equal(0, sampler.SampleCount);

            window.WindowState = WindowState.Maximized;
            Assert.True(sampler.IsActive);
            Assert.Equal(1, sampler.SampleCount);
        });
    }

    [StaFact]
    public void A_window_that_is_both_hidden_and_minimized_resumes_only_when_both_are_lifted()
    {
        WithLoadedWindow(normalState: true, (window, _, sampler) =>
        {
            window.WindowState = WindowState.Minimized;
            window.SetSurfaceHidden(true);
            window.SetSurfaceHidden(false); // tepsiden dönüş ama hâlâ simge durumunda
            Assert.False(sampler.IsActive);

            window.WindowState = WindowState.Normal;
            Assert.True(sampler.IsActive);
        });
    }

    [StaFact]
    public void Closing_the_window_stops_the_sampler_for_good()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        window.WindowState = WindowState.Normal;
        var sampler = SamplerOf(window);
        Load(window);
        Assert.True(sampler.IsActive);

        window.StopResourceStrip();
        Assert.False(sampler.IsActive);

        // Kapanıştan sonra gelen hiçbir sinyal (gösterim, durum, yükleme) örneklemeyi geri getirmez.
        window.SetSurfaceHidden(true);
        window.SetSurfaceHidden(false);
        window.WindowState = WindowState.Minimized;
        window.WindowState = WindowState.Normal;
        Load(window);
        Assert.False(sampler.IsActive);
        Assert.Equal(0, sampler.SampleCount);
        GC.KeepAlive(window);
    }

    /// <summary>Pencere gerçekten kapanınca (<c>OnClosed</c> — tepsiye gizlenen kapatmalarda DEĞİL) örnekleyici durdurulur; <c>OnClosed</c> başsız
    /// süitte çağrılamaz (protected, kapanan pencere), bu yüzden bağ kaynakta pinlenir. <c>OnClosed</c>'ın İLK deyimi
    /// (<c>Narrative.Flush</c>) yerinde kalır — o sırayı <see cref="CloseToTrayTests"/> pinler.</summary>
    [Fact]
    public void The_real_close_path_stops_the_sampler()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml.cs"));
        var onClosed = Regex.Match(source, @"protected override void OnClosed\(EventArgs e\)\s*\{(?<body>.*?)base\.OnClosed\(e\);", RegexOptions.Singleline);

        Assert.True(onClosed.Success, "OnClosed bulunamadı");
        Assert.Contains("StopResourceStrip();", onClosed.Groups["body"].Value, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R6: şerit caption davranışını kendisi iletir

    private static (MainWindow window, List<string> calls) CaptionFixture(TempDir temp)
    {
        var (window, _) = MainWindowHost.NewRealized(temp);
        var calls = new List<string>();
        window.CaptionDragMoveForTest = () => calls.Add("drag");
        window.CaptionToggleMaximizeForTest = () => calls.Add("toggle");
        window.CaptionSystemMenuForTest = () => calls.Add("menu");
        return (window, calls);
    }

    [StaFact]
    public void A_left_press_on_the_strip_starts_a_window_drag_and_the_press_is_consumed()
    {
        using var temp = new TempDir();
        var (window, calls) = CaptionFixture(temp);

        var args = MouseInput.PressLeft(window.ResourceStripView.CpuValue);

        Assert.Equal(["drag"], calls);
        Assert.True(args.Handled);
        GC.KeepAlive(window);
    }

    /// <summary>Çift tık büyütür/geri alır (mevcut maximize-restore yolu) — sürükleme BAŞLAMAZ. Bir hücrenin her parçasında: ikon, değer, mod sözcüğü
    /// ve hücrenin kendisi (şeridin kökü hit-test edilemez, ona hiçbir basış gelmez).</summary>
    [StaFact]
    public void A_double_click_anywhere_on_a_cell_toggles_maximize_and_does_not_drag()
    {
        using var temp = new TempDir();
        var (window, calls) = CaptionFixture(temp);
        var strip = window.ResourceStripView;

        foreach (UIElement target in new UIElement[] { strip.CpuValue, strip.MemoryIcon, strip.ModeValue, strip.ParallelCell })
        {
            calls.Clear();
            var args = MouseInput.Press(target, MouseButton.Left, clickCount: 2);
            Assert.Equal(["toggle"], calls);
            Assert.True(args.Handled);
        }
        GC.KeepAlive(window);
    }

    /// <summary>Sistem menüsü sağ tuş BIRAKILIŞINDA açılır (Windows'un caption davranışı) — basışta değil.</summary>
    [StaFact]
    public void A_right_button_release_on_the_strip_opens_the_system_menu_the_press_does_not()
    {
        using var temp = new TempDir();
        var (window, calls) = CaptionFixture(temp);
        var strip = window.ResourceStripView;

        MouseInput.Press(strip.CpuCell, MouseButton.Right);
        Assert.Empty(calls);

        var args = MouseInput.Release(strip.CpuCell, MouseButton.Right);
        Assert.Equal(["menu"], calls);
        Assert.True(args.Handled);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Nothing_else_on_the_strip_is_forwarded_and_a_press_elsewhere_in_the_window_is_not_either()
    {
        using var temp = new TempDir();
        var (window, calls) = CaptionFixture(temp);
        var strip = window.ResourceStripView;

        MouseInput.ReleaseLeft(strip.CpuValue);                    // sol bırakış
        MouseInput.Press(strip.CpuValue, MouseButton.Middle);      // orta tuş
        MouseInput.Press(strip.CpuValue, MouseButton.XButton1);    // yan tuş
        MouseInput.Release(strip.CpuValue, MouseButton.Left);
        MouseInput.PressLeft(window.Shell);                        // şeridin dışı: kendi davranışı, caption değil
        MouseInput.Release(window.Shell, MouseButton.Right);

        Assert.Empty(calls);
        GC.KeepAlive(window);
    }

    /// <summary>Sistem menüsünün konumu: <c>PointToScreen</c> CİHAZ pikseli verir, <c>SystemCommands.ShowSystemMenu</c> ise MANTIKSAL (dip) ister
    /// (ölçüldü: WPF onu pencerenin DPI'ıyla cihaza çevirir). 125%'te çevrilmezse menü imlecin 1,25 katı uzağında açılırdı.</summary>
    [Theory]
    [InlineData(1000, 500, 1.0, 1000, 500)]
    [InlineData(1000, 500, 1.25, 800, 400)]
    [InlineData(1500, 750, 1.5, 1000, 500)]
    [InlineData(-1250, 250, 1.25, -1000, 200)] // sol monitörde ekran koordinatı eksidir
    public void The_system_menu_anchor_converts_device_pixels_to_logical_units(double x, double y, double scale, double expectedX, double expectedY)
    {
        var anchor = MainWindow.SystemMenuAnchor(new Point(x, y), new DpiScale(scale, scale));

        Assert.Equal(expectedX, anchor.X, 6);
        Assert.Equal(expectedY, anchor.Y, 6);
    }
}
