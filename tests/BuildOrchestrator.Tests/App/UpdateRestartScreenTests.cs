using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using static BuildOrchestrator.Tests.App.DsResources;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan B6/U4] <c>Restart to update</c>'in ekranı (<c>UpdateRestart</c>,
/// BuildApp.jsx:1745-1786): pencerenin tamamını (title bar dahil) <c>surface-base</c> ile örter, 180ms'de belirir;
/// ortada 232px'lik bir kolon — marka 30px · 16 · <c>Updating &lt;ürün&gt;</c> (13/600 <c>text-primary</c>) · 6 · mono
/// 11px sürüm geçişi (kurulu <c>text-dim</c> → ok 11px <c>text-faint</c> → gelen <c>text-secondary</c>, aralar 7) · 20 ·
/// 2px amber ilerleme çubuğu · 9 · adım etiketi (11px <c>text-faint</c>, sonunda …). Tek adım (<c>Closing
/// &lt;ürün&gt;…</c>) <see cref="UpdateRestartTimeline"/>'dan; çubuk dolunca zamanlayıcı durur.
///
/// <para><b>[motor · Task 11 · K6] Ekran pencere kapanana dek kalır, sönüş yoktur:</b> <c>Restart to update</c>
/// uygulamayı güvenli tam çıkış yoluna sokar ve kurulumu çıkıştan sonra Update.exe penceresiz yapar
/// (<see cref="UpdateRestartFlowTests"/>). Eski iddia — ekran üç adımı oynar, bitişten 120ms sonra 280ms'de söner,
/// kalkar ve uygulama aynen kalır (motor yokken tasarımın önizlemesi, plan U4) — kurulum çalışan programın dosyalarını
/// değiştiremediği için değişti. Zaman tek bir dikişten gelir (enjekte zamanlayıcı + saat, D8): testler
/// <see cref="FakePollTimer"/> ile kare atar, gerçek zaman beklemez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UpdateRestartScreenTests
{
    /// <summary>Oynatmanın başladığı an (sahte saat, ms) — <see cref="RestartScreenTime"/>'ın.</summary>
    private const long StartMs = RestartScreenTime.StartMs;

    private sealed record Rig(UpdateRestartScreen Screen, RestartScreenTime Time, Window Window);

    /// <summary>Ekranı ekran dışı gerçek bir pencerede, sahte zamanlayıcı ve saatle kurar (henüz oynamaz).</summary>
    private static Rig NewRig(double width = 800, double height = 600)
    {
        var host = DsResources.NewHost();
        var screen = new UpdateRestartScreen();
        var time = new RestartScreenTime(screen);
        var window = DsResources.Realize(host, screen, width, height);
        return new Rig(screen, time, window);
    }

    private static void Play(Rig rig, string incoming = "1.8.0")
    {
        rig.Screen.Play(AppIdentity.Version, incoming);
        rig.Screen.UpdateLayout();
    }

    private static Color Token(FrameworkElement host, string key) => DsResources.TokenColor(host, key);

    // ================================================================ realize + ölçüler

    /// <summary>Ekran oynayana dek kapalıdır — pencerede hiçbir şeyi örtmez ve klavyeyi tutmaz.</summary>
    [StaFact]
    public void The_screen_stays_collapsed_until_it_plays()
    {
        var rig = NewRig();

        Assert.Equal(Visibility.Collapsed, rig.Screen.Visibility);
        Assert.False(rig.Screen.IsShowing);
        Assert.False(rig.Time.Timer.IsRunning);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>[realize] Kolon tasarımın sayılarıyla çizilir: zemin <c>surface-base</c>; 232px ortalı kolon; marka 30px;
    /// başlık 16px altında 13/600 <c>text-primary</c>; 6px altında mono 11px geçiş (kurulu <c>text-dim</c> · 7 · ok 11px
    /// <c>Icon.ArrowRight</c> <c>text-faint</c> · 7 · gelen <c>text-secondary</c>); 20px altında 2px amber
    /// <c>Ds.ProgressBar</c> (kolon genişliğinde); 9px altında 11px <c>text-faint</c> adım etiketi. Token tipleri doğru
    /// çözülür.</summary>
    [StaFact]
    public void The_column_is_drawn_to_the_design_numbers()
    {
        var rig = NewRig();
        Play(rig);
        var screen = rig.Screen;

        Assert.Equal(Token(screen, "Brush.SurfaceBase"), DsResources.ColorOf(screen.Background));

        var column = screen.PART_Column;
        Assert.Equal(232.0, column.ActualWidth);
        var columnBounds = BoundsIn(column, screen);
        Assert.Equal(screen.ActualWidth / 2, columnBounds.Left + columnBounds.Width / 2, precision: 1);
        Assert.Equal(screen.ActualHeight / 2, columnBounds.Top + columnBounds.Height / 2, precision: 1);

        var mark = screen.PART_Mark;
        Assert.Equal(30.0, mark.Height);
        var markBounds = BoundsIn(mark, column);
        Assert.Equal(116.0, markBounds.Left + markBounds.Width / 2, precision: 1); // ortalı

        var heading = screen.PART_Heading;
        Assert.Equal(UpdateText.RestartHeading, heading.Text);
        Assert.Equal(13.0, heading.FontSize);
        Assert.Equal(FontWeights.SemiBold, heading.FontWeight);
        Assert.Equal(Token(screen, "Brush.TextPrimary"), DsResources.ColorOf(heading.Foreground));
        Assert.Equal(16.0, BoundsIn(heading, column).Top - markBounds.Bottom, precision: 1);

        var installed = screen.PART_Installed;
        Assert.Equal(AppIdentity.Version, installed.Text);
        Assert.Equal(AppFonts.Mono, installed.FontFamily);
        Assert.Equal(11.0, installed.FontSize);
        Assert.Equal(Token(screen, "Brush.TextDim"), DsResources.ColorOf(installed.Foreground));

        var arrow = screen.PART_Arrow;
        Assert.Equal((11.0, 11.0), (arrow.Width, arrow.Height));
        var glyph = screen.PART_ArrowPath;
        Assert.Same(screen.FindResource("Icon.ArrowRight"), glyph.Data);
        Assert.Equal((double)screen.FindResource("Icon.ArrowRight.StrokeThickness"), glyph.StrokeThickness);
        Assert.Equal(Token(screen, "Brush.TextFaint"), DsResources.ColorOf(glyph.Stroke));

        var incoming = screen.PART_Incoming;
        Assert.Equal("1.8.0", incoming.Text);
        Assert.Equal(AppFonts.Mono, incoming.FontFamily);
        Assert.Equal(11.0, incoming.FontSize);
        Assert.Equal(Token(screen, "Brush.TextSecondary"), DsResources.ColorOf(incoming.Foreground));

        var transition = screen.PART_Transition;
        Assert.Equal(7.0, BoundsIn(arrow, transition).Left - BoundsIn(installed, transition).Right, precision: 1);
        Assert.Equal(7.0, BoundsIn(incoming, transition).Left - BoundsIn(arrow, transition).Right, precision: 1);
        var transitionBounds = BoundsIn(transition, column);
        Assert.Equal(116.0, transitionBounds.Left + transitionBounds.Width / 2, precision: 1); // ortalı
        Assert.Equal(6.0, transitionBounds.Top - BoundsIn(heading, column).Bottom, precision: 1);

        var bar = screen.PART_Progress;
        Assert.Same(screen.FindResource("Ds.ProgressBar"), bar.Style);
        Assert.Equal((double)screen.FindResource("Size.ProgressHeight"), bar.ActualHeight);
        Assert.Equal(2.0, bar.ActualHeight);
        Assert.Equal(232.0, bar.ActualWidth);
        Assert.Equal((0.0, 100.0), (bar.Minimum, bar.Maximum));
        Assert.Equal(Token(screen, "Brush.Amber"), DsResources.ColorOf(bar.Foreground));
        Assert.Equal(20.0, BoundsIn(bar, column).Top - transitionBounds.Bottom, precision: 1);

        var step = screen.PART_Step;
        Assert.Equal(11.0, step.FontSize);
        Assert.Equal(Token(screen, "Brush.TextFaint"), DsResources.ColorOf(step.Foreground));
        Assert.Equal(9.0, BoundsIn(step, column).Top - BoundsIn(bar, column).Bottom, precision: 1);
        var stepBounds = BoundsIn(step, column);
        Assert.Equal(116.0, stepBounds.Left + stepBounds.Width / 2, precision: 1); // ortalı

        Assert.Empty(DsResources.DynamicResourceTypeMismatches(screen));
        GC.KeepAlive(rig.Window);
    }

    /// <summary>[§2.12 · §14.2] Başlık ve adım etiketi arayüz yazı tipiyle (<c>Geist</c>, <see cref="AppFonts.Ui"/>)
    /// çizilir — prototipte ekranın kökü <c>font-sans</c>'tır; sürüm geçişi makine çıktısıdır, <see cref="AppFonts.Mono"/>'da
    /// kalır.
    /// <para><b>Ölçülen kusur:</b> kök yazı tipi vermiyordu ve ekranın hiçbir atası arayüz yazı tipini taşımıyordu
    /// (pencerenin yazı tipi sistem varsayılanıdır) — <c>Updating &lt;ürün&gt;</c> ve adım etiketi Segoe UI
    /// çiziliyordu.</para></summary>
    [StaFact]
    public void The_heading_and_the_step_label_are_drawn_in_the_ui_font()
    {
        var rig = NewRig();
        Play(rig);
        var screen = rig.Screen;

        Assert.Equal(AppFonts.Ui, screen.PART_Heading.FontFamily);
        Assert.Equal(AppFonts.Ui, screen.PART_Step.FontFamily);
        Assert.Equal(AppFonts.Mono, screen.PART_Installed.FontFamily);
        Assert.Equal(AppFonts.Mono, screen.PART_Incoming.FontFamily);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ zaman çizelgesi (sahte zamanlayıcı)

    /// <summary>Oynatma tek bir kare zamanlayıcısıyla sürülür (aralığı <see cref="UpdateRestartScreen.FrameMs"/>); her
    /// karede saat okunur, etiket ve çubuk <see cref="UpdateRestartTimeline"/>'a göre yazılır: tek adım, çubuk 0 → 100.
    /// <para>Eski iddia: üç adım, adım sınırlarında etiket değişirdi (Closing → Installing → Starting) — K6 ile tek adım
    /// kaldı.</para></summary>
    [StaFact]
    public void The_step_label_and_the_bar_follow_the_timeline_frame_by_frame()
    {
        var rig = NewRig();
        Play(rig);
        var (screen, timer) = (rig.Screen, rig.Time.Timer);

        Assert.True(screen.IsShowing);
        Assert.Equal(Visibility.Visible, screen.Visibility);
        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(UpdateRestartScreen.FrameMs), timer.Interval);
        AssertFrame(0);

        rig.Time.FrameAt(200);
        AssertFrame(25);
        rig.Time.FrameAt(400);
        AssertFrame(50);
        rig.Time.FrameAt(799);
        AssertFrame(99.875);
        rig.Time.FrameAt(800);
        AssertFrame(100);
        GC.KeepAlive(rig.Window);

        void AssertFrame(double expectedPercent)
        {
            Assert.Equal(UpdateText.RestartStepLabel(UpdateRestartStep.Closing), screen.PART_Step.Text);
            Assert.Equal(expectedPercent, screen.PART_Progress.Value, precision: 6);
        }
    }

    /// <summary>Çubuk dolunca (<see cref="UpdateRestartTimeline.TotalMs"/>) zamanlayıcı durur ama ekran KALIR — sönüş
    /// yoktur, pencereyi güvenli tam çıkış kapatır. Çıkış gecikse de (ör. 5 s) ekran dolu çubukla görünür durur.
    /// <para>Eski iddia: bitişten 120ms sonra ekran söner ve kalkardı (uygulama aynen kalırdı) — K6 ile ekran pencere
    /// kapanana dek kalır.</para></summary>
    [StaFact]
    public void The_screen_stays_until_the_process_exits_and_stops_its_timer()
    {
        var rig = NewRig();
        Play(rig);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs - 1);
        Assert.True(rig.Screen.IsShowing);
        Assert.True(rig.Time.Timer.IsRunning);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs);
        Assert.False(rig.Time.Timer.IsRunning);
        Assert.True(rig.Screen.IsShowing);
        Assert.Equal(100.0, rig.Screen.PART_Progress.Value);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs + 5000);
        Assert.True(rig.Screen.IsShowing);
        Assert.Equal(Visibility.Visible, rig.Screen.Visibility);
        Assert.False(rig.Time.Timer.IsRunning);
        Assert.Equal(100.0, rig.Screen.PART_Progress.Value);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>[motor · Task 11 · fix-1] Çubuk dolduğu kare ekran bunu BİR kez bildirir (<see cref="UpdateRestartScreen.BarFilled"/>
    /// — kabuk o anda güvenli tam çıkışı ister, <see cref="UpdateRestartFlowTests"/>): dolmadan önce hiç, dolduktan
    /// sonraki karelerde ve çubuk doluyken gelen ikinci bir oynatma isteğinde bir daha değil.</summary>
    [StaFact]
    public void When_the_bar_fills_the_screen_reports_it_once()
    {
        var rig = NewRig();
        int fills = 0;
        rig.Screen.BarFilled += () => fills++;
        Play(rig);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs - 1);
        Assert.Equal(0, fills);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs);
        Assert.Equal(1, fills);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs + 5000);
        Play(rig, "2.0.0");
        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs + 6000);
        Assert.Equal(1, fills);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Oynarken gelen ikinci bir istek çizelgeyi baştan başlatmaz.</summary>
    [StaFact]
    public void A_second_play_while_showing_keeps_the_running_timeline()
    {
        var rig = NewRig();
        Play(rig);
        rig.Time.FrameAt(400);

        rig.Time.Now = StartMs + 450;
        rig.Screen.Play(AppIdentity.Version, "9.9.0");
        rig.Time.FrameAt(400);

        Assert.Equal(50.0, rig.Screen.PART_Progress.Value, precision: 6);
        Assert.Equal("1.8.0", rig.Screen.PART_Incoming.Text);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Çubuk dolduktan sonra gelen bir istek de ekranı baştan oynatmaz: ekran görünür kalır, çubuk dolu, sürüm
    /// ve durmuş zamanlayıcı yerinde.
    /// <para>Eski iddia (<c>After_it_leaves_the_screen_can_play_again_from_the_start</c>): bir oynatma bitince ekran
    /// kalkar, yeniden baştan ve yeni sürümle oynayabilirdi. K6 ile ekran kalkmaz — pencere kapanır; görünürken gelen
    /// isteğin yok sayılması çubuk dolduktan sonra da geçerlidir.</para></summary>
    [StaFact]
    public void After_the_bar_fills_a_second_play_keeps_the_screen_as_it_is()
    {
        var rig = NewRig();
        Play(rig);
        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs);
        Assert.False(rig.Time.Timer.IsRunning); // ön-koşul: çubuk doldu

        rig.Time.Now = StartMs;
        Play(rig, "2.0.0");

        Assert.True(rig.Screen.IsShowing);
        Assert.Equal(100.0, rig.Screen.PART_Progress.Value);
        Assert.Equal("1.8.0", rig.Screen.PART_Incoming.Text);
        Assert.False(rig.Time.Timer.IsRunning);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ ekran okuyucu

    /// <summary><c>role="status" aria-live="polite"</c>: adım etiketi sakin bir canlı bölgedir ve adım BİR KEZ
    /// duyurulur — kare başına değil (tek adım, tek duyuru).
    /// <para>Eski iddia: üç adım, üç duyuru — K6 ile tek adım kaldı.</para></summary>
    [StaFact]
    public void Each_step_is_announced_once_to_a_screen_reader()
    {
        var rig = NewRig();
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(rig.Screen.PART_Step));

        Play(rig);
        Assert.Equal(1, rig.Screen.StepAnnouncements);
        foreach (double at in new[] { 100.0, 400, 799, 800, 1200, 1900, 2700, 2800 })
            rig.Time.FrameAt(at);

        Assert.Equal(1, rig.Screen.StepAnnouncements);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ hareket

    /// <summary>Hareket açıkken ekran <c>Duration.Base</c>'te (180ms) belirir; çubuk dolduktan sonra da görünür,
    /// tıklanabilir (arkadaki uygulamaya tıklama geçirmez) ve tam opak kalır — çıkış hareketi yoktur.
    /// <para>Eski iddia (<c>…_and_out_over_the_slow_one</c>): bitişte <c>Duration.Slow</c>'da (280ms) sönerdi, sönerken
    /// tıklamaları geçirirdi ve sönüş bitince kalkardı; sönüşün görünür ekrandan başladığı da (ölçülen kusur: giriş
    /// animasyonu silinince opaklık tabana, 0'a düşüyordu) burada pinliydi. K6 ile sönüş kalktı: ekranı pencerenin
    /// kapanışı kaldırır.</para></summary>
    [StaFact]
    public void With_motion_on_it_fades_in_over_the_base_duration()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var rig = NewRig();
        var screen = rig.Screen;
        Assert.Equal(((Duration)screen.FindResource("Duration.Base")).TimeSpan, PopIn.FadeInDuration(screen));
        Assert.Equal(TimeSpan.FromMilliseconds(180), PopIn.FadeInDuration(screen));

        Play(rig);
        Assert.True(screen.HasAnimatedProperties, "ekran belirerek girmedi");
        DispatcherPump.PumpUntil(() => screen.Opacity >= 1.0, TimeSpan.FromSeconds(3));
        Assert.Equal(1.0, screen.Opacity, precision: 3);

        rig.Time.FrameAt(UpdateRestartTimeline.TotalMs + 5000);
        Assert.True(screen.IsShowing, "çubuk dolunca ekran kalktı");
        Assert.True(screen.IsHitTestVisible, "çubuk dolunca ekran tıklamaları arkaya geçirdi");
        Assert.Equal(1.0, screen.Opacity, precision: 3);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ kabuk: katman, istek

    /// <summary>Realize edilmiş kabuk + iki projeli, boşta bir workspace ve kuruluma hazır bir teklif
    /// (<see cref="UpdateOffers.Sample"/> — uygulama teklifsiz açılır); motora giden komutlar yakalanır, restart
    /// ekranının zamanı sahtedir.</summary>
    private sealed record ShellRig(MainWindow Window, RunViewModel Vm, RestartScreenTime Time, List<IpcCommand> Sent)
    {
        public UpdateRestartScreen Screen => Window.UpdateRestartOverlay;

        /// <summary>Kartın <c>Restart to update</c>'ine kullanıcı gibi basar (kapıdan geçer).</summary>
        public void PressRestart() => Assert.True(CommandPress.Press(Vm.RestartToUpdateCommand));

        public IEnumerable<StartRunCommand> Runs => Sent.OfType<StartRunCommand>();
    }

    private static ShellRig NewShell(TempDir temp)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("A", null), ("B", null));
        vm.AvailableUpdate = UpdateOffers.Sample();
        MainWindowHost.AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        return new ShellRig(window, vm, new RestartScreenTime(window.UpdateRestartOverlay), sent);
    }

    /// <summary>Ekran pencerenin EN ÜST katmanıdır (modalların da üstünde, XAML'de son), iki satırı da örter (title bar
    /// dahil) ve caption bandında da tıklamayı kendisi alır — sürükleme ve pencere düğmeleri altında kalır. Varsayılan
    /// kapalıdır: kapalıyken şablonu açılmaz ve title bar'ın markası pencerede TEK kalır (<c>TitleBarContextTests</c>
    /// markayı <c>.Single()</c> ile bulur).</summary>
    [StaFact]
    public void The_screen_is_the_topmost_layer_over_the_title_bar_and_starts_collapsed()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        var screen = window.UpdateRestartOverlay;

        var layers = (Grid)window.RootShell.Child;
        Assert.Same(screen, layers.Children[^1]);
        Assert.Equal((0, 2), (Grid.GetRow(screen), Grid.GetRowSpan(screen)));
        Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(screen));
        Assert.Equal(Visibility.Collapsed, screen.Visibility);
        Assert.Single(DsResources.Descendants(window.RootShell).OfType<AppMark>());
        GC.KeepAlive(window);
    }

    /// <summary><c>Restart to update</c> kartı kapatır ve ekranı oynatır: kurulu sürüm → teklifin sürümü. Ardından
    /// gelen güvenli tam çıkış <see cref="UpdateRestartFlowTests"/>'tedir.</summary>
    [StaFact]
    public void Restart_to_update_closes_the_card_and_plays_the_screen_into_the_offered_version()
    {
        using var temp = new TempDir();
        var rig = NewShell(temp);
        rig.Window.UpdatePill.IsChecked = true;

        rig.PressRestart();

        Assert.False(rig.Window.UpdatePill.IsChecked);
        Assert.True(rig.Screen.IsShowing);
        Assert.Equal(AppIdentity.Version, rig.Screen.PART_Installed.Text);
        Assert.Equal(rig.Vm.AvailableUpdate!.Version, rig.Screen.PART_Incoming.Text);
        Assert.Equal(UpdateText.RestartStepLabel(UpdateRestartStep.Closing), rig.Screen.PART_Step.Text);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ kabuk: klavye

    /// <summary>Tuş olayları için bir girdi kaynağı — <see cref="KeyEventArgs"/> bir <see cref="PresentationSource"/>
    /// ister ve kabuk testlerinde pencere gösterilmez; ekran dışı küçük bir pencere yeter (olayın hedefi yine
    /// ana penceredir).</summary>
    private static (PresentationSource source, Window keepAlive) KeySource()
    {
        var anchor = new Border();
        var window = DsResources.Realize(DsResources.NewHost(), anchor);
        return (PresentationSource.FromVisual(anchor)!, window);
    }

    /// <summary>Bir tuşa WPF'in girdi yöneticisi gibi basar: önce tünelleyen <c>PreviewKeyDown</c>, sonra AYNI argümanla
    /// kabarcıklanan <c>KeyDown</c> — pencerenin <see cref="KeyBinding"/>'leri ikincisinde, yalnız olay handled
    /// değilse çalışır.</summary>
    private static KeyEventArgs PressKey(MainWindow window, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(args);
        args.RoutedEvent = Keyboard.KeyDownEvent;
        window.RaiseEvent(args);
        return args;
    }

    /// <summary>Ekran görünürken pencere klavyeyi yok sayar (prototip: keydown'da erken dönüş): F5 derleme başlatmaz, Esc
    /// seçimi temizlemez. Kontrol ekran açılmadan ÖNCE yapılır: aynı Esc seçimi temizler — tuş yolunun gerçekten bağlama
    /// ulaştığının kanıtı.
    /// <para>Eski iddia: kontrol ekran kalktıktan SONRA yapılırdı (Esc ve F5 yeniden çalışırdı). K6 ile ekran kalkmaz —
    /// pencere kapanır; kontrol Esc için öne alındı (F5 önceden basılsaydı başlayan derleme Restart'ı kilitlerdi).</para></summary>
    [StaFact]
    public void The_window_ignores_the_keyboard_while_the_screen_shows()
    {
        using var temp = new TempDir();
        var rig = NewShell(temp);
        var (source, keepAlive) = KeySource();
        rig.Vm.SelectProject(MainWindowHost.IdOf("A"));
        PressKey(rig.Window, source, Key.Escape);
        Assert.Null(rig.Vm.SelectedProjectId); // kontrol: ekran yokken Esc bağlamaya ulaşır
        rig.Vm.SelectProject(MainWindowHost.IdOf("A"));

        rig.PressRestart();
        var f5 = PressKey(rig.Window, source, Key.F5);
        var esc = PressKey(rig.Window, source, Key.Escape);

        Assert.True(f5.Handled);
        Assert.True(esc.Handled);
        Assert.Empty(rig.Runs);
        Assert.False(rig.Vm.IsStarting);
        Assert.Equal(MainWindowHost.IdOf("A"), rig.Vm.SelectedProjectId);
        GC.KeepAlive(rig.Window);
        GC.KeepAlive(keepAlive);
    }

    /// <summary>Ekran görünürken global kısayollar da yok sayılır — Ctrl+Shift+Space arka planda derleme başlatmaz.
    /// Kontrol ekransız ikinci bir kabukta yapılır: aynı kısayol orada derlemeyi başlatır.
    /// <para>Eski iddia: kontrol ekran kalktıktan SONRA aynı kabukta yapılırdı. K6 ile ekran kalkmaz — pencere kapanır;
    /// aynı kabukta önceden basılan kısayol başlattığı derlemeyle Restart'ı kilitlerdi.</para></summary>
    [StaFact]
    public void Global_hotkeys_are_ignored_while_the_screen_shows()
    {
        using var temp = new TempDir();
        var rig = NewShell(temp);
        using var controlTemp = new TempDir();
        var control = NewShell(controlTemp);
        control.Window.OnGlobalHotkey(GlobalHotkeyAction.Build);
        Assert.True(control.Vm.IsStarting || control.Runs.Any(), "kontrol: ekran yokken kısayol derlemeyi başlatmadı");

        rig.PressRestart();
        rig.Window.OnGlobalHotkey(GlobalHotkeyAction.Build);

        Assert.Empty(rig.Runs);
        Assert.False(rig.Vm.IsStarting);
        GC.KeepAlive(rig.Window);
        GC.KeepAlive(control.Window);
    }
}
