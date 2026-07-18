using System;
using System.IO;
using System.Linq;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Supervisor;

namespace BuildOrchestrator.Tests.Supervisor;

// [T72/Task 14] SPIKE S2 (OSYS.Types.NewSales.Print vakası) — in-place run başında bayat-obj tespiti
// StaleObjDetector.Inspect'e bağlanır: bayat obj → TEK warn satırı, dosyaya ASLA dokunulmaz.
public class StaleObjRunStartWarnerTests
{
    private static ProjectNode Node(string id, string name) =>
        new(id, name, id, SolutionNames: [], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    // OSYS.Types.NewSales.Print vakasının aynısı: v4.6 legacy csproj + obj altında yabancı (netstandard2.0)
    // restore artığı.
    private static string WriteStaleProject(string dir, string name)
    {
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        string proj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(proj, """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <AssemblyName>OSYS.Types.NewSales.Print</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
            "{ \"targets\": { \".NETStandard,Version=v2.0\": {} } }"); // yabancı — sibling'in netstandard restore'u sızmış
        return proj;
    }

    private static string WriteCleanProject(string dir, string name)
    {
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        string proj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(proj, """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <AssemblyName>OSYS.Clean</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
            "{ \"targets\": { \".NETFramework,Version=v4.6\": {} } }"); // temiz
        return proj;
    }

    // [Review fix/Task 14] Temiz SDK-style netstandard projesi: obj/project.assets.json "targets" anahtarı
    // NuGet'in gerçekte ürettiği KISA biçim ("netstandard2.0") — bu projenin KENDİ meşru restore'u, yabancı
    // artık DEĞİL.
    private static string WriteCleanSdkStyleNetstandardProject(string dir, string name)
    {
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        string proj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(proj,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
            "{ \"targets\": { \"netstandard2.0\": {} } }"); // temiz — SDK-style'ın kendi kısa-form restore'u
        return proj;
    }

    [Fact]
    public void warns_once_for_a_stale_project_and_never_touches_its_obj_files()
    {
        string root = Path.Combine(Path.GetTempPath(), "warner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P");
            string proj = WriteStaleProject(dir, "P");
            string assets = Path.Combine(dir, "obj", "project.assets.json");
            byte[] before = File.ReadAllBytes(assets);
            DateTime writeTimeBefore = File.GetLastWriteTimeUtc(assets);

            var lines = new System.Collections.Generic.List<string>();
            StaleObjRunStartWarner.WarnStaleObj([Node(proj, "P")], lines.Add);

            var warn = Assert.Single(lines);
            Assert.Contains("P", warn);
            Assert.Contains("netstandard", warn, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(assets));                              // dosya SİLİNMEDİ
            Assert.Equal(before, File.ReadAllBytes(assets));               // byte-tam AYNI
            Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(assets)); // dokunulmadı (mtime değişmedi)
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void does_not_warn_for_a_clean_project()
    {
        string root = Path.Combine(Path.GetTempPath(), "warner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P");
            string proj = WriteCleanProject(dir, "P");

            var lines = new System.Collections.Generic.List<string>();
            StaleObjRunStartWarner.WarnStaleObj([Node(proj, "P")], lines.Add);

            Assert.Empty(lines);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [Review fix/Task 14] Round-trip false-positive regression: a CLEAN SDK-style netstandard project (its OWN
    // legit restore, not a foreign artifact) whose obj/project.assets.json "targets" key is the SHORT form
    // "netstandard2.0" — the ONLY form NuGet ever produces for SDK-style projects (verified in this repo's own
    // obj outputs: net10.0-windows, net46 — never long-form). Full round-trip through
    // CsprojEvaluator → TargetFrameworkMonikerDeriver → StaleObjDetector.Inspect.
    //
    // Before the fix, TargetFrameworkMonikerDeriver special-cased SDK-style "netstandardX.Y" to the LONG form
    // ".NETStandard,Version=vX.Y". That expectedTfm never Contains-matches the real short-form key
    // "netstandard2.0", so Inspect fell through to its ForeignTfm regex — which matches the project's OWN key —
    // producing a SPURIOUS "yabancı TFM" warning on every fresh run of a perfectly clean project. RED under the
    // buggy long-form derivation (this test failed: Assert.Empty(lines) got 1 spurious warning); GREEN after the
    // fix (short-form pass-through matches the real key, no warning).
    [Fact]
    public void does_not_warn_for_a_clean_sdk_style_netstandard_project_with_short_form_targets_key()
    {
        string root = Path.Combine(Path.GetTempPath(), "warner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "N");
            string proj = WriteCleanSdkStyleNetstandardProject(dir, "N");

            var lines = new System.Collections.Generic.List<string>();
            StaleObjRunStartWarner.WarnStaleObj([Node(proj, "N")], lines.Add);

            Assert.Empty(lines);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void does_not_warn_when_the_csproj_does_not_exist_and_never_throws()
    {
        string missing = Path.Combine(Path.GetTempPath(), "warner-missing-" + Guid.NewGuid().ToString("N"), "X.csproj");
        var lines = new System.Collections.Generic.List<string>();

        var ex = Record.Exception(() => StaleObjRunStartWarner.WarnStaleObj([Node(missing, "X")], lines.Add));

        Assert.Null(ex);
        Assert.Empty(lines);
    }

    [Fact]
    public void mixed_set_warns_only_for_the_stale_project()
    {
        string root = Path.Combine(Path.GetTempPath(), "warner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string staleProj = WriteStaleProject(Path.Combine(root, "Stale"), "Stale");
            string cleanProj = WriteCleanProject(Path.Combine(root, "Clean"), "Clean");

            var lines = new System.Collections.Generic.List<string>();
            StaleObjRunStartWarner.WarnStaleObj([Node(staleProj, "Stale"), Node(cleanProj, "Clean")], lines.Add);

            var warn = Assert.Single(lines);
            Assert.Contains("Stale", warn);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
