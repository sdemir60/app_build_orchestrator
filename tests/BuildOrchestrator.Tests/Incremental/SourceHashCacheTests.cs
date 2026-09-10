using System.IO;
using BuildOrchestrator.Core.Incremental;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [D3] Özet önbelleği: kararın bedelini "her koşuda 288 MB oku"dan "her koşuda bir stat geçişi"ne indiren
/// şey budur (ölçüm: 1.020 ms → 156 ms). Yanlış bir isabet under-build demektir, bu yüzden anahtar boyut VE
/// mtime'dır ve git'in "racy" kuralı da uygulanır.
/// </summary>
public sealed class SourceHashCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bo-hashcache-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* test temizliği */ }
    }

    private string CachePath => Path.Combine(_dir, SourceHashCache.FileName);

    private string WriteFile(string name, string content, DateTime? mtimeUtc = null)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        if (mtimeUtc is { } stamp) File.SetLastWriteTimeUtc(path, stamp);
        return path;
    }

    [Fact]
    public void the_same_content_hashes_the_same_and_different_content_differs()
    {
        var cache = new SourceHashCache(CachePath);
        string a = WriteFile("a.cs", "class A {}");
        string b = WriteFile("b.cs", "class A {}");
        string c = WriteFile("c.cs", "class C {}");

        Assert.Equal(cache.HashOf(a), cache.HashOf(b));
        Assert.NotEqual(cache.HashOf(a), cache.HashOf(c));
    }

    [Fact]
    public void a_file_that_does_not_exist_hashes_to_null()
    {
        var cache = new SourceHashCache(CachePath);
        Assert.Null(cache.HashOf(Path.Combine(_dir, "missing.cs")));
    }

    [Fact]
    public void an_unchanged_file_is_not_read_again_after_the_cache_is_persisted()
    {
        // İSABETİN KANITI: önbellek yazılıp yeniden yüklendikten sonra dosyanın İÇERİĞİ diskte değiştirilir
        // ama boyut ve mtime eski hâlinde bırakılır. Önbellek okusaydı yeni içeriği görürdü; stat isabetiyle
        // ESKİ özet döner. (Aynı numarayla "okundu mu" sorusu ek bir sayaç enjekte etmeden ölçülür.)
        var old = DateTime.UtcNow.AddHours(-1);
        string path = WriteFile("a.cs", "class A {}", old);

        var first = new SourceHashCache(CachePath);
        string? original = first.HashOf(path);
        first.Flush();

        File.WriteAllText(path, "class B {}");          // aynı uzunluk, farklı içerik
        File.SetLastWriteTimeUtc(path, old);            // mtime geri alındı

        var second = new SourceHashCache(CachePath);
        Assert.Equal(original, second.HashOf(path));
        Assert.True(second.IsCached(path));
    }

    [Fact]
    public void a_changed_mtime_or_size_invalidates_the_entry()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        string path = WriteFile("a.cs", "class A {}", old);

        var first = new SourceHashCache(CachePath);
        string? original = first.HashOf(path);
        first.Flush();

        File.WriteAllText(path, "class A { int changed; }");   // hem boyut hem mtime değişti
        var second = new SourceHashCache(CachePath);

        Assert.False(second.IsCached(path));
        Assert.NotEqual(original, second.HashOf(path));
    }

    [Fact]
    public void an_entry_written_within_the_racy_window_is_not_persisted()
    {
        // git'in index kuralı: mtime'ı yazma anına çok yakın bir dosyaya güvenilmez — aynı saniye içinde,
        // aynı boyutta yeniden yazılan bir dosya bayat bir özetle "değişmemiş" görünebilirdi.
        string fresh = WriteFile("fresh.cs", "class Fresh {}");                       // mtime = şimdi
        string old = WriteFile("old.cs", "class Old {}", DateTime.UtcNow.AddHours(-1));

        var cache = new SourceHashCache(CachePath);
        cache.HashOf(fresh);
        cache.HashOf(old);
        cache.Flush();

        var reloaded = new SourceHashCache(CachePath);
        Assert.False(reloaded.IsCached(fresh));   // racy → kalıcı değil, yeniden okunur
        Assert.True(reloaded.IsCached(old));
    }

    [Fact]
    public void a_corrupt_cache_file_is_treated_as_empty()
    {
        File.WriteAllText(CachePath, "{ this is not json");
        string path = WriteFile("a.cs", "class A {}");

        var cache = new SourceHashCache(CachePath);

        Assert.True(cache.WasEmpty);
        Assert.NotNull(cache.HashOf(path));       // hesaplama düşmez
    }

    [Fact]
    public void prefill_announces_the_missing_count_once_and_only_reads_what_is_missing()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        var files = Enumerable.Range(0, 5).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", old)).ToList();

        var cache = new SourceHashCache(CachePath);
        var announced = new List<int>();

        Assert.Equal(5, cache.Prefill(files, announced.Add));
        Assert.Equal([5], announced);

        // İkinci geçişte hepsi önbellekte: ne satır yazılır ne dosya okunur.
        announced.Clear();
        Assert.Equal(0, cache.Prefill(files, announced.Add));
        Assert.Empty(announced);
    }

    [Fact]
    public void prefill_fills_the_cache_for_every_path_it_was_given()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        var files = Enumerable.Range(0, 40).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", old)).ToList();

        var cache = new SourceHashCache(CachePath);
        cache.Prefill(files);

        Assert.All(files, f => Assert.True(cache.IsCached(f)));
    }
}
