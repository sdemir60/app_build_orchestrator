using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.2/§2.4/§5] Döngü tooltip'leri. Tasarım üç yüzeyde de aynı iki şeyi ister: döngünün NE
/// olduğunu söyleyen cümle ve döngünün YOLU (<c>A → B → C → A</c>).
///
/// <para><b>Neden bu dosya var:</b> yol hiçbir tooltip'te yoktu (nokta sabit bir cümle taşıyordu) ve şeridin
/// döngü kümesinin tooltip'i hiç kurulmuyordu — üçgen tek başına "bir şey ters" diyor ama hangi projelerin
/// birbirini beklediğini söylemiyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class CycleTooltipTests
{
    private const string A = @"C:\p\Domain.Parts.csproj";
    private const string B = @"C:\p\Parts.Inventory.csproj";
    private const string C = @"C:\p\Parts.Api.csproj";

    private static RunViewModel NewVm()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe),
            new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(A, "Domain.Parts"), Node(B, "Parts.Inventory"), Node(C, "Parts.Api")],
            [[A, B, C]], [], []));
        return vm;
    }

    private static ProjectNode Node(string id, string name) =>
        new(id, name, id, [], [], 0, null, null, true, null);

    [Fact]
    public void The_path_closes_the_loop_so_it_reads_as_a_cycle()
        => Assert.Equal("Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts",
                        CycleText.Path(["Domain.Parts", "Parts.Inventory", "Parts.Api"]));

    /// <summary>Yol satıra iner: her üye kendi döngüsünün yolunu taşır.</summary>
    [Fact]
    public void Every_member_row_carries_its_cycle_path()
    {
        var vm = NewVm();

        Assert.All(vm.Projects, row => Assert.Equal(
            "Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts", row.CyclePath));
    }

    /// <summary>Döngüde olmayan satırda yol YOKTUR — boş bir satır tooltip'e sızmamalı.</summary>
    [Fact]
    public void A_row_outside_any_cycle_has_no_path()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe),
            new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(A, "Domain.Parts") with { InCycle = false }], [], [], []));

        Assert.Equal("", Assert.Single(vm.Projects).CyclePath);
    }

    /// <summary>§2.4-2: noktanın tooltip'i döngü açıklaması + yol.</summary>
    [StaFact]
    public void The_dot_tooltip_explains_the_cycle_and_shows_its_path()
    {
        var dot = new WillBuildDot { InCycle = true, CyclePath = "A → B → A" };
        DsResources.Realize(DsResources.NewHost(), dot);

        string tip = Assert.IsType<string>(dot.ToolTip);
        Assert.Equal(CycleText.Membership + Environment.NewLine + "A → B → A", tip);
    }

    /// <summary>§2.4-6: uyarı üçgeninin nedenleri de yolu taşır (en ağır neden en üstte).</summary>
    [StaFact]
    public void The_warning_triangle_lists_the_cycle_path_under_its_reason()
    {
        var vm = NewVm();
        var row = new ProjectRow { DataContext = vm.Projects[0] };
        DsResources.Realize(DsResources.NewHost(), row);

        string tip = Assert.IsType<string>(row.DepTooltip);
        Assert.StartsWith("In a dependency cycle", tip, StringComparison.Ordinal);
        Assert.Contains("Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts", tip, StringComparison.Ordinal);
    }

    /// <summary>[design v1.11.0 §2.2] Şeritte döngü kümesi ARTIK YOKTUR.
    /// <para><b>[DEĞİŞEN KURAL]</b> §2.2 (v1.7.0) şeritte turuncu bir döngü kümesi istiyordu ve iki test onu
    /// pinliyordu ("iki satırlık tooltip: ne olduğu + yol", "birden çok döngü her biri kendi satırında").
    /// v1.11.0 turuncuyu UI'dan tamamen çıkardı: döngü bilgisi satırdaki TEK amber üçgende ve alt bardaki ⚠
    /// filtresinde yaşıyor. Şerit yalnız KOŞU sonuçlarını taşır — döngü bir koşu sonucu değildir. İki eski
    /// iddia bu tek teste indi.</para>
    /// <para>Döngü YOLUNUN kendisi silinmedi: <see cref="CycleText.Path"/> hâlâ üretilir ve satır VM'ine
    /// itilir (yukarıdaki testler onu pinler) — yalnız ŞERİTTEKİ tüketicisi kalktı.</para></summary>
    [StaFact]
    public void The_ribbon_no_longer_carries_a_cycle_cluster()
    {
        var vm = NewVm();
        var ribbon = new StickyRibbon { DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), ribbon);

        Assert.NotEmpty(vm.CyclePaths); // ön-koşul: topolojide GERÇEKTEN bir döngü var
        var texts = DsResources.Descendants(ribbon).OfType<System.Windows.Controls.TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("in a dependency cycle", StringComparison.Ordinal));
        GC.KeepAlive(window);
    }
}
