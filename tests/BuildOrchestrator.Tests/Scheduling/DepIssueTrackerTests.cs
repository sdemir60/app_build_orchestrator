using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Tests.Scheduling;

/// <summary>
/// [T54] DepIssueTracker: resolved = succeeded|failed|skipped (ReadySetScheduler zaten böyle — bu değişmez);
/// başarısız bir bağımlılık dependent'i BLOKLAMAZ ama kök hata adı (CS0006 zinciri, OSYS It-2 acceptance) zincir
/// boyunca "depIssues" olarak taşınır. Saf hesaplama — scheduler/Supervisor'dan bağımsız test edilir [D3].
/// </summary>
public class DepIssueTrackerTests
{
    private static readonly Func<string, string> IdentityName = id => id;

    private static Dictionary<string, BuildResult> Completed(params (string Id, BuildResult Result)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, BuildResult>(e.Id, e.Result)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<string>> Issues(params (string Id, string[] Roots)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, IReadOnlyList<string>>(e.Id, e.Roots)),
            StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- CS0006 zinciri (doğrudan + miras)

    [Fact]
    public void direct_failed_dependency_produces_a_dep_issue_carrying_its_own_name()
    {
        // A fails → B (A'ya doğrudan bağımlı) depIssues=[A] taşır — A dispatch anında zaten terminal (resolved-gate).
        var completed = Completed(("A", BuildResult.Failed));
        var depIssuesById = Issues(); // henüz hiçbir proje tamamlanmadı (A'nın kendi depIssue'u yok, kök zaten o)

        var result = DepIssueTracker.Compute(["A"], completed, depIssuesById, IdentityName);

        Assert.Equal(["A"], result.All);
        Assert.Equal(["A"], result.Direct);
        Assert.Empty(result.Indirect);
    }

    [Fact]
    public void a_dependency_of_a_dependency_inherits_the_root_without_depending_on_it_directly()
    {
        // C, B'ye bağımlı (B'nin kendisi A'ya bağımlıydı ve B.depIssues=[A] olarak önceden hesaplanmıştı).
        // C, A'ya DOĞRUDAN bağımlı DEĞİL — yine de A'yı miras alır (zincir).
        var completed = Completed(("A", BuildResult.Failed), ("B", BuildResult.Succeeded));
        var depIssuesById = Issues(("B", ["A"]));

        var result = DepIssueTracker.Compute(["B"], completed, depIssuesById, IdentityName);

        Assert.Equal(["A"], result.All);
        Assert.Empty(result.Direct);   // B'nin kendisi failed DEĞİL
        Assert.Equal(["A"], result.Indirect); // A, B üzerinden miras alındı
    }

    [Fact]
    public void direct_and_inherited_roots_merge_deduped_and_sorted()
    {
        // D, doğrudan-failed X'e VE zincirden [Y] miras alan W'ye bağımlı → depIssues=[X, Y] (birleşim, sıralı).
        var completed = Completed(("X", BuildResult.Failed), ("W", BuildResult.Succeeded));
        var depIssuesById = Issues(("W", ["Y"]));

        var result = DepIssueTracker.Compute(["X", "W"], completed, depIssuesById, IdentityName);

        Assert.Equal(["X", "Y"], result.All);
        Assert.Equal(["X"], result.Direct);
        Assert.Equal(["Y"], result.Indirect);
    }

    [Fact]
    public void roots_are_sorted_deterministically_regardless_of_dependency_order()
    {
        var completed = Completed(("Zeta", BuildResult.Failed), ("Alpha", BuildResult.Failed));

        var result = DepIssueTracker.Compute(["Zeta", "Alpha"], completed, Issues(), IdentityName);

        Assert.Equal(["Alpha", "Zeta"], result.All); // girdi sırası Zeta,Alpha olsa da çıktı alfabetik
        Assert.Equal(["Alpha", "Zeta"], result.Direct);
    }

    // ---------------------------------------------------------------- skipped/succeeded → depIssue YOK

    [Fact]
    public void a_skipped_dependency_does_not_produce_a_dep_issue()
    {
        // Skipped resolved sayılır (bloklamaz) ama yalnız FAILED kökler taşınır — v7 A6.
        var completed = Completed(("A", BuildResult.Skipped));

        var result = DepIssueTracker.Compute(["A"], completed, Issues(), IdentityName);

        Assert.Empty(result.All);
        Assert.Empty(result.Direct);
        Assert.Empty(result.Indirect);
    }

    [Fact]
    public void a_project_with_all_succeeded_dependencies_has_no_dep_issues()
    {
        var completed = Completed(("A", BuildResult.Succeeded), ("B", BuildResult.Succeeded));

        var result = DepIssueTracker.Compute(["A", "B"], completed, Issues(), IdentityName);

        Assert.Empty(result.All);
    }

    [Fact]
    public void no_dependencies_means_no_dep_issues()
    {
        var result = DepIssueTracker.Compute([], Completed(), Issues(), IdentityName);

        Assert.Empty(result.All);
    }

    // ---------------------------------------------------------------- diamond dedup

    [Fact]
    public void a_shared_failed_root_reaching_a_diamond_join_through_two_paths_is_deduped()
    {
        // R fails. M ve N ikisi de R'ye doğrudan bağımlı (M.depIssues=[R], N.depIssues=[R] önceden hesaplanmış).
        // J, hem M'ye hem N'ye bağımlı → R iki kez DEĞİL, bir kez görünmeli.
        var completed = Completed(("R", BuildResult.Failed), ("M", BuildResult.Succeeded), ("N", BuildResult.Succeeded));
        var depIssuesById = Issues(("M", ["R"]), ("N", ["R"]));

        var result = DepIssueTracker.Compute(["M", "N"], completed, depIssuesById, IdentityName);

        Assert.Equal(["R"], result.All);   // dedup: bir kez
        Assert.Empty(result.Direct);       // J, R'ye doğrudan bağımlı değil
        Assert.Equal(["R"], result.Indirect);
    }

    [Fact]
    public void a_root_that_is_both_a_direct_failure_and_inherited_through_another_path_appears_once_and_counts_as_direct()
    {
        // R hem J'nin doğrudan bağımlılığı (failed) HEM de M üzerinden miras — All'da bir kez, Direct'te sayılır,
        // Indirect'te TEKRAR edilmez.
        var completed = Completed(("R", BuildResult.Failed), ("M", BuildResult.Succeeded));
        var depIssuesById = Issues(("M", ["R"]));

        var result = DepIssueTracker.Compute(["R", "M"], completed, depIssuesById, IdentityName);

        Assert.Equal(["R"], result.All);
        Assert.Equal(["R"], result.Direct);
        Assert.Empty(result.Indirect); // R zaten Direct'te — Indirect'te ikinci kez sayılmaz
    }

    // ---------------------------------------------------------------- ad çözümü (id != görünen ad)

    [Fact]
    public void root_names_come_from_the_name_lookup_not_the_raw_dependency_id()
    {
        var completed = Completed(("proj-a-id", BuildResult.Failed));

        var result = DepIssueTracker.Compute(["proj-a-id"], completed, Issues(),
            id => id == "proj-a-id" ? "A" : id);

        Assert.Equal(["A"], result.All);
    }
}
