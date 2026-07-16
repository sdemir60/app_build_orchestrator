using System;
using System.IO;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

// [T72] SPIKE S2 — OSYS.Types.NewSales.Print vakası: in-place build öncesi yabancı-TFM obj artığı warn edilir, dokunulmaz.
public class StaleObjDetectorTests
{
    [Fact]
    public void flags_stale_when_foreign_tfm_in_assets()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
                "{ \"targets\": { \".NETStandard,Version=v2.0\": {} } }"); // yabancı
            var d = StaleObjDetector.Inspect(proj, "net46");
            Assert.True(d.IsStale);
            Assert.Contains("netstandard", d.Reason!, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void clean_when_no_obj()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(dir);
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            Assert.False(StaleObjDetector.Inspect(proj, "net46").IsStale);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
