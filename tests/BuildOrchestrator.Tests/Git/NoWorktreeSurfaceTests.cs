using System.Reflection;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [spec 2026-09-18 §1-1] Worktree modu yoktur: araç her koşuyu kullanıcının çalışma ağacında derler. Bu
/// guard, kaldırılan yüzeyin geri sızmasını çitler.
///
/// <para><b>Neden var:</b> worktree modu kalktığında onun izleri üç yerde yaşıyordu — git'e verilen
/// <c>worktree</c> fiili (havuz kurma/sıfırlama), MSBuild'e verilen <c>BaseIntermediateOutputPath</c>
/// (worktree başına obj izolasyonu) ve <c>StartRunCommand</c>'ın worktree alanları. Üçünden biri geri
/// gelirse tek ağaç iddiası sessizce bozulur.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> fiili çalışma zamanında birleştirmek
/// (<c>"work" + "tree"</c>). Guard literal çağrı biçimine bakar; niyetin denetimi review'ın işidir.
/// Harici kökün kendisi bir linked worktree olabilir (<c>VcsDetector</c>'ın <c>.git</c> dosyası kabulü) —
/// bu bir okuma kararıdır, git fiili değildir ve guard'ın konusu dışındadır.</para>
/// </summary>
public sealed class NoWorktreeSurfaceTests
{
    /// <summary>Bir <c>ArgumentList</c> literalinin İLK elemanı olarak <c>worktree</c> git fiili.</summary>
    private static readonly Regex WorktreeGitVerb = new("\\[\\s*\"worktree\"", RegexOptions.Compiled);

    private static readonly Regex ObjRedirect = new("BaseIntermediateOutputPath", RegexOptions.Compiled);

    [Fact]
    public void No_worktree_git_verb_lives_in_src()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", WorktreeGitVerb, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "src'de git worktree fiili var:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_obj_redirect_lives_in_src()
    {
        // Yorum satırları da sayılır: kavramın kendisi kalktı, onu anlatan yorum da bayattır.
        var offenders = SourceGuard.ScanSrc("*.cs", ObjRedirect);

        Assert.True(offenders.Count == 0,
            "src'de BaseIntermediateOutputPath var:\n  " + string.Join("\n  ", offenders));
    }

    [Theory]
    [InlineData("UseWorktree")]
    [InlineData("WorktreeName")]
    [InlineData("Branch")]
    public void StartRunCommand_carries_no_worktree_or_branch_field(string property)
    {
        Assert.Null(typeof(StartRunCommand).GetProperty(property, BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void The_rule_recognises_a_worktree_verb_that_sneaks_back()
    {
        // Guard'ın kendi kanıtı: sahte bir ihlal gerçekten raporlanıyor mu?
        var offenders = SourceGuard.ScanText("Core/Git/GitService.cs",
            """var r = await CommandLineTool.RunAsync(_runner, "git", exe, ["worktree", "add", path], root, t, ct);""",
            WorktreeGitVerb);

        Assert.Single(offenders);
    }
}
