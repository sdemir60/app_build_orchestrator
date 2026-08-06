using System.Diagnostics;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>UI thread'i hiçbir şart ve koşulda kilitlenmez</b> — Sync ve run yollarının bütçe guard'ı, gerçek OSYS
/// ölçeğinde (177 proje, 6 katman).
///
/// <para><b>Neden var:</b> bu yollar ölçülene kadar sessizce büyüyordu. Ölçüm (fix öncesi, aynı makine):
/// ilk Sync ~1.100 ms tek parça blok, filtre değişimi ~694 ms, envanter yayını Sync başına 17.851 ms.
/// Kök nedenler ayrı ayrı düzeltildi (<see cref="InventoryPublishTests"/> envanteri,
/// <see cref="ListRealizationPerfTests"/> liste realizasyonunu pinler); burada uçtan uca <b>üretim yolu</b>
/// korunur — VM + liste + graf + kabuk birlikte.</para>
///
/// <para><b>Bütçe:</b> tek bir UI bloğu <see cref="BudgetMs"/>'yi aşamaz. Sayı bir viewport dolusu satırın
/// indirgenemez kurulum maliyetinden türer (bkz. <see cref="ListRealizationPerfTests"/>); asıl korunan şey
/// bloğun repo BÜYÜKLÜĞÜYLE ölçeklenmemesidir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UiResponsivenessBudgetTests(ITestOutputHelper output)
{
    private const int ProjectCount = 177;   // gerçek OSYS: 177 .csproj
    /// <summary>Tek UI bloğu tavanı — türetme <c>ListRealizationPerfTests.BudgetMs</c>'te. [quiet] internal:
    /// grafın "177 proje aynı tick'te bitti" ölçümü (<c>GraphRunLifecycleTests</c>) AYNI tavanı okur —
    /// ikinci bir sayı yazılmaz (kopya YASAK, CLAUDE.md).</summary>
    internal const double BudgetMs = 120;
    /// <summary>Koşan bir run'da TEK event'in tavanı (akış boyunca sürekli tekrarlar). [quiet] internal:
    /// grafın hold-fade ölçümü (<c>GraphRunLifecycleTests</c>) AYNI bütçeyi okur — ikinci bir sayı yazılmaz
    /// (kopya YASAK, CLAUDE.md).</summary>
    internal const double EventBudgetMs = 50;

    private static List<ProjectNode> Topology(int count)
    {
        var nodes = new List<ProjectNode>(count);
        for (int i = 0; i < count; i++)
        {
            int layer = i % 6;
            var deps = i >= 6 ? new List<string> { $@"C:\p\Osys.Proj{i - 6}.csproj" } : new List<string>();
            nodes.Add(new ProjectNode(
                $@"C:\p\Osys.Proj{i}.csproj", $"Osys.Proj{i}", $@"C:\p\Osys.Proj{i}.csproj",
                ["Osys"], deps, i, layer, $"Layer{layer}", false, null));
        }
        return nodes;
    }

    private static double MsOf(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Bir Sync'in TÜM adımları tek tek bütçe içinde kalmalı — hiçbiri repo büyüklüğüyle ölçeklenmemeli.</summary>
    [StaFact]
    public void No_single_step_of_a_sync_blocks_the_ui_thread_beyond_the_budget()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var nodes = Topology(ProjectCount);
        var preview = nodes.Select(n => new BuildPreviewItem(n.Id, n.Name, true, "abc1234")).ToList();

        var steps = new (string Name, double Ms)[]
        {
            ("topoloji handler", MsOf(() => vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], [])))),
            ("topoloji layout",  MsOf(content.UpdateLayout)),
            ("buildPreview",     MsOf(() => vm.OnEvent(new BuildPreviewEvent(preview))) + MsOf(content.UpdateLayout)),
            ("syncCompleted",    MsOf(() => vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0)))
                                 + MsOf(content.UpdateLayout)),
            ("2. Sync (aynı topoloji)", MsOf(() => vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], [])))
                                 + MsOf(content.UpdateLayout)),
            ("reveal stagger",   MsOf(window.Shell.ProjectsList.PlayRevealStagger)),
            ("filtre AÇ",        MsOf(() => vm.ToggleFilter("failed")) + MsOf(content.UpdateLayout)),
            ("filtre KAPA",      MsOf(() => vm.ToggleFilter("failed")) + MsOf(content.UpdateLayout)),
        };

        foreach (var (name, ms) in steps) output.WriteLine($"[sync] {name,-24} = {ms,8:N1} ms");
        output.WriteLine($"[sync] realize edilmiş satır = {window.Shell.ProjectsList.RevealRows.Count} / {ProjectCount}");

        var over = steps.Where(s => s.Ms >= BudgetMs).ToList();
        Assert.True(over.Count == 0,
            $"UI thread'ini bütçenin ({BudgetMs:N0} ms) üstünde bloke eden adım(lar): " +
            string.Join(" · ", over.Select(s => $"{s.Name} {s.Ms:N1} ms")));

        // Sanallaştırmanın gerçekten devrede olduğunu pinle: aksi halde bütçe "hızlı makine" sayesinde geçebilirdi.
        Assert.True(window.Shell.ProjectsList.RevealRows.Count < ProjectCount,
            "177 satırın tamamı realize oldu — liste sanallaştırması kapanmış.");
    }

    /// <summary>Koşan bir run'ın event akışında TEK bir event bile bütçeyi aşmamalı: bunlar akış boyunca
    /// yüzlerce kez tekrarlanır, dolayısıyla tavanları Sync'inkinden daha dardır.</summary>
    [StaFact]
    public void No_single_project_event_of_a_run_blocks_the_ui_thread_beyond_the_budget()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology(ProjectCount);
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));

        double worst = 0;
        string worstName = "";
        double total = MsOf(() =>
        {
            foreach (var node in nodes)
            {
                double started = MsOf(() => vm.OnEvent(new ProjectStartedEvent("r1", node.Id, node.Name)));
                double done = MsOf(() => vm.OnEvent(new ProjectSucceededEvent("r1", node.Id, 120)));
                if (started > worst) { worst = started; worstName = $"{node.Name} started"; }
                if (done > worst) { worst = done; worstName = $"{node.Name} succeeded"; }
            }
            content.UpdateLayout();
        });

        output.WriteLine($"[run] {ProjectCount} proje × 2 event → toplam {total:N1} ms · en kötü TEK event {worst:N1} ms ({worstName})");
        Assert.True(worst < EventBudgetMs,
            $"tek bir proje event'i UI thread'ini {worst:N1} ms bloke etti ({worstName}) — bütçe {EventBudgetMs:N0} ms.");
    }

    /// <summary>Koşarken 200 ms'de bir dönen graf statü itişi (MainWindow._elapsedTimer) — sürekli tekrarlandığı
    /// için ölçülü kalmalı.</summary>
    [StaFact]
    public void The_mid_run_graph_status_tick_stays_negligible()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology(ProjectCount);
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var graph = window.Shell.GraphHost;
        var costs = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var feed = GraphBinder.Nodes(vm.Topology,
                vm.Projects.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase));
            costs.Add(MsOf(() => graph.UpdateStatuses(feed)) + MsOf(content.UpdateLayout));
        }

        double worst = costs.Max();
        output.WriteLine($"[200ms tick] graf statü itişi en kötü {worst:N1} ms");
        Assert.True(worst < EventBudgetMs,
            $"graf statü itişi {worst:N1} ms sürdü — bütçe {EventBudgetMs:N0} ms (saniyede 5 kez döner).");
        GC.KeepAlive(window);
    }
}
