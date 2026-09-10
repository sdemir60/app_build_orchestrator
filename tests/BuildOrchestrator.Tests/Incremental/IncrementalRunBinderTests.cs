using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

// [Task 19 wiring][D1] IncrementalRunBinder — planlama kökleri ↔ IncrementalPlanner glue'su: girdi kümesi
// toplama, içerik özetleri ve imza haritası. Git YOK: karar tamamen diskteki gerçek dosyalardan verilir.
//
// DEĞİŞEN KURAL: bu dosyanın eski hâli git'i taklit ediyordu — testler bir `trackedBlobHashes` haritası ve
// `dirtyRepoRelativePaths` listesi enjekte ediyor, binder da imzayı o ikisinden kuruyordu. Kural D1 ile
// değişti (bkz. plan 2026-09-10-01-25): commit'lenmiş bir xaml/resx değişikliği ve git'e hiç girmemiş bir
// kaynak dosya o formülde GÖRÜNMÜYORDU. Testler artık gerçek dosyaları değiştirip kararı ölçüyor.
public sealed class IncrementalRunBinderTests : IDisposable
{
    private readonly List<string> _temp = [];

    public void Dispose()
    {
        foreach (string dir in _temp)
            try { Directory.Delete(dir, recursive: true); } catch { /* test temizliği */ }
    }

    private string NewRoot(string prefix = "bo-binder-")
    {
        string dir = Directory.CreateTempSubdirectory(prefix).FullName;
        _temp.Add(dir);
        return dir;
    }

    /// <summary>Her koşu KENDİ önbelleğiyle başlar: burada ölçülen şey karar, önbellek isabeti değil
    /// (o <see cref="SourceHashCacheTests"/>'in işi ve orada stat anahtarıyla ayrıca pinlenir).</summary>
    private SourceHashCache FreshCache() =>
        new(Path.Combine(NewRoot("bo-cache-"), SourceHashCache.FileName));

    private static string Write(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> Evaluated) SingleProject(
        string root, string name = "A")
    {
        string csproj = Path.Combine(root, "src", name, name + ".csproj");
        var evaluated = new EvaluatedProject(csproj, name, [], [], [], IsSdkStyle: true);
        var node = new ProjectNode(csproj, name, csproj, [], [], 0, null, null, InCycle: false, WillBuild: null);
        return (new BuildPlan([node], [], "Debug"),
            new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase) { [csproj] = evaluated });
    }

    private static Dictionary<string, BuildState> Built(string projectId, string signature) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [projectId] = new BuildState(projectId, signature, LastResult: BuildResult.Succeeded),
        };

    private static readonly Dictionary<string, BuildState> NoState = new(StringComparer.OrdinalIgnoreCase);

    // ---- Yol terimi (D5) ------------------------------------------------------------------------

    [Fact]
    public void a_path_under_the_root_becomes_a_root_relative_forward_slash_term()
    {
        string root = Path.Combine(Path.GetTempPath(), "repoX");
        Assert.Equal("src/A/A.csproj", IncrementalRunBinder.PathTerm(root, Path.Combine(root, "src", "A", "A.csproj")));
    }

    [Fact]
    public void a_path_outside_the_root_keeps_its_full_path_as_the_term()
    {
        // Harici köklerden gelen projeler ve kökün ÜSTÜNDEKİ bir Directory.Build.props: köke göreli bir
        // kimlikleri yoktur, bulundukları yer de koşudan koşuya değişmez.
        string root = Path.Combine(Path.GetTempPath(), "repoX");
        string outside = Path.Combine(Path.GetTempPath(), "external", "Mail", "Mail.csproj");
        Assert.Equal(outside.Replace('\\', '/'), IncrementalRunBinder.PathTerm(root, outside));
    }

    // ---- Temel karar ----------------------------------------------------------------------------

    [Fact]
    public void a_clean_project_with_a_matching_signature_and_a_succeeded_state_is_skipped()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        string id = plan.Nodes[0].Id;

        var (first, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);
        Assert.True(first.Nodes[0].WillBuild);                       // hiç derlenmemiş
        Assert.Equal(WillBuildReason.NeverBuilt, first.Nodes[0].WillBuildReason);
        Assert.True(signatures.ContainsKey(id));                      // imza persist edilebilir

        var (second, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.False(second.Nodes[0].WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, second.Nodes[0].WillBuildReason);
    }

    [Fact]
    public void editing_a_source_file_under_the_project_directory_flips_the_decision_to_build()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        string id = plan.Nodes[0].Id;

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Write(projDir, "A.cs", "class A { int changed; }");

        var (after, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.True(after.Nodes[0].WillBuild);
        Assert.Equal(WillBuildReason.SignatureChanged, after.Nodes[0].WillBuildReason);
    }

    [Fact]
    public void a_xaml_change_rebuilds_the_project()
    {
        // Eski git yolunun en somut açığı: imzaya giren dosya listesi csproj + Compile öğeleriydi, bir WPF
        // projesinin .xaml'ı orada YOKTU — başka bir makinede commit'lenip pull edilen bir ekran değişikliği
        // imzayı değiştirmiyor, proje "güncel" sayılıp atlanıyordu (under-build).
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        Write(projDir, "A.xaml", "<Window/>");
        string id = plan.Nodes[0].Id;

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Write(projDir, "A.xaml", "<Window Title=\"changed\"/>");

        var (after, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.True(after.Nodes[0].WillBuild);
    }

    [Fact]
    public void a_source_file_that_version_control_never_saw_rebuilds_the_project()
    {
        // İkinci açık: git'e eklenmemiş ya da gitignore'lanmış bir kaynak dosya ne blob tablosunda ne kirli
        // listede görünürdü — motorun kör noktasıydı. Disk onu görür.
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        string id = plan.Nodes[0].Id;

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Write(projDir, "Generated.cs", "class Generated {}");

        var (after, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.True(after.Nodes[0].WillBuild);
    }

    [Fact]
    public void a_file_that_cannot_affect_the_build_does_not_change_the_decision()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        string id = plan.Nodes[0].Id;

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Write(projDir, "README.md", "yeni bir doküman");

        var (after, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.False(after.Nodes[0].WillBuild);
    }

    /// <summary>
    /// AYIRT EDİCİ — bağımlılığı hatalı olduğu NOTLA kaydedilmiş bir proje, imzası GÜNCEL olsa bile yine
    /// "derlenecek" gelir. Not olmasaydı bu proje pre-skip edilir ve bayat bir binary'e link'li kalırdı.
    /// </summary>
    [Fact]
    public void a_project_recorded_against_a_failed_dependency_stays_dirty_even_with_a_matching_signature()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        Write(Path.Combine(root, "src", "A"), "A.csproj", "<Project/>");
        string id = plan.Nodes[0].Id;

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        var flagged = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            [id] = new BuildState(id, signatures[id], LastResult: BuildResult.Succeeded, DepIssue: true),
        };
        var (flaggedPlan, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(flagged, buildCycles: false, DependentMode.Safe);
        Assert.True(flaggedPlan.Nodes[0].WillBuild);

        // Kontrol grubu: notsuz aynı kayıt temizdir.
        var (cleanPlan, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(id, signatures[id]), buildCycles: false, DependentMode.Safe);
        Assert.False(cleanPlan.Nodes[0].WillBuild);
    }

    // ---- Kök bağımsızlığı ve worktree eşitliği (D5) ------------------------------------------------

    /// <summary>
    /// AYIRT EDİCİ — bir worktree'de derlenen proje, ana kökte yapılan bir sonraki Sync'te BULUNUR ve
    /// "güncel" sayılır.
    ///
    /// <para>Sahada latent duran kusur buydu: kimlik tam csproj yolu olduğu için worktree koşusu kayıtları
    /// worktree yollarıyla yazıyor, in-place Sync onları ana kök id'siyle arayıp bulamıyordu — yani farklı
    /// bir branch'e alınan TEK bir Build'den sonra her şey yeniden "derlenecek" görünüyordu.</para>
    ///
    /// <para>Çözümün iki yarısı vardır ve bu test ikisini birden sürer: kimlikler imza hesabından ÖNCE ana
    /// köke taşınır (<see cref="ProjectIdentityRebase"/>) ve imzanın yol terimi köke GÖRELİ tutulup içerik
    /// derlenen ağacın fiziksel dosyasından okunur (D5). İkincisi olmasaydı aynı içerik iki kökte iki farklı
    /// imza üretirdi.</para>
    /// </summary>
    [Fact]
    public void a_project_built_in_a_worktree_is_recognised_as_up_to_date_by_the_next_in_place_sync()
    {
        string main = NewRoot("bo-main-");
        string tree = NewRoot("bo-tree-");
        foreach (string root in new[] { main, tree })
        {
            Write(Path.Combine(root, "src", "A"), "A.csproj", "<Project/>");
            Write(Path.Combine(root, "src", "A"), "A.cs", "class A {}");
        }

        // --- Worktree koşusu: plan worktree'de kurulur, kimlik ANA KÖKE taşınır, sonra imza hesaplanır.
        var (treePlan, treeEvaluated) = SingleProject(tree);
        var rebased = ProjectIdentityRebase.To(main, tree, treePlan,
            new Dictionary<string, IReadOnlyList<SolutionRef>>(), treeEvaluated);
        string mainId = Path.Combine(main, "src", "A", "A.csproj");
        Assert.Equal(Path.Combine(tree, "src", "A", "A.csproj"), rebased.BuildPathById[mainId]);

        string ToWorktree(string logical) =>
            logical.StartsWith(main, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(tree, logical[(main.Length + 1)..])
                : logical;

        var (_, worktreeSignatures) = new IncrementalRunBinder(
                rebased.Plan, rebased.EvaluatedById, main, FreshCache(), ToWorktree)
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        // --- Sonraki in-place Sync: ana kök, aynı içerik.
        var (mainPlan, mainEvaluated) = SingleProject(main);
        var (syncPlan, inPlaceSignatures) = new IncrementalRunBinder(mainPlan, mainEvaluated, main, FreshCache())
            .Bind(Built(mainId, worktreeSignatures[mainId]), buildCycles: false, DependentMode.Safe);

        Assert.Equal(worktreeSignatures[mainId], inPlaceSignatures[mainId]);
        Assert.False(syncPlan.Nodes[0].WillBuild, "worktree'de derlenen proje ana kökte 'güncel' sayılmalı");
    }

    [Fact]
    public void the_same_content_in_a_different_root_yields_the_same_signature()
    {
        // Yol terimi köke göreli olduğu için repo'nun diskteki yeri (klonun adı, sürücü) imzayı ETKİLEMEZ.
        string first = NewRoot();
        string second = NewRoot();
        foreach (string root in new[] { first, second })
        {
            Write(Path.Combine(root, "src", "A"), "A.csproj", "<Project/>");
            Write(Path.Combine(root, "src", "A"), "A.cs", "class A {}");
        }

        var (planA, evaluatedA) = SingleProject(first);
        var (planB, evaluatedB) = SingleProject(second);

        var (_, sigA) = new IncrementalRunBinder(planA, evaluatedA, first, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);
        var (_, sigB) = new IncrementalRunBinder(planB, evaluatedB, second, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Assert.Equal(sigA[planA.Nodes[0].Id], sigB[planB.Nodes[0].Id]);
    }

    // ---- Harici kökler: AYNI yol, ayrı dal YOK ----------------------------------------------------

    [Fact]
    public void a_project_from_an_external_root_takes_the_very_same_path()
    {
        // Harici projeler ana reponun git ağacında değildir (TFVC'de git hiç yoktur). Eskiden bu yüzden
        // binder'da ayrı bir dal vardı; artık karar zaten diskten geldiği için ayrım kalmadı.
        string root = NewRoot("bo-main-");
        string external = NewRoot("bo-ext-");
        string externalProjDir = Path.Combine(external, "Mail");
        string externalCsproj = Write(externalProjDir, "Mail.csproj", "<Project/>");
        Write(externalProjDir, "Mail.cs", "class Mail {}");

        var node = new ProjectNode(externalCsproj, "Mail", externalCsproj, [], [], 0, -1, "External",
            InCycle: false, WillBuild: null, ExternalVcs: VcsKind.Git);
        var plan = new BuildPlan([node], [], "Debug");
        var evaluated = new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase)
        {
            [externalCsproj] = new EvaluatedProject(externalCsproj, "Mail", [], [], [], IsSdkStyle: true),
        };

        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);

        Write(externalProjDir, "Mail.cs", "class Mail { int changed; }");

        var (after, _) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(Built(externalCsproj, signatures[externalCsproj]), buildCycles: false, DependentMode.Safe);
        Assert.True(after.Nodes[0].WillBuild);
    }

    // ---- Girdi kümesi binder üstünden de görünür ---------------------------------------------------

    [Fact]
    public void the_binder_exposes_the_physical_paths_it_will_hash()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");

        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());

        Assert.Equal(2, binder.PhysicalPaths.Count);
        Assert.Equal(2, binder.Prefill());     // ilk geçiş iki dosyayı okur
        Assert.Equal(0, binder.Prefill());     // ikinci geçişte hepsi önbellekte
    }
}
