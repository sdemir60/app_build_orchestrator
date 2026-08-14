using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// İki kaydırılabilir panelin ORTAK sözleşmesi: <b>kullanıcı bir kez dokununca akan içerik onu yerinden
/// oynatmaz.</b> Sinyal geometriden çıkarılmaz, ham girdiden gelir (<c>UserScrollSignal</c>) ve dibe çekme
/// yetkisi tek yerdedir (<c>BottomAnchorBehavior.ShouldFollow</c>).
///
/// <para>Sahada iki ayrı yüzde görüldü: konsol derleme sürerken kaydırılamıyordu (her batch kullanıcıyı geri
/// fırlatıyordu), event stream ise kimse dokunmadan takibi bırakabiliyordu. İkisi de aynı kök nedendendi —
/// takip kararı geometriye bakıyor, ham girdiye bakmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ScrollFollowWiringTests
{
    private static ConsoleView RealizedConsole()
    {
        var view = new ConsoleView();
        view.Measure(new Size(800, 400));
        view.Arrange(new Rect(0, 0, 800, 400));
        view.UpdateLayout();
        // Viewport'u gerçekten aşan bir anlatı — aksi halde "kaydırma" diye bir şey olmaz ve test vacuous olur.
        view.ShowRunDocument(string.Concat(Enumerable.Range(0, 300).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        return view;
    }

    [StaFact]
    public void A_scroll_gesture_stops_the_console_from_following_and_arriving_output_leaves_it_alone()
    {
        var view = RealizedConsole();
        Assert.True(view.FollowsBottom);                                  // ön-koşul: geçiş dibe pinledi
        Assert.True(view.Editor.ExtentHeight > view.Editor.ViewportHeight, "senaryo kurulamadı: içerik kaydırılabilir değil");

        UserScrollGesture.Raise(view);          // kullanıcı kaydırma çubuğunu tuttu
        view.Editor.ScrollToVerticalOffset(0);  // ...ve yukarı çıktı
        view.UpdateLayout();
        Assert.False(view.FollowsBottom);

        double readingAt = view.Editor.VerticalOffset;
        view.AppendBatch("fresh output\n");     // derleme akmaya devam ediyor
        view.UpdateLayout();

        Assert.Equal(readingAt, view.Editor.VerticalOffset); // okuyucu yerinde kaldı
    }

    [StaFact]
    public void A_scroll_gesture_stops_the_stream_from_following_and_arriving_events_leave_it_alone()
    {
        var vm = new RunViewModel(
            new EngineHost(TestPaths.SupervisorExe),
            new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };
        var view = new EventStreamView { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), view);

        for (int i = 0; i < 60; i++)
            vm.OnEvent(new ProjectSkippedEvent("r1", $@"C:\p\proj{i}.csproj", SkipReasons.UpToDate));
        DispatcherPump.PumpUntil(() => view.Scroll.ScrollableHeight > 0, TimeSpan.FromSeconds(2));
        Assert.True(view.Scroll.ScrollableHeight > 0, "senaryo kurulamadı: stream içeriği kaydırılabilir değil");
        Assert.True(view.FollowsBottom); // ön-koşul

        UserScrollGesture.Raise(view);
        view.Scroll.ScrollToVerticalOffset(0);
        view.UpdateLayout();
        Assert.False(view.FollowsBottom);

        double readingAt = view.Scroll.VerticalOffset;
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\late.csproj", SkipReasons.UpToDate));
        view.UpdateLayout();

        Assert.Equal(readingAt, view.Scroll.VerticalOffset);
        GC.KeepAlive(window);
    }
}
