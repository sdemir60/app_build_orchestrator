using System.Windows;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Spikes;

/// <summary>
/// [It-4a Foundation] Dev-only lab penceresi (--it4a-lab bayrağı). [T56/3a] Konsol demo: colorizer (saat=faint,
/// ▸=amber, tip renkleri), hibrit aktif-satır typewriter (7×13px Rectangle imleç, 1.1s blink), anlatı↔proje-log
/// mod geçişi ve boş-durum metinleri gözle doğrulanır. DI/EngineHost kurulmaz; veri temsilidir.
/// </summary>
public partial class It4aLabWindow : Window
{
    // design-v1 §2.5/§3.1 örnek anlatı satırları — colorizer'ın her dalını (info/cmd/warn/error/success + saat) sergiler.
    private static readonly string[] NarrativeSample =
    [
        "12:04:05 Build Orchestrator 2.4.1 — Osys.sln loaded (36 projects) · main",
        "12:04:06 ▸ git fetch origin main",
        "12:04:06 HEAD b7e91d4 — computing osys-state diff",
        "12:04:07 Sync complete — 7 changed projects, 14 to build",
        "12:04:07 warning: OSYS.Sales.Core failed in this run — last successful output referenced (yesterday 18:42)",
        "12:04:08 OSYS.Base build succeeded (0.4s)",
        "12:04:11 OSYS.Domain.Service failed — 2 errors (3.1s)",
    ];

    private static readonly string[] FailedLogSample =
    [
        "  Determining projects to restore...",
        "  Restored OSYS.Sales.Core.csproj (in 412 ms).",
        "OSYS.Sales.Core -> obj\\Debug\\net10.0\\OSYS.Sales.Core.dll",
        "Program.cs(42,17): error CS0103: The name 'Foo' does not exist in the current context",
        "Program.cs(48,9): error CS1002: ; expected",
        "Build FAILED.",
        "    2 Error(s)",
    ];

    // [3b] Kaskatı ve build-in-progress'i gözle görmek için daha uzun, building bir proje logu.
    private static readonly string[] BuildingLogSample =
    [
        "12:04:07 ▸ msbuild OSYS.Server.Api.csproj /m:4 /p:Configuration=Debug",
        "  Determining projects to restore...",
        "  Restored OSYS.Server.Api.csproj (in 388 ms).",
        "  Restored OSYS.Domain.Service.csproj (in 401 ms).",
        "OSYS.Base -> obj\\Debug\\net10.0\\OSYS.Base.dll",
        "OSYS.Domain.Service -> obj\\Debug\\net10.0\\OSYS.Domain.Service.dll",
        "  Compiling OSYS.Server.Api ...",
        "  ApiControllers.cs -> generated routes (24)",
        "  Emitting OSYS.Server.Api.dll ...",
        "12:04:09 warning: referenced outputs may be stale (Sales.Core)",
        "  Linking references ...",
    ];

    public It4aLabWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SeedNarrative();
            LoadSampleLayers(); // [T58] açılışta 6 örnek katmanla göster (prototip "Load sample layers")
        };
    }

    // [T58] SampleGraphData'nın 36 OSYS düğümünü katman indeksine göre 6 gruba böl (build sırası korunur —
    // Nodes zaten L0..L5 sıralı). Grup adı = Layers[i].Name (caps başlık) + satır adedi başlıkta gösterilir.
    private void LoadSampleLayers()
    {
        var groups = SampleGraphData.Layers
            .Select(layer => new StickyLayerList.LayerGroup(
                layer.Name,
                SampleGraphData.Nodes.Where(n => n.Layer == layer.Id).Cast<object>().ToList()))
            .Where(g => g.Rows.Count > 0)
            .ToList();
        LabProjects.SetGroups(groups);
        ProjectsMode.Text = $"{groups.Count} layers";
    }

    // [T58] Varsayılan: katman YOK → tek başlıksız liste, build sırasında (sticky devrede değil).
    private void LoadFlatProjects()
    {
        LabProjects.SetGroups([new StickyLayerList.LayerGroup("", SampleGraphData.Nodes.Cast<object>().ToList())]);
        ProjectsMode.Text = "build-order";
    }

    private void OnProjectsLayers(object sender, RoutedEventArgs e) => LoadSampleLayers();
    private void OnProjectsFlat(object sender, RoutedEventArgs e) => LoadFlatProjects();

    private void SeedNarrative()
    {
        // [3b] ShowRunDocument: proje modunu/kaskatı sıfırlar + render dilimini (son 200) uygular.
        LabConsole.ShowRunDocument(string.Join('\n', NarrativeSample) + "\n");
        LabHeader.ShowNarrative(NarrativeSample.Length);
    }

    private void OnSeedNarrative(object sender, RoutedEventArgs e) => SeedNarrative();

    private void OnTypeLine(object sender, RoutedEventArgs e)
        => LabConsole.TypeActiveLine("12:04:12 ▸ msbuild Osys.sln /m:4 /p:Configuration=Debug — 14 projects, 22 skipped");

    // [3b] Kaskat + copy-log gözle doğrulama: PlayCascade (26ms/3 satır + 140ms/satır fade), building'de amber ▮.
    private void OnProjectLogFailed(object sender, RoutedEventArgs e)
    {
        LabHeader.ShowProjectLog("OSYS.Sales.Core", ProjectRowState.Failed, hasDepIssue: true, lineCount: FailedLogSample.Length);
        LabHeader.LogTextProvider = () => string.Join('\n', FailedLogSample);
        LabConsole.PlayCascade(FailedLogSample, buildInProgress: false);
    }

    private void OnProjectLogBuilding(object sender, RoutedEventArgs e)
    {
        LabHeader.ShowProjectLog("OSYS.Server.Api", ProjectRowState.Started, hasDepIssue: false, lineCount: BuildingLogSample.Length);
        LabHeader.LogTextProvider = () => string.Join('\n', BuildingLogSample);
        LabConsole.PlayCascade(BuildingLogSample, buildInProgress: true);
    }

    private void OnProjectLogSkipped(object sender, RoutedEventArgs e)
    {
        LabHeader.ShowProjectLog("OSYS.Base", ProjectRowState.Skipped, hasDepIssue: false, lineCount: 0);
        LabConsole.PlayCascade([ConsoleEmptyState.Skipped("a3f81c2")], buildInProgress: false);
    }

    private void OnProjectLogQueued(object sender, RoutedEventArgs e)
    {
        LabHeader.ShowProjectLog("OSYS.Server.Api", ProjectRowState.Pending, hasDepIssue: false, lineCount: 0);
        LabConsole.PlayCascade([ConsoleEmptyState.Queued(["Sales.Core", "Security"])], buildInProgress: false);
    }

    private void OnBackToNarrative(object sender, RoutedEventArgs e) => SeedNarrative();

    // ---------------------------------------------------------------- [T59] scroll/follow/pill demo

    // Bir tick'te birden çok satır ilerlenir: tek satır (36px) çoğu zaman 54px dead-band'in ALTINDA kalır
    // (bilerek — spec'in "hedef sapması <54px ise dokunulmaz" kuralı), bu yüzden gözle DOĞRULANABİLİR bir hareket
    // için birkaç satır atlanır (gerçek koşuda bağımlılık katmanları paralel bitince frontier de böyle sıçrar).
    private const int SimStepRows = 2;
    private static readonly TimeSpan SimTickInterval = TimeSpan.FromMilliseconds(600); // > 550ms throttle penceresi

    private DispatcherTimer? _simTimer;
    private int _simIndex;

    private void OnStartSim(object sender, RoutedEventArgs e)
    {
        _simTimer?.Stop();
        _simIndex = 0;
        LoadSampleLayers(); // [T59] Metrics/FollowScrollController'ı taze kurar — satır indeksleri SampleGraphData.Nodes ile hizalanır (Nodes zaten L0..L5 sıralı)
        LabConsole.ShowRunDocument(""); // temiz sayfa — BottomAnchor yeniden dibe yapışır (T59)
        LabListPill.Visibility = Visibility.Collapsed;
        LabHeader.ShowNarrative(0);

        _simTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = SimTickInterval };
        _simTimer.Tick += OnSimTick;
        _simTimer.Start();
    }

    private void OnStopSim(object sender, RoutedEventArgs e)
    {
        _simTimer?.Stop();
        _simTimer = null;
    }

    // Her tick: frontier'i (T59 FollowScrollController) ilerlet + konsola bir "building" satırı akıt + liste-pill'i
    // (elle kaydırılıp follow durduysa) göster. Konsolun KENDİ `⌄ latest` pill'i ConsoleView'ın içinde, ayrıca kod
    // gerekmez.
    private void OnSimTick(object? sender, EventArgs e)
    {
        if (_simIndex >= SampleGraphData.Nodes.Count) _simIndex = 0; // sürekli demo için baştan sar
        var node = SampleGraphData.Nodes[_simIndex];

        LabProjects.FollowRow(_simIndex);
        LabConsole.AppendBatch($"{DateTime.Now:HH:mm:ss} ▸ building {node.Name} ({node.DurationMs}ms)\n");
        LabListPill.Visibility = LabProjects.IsFollowSuppressedByUser ? Visibility.Visible : Visibility.Collapsed;

        _simIndex += SimStepRows;
    }

    // [T59] Liste pill tıklaması: "frontier'e dön" — kullanıcı-suppress'i yok sayarak son bilinen frontier satırına
    // yumuşak döner ve pill'i gizler (design-v1'in dip-pili değil, follow-mode'a özgü bir varyant — bkz. task-5-report.md).
    private void OnResumeFollowClick(object sender, RoutedEventArgs e)
    {
        int row = Math.Clamp(_simIndex, 0, SampleGraphData.Nodes.Count - 1);
        LabProjects.ResumeFollow(row);
        LabListPill.Visibility = Visibility.Collapsed;
    }
}
