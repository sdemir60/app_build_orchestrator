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
    /// <c>!cycleGroupMember</c> kuralıyla AYNI) — ama bir sonraki Sync'in <c>WillBuildEvaluator</c>'ı bu üyeyi
    /// yine de <c>WaitingForDependency</c> okur ("etiket bir disk olgusudur" kuralı, §13.2): defter GERÇEKTEN
    /// not+kök yazdı (grup YAKINSADI, sonuç güvenilir), yalnız <c>WillBuild</c> döngü kapsamı yüzünden
    /// <c>false</c>'a ZORLANIR. Canlı geçiş bu ÜÇLÜYÜ BİREBİR üretmeli — aksi hâlde etiket bir sonraki Sync'te
    /// FLİP EDER (round 1'in bıraktığı boşluk: <c>UpToDate</c> canlı → <c>WaitingForDependency</c> Sync sonrası).
    /// </summary>
    [Fact]
    public void a_converged_cycle_member_with_a_dep_issue_waits_without_being_conditional()
        => Assert.Equal((false, WillBuildReason.WaitingForDependency, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: true, ["Up"]));

    /// <summary>[I1 (ii)] Yakınsamayan bir grubun üyesi (<c>trustedResult=false</c>, <c>RunCoordinator</c> onu
    /// PERSIST ETMEZ, kaydını kanıtsız hata olarak geçersizleştirir).
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eski iddia: <c>(false, UpToDate, false)</c> — "defter bu
    /// başarıdan hiçbir şey öğrenmedi, satır bugünkü olguya döner". Yanlıştı: defter öğrenir, kaydı
    /// <c>LastResult=Failed</c>/<c>FailedSignature=null</c> olur ve bir sonraki Sync onu <c>NeverBuilt</c>
    /// okur — satır canlıda yeşil ✓, Sync sonrası gri ○ idi. Yeni cevap evaluator'ın o kayda verdiği cevabın
    /// kendisidir (aşağıda aynı kayıtla doğrulanır).</para></summary>
    [Fact]
    public void an_untrusted_cycle_member_reads_what_the_invalidated_ledger_will_say()
    {
        Assert.Equal((false, WillBuildReason.NeverBuilt, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: false, ["Up"]));
        Assert.Equal((false, WillBuildReason.NeverBuilt, false),
            NextPreview.AfterSuccess(inCycle: true, trusted: false, depIssues: null));

        // Motorun gerçekten yazdığı kayıt (dün yeşil, bugün güvenilmez başarı ⇒ kanıtsız invalidate):
        var invalidated = new BuildState("A", BuiltSignature: "old", LastResult: BuildResult.Failed, FailedSignature: null);
        var (willBuild, reason) = WillBuildEvaluator.EvaluateWithReason(inCycle: true, "sig", invalidated, buildCycles: false);
        Assert.False(willBuild);
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

    /// <summary>Clean kaydı SİLER; kayıt yoksa evaluator <c>NeverBuilt</c> der.</summary>
    [Fact]
    public void a_cleaned_project_reads_never_built_like_a_missing_ledger_row()
    {
        Assert.Equal(WillBuildReason.NeverBuilt, NextPreview.AfterClean);
        Assert.Equal(NextPreview.AfterClean,
            WillBuildEvaluator.EvaluateWithReason(inCycle: false, "sig", state: null, buildCycles: false).Reason);
    }

    // ---------------------------------------------------------------- AfterConfigurationChange [R-Config · M6]

    [Theory]
    [InlineData(WillBuildReason.UpToDate, "abc", WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.WaitingForDependency, null, WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.SignatureChanged, "abc", WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.LastFailed, "abc", WillBuildReason.SignatureChanged)] // başarı izi var
    [InlineData(WillBuildReason.LastFailed, null, WillBuildReason.NeverBuilt)]        // hiç başarı yok
    [InlineData(WillBuildReason.NeverBuilt, "abc", WillBuildReason.NeverBuilt)]
    // [Task 7 — Faz 3] Diğer üç yeni gerekçe bugünkü düşüşü izler: SignatureChanged. OutputMissing kendi
    // satırında ayrı test edilir (dedicated Fact, aşağıda) — motor zaten "çıktı yok" diyor, "signature changed"
    // onu YANLIŞ ANLATIR.
    [InlineData(WillBuildReason.BuiltOutside, "abc", WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.OutputStale, "abc", WillBuildReason.SignatureChanged)]
    [InlineData(WillBuildReason.OutputReplaced, "abc", WillBuildReason.SignatureChanged)]
    public void a_configuration_change_reads_signature_changed_unless_nothing_ever_succeeded(
        WillBuildReason before, string? builtCommit, WillBuildReason expected)
        => Assert.Equal(expected, NextPreview.AfterConfigurationChange(before, builtCommit));

    /// <summary>[Task 7 — Faz 3] OutputMissing configuration değişiminden SONRA da <c>never built</c> okunmalı:
    /// motor zaten çıktı kanıtı olmadığını söylüyor, <c>SignatureChanged</c> "bir şey değişti" der ki bu proje
    /// için yanlıştır — hiç derlenmemiş gibi okunmalı.</summary>
    [Fact]
    public void A_missing_output_stays_never_built_after_a_configuration_change()
        => Assert.Equal(WillBuildReason.NeverBuilt,
            NextPreview.AfterConfigurationChange(WillBuildReason.OutputMissing, "abc"));
}
