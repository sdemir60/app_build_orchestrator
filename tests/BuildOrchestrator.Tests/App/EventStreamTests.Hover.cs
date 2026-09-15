using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.17.0 §9 "3" — Task 7] Event stream'in her satırında hover artık var — eski kural (tıklanamaz
/// satırlarda "parıltıyı ezmesin" gerekçesiyle hover zemini hiç açılmıyordu) DEĞİŞTİ: tıklanamaz satır artık bir
/// adım daha sessiz (<c>Brush.Surface</c>) zemin alır, tıklanabilir satır (mevcut) <c>Brush.SurfaceHover</c>'a
/// açılır; imleç eşlemesi (tıklanamaz → Arrow, tıklanabilir → Hand) DEĞİŞMEDİ. Glow-once ile etkileşim: hover,
/// parıltının O ANKİ rengeinden (<see cref="MotionTokens.TransitionColor"/>'ın retarget'ı — <c>SnapshotAndReplace</c>)
/// doğru hedefe yumuşak geçer; ATLAMA yoktur ama parıltı KESİLİR — bkz.
/// <see cref="Hovering_a_glowing_done_row_takes_over_the_animation_and_settles_on_the_correct_surface"/>.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamHoverTests
{
    private static readonly TimeSpan PumpTimeout = TimeSpan.FromSeconds(2);

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (EventStreamView view, Window window, System.Windows.Controls.Border host) Realize(
        RunViewModel vm, bool forceAnimations = false)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => forceAnimations, DataContext = vm };
        var window = DsResources.Realize(host, view);
        return (view, window, host);
    }

    private static void Enter(UIElement row) =>
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });

    private static void Leave(UIElement row) =>
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

    // ================================================================ tıklanamaz satır

    [StaFact]
    public void Hovering_a_non_clickable_row_opens_a_quiet_surface_band_and_keeps_the_arrow_cursor()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 1200, "error CS0103"));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 1, 0, 0, 100)); // hatalı → Done UYGUN DEĞİL (parıltı yok)

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Last();

        // Non-vacuous: satır gerçekten Done ve gerçekten tıklanamaz, glow'la karışmıyor.
        Assert.Equal(StreamKind.Done, row.ViewModel!.Kind);
        Assert.False(row.ViewModel!.IsClickable);
        Assert.False(row.ViewModel!.GlowEligible);
        Assert.Equal(Cursors.Arrow, row.Cursor);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background)); // dinlenme: şeffaf

        Enter(row);
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.Surface"), PumpTimeout);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Surface"), DsResources.ColorOf(row.Background));
        Assert.Equal(Cursors.Arrow, row.Cursor); // imleç hover'dan etkilenmez

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ tıklanabilir satır

    [StaFact]
    public void Hovering_a_clickable_row_opens_surface_hover_and_switches_to_the_hand_cursor()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200)); // → "A built (1.2s)" ok satırı, tıklanabilir

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);

        Assert.True(row.ViewModel!.IsClickable);
        Assert.Equal(Cursors.Hand, row.Cursor);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));

        Enter(row);
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.SurfaceHover"), PumpTimeout);
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceHover"), DsResources.ColorOf(row.Background));
        Assert.Equal(Cursors.Hand, row.Cursor);

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ seçili satır değişmez

    [StaFact]
    public void Hovering_a_selected_row_does_not_disturb_its_raised_surface()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200));

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);
        vm.SelectProject(id);
        view.UpdateLayout();
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.SurfaceRaised"), PumpTimeout);

        Enter(row);
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200)); // geçiş penceresinden fazlası — durağan olmalı

        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(row.Background));
        GC.KeepAlive(window);
    }

    // ================================================================ glow-once ile etkileşim

    /// <summary>
    /// [DEĞİŞEN KURAL] Eski davranış: <c>Done</c> satırı (tıklanamaz) hiç hover almazdı, gerekçe "parıltıyı
    /// ezmesin". Artık her satırda hover var; seçilen davranış — parıltı SÜRERKEN hover gelirse zemin animasyonu
    /// KESİLİR ama ATLAMA olmadan (<see cref="MotionTokens.TransitionColor"/> uçuştaki rengi
    /// <c>HandoffBehavior.SnapshotAndReplace</c> ile devralır) doğru hover hedefine (tıklanamaz → <c>Brush.Surface</c>)
    /// yumuşakça oturur; parıltı BİR KEZ oynanmış sayılır (<c>GlowPlayCount</c> artmaz).
    /// </summary>
    [StaFact]
    public void Hovering_a_glowing_done_row_takes_over_the_animation_and_settles_on_the_correct_surface()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 0, 0, 2100)); // hatasız → Done UYGUN

        var (view, window, host) = Realize(vm, forceAnimations: true);
        var row = view.Rows.Last();
        DispatcherPump.PumpUntil(() => row.GlowPlayCount >= 1, PumpTimeout);

        // Non-vacuous: parıltı GERÇEKTEN sürüyor — yeşil, canlı bir saat.
        Assert.True(((SolidColorBrush)row.Background).HasAnimatedProperties);
        Assert.NotEqual(Colors.Transparent, DsResources.ColorOf(row.Background));
        Assert.False(row.ViewModel!.IsClickable);

        // Parıltı devam ederken (1.1s'nin başlarında) hover gelir.
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(150));
        Enter(row);

        // Hover devralır: zemin nihayetinde doğru hedefe (tıklanamaz → Brush.Surface) oturur — yeşilde asılı kalmaz,
        // şeffafa da dönmez (satırın üstünde fare hâlâ var).
        DispatcherPump.PumpUntil(
            () => DsResources.ColorOf(row.Background) == DsResources.TokenColor(host, "Brush.Surface"), PumpTimeout);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Surface"), DsResources.ColorOf(row.Background));
        Assert.Equal(1, row.GlowPlayCount); // parıltı YENİDEN oynamadı — yalnız zemin devralındı

        Leave(row);
        DispatcherPump.PumpUntil(() => DsResources.ColorOf(row.Background) == Colors.Transparent, PumpTimeout);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(row.Background));
        Assert.Equal(1, row.GlowPlayCount);
        GC.KeepAlive(window);
    }
}
