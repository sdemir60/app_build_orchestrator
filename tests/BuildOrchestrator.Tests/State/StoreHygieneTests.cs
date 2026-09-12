using System;
using System.IO;
using System.Linq;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [optimize] Üç kalıcı defterin (build-state.json + evaluation-cache.json + source-hash-cache.json) HİJYEN
/// primitifleri: <c>PruneMissingUnderRoot</c> (dosyası artık diskte olmayan girdileri budar) ve
/// <c>SweepOrphanTempFiles</c> (yarım kalmış atomik-yazma <c>.tmp</c> artıklarını süpürür). İkisini de
/// yalnız kullanıcı-tetikli Optimize çağırır; Load/Upsert/GetOrEvaluate/HashOf/Flush davranışı DEĞİŞMEZ.
///
/// <para>İlk iki defter csproj yollarıyla, üçüncüsü KAYNAK DOSYA yollarıyla anahtarlanır — budama ölçütü
/// ikisinde de aynıdır (anahtarın gösterdiği dosya var mı), bu yüzden üçü de aynı iki primitifi paylaşır.</para>
///
/// <para>Budama iki bakımdan dar tutulur: (a) <b>workspace-scoped</b> — yalnız verilen kökün ALTINDAKİ
/// anahtarlar bakılır, başka repolardan gelen girdiler (ve worktree yollu kayıtlar) korunur; (b)
/// <b>varlık-tabanlı</b> — silinen tek şey csproj'u gerçekten kaybolmuş girdidir.</para>
/// </summary>
public class StoreHygieneTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bo-hygiene-" + Guid.NewGuid().ToString("N"));

    public StoreHygieneTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private string Workspace => Path.Combine(_root, "repo");

    /// <summary>Kök altında GERÇEKTEN var olan bir csproj yaratır ve tam yolunu döner.</summary>
    private string LiveProject(string name)
    {
        string dir = Path.Combine(Workspace, name);
        Directory.CreateDirectory(dir);
        string csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj, "<Project/>");
        return csproj;
    }

    /// <summary>Kök altında var OLMAYAN bir csproj'un tam yolunu döner (dosya yaratılmaz).</summary>
    private string DeadProject(string name) => Path.Combine(Workspace, name, name + ".csproj");

    // ------------------------------------------------------------------ BuildStateStore

    [Fact]
    public void PruneMissingUnderRoot_removes_only_entries_whose_csproj_no_longer_exists()
    {
        var store = new BuildStateStore(_root);
        string live = LiveProject("Alive");
        string dead = DeadProject("Gone");
        string foreignDead = Path.Combine(_root, "other-repo", "X", "X.csproj"); // kök DIŞI + diskte YOK

        store.Upsert(new BuildState(live, "sig-a"));
        store.Upsert(new BuildState(dead, "sig-b"));
        store.Upsert(new BuildState(foreignDead, "sig-c"));

        Assert.Equal(1, store.PruneMissingUnderRoot(Workspace));

        var map = store.Load();
        Assert.True(map.ContainsKey(live));
        Assert.False(map.ContainsKey(dead));
        Assert.True(map.ContainsKey(foreignDead)); // başka bir workspace'in defterine karışılmaz
    }

    [Fact]
    public void PruneMissingUnderRoot_normalizes_case_and_trailing_separator_and_avoids_the_prefix_trap()
    {
        var store = new BuildStateStore(_root);
        string dead = DeadProject("Gone");
        // "repo2" ADI "repo" ile başlar ama AYRI bir workspace'tir — ayraçsız prefix karşılaştırması onu
        // yanlışlıkla kapsam içinde sayardı.
        string siblingDead = Path.Combine(_root, "repo2", "Y", "Y.csproj");

        store.Upsert(new BuildState(dead, "sig-a"));
        store.Upsert(new BuildState(siblingDead, "sig-b"));

        // Kök farklı harf kutusu + sondaki ayraçla verilir; sonuç değişmez.
        string oddlyWritten = Workspace.ToUpperInvariant() + Path.DirectorySeparatorChar;
        Assert.Equal(1, store.PruneMissingUnderRoot(oddlyWritten));

        var map = store.Load();
        Assert.False(map.ContainsKey(dead));
        Assert.True(map.ContainsKey(siblingDead));
    }

    [Fact]
    public void PruneMissingUnderRoot_with_nothing_to_prune_does_not_rewrite_the_file()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState(LiveProject("Alive"), "sig-a"));

        string path = Path.Combine(_root, "build-state.json");
        string before = File.ReadAllText(path);
        var stampBefore = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, stampBefore.AddMinutes(-10)); // yazım olursa damga İLERİ giderdi
        var expected = File.GetLastWriteTimeUtc(path);

        Assert.Equal(0, store.PruneMissingUnderRoot(Workspace));

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(expected, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void PruneMissingUnderRoot_on_a_missing_file_returns_zero()
    {
        var store = new BuildStateStore(Path.Combine(_root, "no-such-cache-root"));
        Assert.Equal(0, store.PruneMissingUnderRoot(Workspace));
    }

    [Fact]
    public void SweepOrphanTempFiles_removes_only_this_stores_stale_tmp_files()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState(LiveProject("Alive"), "sig-a")); // build-state.json'ı yarat

        string statePath = Path.Combine(_root, "build-state.json");
        string staleTmp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string freshTmp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string foreignTmp = Path.Combine(_root, "evaluation-cache.json.deadbeef.tmp"); // BAŞKA store'un deseni
        string neighbour = Path.Combine(_root, "notes.txt");
        foreach (string f in new[] { staleTmp, freshTmp, foreignTmp, neighbour }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(staleTmp, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, store.SweepOrphanTempFiles(TimeSpan.FromHours(1)));

        Assert.False(File.Exists(staleTmp));
        Assert.True(File.Exists(freshTmp));   // aktif bir yazımın tmp'si olabilir — eşik altı ASLA silinmez
        Assert.True(File.Exists(foreignTmp)); // her store yalnız KENDİ desenini süpürür
        Assert.True(File.Exists(neighbour));
        Assert.True(File.Exists(statePath));  // hedef defterin kendisi
    }

    /// <summary>
    /// [D8] Eşik kararı enjekte edilebilir bir saatten okunur: test gerçek zamanı beklemez, saati ileri
    /// alır. Dosya damgasını geriye çekmek de eşdeğer olurdu — ama o, eşiğin hangi SAATE göre ölçüldüğünü
    /// pinlemez; saat dikişi mutasyona (ör. eşiği yok sayan bir sweep) karşı doğrudan kanıttır.
    /// </summary>
    [Fact]
    public void SweepOrphanTempFiles_clock_is_injectable()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState(LiveProject("Alive"), "sig-a"));

        string statePath = Path.Combine(_root, "build-state.json");
        string tmp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, "x"); // TAZE — gerçek saatle asla silinmez

        Assert.Equal(0, store.SweepOrphanTempFiles(TimeSpan.FromHours(1)));
        Assert.True(File.Exists(tmp));

        store.UtcNow = () => DateTime.UtcNow.AddHours(2); // saat ileri → aynı dosya artık bayat
        Assert.Equal(1, store.SweepOrphanTempFiles(TimeSpan.FromHours(1)));
        Assert.False(File.Exists(tmp));
    }

    // ------------------------------------------------------------------ EvaluationCache

    private static EvaluatedProject Fake(string p) => new(p, Path.GetFileNameWithoutExtension(p), [], [], [], false);

    [Fact]
    public void EvaluationCache_PruneMissingUnderRoot_removes_only_dead_entries_under_the_root()
    {
        string cachePath = Path.Combine(_root, "evaluation-cache.json");
        string live = LiveProject("Alive");
        string dead = LiveProject("Gone");
        string foreign = Path.Combine(_root, "other-repo", "X");
        Directory.CreateDirectory(foreign);
        string foreignLive = Path.Combine(foreign, "X.csproj");
        File.WriteAllText(foreignLive, "<Project/>");

        var cache = new EvaluationCache(cachePath);
        foreach (string p in new[] { live, dead, foreignLive }) Assert.NotNull(cache.GetOrEvaluate(p, Fake));
        File.Delete(dead);
        File.Delete(foreignLive); // kök DIŞI ölü girdi — budanmamalı

        Assert.Equal(1, cache.PruneMissingUnderRoot(Workspace));

        // Diri kayıt hâlâ cache-hit verir (prune canlı girdilere dokunmaz).
        int calls = 0;
        EvaluatedProject Counting(string p) { calls++; return Fake(p); }
        Assert.NotNull(cache.GetOrEvaluate(live, Counting));
        Assert.Equal(0, calls);

        // Kök dışı ölü girdi diskte de duruyor: yeni bir örnek onu hâlâ okur.
        cache.Flush();
        Assert.Contains("other-repo", File.ReadAllText(cachePath));
    }

    [Fact]
    public void EvaluationCache_PruneMissingUnderRoot_avoids_the_prefix_trap_and_ignores_case()
    {
        string cachePath = Path.Combine(_root, "evaluation-cache.json");
        string dead = LiveProject("Gone");
        string siblingDir = Path.Combine(_root, "repo2", "Y");
        Directory.CreateDirectory(siblingDir);
        string siblingDead = Path.Combine(siblingDir, "Y.csproj");
        File.WriteAllText(siblingDead, "<Project/>");

        var cache = new EvaluationCache(cachePath);
        foreach (string p in new[] { dead, siblingDead }) Assert.NotNull(cache.GetOrEvaluate(p, Fake));
        File.Delete(dead);
        File.Delete(siblingDead);

        Assert.Equal(1, cache.PruneMissingUnderRoot(Workspace.ToUpperInvariant() + Path.DirectorySeparatorChar));

        cache.Flush();
        string json = File.ReadAllText(cachePath);
        Assert.Contains("repo2", json);
        Assert.DoesNotContain("Gone.csproj", json);
    }

    [Fact]
    public void EvaluationCache_PruneMissingUnderRoot_with_nothing_to_prune_does_not_rewrite_the_file()
    {
        string cachePath = Path.Combine(_root, "evaluation-cache.json");
        var cache = new EvaluationCache(cachePath);
        Assert.NotNull(cache.GetOrEvaluate(LiveProject("Alive"), Fake));
        cache.Flush();

        string before = File.ReadAllText(cachePath);
        File.SetLastWriteTimeUtc(cachePath, File.GetLastWriteTimeUtc(cachePath).AddMinutes(-10));
        var expected = File.GetLastWriteTimeUtc(cachePath);

        Assert.Equal(0, cache.PruneMissingUnderRoot(Workspace));

        Assert.Equal(before, File.ReadAllText(cachePath));
        Assert.Equal(expected, File.GetLastWriteTimeUtc(cachePath));
    }

    [Fact]
    public void EvaluationCache_PruneMissingUnderRoot_on_a_missing_file_returns_zero()
        => Assert.Equal(0, new EvaluationCache(Path.Combine(_root, "nope", "evaluation-cache.json"))
            .PruneMissingUnderRoot(Workspace));

    [Fact]
    public void EvaluationCache_SweepOrphanTempFiles_removes_only_its_own_stale_tmp_files()
    {
        string cachePath = Path.Combine(_root, "evaluation-cache.json");
        var cache = new EvaluationCache(cachePath);
        Assert.NotNull(cache.GetOrEvaluate(LiveProject("Alive"), Fake));
        cache.Flush();

        string staleTmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string freshTmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string foreignTmp = Path.Combine(_root, "build-state.json.deadbeef.tmp");
        foreach (string f in new[] { staleTmp, freshTmp, foreignTmp }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(staleTmp, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, cache.SweepOrphanTempFiles(TimeSpan.FromHours(1)));

        Assert.False(File.Exists(staleTmp));
        Assert.True(File.Exists(freshTmp));
        Assert.True(File.Exists(foreignTmp));
        Assert.True(File.Exists(cachePath));
    }

    [Fact]
    public void EvaluationCache_SweepOrphanTempFiles_clock_is_injectable()
    {
        string cachePath = Path.Combine(_root, "evaluation-cache.json");
        var cache = new EvaluationCache(cachePath);
        Assert.NotNull(cache.GetOrEvaluate(LiveProject("Alive"), Fake));
        cache.Flush();

        string tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, "x");

        Assert.Equal(0, cache.SweepOrphanTempFiles(TimeSpan.FromHours(1)));
        cache.UtcNow = () => DateTime.UtcNow.AddHours(2);
        Assert.Equal(1, cache.SweepOrphanTempFiles(TimeSpan.FromHours(1)));
        Assert.False(File.Exists(tmp));
    }

    // ------------------------------------------------------------------ SourceHashCache

    /// <summary>Kök altında GERÇEKTEN var olan bir kaynak dosya yaratır. Damga geri çekilir: <c>Flush</c>
    /// racy penceresindeki (son 2 sn) girdileri diske YAZMAZ, taze dosyayla kurulan bir defter boş kalırdı.</summary>
    private string LiveSource(string name)
    {
        string dir = Path.Combine(Workspace, name);
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name + ".cs");
        File.WriteAllText(file, "class " + name + " { }");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        return file;
    }

    /// <summary>[optimize] Üçüncü defter de aynı sözleşmeye tabidir; farkı anahtarının csproj değil KAYNAK
    /// DOSYA olmasıdır — silinen tek bir <c>.cs</c> bile burada ölü girdi bırakır, bu yüzden en çok o birikir.</summary>
    [Fact]
    public void SourceHashCache_PruneMissingUnderRoot_removes_only_dead_entries_under_the_root()
    {
        string cachePath = Path.Combine(_root, SourceHashCache.FileName);
        string live = LiveSource("Alive");
        string dead = LiveSource("Gone");
        string foreignDir = Path.Combine(_root, "other-repo");
        Directory.CreateDirectory(foreignDir);
        string foreignDead = Path.Combine(foreignDir, "X.cs");
        File.WriteAllText(foreignDead, "class X { }");
        File.SetLastWriteTimeUtc(foreignDead, DateTime.UtcNow.AddMinutes(-5));

        var cache = new SourceHashCache(cachePath);
        foreach (string p in new[] { live, dead, foreignDead }) Assert.NotNull(cache.HashOf(p));
        File.Delete(dead);
        File.Delete(foreignDead); // kök DIŞI ölü girdi — budanmamalı

        Assert.Equal(1, cache.PruneMissingUnderRoot(Workspace));

        string json = File.ReadAllText(cachePath);
        Assert.Contains("other-repo", json);
        Assert.Contains("Alive.cs", json);
        Assert.DoesNotContain("Gone.cs", json);
    }

    [Fact]
    public void SourceHashCache_PruneMissingUnderRoot_with_nothing_to_prune_does_not_rewrite_the_file()
    {
        string cachePath = Path.Combine(_root, SourceHashCache.FileName);
        var cache = new SourceHashCache(cachePath);
        Assert.NotNull(cache.HashOf(LiveSource("Alive")));
        cache.Flush();

        string before = File.ReadAllText(cachePath);
        File.SetLastWriteTimeUtc(cachePath, File.GetLastWriteTimeUtc(cachePath).AddMinutes(-10));
        var expected = File.GetLastWriteTimeUtc(cachePath);

        Assert.Equal(0, cache.PruneMissingUnderRoot(Workspace));

        Assert.Equal(before, File.ReadAllText(cachePath));
        Assert.Equal(expected, File.GetLastWriteTimeUtc(cachePath));
    }

    [Fact]
    public void SourceHashCache_SweepOrphanTempFiles_removes_only_its_own_stale_tmp_files()
    {
        string cachePath = Path.Combine(_root, SourceHashCache.FileName);
        var cache = new SourceHashCache(cachePath);
        Assert.NotNull(cache.HashOf(LiveSource("Alive")));
        cache.Flush();

        string staleTmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string freshTmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string foreignTmp = Path.Combine(_root, "build-state.json.deadbeef.tmp");
        foreach (string f in new[] { staleTmp, freshTmp, foreignTmp }) File.WriteAllText(f, "x");
        File.SetLastWriteTimeUtc(staleTmp, DateTime.UtcNow.AddHours(-2));

        Assert.Equal(1, cache.SweepOrphanTempFiles(TimeSpan.FromHours(1)));

        Assert.False(File.Exists(staleTmp));
        Assert.True(File.Exists(freshTmp));
        Assert.True(File.Exists(foreignTmp));
        Assert.True(File.Exists(cachePath));
    }

    // ------------------------------------------------------------------ RootScope (üç defterin ortak kapısı)

    /// <summary>
    /// [RootScope] Çözülemeyen kök FIRLATMAZ, sessizce "dokunulacak bir şey yok"a düşer — defterlerin
    /// never-throw sözleşmesi budur ve kök-kapsamlı DÖRT giriş noktasının hepsi aynı kapıdan geçer.
    ///
    /// <para>Bu pin bilinçlidir: normalizasyon <c>RemoveUnderRoot</c>'un içinden <see cref="RootScope"/>'a
    /// taşındığında catch kümesi daralırsa sözleşme SESSİZCE kırılırdı — hiçbir mevcut test onu tutmuyordu.
    /// Mutasyonla doğrulandı: <c>NormalizeRoot</c>'un try/catch'i kaldırılınca null karakterli kök
    /// <c>ArgumentException</c> fırlatır ve dördü birden kırmızı olur.</para>
    /// </summary>
    [Fact]
    public void A_root_that_cannot_be_resolved_touches_nothing_and_throws_nowhere()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState(LiveProject("Alive"), "sig-a"));
        var cache = new EvaluationCache(Path.Combine(_root, "evaluation-cache.json"));
        var hashes = new SourceHashCache(Path.Combine(_root, SourceHashCache.FileName));

        foreach (string bad in new[] { "", "   ", "C:\\bad\0root" })
        {
            Assert.Equal(0, store.RemoveUnderRoot(bad));
            Assert.Equal(0, store.PruneMissingUnderRoot(bad));
            Assert.Equal(0, cache.PruneMissingUnderRoot(bad));
            Assert.Equal(0, hashes.PruneMissingUnderRoot(bad));
        }

        Assert.True(store.Load().Count == 1); // hiçbir şeye dokunulmadı
    }
}
