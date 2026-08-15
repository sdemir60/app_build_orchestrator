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

    /// <summary>§2.2: şeridin döngü kümesi iki satır söyler — ne olduğu + yol.</summary>
    [StaFact]
    public void The_ribbon_cycle_cluster_has_a_two_line_tooltip()
    {
        var vm = NewVm();
        var ribbon = new StickyRibbon { DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), ribbon);

        Assert.NotNull(ribbon.CycleChip);
        string tip = Assert.IsType<string>(ribbon.CycleChip!.ToolTip);
        Assert.Equal(
            CycleText.ClusterHeadline + Environment.NewLine
                + "Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts",
            tip);
        GC.KeepAlive(window);
    }

    /// <summary>Birden çok döngü varsa her biri kendi satırında listelenir.</summary>
    [StaFact]
    public void Several_cycles_are_listed_one_per_line()
    {
        const string D = @"C:\p\Sales.Core.csproj", E = @"C:\p\Sales.Api.csproj";
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe),
            new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(A, "Domain.Parts"), Node(B, "Parts.Inventory"), Node(D, "Sales.Core"), Node(E, "Sales.Api")],
            [[A, B], [D, E]], [], []));

        var ribbon = new StickyRibbon { DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), ribbon);

        string tip = Assert.IsType<string>(ribbon.CycleChip!.ToolTip);
        Assert.Equal(
            CycleText.ClusterHeadline + Environment.NewLine
                + "Domain.Parts → Parts.Inventory → Domain.Parts" + Environment.NewLine
                + "Sales.Core → Sales.Api → Sales.Core",
            tip);
        GC.KeepAlive(window);
    }
}
