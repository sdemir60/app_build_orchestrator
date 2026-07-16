using System;
using System.IO;
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
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
