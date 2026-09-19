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
/// çitlenir. Guard'ın koruduğu şey değişmedi: mutasyonun ikinci bir yere sızmaması.</para>
///
/// <para><b>[DEĞİŞEN KURAL — spec §6.6]</b> Eski iddia "iki dosya (fast-forward yüzeyi + havuz worktree'si),
/// checkout/stash hiçbir yerde" idi: <c>MutatingGitVerb</c> regex'i <c>checkout</c>/<c>stash</c>'i ZATEN
/// tanıyordu ama izin listesindeki hiçbir dosya bu fiilleri kullanmıyordu — araçtan branch değiştirme henüz
/// yoktu. Faz 2 ile birlikte "tek ağaç ve branch" akışı checkout/stash'i gerçek bir ürün özelliği yapar
/// (§6.3) ve bu ikisi ff-only pull'la AYNI dosyaya (<c>Core/Git/RepositoryWriter.cs</c>, sınıf adı
/// <c>BranchSwitcher</c>) eklenir — <c>FastForwardUpdater</c> da git mv ile aynı dosyaya taşınır. Sınıf/dosya
/// adı değişse de kural aynı kalır: mutasyon TEK dosyada yaşar.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> yalnız tek yazım dosyası. Adet PİNLENMEZ ama dosya listesi
/// pinlenir — yeni bir dosyaya mutasyon komutu eklemek guard'ı kırmızıya çeker.</para>
///
/// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1]</b> İzin listesinde ikinci bir dosya vardı: havuz
/// worktree'lerini kuran/sıfırlayan <c>Core/Git/WorktreeManager.cs</c>. Worktree modu kalktı ve dosya
/// silindi; liste artık tek dosyadır. <c>worktree</c> fiilinin geri dönmesini <c>NoWorktreeSurfaceTests</c>
/// ayrıca çitler.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> komut adını çalışma zamanında birleştirmek
/// (<c>"mer" + "ge"</c>) ya da argümanları bir listeden okumak. Guard literal çağrı biçimine bakar; niyetin
/// denetimi review'ın işidir.</para>
/// </summary>
public sealed class NoGitMutationOutsideTheWriterTests
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
        // Kod tabanındaki TEK mutasyon dosyası: merge-base kararı + merge --ff-only (§10.4, FastForwardUpdater)
        // VE branch checkout + stash push (§6.3/§6.6, BranchSwitcher). Hem harici kartlar hem (yalnız
        // kullanıcı chip'e/branch chip'ine bastığında) ana repo buradan geçer.
        @"BuildOrchestrator.Core\Git\RepositoryWriter.cs",
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
            Assert.StartsWith(@"BuildOrchestrator.Core\Git\RepositoryWriter.cs", offender, StringComparison.Ordinal));
        Assert.NotEmpty(mergeUsers); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_writer_file_is_the_only_mutation_surface()
    {
        // 'checkout' da (merge gibi) tek bir dosyada yaşar — BranchSwitcher'ın eklenmesiyle birlikte.
        var checkoutUsers = SourceGuard.ScanSrc("*.cs", new Regex("\"checkout\"", RegexOptions.Compiled),
            allowedFiles: null, skipCommentLines: true);

        Assert.All(checkoutUsers, offender =>
            Assert.StartsWith(@"BuildOrchestrator.Core\Git\RepositoryWriter.cs", offender, StringComparison.Ordinal));
        Assert.NotEmpty(checkoutUsers); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_guard_actually_scans_the_production_tree()
    {
        // Boş bir tarama guard'ı sessizce yeşil bırakırdı.
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");

        Assert.Contains(@"BuildOrchestrator.Core\Git\RepositoryWriter.cs", scanned);
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
