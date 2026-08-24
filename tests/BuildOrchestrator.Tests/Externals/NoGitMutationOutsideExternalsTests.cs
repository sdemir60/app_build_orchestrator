using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D7] Mutasyon yapan git komutlarının kaynak-tarayan guard'ı: ana repo git açısından SALT-OKURDUR ve
/// çalışma ağacını değiştiren tek yüzey <c>Core/Externals</c> içindedir.
///
/// <para><b>Neden var:</b> harici projeler özelliğiyle birlikte kod tabanına ilk kez bir <c>merge</c> girdi.
/// O komut yanlış köke — kullanıcının OSYS reposuna — verilirse çalışma kopyası aracın altında değişir; bu,
/// bu projenin en pahalı sessiz arızası olurdu. İnceleme dikkatine güvenmek yerine sınır burada çitlenir.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> yalnız harici çalışma kopyalarını güncelleyen dosya ve havuz
/// worktree'lerini kuran/sıfırlayan dosya. Adet PİNLENMEZ ama dosya listesi pinlenir — yeni bir dosyaya
/// mutasyon komutu eklemek guard'ı kırmızıya çeker.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> komut adını çalışma zamanında birleştirmek
/// (<c>"mer" + "ge"</c>) ya da argümanları bir listeden okumak. Guard literal çağrı biçimine bakar; niyetin
/// denetimi review'ın işidir.</para>
/// </summary>
public sealed class NoGitMutationOutsideExternalsTests
{
    /// <summary>
    /// Çalışma ağacını ya da branch ref'lerini değiştiren git fiilleri — argüman listesi literali biçiminde.
    /// <c>fetch</c> BURADA YOKTUR: yalnız <c>refs/remotes/*</c>'ı günceller ve Sync'in temelidir.
    /// </summary>
    private static readonly Regex MutatingGitVerb = new(
        "\"(?:merge|checkout|switch|pull|rebase|cherry-pick|stash|clean|reset|commit|push)\"",
        RegexOptions.Compiled);

    /// <summary>Mutasyonun MEŞRU olduğu yollar (src köküne göre) ve gerekçeleri.</summary>
    private static readonly IReadOnlyCollection<string> Allowed =
    [
        // Harici çalışma kopyasını ilerleten tek yüzey: merge-base kararı + merge --ff-only (§10.6).
        @"BuildOrchestrator.Core\Externals\ExternalGitUpdater.cs",
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
    public void The_external_updater_is_the_only_place_that_merges()
    {
        // Ana repo salt-okur kalmalı: 'merge' tek bir dosyada, tek biçimde (--ff-only) yaşar.
        var mergeUsers = SourceGuard.ScanSrc("*.cs", new Regex("\"merge\"|\"merge-base\"", RegexOptions.Compiled),
            allowedFiles: null, skipCommentLines: true);

        Assert.All(mergeUsers, offender =>
            Assert.StartsWith(@"BuildOrchestrator.Core\Externals\ExternalGitUpdater.cs", offender, StringComparison.Ordinal));
        Assert.NotEmpty(mergeUsers); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_guard_actually_scans_the_production_tree()
    {
        // Boş bir tarama guard'ı sessizce yeşil bırakırdı.
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");

        Assert.Contains(@"BuildOrchestrator.Core\Externals\ExternalGitUpdater.cs", scanned);
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
}
