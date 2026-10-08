using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// App'in canlı geçişlerinin saf eşlemeleri (<see cref="NextPreview"/>): her biri, motorun o anda deftere
/// yazdığı kaydın bir sonraki önizlemede (<see cref="WillBuildEvaluator"/>) neye düştüğünü söyler. Eşlemelerin
/// bir kısmı, defterin GERÇEK kaydını evaluator'a verip aynı cevabı aldığını da pinler — kopya değil, aynı karar.
/// </summary>
public class NextPreviewTests
{
    // ---------------------------------------------------------------- AfterSuccess [Task 4 review round 1+2 — I1]

    [Fact]
    public void a_plain_project_succeeding_with_a_dep_issue_waits_and_is_conditional()
        => Assert.Equal((true, WillBuildReason.WaitingForDependency, true),
            NextPreview.AfterSuccess(inCycle: false, trusted: true, ["Up"]));

    [Fact]
    public void a_success_without_a_dep_issue_is_up_to_date()
        => Assert.Equal((false, WillBuildReason.UpToDate, false),
            NextPreview.AfterSuccess(inCycle: false, trusted: true, depIssues: null));

    /// <summary>
    /// [I1 (i) · round 2] Bir SCC üyesi TEK BAŞINA hiçbir zaman koşullu DEĞİLDİR (<see cref="ConditionalRebuild.AppliesTo"/>'nun
    /// <c>!cycleGroupMember</c> kuralıyla AYNI): grubun kaderine <see cref="ConditionalRebuild.GroupAppliesTo"/> dispatch
    /// anında grup düzeyinde karar verir. Bir sonraki Sync'in <c>WillBuildEvaluator</c>'ı bu üyeyi
    /// <c>WaitingForDependency</c> okur ("etiket bir disk olgusudur" kuralı, §13.2): defter GERÇEKTEN not+kök yazdı
    /// (grup YAKINSADI, sonuç güvenilir). Canlı geçiş bu ÜÇLÜYÜ BİREBİR üretmeli — aksi hâlde etiket bir sonraki
    /// Sync'te FLİP EDER (round 1'in bıraktığı boşluk: <c>UpToDate</c> canlı → <c>WaitingForDependency</c> Sync sonrası).
    /// <para><b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski iddia: <c>(false, WaitingForDependency, false)</c> — bir
    /// sonraki Sync üyeyi <c>buildCycles: false</c> ile değerlendirir, <c>WillBuild</c> döngü kapsamı yüzünden
    /// <c>false</c>'a ZORLANIRDI. Değişme gerekçesi (ölçüm, 2026-10-07 13:17 koşusu, ARCHITECTURE §8.1): düz Build
    /// kirli cycle grubunu da derler; Sync üyeyi Build'in kararıyla (<see cref="CycleCompilation"/>) değerlendirir ve
    /// bekleyen üye "derlenecek" okunur. Aynı kayıt evaluator'a verilerek doğrulanır — kopya değil, aynı karar.</para>
    /// </summary>
    [Fact]
    public void a_converged_cycle_member_with_a_dep_issue_waits_without_being_conditional()
    {
        Assert.Equal((true, WillBuildReason.WaitingForDependency, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: true, ["Up"]));

        // Motorun yakınsamış gruptaki dep-issue'lu üye için gerçekten yazdığı kayıt (taze imza + not + kök):
        var noted = new BuildState("A", BuiltSignature: "sig", LastResult: BuildResult.Succeeded,
            DepIssue: true, DepIssueRoots: ["Up"]);
        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(inCycle: true, "sig", noted,
            buildCycles: CycleCompilation.CompilesCycles(RunMode.Build));
        Assert.True(willBuild);
        Assert.Equal(WillBuildReason.WaitingForDependency, reason);
    }

    /// <summary>[I1 (ii)] Yakınsamayan bir grubun üyesi (<c>trustedResult=false</c>, <c>RunCoordinator</c> onu
    /// PERSIST ETMEZ, kaydını kanıtsız hata olarak geçersizleştirir).
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eski iddia: <c>(false, UpToDate, false)</c> — "defter bu
    /// başarıdan hiçbir şey öğrenmedi, satır bugünkü olguya döner". Yanlıştı: defter öğrenir, kaydı
    /// <c>LastResult=Failed</c>/<c>FailedSignature=null</c> olur ve bir sonraki Sync onu <c>NeverBuilt</c>
    /// okur — satır canlıda yeşil ✓, Sync sonrası gri ○ idi. Yeni cevap evaluator'ın o kayda verdiği cevabın
    /// kendisidir (aşağıda aynı kayıtla doğrulanır).</para>
    /// <para><b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski iddia: <c>WillBuild=false</c> — döngü üyesi bir sonraki
    /// Sync'in (<c>buildCycles: false</c>) kapsamı dışındaydı. Değişme gerekçesi (ölçüm, 2026-10-07 13:17 koşusu,
    /// ARCHITECTURE §8.1): düz Build kirli grubu derler; kanıtsız hata kaydı taşıyan üye düz projeyle aynı biçimde
    /// "derlenecek" okunur (<see cref="CycleCompilation"/>).</para></summary>
    [Fact]
    public void an_untrusted_cycle_member_reads_what_the_invalidated_ledger_will_say()
    {
        Assert.Equal((true, WillBuildReason.NeverBuilt, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: false, ["Up"]));
        Assert.Equal((true, WillBuildReason.NeverBuilt, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: false, depIssues: null));

        // Motorun gerçekten yazdığı kayıt (dün yeşil, bugün güvenilmez başarı ⇒ kanıtsız invalidate):
        var invalidated = new BuildState("A", BuiltSignature: "old", LastResult: BuildResult.Failed, FailedSignature: null);
        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(inCycle: true, "sig", invalidated,
            buildCycles: CycleCompilation.CompilesCycles(RunMode.Build));
        Assert.True(willBuild);
        Assert.Equal(WillBuildReason.NeverBuilt, reason);
    }

    // ---------------------------------------------------------------- AfterFailure [R-M4b]

    [Fact]
    public void a_failure_with_evidence_reads_last_failed_and_without_reads_never_built()
    {
        Assert.Equal(WillBuildReason.LastFailed, NextPreview.AfterFailure(evidence: true));
        Assert.Equal(WillBuildReason.NeverBuilt, NextPreview.AfterFailure(evidence: false));
    }

    // ---------------------------------------------------------------- AfterClean

    /// <summary>
    /// Clean'in sonucu — başarı da hata da — defterde bu projenin başarısını bırakmaz: başarıda kayıt SİLİNİR
    /// (<c>BuildStateStore.Remove</c>), hatada kanıtsız hata yazılır (<c>-t:Clean</c> derleyiciyi çağırmaz, kanıt
    /// sayılmaz). Evaluator ikisini de <c>NeverBuilt</c> okur; <c>WillBuild</c> bir sonraki düz Build'in cevabıdır
    /// ve düz Build kirli döngü grubunu da derlediği için temizlenen üye de <c>true</c>'dur. Canlı geçiş bu ÜÇLÜYÜ
    /// evaluator'ın o iki kayda verdiği cevapla BİREBİR üretir.
    /// <para><b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski iddia: temizlenen döngü üyesi <c>false</c>'tur, çünkü
    /// düz Build onu derlemezdi (evaluator <c>buildCycles: false</c> ile sorulurdu). Değişme gerekçesi (ölçüm,
    /// 2026-10-07 13:17 koşusu, ARCHITECTURE §8.1): Build kirli grupları derler; evaluator Build'in kararıyla
    /// (<see cref="CycleCompilation"/>) sorulur ve temizlenen üye düz proje gibi dalgada yanar.</para>
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>a_cleaned_project_reads_never_built_like_a_missing_ledger_row</c>
    /// — eşleme yalnız gerekçeyi döndürürdü (<c>AfterClean == NeverBuilt</c>), satırın plan bayrağı Clean
    /// önizlemesinin <c>true</c>'sunda kalırdı. Değişme gerekçesi: Build menüsünün Clean'i grafın tamamını
    /// temizler ve önizlemesi her projeye <c>true</c> verir — bu, bir sonraki Build'in cevabı değildir; temizlenen
    /// döngü üyesi Build'in dalgasında boşuna yanıyordu. Bayrağı artık sonuç yazar, gerekçeyle AYNI kaynaktan.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void a_clean_result_reads_what_the_next_sync_says_for_its_ledger_row(bool inCycle)
    {
        var (willBuild, reason, conditional) = NextPreview.AfterClean(inCycle);

        bool buildCycles = CycleCompilation.CompilesCycles(RunMode.Build);
        var forgotten = WillBuildEvaluator.EvaluateWithReason(inCycle, "sig", state: null, buildCycles);
        var invalidated = WillBuildEvaluator.EvaluateWithReason(inCycle, "sig",
            new BuildState("A", BuiltSignature: "sig", LastResult: BuildResult.Failed, FailedSignature: null),
            buildCycles);
        Assert.Equal((forgotten.WillBuild, forgotten.Reason), (willBuild, reason));     // başarı: kayıt silindi
        Assert.Equal((invalidated.WillBuild, invalidated.Reason), (willBuild, reason)); // hata: kanıtsız invalidate
        Assert.True(willBuild);                                                          // döngü üyesi de derlenecek
        Assert.False(conditional);
    }

    // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29] AfterConfigurationChange eşlemesi ve iki testi
    // (a_configuration_change_reads_signature_changed_unless_nothing_ever_succeeded,
    // A_missing_output_stays_never_built_after_a_configuration_change) KALDIRILDI. Eski iddia: configuration değişince
    // her kayıtta imza değişir, satır motorun bir sonraki önizlemesinin diyeceğini şimdiden der (başarı izi varsa
    // SignatureChanged, yoksa/çıktı yoksa NeverBuilt). Ölçülen yanılgı: defter proje başına TEK imza tutar — o da
    // projenin en son derlendiği configuration'ınkidir — ve motor ona karşı karar verir; Debug → Release → Debug
    // dönüşünde motor UpToDate derken eşleme SignatureChanged diyordu. Geçiş artık hiçbir şey tahmin etmez, kendi
    // Sync'ini başlatır; yeni kural RunViewModelStateTests'te pinlidir
    // (Switching_configuration_drops_every_decision_until_its_sync_answers ve komşuları).
}
