using System.Reflection;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [worktree] Worktree yüzeyi DARDIR: tek bir kullanıcı-tanımlı worktree, ikinci bir kök olarak. Araç onu yalnız Settings Save'de,
/// yalnız ana kopyanın HEAD'inde detached olarak kurar (<c>WorktreeCreator</c>) ve hangi kopyanın hangi branch'te olduğunu sorar
/// (<c>BranchSwitcher</c>); ikisi de tek mutasyon dosyasındadır (<c>Core/Git/RepositoryWriter.cs</c>). Her koşu yine bir çalışma
/// ağacında, OutDir ve obj'e dokunmadan derlenir.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-11-05]</b> Eski iddia "worktree modu yoktur" idi (spec 2026-09-18 §1-1): git'in
/// <c>worktree</c> fiili src'de HİÇBİR yerde geçemez, sözleşmede worktree tipi ya da ayırt edicisi olamaz. O kural eski havuz modunu
/// (her branch için <c>%LOCALAPPDATA%</c> altında bir worktree, obj izolasyonu, <c>StartRunCommand</c>'ın worktree alanları) dışarıda
/// tutuyordu. Kullanıcı tek, kendi tanımladığı bir worktree'yi geri istedi; bu bir mod değil ikinci bir köktür — eski modun hiçbir
/// parçasını getirmez. Bu yüzden kural daraltıldı, kaldırılmadı: fiil yalnız yazım dosyasında ve yalnız iki biçimde (<c>add
/// --detach</c>, <c>list</c>) yaşar; sözleşmede yalnız oluşturma komutu ve sonucu vardır (ne liste ne silme); obj yönlendirmesi ve
/// koşunun worktree/branch alanı yasağı aynen durur.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> fiili çalışma zamanında birleştirmek (<c>"work" + "tree"</c>). Guard literal çağrı
/// biçimine bakar; niyetin denetimi review'ın işidir. Harici kökün kendisi bir linked worktree olabilir (<c>VcsDetector</c>'ın
/// <c>.git</c> dosyası kabulü) — bu bir okuma kararıdır, git fiili değildir ve guard'ın konusu dışındadır.</para>
/// </summary>
public sealed class NoWorktreeSurfaceTests
{
    private const string Writer = @"BuildOrchestrator.Core\Git\RepositoryWriter.cs";

    /// <summary>Bir <c>ArgumentList</c> literalinin İLK elemanı olarak <c>worktree</c> git fiili.</summary>
    private static readonly Regex WorktreeGitVerb = new("\\[\\s*\"worktree\"", RegexOptions.Compiled);

    /// <summary><c>add</c> ve <c>list</c> dışındaki bir alt komut (<c>remove</c>, <c>prune</c>, <c>move</c>, <c>lock</c>, …).</summary>
    private static readonly Regex OtherWorktreeSubcommand = new("\\[\\s*\"worktree\"\\s*,\\s*\"(?!add\"|list\")", RegexOptions.Compiled);

    /// <summary><c>--detach</c>'siz <c>add</c>: yeni bir branch açar ya da var olanı ikinci kopyada açmaya çalışırdı. <c>add</c>'den
    /// sonraki öğe literal <c>"--detach"</c> değilse — bir değişken (yol) ya da başka bir seçenek — ihlaldir. Boşluk atomik
    /// eşleşir: geri izleyen <c>\s*</c> bir boşluk bırakıp lookahead'i <c>"--detach"</c>'in önündeki boşlukta kandırırdı.</summary>
    private static readonly Regex AddWithoutDetach = new("\\[\\s*\"worktree\"\\s*,\\s*\"add\"\\s*,(?>\\s*)(?!\"--detach\")", RegexOptions.Compiled);

    private static readonly Regex ObjRedirect = new("BaseIntermediateOutputPath", RegexOptions.Compiled);

    [Fact]
    public void The_worktree_git_verb_lives_only_in_the_writer()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", WorktreeGitVerb, [Writer], skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "src'de yazım dosyası dışında git worktree fiili var:\n  " + string.Join("\n  ", offenders));
        Assert.NotEmpty(SourceGuard.ScanSrc("*.cs", WorktreeGitVerb, skipCommentLines: true)); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_writer_only_adds_a_detached_worktree_and_lists_them()
    {
        var other = SourceGuard.ScanSrc("*.cs", OtherWorktreeSubcommand, skipCommentLines: true);
        var undetached = SourceGuard.ScanSrc("*.cs", AddWithoutDetach, skipCommentLines: true);

        Assert.True(other.Count == 0, "add/list dışında worktree alt komutu var:\n  " + string.Join("\n  ", other));
        Assert.True(undetached.Count == 0, "--detach'siz worktree add var:\n  " + string.Join("\n  ", undetached));
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

    /// <summary>
    /// Sözleşmenin worktree yüzeyi yalnız oluşturmadır: <see cref="CreateWorktreeCommand"/> ve sonucu
    /// <see cref="WorktreeCreatedEvent"/>. Eski modun listeleme/silme tipleri (<c>ListWorktreesCommand</c>,
    /// <c>DeleteWorktreeCommand</c>, <c>WorktreeListEvent</c>, <c>Worktree</c> modeli) geri gelmez.
    /// </summary>
    [Fact]
    public void The_contracts_carry_only_the_worktree_creation()
    {
        var types = typeof(IpcCommand).Assembly.GetTypes()
            .Where(t => t.Name.Contains("Worktree", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        var discriminators = new[] { typeof(IpcCommand), typeof(IpcEvent) }
            .SelectMany(t => t.GetCustomAttributes<System.Text.Json.Serialization.JsonDerivedTypeAttribute>())
            .Where(a => a.DerivedType.Name.Contains("Worktree", StringComparison.OrdinalIgnoreCase)
                     || (a.TypeDiscriminator as string ?? "").Contains("worktree", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.TypeDiscriminator?.ToString())
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([nameof(CreateWorktreeCommand), nameof(WorktreeCreatedEvent)], types);
        Assert.Equal(["createWorktree", "worktreeCreated"], discriminators);
    }

    [Fact]
    public void The_rule_recognises_a_worktree_verb_that_sneaks_back()
    {
        // Guard'ın kendi kanıtı: sahte bir ihlal gerçekten raporlanıyor mu?
        var outside = SourceGuard.ScanText("Core/Git/GitService.cs",
            """var r = await CommandLineTool.RunAsync(_runner, "git", exe, ["worktree", "add", path], root, t, ct);""",
            WorktreeGitVerb);
        var remove = SourceGuard.ScanText("Core/Git/RepositoryWriter.cs",
            """var r = await CommandLineTool.RunAsync(_runner, "git", exe, ["worktree", "remove", path], root, t, ct);""",
            OtherWorktreeSubcommand);
        var branchy = SourceGuard.ScanText("Core/Git/RepositoryWriter.cs",
            """var r = await CommandLineTool.RunAsync(_runner, "git", exe, ["worktree", "add", path, branch], root, t, ct);""",
            AddWithoutDetach);

        Assert.Single(outside);
        Assert.Single(remove);
        Assert.Single(branchy);
    }
}
