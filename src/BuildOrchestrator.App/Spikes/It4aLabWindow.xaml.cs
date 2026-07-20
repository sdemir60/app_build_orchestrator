using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.ViewModels;
using ICSharpCode.AvalonEdit.Document;

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

    public It4aLabWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => SeedNarrative();
    }

    private void SeedNarrative()
    {
        LabConsole.Document = new TextDocument(string.Join('\n', NarrativeSample) + "\n");
        LabHeader.ShowNarrative(NarrativeSample.Length);
    }

    private void OnSeedNarrative(object sender, RoutedEventArgs e) => SeedNarrative();

    private void OnTypeLine(object sender, RoutedEventArgs e)
        => LabConsole.TypeActiveLine("12:04:12 ▸ msbuild Osys.sln /m:4 /p:Configuration=Debug — 14 projects, 22 skipped");

    private void OnProjectLogFailed(object sender, RoutedEventArgs e)
    {
        LabConsole.Document = new TextDocument(string.Join('\n', FailedLogSample) + "\n");
        LabHeader.ShowProjectLog("OSYS.Sales.Core", ProjectRowState.Failed, hasDepIssue: true, lineCount: FailedLogSample.Length);
    }

    private void OnProjectLogSkipped(object sender, RoutedEventArgs e)
    {
        LabConsole.Document = new TextDocument(ConsoleEmptyState.Skipped("a3f81c2") + "\n");
        LabHeader.ShowProjectLog("OSYS.Base", ProjectRowState.Skipped, hasDepIssue: false, lineCount: 0);
    }

    private void OnProjectLogQueued(object sender, RoutedEventArgs e)
    {
        LabConsole.Document = new TextDocument(ConsoleEmptyState.Queued(["Sales.Core", "Security"]) + "\n");
        LabHeader.ShowProjectLog("OSYS.Server.Api", ProjectRowState.Pending, hasDepIssue: false, lineCount: 0);
    }

    private void OnBackToNarrative(object sender, RoutedEventArgs e) => SeedNarrative();
}
