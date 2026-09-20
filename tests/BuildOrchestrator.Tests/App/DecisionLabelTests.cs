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
/// kalktı: (a) <c>failed · retry</c> — <c>retry</c> bir SÖZDÜ; (b) <c>affected · up to date · &lt;yaş&gt;</c>
/// üçlüsü — <c>WaitingForDependency</c> artık <c>UpToDate</c> ile BİREBİR okunur, hangi kökün beklendiğini
/// yalnız uyarı üçgeni (<c>RowWarning</c>) söyler.</para>
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Bu aile bir dizi YAŞ pinliyordu ve hepsi kalktı.
/// Eski iddialar: <c>failed</c> ile <c>up to date</c> kuyruklarında kanıtın yaşı dururdu (<c>failed · 2h</c>,
/// <c>up to date · 2h</c>, dışarıda derlenmiş çıktıda <c>up to date · 5m</c>), yaş tek kaba birimle
/// biçimlenirdi (<c>just now</c>/<c>14m</c>/<c>3d</c>), <c>BuiltOutside</c>'ın yaşı <c>lastBuiltAt</c>'ten
/// DEĞİL <c>outputBuiltAt</c>'ten gelirdi ve uzun gerekçeler yaşı tekrar ederdi (<c>Up to date — last built
/// 2h ago</c>, <c>Failed at this source 2h ago — …</c>, <c>Up to date — built outside this tool 5m ago</c>).
/// Gerekçe: yaşlardan biri YANILTIYORDU (bu araç dışında derlenmiş bir projede satır aracın KENDİ son
/// derlemesinin yaşını gösterebiliyordu) ve kullanıcı bilginin gürültüsüne değmediğine karar verdi. Etiket
/// artık saat okumaz: <c>lastBuiltAt</c>/<c>failedAt</c>/<c>outputBuiltAt</c>/<c>now</c> parametreleri imzadan
/// kalktı, kalan tek kuyruk <c>local</c>'dır ve bunu <see cref="The_label_never_carries_a_time"/> süpürerek
/// pinler.</para>
/// </summary>
public class DecisionLabelTests
{
    private static RowDecision For(
        bool? willBuild, WillBuildReason? reason = null, bool? ownChanged = null, bool localEdits = false,
        bool inCycle = false)
        => DecisionLabel.For(willBuild, reason, ownChanged, localEdits, inCycle);

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

    /// <summary>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Bu yerde İKİ test vardı: biri
    /// <c>failed · 2h</c> çiftini ve <c>Failed at this source 2h ago — Build will retry it</c> cümlesini
    /// pinliyordu, öbürü de kanıtın zamanı bilinmiyorsa kuyruğun boş kaldığını. Yaş kalkınca ikisi AYNI iddiaya
    /// düştü (kopya YASAK) ve tek teste indi: sözcük her koşulda yalnız <c>failed</c>, kuyruk YOK, uzun gerekçe
    /// de kimin yeniden deneyeceğini söyler — bir zaman değil.</summary>
    [Fact]
    public void A_failure_at_this_source_reads_plain_failed()
    {
        var decision = For(true, WillBuildReason.LastFailed);

        Assert.Equal("failed", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Failed at this source — Build will retry it", decision.Title);
    }

    /// <summary>[Task 6 review round 1] <c>UpToDate</c> satırı kendi payına düşeni doğru okur: bir hata
    /// kanıtından söz ETMEZ (satır aynı anda hem güncel hem "kanıtlı hatalı" olamaz — bunlar motorun gerekçe
    /// alanında zaten birbirini DIŞLAR); <c>modified</c>'in başlığı da kelimenin kendisini asla içermez.
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Eski iddia daha dardı ve bir ZAMAN alanını
    /// pinliyordu: "<c>failedAt</c> yalnız <c>LastFailed</c> gerekçesinde okunur, <c>UpToDate</c>'in kuyruğu
    /// her zaman <c>lastBuiltAt</c>'in yaşıdır" (eski adı <c>FailedAt_is_read_only_for_the_last_failed_reason</c>).
    /// Etiket artık hiçbir zaman alanı okumadığı için o kural anlamsızlaştı; geriye sözcüklerin karışmaması
    /// kaldı.</para></summary>
    [Fact]
    public void An_up_to_date_row_never_speaks_of_a_failure()
    {
        var upToDate = For(false, WillBuildReason.UpToDate);
        Assert.Equal("up to date", upToDate.Word);
        Assert.Null(upToDate.Tail);
        Assert.DoesNotContain("failed", upToDate.Title, StringComparison.Ordinal);

        var modified = For(true, WillBuildReason.SignatureChanged, ownChanged: true);
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
        var cycleMember = For(true, WillBuildReason.LastFailed, inCycle: true);

        Assert.Equal("failed", cycleMember.Word);
        Assert.Null(cycleMember.Tail);
        Assert.Equal("Failed at this source — Resolve cycles will retry it", cycleMember.Title);
        Assert.True(cycleMember.Stale);

        var outOfScope = For(false, WillBuildReason.LastFailed);
        Assert.Equal("Failed at this source — Build will retry it", outOfScope.Title);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Bu testin eski hâli (<c>Retry_is_never_promised_in_the_label</c>)
    /// yalnız kuyruğun "retry" OLMADIĞINI süpürüyordu — çünkü o gün kuyruk kanıtın YAŞIydı. Yaş kalktı ve
    /// süpürme genişledi: hiçbir girdi bileşiminde ne kuyrukta ne uzun gerekçede bir ZAMAN çıkar. Kuyruk ya
    /// yoktur ya <c>local</c>'dır; cümlelerde "ago"/"just now" ve rakam bulunmaz ("retry" düz metinde,
    /// "Build will retry it" cümlesinde yaşamaya devam eder — o bir söz değil, kimin derleyeceğidir).
    /// </summary>
    [Theory]
    [InlineData(WillBuildReason.NeverBuilt)]
    [InlineData(WillBuildReason.LastFailed)]
    [InlineData(WillBuildReason.UpToDate)]
    [InlineData(WillBuildReason.WaitingForDependency)]
    [InlineData(WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.DepIssue)]
    [InlineData(WillBuildReason.BuiltOutside)]
    [InlineData(WillBuildReason.OutputMissing)]
    [InlineData(WillBuildReason.OutputStale)]
    [InlineData(WillBuildReason.OutputReplaced)]
    public void The_label_never_carries_a_time(WillBuildReason reason)
    {
        foreach (bool willBuild in new[] { true, false })
        foreach (bool ownChanged in new[] { true, false })
        foreach (bool localEdits in new[] { true, false })
        foreach (bool inCycle in new[] { true, false })
        {
            var decision = For(willBuild, reason, ownChanged, localEdits, inCycle);

            Assert.True(decision.Tail is null or "local", $"beklenmedik kuyruk: {decision.Tail}");
            Assert.DoesNotContain("ago", decision.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("just now", decision.Title, StringComparison.Ordinal);
            Assert.DoesNotContain(decision.Title, char.IsDigit);
        }
    }

    /// <summary>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Eski iddia: <c>up to date</c> kuyruğu son
    /// BAŞARILI derlemenin yaşıydı (<c>up to date · 2h</c>, <c>Up to date — last built 2h ago</c>) ve zaman
    /// bilinmiyorsa kuyruk boş kalırdı (ayrı bir test). İkisi tek iddiaya indi: sözcük yalın, kuyruk yok,
    /// gerekçe tek sözcüklük bir cümle.</summary>
    [Fact]
    public void An_up_to_date_project_reads_plain_up_to_date()
    {
        var decision = For(false, WillBuildReason.UpToDate);

        Assert.Equal("up to date", decision.Word);
        Assert.Null(decision.Tail);
        Assert.False(decision.Stale);       // yuva soluk çizilir
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
        var decision = For(true, WillBuildReason.WaitingForDependency);

        Assert.Equal("up to date", decision.Word);
        Assert.Null(decision.Tail);
        Assert.False(decision.Stale);
        Assert.Equal("Up to date", decision.Title);

        // Kapsam ZORLASA bile (eski "conditional=false") aynı cümle — döngü üyesi de aynı okur.
        Assert.Equal(decision, For(false, WillBuildReason.WaitingForDependency));
        Assert.Equal(decision, For(false, WillBuildReason.WaitingForDependency, inCycle: true));
    }

    // ---------------------------------------------------------------- [Faz 3 — spec 2026-09-18 §5.4] dört yeni gerekçe

    /// <summary>[Task 7] Çıktı bu araç dışında derlenmiş ve güncel — yeşil, sözcük <c>UpToDate</c> ile AYNI;
    /// ayrımı yalnız uzun gerekçe söyler (çıktı bu aracın eseri değil).
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Burada ÜÇ test vardı: kuyruk çıktı kanıtının
    /// yaşıydı (<c>up to date · 5m</c>, <c>Up to date — built outside this tool 5m ago</c>), zaman
    /// bilinmiyorsa kuyruk boş kalırdı, ve bir üçüncü test yaşın <c>lastBuiltAt</c>'ten DEĞİL
    /// <c>outputBuiltAt</c>'ten geldiğini pinliyordu. Kullanıcının kaldırma gerekçesi tam da o üçüncüsüydü:
    /// pratikte satır aracın KENDİ son derlemesinin yaşını gösterip yanıltabiliyordu. Yaş hepten kalktı,
    /// üç test tek iddiaya indi — cümle "dışarıda derlendi" demeye devam eder, ne zaman olduğunu söylemez.</para></summary>
    [Fact]
    public void Built_outside_reads_up_to_date_and_says_it_was_built_elsewhere()
    {
        var decision = For(false, WillBuildReason.BuiltOutside);

        Assert.Equal("up to date", decision.Word);
        Assert.Null(decision.Tail);
        Assert.False(decision.Stale);
        Assert.Equal("Up to date — built outside this tool", decision.Title);
    }

    /// <summary>[Task 7] Derleme kanıtı diskte yok — <c>never built</c> ile AYNI okunur (kopya YASAK: tek
    /// tooltip metni <c>DecisionLabel</c> içinde iki gerekçe arasında paylaşılır).</summary>
    [Fact]
    public void Output_missing_reads_never_built()
    {
        var decision = For(true, WillBuildReason.OutputMissing);

        Assert.Equal("never built", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("No build output known to this tool", decision.Title);
    }

    /// <summary>[Task 7] Öğrenilmiş beslenen kopya bozuk — bağımlılar başka bir çıktıya link'lidir.</summary>
    [Fact]
    public void Output_replaced_reads_affected()
    {
        var decision = For(true, WillBuildReason.OutputReplaced);

        Assert.Equal("affected", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its copy in the shared folder does not match its build output", decision.Title);
    }

    /// <summary>[Task 7] Zaman kipi: kendi girdisi derleme kanıtından yeni.</summary>
    [Fact]
    public void Output_stale_with_own_files_changed_reads_modified()
    {
        var decision = For(true, WillBuildReason.OutputStale, ownChanged: true);

        Assert.Equal("modified", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its own files are newer than its build output", decision.Title);
    }

    /// <summary>[Task 7] Zaman kipi: yalnız bir HintPath hedefi derleme kanıtından yeni — kendi girdisi durur.</summary>
    [Fact]
    public void Output_stale_with_only_a_dependency_changed_reads_affected()
    {
        var decision = For(true, WillBuildReason.OutputStale, ownChanged: false);

        Assert.Equal("affected", decision.Word);
        Assert.Null(decision.Tail);
        Assert.True(decision.Stale);
        Assert.Equal("Its own files are unchanged — a dependency changed", decision.Title);
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
        Assert.Equal("up to date", For(false, WillBuildReason.UpToDate).Word);
        Assert.Equal("never built", For(false, WillBuildReason.NeverBuilt).Word);

        // ...ama hiçbiri SÖZ vermez ve hiçbiri saat okumaz: döngü üyesinin hatası da kuyruksuzdur.
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
