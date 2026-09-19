using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// Planlama adım satırlarının TEK kaynağı. Sync ve run planlaması AYNI işi yapar (tarama → değerlendirme →
/// graf → topo) ve kullanıcıya AYNI satırları göstermelidir; Sync bu satırları yazıyordu, Build'e basınca
/// koşan planlama ise TEK SATIR BİLE yazmıyordu — konsol temizlenip boş kalıyor, şerit önceki metinde
/// donuyordu ("Build'e bastım hiçbir şey olmadı").
///
/// <para>Metinler Core'da toplanır çünkü iki çağıran vardır (<c>SyncWorkspaceService</c> ve Supervisor'ın
/// <c>BuildRunPlan</c>'ı) ve CLAUDE.md kopya yasağı aynı metnin iki yerde tanımlanmasını yasaklar: biri
/// güncellenip diğeri unutulursa kullanıcı aynı işin iki farklı adını görürdü.</para>
/// </summary>
public sealed class PlanProgressLinesTests
{
    /// <summary>Sayılar gerçek plandan gelir — satırların biçimi burada pinlenir (Sync tarafındaki uçtan uca
    /// pin <c>SyncWorkspaceServiceTests.Sync_prints_the_granular_scan_steps_after_the_fetch_line</c>).</summary>
    [Fact]
    public void Each_step_line_renders_its_own_counts()
    {
        Assert.Equal("Scanning solutions (12)", PlanProgressLines.ScanningSolutions(12));
        Assert.Equal("Reading HintPath/Compile items (177 projects)", PlanProgressLines.ReadingProjectItems(177));
        Assert.Equal("Dependency graph — 0 cycles", PlanProgressLines.DependencyGraph(0));
        Assert.Equal("Build order resolved (177)", PlanProgressLines.BuildOrderResolved(177));
        Assert.Equal("Computing incremental state (177 projects)", PlanProgressLines.ComputingIncremental(177));
    }

    /// <summary>
    /// [v1.16.0] Sync'in mesafe satırı. Uzak uçtaki commit'in KİMLİĞİ yazılmaz — kullanıcı onu pull etmedikçe
    /// yereldeki hiçbir şeyi anlatmaz; anlamlı olan tek şey MESAFEDİR.
    /// </summary>
    [Theory]
    [InlineData(null, "HEAD a3f81c2")]
    [InlineData(0, "HEAD a3f81c2 · up to date with origin/main")]
    [InlineData(1, "HEAD a3f81c2 · 1 commit behind origin/main")]
    [InlineData(3, "HEAD a3f81c2 · 3 commits behind origin/main")]
    public void The_head_line_reports_the_distance_from_the_remote(int? behind, string expected)
        => Assert.Equal(expected, PlanProgressLines.HeadDistance("a3f81c2", behind, "main"));

    /// <summary>[spec 2026-09-18 §6.3] Branch chip'inden checkout'un satırları: App onları
    /// <c>CheckoutCompletedEvent</c>'ten kurar — başarıda temizlenen konsolun ilk satırları, reddetmede
    /// korunan konsolun altına eklenen uyarı.
    /// <para><b>[DEĞİŞEN KURAL — Task 7]</b> Eski iddia: reddetme ve hata satırları öneksizdi
    /// (<c>3 files have uncommitted changes — …</c>, <c>Switch failed — …</c>) ve konsolda düz tonda kalıyordu.
    /// Değişme gerekçesi: bir git reddi gözden kaçıyordu; konsol satırı METNİNDEN boyadığı için
    /// (§13.5) amber'in tek yolu <c>warning:</c> önekidir — bkz.
    /// <see cref="Git_refusal_and_failure_lines_carry_the_warning_prefix_so_the_console_colours_them_amber"/>.</para></summary>
    [Fact]
    public void The_branch_switch_lines_say_what_happened_and_what_to_do()
    {
        Assert.Equal("Switched to feature/x (b7e91d4) — from main",
            PlanProgressLines.SwitchedBranch("main", "feature/x", "b7e91d4"));
        Assert.Equal(
            "Stashed uncommitted changes: \"build-orchestrator: leaving main for feature/x\" — restore them with git stash pop",
            PlanProgressLines.StashedBeforeSwitch("build-orchestrator: leaving main for feature/x"));
        Assert.Equal("warning: 3 files have uncommitted changes — commit or stash them first",
            PlanProgressLines.SwitchRefusedDirty(3));
        Assert.Equal("warning: 1 file has uncommitted changes — commit or stash them first",
            PlanProgressLines.SwitchRefusedDirty(1));
        Assert.Equal("warning: switch failed — pathspec 'x' did not match", PlanProgressLines.SwitchFailed("pathspec 'x' did not match"));
    }

    /// <summary>[Task 7] Kirli ağaç reddi, checkout hatası ve pull redleri artık <c>warning:</c> önekiyle
    /// gider — konsolun TEK renklendirme sözleşmesi (<see cref="ConsoleLineClassifier"/>) metinden türetildiği
    /// için (§13.5, Level alanından DEĞİL) bu önek bu satırların amber boyanmasının TEK yoludur; yeni bir
    /// sınıflandırma mekanizması İCAT EDİLMEZ.</summary>
    [Fact]
    public void Git_refusal_and_failure_lines_carry_the_warning_prefix_so_the_console_colours_them_amber()
    {
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(PlanProgressLines.SwitchRefusedDirty(3)));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(PlanProgressLines.SwitchFailed("exit 1")));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(PlanProgressLines.PullRefusedDirty()));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(PlanProgressLines.PullRefusedDiverged("main")));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(PlanProgressLines.PullRefusedDetached()));
    }

    /// <summary>[spec 2026-09-18 §6.2] Kesilen koşunun özeti: kaç proje bitti, kaçı derlenmedi, log klasörü —
    /// klasör bilinmiyorsa ek yazılmaz.</summary>
    [Fact]
    public void The_interrupted_run_summary_names_the_counts_and_the_log_folder()
    {
        Assert.Equal(@"Run interrupted by a branch change — 3 built, 5 not built · logs: D:\logs\run",
            PlanProgressLines.RunInterruptedByBranchChange(3, 5, @"D:\logs\run"));
        Assert.Equal("Run interrupted by a branch change — 0 built, 1 not built",
            PlanProgressLines.RunInterruptedByBranchChange(0, 1, null));
    }

    /// <summary>Mesafe bilinmiyorsa (fetch degrade / başka branch seçili) satır SUSAR: uydurma bir sayı
    /// yazmak, chip'in de yanlış çıkmasına yol açardı.</summary>
    [Fact]
    public void An_unknown_distance_leaves_the_line_with_the_head_alone()
        => Assert.DoesNotContain("behind", PlanProgressLines.HeadDistance("a3f81c2", null, "main"));

    /// <summary>Kopya yasağı (CLAUDE.md): bu dört satır iki tüketicilidir, bu yüzden metinleri ÜRETİM ağacında
    /// yalnız <see cref="PlanProgressLines"/> tanımlayabilir. Guard olmadan run planlamasına "Scanning
    /// solutions…" inline yazılır ve iki akış sessizce ayrışırdı.</summary>
    [Fact]
    public void The_shared_step_lines_have_exactly_one_source_in_the_production_tree()
    {
        var rule = new Regex(
            "Scanning solutions|Reading HintPath/Compile items|Dependency graph —|Build order resolved",
            RegexOptions.Compiled);
        string owner = Path.Combine("BuildOrchestrator.Core", "Planning", "PlanProgressLines.cs");

        Assert.Contains(owner, SourceGuard.ScannedSrcFiles("*.cs")); // tarama sahibi dosyayı GÖRÜYOR mu
        Assert.Empty(SourceGuard.ScanSrc("*.cs", rule, [owner], skipCommentLines: true));
    }
}
