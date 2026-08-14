using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [prototip BuildApp.jsx:900-909] Event stream'de yazacak bir şey kalmayınca satır KAYBOLMAZ: geriye saat +
/// yanıp sönen soluk imleçten oluşan bir bekleme satırı kalır — konsolun prompt satırının ikizi.
///
/// <para>Eskiden aktif satır tamamen gizleniyordu; koşu bitince akışın altında hiçbir işaret kalmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EventStreamIdlePromptTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1")
        { RootPath = @"D:\repo" };

    private static (EventStreamView view, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var view = new EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        return (view, DsResources.Realize(host, view));
    }

    private static Color Token(EventStreamView view, string key) =>
        ((SolidColorBrush)view.FindResource(key)).Color;

    private static Color CursorColour(EventStreamView view) =>
        ((SolidColorBrush)((Rectangle)view.ActiveCursorGlyph).Fill).Color;

    [StaFact]
    public void After_the_writing_stops_a_dim_prompt_line_stays_at_the_bottom()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        // Bir koşu: aktif satır canlı (amber), sonra biter.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);       // ön-koşul
        Assert.Equal(Token(view, "Brush.AmberText"), CursorColour(view));   // ön-koşul: canlıyken amber

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 0, 100));

        // Satır DURUR: saat + soluk imleç, metin boş.
        Assert.Equal(Visibility.Visible, view.ActiveLine.Visibility);
        Assert.Equal(Token(view, "Brush.TextFaint"), CursorColour(view));
        Assert.True(view.ActiveCursorGlyph.HasAnimatedProperties, "bekleme imleci yanıp sönmeli");
        GC.KeepAlive(window);
    }

    /// <summary>Akışta hiç olay yokken bekleme satırı gösterilmez — orada boş-durum metni konuşur.</summary>
    [StaFact]
    public void An_empty_stream_shows_no_prompt_line()
    {
        var vm = NewVm();
        var (view, window) = Realize(vm);

        Assert.Equal(Visibility.Collapsed, view.ActiveLine.Visibility);
        GC.KeepAlive(window);
    }
}
