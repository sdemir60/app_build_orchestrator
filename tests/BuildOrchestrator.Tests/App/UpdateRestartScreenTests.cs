using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan B6/U4] <c>Restart to update</c>'in ekranı (<c>UpdateRestart</c>,
/// BuildApp.jsx:1745-1786): pencerenin tamamını (title bar dahil) <c>surface-base</c> ile örter, 180ms'de belirir;
/// ortada 232px'lik bir kolon — marka 30px · 16 · <c>Updating &lt;ürün&gt;</c> (13/600 <c>text-primary</c>) · 6 · mono
/// 11px sürüm geçişi (kurulu <c>text-dim</c> → ok 11px <c>text-faint</c> → gelen <c>text-secondary</c>, aralar 7) · 20 ·
/// 2px amber ilerleme çubuğu · 9 · adım etiketi (11px <c>text-faint</c>, sonunda …). Adımlar
/// <see cref="UpdateRestartTimeline"/>'dan; bitişten 120ms sonra 280ms'de söner ve kalkar.
///
/// <para><b>Güncelleme motoru henüz yok:</b> ekran tasarımın önizlemesidir — oynar, söner ve uygulama aynen kalır
/// (plan U4). Zaman tek bir dikişten gelir (enjekte zamanlayıcı + saat, D8): testler <see cref="FakePollTimer"/> ile
/// kare atar, gerçek zaman beklemez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UpdateRestartScreenTests
{
    /// <summary>Oynatmanın başladığı an (sahte saat, ms).</summary>
    private const long StartMs = 10_000;

    /// <summary>Sahte saat — ekranın <c>NowMs</c>'i bunu okur.</summary>
    private sealed class Clock
    {
        public long Now = StartMs;
    }

    private sealed record Rig(UpdateRestartScreen Screen, FakePollTimer Timer, Clock Clock, Window Window)
    {
        /// <summary>Saati oynatmanın başından <paramref name="elapsedMs"/> sonrasına alır ve bir kare atar.</summary>
        public void FrameAt(double elapsedMs)
        {
            Clock.Now = StartMs + (long)elapsedMs;
            Timer.Tick();
        }
    }

    /// <summary>Ekranı ekran dışı gerçek bir pencerede, sahte zamanlayıcı ve saatle kurar (henüz oynamaz).</summary>
    private static Rig NewRig(double width = 800, double height = 600)
    {
        var host = DsResources.NewHost();
        var screen = new UpdateRestartScreen();
        var timer = new FakePollTimer();
        var clock = new Clock();
        screen.Timer = timer;
        screen.NowMs = () => clock.Now;
        var window = DsResources.Realize(host, screen, width, height);
        return new Rig(screen, timer, clock, window);
    }

    private static void Play(Rig rig, string incoming = "1.8.0")
    {
        rig.Screen.Play(AppIdentity.Version, incoming);
        rig.Screen.UpdateLayout();
    }

    private static Rect BoundsIn(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static Color Token(FrameworkElement host, string key) => DsResources.TokenColor(host, key);

    // ================================================================ realize + ölçüler

    /// <summary>Ekran oynayana dek kapalıdır — pencerede hiçbir şeyi örtmez ve klavyeyi tutmaz.</summary>
    [StaFact]
    public void The_screen_stays_collapsed_until_it_plays()
    {
        var rig = NewRig();

        Assert.Equal(Visibility.Collapsed, rig.Screen.Visibility);
        Assert.False(rig.Screen.IsShowing);
        Assert.False(rig.Timer.IsRunning);
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

    // ================================================================ zaman çizelgesi (sahte zamanlayıcı)

    /// <summary>Oynatma tek bir kare zamanlayıcısıyla sürülür (aralığı <see cref="UpdateRestartScreen.FrameMs"/>); her
    /// karede saat okunur, etiket ve çubuk <see cref="UpdateRestartTimeline"/>'a göre yazılır — adım sınırlarında ve adım
    /// ortalarında.</summary>
    [StaFact]
    public void The_step_label_and_the_bar_follow_the_timeline_frame_by_frame()
    {
        var rig = NewRig();
        Play(rig);
        var (screen, timer) = (rig.Screen, rig.Timer);

        Assert.True(screen.IsShowing);
        Assert.Equal(Visibility.Visible, screen.Visibility);
        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(UpdateRestartScreen.FrameMs), timer.Interval);
        AssertFrame(UpdateRestartStep.Closing, 0);

        rig.FrameAt(400);
        AssertFrame(UpdateRestartStep.Closing, 10);
        rig.FrameAt(800);
        AssertFrame(UpdateRestartStep.Installing, 20);
        rig.FrameAt(1350);
        AssertFrame(UpdateRestartStep.Installing, 49);
        rig.FrameAt(1900);
        AssertFrame(UpdateRestartStep.Starting, 78);
        rig.FrameAt(2300);
        AssertFrame(UpdateRestartStep.Starting, 89);
        rig.FrameAt(2700);
        AssertFrame(UpdateRestartStep.Starting, 100);
        GC.KeepAlive(rig.Window);

        void AssertFrame(UpdateRestartStep expectedStep, double expectedPercent)
        {
            Assert.Equal(UpdateText.RestartStepLabel(expectedStep, "1.8.0"), screen.PART_Step.Text);
            Assert.Equal(expectedPercent, screen.PART_Progress.Value, precision: 6);
        }
    }

    /// <summary>Bitişten 120ms sonra ekran söner ve kalkar; zamanlayıcı durur. Hareket kapalıyken (headless) sönüş
    /// anındadır — ekran aynı karede kalkar.</summary>
    [StaFact]
    public void The_screen_leaves_120ms_after_the_last_step_and_stops_its_timer()
    {
        var rig = NewRig();
        Play(rig);

        rig.FrameAt(UpdateRestartTimeline.FadeOutAtMs - 1);
        Assert.True(rig.Screen.IsShowing);
        Assert.True(rig.Timer.IsRunning);

        rig.FrameAt(UpdateRestartTimeline.FadeOutAtMs);
        Assert.Equal(Visibility.Collapsed, rig.Screen.Visibility);
        Assert.False(rig.Screen.IsShowing);
        Assert.False(rig.Timer.IsRunning);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Oynarken gelen ikinci bir istek çizelgeyi baştan başlatmaz.</summary>
    [StaFact]
    public void A_second_play_while_showing_keeps_the_running_timeline()
    {
        var rig = NewRig();
        Play(rig);
        rig.FrameAt(1350);

        rig.Clock.Now = StartMs + 1400;
        rig.Screen.Play(AppIdentity.Version, "9.9.0");
        rig.FrameAt(1350);

        Assert.Equal(49.0, rig.Screen.PART_Progress.Value, precision: 6);
        Assert.Equal("1.8.0", rig.Screen.PART_Incoming.Text);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Bir oynatma bittikten sonra ekran yeniden oynayabilir — baştan, yeni sürümle.</summary>
    [StaFact]
    public void After_it_leaves_the_screen_can_play_again_from_the_start()
    {
        var rig = NewRig();
        Play(rig);
        rig.FrameAt(UpdateRestartTimeline.FadeOutAtMs);
        Assert.False(rig.Screen.IsShowing); // ön-koşul

        rig.Clock.Now = StartMs;
        Play(rig, "2.0.0");

        Assert.True(rig.Screen.IsShowing);
        Assert.Equal(0.0, rig.Screen.PART_Progress.Value);
        Assert.Equal(UpdateText.RestartStepLabel(UpdateRestartStep.Closing, "2.0.0"), rig.Screen.PART_Step.Text);
        Assert.Equal(1.0, rig.Screen.Opacity);
        Assert.True(rig.Screen.IsHitTestVisible);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ ekran okuyucu

    /// <summary><c>role="status" aria-live="polite"</c>: adım etiketi sakin bir canlı bölgedir ve her adım BİR KEZ
    /// duyurulur — kare başına değil (üç adım, üç duyuru).</summary>
    [StaFact]
    public void Each_step_is_announced_once_to_a_screen_reader()
    {
        var rig = NewRig();
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(rig.Screen.PART_Step));

        Play(rig);
        Assert.Equal(1, rig.Screen.StepAnnouncements);
        foreach (double at in new[] { 100.0, 400, 799, 800, 1200, 1899, 1900, 2500, 2700, 2800 })
            rig.FrameAt(at);

        Assert.Equal(3, rig.Screen.StepAnnouncements);
        GC.KeepAlive(rig.Window);
    }

    // ================================================================ hareket

    /// <summary>Hareket açıkken ekran <c>Duration.Base</c>'te (180ms) belirir ve bitişte <c>Duration.Slow</c>'da (280ms)
    /// söner; sönerken tıklamaları geçirir (<c>pointer-events: none</c>), sönüş bitince kalkar ve opaklığı bir sonraki
    /// oynatma için geri gelir.</summary>
    [StaFact]
    public void With_motion_on_it_fades_in_over_the_base_duration_and_out_over_the_slow_one()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var rig = NewRig();
        var screen = rig.Screen;
        Assert.Equal(((Duration)screen.FindResource("Duration.Base")).TimeSpan, PopIn.FadeInDuration(screen));
        Assert.Equal(TimeSpan.FromMilliseconds(180), PopIn.FadeInDuration(screen));
        Assert.Equal(((Duration)screen.FindResource("Duration.Slow")).TimeSpan, UpdateRestartScreen.FadeOutDuration(screen));
        Assert.Equal(TimeSpan.FromMilliseconds(280), UpdateRestartScreen.FadeOutDuration(screen));

        Play(rig);
        Assert.True(screen.HasAnimatedProperties, "ekran belirerek girmedi");
        DispatcherPump.PumpUntil(() => screen.Opacity >= 1.0, TimeSpan.FromSeconds(3));
        Assert.Equal(1.0, screen.Opacity, precision: 3);

        rig.FrameAt(UpdateRestartTimeline.FadeOutAtMs);
        Assert.True(screen.IsShowing, "sönüş beklenmeden kalktı");
        Assert.False(screen.IsHitTestVisible);
        Assert.True(screen.HasAnimatedProperties, "ekran sönerek çıkmadı");

        DispatcherPump.PumpUntil(() => screen.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));
        Assert.Equal(Visibility.Collapsed, screen.Visibility);
        Assert.False(screen.HasAnimatedProperties);
        Assert.Equal(1.0, screen.Opacity);
        GC.KeepAlive(rig.Window);
    }
}
