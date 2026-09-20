using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// Koşullu yeniden derlemenin saf kararları (<see cref="ConditionalRebuild"/>): dep-issue notlu, imzası
/// değişmemiş bir proje sırası geldiğinde ANCAK kayıtlı köklerinden biri artık başarılıysa derlenir.
/// Belirsizlikte yön derlemedir.
/// </summary>
public class ConditionalRebuildTests
{
    private static Dictionary<string, BuildResult> Completed(params (string Id, BuildResult Result)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, BuildResult>(e.Id, e.Result)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, BuildState> Ledger(params (string Id, BuildResult Result)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, BuildState>(e.Id,
            new BuildState(e.Id, "sig", LastResult: e.Result))), StringComparer.OrdinalIgnoreCase);

    private static readonly Func<string, bool> Everywhere = _ => true;

    /// <summary>Önizleme bu kök için bir şey söylemiyor (gerekçe yok) — karar defterin son sonucuna düşer.</summary>
    private static readonly Func<string, WillBuildReason?> NoPreview = _ => null;

    /// <summary>Önizleme bu kökün çıktısını GÜNCEL buluyor — <paramref name="reason"/> ile hangi yolla.</summary>
    private static Func<string, WillBuildReason?> Preview(WillBuildReason reason) => _ => reason;

    // ---------------------------------------------------------------- Decide

    /// <summary>Senaryo 1 (saf): kök bu koşuda yine patladı ⇒ derlemek aynı bayat çıktıya link'lemek olurdu.</summary>
    [Fact]
    public void a_root_that_failed_again_in_this_run_keeps_the_project_waiting()
        => Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Failed)), Everywhere,
                Ledger(("A", BuildResult.Failed)), NoPreview));

    /// <summary>Senaryo 2 (saf): kök bu koşuda başarıyla derlendi ⇒ proje derlenir. Defter koşu BAŞINDAKİ
    /// hâlidir (kök orada hâlâ hatalı) — bu koşunun sonucu önce gelir.</summary>
    [Fact]
    public void a_root_that_succeeded_in_this_run_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Succeeded)), Everywhere,
                Ledger(("A", BuildResult.Failed)), NoPreview));

    /// <summary>Senaryo 3 (saf, §8.3 güvenlik): kök kaynak değişmeden düzeldi — bu koşuda "up to date" atlandı,
    /// defterdeki son sonucu başarı ⇒ proje derlenir.</summary>
    [Fact]
    public void a_root_skipped_in_this_run_whose_last_recorded_result_is_success_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Succeeded)), NoPreview));

    /// <summary>Kök bu koşuda derlenmedi, önizleme de onun için bir güncellik kanıtı taşımıyor (ör. kapsam dışı
    /// bir döngü üyesinin <c>SignatureChanged</c>'i) ⇒ karar defterin son bilinen sonucuna düşer.</summary>
    [Fact]
    public void a_root_skipped_in_this_run_whose_last_recorded_result_is_failure_keeps_the_project_waiting()
    {
        Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Failed)), NoPreview));
        Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Failed)), Preview(WillBuildReason.SignatureChanged)));
    }

    /// <summary>
    /// [fix round 1 — bulgu 1] <b>Önizleme defterden daha tazedir.</b> Kök bu koşuda derlenmedi ÇÜNKÜ önizleme
    /// çıktısını güncel buldu: dışarıda (VS'te) düzeltilip derlenmiş bir kök <c>BuiltOutside</c> okunur, defter
    /// kipinde imzası tutan bir kök <c>UpToDate</c> okunur. İkisinde de defterdeki <c>LastResult=Failed</c> kaydı
    /// aracın EN SON gördüğüdür, diskin ŞU ANKİ hâli değil — kök temiz sayılır ve proje derlenir.
    /// <para>Eskiden ikisi de <c>FailedInLedgerOnly</c>'ye düşerdi: bağımlı, aracın kendisi kökü derleyene kadar
    /// HER koşuda <c>dependency still failing</c> ile atlanırdı (kilitli durum).</para>
    /// </summary>
    [Theory]
    [InlineData(WillBuildReason.BuiltOutside)]
    [InlineData(WillBuildReason.UpToDate)]
    public void a_root_the_preview_found_current_releases_the_project(WillBuildReason reason)
    {
        Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Skipped)), Everywhere,
                Ledger(("A", BuildResult.Failed)), Preview(reason)));
        // Kök bu koşuda GERÇEKTEN patladıysa önizlemenin eski hükmü onu temize çıkarmaz.
        Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A"], Completed(("A", BuildResult.Failed)), Everywhere,
                Ledger(("A", BuildResult.Failed)), Preview(reason)));
    }

    [Fact]
    public void one_successful_root_among_failing_ones_is_enough()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A", "B"],
                Completed(("A", BuildResult.Failed), ("B", BuildResult.Succeeded)), Everywhere,
                Ledger(("A", BuildResult.Failed), ("B", BuildResult.Failed)), NoPreview));

    [Fact]
    public void all_roots_failing_across_run_and_ledger_keeps_the_project_waiting()
        => Assert.Equal(ConditionalRebuildVerdict.DependencyStillFailing,
            ConditionalRebuild.Decide(["A", "B"],
                Completed(("A", BuildResult.Failed)), Everywhere,          // B bu koşuda hiç görünmedi
                Ledger(("A", BuildResult.Succeeded), ("B", BuildResult.Failed)), NoPreview));

    /// <summary>Kök projeden kaldırıldı ⇒ bekleyecek bir şey yok, güvenli yön derlemek.</summary>
    [Fact]
    public void a_root_missing_from_the_workspace_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["Gone", "A"], Completed(("A", BuildResult.Failed)),
                id => id != "Gone", Ledger(("A", BuildResult.Failed)), NoPreview));

    /// <summary>Kökün defter kaydı yoksa sonucu bilinmiyor ⇒ derlenir.</summary>
    [Fact]
    public void a_root_without_a_ledger_record_that_did_not_run_releases_the_project()
        => Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(["A"], Completed(), Everywhere, Ledger(), NoPreview));

    /// <summary>Senaryo 4 (saf): kök listesi olmayan (eski) kayıt ⇒ bugünkü davranış, derlenir.</summary>
    [Fact]
    public void unknown_roots_release_the_project()
    {
        Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide(null, Completed(("A", BuildResult.Failed)), Everywhere, Ledger(), NoPreview));
        Assert.Equal(ConditionalRebuildVerdict.Build,
            ConditionalRebuild.Decide([], Completed(("A", BuildResult.Failed)), Everywhere, Ledger(), NoPreview));
    }

    // ---------------------------------------------------------------- AppliesTo

    private static ProjectNode Waiting(WillBuildReason reason = WillBuildReason.WaitingForDependency, bool? willBuild = true) =>
        new("P", "P", "P", [], [], 0, null, null, InCycle: false, WillBuild: willBuild, WillBuildReason: reason);

    [Fact]
    public void build_and_cycles_runs_evaluate_a_waiting_project_conditionally()
    {
        Assert.True(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Build, scopedRun: false, cycleGroupMember: false));
        Assert.True(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Cycles, scopedRun: false, cycleGroupMember: false));
    }

    /// <summary>Senaryo 7 (saf): Rebuild her şeyi derler; satırdan tetiklenen hedef koşulsuz derlenir.</summary>
    [Fact]
    public void rebuild_and_single_project_runs_are_unconditional()
    {
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Rebuild, scopedRun: false, cycleGroupMember: false));
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Build, scopedRun: true, cycleGroupMember: false));
    }

    [Fact]
    public void a_cycle_group_member_is_unconditional()
        => Assert.False(ConditionalRebuild.AppliesTo(Waiting(), RunMode.Cycles, scopedRun: false, cycleGroupMember: true));

    [Fact]
    public void only_the_waiting_reason_makes_a_project_conditional()
    {
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(WillBuildReason.DepIssue), RunMode.Build, false, false));
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(WillBuildReason.SignatureChanged), RunMode.Build, false, false));
        // Pre-skip edilen (WillBuild=false) proje koşuda zaten derlenmez — koşullu da sayılmaz.
        Assert.False(ConditionalRebuild.AppliesTo(Waiting(willBuild: false), RunMode.Cycles, false, false));
    }

    // ---------------------------------------------------------------- RootNames

    [Fact]
    public void root_names_are_display_names_sorted_with_a_file_name_fallback()
    {
        var state = new BuildState("P", "sig", LastResult: BuildResult.Succeeded, DepIssue: true,
            DepIssueRoots: [@"C:\r\Zeta\Zeta.csproj", @"C:\r\gone\Gone.csproj", @"C:\r\A\A.csproj"]);

        var names = ConditionalRebuild.RootNames(WillBuildReason.WaitingForDependency, state,
            id => id.EndsWith("Zeta.csproj", StringComparison.Ordinal) ? "OSYS.Zeta"
                : id.EndsWith("A.csproj", StringComparison.Ordinal) ? "OSYS.A" : null);

        Assert.Equal(["Gone", "OSYS.A", "OSYS.Zeta"], names);
    }

    [Fact]
    public void there_are_no_root_names_without_recorded_roots()
    {
        Assert.Null(ConditionalRebuild.RootNames(WillBuildReason.WaitingForDependency, null, _ => null));
        Assert.Null(ConditionalRebuild.RootNames(WillBuildReason.WaitingForDependency,
            new BuildState("P", "sig", LastResult: BuildResult.Succeeded, DepIssue: true), _ => null));
        // Kökler kayıtlı ama proje beklemede değil (ör. imzası değişti) — etiket kök yazmaz.
        Assert.Null(ConditionalRebuild.RootNames(WillBuildReason.SignatureChanged,
            new BuildState("P", "sig", LastResult: BuildResult.Succeeded, DepIssue: true, DepIssueRoots: ["U"]), _ => "U"));
    }

    // ---------------------------------------------------------------- DescribeStillFailingRoots [Task 4 — carried item 3]

    /// <summary>
    /// [carried item 3] Kök bu koşuda GERÇEKTEN patladıysa ("dependency still failing" verdiğini üreten aynı
    /// veri) metin çıplak addır — bugünkü <c>ReportSkipped</c> satırıyla (<c>"…(Up)"</c>) AYNI, mevcut
    /// <c>ConditionalRebuildRunTests.A_waiting_project_whose_root_fails_again_is_skipped…</c> testi kırılmaz.
    /// </summary>
    [Fact]
    public void a_root_that_actually_failed_in_this_run_has_a_bare_name()
        => Assert.Equal(["A"], ConditionalRebuild.DescribeStillFailingRoots(
            ["A"], Completed(("A", BuildResult.Failed)), Everywhere, Ledger(("A", BuildResult.Failed)),
            NoPreview, _ => "A"));

    /// <summary>
    /// [carried item 3] Kök bu koşuda HİÇ denenmedi (ör. bir SCC üyesi — Build modunda "in dependency cycle"
    /// ile pre-skip edilir) ve "hâlâ hatalı" iddiası yalnız DEFTERDEN geliyorsa metin bunu AYIRT EDER — "R
    /// failed in this run" YALANI söylenmez, son bilinen sonuç olduğu belirtilir.
    /// </summary>
    [Fact]
    public void a_root_only_known_failing_from_the_ledger_is_labelled_as_such()
        => Assert.Equal(["A (last known failure)"], ConditionalRebuild.DescribeStillFailingRoots(
            ["A"], Completed(("A", BuildResult.Skipped)), Everywhere, Ledger(("A", BuildResult.Failed)),
            NoPreview, _ => "A"));

    /// <summary>Karışık: bir kök bu koşuda patladı, diğeri yalnız defterden — isim sıralı, her biri kendi etiketiyle.</summary>
    [Fact]
    public void mixed_roots_are_each_labelled_by_their_own_evidence()
        => Assert.Equal(["A", "B (last known failure)"], ConditionalRebuild.DescribeStillFailingRoots(
            ["A", "B"], Completed(("A", BuildResult.Failed), ("B", BuildResult.Skipped)), Everywhere,
            Ledger(("A", BuildResult.Failed), ("B", BuildResult.Failed)), NoPreview, id => id));

    /// <summary>[fix round 1 — bulgu 1] Önizlemenin güncel bulduğu kök <c>Cleared</c>'dır, yani "hâlâ hatalı"
    /// listesinde HİÇ görünmez — karar ile satır metni AYNI sınıflandırmayı okur.</summary>
    [Fact]
    public void a_root_the_preview_found_current_is_not_described_as_failing()
        => Assert.Empty(ConditionalRebuild.DescribeStillFailingRoots(
            ["A"], Completed(("A", BuildResult.Skipped)), Everywhere, Ledger(("A", BuildResult.Failed)),
            Preview(WillBuildReason.BuiltOutside), _ => "A"));

    [Fact]
    public void an_empty_or_null_root_list_describes_nothing()
    {
        Assert.Empty(ConditionalRebuild.DescribeStillFailingRoots(
            null, Completed(), Everywhere, Ledger(), NoPreview, _ => "X"));
        Assert.Empty(ConditionalRebuild.DescribeStillFailingRoots(
            [], Completed(), Everywhere, Ledger(), NoPreview, _ => "X"));
    }
}
