using System;
using System.IO;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

public class EvaluationCacheTests
{
    [Fact]
    public void GetOrEvaluate_returns_cached_when_file_unchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "A", [], [], [], false); }
            cache.GetOrEvaluate(proj, Fake);
            cache.GetOrEvaluate(proj, Fake); // aynı mtime → cache-hit, çağırma
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_reevaluates_when_content_changes()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "A", [], [], [], false); }
            cache.GetOrEvaluate(proj, Fake);
            File.SetLastWriteTimeUtc(proj, DateTime.UtcNow.AddSeconds(5)); // mtime değişti
            File.WriteAllText(proj, "<Project><!-- changed --></Project>"); // içerik değişti
            cache.GetOrEvaluate(proj, Fake);
            Assert.Equal(2, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
