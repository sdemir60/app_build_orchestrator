using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// "Kullanıcı listeyi kaydırdı" sinyalinin KANALLARI — proje listesi, konsol ve event stream ile AYNI üç
/// girdiyi (tekerlek · kaydırma çubuğu · gezinme tuşları) dinlemek zorundadır. Tüketici tarafı
/// (duraklat/geri-aç politikası) <see cref="FrontierFollowResumeTests"/>'tedir; burada ölçülen şey
/// sinyalin KENDİSİNİN doğup doğmadığıdır.
///
/// <para><b>Ölçülen kusur:</b> liste sinyali yalnız <c>PreviewMouseWheel</c>'den alıyordu. Kaydırma çubuğunun
/// başlığını sürüklemek ya da oluğa tıklamak hiç tekerlek olayı doğurmaz — liste "kimse dokunmadı" sanıp
/// derlenen satırı takip etmeye devam ediyor, kullanıcıyı sürüklediği yerden geri çekiyordu; panel ancak
/// çubuk bırakılıp takip throttle'ı oturunca sakinleşiyordu. Konsol ve event stream aynı kusuru
/// <see cref="UserScrollSignal"/> ile çözmüştü (o sınıfın kendi doc'u tam bu senaryoyu anlatır); proje
/// listesi o kablonun dışında kalmıştı.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class FrontierFollowPauseChannelsTests(ITestOutputHelper output)
{
    private sealed record Proj(string Name);

    // StickyLayerHeaderClickTests ile AYNI topoloji: 3 katman (3+5+6 satır) → 3×24 + 14×36 = 576px içerik.
    private static IReadOnlyList<StickyLayerList.LayerGroup> SampleGroups() =>
    [
        new("L0", [new Proj("a"), new Proj("b"), new Proj("c")]),
        new("L1", [new Proj("d"), new Proj("e"), new Proj("f"), new Proj("g"), new Proj("h")]),
        new("L2", [new Proj("i"), new Proj("j"), new Proj("k"), new Proj("l"), new Proj("m"), new Proj("n")]),
    ];

    /// <summary>Listenin dikey kaydırma çubuğu — ScrollViewer'ın şablonundan (gerçek kablaj yolu).</summary>
    private static ScrollBar VerticalBarOf(StickyLayerList list) =>
        DsResources.Descendants(list.Scroll).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);

    /// <summary>Çubuğun başlığını sürüklemek: <see cref="ScrollBar.ScrollEvent"/> YALNIZ kullanıcı
    /// etkileşiminde doğar (programatik <c>ScrollToVerticalOffset</c> onu yaymaz) — aradığımız ayrım budur.</summary>
    private static void DragScrollBar(StickyLayerList list) =>
        VerticalBarOf(list).RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 0)
        { RoutedEvent = ScrollBar.ScrollEvent });

    private static void RaiseWheel(StickyLayerList list) =>
        list.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

    /// <summary>Gezinme tuşu — olay <see cref="StickyLayerList.Scroll"/>'dan TÜNELLENİR (üretimde odak satırdadır,
    /// sinyali kökteki kablo yakalamalıdır).</summary>
    private static void PressScrollKey(StickyLayerList list, Key key) =>
        list.Scroll.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list)!, 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    // ---------------------------------------------------------------- 1) sinyal doğuyor mu (izole)

    /// <summary>
    /// Üç jest de AYNI sinyali doğurur: uçuştaki takip animasyonu bırakılır (<c>ScrollAnimator</c> per-target
    /// suppress) ve merkezi arbiter'ın frontier bölgesi duraklar. Tekerlek zaten kabluydu — kontrol olarak
    /// buradadır; ayrışırsa test görür.
    /// </summary>
    [StaTheory]
    [InlineData("wheel")]
    [InlineData("scrollbar")]
    [InlineData("keyboard")]
    public void Every_user_scroll_gesture_pauses_the_lists_follow(string gesture)
    {
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var arbiter = new ScrollArbiter();
        list.Arbiter = arbiter;
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list, width: 400, height: 400);
        list.SetGroups(SampleGroups());
        list.UpdateLayout();
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));

        Assert.False(ScrollAnimator.GetIsUserSuppressed(list.Scroll)); // ön-koşul: henüz kimse dokunmadı
        Assert.False(arbiter.IsSuppressed(ScrollPanel.Frontier));

        switch (gesture)
        {
            case "wheel": RaiseWheel(list); break;
            case "scrollbar": DragScrollBar(list); break;
            case "keyboard": PressScrollKey(list, Key.PageDown); break;
            default: throw new InvalidOperationException(gesture);
        }

        output.WriteLine($"[kanal:{gesture}] animator-suppress {ScrollAnimator.GetIsUserSuppressed(list.Scroll)} · " +
                         $"arbiter-suppress {arbiter.IsSuppressed(ScrollPanel.Frontier)}");
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll),
            $"'{gesture}' jesti uçuştaki takip animasyonunu bırakmadı — liste kullanıcıyla dövüşür.");
        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier),
            $"'{gesture}' jesti merkezi arbiter'a bildirilmedi — frontier follow duraklamaz.");
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- 2) üretim zinciri (koşan run)

    private const int ProjectCount = 60;

    private static List<ProjectNode> Topology() =>
        [.. Enumerable.Range(0, ProjectCount).Select(i => new ProjectNode(
            $@"C:\p\Proj{i}.csproj", $"Proj{i}", $@"C:\p\Proj{i}.csproj",
            ["Osys"], [], i, null, null, false, null))];

    /// <summary>
    /// Koşan bir run'da kullanıcı kaydırma ÇUBUĞUNU sürükler → takip durur ve frontier ilerlese bile liste
    /// kullanıcının bıraktığı yerde kalır. (Tekerlek eşdeğeri <see cref="FrontierFollowResumeTests"/>'te
    /// zaten pinli; kopan halka çubuktu.)
    /// </summary>
    [StaFact]
    public void Dragging_the_scrollbar_during_a_run_pauses_frontier_follow()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var list = window.Shell.ProjectsList;
        long[] clock = [0];
        list.NowMs = () => clock[0]; // boşta-geri-açılma penceresi deterministik (D8)

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[30].Id, nodes[30].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));

        // Kullanıcı çubuğu kavrar ve listenin tepesine sürükler (frontier'den UZAĞA — yakınlık kapısı kapalı).
        DragScrollBar(list);
        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();

        double parked = list.Scroll.VerticalOffset;
        vm.OnEvent(new ProjectSucceededEvent("r1", nodes[30].Id, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[40].Id, nodes[40].Name));
        DispatcherPump.PumpUntil(() => Math.Abs(list.Scroll.VerticalOffset - parked) > 1, TimeSpan.FromSeconds(2));

        double after = list.Scroll.VerticalOffset;
        output.WriteLine($"[cubuk] kullanıcı {parked:N1}'e sürükledi · frontier 30→40 sonrası offset {after:N1}");
        Assert.True(Math.Abs(after - parked) <= 1,
            $"kullanıcı çubuğu sürüklemişken liste frontier'i takip etti ({parked:N1} → {after:N1}) — " +
            "çubuk kanalı 'kullanıcı kaydırdı' sinyalini doğurmuyor.");
        GC.KeepAlive(window);
    }
}
