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
    public void GetOrEvaluate_tolerates_evaluate_throwing_after_existence_check_passes()
    {
        // İkinci (daha dar) pencere [Important review bulgusu]: dosya info.Exists kontrolünü GEÇER
        // (var, mtime/length okunur) ama evaluate() (ör. XDocument.Load) sırasında canlı build onu
        // sildiği için FileNotFoundException fırlatır. Deterministik simülasyon: gerçek, var olan bir
        // dosya + fırlatan bir evaluate func (sleep/poll YOK, D8).
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "Vanishing.csproj");
            File.WriteAllText(proj, "<Project/>"); // gerçekten var → info.Exists/mtime/length geçer
            string other = Path.Combine(root, "A.csproj");
            File.WriteAllText(other, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));

            EvaluatedProject ThrowingEvaluate(string p) =>
                throw new FileNotFoundException("simulated vanish during XDocument.Load", p);

            var result = cache.GetOrEvaluate(proj, ThrowingEvaluate);
            Assert.Null(result); // throw dışarı sızmadı; cache'te önceden girdi yoktu → null

            // cache bozulmamış: sonraki farklı, var olan dosya normal işlenir
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "A", [], [], [], false); }
            var otherResult = cache.GetOrEvaluate(other, Fake);
            Assert.NotNull(otherResult);
            Assert.Equal(1, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_returns_stale_entry_when_evaluate_throws_during_rehash()
    {
        // Dosya daha önce cache'e girmiş; SONRA mtime+içerik gerçekten değişiyor (hash farklı →
        // re-evaluate tetiklenir) ama bu kez evaluate() ikinci pencerede FileNotFoundException
        // fırlatıyor (canlı build dosyayı evaluate() çağrısı sırasında sildi). Eski (stale) cache
        // girdisi aynen dönmeli — throw sızmamalı, cache güncellenmemeli.
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

            File.SetLastWriteTimeUtc(proj, DateTime.UtcNow.AddSeconds(5)); // mtime değişti
            File.WriteAllText(proj, "<Project><!-- changed --></Project>"); // içerik (hash) değişti → re-evaluate tetiklenir

            EvaluatedProject ThrowingEvaluate(string p) =>
                throw new FileNotFoundException("simulated vanish during XDocument.Load", p);
            var second = cache.GetOrEvaluate(proj, ThrowingEvaluate);

            Assert.Same(first, second); // eski (stale) girdi aynen döndü, throw sızmadı
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_propagates_io_failure_of_an_existing_locked_csproj_instead_of_dropping_it()
    {
        // [Final review I-3] Tolerans YALNIZ "dosya kayboldu" yarışı içindir. VAR OLAN ama okunamayan bir
        // csproj (ör. editör/başka bir process tarafından FileShare.None ile kilitli, ağ yolu hıçkırığı, disk
        // hatası) SESSİZCE null dönmemeli: null dönerse proje build plan'ından düşer ve build EKSİK graph ile
        // koşar. Gerçek bir kilit kullanılır (simülasyon değil).
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "Locked.csproj");
            File.WriteAllText(proj, "<Project/>");
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            EvaluatedProject Fake(string p) => new(p, "A", [], [], [], false);

            using var exclusive = new FileStream(proj, FileMode.Open, FileAccess.Read, FileShare.None);

            // Hash(csprojPath) → File.ReadAllBytes → paylaşım ihlali (IOException). Bu YUTULMAMALI.
            Assert.Throws<IOException>(() => cache.GetOrEvaluate(proj, Fake));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetOrEvaluate_propagates_XmlException_from_a_malformed_csproj()
    {
        // [Final review I-3] Bozuk/malformed bir csproj'un XmlException'ı IO toleransına takılmaz — aynen
        // yukarı sızar (kalıcı bir hata sessizce "proje yok" sayılmaz). Gerçek CsprojEvaluator kullanılır.
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "Broken.csproj");
            File.WriteAllText(proj, "<Project><ItemGroup></Project>"); // kapanmayan etiket
            var cache = new EvaluationCache(Path.Combine(root, "cache.json"));
            var evaluator = new CsprojEvaluator();

            Assert.Throws<System.Xml.XmlException>(() => cache.GetOrEvaluate(proj, evaluator.Evaluate));
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
