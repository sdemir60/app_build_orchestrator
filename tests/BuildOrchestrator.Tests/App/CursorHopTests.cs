using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
/// [design v1.12.1 §2.5 · §2.6] <b>İmleç her kırpmanın dibinde rengini değiştirir.</b> Kırpma değişmedi
/// (1.1s, opacity 1 → 0.1 → 1); üstüne 6.6s'lik, <b>kesikli</b> altı adımlı bir renk turu bindi:
/// cmd → info → success → warn → error → dim. Faz −0.55s'tir, yani renk tam kırpmanın DİBİNDE atlar ve
/// geçiş görünmez — imleç her yanışta yeni renkle gelir.
///
/// <para>Renkler konsolun KENDİ satır paletindendir (<see cref="ConsolePalette.Keys"/>): imleç, bir konsol
/// satırının taşıyamayacağı hiçbir rengi almaz.</para>
///
/// <para>İki imleç de AYNI bileşendir (konsol prompt'u + event stream'in canlı satırı), bu yüzden ikisi de
/// aynı turu döner — tek uygulama, iki yüzey.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class CursorHopTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1")
        { RootPath = @"D:\repo" };

    private static ConsoleView RealizeConsole(bool motion, out Window window)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => motion };
        window = DsResources.Realize(DsResources.NewHost(), view);
        return view;
    }

    private static EventStreamView RealizeStream(bool motion, RunViewModel vm, out Window window)
    {
        var view = new EventStreamView { AnimationsEnabledProvider = () => motion, DataContext = vm };
        window = DsResources.Realize(DsResources.NewHost(), view);
        return view;
    }

    /// <summary>Konsol prompt'unun imleci: motion açıkken rengi YEREL bir fırçadan gelir ve o fırça
    /// animasyonludur — token referansında sabit duran bir imleç turu dönemezdi.</summary>
    [StaFact]
    public void The_console_prompt_cursor_hops_through_the_line_palette()
    {
        var view = RealizeConsole(motion: true, out var window);

        var fill = ((Rectangle)view.ActiveCursorGlyph).Fill;
        Assert.True(fill is SolidColorBrush { HasAnimatedProperties: true },
            "konsol imlecinin renk turu kurulmadı (Fill animasyonlu yerel fırça olmalı)");
        GC.KeepAlive(window);
    }

    /// <summary>Event stream'in canlı satırındaki imleç AYNI bileşendir — o da turu döner.</summary>
    [StaFact]
    public void The_event_stream_cursor_hops_through_the_same_palette()
    {
        var vm = NewVm();
        var view = RealizeStream(motion: true, vm, out var window);

        var fill = ((Rectangle)view.ActiveCursorGlyph).Fill;
        Assert.True(fill is SolidColorBrush { HasAnimatedProperties: true },
            "stream imlecinin renk turu kurulmadı (Fill animasyonlu yerel fırça olmalı)");
        GC.KeepAlive(window);
    }

    /// <summary>[reduced motion] Tasarım: animasyonlar YALNIZ <c>prefers-reduced-motion: no-preference</c>
    /// altında koşar; aksi hâlde imleç SABİTTİR ve satırın rengini taşır. Ton kanalı (stream'in olay rengi)
    /// bu yüzden ölmez — yalnız sıçrama yokken görünür hâle gelir.</summary>
    [StaFact]
    public void Reduced_motion_leaves_the_cursor_still_in_the_line_colour()
    {
        var vm = NewVm();
        var view = RealizeStream(motion: false, vm, out var window);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));

        var fill = (SolidColorBrush)((Rectangle)view.ActiveCursorGlyph).Fill;
        Assert.False(fill.HasAnimatedProperties);                                          // tur YOK
        Assert.Equal(((SolidColorBrush)view.FindResource("Brush.StatusSuccessText")).Color, fill.Color);
        GC.KeepAlive(window);
    }

    /// <summary>Turun ZAMAN ÇİZELGESİ: altı KESİKLİ adım, adım başına tam bir kırpma (1.1s), toplam 6.6s,
    /// sonsuz tekrar ve faz olarak yarım kırpma geri (−0.55s) — renk tam kırpmanın dibinde atlar.</summary>
    [StaFact]
    public void The_hop_is_six_discrete_steps_one_blink_apart_phased_into_the_trough()
    {
        var view = RealizeConsole(motion: true, out var window);
        var hop = CursorHop.CreateAnimation(view.TryFindResource);

        Assert.NotNull(hop);
        Assert.Equal(6, hop.KeyFrames.Count);
        Assert.All(hop.KeyFrames.Cast<ColorKeyFrame>(), f => Assert.IsType<DiscreteColorKeyFrame>(f)); // geçiş YOK
        Assert.Equal(TimeSpan.FromMilliseconds(6600), hop.Duration.TimeSpan);
        Assert.Equal(TimeSpan.FromMilliseconds(-550), hop.BeginTime);
        Assert.Equal(RepeatBehavior.Forever, hop.RepeatBehavior);
        for (int i = 0; i < hop.KeyFrames.Count; i++)
            Assert.Equal(TimeSpan.FromMilliseconds(i * 1100), hop.KeyFrames[i].KeyTime.TimeSpan);
        GC.KeepAlive(window);
    }

    /// <summary>Turun renkleri konsolun KENDİ satır paletidir ve sırası tasarımın sırasıdır:
    /// cmd → info → success → warn → error → dim. İkinci bir renk tablosu YAZILMAZ — anahtarlar
    /// <see cref="ConsolePalette.Keys"/>'ten gelir, yani palet değişirse imleç de onunla değişir.</summary>
    [StaFact]
    public void The_hop_only_ever_shows_colours_a_console_line_could_carry()
    {
        var view = RealizeConsole(motion: true, out var window);
        var hop = CursorHop.CreateAnimation(view.TryFindResource);

        Assert.Equal(
            [ConsolePalette.Keys.Cmd, ConsolePalette.Keys.Info, ConsolePalette.Keys.Success,
             ConsolePalette.Keys.Warn, ConsolePalette.Keys.Error, ConsolePalette.Keys.Dim],
            CursorHop.BrushKeys);
        Assert.NotNull(hop);
        for (int i = 0; i < CursorHop.BrushKeys.Count; i++)
            Assert.Equal(((SolidColorBrush)view.FindResource(CursorHop.BrushKeys[i])).Color, hop.KeyFrames[i].Value);
        GC.KeepAlive(window);
    }

    /// <summary>Zaten dönen bir tur YENİDEN kurulmaz: stream'in imleci her olayda <c>StartCursorBlink</c>
    /// üzerinden geçer ve turu her seferinde baştan başlatmak ritmi sıfırlayıp imleci ilk renkte takılı
    /// gösterirdi (<c>BuildingSpinner</c>/<c>StatusGlyph</c> deseni).</summary>
    [StaFact]
    public void A_running_hop_is_never_restarted()
    {
        var vm = NewVm();
        var view = RealizeStream(motion: true, vm, out var window);
        var first = ((Rectangle)view.ActiveCursorGlyph).Fill;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));

        Assert.Same(first, ((Rectangle)view.ActiveCursorGlyph).Fill); // aynı fırça = aynı saat, tur kesilmedi
        GC.KeepAlive(window);
    }
}
