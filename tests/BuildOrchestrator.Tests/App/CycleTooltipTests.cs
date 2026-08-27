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

    /// <summary>[design v1.11.0 §2.4-2] Statü noktası TOOLTIP TAŞIMAZ.
    /// <para><b>[DEĞİŞEN KURAL]</b> §2.4-2 (v1.7.0) noktaya iki satırlık bir tooltip veriyordu (döngü
    /// açıklaması + yol) ve nokta ayrı bir plan/cycle kanalıydı. v1.11.0 o kanalı kaldırdı: nokta artık
    /// şeritle aynı statü rengini taşır, tooltip'i yoktur ve listede tooltip taşıyan TEK öğe uyarı
    /// üçgenidir (§9-13).</para></summary>
    [StaFact]
    public void The_status_dot_carries_no_tooltip_at_all()
    {
        var vm = NewVm();
        var row = new ProjectRow { DataContext = vm.Projects[0] };
        DsResources.Realize(DsResources.NewHost(), row);

        Assert.Null(row.Dot.ToolTip);
        GC.KeepAlive(row);
    }

    /// <summary>[design v1.11.0 §2.4-6] Uyarı üçgeninin tooltip'i TEK SATIRDIR.
    /// <para><b>[DEĞİŞEN KURAL]</b> §2.4-6 (v1.7.0) nedenleri alt alta listeliyordu ve döngü YOLUNU tooltip'in
    /// ikinci satırı olarak veriyordu. v1.11.0 tooltip'i tek cümleye indirdi: <i>döngü yolu, üye listesi ve
    /// gerekçe proje LOGUNDADIR</i>. Yolun kendisi silinmedi (yukarıdaki testler onu pinler) — yalnız
    /// tooltip'teki tüketicisi kalktı.</para></summary>
    [StaFact]
    public void The_warning_triangle_tooltip_is_a_single_line_without_the_cycle_path()
    {
        var vm = NewVm();
        var row = new ProjectRow { DataContext = vm.Projects[0] };
        DsResources.Realize(DsResources.NewHost(), row);

        string tip = Assert.IsType<string>(row.DepTooltip);
        Assert.Equal(RowWarning.InCycle, tip);
        Assert.DoesNotContain(Environment.NewLine, tip, StringComparison.Ordinal);
        Assert.DoesNotContain("→", tip, StringComparison.Ordinal); // yol tooltip'te DEĞİL, logda
        GC.KeepAlive(row);
    }

    /// <summary>[design v1.11.0 §2.4-6] Üçgen HER ZAMAN amberdir — yapısal/geçici ayrımı ARTIK renkle
    /// yapılmaz (turuncu UI'dan çıktı), tooltip'in cümlesiyle yapılır.</summary>
    [StaFact]
    public void The_warning_triangle_is_always_amber_even_for_a_structural_cycle()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var row = new ProjectRow { DataContext = vm.Projects[0] };
        DsResources.Realize(host, row);

        Assert.Equal(Visibility.Visible, row.DepIcon.Visibility);
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(row.DepTriangle.Stroke));
        GC.KeepAlive(row);
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
