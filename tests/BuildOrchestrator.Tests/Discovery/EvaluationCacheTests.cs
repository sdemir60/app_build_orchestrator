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

    [Fact]
    public void GetOrEvaluate_touch_only_does_not_reevaluate()
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
            // yalnız mtime değişiyor; içerik (dolayısıyla length + hash) aynı kalıyor
            File.SetLastWriteTimeUtc(proj, DateTime.UtcNow.AddDays(1));
            cache.GetOrEvaluate(proj, Fake);
            Assert.Equal(1, calls); // hash-fallback cache-hit → ikinci kez evaluate çağrılmamalı
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_tolerates_vanished_file_without_throwing_or_evaluating()
    {
        // Canlı build ↔ scan yarışı: scanner dosyayı bulduktan sonra GetOrEvaluate çağrılana kadar
        // dosya silinebilir (ör. WPF wpftmp geçici projesi). Deterministik simülasyon: sleep/poll
        // yok [D8] — dosyayı sil, sonra doğrudan çağır.
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string vanished = Path.Combine(root, "Ghost.csproj");
            File.WriteAllText(vanished, "<Project/>");
            string other = Path.Combine(root, "A.csproj");
            File.WriteAllText(other, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "X", [], [], [], false); }

            File.Delete(vanished); // önceden cache'e hiç girmemiş, şimdi de yok
            var result = cache.GetOrEvaluate(vanished, Fake);

            Assert.Null(result);   // throw YOK; cache'te girdi yoksa evaluate çağrılmadan atlanır
            Assert.Equal(0, calls); // evaluate hiç çağrılmadı

            // cache bozulmamış: sonraki mevcut dosya normal işlenir
            var otherResult = cache.GetOrEvaluate(other, Fake);
            Assert.NotNull(otherResult);
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_returns_stale_cached_entry_when_file_vanishes_after_caching()
    {
        // Dosya daha önce evaluate edilip cache'e girmişse ve SONRA silinmişse: mevcut girdi
        // aynen döner (yeniden evaluate YOK) — "kaybolan dosya = yeniden değerlendir" DEĞİL.
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "A", [], [], [], false); }

            var first = cache.GetOrEvaluate(proj, Fake);
            Assert.Equal(1, calls);

            File.Delete(proj);
            var second = cache.GetOrEvaluate(proj, Fake);

            Assert.Same(first, second);
            Assert.Equal(1, calls); // yeniden evaluate edilmedi
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_persists_to_disk_and_reloads_in_new_instance()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, "<Project/>");
            string cachePath = Path.Combine(root, "cache.json");

            int callsA = 0;
            EvaluatedProject FakeA(string p)
            {
                callsA++;
                return new EvaluatedProject(
                    p, "A",
                    ["Foo.cs", "Bar.cs"],
                    [new RawHintPath(@"..\packages\Lib.1.0\lib\net48\Lib.dll", "Lib.dll")],
                    [Path.Combine(root, "B.csproj")],
                    false);
            }

            var cacheA = new EvaluationCache(cachePath);
            var original = cacheA.GetOrEvaluate(proj, FakeA);
            cacheA.Flush();
            Assert.Equal(1, callsA);
            Assert.NotNull(original); // dosya var → null dönmemeli (nullable flow narrowing)

            // yeni instance, aynı cachePath → diskten yüklenmeli
            var cacheB = new EvaluationCache(cachePath);
            int callsB = 0;
            EvaluatedProject FakeB(string p) { callsB++; return original; }
            var loaded = cacheB.GetOrEvaluate(proj, FakeB);

            Assert.Equal(0, callsB); // disk'ten cache-hit → evaluate çağrılmamalı
            Assert.NotNull(loaded); // dosya var → null dönmemeli (nullable flow narrowing)
            Assert.Equal(original.Path, loaded.Path);
            Assert.Equal(original.AssemblyName, loaded.AssemblyName);
            Assert.NotNull(loaded.CompileFiles);
            Assert.Equal(["Foo.cs", "Bar.cs"], loaded.CompileFiles);
            Assert.NotNull(loaded.HintPaths);
            Assert.Single(loaded.HintPaths);
            Assert.Equal("Lib.dll", loaded.HintPaths[0].BaseName);
            Assert.Equal(@"..\packages\Lib.1.0\lib\net48\Lib.dll", loaded.HintPaths[0].Raw);
            Assert.NotNull(loaded.ProjectReferences);
            Assert.Equal([Path.Combine(root, "B.csproj")], loaded.ProjectReferences);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
