using System.IO;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [D2] Girdi kümesi: bir projenin "değişirse beni bayatlatır" dediği dosyaların TAMAMI. Kararın tek kaynağı
/// disk olduğu için bu küme yanlışsa karar da yanlıştır — eksik dosya under-build (sessizce atlanan proje),
/// fazla dosya over-build (boşuna derleme) demektir.
/// </summary>
public sealed class ProjectInputsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bo-inputs-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* test temizliği */ }
    }

    private string Write(string relativePath, string content)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private IReadOnlyList<string> LogicalPathsOf(string csproj, EvaluatedProject? evaluated = null) =>
        [.. ProjectInputs.Collect(csproj, evaluated, _root).Select(i => i.LogicalPath)];

    [Fact]
    public void the_project_file_and_every_build_affecting_file_under_its_folder_are_inputs()
    {
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        string cs = Write(@"src\A\Model.cs", "class Model {}");
        string xaml = Write(@"src\A\Views\Main.xaml", "<Window/>");
        string resx = Write(@"src\A\Properties\Strings.resx", "<root/>");

        var inputs = LogicalPathsOf(csproj);

        Assert.Contains(csproj, inputs);
        Assert.Contains(cs, inputs);
        Assert.Contains(xaml, inputs);   // xaml açığı: eski git yolunda yalnız Compile öğeleri sayılıyordu
        Assert.Contains(resx, inputs);
    }

    [Fact]
    public void files_that_cannot_affect_the_build_are_not_inputs()
    {
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        Write(@"src\A\README.md", "docs");
        Write(@"src\A\notes.txt", "notes");
        Write(@"src\A\Assets\logo.png", "binary-ish");

        var inputs = LogicalPathsOf(csproj);

        Assert.Single(inputs);
        Assert.Equal(csproj, inputs[0]);
    }

    [Fact]
    public void build_output_folders_are_not_inputs()
    {
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        Write(@"src\A\obj\Debug\A.AssemblyInfo.cs", "// generated");
        Write(@"src\A\bin\Debug\Something.cs", "// copied");
        string real = Write(@"src\A\Real.cs", "class Real {}");

        var inputs = LogicalPathsOf(csproj);

        Assert.Equal([csproj, real], [.. inputs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)]);
    }

    [Fact]
    public void a_file_linked_from_outside_the_project_folder_is_an_input()
    {
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        string sharedCs = Write(@"shared\Shared.cs", "class Shared {}");
        string sharedXaml = Write(@"shared\Shared.xaml", "<ResourceDictionary/>");
        var evaluated = new EvaluatedProject(csproj, "A", [sharedCs], [], [], IsSdkStyle: false)
        {
            ResourceFiles = [sharedXaml],
        };

        var inputs = LogicalPathsOf(csproj, evaluated);

        Assert.Contains(sharedCs, inputs);
        Assert.Contains(sharedXaml, inputs);
    }

    [Fact]
    public void directory_build_files_above_the_project_are_inputs_for_every_project_below()
    {
        string props = Write("Directory.Build.props", "<Project/>");
        string targets = Write(@"src\Directory.Build.targets", "<Project/>");
        string packages = Write("Directory.Packages.props", "<Project/>");
        string a = Write(@"src\A\A.csproj", "<Project/>");
        string b = Write(@"src\B\B.csproj", "<Project/>");

        foreach (string csproj in new[] { a, b })
        {
            var inputs = LogicalPathsOf(csproj);
            Assert.Contains(props, inputs);
            Assert.Contains(targets, inputs);
            Assert.Contains(packages, inputs);
        }
    }

    [Fact]
    public void the_nearest_directory_build_props_wins_and_the_search_stops_there()
    {
        // MSBuild kuralı: yukarı yürüyüş İLK bulunanda durur. Üsttekini de saymak, aslında hiç
        // içeri alınmayan bir dosyayı projeye bağlamak olurdu (over-build).
        string outer = Write("Directory.Build.props", "<Project/>");
        string inner = Write(@"src\Directory.Build.props", "<Project/>");
        string csproj = Write(@"src\A\A.csproj", "<Project/>");

        var inputs = LogicalPathsOf(csproj);

        Assert.Contains(inner, inputs);
        Assert.DoesNotContain(outer, inputs);
    }

    [Fact]
    public void the_search_does_not_climb_above_the_workspace_root()
    {
        // Kök DIŞINDA, kökün kardeşi bir Directory.Build.props kimseyi etkilememeli.
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, "Directory.Build.props");
        bool created = false;
        try
        {
            if (!File.Exists(outside)) { File.WriteAllText(outside, "<Project/>"); created = true; }
            string csproj = Write(@"src\A\A.csproj", "<Project/>");

            Assert.DoesNotContain(outside, LogicalPathsOf(csproj));
        }
        finally { if (created) File.Delete(outside); }
    }

    [Fact]
    public void the_order_is_deterministic_and_the_list_has_no_duplicates()
    {
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        string cs = Write(@"src\A\Model.cs", "class Model {}");
        // AYNI dosya hem Compile öğesi hem klasör taramasında görünür — tek kez girmeli.
        var evaluated = new EvaluatedProject(csproj, "A", [cs], [], [], IsSdkStyle: false);

        var first = LogicalPathsOf(csproj, evaluated);
        var second = LogicalPathsOf(csproj, evaluated);

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal([.. first.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)], first);
    }

    [Fact]
    public void a_transient_wpftmp_project_file_is_never_an_input()
    {
        // WPF derlemesi sırasında doğup ölen geçici csproj: kümeye girseydi imza koşudan koşuya oynardı.
        string csproj = Write(@"src\A\A.csproj", "<Project/>");
        Write(@"src\A\A_wpftmp.csproj", "<Project/>");

        Assert.DoesNotContain(LogicalPathsOf(csproj), p => p.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void the_physical_path_follows_the_mapper_while_the_identity_stays_on_the_main_root()
    {
        // [D5] Worktree koşusu: kimlik ana kökte kalır, okuma havuzdaki kopyadan yapılır.
        string tree = Directory.CreateTempSubdirectory("bo-tree-").FullName;
        try
        {
            Write(@"src\A\A.csproj", "<Project/>");
            Write(@"src\A\Model.cs", "class Model {}");
            Directory.CreateDirectory(Path.Combine(tree, "src", "A"));
            File.WriteAllText(Path.Combine(tree, "src", "A", "A.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(tree, "src", "A", "Model.cs"), "class Model { int worktree; }");

            string mainCsproj = Path.Combine(_root, "src", "A", "A.csproj");
            string ToPhysical(string logical) =>
                logical.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(tree, logical[(_root.Length + 1)..])
                    : logical;

            var inputs = ProjectInputs.Collect(mainCsproj, null, _root, ToPhysical);

            Assert.All(inputs, i => Assert.StartsWith(_root, i.LogicalPath, StringComparison.OrdinalIgnoreCase));
            Assert.All(inputs, i => Assert.StartsWith(tree, i.PhysicalPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(inputs, i => i.LogicalPath.EndsWith("Model.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally { try { Directory.Delete(tree, recursive: true); } catch { /* test temizliği */ } }
    }
}
