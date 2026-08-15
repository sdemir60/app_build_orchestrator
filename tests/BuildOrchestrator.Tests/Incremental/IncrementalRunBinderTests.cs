using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

// [Task 19 wiring] IncrementalRunBinder — Program.cs ↔ IncrementalPlanner glue: repo-relative path mapping
// (Task 7b), dirty attribution (proje dizinine göre) ve non-null imza haritası. Gerçek git YOK — trackedBlobHashes
// haritası ve dirty yolları doğrudan enjekte edilir; committed dosyalar diskte gerçek dosyalardır (binder
// File.ReadAllText'i yalnız dirty in-place dosyalar için çağırır).
public sealed class IncrementalRunBinderTests
{
    [Fact]
    public void ToRepoRelativeNormalized_forward_slashes_and_relative_to_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "repoX");
        string abs = Path.Combine(root, "src", "A", "A.csproj");
        Assert.Equal("src/A/A.csproj", IncrementalRunBinder.ToRepoRelativeNormalized(root, abs));
    }

    [Fact]
    public void clean_project_with_matching_committed_fingerprint_and_succeeded_state_is_skipped_and_signature_persistable()
    {
        // Gerçek dizin: repo/src/A/A.csproj + A.cs. ls-tree haritası bu iki dosyanın committed blob SHA'sını taşır.
        string root = Directory.CreateTempSubdirectory("bo-binder-").FullName;
        try
        {
            string projDir = Path.Combine(root, "src", "A");
            Directory.CreateDirectory(projDir);
            string csproj = Path.Combine(projDir, "A.csproj");
            string cs = Path.Combine(projDir, "A.cs");
            File.WriteAllText(csproj, "<Project/>");
            File.WriteAllText(cs, "class A {}");

            var evaluated = new EvaluatedProject(csproj, "A", [cs], [], [], IsSdkStyle: true);
            var node = new ProjectNode(csproj, "A", csproj, [], [], 0, null, null, InCycle: false, WillBuild: null);
            var plan = new BuildPlan([node], [], "Debug");

            var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/A/A.csproj"] = "blob-csproj",
                ["src/A/A.cs"] = "blob-cs",
            };

            // Önce state YOK → willBuild=true, imza döner (persist edilebilir).
            var (planA, sigA) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], new Dictionary<string, BuildState>(), inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            Assert.True(planA.Nodes[0].WillBuild);              // hiç derlenmemiş → dirty
            Assert.True(sigA.ContainsKey(csproj));              // imza persist edilebilir (non-null)

            // O imzayı Succeeded state olarak geri besle → aynı committed fingerprint + temiz working tree → skip.
            var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sigA[csproj], LastResult: BuildResult.Succeeded),
            };
            var (planB, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], state, inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            Assert.False(planB.Nodes[0].WillBuild);            // temiz + imza eşit + Succeeded → skip
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void a_dirty_source_file_under_the_project_directory_flips_will_build_to_dirty()
    {
        string root = Directory.CreateTempSubdirectory("bo-binder-").FullName;
        try
        {
            string projDir = Path.Combine(root, "src", "A");
            Directory.CreateDirectory(projDir);
            string csproj = Path.Combine(projDir, "A.csproj");
            string cs = Path.Combine(projDir, "A.cs");
            File.WriteAllText(csproj, "<Project/>");
            File.WriteAllText(cs, "class A { int changed; }");

            var evaluated = new EvaluatedProject(csproj, "A", [cs], [], [], IsSdkStyle: true);
            var node = new ProjectNode(csproj, "A", csproj, [], [], 0, null, null, InCycle: false, WillBuild: null);
            var plan = new BuildPlan([node], [], "Debug");
            var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/A/A.csproj"] = "blob-csproj",
                ["src/A/A.cs"] = "blob-cs",
            };

            // Temiz koşuda imzayı al, Succeeded state kur.
            var (_, sigClean) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], new Dictionary<string, BuildState>(), inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sigClean[csproj], LastResult: BuildResult.Succeeded),
            };

            // Şimdi A.cs working-tree'de dirty (git porcelain repo-relative "src/A/A.cs") → imza değişir → dirty.
            var (planDirty, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                ["src/A/A.cs"], state, inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            Assert.True(planDirty.Nodes[0].WillBuild);
        }
        finally { TryDelete(root); }
    }

    /// <summary>
    /// AYIRT EDİCİ — bağımlılığı hatalı olduğu NOTLA kaydedilmiş bir proje, imzası GÜNCEL olsa bile yine
    /// "derlenecek" gelir. Yeni persist kuralının (bkz. <c>RunCoordinatorTests
    /// .A_success_carrying_a_dep_issue_is_persisted_with_the_dep_issue_flag</c>) ikinci yarısı budur:
    /// defter ilerler ama derlenecek KÜME daralmaz. Not olmasaydı bu proje pre-skip edilir ve bayat bir
    /// binary'e link'li kalırdı.
    /// </summary>
    [Fact]
    public void a_project_recorded_against_a_failed_dependency_stays_dirty_even_with_a_matching_signature()
    {
        string root = Directory.CreateTempSubdirectory("bo-binder-").FullName;
        try
        {
            string projDir = Path.Combine(root, "src", "A");
            Directory.CreateDirectory(projDir);
            string csproj = Path.Combine(projDir, "A.csproj");
            File.WriteAllText(csproj, "<Project/>");

            var evaluated = new EvaluatedProject(csproj, "A", [], [], [], IsSdkStyle: true);
            var node = new ProjectNode(csproj, "A", csproj, [], [], 0, null, null, InCycle: false, WillBuild: null);
            var plan = new BuildPlan([node], [], "Debug");
            var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/A/A.csproj"] = "blob-csproj",
            };

            var (_, sig) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], new Dictionary<string, BuildState>(), inPlace: true, buildCycles: false, mode: DependentMode.Safe);

            // Aynı imza + Succeeded ama DepIssue notu var.
            var flagged = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sig[csproj], LastResult: BuildResult.Succeeded, DepIssue: true),
            };
            var (planFlagged, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], flagged, inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            Assert.True(planFlagged.Nodes[0].WillBuild);

            // Kontrol grubu: notsuz aynı kayıt temizdir.
            var clean = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sig[csproj], LastResult: BuildResult.Succeeded),
            };
            var (planClean, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], clean, inPlace: true, buildCycles: false, mode: DependentMode.Safe);
            Assert.False(planClean.Nodes[0].WillBuild);
        }
        finally { TryDelete(root); }
    }

    /// <summary>
    /// AYIRT EDİCİ — bir worktree'de derlenen proje, ana kökte yapılan bir sonraki Sync'te BULUNUR ve
    /// "güncel" sayılır.
    ///
    /// <para>Sahada latent duran kusur buydu: kimlik tam csproj yolu olduğu için worktree koşusu kayıtları
    /// worktree yollarıyla yazıyor, in-place Sync onları ana kök id'siyle arayıp bulamıyordu — yani farklı
    /// bir branch'e alınan TEK bir Build'den sonra her şey yeniden "derlenecek" görünüyordu. Ayrıca imzanın
    /// upstream terimi bağımlılık id'lerini hash'lediği için imzalar da ayrışıyordu.</para>
    ///
    /// <para>Çözüm kimliği imza hesabından ÖNCE ana köke taşımaktır (<see cref="ProjectIdentityRebase"/>);
    /// bu test o zinciri uçtan uca sürer. Ayrıca sessizce şunu da pinler: temiz bir ağaçta in-place imza ile
    /// worktree imzası BİREBİR aynıdır (dirty listesi boşken <c>diff=</c> terimi iki hâlde de boştur).</para>
    /// </summary>
    [Fact]
    public void a_project_built_in_a_worktree_is_recognised_as_up_to_date_by_the_next_in_place_sync()
    {
        string main = Directory.CreateTempSubdirectory("bo-main-").FullName;
        string tree = Directory.CreateTempSubdirectory("bo-tree-").FullName;
        try
        {
            // Aynı ağaç iki kökte: repo-göreli yollar birebir aynı.
            foreach (string root in new[] { main, tree })
            {
                Directory.CreateDirectory(Path.Combine(root, "src", "A"));
                File.WriteAllText(Path.Combine(root, "src", "A", "A.csproj"), "<Project/>");
                File.WriteAllText(Path.Combine(root, "src", "A", "A.cs"), "class A {}");
            }
            var tracked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/A/A.csproj"] = "blob-csproj",
                ["src/A/A.cs"] = "blob-cs",
            };

            static (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> Ev) Workspace(string root)
            {
                string csproj = Path.Combine(root, "src", "A", "A.csproj");
                var ev = new EvaluatedProject(csproj, "A", [Path.Combine(root, "src", "A", "A.cs")], [], [], true);
                var node = new ProjectNode(csproj, "A", csproj, [], [], 0, null, null, false, null);
                return (new BuildPlan([node], [], "Debug"), Ev(ev));
            }

            // --- Worktree koşusu: plan worktree'de kurulur, kimlik ANA KÖKE taşınır, sonra imza hesaplanır.
            var (treePlan, treeEv) = Workspace(tree);
            var rebased = ProjectIdentityRebase.To(main, tree, treePlan,
                new Dictionary<string, IReadOnlyList<SolutionRef>>(), treeEv);
            var (_, worktreeSignatures) = IncrementalRunBinder.Bind(
                rebased.Plan, rebased.EvaluatedById, main, "HEAD1", tracked, [],
                new Dictionary<string, BuildState>(), inPlace: false, buildCycles: false, mode: DependentMode.Safe);

            string mainId = Path.Combine(main, "src", "A", "A.csproj");
            Assert.Equal(Path.Combine(tree, "src", "A", "A.csproj"), rebased.BuildPathById[mainId]); // MSBuild worktree'yi derler
            var persisted = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [mainId] = new BuildState(mainId, worktreeSignatures[mainId], LastResult: BuildResult.Succeeded),
            };

            // --- Sonraki in-place Sync: ana kök, temiz ağaç.
            var (mainPlan, mainEv) = Workspace(main);
            var (syncPlan, _) = IncrementalRunBinder.Bind(
                mainPlan, mainEv, main, "HEAD1", tracked, [], persisted,
                inPlace: true, buildCycles: false, mode: DependentMode.Safe);

            Assert.False(syncPlan.Nodes[0].WillBuild, "worktree'de derlenen proje ana kökte 'güncel' sayılmalı");
        }
        finally { TryDelete(main); TryDelete(tree); }
    }

    private static IReadOnlyDictionary<string, EvaluatedProject> Ev(EvaluatedProject p) =>
        new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase) { [p.Path] = p };

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* test temizliği */ }
    }
}
