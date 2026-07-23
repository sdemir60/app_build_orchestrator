using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D3/T?] design-v1 Event stream paneli. SAF çekirdek (<see cref="StreamComposer"/>) fırtına/hata/aktif-satır
/// kararları <c>[Fact]</c>; görünüm (<see cref="EventStreamView"/>: parıltı-once, sayaç, seçim şeridi) GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) <c>[StaFact]</c>.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamTests
{
    // ============================================================ SAF ÇEKİRDEK ([Fact])

    [Fact]
    public void Burst_events_under_three_hundred_forty_milliseconds_are_printed_instantly()
    {
        var c = new StreamComposer();
        var first = c.Push(isFail: false, nowMs: 1000);  // ilk emit → fırtına DEĞİL → daktilo (instant=false)
        var burst = c.Push(isFail: false, nowMs: 1100);  // 100ms sonra (<340) → fırtına → ANINDA

        Assert.False(first.Instant);
        Assert.True(burst.Instant);
    }

    [Fact]
    public void Failure_events_skip_the_typewriter_entirely()
    {
        var c = new StreamComposer();
        c.Push(isFail: false, nowMs: 1000);
        var fail = c.Push(isFail: true, nowMs: 5000); // 4000ms sonra → fırtına DEĞİL, ama hata → ANINDA

        Assert.True(fail.Instant);
    }

    [Fact]
    public void The_active_line_jumps_to_the_most_recently_started_building_project()
    {
        var c = new StreamComposer();
        c.StartBuilding("A", "A", 1000);
        c.StartBuilding("B", "B", 1100);
        c.StartBuilding("C", "C", 1200);
        Assert.Equal("C", c.ActiveProjectId); // en son başlayan aktif satırdır

        c.FinishBuilding("C", 1300);
        Assert.Equal("B", c.ActiveProjectId);   // izlenen bitince → en son başlayan hâlâ building projeye ATLAR
        Assert.Equal("B building…", c.ActiveText);
    }

    // ============================================================ GÖRÜNÜM ([StaFact])

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

    [StaFact]
    public void Event_counter_reports_the_full_buffer_not_the_rendered_slice()
    {
        var vm = NewVm();
        // 160 skipped olay → tampon 160 (≤260), render dilimi 150 ile sınırlı.
        for (int i = 0; i < 160; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", "up to date"));

        var (view, window, _) = Realize(vm);

        Assert.Equal("160 events", view.Counter.Text);              // TAM tampon (render dilimi DEĞİL)
        Assert.Equal(StreamComposer.RenderSlice, view.Rows.Count);  // yalnız 150 satır render edildi
        Assert.Equal(160, vm.StreamEventCount);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Selected_row_gets_a_two_pixel_amber_stripe_and_a_raised_surface()
    {
        const string id = @"C:\p\a.csproj";
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 1200)); // → tıklanabilir "A built (1.2s)" ok satırı

        var (view, window, host) = Realize(vm);
        var row = view.Rows.Single(r => r.ViewModel?.ProjectId == id);

        Assert.Equal(Visibility.Collapsed, row.SelectionStripe.Visibility); // seçili değil → şerit yok

        vm.SelectProject(id);
        view.UpdateLayout();

        Assert.Equal(Visibility.Visible, row.SelectionStripe.Visibility);
        Assert.Equal(2.0, row.SelectionStripe.Width);                                    // sol 2px şerit
        Assert.Equal(DsResources.TokenColor(host, "Brush.Amber"), DsResources.ColorOf(row.SelectionStripe.Fill)); // amber
        Assert.Equal(DsResources.TokenColor(host, "Brush.SurfaceRaised"), DsResources.ColorOf(row.Background));    // raised zemin
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Done_row_glows_once_and_never_replays_after_container_recycling()
    {
        var vm = NewVm();
        var (view, window, _) = Realize(vm, forceAnimations: true);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 0, 4, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 0, 0, 100)); // hatasız "Completed …" done satırı

        var row = view.Rows.Last();
        DispatcherPump.PumpUntil(() => row.GlowPlayCount >= 1, TimeSpan.FromSeconds(2));

        Assert.True(row.ViewModel!.GlowEligible);
        Assert.Equal(1, row.GlowPlayCount);
        Assert.True(row.ViewModel!.GlowPlayed);

        // Container recycle taklidi: aynı VM yeniden bağlanır → GlowPlayed guard'ı TEKRAR oynatmaz.
        row.SimulateContainerRecycle();
        Assert.Equal(1, row.GlowPlayCount);
        GC.KeepAlive(window);
    }
}
