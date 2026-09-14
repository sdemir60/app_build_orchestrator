using System.Windows;
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
/// Pencere tepsiye inince imleçlerin SONSUZ saatleri (kırpma + renk turu) durmalıdır.
///
/// <para><b>Neden bu dosya var (ÖLÇÜLDÜ):</b> <c>X</c> pencereyi gizler, görünümleri boşaltmaz — yani
/// <c>Unloaded</c> hiç ateşlenmez ve konsol ile event stream imleçlerinin iki sonsuz saati tepside de dönmeye
/// devam ediyordu. Uygulama boştayken tepside döngü sayacıyla ölçüldü: süreç 80,9 M döngü/sn (UI thread
/// 65,5 M); başlatıcılar görünürlüğe bağlanınca 67,1 M (UI thread 52,9 M). ARCHITECTURE §14.5 kuralı zaten
/// "every infinite animation is gated on IsVisible" der; bu iki sahip kurala uymuyordu.</para>
///
/// <para><b>Neden yalnız "gizlenince durdur" yetmez (ÖLÇÜLDÜ):</b> başlatıcılar olaylarla yeniden çağrılır —
/// konsolda <c>VisualLinesChanged → RefreshPrompt → StartBlink</c>, stream'de her olayda
/// <c>UpdateActiveLine → StartCursorBlink</c>. Yalnız <c>IsVisibleChanged</c>'de durduran bir deneme tepsideki
/// maliyeti düşürmedi: saat bir sonraki olayda geri kuruluyordu. Kapı başlatıcının kendisindedir; ikinci test
/// grubu tam olarak bunu pinler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HiddenCursorClockTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1")
        { RootPath = @"D:\repo" };

    private static bool Ticking(UIElement cursor) =>
        cursor.HasAnimatedProperties || CursorHop.IsRunning(cursor as Shape);

    private static ConsoleView RealizeConsole(out Window window)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        window = DsResources.Realize(DsResources.NewHost(), view);
        return view;
    }

    private static EventStreamView RealizeStream(RunViewModel vm, out Window window)
    {
        var view = new EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        window = DsResources.Realize(DsResources.NewHost(), view);
        return view;
    }

    // ---------------------------------------------------------------- konsol prompt imleci

    [StaFact]
    public void Hiding_the_window_stops_the_console_cursor_clocks()
    {
        var view = RealizeConsole(out var window);
        view.ShowReady();
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "ön-koşul: görünür imleç GERÇEKTEN kırpmalı");
        Assert.True(CursorHop.IsRunning(view.ActiveCursorGlyph as Shape), "ön-koşul: renk turu GERÇEKTEN dönmeli");

        window.Hide();

        Assert.False(Ticking(view.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_hidden_console_does_not_restart_the_cursor_when_its_prompt_is_refreshed()
    {
        var view = RealizeConsole(out var window);
        view.ShowReady();
        window.Hide();
        Assert.False(Ticking(view.ActiveCursorGlyph)); // non-vacuous: gizlenince gerçekten durdu

        view.ShowReady(); // başlatıcıyı yeniden çağıran üretim yolu — tepsideyken de koşar

        Assert.False(Ticking(view.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Showing_the_window_again_resumes_the_console_cursor()
    {
        var view = RealizeConsole(out var window);
        view.ShowReady();
        window.Hide();
        Assert.False(Ticking(view.ActiveCursorGlyph));

        window.Show();
        window.UpdateLayout();

        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.True(CursorHop.IsRunning(view.ActiveCursorGlyph as Shape));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- event stream canlı satır imleci

    [StaFact]
    public void Hiding_the_window_stops_the_event_stream_cursor_clocks()
    {
        var vm = NewVm();
        var view = RealizeStream(vm, out var window);
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "ön-koşul: görünür imleç GERÇEKTEN kırpmalı");
        Assert.True(CursorHop.IsRunning(view.ActiveCursorGlyph as Shape), "ön-koşul: renk turu GERÇEKTEN dönmeli");

        window.Hide();

        Assert.False(Ticking(view.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_hidden_event_stream_does_not_restart_the_cursor_when_an_event_arrives()
    {
        var vm = NewVm();
        var view = RealizeStream(vm, out var window);
        window.Hide();
        Assert.False(Ticking(view.ActiveCursorGlyph)); // non-vacuous

        // Tepsideyken bir koşu başlar: her olay aktif satırı tazeler ve başlatıcıyı yeniden çağırır.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        Assert.False(Ticking(view.ActiveCursorGlyph));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Showing_the_window_again_resumes_the_event_stream_cursor()
    {
        var vm = NewVm();
        var view = RealizeStream(vm, out var window);
        window.Hide();
        Assert.False(Ticking(view.ActiveCursorGlyph));

        window.Show();
        window.UpdateLayout();

        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties);
        Assert.True(CursorHop.IsRunning(view.ActiveCursorGlyph as Shape));
        GC.KeepAlive(window);
    }
}
