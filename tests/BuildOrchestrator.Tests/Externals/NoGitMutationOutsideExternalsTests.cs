using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D7] Mutasyon yapan git komutlarının kaynak-tarayan guard'ı: çalışma ağacını değiştiren git komutları TEK
/// bir dosyada yaşar.
///
/// <para><b>Neden var:</b> harici projeler özelliğiyle birlikte kod tabanına ilk kez bir <c>merge</c> girdi.
/// O komut yanlış köke ve yanlış anda verilirse kullanıcının çalışma kopyası aracın altında değişir; bu, bu
/// projenin en pahalı sessiz arızası olurdu. İnceleme dikkatine güvenmek yerine sınır burada çitlenir.</para>
///
/// <para><b>[DEĞİŞEN KURAL — v1.16.0]</b> Eski iddia "mutasyon <c>Core/Externals</c> DIŞINA çıkamaz" idi ve
/// gerekçesi "ana repo hiçbir koşulda ilerletilmez"di. Kural bilinçli olarak güncellendi: kullanıcı alt
/// bardaki <c>N behind</c> chip'ine bastığında ana repo da ff-only ilerletilir (yalnız aktif branch, yalnız
/// kullanıcı tıklamasıyla, asla kendiliğinden). Bu yüzden yüzey artık VCS'e göre değil, DOSYAYA göre
/// çitlenir — tek mutasyon dosyası <c>Core/Git/FastForwardUpdater.cs</c>'tir ve ana repo ile harici kökler
/// aynı ilkeli oradan geçer. Guard'ın koruduğu şey değişmedi: mutasyonun ikinci bir yere sızmaması.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> yalnız fast-forward yüzeyi ve havuz worktree'lerini
/// kuran/sıfırlayan dosya. Adet PİNLENMEZ ama dosya listesi pinlenir — yeni bir dosyaya mutasyon komutu
/// eklemek guard'ı kırmızıya çeker.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> komut adını çalışma zamanında birleştirmek
/// (<c>"mer" + "ge"</c>) ya da argümanları bir listeden okumak. Guard literal çağrı biçimine bakar; niyetin
/// denetimi review'ın işidir.</para>
/// </summary>
public sealed class NoGitMutationOutsideExternalsTests
{
    /// <summary>
    /// Çalışma ağacını ya da branch ref'lerini değiştiren git fiilleri — bir <c>ArgumentList</c> literalinin
    /// İLK elemanı olarak (<c>["merge", ...]</c>); git fiili her zaman listenin başındadır.
    /// <c>fetch</c> BURADA YOKTUR: yalnız <c>refs/remotes/*</c>'ı günceller ve Sync'in temelidir.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski kural HERHANGİ bir <c>"clean"</c>/<c>"reset"</c> string literalini
    /// ihlal sayıyordu. Build menüsünün öğe türleri (<c>new("clean", "Clean", …)</c>, <c>ProjectRowMenu</c>)
    /// aynı sözcükleri UI kimliği olarak taşıyınca guard git'le ilgisi olmayan koda kırmızı verdi. Kural artık
    /// argüman listesinin başındaki fiile bakar — gerçek bir git çağrısı bu biçimden kaçamaz
    /// (<see cref="The_rule_recognises_a_mutation_that_sneaks_into_another_file"/>), UI literalleri ise
    /// bu biçimde yazılmaz (<see cref="The_rule_ignores_a_menu_item_kind_that_happens_to_share_the_word"/>).</para>
    /// </summary>
    private static readonly Regex MutatingGitVerb = new(
        "\\[\\s*\"(?:merge|checkout|switch|pull|rebase|cherry-pick|stash|clean|reset|commit|push)\"",
        RegexOptions.Compiled);

    /// <summary>Mutasyonun MEŞRU olduğu yollar (src köküne göre) ve gerekçeleri.</summary>
    private static readonly IReadOnlyCollection<string> Allowed =
    [
        // Çalışma kopyasını ilerleten tek yüzey: merge-base kararı + merge --ff-only (§10.6). Hem harici
        // kartlar hem (yalnız kullanıcı chip'e bastığında) ana repo buradan geçer.
        @"BuildOrchestrator.Core\Git\FastForwardUpdater.cs",
        // Havuz worktree'lerini kurar ve sıfırlar; üç kapısı (havuz altında, ana kök değil, detached HEAD)
        // ana repoya dokunmasını imkânsız kılar (§10.4).
        @"BuildOrchestrator.Core\Git\WorktreeManager.cs",
    ];

    [Fact]
    public void No_mutating_git_verb_lives_outside_the_allowed_files()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", MutatingGitVerb, Allowed, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "Mutasyon yapan git komutu izinli dosyaların dışında:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_fast_forward_updater_is_the_only_place_that_merges()
    {
        // 'merge' tek bir dosyada, tek biçimde (--ff-only) yaşar — hangi kök verilirse verilsin ilke aynı.
        var mergeUsers = SourceGuard.ScanSrc("*.cs", new Regex("\"merge\"|\"merge-base\"", RegexOptions.Compiled),
            allowedFiles: null, skipCommentLines: true);

        Assert.All(mergeUsers, offender =>
            Assert.StartsWith(@"BuildOrchestrator.Core\Git\FastForwardUpdater.cs", offender, StringComparison.Ordinal));
        Assert.NotEmpty(mergeUsers); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_guard_actually_scans_the_production_tree()
    {
        // Boş bir tarama guard'ı sessizce yeşil bırakırdı.
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");

        Assert.Contains(@"BuildOrchestrator.Core\Git\FastForwardUpdater.cs", scanned);
        Assert.True(scanned.Count > 50, $"Beklenenden az dosya tarandı: {scanned.Count}");
    }

    [Fact]
    public void The_rule_recognises_a_mutation_that_sneaks_into_another_file()
    {
        // Guard'ın kendi kanıtı: sahte bir ihlal gerçekten raporlanıyor mu?
        var offenders = SourceGuard.ScanText("Core/Git/GitService.cs",
            """var r = await CommandLineTool.RunAsync(_runner, "git", exe, ["checkout", branch], root, t, ct);""",
            MutatingGitVerb);

        Assert.Single(offenders);
    }

    [Fact]
    public void The_rule_ignores_a_menu_item_kind_that_happens_to_share_the_word()
    {
        // UI öğe türleri git fiili değildir: "clean" bir menü kimliği olarak da yaşar (BuildMenu, ProjectRowMenu).
        var offenders = SourceGuard.ScanText("App/Views/BuildMenu.xaml.cs",
            """
            new("clean", "Clean", "Remove build outputs", null);
            if (item.Kind == "clean") { }
            [("build", "Build"), ("clean", "Clean")];
            """,
            MutatingGitVerb);

        Assert.Empty(offenders);
    }
}
