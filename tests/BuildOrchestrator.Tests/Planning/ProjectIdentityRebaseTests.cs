using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// Proje kimliği bu kod tabanında tam csproj YOLUDUR. Farklı bir branch'e build alındığında tarama havuzdaki
/// worktree'de yapılır ve aynı proje bambaşka bir kimlik kazanır — bu tip o kimliği ana repo köküne taşır.
///
/// <para>Taşınmazsa: bağımlılığı olan her projenin imzası ayrışır (upstream terimi id hash'ler), build-state
/// kayıtları worktree yoluyla yazılıp bir sonraki in-place Sync tarafından bulunamaz (her şey "derlenecek"
/// görünür) ve koşunun önizlemesi App'te KOPYA satırlar üretir.</para>
/// </summary>
public class ProjectIdentityRebaseTests
{
    private static readonly string Main = Path.Combine(Path.GetTempPath(), "bo-main");
    private static readonly string Tree = Path.Combine(Path.GetTempPath(), "bo-wt", "main-1");

    private static string InMain(params string[] parts) => Path.Combine([Main, .. parts]);
    private static string InTree(params string[] parts) => Path.Combine([Tree, .. parts]);

    private static ProjectNode Node(string root, string name, params string[] deps) =>
        new(Path.Combine(root, name, name + ".csproj"), name, Path.Combine(root, name, name + ".csproj"),
            SolutionNames: ["Osys"],
            Dependencies: [.. deps.Select(d => Path.Combine(root, d, d + ".csproj"))],
            BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    private static (BuildPlan Plan,
        IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> Refs,
        IReadOnlyDictionary<string, EvaluatedProject> Evaluated) Worktree()
    {
        var a = Node(Tree, "A");
        var b = Node(Tree, "B", "A");
        var plan = new BuildPlan([a, b], [[a.Id, b.Id]], "Debug");
        var refs = new Dictionary<string, IReadOnlyList<SolutionRef>>(StringComparer.OrdinalIgnoreCase)
        {
            [a.Id] = [new SolutionRef("Osys", InTree("Osys.sln"))],
        };
        var evaluated = new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase)
        {
            [a.Id] = new EvaluatedProject(a.Id, "A", [InTree("A", "A.cs")], [], [], IsSdkStyle: true),
        };
        return (plan, refs, evaluated);
    }

    [Fact]
    public void Every_identity_moves_to_the_main_root()
    {
        var (plan, refs, evaluated) = Worktree();

        var result = ProjectIdentityRebase.To(Main, Tree, plan, refs, evaluated);

        Assert.Equal(InMain("A", "A.csproj"), result.Plan.Nodes[0].Id);
        Assert.Equal(InMain("A", "A.csproj"), result.Plan.Nodes[0].ProjectPath);
        Assert.Equal([InMain("A", "A.csproj")], result.Plan.Nodes[1].Dependencies);
        Assert.Equal([InMain("A", "A.csproj"), InMain("B", "B.csproj")], result.Plan.Cycles[0]);
        Assert.Contains(InMain("A", "A.csproj"), result.Refs());
        Assert.Contains(InMain("A", "A.csproj"), result.EvaluatedById);
        Assert.Equal([InMain("A", "A.cs")], result.EvaluatedById[InMain("A", "A.csproj")].CompileFiles);
    }

    /// <summary>Solution DEĞERLERİ worktree'de kalır — resolver diskteki gerçek <c>.sln</c>'i görmelidir.</summary>
    [Fact]
    public void Solution_paths_keep_pointing_at_the_real_files_on_disk()
    {
        var (plan, refs, evaluated) = Worktree();

        var result = ProjectIdentityRebase.To(Main, Tree, plan, refs, evaluated);

        Assert.Equal(InTree("Osys.sln"), result.SolutionRefs[InMain("A", "A.csproj")].Single().Path);
    }

    /// <summary>MSBuild'in derleyeceği GERÇEK yol ayrı bir haritada durur — kimlik mantıksal, yol fiziksel.</summary>
    [Fact]
    public void The_build_path_map_points_each_identity_back_at_the_worktree()
    {
        var (plan, refs, evaluated) = Worktree();

        var result = ProjectIdentityRebase.To(Main, Tree, plan, refs, evaluated);

        Assert.Equal(InTree("A", "A.csproj"), result.BuildPathById[InMain("A", "A.csproj")]);
        Assert.Equal(InTree("B", "B.csproj"), result.BuildPathById[InMain("B", "B.csproj")]);
    }

    /// <summary>In-place koşuda hiçbir şey kopyalanmaz — girdi nesneleri AYNEN döner (sıcak yolda maliyet yok).</summary>
    [Fact]
    public void An_in_place_run_is_left_completely_untouched()
    {
        var (plan, refs, evaluated) = Worktree();

        var result = ProjectIdentityRebase.To(Tree, Tree, plan, refs, evaluated);

        Assert.Same(plan, result.Plan);
        Assert.Same(refs, result.SolutionRefs);
        Assert.Same(evaluated, result.EvaluatedById);
        Assert.Empty(result.BuildPathById);
    }

    /// <summary>Kök DIŞINDA kalan bir yol (savunmacı) aynen korunur — kimlik uydurulmaz.</summary>
    [Fact]
    public void A_path_outside_the_worktree_is_left_alone()
    {
        string stray = Path.Combine(Path.GetTempPath(), "bo-elsewhere", "S.csproj");
        var node = new ProjectNode(stray, "S", stray, [], [], 0, null, null, false, null);
        var plan = new BuildPlan([node], [], "Debug");

        var result = ProjectIdentityRebase.To(Main, Tree, plan,
            new Dictionary<string, IReadOnlyList<SolutionRef>>(), new Dictionary<string, EvaluatedProject>());

        Assert.Equal(stray, result.Plan.Nodes[0].Id);
    }
}

internal static class RebaseTestExtensions
{
    /// <summary>Anahtar kümesine kısa erişim (Assert.Contains sözlük üzerinde anahtar arar).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> Refs(
        this ProjectIdentityRebase.Result result) => result.SolutionRefs;
}
