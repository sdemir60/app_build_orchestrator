using System.IO;
using System.Text.RegularExpressions;
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
        Assert.Equal("Preparing worktree for 'release/1.2'", PlanProgressLines.PreparingWorktree("release/1.2"));
    }

    /// <summary>Branch adı yoksa (toggle açık ama seçim yok — aktif branch'in worktree'si) satır yine
    /// anlamlıdır: hazırlık koşuyor ve kullanıcı bunu görmeli.</summary>
    [Fact]
    public void The_worktree_line_survives_a_missing_branch_name()
        => Assert.Equal("Preparing worktree", PlanProgressLines.PreparingWorktree(null));

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
