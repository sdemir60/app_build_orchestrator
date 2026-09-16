using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kullanıcı kaydırmasıyla duraklatılan frontier follow'un GERİ AÇILMA yolu — <b>üretim zinciri üzerinden</b>
/// (<c>MainWindow._elapsedTimer</c> → <c>FollowFrontier</c> → liste).
///
/// <para><b>Kural TEKtir: boşta penceresi.</b> Kullanıcı listeyi kaydırdıysa takip, listeye
/// <see cref="StickyLayerList.FrontierIdleResumeMs"/> boyunca dokunulmayana kadar duraklı kalır — panelin
/// NERESİNDE olduğu kararı DEĞİŞTİRMEZ.</para>
///
/// <para><b>[DEĞİŞEN KURAL] Eskiden iki KONUM yolu daha vardı</b> ve ikisi de boşta penceresini atlıyordu:
/// frontier satırı görünür pencereye 48 px yakınsa, ya da liste dibine 48 px kalmışsa, duraklama ANINDA
/// kalkıyordu. Gerekçe "kullanıcı ilgi çekici yere döndüyse takibi hemen sürdür"dü. Sahada ölçülen sonuç
/// (kullanıcı raporu) bunun tersiydi: konum yolu "geri döndüm" ile "burada okuyorum"u ayırt edemiyor ve
/// frontier ekrandayken duraklamayı HER 200 ms'lik tick'te siliyordu — derlenen satırla aynı ekrandaysanız
/// takip sürekli viewport'u elinizden alıyor, ondan uzaklaştığınızda ise (yalnız boşta penceresi kaldığı için)
/// uslu uslu bekliyordu. Aynı jest iki farklı davranış üretemez; konum yolları kaldırıldı, geri açılma tek
/// kapıdan geçer.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class FrontierFollowResumeTests(ITestOutputHelper output)
{
    private const int ProjectCount = 60;

    private static List<ProjectNode> Topology() =>
        [.. Enumerable.Range(0, ProjectCount).Select(i => new ProjectNode(
            $@"C:\p\Proj{i}.csproj", $"Proj{i}", $@"C:\p\Proj{i}.csproj",
            ["Osys"], [], i, null, null, false, null))];

    private static void RaiseFrontierWheel(StickyLayerList list) =>
        list.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

    /// <summary>Koşan bir run + frontier satırı; liste follow etmiş durumda döner.</summary>
    private static (MainWindow window, RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes, long[] clock)
        RunningWithFrontier(TempDir temp, int frontierIndex)
    {
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var list = window.Shell.ProjectsList;
        long[] clock = [0];
        list.NowMs = () => clock[0]; // idle penceresi deterministik sürülür (D8)

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[frontierIndex].Id, nodes[frontierIndex].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));
        return (window, vm, list, nodes, clock);
    }

    /// <summary>Frontier'i ilerletir ve listenin onu takip edip etmediğini döndürür.</summary>
    private static bool FollowsTo(RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes, int from, int to)
    {
        double before = list.Scroll.VerticalOffset;
        vm.OnEvent(new ProjectSucceededEvent("r1", nodes[from].Id, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[to].Id, nodes[to].Name));
        DispatcherPump.PumpUntil(() => Math.Abs(list.Scroll.VerticalOffset - before) > 1, TimeSpan.FromSeconds(2));
        return Math.Abs(list.Scroll.VerticalOffset - before) > 1;
    }

    /// <summary>
    /// <b>Kullanıcı raporunun ta kendisi:</b> derlenen satır EKRANDAYKEN kaydırmak takibi susturmalı — tıpkı
    /// ondan uzaktayken olduğu gibi. Kullanıcı hiçbir yere gitmez, olduğu yerde kaydırır; frontier ilerlese bile
    /// liste yerinde kalır.
    /// </summary>
    [StaFact]
    public void Scrolling_while_the_frontier_is_on_screen_does_not_let_follow_cut_in()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, _) = RunningWithFrontier(temp, frontierIndex: 30);

        // Ön-koşul: takip frontier'i görünür kıldı, yani satır 30 ŞU AN ekranda (konum yolunun tetiklendiği hâl).
        double frontierTop = list.Metrics!.OffsetOfRow(30);
        Assert.InRange(frontierTop, list.Scroll.VerticalOffset, list.Scroll.VerticalOffset + list.Scroll.ViewportHeight);

        RaiseFrontierWheel(list); // kullanıcı tam burada kaydırıyor — uzağa GİTMİYOR

        bool followed = FollowsTo(vm, list, nodes, 30, 32);
        output.WriteLine($"[ekranda] frontier ekrandayken kaydırma sonrası takip: {followed}");
        Assert.False(followed,
            "derlenen satır ekrandayken kullanıcı kaydırması yok sayıldı — takip viewport'u geri aldı.");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Frontier'e geri dönmek TEK BAŞINA takibi geri açmaz. Eski iddia bunun tersiydi
    /// ("<c>Returning_to_the_frontier_resumes_follow</c>": yakınlık niyeti doğrudan okur, duraklama anında
    /// kalkar); sınıf özetindeki gerekçeyle kaldırıldı. Geri açan tek şey boşta penceresidir — ve o dolunca
    /// kullanıcı frontier'in yanında olsun olmasın takip sürer.
    /// </summary>
    [StaFact]
    public void Returning_to_the_frontier_does_not_resume_follow_on_its_own()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, clock) = RunningWithFrontier(temp, frontierIndex: 30);

        // 1) Tekerlek → follow duraklar; kullanıcı UZAĞA (tepeye) gider.
        RaiseFrontierWheel(list);
        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        Assert.False(FollowsTo(vm, list, nodes, 30, 40)); // uzaktayken takip ETMEZ

        // 2) Kullanıcı O ANKİ frontier satırının (40) yanına geri döner — boşta penceresi DOLMADAN.
        double frontierTop = list.Metrics!.OffsetOfRow(40);
        list.Scroll.ScrollToVerticalOffset(Math.Max(0, frontierTop - list.Scroll.ViewportHeight / 2));
        list.UpdateLayout();
        DispatcherPump.PumpUntil(() => !ScrollAnimator.GetIsUserSuppressed(list.Scroll), TimeSpan.FromSeconds(1));

        output.WriteLine($"[yakınlık] frontier'e dönünce duraklama kalktı mı: {!ScrollAnimator.GetIsUserSuppressed(list.Scroll)}");
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll),
            "frontier'e dönmek duraklamayı tek başına kaldırdı — konum yolu hâlâ boşta penceresini atlıyor.");
        Assert.False(FollowsTo(vm, list, nodes, 40, 50));

        // 3) Boşta penceresi dolunca takip sürer — geri açan TEK kapı budur.
        clock[0] += StickyLayerList.FrontierIdleResumeMs + 1;
        bool followed = FollowsTo(vm, list, nodes, 50, 55);
        output.WriteLine($"[yakınlık] boşta penceresinden sonra takip: {followed}");
        Assert.True(followed, "boşta penceresi doldu ama takip sürmedi.");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Leaving_the_list_alone_for_a_while_resumes_follow()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, clock) = RunningWithFrontier(temp, frontierIndex: 30);

        // 1) Tekerlek → follow duraklar; kullanıcı uzakta kalır ve HİÇ dokunmaz.
        RaiseFrontierWheel(list);
        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        Assert.False(FollowsTo(vm, list, nodes, 30, 40)); // hemen ardından takip ETMEZ (doğru)

        // 2) Boşta geçen süre eşiği aşar → takip kendiliğinden sürmeli.
        clock[0] += StickyLayerList.FrontierIdleResumeMs + 1;

        bool followed = FollowsTo(vm, list, nodes, 40, 50);
        output.WriteLine($"[resume-idle] {StickyLayerList.FrontierIdleResumeMs} ms dokunmama sonrası takip: {followed}");
        Assert.True(followed,
            $"listeye {StickyLayerList.FrontierIdleResumeMs} ms dokunulmadı ama follow hâlâ duraklı.");
        GC.KeepAlive(window);
    }

    /// <summary>Kullanıcı AKTİF kaydırırken boşta-geri-açılma tetiklenmez: her tekerlek hareketi saati sıfırlar.</summary>
    [StaFact]
    public void Active_scrolling_keeps_follow_paused_because_each_wheel_restarts_the_idle_window()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, clock) = RunningWithFrontier(temp, frontierIndex: 30);

        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        for (int i = 0; i < 4; i++)
        {
            clock[0] += StickyLayerList.FrontierIdleResumeMs - 1; // eşiğin hep BİR TIK altında kal
            RaiseFrontierWheel(list);
        }

        bool followed = FollowsTo(vm, list, nodes, 30, 40);
        output.WriteLine($"[resume-idle] aktif kaydırma sırasında takip: {followed}");
        Assert.False(followed, "kullanıcı hâlâ kaydırıyorken follow devreye girdi — boşta penceresi sıfırlanmıyor.");
        GC.KeepAlive(window);
    }
}
