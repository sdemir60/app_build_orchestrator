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

    // ---------------------------------------------------------------- [Fix wave 1 — Finding 3] eşzamanlı Flush

    /// <summary>Bir cache dosyası + tek bir projelik girdi hazırlar (Flush'ın yazacak bir şeyi olsun).</summary>
    private static EvaluationCache SeededCache(string root, string cachePath)
    {
        string proj = Path.Combine(root, "A.csproj");
        if (!File.Exists(proj)) File.WriteAllText(proj, "<Project/>");
        var cache = new EvaluationCache(cachePath);
        cache.GetOrEvaluate(proj, p => new EvaluatedProject(p, "A", [], [], [], false));
        return cache;
    }

    // [Fix wave 1 — Finding 3] Flush eskiden SABİT bir `.tmp` adı kullanıyor ve HİÇBİR ŞEY yakalamıyordu:
    // hedefe erişilemediğinde (eşzamanlı bir Sync + run aynı yola flush ediyorken oluşan paylaşım ihlali)
    // istisna BuildPlanBuilder.Build üzerinden IPC sınırına kadar çıkıp TÜM Sync'i planFailed'a çeviriyordu.
    // Burada hedef GERÇEKTEN kilitlenir (FileShare.None) — rename deterministik olarak başarısız olur.
    [Fact]
    public void Flush_does_not_throw_when_the_destination_cannot_be_replaced()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cachePath = Path.Combine(root, "cache.json");
            File.WriteAllText(cachePath, "{}");
            var cache = SeededCache(root, cachePath);

            // Başka bir process'in cache dosyasını tuttuğu an: rename (File.Move overwrite) sharing-violation alır.
            using (new FileStream(cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                cache.Flush(); // FIRLATMAMALI — kaybolan tek şey bir cache girdisidir, Sync değil

            // Öksüz .tmp bırakmaz (aksi halde her başarısız flush diskte çöp biriktirirdi)
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // İki EvaluationCache örneği (bir run'ın planner'ı + eşzamanlı dispatch edilen bir Sync) AYNI yola
    // flush eder. Sabit `.tmp` adıyla ikisi aynı geçici dosyayı yazıp/rename etmeye çalışır ve biri patlardı.
    [Fact]
    public async Task Concurrent_flushes_on_the_same_cache_path_never_throw()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cachePath = Path.Combine(root, "cache.json");
            var a = SeededCache(root, cachePath);
            var b = SeededCache(root, cachePath);

            // Sleep YOK [D8]: iki task aynı bariyerden çıkıp 200 kez yarışır — sabit .tmp adında çakışma kaçınılmaz.
            using var barrier = new Barrier(2);
            Task Hammer(EvaluationCache cache) => Task.Run(() =>
            {
                barrier.SignalAndWait();
                // [C1] Eski iddia: her Flush yazar, 200 çağrı 200 yazımı yarıştırır. Yeni kural: yalnız KİRLİ Flush yazar —
                // bu yüzden her turda önce yeni bir proje değerlendirilip defter kirletilir; yazım yolu (temp + rename)
                // yine her turda iki örnek arasında yarışır (eşzamanlılık güvencesi gevşemedi, yük aynı kaldı).
                for (int i = 0; i < 200; i++)
                {
                    string proj = Path.Combine(root, Guid.NewGuid().ToString("N") + ".csproj");
                    File.WriteAllText(proj, "<Project/>");
                    cache.GetOrEvaluate(proj, p => new EvaluatedProject(p, "A", [], [], [], false));
                    cache.Flush();
                }
            });

            await Task.WhenAll(Hammer(a), Hammer(b)); // tek bir istisna bile testi düşürür

            // Cache dosyası kullanılabilir durumda (yarım/bozuk JSON yok) ve çöp .tmp kalmadı
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            Assert.NotNull(new EvaluationCache(cachePath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Düşen bir flush'ın GERÇEKTEN zararsız olduğunun kanıtı (Finding 3'ün "yutmak güvenli" gerekçesi):
    // yükleyici hem YOK olan hem BOZUK bir cache dosyasını tolere eder — yalnız yeniden değerlendirilir.
    [Fact]
    public void The_loader_tolerates_a_missing_or_corrupt_cache_so_a_dropped_flush_is_harmless()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, "<Project/>");
            string cachePath = Path.Combine(root, "cache.json");
            int calls = 0;
            EvaluatedProject Fake(string p) { calls++; return new EvaluatedProject(p, "A", [], [], [], false); }

            // (a) hiç yazılmamış cache (flush düştü) → sıfırdan kurulur
            Assert.NotNull(new EvaluationCache(cachePath).GetOrEvaluate(proj, Fake));
            Assert.Equal(1, calls);

            // (b) yarım/bozuk cache (yarışta ezilmiş dosya) → fırlatmaz, yeniden değerlendirir
            File.WriteAllText(cachePath, "{ bu gecerli JSON degil");
            Assert.NotNull(new EvaluationCache(cachePath).GetOrEvaluate(proj, Fake));
            Assert.Equal(2, calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---------------------------------------------------------------- [PERF Faz C/C1] kirli bayrağı: defter yalnız değiştiyse yazılır

    // Warm bir Sync ya da koşu defterdeki hiçbir girdiyi değiştirmez; Flush eskiden yine de koşulsuz yazıyordu
    // (gerçek OSYS'te birkaç MB'lık JSON, her pencereye dönüşte). "Yazıldı mı"yı LedgerFileProbe ölçer: defterin
    // mtime'ı eski bir damgaya çekilir, yeniden yazım (temp + File.Move) damgayı bugüne getirir — saat ve uyku yok.

    /// <summary>[C1] Kirli bayrağı testlerinin ortak iskeleti: geçici kök + defter yolu; iş bitince kök silinir.</summary>
    private static void WithLedger(Action<string, string> test)
    {
        string root = Directory.CreateTempSubdirectory("evcache-").FullName;
        try { test(root, Path.Combine(root, "cache.json")); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DirtyFlag_flush_after_load_without_any_change_leaves_the_ledger_untouched()
    {
        WithLedger((root, cachePath) =>
        {
            SeededCache(root, cachePath).Flush();          // defter bir girdiyle diske yazıldı
            var ledger = LedgerFileProbe.Pin(cachePath);

            new EvaluationCache(cachePath).Flush();        // yükle → hiçbir girdi değişmedi → yazma YOK

            Assert.False(ledger.WasRewritten);
        });
    }

    [Fact]
    public void DirtyFlag_a_cache_hit_does_not_dirty_the_ledger()
    {
        WithLedger((root, cachePath) =>
        {
            SeededCache(root, cachePath).Flush();
            var ledger = LedgerFileProbe.Pin(cachePath);
            var cache = new EvaluationCache(cachePath);

            // Warm yol: mtime+size eşit → hızlı yol; evaluate ÇAĞRILMAZ ve defter kirlenmez.
            var hit = cache.GetOrEvaluate(Path.Combine(root, "A.csproj"),
                _ => throw new InvalidOperationException("a hit must not evaluate"));
            cache.Flush();

            Assert.NotNull(hit);
            Assert.False(ledger.WasRewritten);
        });
    }

    [Fact]
    public void DirtyFlag_a_changed_project_is_written_by_the_next_flush_and_only_by_it()
    {
        WithLedger((root, cachePath) =>
        {
            SeededCache(root, cachePath).Flush();
            string proj = Path.Combine(root, "A.csproj");
            var cache = new EvaluationCache(cachePath);

            File.WriteAllText(proj, "<Project><!-- changed --></Project>");   // boyut + içerik değişti → yeniden değerlendirilir
            cache.GetOrEvaluate(proj, p => new EvaluatedProject(p, "Changed", [], [], [], false));
            var ledger = LedgerFileProbe.Pin(cachePath);
            cache.Flush();
            Assert.True(ledger.WasRewritten);                                 // kirli → yazar

            ledger = LedgerFileProbe.Pin(cachePath);
            cache.Flush();
            Assert.False(ledger.WasRewritten);                                // temiz → ikinci Flush yazmaz

            var reloaded = new EvaluationCache(cachePath).GetOrEvaluate(proj,
                _ => throw new InvalidOperationException("the written entry must be served from disk"));
            Assert.Equal("Changed", reloaded!.AssemblyName);                  // yazılan şey değişen girdidir
        });
    }

    [Fact]
    public void DirtyFlag_a_touched_file_refreshes_its_entry_and_that_refresh_is_written_once()
    {
        WithLedger((root, cachePath) =>
        {
            SeededCache(root, cachePath).Flush();
            string proj = Path.Combine(root, "A.csproj");
            File.SetLastWriteTimeUtc(proj, DateTime.UtcNow.AddDays(1));       // yalnız mtime; içerik (hash) aynı
            var cache = new EvaluationCache(cachePath);

            var served = cache.GetOrEvaluate(proj, _ => throw new InvalidOperationException("a touch must not evaluate"));
            var ledger = LedgerFileProbe.Pin(cachePath);
            cache.Flush();
            Assert.NotNull(served);
            Assert.True(ledger.WasRewritten);       // girdinin mtime'ı yenilendi; kalıcı olmazsa her yüklemede hash yeniden hesaplanır

            ledger = LedgerFileProbe.Pin(cachePath);
            cache.Flush();
            Assert.False(ledger.WasRewritten);
        });
    }

    [Fact]
    public void DirtyFlag_pruning_a_dead_entry_writes_the_ledger_and_pruning_nothing_does_not()
    {
        WithLedger((root, cachePath) =>
        {
            var cache = SeededCache(root, cachePath);                         // A.csproj
            string ghost = Path.Combine(root, "Ghost.csproj");
            File.WriteAllText(ghost, "<Project/>");
            cache.GetOrEvaluate(ghost, p => new EvaluatedProject(p, "Ghost", [], [], [], false));
            cache.Flush();
            File.Delete(ghost);
            var ledger = LedgerFileProbe.Pin(cachePath);

            Assert.Equal(1, cache.PruneMissingUnderRoot(root));
            Assert.True(ledger.WasRewritten);                                 // budama defteri yazar

            ledger = LedgerFileProbe.Pin(cachePath);
            Assert.Equal(0, cache.PruneMissingUnderRoot(root));
            Assert.False(ledger.WasRewritten);                                // budanacak bir şey yoksa dokunulmaz
        });
    }

    [Fact]
    public void DirtyFlag_a_failed_write_leaves_the_ledger_dirty_so_the_next_flush_retries()
    {
        WithLedger((root, cachePath) =>
        {
            File.WriteAllText(cachePath, "{}");
            var cache = SeededCache(root, cachePath);                         // bir girdi → kirli
            var ledger = LedgerFileProbe.Pin(cachePath);

            using (new FileStream(cachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                cache.Flush();                                                // hedef kilitli → yazım düşer (yutulur)
            Assert.False(ledger.WasRewritten);

            cache.Flush();                                                    // kilit kalktı → bayrak kirli kaldıysa yazar
            Assert.True(ledger.WasRewritten);
        });
    }

    // [C1] Akışla yazım disk biçimini DEĞİŞTİRMEZ: tek satır kompakt JSON, BOM yok. Okuma yönünü
    // An_entry_from_an_older_schema_is_evaluated_again kapatır (elle yazılmış eski biçim akışla okunur).
    [Fact]
    public void The_streamed_ledger_is_still_compact_utf8_json_without_a_byte_order_mark()
    {
        WithLedger((root, cachePath) =>
        {
            SeededCache(root, cachePath).Flush();

            byte[] bytes = File.ReadAllBytes(cachePath);

            Assert.Equal((byte)'{', bytes[0]);                                // BOM (EF BB BF) yok
            Assert.DoesNotContain((byte)'\n', bytes);                         // WriteIndented=false: tek satır
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Single(doc.RootElement.EnumerateObject());                 // SeededCache tek proje
        });
    }

    // [Faz 3/Task 1] Eski (semasiz) bir kayit, mtime+length AYNI kalsa bile isabet SAYILMAMALI: Faz 3'te
    // EvaluatedProject'e eklenen yeni alanlar (OutputType, OutputPaths, ...) eski kayitta hep bos kalirdi.
    [Fact]
    public void PruneStaleSchema_removes_every_entry_outside_the_current_schema_whatever_its_root_and_writes_the_ledger()
    {
        // [PERF Faz C/C2] Optimize: eski şemalı girdi hiç isabet vermez (GetOrEvaluate Schema == CurrentSchema ister) ve hiçbir
        // kök onu göremez; budama KÖKTEN BAĞIMSIZdır. Budanan girdi diske yazılmazsa yalnız bellekte gider — dosyada kalır.
        WithLedger((root, cachePath) =>
        {
            string current = Path.Combine(root, "wt-a", "Current.csproj");
            string noField = Path.Combine(root, "wt-b", "NoField.csproj");   // Schema alanından önceki biçim
            string zero = Path.Combine(root, "wt-c", "Zero.csproj");         // açıkça eski şema
            string newer = Path.Combine(root, "wt-d", "Newer.csproj");       // daha yeni bir sürümün yazdığı: bu sürüm için isabet değil
            EvaluationCacheFile.Write(cachePath, (current, EvaluationCache.CurrentSchema), (noField, null), (zero, 0),
                (newer, EvaluationCache.CurrentSchema + 1));
            var cache = new EvaluationCache(cachePath);
            var ledger = LedgerFileProbe.Pin(cachePath);

            Assert.Equal(3, cache.PruneStaleSchema());

            Assert.True(ledger.WasRewritten);                                // budama defteri diske yazar
            Assert.Equal(new[] { current }, EvaluationCacheFile.Keys(cachePath));
        });
    }

    [Fact]
    public void PruneStaleSchema_with_nothing_stale_leaves_the_ledger_untouched()
    {
        WithLedger((root, cachePath) =>
        {
            EvaluationCacheFile.Write(cachePath, (Path.Combine(root, "A", "A.csproj"), EvaluationCache.CurrentSchema));
            var cache = new EvaluationCache(cachePath);
            var ledger = LedgerFileProbe.Pin(cachePath);

            Assert.Equal(0, cache.PruneStaleSchema());

            Assert.False(ledger.WasRewritten);                               // budanacak bir şey yoksa dokunulmaz
        });
    }

    [Fact]
    public void An_entry_from_an_older_schema_is_evaluated_again()
    {
        string root = Path.Combine(Path.GetTempPath(), "evcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string proj = Path.Combine(root, "A.csproj");
            File.WriteAllText(proj, """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>OSYS.A</AssemblyName>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                    <OutputPath>bin\Debug\</OutputPath>
                  </PropertyGroup>
                </Project>
                """);
            var info = new FileInfo(proj);
            string cachePath = Path.Combine(root, "cache.json");

            // Eski format (Schema alani hic yok) elle yazilir; mtime/length GERCEK dosyayla eslesiyor.
            string escapedPath = proj.Replace(@"\", @"\\");
            string oldJson = $$"""
                {
                  "{{escapedPath}}": {
                    "MtimeTicks": {{info.LastWriteTimeUtc.Ticks}},
                    "Length": {{info.Length}},
                    "Hash": "deadbeef",
                    "Project": {
                      "Path": "{{escapedPath}}",
                      "AssemblyName": "Stale",
                      "CompileFiles": [],
                      "HintPaths": [],
                      "ProjectReferences": [],
                      "IsSdkStyle": false
                    }
                  }
                }
                """;
            File.WriteAllText(cachePath, oldJson);

            var cache = new EvaluationCache(cachePath);
            int calls = 0;
            var evaluator = new CsprojEvaluator();
            EvaluatedProject Counting(string p) { calls++; return evaluator.Evaluate(p); }

            var result = cache.GetOrEvaluate(proj, Counting);

            Assert.Equal(1, calls); // eski semali kayit isabet sayilmadi, evaluate yeniden cagrildi
            Assert.NotNull(result);
            Assert.Equal("OSYS.A", result!.AssemblyName); // "Stale" degil, gercek deger
            Assert.Equal("Library", result.OutputType);   // yeni alan dolu
            Assert.Single(result.OutputPaths);             // yeni alan dolu
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
