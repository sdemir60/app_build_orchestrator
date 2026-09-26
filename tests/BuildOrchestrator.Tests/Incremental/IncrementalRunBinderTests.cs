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
    /// <param name="withDownstream">[Task 18] <c>true</c> iken Dep'in ARKASINA üçüncü bir legacy proje
    /// (<c>C</c>) eklenir — Dep'i Dep'in Prod'u kullandığı AYNI paylaşılan-lib HintPath kalıbıyla kullanır
    /// (<c>lib\Dep.dll</c>), kendi derleme kanıtı da (<c>C\bin\Debug\C.dll</c>) tazedir. Varsayılan
    /// <c>false</c> iki mevcut çağrı yerini DEĞİŞTİRMEZ (kopya YASAK — T15'in kalıbı: opsiyonel, geriye
    /// uyumlu parametre).</param>
    private (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> Evaluated, string Prod, string Dep)
        TwoLegacyProjects(string root, bool withDownstream = false)
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

        List<string> evidence =
        [
            Write(Path.Combine(root, "Prod", "bin", "Debug"), "Prod.dll", "prod-binary"),
            Write(Path.Combine(root, "Dep", "bin", "Debug"), "Dep.dll", "dep-binary"),
            Write(Path.Combine(root, "lib"), "Prod.dll", "prod-binary"),
        ];

        var evaluator = new CsprojEvaluator();
        var evaluated = new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase)
        {
            [prod] = evaluator.Evaluate(prod),
            [dep] = evaluator.Evaluate(dep),
        };
        var nodes = new List<ProjectNode>
        {
            new(prod, "Prod", prod, [], [], 0, null, null, InCycle: false, WillBuild: null),
            new(dep, "Dep", dep, [], [prod], 1, null, null, InCycle: false, WillBuild: null),
        };

        if (withDownstream)
        {
            // [Task 18] C, Dep'in ARKASINDA — dalga (IncrementalPlanner.BehindDirtyUpstream) yalnız graf
            // kenarını (Dependencies) okur, C'nin kendi HintPath hedefine BAKMADAN Dep bayatlayınca onu çeker.
            string c = Write(Path.Combine(root, "C"), "C.csproj", string.Format(Legacy, "C",
                """<Reference Include="Dep"><HintPath>..\lib\Dep.dll</HintPath></Reference>"""));
            Write(Path.Combine(root, "C"), "C.cs", "class C {}");
            evidence.Add(Write(Path.Combine(root, "C", "bin", "Debug"), "C.dll", "c-binary"));
            evidence.Add(Write(Path.Combine(root, "lib"), "Dep.dll", "dep-binary"));

            evaluated[c] = evaluator.Evaluate(c);
            nodes.Add(new ProjectNode(c, "C", c, [], [dep], 2, null, null, InCycle: false, WillBuild: null));
        }

        EvidenceTimes.Stamp(root, evidence);
        var plan = new BuildPlan([.. nodes], [], "Debug");
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

    // ---- [Task 18] HintPath DLL yenilendi: affected + dalga (rehber 22) ---------------------------------

    /// <summary>
    /// [Rehber 22] Prod ve Dep VS'de derlenmiş (defter kaydı YOK ⇒ ikisi de zaman kipinde). Sonra YALNIZ Prod
    /// yeniden derlenir: paylaşılan kopya <c>lib\Prod.dll</c>, Dep'in KENDİ kanıtından (<c>Dep.dll</c>) yeni
    /// damgalanır — Prod'un kendi girdisi ve kendi kanıtı dokunulmadan kalır (VS Prod'u derleyip paylaşılan
    /// klasöre kopyaladı, Dep'e hiç dokunmadı). Dep'in hükmü <see cref="TimeVerdict.DependencyNewer"/>,
    /// kararı <see cref="WillBuildReason.OutputStale"/> olur — kendi dosyaları değişmediği için etiket
    /// <c>modified</c> DEĞİL <c>affected</c>'tır (<see cref="OutputEvidence.OwnFilesChanged(OutputCheck?, bool?)"/>
    /// ⇒ <c>false</c>).
    /// </summary>
    [Fact]
    public void Rebuilding_only_the_producer_leaves_the_dependent_affected_not_modified()
    {
        string root = NewRoot();
        var (plan, evaluated, prod, dep) = TwoLegacyProjects(root);
        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());

        // VS Prod'u yeniden derledi: paylaşılan kopya Dep'in kanıtından (EvidenceAt) YENİ.
        File.SetLastWriteTimeUtc(Path.Combine(root, "lib", "Prod.dll"), EditedAt);

        var checks = binder.ChecksFor(NoState);
        Assert.Equal(TimeVerdict.Fresh, checks[prod].Time);              // Prod kendi kanıtına göre taze kaldı
        Assert.Equal(TimeVerdict.DependencyNewer, checks[dep].Time);
        Assert.False(OutputEvidence.OwnFilesChanged(checks[dep], ledgerAnswer: null));

        var (bound, _) = binder.Bind(NoState, buildCycles: false, DependentMode.Safe, checks);
        // Rehber 22: "A yeşil, B affected" — Prod BuiltOutside ile yeşil KALIR, Dep OutputStale'e döner.
        Assert.Equal((false, WillBuildReason.BuiltOutside), (bound.Nodes[0].WillBuild, bound.Nodes[0].WillBuildReason));
        Assert.Equal((true, WillBuildReason.OutputStale), (bound.Nodes[1].WillBuild, bound.Nodes[1].WillBuildReason));
    }

    /// <summary>
    /// [Rehber 22'nin atladığı gerçek] Aynı tetik (yalnız Prod yeniden derlenir), bu sefer Dep'in ARKASINDA
    /// üçüncü bir legacy proje <c>C</c> var (kendi paylaşılan kopyası <c>lib\Dep.dll</c>, kendi kanıtı taze,
    /// hiçbiri dokunulmadı). C'nin KENDİ kontrolü tek başına <see cref="TimeVerdict.Fresh"/>'tir. Ama Dep
    /// <see cref="WillBuildReason.OutputStale"/> ile "yeni çıktı üretecek" sayıldığı için dalga
    /// (<see cref="IncrementalPlanner"/>'ın <c>BehindDirtyUpstream</c>'i, satır ~254-280) C'nin kontrolünü de
    /// <c>DependencyNewer</c>'a çeker — C KENDİ BAŞINA taze göründüğü hâlde <c>WillBuild=true</c> olur.
    /// </summary>
    [Fact]
    public void The_wave_behind_a_stale_dependency_pulls_in_a_third_project_that_looks_fresh_on_its_own()
    {
        string root = NewRoot();
        var (plan, evaluated, _, _) = TwoLegacyProjects(root, withDownstream: true);
        string c = Path.Combine(root, "C", "C.csproj");
        var binder = new IncrementalRunBinder(plan, evaluated, root, FreshCache());

        // VS Prod'u yeniden derledi — C'ye hiç dokunulmadı.
        File.SetLastWriteTimeUtc(Path.Combine(root, "lib", "Prod.dll"), EditedAt);

        var checks = binder.ChecksFor(NoState);
        Assert.Equal(TimeVerdict.Fresh, checks[c].Time); // dalgadan ÖNCE: kendi başına taze

        var (bound, _) = binder.Bind(NoState, buildCycles: false, DependentMode.Safe, checks);
        // Tohum: Dep bu koşuda OutputStale ile "yeni çıktı üretecek" (ProducesNewOutput) sayılır.
        Assert.Equal((true, WillBuildReason.OutputStale), (bound.Nodes[1].WillBuild, bound.Nodes[1].WillBuildReason));
        // Dalga: C kendi kontrolünde Fresh'ken, Dep'in ARKASINDA olduğu için aynı karara (OutputStale) çekilir.
        Assert.Equal((true, WillBuildReason.OutputStale), (bound.Nodes[2].WillBuild, bound.Nodes[2].WillBuildReason));
    }

    // ---- [Task 4] Döngü grubu zaman kipi: kardeşin çıktısı DependencyNewer'a girmez --------------------

    /// <summary>
    /// Üç üyeli gerçek bir SCC: A → B (HintPath), B → C (HintPath), C → A (HintPath) — hiçbiri defterde kayıtlı
    /// değil (hepsi zaman kipinde). Ayrıca döngü DIŞI bir üretici D: yalnız A onun HintPath'ini taşır.
    /// Kaynaklar hepsinde <see cref="EvidenceTimes.InputsAt"/>'ta; her üyenin KENDİ dll'i çağıranın verdiği
    /// zamanda — kardeş dll'lerin birbirinden ileri geri olması testin konusu, çağıran ayarlar.
    /// </summary>
    private (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> Evaluated, string A, string B, string C, string D)
        ThreeMemberCycleWithOutsideUpstream(string root, DateTime aDllAt, DateTime bDllAt, DateTime cDllAt, DateTime dDllAt)
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
        static string Ref(string name) =>
            $"""<Reference Include="{name}"><HintPath>..\{name}\bin\Debug\{name}.dll</HintPath></Reference>""";

        string a = Write(Path.Combine(root, "A"), "A.csproj", string.Format(Legacy, "A", Ref("B") + Ref("D")));
        string b = Write(Path.Combine(root, "B"), "B.csproj", string.Format(Legacy, "B", Ref("C")));
        string c = Write(Path.Combine(root, "C"), "C.csproj", string.Format(Legacy, "C", Ref("A")));
        string d = Write(Path.Combine(root, "D"), "D.csproj", string.Format(Legacy, "D", ""));
        foreach (string name in new[] { "A", "B", "C", "D" })
            Write(Path.Combine(root, name), name + ".cs", "class " + name + " {}");

        EvidenceTimes.Stamp(root,
        [
            Write(Path.Combine(root, "A", "bin", "Debug"), "A.dll", "a-binary"),
            Write(Path.Combine(root, "B", "bin", "Debug"), "B.dll", "b-binary"),
            Write(Path.Combine(root, "C", "bin", "Debug"), "C.dll", "c-binary"),
            Write(Path.Combine(root, "D", "bin", "Debug"), "D.dll", "d-binary"),
        ]);
        // Stamp herkesi InputsAt/EvidenceAt'a çeker; üye dll'lerinin BİRBİRİNE göre sırasını burada elle veririz.
        File.SetLastWriteTimeUtc(Path.Combine(root, "A", "bin", "Debug", "A.dll"), aDllAt);
        File.SetLastWriteTimeUtc(Path.Combine(root, "B", "bin", "Debug", "B.dll"), bDllAt);
        File.SetLastWriteTimeUtc(Path.Combine(root, "C", "bin", "Debug", "C.dll"), cDllAt);
        File.SetLastWriteTimeUtc(Path.Combine(root, "D", "bin", "Debug", "D.dll"), dDllAt);

        var evaluator = new CsprojEvaluator();
        var evaluated = new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase)
        {
            [a] = evaluator.Evaluate(a),
            [b] = evaluator.Evaluate(b),
            [c] = evaluator.Evaluate(c),
            [d] = evaluator.Evaluate(d),
        };
        var plan = new BuildPlan(
        [
            new ProjectNode(a, "A", a, [], [b, d], 0, null, null, InCycle: true, WillBuild: null),
            new ProjectNode(b, "B", b, [], [c], 0, null, null, InCycle: true, WillBuild: null),
            new ProjectNode(c, "C", c, [], [a], 0, null, null, InCycle: true, WillBuild: null),
            new ProjectNode(d, "D", d, [], [], 0, null, null, InCycle: false, WillBuild: null),
        ], [[a, b, c]], "Debug");
        return (plan, evaluated, a, b, c, d);
    }

    /// <summary>
    /// [Kusur] Üyeler SIRAYLA dışarıda derlenmiş (A önce, sonra B, sonra C — her dll bir öncekinden yeni).
    /// A'nın HintPath hedefi B'nin dll'i (yeni), B'ninki C'nin dll'i (yeni) — döngü DIŞI D'nin dll'i hepsinden
    /// eski. Doğru davranış: kardeş çıktıları sayılmaz ⇒ üçü de taze/<c>BuiltOutside</c>. Düzeltmeden önce
    /// <c>ChecksFor</c> ham (filtresiz) HintPath hedeflerini kullandığı için A ve B <c>DependencyNewer</c>
    /// okunur, <c>ApplyCycleGroups</c> grubu hep bayat bırakır.
    /// </summary>
    [Fact]
    public void A_cycle_built_outside_in_sequence_is_fresh_when_siblings_are_excluded()
    {
        string root = NewRoot();
        var t0 = EvidenceTimes.InputsAt;
        var (plan, evaluated, a, b, c, _) = ThreeMemberCycleWithOutsideUpstream(
            root, aDllAt: t0.AddMinutes(10), bDllAt: t0.AddMinutes(11), cDllAt: t0.AddMinutes(12), dDllAt: t0.AddMinutes(9));

        var checks = new IncrementalRunBinder(plan, evaluated, root, FreshCache()).ChecksFor(NoState);

        Assert.Equal(TimeVerdict.Fresh, checks[a].Time);
        Assert.Equal(TimeVerdict.Fresh, checks[b].Time);
        Assert.Equal(TimeVerdict.Fresh, checks[c].Time);
    }

    /// <summary>Aynı gruptaki bir üyenin (B) kendi kaynağı kendi dll'inden yeni ⇒ grubun TAMAMI bayat: A ve C
    /// kendi kontrollerini geçer ama grup tazeliği topluca reddedildiği için <c>DependencyNewer</c>'a çekilir,
    /// B kendi hükmünü (<c>OwnNewer</c>) korur. Bu kural KORUNUR — kardeş filtresi bunu bozmaz.</summary>
    [Fact]
    public void One_member_with_a_newer_source_keeps_the_whole_cycle_stale()
    {
        string root = NewRoot();
        var t0 = EvidenceTimes.InputsAt;
        var (plan, evaluated, a, b, c, _) = ThreeMemberCycleWithOutsideUpstream(
            root, aDllAt: t0.AddMinutes(10), bDllAt: t0.AddMinutes(11), cDllAt: t0.AddMinutes(12), dDllAt: t0.AddMinutes(9));
        File.SetLastWriteTimeUtc(Path.Combine(root, "B", "B.cs"), t0.AddMinutes(20)); // B.cs > B.dll

        var checks = new IncrementalRunBinder(plan, evaluated, root, FreshCache()).ChecksFor(NoState);

        Assert.Equal(TimeVerdict.DependencyNewer, checks[a].Time);
        Assert.Equal(TimeVerdict.OwnNewer, checks[b].Time);
        Assert.Equal(TimeVerdict.DependencyNewer, checks[c].Time);
    }

    /// <summary>Döngü DIŞI D'nin dll'i A'nın (bir üyenin) dll'inden yeni ⇒ grup bayat — döngü dışı upstream'ler
    /// SAYILMAYA devam eder, kardeş filtresi yalnız AYNI SCC'deki üreticileri eler.</summary>
    [Fact]
    public void An_outside_cycle_upstream_still_stales_the_whole_group()
    {
        string root = NewRoot();
        var t0 = EvidenceTimes.InputsAt;
        var (plan, evaluated, a, b, c, _) = ThreeMemberCycleWithOutsideUpstream(
            root, aDllAt: t0.AddMinutes(10), bDllAt: t0.AddMinutes(11), cDllAt: t0.AddMinutes(12), dDllAt: t0.AddMinutes(15));

        var checks = new IncrementalRunBinder(plan, evaluated, root, FreshCache()).ChecksFor(NoState);

        Assert.Equal(TimeVerdict.DependencyNewer, checks[a].Time);
        Assert.Equal(TimeVerdict.DependencyNewer, checks[b].Time);
        Assert.Equal(TimeVerdict.DependencyNewer, checks[c].Time);
    }
}
