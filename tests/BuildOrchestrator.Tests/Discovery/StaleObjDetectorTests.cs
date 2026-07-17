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

    // [T72] Fix pass: "libraries" bölümündeki çoklu-TFM nupkg dosya yolları (ör. lib/netstandard2.0/...)
    // yalnız "targets" anahtarları taranınca artık yanlış-pozitif üretmemeli.
    [Fact]
    public void clean_when_foreign_tfm_only_in_libraries_not_targets()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"), """
                {
                  "targets": { ".NETFramework,Version=v4.6": {} },
                  "libraries": {
                    "Newtonsoft.Json/13.0.1": {
                      "path": "newtonsoft.json/13.0.1",
                      "files": [ "lib/netstandard2.0/Newtonsoft.Json.dll" ]
                    }
                  }
                }
                """);
            var d = StaleObjDetector.Inspect(proj, "net46");
            Assert.False(d.IsStale);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [T72] "targets" anahtarı beklenen TFM'yi içerdiğinde temiz sayılmalı (expectedTfm=targets anahtarının kendisi).
    [Fact]
    public void clean_when_targets_key_contains_expected_tfm()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
                "{ \"targets\": { \".NETFramework,Version=v4.6\": {} } }");
            var d = StaleObjDetector.Inspect(proj, ".NETFramework,Version=v4.6");
            Assert.False(d.IsStale);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [T72 follow-up → It-2] warn-only detector ASLA fırlatmamalı: bozuk JSON degrade olup "temiz" dönmeli.
    [Fact]
    public void does_not_throw_and_reports_clean_on_malformed_json()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"), "{ bu gecerli json degil ///");

            var ex = Record.Exception(() => StaleObjDetector.Inspect(proj, ".NETFramework,Version=v4.6"));
            Assert.Null(ex);

            var d = StaleObjDetector.Inspect(proj, ".NETFramework,Version=v4.6");
            Assert.False(d.IsStale);
            Assert.Null(d.Reason);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [T72 follow-up → It-2] "targets" anahtarı hiç yoksa (ör. farklı biçimli/eksik assets dosyası)
    // teşhis edilemez sayılır — throw yok, false-positive warn yok.
    [Fact]
    public void does_not_throw_and_reports_clean_when_targets_key_missing()
    {
        string root = Path.Combine(Path.GetTempPath(), "stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P"); Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj"); File.WriteAllText(proj, "<Project/>");
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"), "{ \"libraries\": {} }");

            var ex = Record.Exception(() => StaleObjDetector.Inspect(proj, ".NETFramework,Version=v4.6"));
            Assert.Null(ex);

            var d = StaleObjDetector.Inspect(proj, ".NETFramework,Version=v4.6");
            Assert.False(d.IsStale);
            Assert.Null(d.Reason);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
