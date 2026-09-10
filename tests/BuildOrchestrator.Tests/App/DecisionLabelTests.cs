using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.16.0 §2.4] Satırın sağ yuvası: karar etiketi.
///
/// <para><b>DEĞİŞEN KURAL.</b> Bu yuvada eskiden commit çifti (<c>a3f81c2 → b7e91d4</c>) dururdu ve onu
/// pinleyen testler <c>ExternalShaSlotTests</c> + <c>ProjectRowTests</c>'teki sha aileleriydi: kısaltmanın
/// yalnız 40-hex'e uygulanması, hedef yarısı yokken yarım ok basılmaması, çiftin 118px'lik yuvaya sığması.
/// O çift kararı ANLATMIYORDU — sağ yarı kullanıcının pull etmediği bir UZAK commit'ti, sol yarı ise projeye
/// değil REPOYA aitti. Motor kararı diskteki içerikten verdiğinden satır da artık kararı söyler; sözcükler
/// SABİTTİR ve beş tanedir. Revizyon kısaltma kuralı ölmedi, yalnız yer değiştirdi: konsol satırlarını ve
/// proje logu başlığını besler (<see cref="RunViewModel.ShortSha"/>).</para>
/// </summary>
public class DecisionLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    private static RowDecision For(
        bool? willBuild, WillBuildReason? reason = null, bool? ownChanged = null, DateTimeOffset? builtAt = null,
        bool inCycle = false)
        => DecisionLabel.For(willBuild, reason, ownChanged, builtAt, Now, inCycle);

    [Fact]
    public void Its_own_files_changed_reads_modified()
    {
        var decision = For(true, WillBuildReason.SignatureChanged, ownChanged: true);

        Assert.Equal("modified", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its own files changed since the last build", decision.Title);
    }

    [Fact]
    public void Only_a_dependency_changed_reads_affected()
    {
        var decision = For(true, WillBuildReason.SignatureChanged, ownChanged: false);

        Assert.Equal("affected", decision.Word);
        Assert.Equal("Its own files are unchanged — a dependency changed", decision.Title);
    }

    /// <summary>Bağımlılığı patlamış bir başarı da "etkilenmiş"tir: kendi dosyaları durur, bayat olan
    /// bağımlılığının çıktısıdır.</summary>
    [Fact]
    public void A_project_linked_against_a_failed_dependency_reads_affected()
        => Assert.Equal("affected", For(true, WillBuildReason.DepIssue, ownChanged: false).Word);

    [Fact]
    public void A_project_with_no_output_on_disk_reads_never_built()
    {
        var decision = For(true, WillBuildReason.NeverBuilt);

        Assert.Equal("never built", decision.Word);
        Assert.Equal("No build output on disk", decision.Title);
    }

    [Fact]
    public void A_failed_project_reads_failed_retry()
    {
        var decision = For(true, WillBuildReason.LastFailed);

        Assert.Equal("failed", decision.Word);
        Assert.Equal("retry", decision.Tail);   // kuyruk her zaman soluk çizilir
        Assert.True(decision.Stale);
    }

    /// <summary>
    /// [TASARIMDAN BİLİNÇLİ SAPMA — design v1.16.0 §2.4] <c>retry</c> bir SÖZDÜR: "bir sonraki <b>Build</b>
    /// bunu yeniden deneyecek". Düz bir Build bir bağımlılık döngüsünü ASLA derlemez, dolayısıyla döngü
    /// üyesinde o söz tutulmaz — ölçüldü: gerçek bir çalışma alanında 18 <c>failed</c> satırının 15'i döngü
    /// üyesiydi. Sözcük kalır (o bir olgudur), kuyruk düşer, uzun gerekçe kimin deneyeceğini söyler.
    ///
    /// <para>Tasarım bu durumu değerlendirmemişti: §2.4 tablosu döngü üyelerini hiç ele almıyor.</para>
    /// </summary>
    [Fact]
    public void A_failed_row_that_this_run_will_not_retry_makes_no_promise()
    {
        var cycleMember = For(false, WillBuildReason.LastFailed, inCycle: true);

        Assert.Equal("failed", cycleMember.Word);
        Assert.Null(cycleMember.Tail);
        Assert.Equal("The last build of this project failed — Resolve cycles will retry it", cycleMember.Title);
        Assert.True(cycleMember.Stale);

        // Döngüde OLMAYAN bir kapsam-dışı satır (ör. Cycles koşusunun kapsamı dışında kalan proje): söz yok,
        // ama Resolve'u da vaat etmeyiz — onu derleyecek şey sıradan bir Build'dir.
        var outOfScope = For(false, WillBuildReason.LastFailed);
        Assert.Null(outOfScope.Tail);
        Assert.Equal("The last build of this project failed", outOfScope.Title);
    }

    [Fact]
    public void An_up_to_date_project_reads_the_age_of_its_last_successful_build()
    {
        var decision = For(false, WillBuildReason.UpToDate, builtAt: Now.AddHours(-2));

        Assert.Equal("up to date", decision.Word);
        Assert.Equal("2h", decision.Tail);
        Assert.False(decision.Stale);       // yuva soluk çizilir
        Assert.Equal("Up to date — last built 2h ago", decision.Title);
    }

    [Theory]
    [InlineData(0, 30, "just now")]
    [InlineData(0, 14 * 60, "14m")]
    [InlineData(3, 0, "3d")]
    public void The_age_uses_one_coarse_unit(int days, int seconds, string expected)
        => Assert.Equal(expected, For(false, WillBuildReason.UpToDate,
            builtAt: Now.AddDays(-days).AddSeconds(-seconds)).Tail);

    /// <summary>Eski bir kayıtta zaman yoksa etiket kuyruksuz kalır — uydurma bir yaş yazılmaz.</summary>
    [Fact]
    public void An_up_to_date_project_without_a_timestamp_has_no_tail()
    {
        var decision = For(false, WillBuildReason.UpToDate);

        Assert.Equal("up to date", decision.Word);
        Assert.Null(decision.Tail);
        Assert.Equal("Up to date", decision.Title);
    }

    /// <summary>Öncelik: hiç derlenmemiş &gt; son derleme patladı &gt; kendi dosyası &gt; bağımlılığı.</summary>
    [Fact]
    public void The_disk_facts_outrank_the_content_facts()
    {
        Assert.Equal("never built", For(true, WillBuildReason.NeverBuilt, ownChanged: true).Word);
        Assert.Equal("failed", For(true, WillBuildReason.LastFailed, ownChanged: true).Word);
    }

    /// <summary>
    /// [design v1.16.0 §2.4] Kapsam etiketi SUSTURMAZ: kapsam dışı bir döngü üyesi bu koşuda derlenmez
    /// (<c>will=false</c>) ama dosyaları değişmişse bayattır ve etiketi bunu söyler. Onu derleyecek şeyin
    /// <i>Resolve cycles</i> olduğunu uyarı üçgeni anlatır.
    ///
    /// <para>Ölçülen kusur: gerçek bir çalışma alanında 184 satırın 33'ü — tam olarak SCC üyeleri — hiçbir şey
    /// yazmıyordu.</para>
    /// </summary>
    [Fact]
    public void A_cycle_member_that_will_not_build_still_reports_its_disk_fact()
    {
        Assert.Equal("modified", For(false, WillBuildReason.SignatureChanged, ownChanged: true).Word);
        Assert.Equal("affected", For(false, WillBuildReason.SignatureChanged, ownChanged: false).Word);
        Assert.Equal("up to date", For(false, WillBuildReason.UpToDate, builtAt: Now.AddHours(-2)).Word);
        Assert.Equal("never built", For(false, WillBuildReason.NeverBuilt).Word);

        // ...ama hiçbiri SÖZ vermez: kuyruk yalnız gerçekten derlenecek satırda çıkar.
        Assert.Null(For(false, WillBuildReason.LastFailed, inCycle: true).Tail);
    }

    /// <summary>
    /// Ayrım defterdeki içerik özetinden gelir; BİLİNMİYORSA daha ihtiyatlı sözcük yazılır. "Senin dosyan
    /// değişti" demek, olmadığı hâlde söylenirse kullanıcıyı yanlış yere baktırır — ölçülen kusur buydu:
    /// bağımlılığı patlamış altı proje, kullanıcı hiçbir dosyasına dokunmadığı hâlde <c>modified</c> diyordu.
    /// </summary>
    [Fact]
    public void An_unknown_own_change_reads_as_affected_not_modified()
    {
        Assert.Equal("affected", For(true, WillBuildReason.SignatureChanged, ownChanged: null).Word);
        Assert.Equal("affected", For(true, WillBuildReason.DepIssue, ownChanged: null).Word);
    }

    [Fact]
    public void An_unknown_plan_leaves_the_slot_empty()
    {
        Assert.True(For(null).IsEmpty);
        Assert.Equal("", For(null).Word);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Gerekçesi olmayan satır boş kalır. Eski iddia "koşu-zamanlama kuralıyla atlanan satır
    /// (döngü kapsamı, yakınsamama hafızası) güncel değildir, yuva boş kalır" idi — o satırlar motorun
    /// gerekçesini hiç taşımıyordu. Artık taşıyorlar (bkz. <c>WillBuildEvaluator</c> ve koordinatörün
    /// önizlemesi), yani boş yuva GERÇEKTEN bilinmeyene indi: Sync yapılmadı ya da gerekçe üretilemedi.
    /// </summary>
    [Fact]
    public void A_row_without_a_reason_stays_empty()
        => Assert.True(For(false, reason: null).IsEmpty);
}
