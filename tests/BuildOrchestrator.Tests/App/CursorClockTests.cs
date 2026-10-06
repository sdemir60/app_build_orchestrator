using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// İki imleç (konsol prompt'u, event stream aktif satırı) TEK paylaşımlı saatte kırpar ve pencere AKTİF değilken
/// sabit durur — PERF Faz B, karar 4.
///
/// <para><b>Neden bu dosya var (ÖLÇÜLDÜ):</b> ön planda, boşta işlemci maliyetinin en büyük payı imleç saatleriydi.
/// Her imleç iki sonsuz saat kuruyordu (kırpma + renk turu) ve pencere başka bir pencerenin arkasındayken, simge
/// durumundayken ve tepsideyken de dönüyordu. Tepsi kapısı (<see cref="HiddenCursorClockTests"/>) yalnız gizli
/// pencereyi kapsar; arkada duran ama görünür pencere kapsam dışıydı. Artık saatler pencere başına TEK çift
/// (<see cref="CursorClock"/>) ve yalnız pencere aktifken koşar — bir imlecin yalnız etkin pencerede kırpması
/// Windows geleneğidir.</para>
///
/// <para><b>Ne DEĞİŞMEDİ:</b> görünürlük kapısı hâlâ başlatıcının İÇİNDEDİR (<see cref="HiddenCursorClockTests"/>
/// aynen geçerli); iki kapı birbirinden bağımsızdır ve ikisi de saati durdurur. Reduced-motion'da saat HİÇ kurulmaz.</para>
///
/// <para><b>Pencere aktifliği nasıl sınanır:</b> başsız test pencereleri etkin olmayabilir; bu yüzden görünümler
/// <c>Window.IsActive</c>'i kendileri OKUMAZ. Sinyal yalnız <see cref="CursorClock.SetWindowActive"/> ile gelir
/// (üretimde <c>MainWindow</c>'un Activated/Deactivated olayları; ilk durumu da <c>MainWindow</c> ilk gösterimde
/// kendi <c>IsActive</c>'inden bildirir) ve hiç sinyal gelmemiş saat "aktif"tir. Saat pencere başınadır: her test
/// kendi pencerelerini kurduğu için testler birbirinin saatine sızmaz.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class CursorClockTests
{
    /// <summary>Konsol ve event stream aynı pencerede, yan yana — üretimdeki kabuğun iki imleci.</summary>
    private static (ConsoleView Console, EventStreamView Stream, RunViewModel Vm, Window Window) RealizeBoth(
        Func<bool>? motion = null)
    {
        motion ??= () => true;
        var vm = HiddenCursorClockTests.NewVm();
        var console = new ConsoleView { AnimationsEnabledProvider = motion };
        var stream = new EventStreamView { AnimationsEnabledProvider = motion, DataContext = vm };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(stream, 1);
        grid.Children.Add(console);
        grid.Children.Add(stream);
        var window = DsResources.Realize(DsResources.NewHost(), grid, 400, 400);
        console.ShowReady();
        return (console, stream, vm, window);
    }

    private static bool Blinking(UIElement cursor) =>
        cursor.HasAnimatedProperties && CursorHop.IsRunning(cursor as Shape);

    /// <summary>Başlatıcıları yeniden çağıran üretim yolları: stream'de her olay aktif satırı tazeler.</summary>
    private static void RaiseStreamEvents(RunViewModel vm)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
    }

    /// <summary>Görünümü ağaçtan çıkarır ve <c>Unloaded</c>'ı yükseltir. WPF'in kendi <c>Unloaded</c>'ı başsız ağaçta
    /// dispatcher turuna bağlı olabilir; olay doğrudan yükseltilir (başlatıcıların <c>Unloaded</c> işleyicileri
    /// idempotenttir, ağaçtan çıkış zaten <c>IsVisibleChanged</c> ile de ayırır).</summary>
    private static void RemoveFromTree(FrameworkElement view)
    {
        ((Panel)view.Parent).Children.Remove(view);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, view));
    }

    // ---------------------------------------------------------------- tek saat

    [StaFact]
    public void The_console_and_the_event_stream_cursors_ride_one_shared_clock()
    {
        var (console, stream, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);

        Assert.True(Blinking(console.ActiveCursorGlyph), "ön-koşul: konsol imleci GERÇEKTEN kırpmalı");
        Assert.True(Blinking(stream.ActiveCursorGlyph), "ön-koşul: stream imleci GERÇEKTEN kırpmalı");
        Assert.Equal(2, clock.AttachedCount); // iki imleç, TEK saat
        Assert.NotNull(clock.ActiveBlinkClock);
        Assert.NotNull(clock.ActiveColorClock); // renk turu da ORTAK: imleç başına bir renk saati kurulmaz
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Kabuktaki görünümler saati <c>Window.GetWindow</c> ile bulur; <c>MainWindow</c> ise onu ctor'da, henüz
    /// gösterilmemişken <c>For(this)</c> ile alır. İkisi AYNI saat olmalıdır: <c>GetWindow</c>'un döndürdüğü şey, saatin
    /// anahtarı olan pencere nesnesinin kendisidir. Gerçek <c>MainWindow</c> burada kullanılamaz: kabuk testlerinde
    /// pencere hiç <c>Show()</c> edilmez (tepsi ikonu, global kısayol kaydı ve motor gösterimde kurulur) ve
    /// <c>HostOffscreen</c> içeriği başka bir pencereye taşıdığı için görünümler orada o pencerenin saatini bulur.
    /// Eşdeğer: sıradan bir pencerede görünümlerin bulduğu saat, pencerenin <c>For</c> ile aldığı saattir.
    /// </summary>
    [StaFact]
    public void The_views_of_a_window_find_the_clock_that_window_is_keyed_by()
    {
        var (console, stream, _, window) = RealizeBoth();

        Assert.Same(window, Window.GetWindow(console)); // görünümler pencereyi bu nesne olarak çözer
        Assert.Same(window, Window.GetWindow(stream));
        Assert.Equal(2, CursorClock.For(window).AttachedCount); // ve imleçleri O pencerenin saatine bağlar
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Re_evaluating_the_cursors_never_restarts_the_shared_clock()
    {
        var (console, _, vm, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        var shared = clock.ActiveBlinkClock;
        var sharedColor = clock.ActiveColorClock;
        Assert.NotNull(shared); // non-vacuous: saat gerçekten kurulu
        Assert.NotNull(sharedColor);

        console.ShowReady(); // konsolda her görsel-satır değişiminde koşan yol
        RaiseStreamEvents(vm); // stream'de her olayda koşan yol

        Assert.Same(shared, clock.ActiveBlinkClock); // saat sıfırlanmadı: ritim kesilmedi
        Assert.Same(sharedColor, clock.ActiveColorClock); // renk turu da: imleç başına yeni renk saati kurulmadı
        Assert.Equal(2, clock.AttachedCount); // çiftlenmedi
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Hiding_the_window_releases_the_shared_clock_and_showing_it_brings_it_back()
    {
        var (console, stream, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        Assert.Equal(2, clock.AttachedCount); // ön-koşul

        window.Hide();

        Assert.Equal(0, clock.AttachedCount);
        Assert.Null(clock.ActiveBlinkClock); // bağlı imleç yok → saat yok
        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(stream.ActiveCursorGlyph.HasAnimatedProperties);

        window.Show();
        window.UpdateLayout();

        Assert.Equal(2, clock.AttachedCount);
        Assert.NotNull(clock.ActiveBlinkClock);
        Assert.True(Blinking(console.ActiveCursorGlyph));
        Assert.True(Blinking(stream.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Görünüm ağaçtan çıkınca (<c>Unloaded</c>) YALNIZ kendi imleci ayrılır: pencere o an artık çözülemez, bu yüzden
    /// ayrılma imleç→saat kaydından DOĞRU saati bulmak zorundadır. Konsol hâlâ bağlıyken ortak saat yaşar (yeniden
    /// kurulmaz); son imleç de ayrılınca saat durur.
    /// </summary>
    [StaFact]
    public void Removing_a_view_from_the_tree_detaches_only_its_own_cursor_and_the_last_one_stops_the_clock()
    {
        var (console, stream, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        var shared = clock.ActiveBlinkClock;
        Assert.Equal(2, clock.AttachedCount); // ön-koşul
        Assert.NotNull(shared);

        RemoveFromTree(stream);

        Assert.Equal(1, clock.AttachedCount); // yalnız akışın imleci ayrıldı
        Assert.Same(shared, clock.ActiveBlinkClock); // konsol bağlıyken ortak saat yaşar, yeniden kurulmadı
        Assert.True(Blinking(console.ActiveCursorGlyph));
        Assert.False(stream.ActiveCursorGlyph.HasAnimatedProperties);

        RemoveFromTree(console);

        Assert.Equal(0, clock.AttachedCount);
        Assert.Null(clock.ActiveBlinkClock); // son imleç ayrıldı → saat yok
        Assert.Null(clock.ActiveColorClock);
        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    /// <summary>Görünür davranış DEĞİŞMEDİ: pencere aktifken imleç eskisi gibi kırpar — aynı süre, aynı eğri, aynı
    /// kare hızı tavanı. Saat bunları <see cref="MotionTokens.CreateBlinkAnimation"/>'dan alır (tek kurucu); bu test
    /// kırpma saatinin gerçekten O animasyondan kurulduğunu pinler (eski, imleç başına saatlerin ritmi).</summary>
    [StaFact]
    public void The_shared_clock_keeps_the_blink_rhythm_of_the_per_cursor_clocks_it_replaced()
    {
        var (_, _, _, window) = RealizeBoth();
        var blink = CursorClock.For(window).ActiveBlinkClock?.Timeline as System.Windows.Media.Animation.DoubleAnimation;

        Assert.NotNull(blink);
        Assert.Equal(TimeSpan.FromMilliseconds(MotionTokens.BlinkMs), blink.Duration.TimeSpan);
        Assert.True(blink.AutoReverse);
        Assert.Equal(System.Windows.Media.Animation.RepeatBehavior.Forever, blink.RepeatBehavior);
        Assert.IsType<System.Windows.Media.Animation.SineEase>(blink.EasingFunction);
        Assert.Equal(MotionTokens.DecorativeFrameRate, System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(blink));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- pencere aktif değil → sabit

    [StaFact]
    public void An_inactive_window_leaves_both_cursors_steady_and_activating_it_resumes_them()
    {
        var (console, stream, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        Assert.True(Blinking(console.ActiveCursorGlyph) && Blinking(stream.ActiveCursorGlyph),
            "ön-koşul: aktif pencerede iki imleç GERÇEKTEN kırpmalı");

        clock.SetWindowActive(false); // başka pencere öne geldi / simge durumu / tepsi

        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(stream.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(CursorHop.IsRunning(console.ActiveCursorGlyph as Shape));
        Assert.False(CursorHop.IsRunning(stream.ActiveCursorGlyph as Shape));
        Assert.Equal(1.0, console.ActiveCursorGlyph.Opacity); // sönük karede donmaz: tam görünür
        Assert.Equal(1.0, stream.ActiveCursorGlyph.Opacity);
        Assert.Null(clock.ActiveBlinkClock);

        clock.SetWindowActive(true);

        Assert.True(Blinking(console.ActiveCursorGlyph));
        Assert.True(Blinking(stream.ActiveCursorGlyph));
        Assert.NotNull(clock.ActiveBlinkClock);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Cursor_work_while_the_window_is_inactive_does_not_restart_the_clock()
    {
        var (console, stream, vm, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        clock.SetWindowActive(false);
        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties); // non-vacuous: pasifken gerçekten durdu

        // Başlatıcıları yeniden çağıran üretim yolları — pasif pencerede de koşar (kapı başlatıcının kendisindedir).
        console.ShowReady();
        RaiseStreamEvents(vm);
        // Tepsiden dönüş: görünürlük yolu imleçleri YENİDEN bağlar, ama Activated henüz gelmemiştir.
        window.Hide();
        window.Show();
        window.UpdateLayout();

        Assert.Equal(2, clock.AttachedCount); // bağlı kaldılar...
        Assert.Null(clock.ActiveBlinkClock); // ...ama saat dönmüyor
        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(stream.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(CursorHop.IsRunning(console.ActiveCursorGlyph as Shape));
        Assert.False(CursorHop.IsRunning(stream.ActiveCursorGlyph as Shape));

        clock.SetWindowActive(true); // Activated geldi: bağlı imleçler kaldığı yerden değil, taze saatle döner

        Assert.True(Blinking(console.ActiveCursorGlyph));
        Assert.True(Blinking(stream.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- girdi yokken (perf B4 · karar 5)

    /// <summary>
    /// Üçüncü kapı: girdi yokken (<see cref="CaretIdleGate"/>, Windows'un imleç zaman aşımı) iki imleç de sabit durur — pasif
    /// pencereyle aynı duruş (opak, dinlenme rengi, saat ağaçtan çıkmış) — ve girdi gelince taze saatle, aynı fazda döner.
    /// <b>Neden (ÖLÇÜLDÜ):</b> ön planda etkin pencerede boşta 181 Mdöngü/s'in ~165'i imleç saatlerinin render döngüsünü
    /// ayakta tutmasıydı; kare hızı tek başına kaldıraç değildi (kırpma 15 fps: 180), imleçler durunca 17.
    /// </summary>
    [StaFact]
    public void Idle_input_leaves_both_cursors_steady_and_the_next_input_resumes_them()
    {
        var (console, stream, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        Assert.True(Blinking(console.ActiveCursorGlyph) && Blinking(stream.ActiveCursorGlyph),
            "ön-koşul: girdi varken iki imleç GERÇEKTEN kırpmalı");

        clock.SetInputIdle(true); // zaman aşımı doldu

        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(stream.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.False(CursorHop.IsRunning(console.ActiveCursorGlyph as Shape));
        Assert.Equal(1.0, console.ActiveCursorGlyph.Opacity);
        Assert.Null(clock.ActiveBlinkClock);
        Assert.Equal(2, clock.AttachedCount); // imleçler bağlı kalır, yalnız saat durur

        clock.SetInputIdle(false); // tuş / fare

        Assert.True(Blinking(console.ActiveCursorGlyph));
        Assert.True(Blinking(stream.ActiveCursorGlyph));
        Assert.NotNull(clock.ActiveBlinkClock);
        GC.KeepAlive(window);
    }

    /// <summary>Kapılar bağımsızdır: girdi gelse de pasif pencerede saat kurulmaz; aktifleşince (girdi sayılır) kurulur.</summary>
    [StaFact]
    public void Input_does_not_start_the_clock_while_the_window_is_inactive()
    {
        var (console, _, _, window) = RealizeBoth();
        var clock = CursorClock.For(window);
        clock.SetWindowActive(false);
        clock.SetInputIdle(true);

        clock.SetInputIdle(false); // girdi geldi ama pencere pasif

        Assert.Null(clock.ActiveBlinkClock);
        Assert.False(console.ActiveCursorGlyph.HasAnimatedProperties);

        clock.SetWindowActive(true);

        Assert.True(Blinking(console.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Pencerenin kablajı: girdi bildirimi yoklamayı kurar, zaman aşımı dolunca saat durur, yeni girdi geri getirir;
    /// aktifleşme girdi sayılır, pasifleşme yoklamayı bırakır. Zamanlayıcı sahte (gerçek bekleme yok), saat enjekte.
    /// </summary>
    [StaFact]
    public void The_main_window_stops_the_cursors_after_the_caret_timeout_without_input_and_resumes_on_input()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        var clock = CursorClock.For(window);
        var timer = new FakePollTimer();
        long now = 0;
        window.CaretIdleTimer = timer;
        window.CaretIdleClock = () => now;
        Assert.True(window.CaretIdleTimeout > TimeSpan.Zero); // ön-koşul: işletim sistemi değeri ya da varsayılan okundu

        window.NoteUserInput();
        Assert.True(timer.IsRunning);
        Assert.Equal(CaretIdleGate.PollInterval, timer.Interval);
        Assert.False(clock.InputIdle);

        now += (long)window.CaretIdleTimeout.TotalMilliseconds - 1;
        timer.Tick();
        Assert.False(clock.InputIdle);

        now += 1;
        timer.Tick();
        Assert.True(clock.InputIdle);   // zaman aşımı doldu: imleçler durdu
        Assert.False(timer.IsRunning);  // boştayken yoklama yok

        window.NoteUserInput();
        Assert.False(clock.InputIdle);  // girdi: imleçler döner
        Assert.True(timer.IsRunning);   // ve yoklama yeniden başlar

        RaiseWindowEvent(window, "OnDeactivated");
        Assert.False(timer.IsRunning);  // pasif pencerede yoklama yok (saat zaten aktiflik kapısında durur)

        now += (long)window.CaretIdleTimeout.TotalMilliseconds * 2;
        RaiseWindowEvent(window, "OnActivated"); // aktifleşme girdi sayılır
        Assert.False(clock.InputIdle);
        Assert.True(timer.IsRunning);
    }

    // ---------------------------------------------------------------- palet sonradan çözülür

    /// <summary>
    /// Saat kurulurken palet çözülemezse (görünüm henüz bir kaynak sözlüğüne bağlı değil) renk turu o an YOKTUR ama
    /// kırpma döner. Bu durum KALICI olmamalıdır: palet çözülür çözülmez ilk yeniden değerlendirmede tur kurulur ve
    /// çift BİRLİKTE yeniden başlar (renk saatini tek başına sonradan kurmak fazı kaydırırdı). Palet çözülemedikçe
    /// kırpma saati yeniden KURULMAZ — her olayda sıfırlamak imleci "takılı" gösterirdi.
    ///
    /// <para><b>Eski davranış:</b> ilk deneme boşa düşerse o çiftin ömrü boyunca renk turu hiç kurulmazdı.</para>
    /// </summary>
    [StaFact]
    public void A_color_tour_missed_for_want_of_a_palette_is_set_up_once_the_palette_resolves()
    {
        var host = new Border(); // kaynak sözlüğü YOK: palet çözülemez
        var cursor = new Rectangle { Width = 6, Height = 12 };
        var window = DsResources.Realize(host, cursor);
        var clock = CursorClock.For(window);
        Func<string> rest = () => "Brush.AmberText";

        CursorClock.Attach(cursor, host, rest);

        var blink = clock.ActiveBlinkClock;
        Assert.NotNull(blink); // ön-koşul: kırpma yine de döner
        Assert.Null(clock.ActiveColorClock); // ön-koşul: palet çözülemedi, renk saati yok
        Assert.False(CursorHop.IsRunning(cursor), "ön-koşul: palet çözülemedi, renk turu yok");
        CursorClock.Attach(cursor, host, rest); // yeniden değerlendirme — palet HÂLÂ yok
        Assert.Same(blink, clock.ActiveBlinkClock); // kırpma sıfırlanmadı

        host.Resources.MergedDictionaries.Add(DsResources.NewScope()); // palet artık çözülür
        CursorClock.Attach(cursor, host, rest); // bir sonraki olay / konsol tazelemesi

        Assert.True(CursorHop.IsRunning(cursor)); // tur kuruldu
        Assert.NotNull(clock.ActiveColorClock);
        Assert.NotSame(blink, clock.ActiveBlinkClock); // çift BİRLİKTE yeniden başladı (faz)
        Assert.Equal(1, clock.AttachedCount);

        var restarted = clock.ActiveBlinkClock;
        CursorClock.Attach(cursor, host, rest); // tur kurulduktan sonra yeniden başlamaz
        Assert.Same(restarted, clock.ActiveBlinkClock);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- pencere çözülemeyen öğe

    /// <summary>
    /// Bir pencerede olmayan öğenin imleci hiçbir saate KALICI bağlanmaz: pencere çözülemezse bağlama ertelenir ve
    /// öğe sonradan bir pencereye girince (sonraki görünürlük/olay) PENCERENİN saatine bağlanır.
    ///
    /// <para><b>Eski davranış:</b> pencere çözülemeyince öğenin kendisi anahtar olurdu ve imleç o yetim saate kalıcı
    /// bağlanırdı — pencerenin aktiflik sinyali ona hiç ulaşmazdı.</para>
    /// </summary>
    [StaFact]
    public void A_cursor_attached_before_its_host_is_in_a_window_ends_up_on_the_window_clock()
    {
        var host = DsResources.NewHost();
        var cursor = new Rectangle { Width = 6, Height = 12 };
        host.Child = cursor;
        CursorClock.Attach(cursor, host, () => "Brush.AmberText"); // henüz pencere yok: bağlanamaz, ertelenir
        Assert.False(cursor.HasAnimatedProperties);

        var window = DsResources.Realize(host, cursor); // öğe artık bir pencerede
        CursorClock.Attach(cursor, host, () => "Brush.AmberText"); // sonraki görünürlük / olay

        var clock = CursorClock.For(window);
        Assert.Equal(1, clock.AttachedCount); // yetim bir saate DEĞİL, pencerenin saatine bağlı
        Assert.True(Blinking(cursor));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- reduced-motion

    /// <summary>
    /// Hareket koşu SIRASINDA kapanırsa event stream'in imleci saatten ayrılır ve saat serbest kalır (başka imleç
    /// bağlı değilse durur).
    ///
    /// <para><b>Eski davranış:</b> motion-kapalı dalı yalnız opaklığı sıfırlardı; akışın renk turu dönmeye DEVAM
    /// ederdi. <b>Değişme gerekçesi:</b> reduced-motion sözleşmesi hiçbir sonsuz animasyona izin vermez ve konsol imleci
    /// zaten turu söküyordu (<c>ConsoleView.StopBlink</c>) — iki imleç aynı kuralı izlemeli. Tur sökülünce akıştaki ton
    /// kanalı (<c>RefreshCursorTone</c>) rengi devralır.</para>
    /// </summary>
    [StaFact]
    public void Turning_motion_off_detaches_the_cursor_and_releases_the_clock()
    {
        bool motion = true;
        var vm = HiddenCursorClockTests.NewVm();
        var stream = new EventStreamView { AnimationsEnabledProvider = () => motion, DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), stream);
        var clock = CursorClock.For(window);
        Assert.Equal(1, clock.AttachedCount); // ön-koşul: hareket açıkken imleç saate bağlı

        motion = false;
        RaiseStreamEvents(vm); // aktif satırı tazeler → başlatıcı sinyali TAZE okur

        Assert.Equal(0, clock.AttachedCount);
        Assert.Null(clock.ActiveBlinkClock);
        Assert.False(HiddenCursorClockTests.Ticking(stream.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- MainWindow kablajı

    /// <summary>Üretim sinyalinin tek kaynağı: pencerenin KENDİ Activated/Deactivated olayları. Olaylar
    /// <c>Window.OnActivated/OnDeactivated</c> korumalı yöntemleriyle ateşlenir; gerçek odak değişimi masaüstü
    /// etkin değilken güvenilmezdir, bu yüzden aynı yöntemler doğrudan çağrılır.</summary>
    [StaFact]
    public void The_main_window_tells_its_cursor_clock_when_it_is_deactivated_and_activated()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        var clock = CursorClock.For(window);
        Assert.True(clock.WindowActive); // varsayılan: sinyal gelmedikçe aktif sayılır

        RaiseWindowEvent(window, "OnDeactivated");
        Assert.False(clock.WindowActive);

        RaiseWindowEvent(window, "OnActivated");
        Assert.True(clock.WindowActive);
    }

    /// <summary>
    /// Pencere HİÇ aktifleşmeden gösterilebilir (foreground-lock, başka uygulama önde iken açılış, yeniden başlatma):
    /// <c>Deactivated</c> o zaman hiç gelmez ve saat varsayılan "aktif"te kalıp arka plandaki pencerede kırpardı.
    /// Pencere ilk gösterimde (içerik çizildiğinde) durumu KENDİ <c>IsActive</c>'inden bildirir; sonrası
    /// Activated/Deactivated olaylarının işidir. Hiç aktifleşmemiş bir <c>MainWindow</c> (<c>IsActive == false</c>) bu
    /// durumun eşdeğeridir; <c>ContentRendered</c> gerçek olayın yöntemiyle ateşlenir (bkz. önceki test).
    ///
    /// <para><b>Eski davranış:</b> ilk durum hiç bildirilmezdi — pencere aktifleşene dek imleç kırpardı.</para>
    /// </summary>
    [StaFact]
    public void A_window_shown_without_ever_being_activated_tells_its_cursor_clock_it_is_inactive()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        var clock = CursorClock.For(window);
        Assert.False(window.IsActive); // ön-koşul: hiç aktifleşmedi
        Assert.True(clock.WindowActive); // ön-koşul: henüz sinyal yok → varsayılan aktif

        RaiseWindowEvent(window, "OnContentRendered"); // ilk gösterim

        Assert.False(clock.WindowActive); // etkin olmayan pencerede imleçler sabit (bkz. inactive testleri)

        RaiseWindowEvent(window, "OnActivated"); // kullanıcı tıkladı / foreground-lock kalktı
        Assert.True(clock.WindowActive); // aktifleşince imleçler döner
    }

    private static void RaiseWindowEvent(Window window, string protectedRaiser) =>
        typeof(Window).GetMethod(protectedRaiser, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);
}
