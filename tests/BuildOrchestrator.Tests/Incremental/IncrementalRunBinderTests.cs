using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;
using static BuildOrchestrator.Tests.Incremental.EvidenceTimes;

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

    // ---- Kök bağımsızlığı (D5) --------------------------------------------------------------------

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
            InCycle: false, WillBuild: null, IsExternal: true);
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
    public void the_binder_exposes_the_input_paths_it_will_hash()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");

        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());

        Assert.Equal(2, binder.InputPaths.Count);
        Assert.Equal(2, binder.Prefill());     // ilk geçiş iki dosyayı okur
        Assert.Equal(0, binder.Prefill());     // ikinci geçişte hepsi önbellekte
    }

    [Fact]
    public void the_binder_exposes_the_folders_it_swept()
    {
        string root = NewRoot();
        var (plan, evaluated) = SingleProject(root);
        string projDir = Path.Combine(root, "src", "A");
        Write(projDir, "A.csproj", "<Project/>");
        Write(projDir, "A.cs", "class A {}");
        string id = plan.Nodes[0].Id;

        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());

        Assert.Contains(projDir, binder.FoldersOf(id));
        Assert.Empty(binder.FoldersOf("unknown"));
    }

    // ---- [Faz 3/Task 5 — spec 2026-09-18 §5] Çıktı kanıtı: gerçek legacy csproj'larla uçtan uca -----------

    /// <summary>
    /// İki gerçek legacy proje: üretici <c>Prod</c> ve HintPath'i paylaşılan klasördeki kopyayı
    /// (<c>lib\Prod.dll</c>, üreticinin çıktısıyla aynı ad) gösteren bağımlı <c>Dep</c>. Girdiler ve klasörler
    /// <see cref="EvidenceTimes.InputsAt"/>'ta, iki derleme kanıtı (<c>bin\Debug</c>) ve kopya
    /// <see cref="EvidenceTimes.EvidenceAt"/>'ta — damga ortak <see cref="EvidenceTimes.Stamp"/>'tan (D8).
    /// </summary>
    private (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> Evaluated, string Prod, string Dep)
        TwoLegacyProjects(string root)
    {
        const string Legacy = """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><AssemblyName>{0}</AssemblyName><OutputType>Library</OutputType></PropertyGroup>
              <ItemGroup>
                <Compile Include="{0}.cs" />
                {1}
              </ItemGroup>
            </Project>
            """;
        string prod = Write(Path.Combine(root, "Prod"), "Prod.csproj", string.Format(Legacy, "Prod", ""));
        string dep = Write(Path.Combine(root, "Dep"), "Dep.csproj", string.Format(Legacy, "Dep",
            """<Reference Include="Prod"><HintPath>..\lib\Prod.dll</HintPath></Reference>"""));
        Write(Path.Combine(root, "Prod"), "Prod.cs", "class Prod {}");
        Write(Path.Combine(root, "Dep"), "Dep.cs", "class Dep {}");
        EvidenceTimes.Stamp(root,
        [
            Write(Path.Combine(root, "Prod", "bin", "Debug"), "Prod.dll", "prod-binary"),
            Write(Path.Combine(root, "Dep", "bin", "Debug"), "Dep.dll", "dep-binary"),
            Write(Path.Combine(root, "lib"), "Prod.dll", "prod-binary"),
        ]);

        var evaluator = new CsprojEvaluator();
        var evaluated = new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase)
        {
            [prod] = evaluator.Evaluate(prod),
            [dep] = evaluator.Evaluate(dep),
        };
        var plan = new BuildPlan(
        [
            new ProjectNode(prod, "Prod", prod, [], [], 0, null, null, InCycle: false, WillBuild: null),
            new ProjectNode(dep, "Dep", dep, [], [prod], 1, null, null, InCycle: false, WillBuild: null),
        ], [], "Debug");
        return (plan, evaluated, prod, dep);
    }

    /// <summary><c>Dep</c> aracın kendi derlemesi: imzası güncel, <c>LastRunAt</c> kanıttan sonra ⇒ defter kipi.
    /// <c>Prod</c>'un kaydı yok ⇒ zaman kipi.</summary>
    private Dictionary<string, BuildState> DepBuiltByTheTool(
        BuildPlan plan, IReadOnlyDictionary<string, EvaluatedProject> evaluated, string root, string dep)
    {
        var (_, signatures) = new IncrementalRunBinder(plan, evaluated, root, FreshCache())
            .Bind(NoState, buildCycles: false, DependentMode.Safe);
        return new(StringComparer.OrdinalIgnoreCase)
        {
            [dep] = new BuildState(dep, signatures[dep], LastResult: BuildResult.Succeeded,
                LastRunAt: new DateTimeOffset(ToolRunAt)),
        };
    }

    /// <summary>§5.1/§5.2 uçtan uca: binder üreticinin kanıtını ve bağımlının HintPath'inden beslenen aday kopyayı
    /// bulur; kaydı olmayan ve kanıtı her girdiden yeni üretici zaman kipinde tazedir (<c>BuiltOutside</c>),
    /// <c>LastRunAt</c>'ı kanıttan sonra olan bağımlı defter kipindedir (<c>UpToDate</c>).</summary>
    [Fact]
    public void The_binder_locates_the_outputs_and_checks_both_modes_end_to_end()
    {
        string root = NewRoot();
        var (plan, evaluated, prod, dep) = TwoLegacyProjects(root);
        var state = DepBuiltByTheTool(plan, evaluated, root, dep);
        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());
        var evidenceAt = new DateTimeOffset(EvidenceAt);

        var prodOutputs = binder.OutputsById[prod];
        Assert.Equal(Path.Combine(root, "Prod", "bin", "Debug", "Prod.dll"), prodOutputs.Evidence);
        Assert.Equal(new[] { Path.Combine(root, "lib", "Prod.dll") }, prodOutputs.FedCandidates);
        Assert.Equal(Path.Combine(root, "Dep", "bin", "Debug", "Dep.dll"), binder.OutputsById[dep].Evidence);
        Assert.Empty(binder.OutputsById[dep].FedCandidates);

        var checks = binder.ChecksFor(state);

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, evidenceAt), checks[prod]);
        Assert.Equal(new OutputCheck(EvidenceMode.Ledger, false, true, null, evidenceAt), checks[dep]);

        var (bound, _) = binder.Bind(state, buildCycles: false, DependentMode.Safe, checks);
        Assert.Equal((false, WillBuildReason.BuiltOutside), (bound.Nodes[0].WillBuild, bound.Nodes[0].WillBuildReason));
        Assert.Equal((false, WillBuildReason.UpToDate), (bound.Nodes[1].WillBuild, bound.Nodes[1].WillBuildReason));
    }

    /// <summary>§5 maliyet (ruling R3 — sayaçlı stat sahtesi yerine gözlenebilir): iki projenin girdi dosyası
    /// kanıttan yeni bir zamana taşınır. Zaman kipindeki <c>Prod</c>'un hükmü değişir (<c>OwnNewer</c> ⇒
    /// <c>OutputStale</c>); defter kipindeki <c>Dep</c>'in kontrolü BİREBİR aynı kalır — girdi zamanı orada okunmaz,
    /// cevap defterden (içerik değişmedi, imza aynı ⇒ <c>UpToDate</c>).</summary>
    [Fact]
    public void Input_times_are_read_only_for_time_mode_projects()
    {
        string root = NewRoot();
        var (plan, evaluated, prod, dep) = TwoLegacyProjects(root);
        var state = DepBuiltByTheTool(plan, evaluated, root, dep);
        var before = new IncrementalRunBinder(plan, evaluated, root, FreshCache()).ChecksFor(state);

        File.SetLastWriteTimeUtc(Path.Combine(root, "Prod", "Prod.cs"), EditedAt);
        File.SetLastWriteTimeUtc(Path.Combine(root, "Dep", "Dep.cs"), EditedAt);
        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());
        var after = binder.ChecksFor(state);

        Assert.Equal(TimeVerdict.Fresh, before[prod].Time);
        Assert.Equal(TimeVerdict.OwnNewer, after[prod].Time);
        Assert.Equal(EvidenceMode.Ledger, before[dep].Mode);
        Assert.Equal(before[dep], after[dep]);

        var (bound, _) = binder.Bind(state, buildCycles: false, DependentMode.Safe, after);
        Assert.Equal(WillBuildReason.OutputStale, bound.Nodes[0].WillBuildReason);
        Assert.Equal(WillBuildReason.UpToDate, bound.Nodes[1].WillBuildReason);
    }
}
