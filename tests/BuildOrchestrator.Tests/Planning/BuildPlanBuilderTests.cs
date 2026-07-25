using System;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

// [T26] BuildPlanBuilder integration test: tam pipeline (scan -> evaluate -> graph -> solution -> topo -> BuildPlan).
public class BuildPlanBuilderTests
{
    [Fact]
    public void builds_plan_in_build_order_with_edges_and_solutions()
    {
        string root = Path.Combine(Path.GetTempPath(), "plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            // B (yaprak) ← A (HintPath ile B'ye bağımlı). Build-order: B, A.
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));
            File.WriteAllText(Path.Combine(root, "B", "B.csproj"),
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><AssemblyName>OSYS.B</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "A", "A.csproj"),
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><AssemblyName>OSYS.A</AssemblyName></PropertyGroup>" +
                "<ItemGroup><Reference Include=\"OSYS.B\"><HintPath>..\\B\\bin\\OSYS.B.dll</HintPath></Reference></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Root.sln"),
                "Project(\"{X}\") = \"A\", \"A\\A.csproj\", \"{1}\"\nProject(\"{X}\") = \"B\", \"B\\B.csproj\", \"{2}\"\n");

            var builder = new BuildPlanBuilder(new WorkspaceScanner(), new CsprojEvaluator(),
                new EvaluationCache(Path.Combine(root, "cache.json")));
            var plan = builder.Build(root, "Debug");

            Assert.Equal("Debug", plan.Configuration);
            Assert.Equal(2, plan.Nodes.Count);
            Assert.Equal("OSYS.B", plan.Nodes[0].Name);   // build-order: bağımlılık önce
            Assert.Equal("OSYS.A", plan.Nodes[1].Name);
            var a = plan.Nodes[1];
            Assert.Single(a.Dependencies);
            Assert.EndsWith("B.csproj", a.Dependencies[0]);
            Assert.Equal(["Root"], a.SolutionNames);
            Assert.Empty(plan.Cycles);
            Assert.All(plan.Nodes, n => Assert.Null(n.WillBuild)); // Task 15 dolduracak
            Assert.All(plan.Nodes, n => Assert.Null(n.LayerIndex));
            Assert.All(plan.Nodes, n => Assert.Null(n.LayerName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// B (yaprak) ← A (B'ye bağımlı). B "Ui" pattern'ine, A "Data" pattern'ine uyar — normalde (patternsiz)
    /// build-order zaten B, A olurdu; A'yı Data(layer0), B'yi Ui(layer1) yapmak bariyerin B'yi (dependency,
    /// layer1) A'dan (dependent, layer0) SONRAYA itmesine yol açar. Yani bu kurulum AYNI ZAMANDA bir TERS
    /// KATMAN BAĞIMLILIĞIDIR (A, kendisinden sonraki bir katmandaki B'ye bağımlı).
    /// </summary>
    private static BuildPlan BuildPlanWithLayers()
    {
        string root = Path.Combine(Path.GetTempPath(), "plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "B"));
            File.WriteAllText(Path.Combine(root, "B", "B.csproj"),
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><AssemblyName>OSYS.Ui</AssemblyName></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "A", "A.csproj"),
                "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup><AssemblyName>OSYS.Data</AssemblyName></PropertyGroup>" +
                "<ItemGroup><Reference Include=\"OSYS.Ui\"><HintPath>..\\B\\bin\\OSYS.Ui.dll</HintPath></Reference></ItemGroup></Project>");

            var builder = new BuildPlanBuilder(new WorkspaceScanner(), new CsprojEvaluator(),
                new EvaluationCache(Path.Combine(root, "cache.json")));
            LayerPattern[] patterns =
            [
                new(Order: 0, Regex: "Data", Name: "DataLayer"),
                new(Order: 1, Regex: "Ui", Name: "UiLayer"),
            ];
            return builder.Build(root, "Debug", patterns);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [T15] Wire-in: pattern verilirse katman ataması uygulanır (LayerIndex/LayerName dolar, sert bariyer
    // sırayı etkiler); pattern verilmezse (varsayılan) davranış birebir eskisiyle aynıdır (yukarıdaki test).
    [Fact]
    public void with_layer_patterns_assigns_layers_and_applies_hard_phase_barrier()
    {
        var plan = BuildPlanWithLayers();

        Assert.Equal(2, plan.Nodes.Count);
        Assert.Equal("OSYS.Data", plan.Nodes[0].Name);
        Assert.Equal(0, plan.Nodes[0].LayerIndex);
        Assert.Equal("DataLayer", plan.Nodes[0].LayerName);
        Assert.Equal(0, plan.Nodes[0].BuildOrder);
        Assert.Equal("OSYS.Ui", plan.Nodes[1].Name);
        Assert.Equal(1, plan.Nodes[1].LayerIndex);
        Assert.Equal("UiLayer", plan.Nodes[1].LayerName);
        Assert.Equal(1, plan.Nodes[1].BuildOrder);
    }

    // [A1/T15] Ters-katman uyarısı artık BuildPlanBuilder'da YUTULMUYOR: kullanıcının pattern'lerini gözden
    // geçirmesi bu tasarımın (warn-only, LayerEngine düzeltme yapmaz) TEK gerçek düzeltmesidir — uyarı ona
    // ulaşmazsa sessizce yanlış sıralanmış bir plan derlenir.
    [Fact]
    public void Reverse_layer_dependency_warning_reaches_the_build_plan_instead_of_being_swallowed()
    {
        var plan = BuildPlanWithLayers();

        Assert.NotNull(plan.LayerWarnings);
        Assert.Contains(plan.LayerWarnings!, w => w.StartsWith("reverse layer dependency:", StringComparison.Ordinal));
    }
}
