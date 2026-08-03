using System.Diagnostics;
using System.Windows;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// GEÇİCİ TANI (systematic-debugging Faz 1) — Sync sırasında UI thread'inin BLOKLANDIĞI yeri ölçer.
/// Gerçek OSYS ölçeği: 177 proje. Hiçbir iddia yok; yalnız kırılım yazdırır.
/// </summary>
[Collection("Console UI (serial)")]
public class SyncUiBlockDiagnosticTests(ITestOutputHelper output)
{
    private const int ProjectCount = 177;

    private static List<ProjectNode> Topology(int count, string suffix = "")
    {
        var nodes = new List<ProjectNode>(count);
        for (int i = 0; i < count; i++)
        {
            // Katmanlar: 6 katmana yay (gerçek repo gibi) + bir önceki katmandan bağımlılık
            int layer = i % 6;
            var deps = i >= 6 ? new List<string> { $@"C:\p\Osys.Proj{i - 6}.csproj" } : new List<string>();
            nodes.Add(new ProjectNode(
                $@"C:\p\Osys.Proj{i}{suffix}.csproj", $"Osys.Proj{i}{suffix}", $@"C:\p\Osys.Proj{i}{suffix}.csproj",
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

    [StaFact]
    public void Measure_ui_thread_block_of_a_sync_at_osys_scale()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var nodes = Topology(ProjectCount);
        var preview = nodes.Select(n => new BuildPreviewItem(n.Id, n.Name, true, "abc1234")).ToList();

        // --- 1) İLK Sync: topoloji event'i (liste SetGroups + graf SetGraph + VM uzlaştırma)
        double topologyMs = MsOf(() => vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], [])));
        double layoutMs = MsOf(content.UpdateLayout);

        // --- 2) buildPreview (177 satıra WillBuild + sha itilir)
        double previewMs = MsOf(() => vm.OnEvent(new BuildPreviewEvent(preview)));
        double previewLayoutMs = MsOf(content.UpdateLayout);

        // --- 3) syncCompleted (TargetSha 177 satıra itilir)
        double completedMs = MsOf(() => vm.OnEvent(
            new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0)));
        double completedLayoutMs = MsOf(content.UpdateLayout);

        // --- 4) İKİNCİ Sync, AYNI topoloji (imza guard'ı tutmalı → ucuz olmalı)
        double secondSameMs = MsOf(() => vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], [])));
        double secondSameLayoutMs = MsOf(content.UpdateLayout);

        // --- 5) reveal stagger (liste kademeli beliriş) — üretimde Loaded önceliğinde koşar
        double revealMs = MsOf(window.Shell.ProjectsList.PlayRevealStagger);

        // --- 6) Filtre aktifken bir satırın statüsü değişirse (görünür küme değişir → tam reset)
        int realizedRows = window.Shell.ProjectsList.RevealRows.Count;

        // --- 6) Filtre aktifken görünür küme değişir → tam reset (177 satır teardown)
        double filterToggleMs = MsOf(() => vm.ToggleFilter("failed"));
        double filterMs = MsOf(content.UpdateLayout) + filterToggleMs;
        double unfilterMs = MsOf(() => vm.ToggleFilter("failed")) + MsOf(content.UpdateLayout);

        output.WriteLine($"[sync] topology handler   = {topologyMs,8:N1} ms");
        output.WriteLine($"[sync] layout after topo  = {layoutMs,8:N1} ms");
        output.WriteLine($"[sync] buildPreview       = {previewMs,8:N1} ms  (+ layout {previewLayoutMs:N1} ms)");
        output.WriteLine($"[sync] syncCompleted      = {completedMs,8:N1} ms  (+ layout {completedLayoutMs:N1} ms)");
        output.WriteLine($"[sync] 2nd same topology  = {secondSameMs,8:N1} ms  (+ layout {secondSameLayoutMs:N1} ms)");
        output.WriteLine($"[sync] reveal stagger     = {revealMs,8:N1} ms");
        output.WriteLine($"[sync] filtre AÇ (177→0)  = {filterMs,8:N1} ms");
        output.WriteLine($"[sync] filtre KAPA (0→177)= {unfilterMs,8:N1} ms");
        output.WriteLine($"[sync] TOPLAM ilk Sync    = {topologyMs + layoutMs + previewMs + previewLayoutMs + completedMs + completedLayoutMs + revealMs,8:N1} ms");
        output.WriteLine($"[sync] realize edilmiş satır = {realizedRows}");
        GC.KeepAlive(window);
    }

    /// <summary>Aynı Sync'i graf paneli KAPALIYKEN (List modu) ölçer — fark = grafın payı.</summary>
    [StaFact]
    public void Measure_the_same_sync_with_the_graph_panel_collapsed()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        window.Shell.SetMode(BuildOrchestrator.App.Shell.LayoutMode.List); // graf Collapsed
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var nodes = Topology(ProjectCount);
        double topologyMs = MsOf(() => vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], [])));
        double layoutMs = MsOf(content.UpdateLayout);

        output.WriteLine($"[list-only] topology handler = {topologyMs,8:N1} ms");
        output.WriteLine($"[list-only] layout          = {layoutMs,8:N1} ms");
        output.WriteLine($"[list-only] realize satır   = {window.Shell.ProjectsList.RevealRows.Count}");
        GC.KeepAlive(window);
    }

    /// <summary>Koşan bir run'da (filtre AÇIK) her proje statü değişiminin liste maliyeti.</summary>
    [StaFact]
    public void Measure_list_cost_per_project_event_while_a_filter_is_active()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology(ProjectCount);
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        vm.ToggleFilter("failed");
        content.UpdateLayout();

        // Her failed proje görünür kümeye girer → liste tam reset + realize
        var costs = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            costs.Add(MsOf(() =>
            {
                vm.OnEvent(new ProjectFailedEvent("r1", nodes[idx].Id, 10, "MSB1234"));
                content.UpdateLayout();
            }));
        }

        output.WriteLine($"[filtre açıkken] proje başına liste maliyeti = {string.Join(" · ", costs.Select(c => $"{c:N1} ms"))}");
        GC.KeepAlive(window);
    }

    /// <summary>Sync'in branch envanterini yayınlaması: OSYS'de 475 branch (refs/heads + refs/remotes).
    /// BranchPopover ÖMÜR BOYU Branches.CollectionChanged'e abone ve her bildirimde TÜM satırlarını yeniden kurar;
    /// RunViewModel.Replace ise N kez RemoveAt + N kez Add yapar → 2N bildirim.</summary>
    [StaFact]
    public void Measure_the_branch_inventory_publish_at_osys_scale()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        foreach (int branchCount in new[] { 55, 200, 475 })
        {
            var branches = Enumerable.Range(0, branchCount)
                .Select(i => new BranchRef(i == 0 ? "main" : $"feature/branch-{i}", $"sha{i:D6}", i == 0, i > 55))
                .ToList();
            // İkinci yayın (envanter zaten doluyken) üretimdeki HER Sync'in yoludur.
            double first = MsOf(() => vm.OnEvent(new BranchListEvent(branches)));
            double second = MsOf(() => vm.OnEvent(new BranchListEvent(branches)));
            output.WriteLine($"[branch] {branchCount,3} branch → ilk yayın {first,8:N1} ms · sonraki Sync {second,8:N1} ms");
        }
        GC.KeepAlive(window);
    }

    /// <summary>Koşarken 200 ms'de bir dönen graf statü itişinin (MainWindow._elapsedTimer) maliyeti.</summary>
    [StaFact]
    public void Measure_the_mid_run_graph_status_tick()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology(ProjectCount);
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var binder = window.Shell.GraphHost;
        var costs = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var feed = GraphBinder.Nodes(vm.Topology, vm.Projects.ToDictionary(p => p.Id, p => p, StringComparer.OrdinalIgnoreCase));
            costs.Add(MsOf(() => binder.UpdateStatuses(feed)) + MsOf(content.UpdateLayout));
        }
        output.WriteLine($"[200ms tick] graf statü itişi = {string.Join(" · ", costs.Select(c => $"{c:N1}"))} ms");
        GC.KeepAlive(window);
    }
}
