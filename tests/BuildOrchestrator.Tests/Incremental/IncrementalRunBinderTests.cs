using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
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
                [], new Dictionary<string, BuildState>(), inPlace: true, DependentMode.Safe);
            Assert.True(planA.Nodes[0].WillBuild);              // hiç derlenmemiş → dirty
            Assert.True(sigA.ContainsKey(csproj));              // imza persist edilebilir (non-null)

            // O imzayı Succeeded state olarak geri besle → aynı committed fingerprint + temiz working tree → skip.
            var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sigA[csproj], LastResult: BuildResult.Succeeded),
            };
            var (planB, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                [], state, inPlace: true, DependentMode.Safe);
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
                [], new Dictionary<string, BuildState>(), inPlace: true, DependentMode.Safe);
            var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
            {
                [csproj] = new BuildState(csproj, sigClean[csproj], LastResult: BuildResult.Succeeded),
            };

            // Şimdi A.cs working-tree'de dirty (git porcelain repo-relative "src/A/A.cs") → imza değişir → dirty.
            var (planDirty, _) = IncrementalRunBinder.Bind(plan, Ev(evaluated), root, "HEAD1", tracked,
                ["src/A/A.cs"], state, inPlace: true, DependentMode.Safe);
            Assert.True(planDirty.Nodes[0].WillBuild);
        }
        finally { TryDelete(root); }
    }

    private static IReadOnlyDictionary<string, EvaluatedProject> Ev(EvaluatedProject p) =>
        new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase) { [p.Path] = p };

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* test temizliği */ }
    }
}
