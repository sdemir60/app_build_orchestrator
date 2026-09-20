using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Planning;

// [T53][A6][v7Δ-8] WillBuildEvaluator karar tablosu + BuildPreview.ComputeWillBuild mapping testleri.
// It-1 kapsamı: currentSignature enjekte edilen bir provider (gerçek imza motoru T25, It-3'te bağlanır).
public class WillBuildTests
{
    [Fact]
    public void hollow_when_signature_null()
        => Assert.Null(WillBuildEvaluator.Evaluate(false, null, null, buildCycles: false));

    [Fact]
    public void true_when_never_built()
        => Assert.True(WillBuildEvaluator.Evaluate(false, "sig1", null, buildCycles: false));

    [Fact]
    public void true_when_dirty()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig2", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded), buildCycles: false));

    [Fact]
    public void false_when_up_to_date_after_success() // succeeded → clean
        => Assert.False(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded), buildCycles: false));

    // [DEĞİŞEN KURAL] ESKİ İDDİA: "inCycle olan proje ASLA derlenmez → Evaluate her zaman false".
    // Bu kural kaldırıldı: graf kenarlarının primeri HintPath'tir (ProjectReference değil) ve MSBuild bir
    // HintPath döngüsünü reddetmez — döngü sıralı turlarla derlenebilir. Artık cycle üyeleri de normal
    // dirty/clean kararını alır; ESKİ davranış yalnız kill switch KAPALIYKEN geçerlidir.
    [Fact]
    public void cycle_member_is_not_built_when_switch_is_off()
    {
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: false));
    }

    [Fact]
    public void cycle_member_follows_normal_decision_when_switch_is_on()
    {
        // Hiç derlenmemiş (BuiltSignature yok) ⇒ dirty ⇒ true
        Assert.True(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: true));

        // İmza eşleşiyor + son sonuç Succeeded ⇒ güncel ⇒ false
        var clean = new BuildState("p", "sig", LastResult: BuildResult.Succeeded);
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: clean, buildCycles: true));
    }

    // Anahtar AÇIK olsa bile imza yoksa hollow kalır — cycle bunu ezmez.
    [Fact]
    public void cycle_member_stays_hollow_without_signature()
    {
        Assert.Null(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: null, state: null, buildCycles: true));
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] Eski iddia: "LastResult=Failed ⇒ gerekçe LastFailed" (bu test
    /// yalnız <c>WillBuild==true</c>'yu doğruluyordu ve kural değiştikten SONRA da yeşil kaldı, çünkü
    /// <c>NeverBuilt</c> de <c>WillBuild=true</c> üretir — testin kendisi ayrımı görmüyordu). Yeni kural:
    /// kırmızı artık yalnız KANITLIYSA üretilir (hata anındaki imza = <c>FailedSignature</c>, bugünküyle eşit);
    /// burada kayıt bir hata SONUCU taşıyor ama hangi imzada patladığını (<c>FailedSignature</c>) bilmiyor — kanıt
    /// yok, gerekçe <c>NeverBuilt</c>'tir, <c>LastFailed</c> DEĞİL. <c>WillBuild</c> iki gerekçede de <c>true</c>:
    /// proje yine derlenecektir, değişen yalnız KULLANICIYA gösterilen renk/etiket.
    /// </summary>
    [Fact]
    public void true_when_last_result_failed_even_if_signature_matches()
    {
        var state = new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Failed);
        Assert.True(WillBuildEvaluator.Evaluate(false, "sig1", state, buildCycles: false));
        Assert.Equal(WillBuildReason.NeverBuilt, ReasonOf("sig1", state));
    }

    /// <summary>Kanıt: hata anındaki imza (<c>FailedSignature</c>) bugünkü imzayla eşleşince gerekçe
    /// <c>LastFailed</c>'dir — spec §1-14 "kanıtlı kırmızı". <c>BuiltSignature</c> farklı bir (eski, başarılı)
    /// imza taşıyabilir; kırmızı kararı yalnız hata imzasının kanıtına bakar.</summary>
    [Fact]
    public void Failed_at_the_current_signature_reads_LastFailed()
    {
        var state = new BuildState("A", BuiltSignature: "sig0", LastResult: BuildResult.Failed,
            FailedSignature: "sig1");

        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false);

        Assert.Equal(WillBuildReason.LastFailed, reason);
        Assert.True(willBuild);
    }

    /// <summary>Hata imzası dolu ama BUGÜNKÜYLE eşleşmiyor — kanıt bayat, kırmızı ÜRETİLMEZ; karar sıradaki
    /// kurala (imza/BuiltSignature) düşer. İki kontrol grubu: (a) güncel bir başarı varsa UpToDate, (b) hiç
    /// başarı yoksa (BuiltSignature null) NeverBuilt.</summary>
    [Fact]
    public void Failed_at_another_signature_falls_through_to_the_signature_rule()
    {
        var upToDate = new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded,
            FailedSignature: "sig0");
        Assert.Equal(WillBuildReason.UpToDate, ReasonOf("sig1", upToDate));

        var neverBuilt = new BuildState("A", BuiltSignature: null, FailedSignature: "sig0");
        Assert.Equal(WillBuildReason.NeverBuilt, ReasonOf("sig1", neverBuilt));
    }

    /// <summary>[final review M1 · spec §5.3] Kaynağı geri alınan hata: sig1'de başarı, sig0'da KANITLI hata,
    /// kaynak sig1'e geri döndü. Motorun gerçekten yazdığı kayıt budur (<c>InvalidateBuildStateOnFailure</c>
    /// partial merge: <c>BuiltSignature</c> korunur, <c>LastResult=Failed</c> + <c>FailedSignature=sig0</c>).
    /// Kanıt bugünkü imzaya ait değil; bugünkü imzanın son bilinen sonucu başarıdır ⇒ <c>UpToDate</c> ve
    /// pre-skip (<c>WillBuild=false</c>). "<c>LastResult != Succeeded</c> ⇒ derlenir" genel bir kural DEĞİLDİR.</summary>
    [Fact]
    public void Content_reverted_to_the_last_successful_signature_after_a_failure_reads_UpToDate()
    {
        var state = new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Failed,
            FailedSignature: "sig0", FailedAt: DateTimeOffset.UtcNow);

        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false);

        Assert.Equal(WillBuildReason.UpToDate, reason);
        Assert.False(willBuild);
    }

    /// <summary>Kesilmiş deneme (ortam hatası, kill, timeout — Task 2'nin YAZMADIĞI durumlar): sonuç başarısız
    /// ama <c>FailedSignature</c> boş, yani hangi imzada patladığı kanıtlanmamış. Gerekçe <c>NeverBuilt</c>'tir
    /// (gri), <c>LastFailed</c> DEĞİL — kanıtsız kırmızı gösterilmez. <c>WillBuild</c> yine <c>true</c>: proje
    /// derlenecek listesinde kalır.</summary>
    [Fact]
    public void An_interrupted_attempt_without_evidence_reads_NeverBuilt()
    {
        var state = new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Failed,
            FailedSignature: null);

        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false);

        Assert.Equal(WillBuildReason.NeverBuilt, reason);
        Assert.True(willBuild);
    }

    [Fact]
    public void false_when_in_cycle_even_if_signature_null()
        => Assert.False(WillBuildEvaluator.Evaluate(true, null, null, buildCycles: false));

    /// <summary>
    /// [DEĞİŞEN KURAL — koşullu yeniden derleme] Kök bağımlılıkları BİLİNMEYEN (eski) dep-issue kaydı hâlâ
    /// kesin derlenir.
    ///
    /// <para><b>Eski iddia:</b> "bağımlılığı başarısız olmuş bir başarı, kendi kaynağı değişmese bile HER
    /// Build'de yeniden derlenir" — kayıt ne olursa olsun. Bu güvenlik eskiden koordinatörde "böyle bir başarıyı
    /// deftere hiç yazma" biçiminde duruyordu (defteri durdurduğu ölçüldü: 74 başarının 0'ı yazıldı) ve buraya
    /// taşınmıştı.</para>
    ///
    /// <para><b>Değişme gerekçesi:</b> tetik yanlış yerdeydi. Bağımlılık hâlâ patlıyorken yeniden derlemek aynı
    /// bayat çıktıya yeniden link'lemekten başka bir şey yapmıyordu; ikinci Build'de dalganın yakmadığı düzinelerce
    /// proje amber'a dönüp gerçekten derleniyordu (inceleme raporu §2.3). Kök kimlikleri artık deftere yazıldığı
    /// için karar "kök düzeldiğinde" verilebiliyor (<see cref="WillBuildReason.WaitingForDependency"/>). Kök
    /// listesi olmayan kayıtta o soru sorulamaz — eski davranış güvenli yön olarak kalır.</para>
    /// </summary>
    [Fact]
    public void true_when_the_last_success_was_built_against_a_failed_dependency_with_unknown_roots()
        => Assert.True(WillBuildEvaluator.Evaluate(false, "sig1",
            new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded, DepIssue: true),
            buildCycles: false));

    /// <summary>Kökleri bilinen, imzası değişmemiş dep-issue kaydı KOŞULLUDUR: WillBuild <c>true</c> kalır (bu
    /// koşu onu derleyebilir, pre-skip EDİLMEZ) ama gerekçe onun kesin değil, kök düzelirse derleneceğini söyler.</summary>
    [Fact]
    public void a_dep_issue_record_with_known_roots_and_an_unchanged_signature_is_waiting_for_its_dependency()
    {
        var state = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Up\Up.csproj"]);

        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false);

        Assert.Equal(WillBuildReason.WaitingForDependency, reason);
        Assert.True(willBuild);
    }

    /// <summary>Senaryo 5: imzası değişmiş dep-issue'lu proje KOŞULLU DEĞİL — kendi değişikliği onu kesin
    /// derletir, gerekçe de bunu söyler.</summary>
    [Fact]
    public void a_dep_issue_record_whose_signature_moved_is_a_plain_signature_change()
    {
        var withRoots = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Up\Up.csproj"]);

        Assert.Equal(WillBuildReason.SignatureChanged, ReasonOf("sig2", withRoots));
        Assert.True(WillBuildEvaluator.Evaluate(false, "sig2", withRoots, buildCycles: false));
    }

    /// <summary>Boş kök listesi "kök bilinmiyor" ile aynıdır — koşullu sayılmaz.</summary>
    [Fact]
    public void an_empty_root_list_counts_as_unknown_roots()
        => Assert.Equal(WillBuildReason.DepIssue, ReasonOf("sig1",
            new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true, DepIssueRoots: [])));

    /// <summary>Not TEMİZ bir kayıtta yoktur — aynı imza güncel demektir (kontrol grubu).</summary>
    [Fact]
    public void false_when_the_last_success_carried_no_dependency_issue()
        => Assert.False(WillBuildEvaluator.Evaluate(false, "sig1",
            new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded, DepIssue: false),
            buildCycles: false));

    [Fact]
    public void true_when_signature_matches_but_last_result_null()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: null), buildCycles: false));

    // ---- Gerekçe (WillBuildReason) --------------------------------------------------------
    // Karar TEK gövdededir (EvaluateWithReason); Evaluate ona delege eder. Gerekçe kullanıcıya
    // gösterilir: "amber nokta ama commit aynı" görüntüsünün açıklaması buradan gelir.

    private static WillBuildReason? ReasonOf(string? signature, BuildState? state, bool inCycle = false) =>
        WillBuildEvaluator.EvaluateWithReason(inCycle, signature, state, buildCycles: false).Reason;

    [Fact]
    public void reason_is_never_built_when_there_is_no_record()
        => Assert.Equal(WillBuildReason.NeverBuilt, ReasonOf("sig1", null));

    /// <summary>
    /// [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] Eski iddia: "son koşu başarısız (LastResult != Succeeded) ⇒
    /// gerekçe LastFailed" — kayıt hangi imzada patladığını taşımasa BİLE. Ölçüldü: bu, ortam hatası (kilit,
    /// disk, kill) yüzünden yarıda kalmış ya da eski bir hata kaydı taşıyan bir projeyi de kanıtsızca kırmızı
    /// gösteriyordu. Yeni kural kontrol grubunu KANITLI hâle getirir (<c>FailedSignature</c> bugünkü imzayla
    /// eşleşir) ve aynı iddiayı ("son koşu başarısız ⇒ LastFailed") artık doğru koşulda pinler; kanıtsız hâl
    /// artık <see cref="An_interrupted_attempt_without_evidence_reads_NeverBuilt"/>'in konusudur.
    /// </summary>
    [Fact]
    public void reason_is_last_failed_when_the_previous_run_did_not_succeed()
        => Assert.Equal(WillBuildReason.LastFailed,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Failed, FailedSignature: "sig1")));

    /// <summary>Senaryo 4: kök listesi olmayan (bu alandan önce yazılmış) kayıt bugünkü gerekçeyi taşır.</summary>
    [Fact]
    public void reason_is_dep_issue_when_the_last_success_was_built_against_a_failed_dependency()
        => Assert.Equal(WillBuildReason.DepIssue,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true)));

    [Fact]
    public void reason_is_signature_changed_when_the_source_moved()
        => Assert.Equal(WillBuildReason.SignatureChanged,
            ReasonOf("sig2", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded)));

    [Fact]
    public void reason_is_up_to_date_when_nothing_moved()
        => Assert.Equal(WillBuildReason.UpToDate,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded)));

    /// <summary>Hollow'da gerekçe YOKTUR: imza hesaplanamadıysa söylenecek bir şey de yoktur.</summary>
    [Fact]
    public void hollow_carries_no_reason()
        => Assert.Null(ReasonOf(null, null));

    /// <summary>
    /// [DEĞİŞEN KURAL — v1.16.0] Kapsam dışı bir SCC üyesi de gerekçesini SÖYLER.
    ///
    /// <para><b>Eski iddia:</b> "kapsam-dışı hâlde gerekçe yoktur; üyelik kanalı (döngü rozeti) zaten
    /// konuşuyor, plan gerekçesi orada yanıltıcı olurdu" — ve o dönemde gerekçe gerçekten bir PLAN kanalını
    /// (will-build noktası) besliyordu. Gerekçe artık satırın KARAR ETİKETİNİ besliyor; etiket ise bir DİSK
    /// OLGUSUDUR ve o olgu döngü üyesi için de vardır. Gizlenmesi ölçüldü: gerçek bir çalışma alanında 184
    /// satırın 33'ü (tüm SCC üyeleri) hiçbir şey yazmıyordu.</para>
    ///
    /// <para><b>WillBuild DEĞİŞMEDİ</b> — kapsam dışı üye hâlâ <c>false</c>: bu koşu onu derlemez.</para>
    /// </summary>
    [Fact]
    public void an_out_of_scope_cycle_member_still_reports_why_it_is_stale()
    {
        var built = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded);

        Assert.Equal(WillBuildReason.NeverBuilt, ReasonOf("sig1", null, inCycle: true));
        Assert.Equal(WillBuildReason.UpToDate, ReasonOf("sig1", built, inCycle: true));
        Assert.Equal(WillBuildReason.SignatureChanged, ReasonOf("sig2", built, inCycle: true));

        // ...ama derlenmez: kapsam kararı aynen duruyor.
        Assert.False(WillBuildEvaluator.Evaluate(true, "sig2", built, buildCycles: false));
        Assert.False(WillBuildEvaluator.Evaluate(true, "sig1", null, buildCycles: false));
    }

    /// <summary>Evaluate, EvaluateWithReason'a delege eder — iki yüzey ayrışamaz (kopya yok).</summary>
    [Fact]
    public void the_two_surfaces_always_agree()
    {
        var state = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true);
        Assert.Equal(WillBuildEvaluator.Evaluate(false, "sig1", state, buildCycles: false),
                     WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false).WillBuild);
    }

    [Fact]
    public void ComputeWillBuild_populates_node_field()
    {
        var node = new ProjectNode("A", "A", "A", [], [], 0, null, null, InCycle: false, WillBuild: null);
        var plan = new BuildPlan([node], [], "Debug");
        var result = BuildPreview.ComputeWillBuild(plan, _ => "sig1", _ => null, buildCycles: false); // never built
        Assert.True(result.Nodes[0].WillBuild);
    }

    // ---- [Faz 3/Task 5 — spec 2026-09-18 §5.3/§5.4] Çıktı kanıtı karara girer ------------------------------
    // Defter kipi: bugünkü karar + iki veto (kanıt yok ⇒ OutputMissing, beslenen kopya bozuk ⇒ OutputReplaced).
    // Zaman kipi: hüküm kanıttan; kırmızı yok, defter notları okunmaz. Kanıtsız (None / null): bugünkü karar.

    private static readonly BuildState Clean = new("A", "sig1", LastResult: BuildResult.Succeeded);

    private static OutputCheck Ledger(bool evidenceMissing = false, bool fedIntact = true) =>
        new(EvidenceMode.Ledger, evidenceMissing, fedIntact, null, null);

    private static OutputCheck Time(TimeVerdict verdict) =>
        new(EvidenceMode.Time, verdict == TimeVerdict.Missing, verdict != TimeVerdict.FedBroken, verdict, null);

    private static (bool? WillBuild, WillBuildReason? Reason) With(
        OutputCheck? output, string? signature, BuildState? state, bool inCycle = false, bool buildCycles = false) =>
        WillBuildEvaluator.EvaluateWithReason(inCycle, signature, state, buildCycles, output);

    /// <summary>§5.3: defterde temiz bir kayıt var ama derleme kanıtı diskte yok — çıktı ortada değil, derlenir.
    /// İmzası değişmiş kayıtta da kanıtın yokluğu söylenir (controller eşlemesi: LastFailed/NeverBuilt dışındaki
    /// her gerekçe).</summary>
    [Fact]
    public void A_ledger_record_without_its_output_is_output_missing()
    {
        Assert.Equal((true, WillBuildReason.OutputMissing), With(Ledger(evidenceMissing: true), "sig1", Clean));
        Assert.Equal((true, WillBuildReason.OutputMissing), With(Ledger(evidenceMissing: true), "sig2", Clean));
        // Kontrol grubu: kanıt yerindeyse bugünkü karar.
        Assert.Equal((false, WillBuildReason.UpToDate), With(Ledger(), "sig1", Clean));
    }

    /// <summary>§5.3, §7-17/18: imza güncel ama havuzdaki beslenen kopya bozuk — <c>UpToDate</c> ve
    /// <c>WaitingForDependency</c> <c>OutputReplaced</c> olur ve derlenir.</summary>
    [Fact]
    public void A_broken_shared_copy_turns_up_to_date_into_output_replaced()
    {
        var waiting = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Up\Up.csproj"]);

        Assert.Equal((true, WillBuildReason.OutputReplaced), With(Ledger(fedIntact: false), "sig1", Clean));
        Assert.Equal((true, WillBuildReason.OutputReplaced), With(Ledger(fedIntact: false), "sig1", waiting));
    }

    /// <summary>§5.3: kanıtlı kırmızı (<c>LastFailed</c>) ve <c>NeverBuilt</c> vetolarla ezilmez — kanıt yok ya da
    /// kopya bozuk olsa bile.</summary>
    [Fact]
    public void A_proven_failure_stays_red_even_without_output()
    {
        var failed = new BuildState("A", "sig0", LastResult: BuildResult.Failed, FailedSignature: "sig1");

        Assert.Equal((true, WillBuildReason.LastFailed), With(Ledger(evidenceMissing: true), "sig1", failed));
        Assert.Equal((true, WillBuildReason.LastFailed), With(Ledger(fedIntact: false), "sig1", failed));
        Assert.Equal((true, WillBuildReason.NeverBuilt),
            With(Ledger(evidenceMissing: true), "sig1", new BuildState("A", null, LastRunAt: DateTimeOffset.UtcNow)));
    }

    /// <summary>§5.3: içerik değiştiyse (<c>SignatureChanged</c>, <c>DepIssue</c>) proje zaten derlenecek —
    /// bozuk kopya gerekçeyi değiştirmez; <c>modified</c>/<c>affected</c> ayrımı bugünkü kaynaktan.</summary>
    [Fact]
    public void A_changed_signature_is_not_overridden_by_the_vetoes()
    {
        var depIssue = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true);

        Assert.Equal((true, WillBuildReason.SignatureChanged), With(Ledger(fedIntact: false), "sig2", Clean));
        Assert.Equal((true, WillBuildReason.DepIssue), With(Ledger(fedIntact: false), "sig1", depIssue));
    }

    /// <summary>§5.4, §7-3/4/39: zaman kipi ve taze — kayıt olmasa da proje <c>BuiltOutside</c>'tır ve
    /// derlenmez. Kapsam dışı döngü üyesi yine <c>false</c>; hollow (imza yok) yine hollow.</summary>
    [Fact]
    public void Time_mode_fresh_is_built_outside_and_skipped()
    {
        Assert.Equal((false, WillBuildReason.BuiltOutside), With(Time(TimeVerdict.Fresh), "sig1", null));
        Assert.Equal((false, WillBuildReason.BuiltOutside), With(Time(TimeVerdict.Fresh), "sig2", Clean));
        Assert.Equal((false, WillBuildReason.BuiltOutside),
            With(Time(TimeVerdict.Fresh), "sig1", null, inCycle: true, buildCycles: true));
        Assert.Equal((false, WillBuildReason.OutputStale),
            With(Time(TimeVerdict.OwnNewer), "sig1", null, inCycle: true, buildCycles: false));
        Assert.Equal((null, null), With(Time(TimeVerdict.Fresh), null, null));
    }

    /// <summary>§5.4: zaman kipinde kırmızı yok — hata imzası bugünküyle eşleşse de, dep-issue notu olsa da
    /// defter notları BU YÜZEYDE okunmaz; hüküm kanıttan. Bağımlılık notu tek bir projeye bakarak
    /// değerlendirilemez (kökün bugünkü hâli gerekir) ve yalnız <c>BuildPreview</c>'da, planın tamamı
    /// görünürken okunur — bkz. <c>IncrementalPlannerTests</c>'in bağımlılık notu testleri.</summary>
    [Fact]
    public void Time_mode_never_reads_red()
    {
        var failed = new BuildState("A", "sig0", LastResult: BuildResult.Failed, FailedSignature: "sig1");
        var note = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Up\Up.csproj"]);

        Assert.Equal((false, WillBuildReason.BuiltOutside), With(Time(TimeVerdict.Fresh), "sig1", failed));
        Assert.Equal((true, WillBuildReason.OutputStale), With(Time(TimeVerdict.OwnNewer), "sig1", failed));
        Assert.Equal((false, WillBuildReason.BuiltOutside),
            With(Time(TimeVerdict.Fresh), "sig1", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true)));
        Assert.Equal((false, WillBuildReason.BuiltOutside), With(Time(TimeVerdict.Fresh), "sig1", note));
    }

    /// <summary><see cref="WillBuildEvaluator.OutputIsCurrent"/> <c>WillBuild</c> kapısının TA KENDİSİDİR ve
    /// koşullu yeniden derlemenin kök sınıflandırması ile bağımlılık notunun geçerlilik kapısı da onu okur
    /// (kopya YASAK). Yalnız iki gerekçe "çıktı güncel" der; gerekçesiz (hollow) hâl güncel değildir.</summary>
    [Fact]
    public void Output_is_current_for_exactly_up_to_date_and_built_outside()
    {
        foreach (var reason in Enum.GetValues<WillBuildReason>())
            Assert.Equal(
                reason is WillBuildReason.UpToDate or WillBuildReason.BuiltOutside,
                WillBuildEvaluator.OutputIsCurrent(reason));
        Assert.False(WillBuildEvaluator.OutputIsCurrent(null));
    }

    /// <summary>§5.4: zaman hükmü → gerekçe. Kendi girdisi ya da HintPath hedefi yeni ⇒ <c>OutputStale</c>;
    /// kanıt yok ⇒ <c>OutputMissing</c>; beslenen kopya bozuk ⇒ <c>OutputReplaced</c>. Hepsi derlenir.</summary>
    [Fact]
    public void Time_mode_own_newer_is_output_stale()
    {
        Assert.Equal((true, WillBuildReason.OutputStale), With(Time(TimeVerdict.OwnNewer), "sig1", Clean));
        Assert.Equal((true, WillBuildReason.OutputStale), With(Time(TimeVerdict.DependencyNewer), "sig1", Clean));
        Assert.Equal((true, WillBuildReason.OutputMissing), With(Time(TimeVerdict.Missing), "sig1", null));
        Assert.Equal((true, WillBuildReason.OutputReplaced), With(Time(TimeVerdict.FedBroken), "sig1", Clean));
    }

    /// <summary>§5.1: kanıt yolu bilinmiyor (<c>Mode=None</c>) ya da kanıt verilmedi (<c>null</c>) ⇒ bugünkü karar,
    /// hiçbir veto yok — yukarıdaki tablo testleri kanıtsız çağrıyla aynen geçer.</summary>
    [Fact]
    public void Mode_none_is_todays_decision()
    {
        var none = new OutputCheck(EvidenceMode.None, false, true, null, null);
        BuildState?[] states =
        [
            null,
            Clean,
            new BuildState("A", "sig0", LastResult: BuildResult.Failed, FailedSignature: "sig1"),
            new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true),
            new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true, DepIssueRoots: ["U"]),
        ];

        foreach (var state in states)
            foreach (string? signature in new[] { "sig1", "sig2", null })
                foreach (bool inCycle in new[] { false, true })
                    Assert.Equal(
                        WillBuildEvaluator.EvaluateWithReason(inCycle, signature, state, buildCycles: false),
                        With(none, signature, state, inCycle));
    }
}
