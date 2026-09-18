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
///
/// <para><b>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4]</b> Sınıf İKİ girdi daha KAZANDI (<c>failedAt</c>,
/// <c>localEdits</c>) ve BİRİNİ KAYBETTİ (<c>conditional</c>). Eski aile burada iki şey PİNLİYORDU ve ikisi de
/// kalktı: (a) <c>failed · retry</c> — <c>retry</c> bir SÖZDÜ, kuyruk artık kanıtın YAŞIdır; (b)
/// <c>affected · up to date · &lt;yaş&gt;</c> üçlüsü — <c>WaitingForDependency</c> artık <c>UpToDate</c> ile
/// BİREBİR okunur, hangi kökün beklendiğini yalnız uyarı üçgeni (<c>RowWarning</c>) söyler. Bu yüzden
/// <c>conditional</c>/<c>roots</c>/<c>prefix</c> parametreleri buradan da kalktı — imza artık
/// <see cref="DecisionLabel.For"/>'un GÜNCEL hâliyle birebirdir (kökler/önek hâlâ imzada var ama fonksiyon
/// onları okumuyor, bkz. o sınıfın <c>&lt;param&gt;</c> notu).</para>
/// </summary>
public class DecisionLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    private static RowDecision For(
        bool? willBuild, WillBuildReason? reason = null, bool? ownChanged = null, DateTimeOffset? builtAt = null,
        DateTimeOffset? failedAt = null, bool localEdits = false, bool inCycle = false)
        => DecisionLabel.For(willBuild, reason, ownChanged, builtAt, failedAt, localEdits, Now, inCycle);

    [Fact]
    public void Its_own_files_changed_reads_modified()
    {
        var decision = For(true, WillBuildReason.SignatureChanged, ownChanged: true);

        Assert.Equal("modified", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its own files changed since the last build", decision.Title);
    }

    /// <summary>[spec 2026-09-18 §4 <c>local</c>] Girdilerinden en az biri <c>git status</c>'ta kirliyse
    /// <c>modified</c> soluk bir <c>local</c> kuyruğu kazanır ve uzun gerekçe bunu adlandırır.</summary>
    [Fact]
    public void Uncommitted_edits_add_the_local_tail()
    {
        var decision = For(true, WillBuildReason.SignatureChanged, ownChanged: true, localEdits: true);

        Assert.Equal("modified", decision.Word);
        Assert.Equal("local", decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its own files changed since the last build — includes uncommitted edits", decision.Title);

        // Kirli değilse kuyruk yok — eski davranışla AYNI.
        Assert.Null(For(true, WillBuildReason.SignatureChanged, ownChanged: true, localEdits: false).Tail);
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

    /// <summary>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] Eski metin "No build output on disk" idi. Yeni
    /// metin ARACIN kendi bilgisini adlandırır ("known to this tool") — diskte bir çıktı hâlâ durabilir
    /// (başka bir araçla üretilmiş), aracın onu HİÇ görmediğidir olgu.</summary>
    [Fact]
    public void Never_built_names_the_tool_not_the_disk()
    {
        var decision = For(true, WillBuildReason.NeverBuilt);

        Assert.Equal("never built", decision.Word);
        Assert.Equal("No build output known to this tool", decision.Title);
    }

    /// <summary>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] Eski aile <c>failed · retry</c> çiftini
    /// pinliyordu: kuyruk sabit "retry" sözcüğüydü ve yalnız GERÇEKTEN derlenecek satırda çıkardı. O söz
    /// kalktı (bkz. sınıf özeti) — kuyruk artık kanıtın (son hatanın) YAŞIdır, <c>up to date</c>'in
    /// <c>lastBuiltAt</c> kuyruğuyla AYNI biçimde (<see cref="Core.Formatting.AgeFormat"/>).</summary>
    [Fact]
    public void A_failure_at_this_source_reads_failed_with_its_age()
    {
        var decision = For(true, WillBuildReason.LastFailed, failedAt: Now.AddHours(-2));

        Assert.Equal("failed", decision.Word);
        Assert.Equal("2h", decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Failed at this source 2h ago — Build will retry it", decision.Title);
    }

    /// <summary>Kayıtlı hatanın zamanı bilinmiyorsa (eski defter) kuyruk uydurma bir yaş taşımaz — <c>up to
    /// date</c>'in kuralıyla AYNI.</summary>
    [Fact]
    public void A_failure_without_a_recorded_time_has_no_age_in_its_tail()
    {
        var decision = For(true, WillBuildReason.LastFailed);

        Assert.Equal("failed", decision.Word);
        Assert.Null(decision.Tail);
        Assert.Equal("Failed at this source — Build will retry it", decision.Title);
    }

    /// <summary>[Task 6 review round 1] <c>failedAt</c> yalnız <c>LastFailed</c> gerekçesinde okunur — başka
    /// hiçbir dal eski bir hata kanıtı taşıyor diye "failed" YAZMAZ. <c>UpToDate</c> kuyruğu her zaman
    /// <c>lastBuiltAt</c>'in yaşıdır (satır aynı anda hem güncel hem "kanıtlı hatalı" olamaz — bunlar motorun
    /// gerekçe alanında zaten birbirini DIŞLAR, ama etiket kendi payına düşeni doğru okumalı); <c>modified</c>'in
    /// başlığı da kelimenin kendisini asla içermez.</summary>
    [Fact]
    public void FailedAt_is_read_only_for_the_last_failed_reason()
    {
        var upToDate = For(false, WillBuildReason.UpToDate, builtAt: Now.AddHours(-2), failedAt: Now.AddDays(-3));
        Assert.Equal("up to date", upToDate.Word);
        Assert.Equal("2h", upToDate.Tail);           // failedAt'in 3 günlük yaşı DEĞİL, lastBuiltAt'in 2 saati
        Assert.DoesNotContain("failed", upToDate.Title, StringComparison.Ordinal);

        var modified = For(true, WillBuildReason.SignatureChanged, ownChanged: true, failedAt: Now.AddDays(-3));
        Assert.Equal("modified", modified.Word);
        Assert.Null(modified.Tail);
        Assert.DoesNotContain("failed", modified.Title, StringComparison.Ordinal);
    }

    /// <summary>Bir döngü üyesinde bu satırı yeniden derleyecek şey düz bir Build DEĞİL, <i>Resolve
    /// cycles</i>'tır — uzun gerekçe bunu adlandırır (kelime <c>failed</c> her koşulda kalır, o bir
    /// olgudur).</summary>
    [Fact]
    public void A_cycle_member_failure_names_resolve_cycles_in_the_tooltip()
    {
        var cycleMember = For(true, WillBuildReason.LastFailed, failedAt: Now.AddHours(-2), inCycle: true);

        Assert.Equal("failed", cycleMember.Word);
        Assert.Equal("2h", cycleMember.Tail);
        Assert.Equal("Failed at this source 2h ago — Resolve cycles will retry it", cycleMember.Title);
        Assert.True(cycleMember.Stale);

        var outOfScope = For(false, WillBuildReason.LastFailed, failedAt: Now.AddHours(-2));
        Assert.Equal("Failed at this source 2h ago — Build will retry it", outOfScope.Title);
    }

    /// <summary>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] <c>retry</c> sözcüğü artık KUYRUKTA hiçbir
    /// girdi kombinasyonunda çıkmaz (kuyruk kanıtın yaşıdır ya da <c>local</c>/null) — düz metinde kalabilir
    /// (uzun gerekçenin cümlesinde, "Build will retry it"), ama bir SÖZ olarak yuvanın kuyruğuna asla
    /// taşınmaz.</summary>
    [Theory]
    [InlineData(WillBuildReason.NeverBuilt)]
    [InlineData(WillBuildReason.LastFailed)]
    [InlineData(WillBuildReason.UpToDate)]
    [InlineData(WillBuildReason.WaitingForDependency)]
    [InlineData(WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.DepIssue)]
    public void Retry_is_never_promised_in_the_label(WillBuildReason reason)
    {
        foreach (bool willBuild in new[] { true, false })
        foreach (bool ownChanged in new[] { true, false })
        foreach (bool localEdits in new[] { true, false })
        foreach (bool inCycle in new[] { true, false })
        {
            var decision = For(willBuild, reason, ownChanged, Now.AddHours(-2), Now.AddHours(-2), localEdits, inCycle);
            Assert.NotEqual("retry", decision.Tail);
        }
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

    /// <summary>
    /// [DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] Eski iddia: motor bu koşuyu GERÇEKTEN bekletiyorsa
    /// (<c>conditional=true</c>) yuva <c>affected · up to date · &lt;yaş&gt;</c> yazardı ve tooltip'i kökleri
    /// sıralardı. O ayrım kalktı: <c>WaitingForDependency</c> artık <c>UpToDate</c> ile BİREBİR aynı okunur —
    /// proje ÇIKTI olarak güncel, hangi kökün beklendiğini yalnız uyarı üçgeni (<c>RowWarning</c>) söyler
    /// (kopya YASAK: aynı bilgiyi iki tooltip'te tekrarlamamak). Kapsamın zorlayıp zorlamadığı (eski
    /// "conditional") artık etiketi hiç etkilemez — döngü üyesi de, kapsam-dışı da, genuinely-waiting satır da
    /// AYNI düz "up to date"i yazar.
    /// </summary>
    [Fact]
    public void A_waiting_project_reads_plain_up_to_date()
    {
        var decision = For(true, WillBuildReason.WaitingForDependency, builtAt: Now.AddHours(-2));

        Assert.Equal("up to date", decision.Word);
        Assert.Equal("2h", decision.Tail);
        Assert.False(decision.Stale);
        Assert.Equal("Up to date — last built 2h ago", decision.Title);

        // Kapsam ZORLASA bile (eski "conditional=false") aynı cümle — döngü üyesi de aynı okur.
        Assert.Equal(decision, For(false, WillBuildReason.WaitingForDependency, builtAt: Now.AddHours(-2)));
        Assert.Equal(decision, For(false, WillBuildReason.WaitingForDependency, builtAt: Now.AddHours(-2), inCycle: true));
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

        // ...ama hiçbiri SÖZ vermez: kuyruk yalnız kanıtlı hatanın yaşıdır, sabit bir "retry" değil.
        Assert.Equal("2h", For(false, WillBuildReason.LastFailed, failedAt: Now.AddHours(-2), inCycle: true).Tail);
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
