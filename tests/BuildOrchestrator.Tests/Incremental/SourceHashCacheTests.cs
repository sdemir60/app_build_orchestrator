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

    /// <summary>
    /// [test seam] <c>Seed</c> bir dosya için dosyanın GERÇEK boyut+mtime'ıyla sahte bir özet yazar ve diske
    /// işler: diskten yeniden yüklenen önbellek o dosyayı AÇMADAN tohumlanan özeti döner. Kabul koşusu bir
    /// kaynağın düzenlenmiş hâlini gerçek dosyaya dokunmadan böyle simüle eder; disk biçimi yalnız
    /// <see cref="SourceHashCache"/>'te tanımlı kalır.
    /// </summary>
    [Fact]
    public void a_seeded_hash_is_persisted_and_returned_without_reading_the_file()
    {
        string path = WriteFile("a.cs", "class A {}", DateTime.UtcNow.AddHours(-1));
        string real = new SourceHashCache(Path.Combine(_dir, "other.json")).HashOf(path)!;

        new SourceHashCache(CachePath).Seed(path, "SIMULATED-EDIT");
        var reloaded = new SourceHashCache(CachePath);

        Assert.True(reloaded.IsCached(path));
        Assert.Equal("SIMULATED-EDIT", reloaded.HashOf(path));
        Assert.NotEqual(real, reloaded.HashOf(path));
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

    /// <summary>Prefill'in gözlemciye söylediklerini sırayla yazar (zaman damgası olmayan, deterministik metin).</summary>
    private sealed class RecordingObserver : IPrefillObserver
    {
        public List<string> Calls { get; } = [];
        public void Started(int total) => Calls.Add($"Started({total})");
        public void Progress(int done, int total, long elapsedMs) => Calls.Add($"Progress({done},{total},{elapsedMs})");
        public void Finished(int total, long elapsedMs) => Calls.Add($"Finished({total},{elapsedMs})");
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Eski sözleşme: <c>Prefill(paths, Action&lt;int&gt; announce)</c> yalnız eksik sayıyı okuma
    /// başlamadan bir kez bildirirdi; bu test onu pinliyordu. Yeni sözleşme: gözlemci (<see cref="IPrefillObserver"/>)
    /// başlangıcın yanında ilerlemeyi ve bitişi de taşır — çünkü ilk indeksleme (23 bin dosya, ~51 sn) tek bir
    /// sayıdan sonra sessiz kalıyordu. "Eksik sayı bir kez bildirilir, ikinci geçiş hiçbir şey bildirmez ve okumaz"
    /// iddiası aynen korunur; yalnız bildirimin biçimi değişti.
    /// </summary>
    [Fact]
    public void prefill_tells_the_observer_the_missing_count_once_and_only_reads_what_is_missing()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        var files = Enumerable.Range(0, 5).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", old)).ToList();

        // Tik ve saat sabit: Progress'in kaç kez çağrıldığı bu testin konusu değil (aşağıdaki testler pinler).
        var cache = new SourceHashCache(CachePath) { PrefillTick = work => { work.Wait(); return true; }, PrefillElapsedMs = () => 0 };
        var observer = new RecordingObserver();

        Assert.Equal(5, cache.Prefill(files, observer));
        Assert.Equal(["Started(5)", "Finished(5,0)"], observer.Calls);

        // İkinci geçişte hepsi önbellekte: ne bildirim yapılır ne dosya okunur.
        observer.Calls.Clear();
        Assert.Equal(0, cache.Prefill(files, observer));
        Assert.Empty(observer.Calls);
    }

    /// <summary>İlk indeksleme sürerken gözlemci her tikte sayıyı alır, bitiş EN SON gelir. Kapı: hash işi tik 2'ye
    /// kadar bekler, böylece "iş sürerken" ve "iş bitti" anları saat/uyku olmadan deterministik kurulur.</summary>
    [Fact]
    public void prefill_reports_progress_while_it_hashes_and_finishes_last()
    {
        var files = Enumerable.Range(0, 3).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", OldStamp)).ToList();
        using var gate = new ManualResetEventSlim();
        var clock = new Queue<long>([1_000, 2_000]);
        int ticks = 0;
        var observer = new RecordingObserver();

        var cache = new SourceHashCache(CachePath);
        cache.PrefillHasher = path => { gate.Wait(TimeSpan.FromSeconds(30)); return cache.HashOf(path); };
        cache.PrefillElapsedMs = clock.Dequeue;
        cache.PrefillTick = work =>
        {
            if (++ticks == 1) return false;     // iş sürüyor: hiçbir dosya bitmedi
            gate.Set();
            work.Wait();
            return true;                        // iş bitti
        };

        Assert.Equal(3, cache.Prefill(files, observer));

        Assert.Equal(["Started(3)", "Progress(0,3,1000)", "Finished(3,2000)"], observer.Calls);
    }

    /// <summary>Soğuk ilk indekslemede 23 bin dosya ~51 sn sürer ve eskiden defter en sonda bir kez yazılırdı: yarıda
    /// kesilen indeksleme sıfırdan başlardı. Şimdi Prefill sürerken her <see cref="SourceHashCache.PrefillFlushIntervalMs"/>
    /// defteri diske alır.</summary>
    [Fact]
    public void prefill_saves_what_it_has_hashed_while_it_is_still_running()
    {
        string a = WriteFile("a.cs", "class A {}", OldStamp);
        string b = WriteFile("b.cs", "class B {}", OldStamp);
        string c = WriteFile("c.cs", "class C {}", OldStamp);
        using var gate = new ManualResetEventSlim();
        using var aAndBHashed = new CountdownEvent(2);
        int ticks = 0, clockReads = 0;
        bool aOnDisk = false, bOnDisk = false, cOnDisk = true;

        var cache = new SourceHashCache(CachePath);
        cache.PrefillHasher = path =>
        {
            if (path == c) gate.Wait(TimeSpan.FromSeconds(30));          // c, tik 2'ye kadar yarım kalır
            string? hash = cache.HashOf(path);
            if (path != c) aAndBHashed.Signal();
            return hash;
        };
        cache.PrefillElapsedMs = () => SourceHashCache.PrefillFlushIntervalMs * ++clockReads;
        cache.PrefillTick = work =>
        {
            if (++ticks == 1) { aAndBHashed.Wait(TimeSpan.FromSeconds(30)); return false; }   // a ve b bitti, c sürüyor

            var onDisk = new SourceHashCache(CachePath);                 // başka bir okuyucunun gördüğü defter
            (aOnDisk, bOnDisk, cOnDisk) = (onDisk.IsCached(a), onDisk.IsCached(b), onDisk.IsCached(c));
            gate.Set();
            work.Wait();
            return true;
        };

        Assert.Equal(3, cache.Prefill([a, b, c]));

        Assert.True(aOnDisk);
        Assert.True(bOnDisk);
        Assert.False(cOnDisk);
    }

    /// <summary>Gözlemci eklenince iptalin türü DEĞİŞMEMELİ: çağıranlar (ör. Sync'in <c>ComputeWillBuild</c>'ı)
    /// <c>ex is not OperationCanceledException</c> ile iptali hatadan ayırır; bitiş de iptalde bildirilmez.</summary>
    [Fact]
    public void prefill_cancellation_still_surfaces_as_operation_canceled()
    {
        var files = Enumerable.Range(0, 5).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", OldStamp)).ToList();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var observer = new RecordingObserver();
        var cache = new SourceHashCache(CachePath);

        Assert.ThrowsAny<OperationCanceledException>(() => cache.Prefill(files, observer, canceled.Token));

        Assert.DoesNotContain(observer.Calls, call => call.StartsWith("Finished", StringComparison.Ordinal));
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

    // ---------------------------------------------------------------- [PERF Faz C/C1] kirli bayrağı: defter yalnız değiştiyse yazılır
    // "Yazıldı mı"yı LedgerFileProbe ölçer: defterin mtime'ı eski bir damgaya çekilir, yeniden yazım (temp + File.Move)
    // damgayı bugüne getirir — saat ve uyku yok. Dosya mtime'ları GERÇEK saatten bağımsız sabit bir eski damgadır.

    private static readonly DateTime OldStamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DirtyFlag_flush_after_load_without_any_change_leaves_the_ledger_untouched()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        var first = new SourceHashCache(CachePath);
        first.HashOf(path);
        first.Flush();
        var ledger = LedgerFileProbe.Pin(CachePath);

        var warm = new SourceHashCache(CachePath);
        Assert.NotNull(warm.HashOf(path));      // isabet: dosya açılmaz, girdi değişmez
        warm.Flush();

        Assert.False(ledger.WasRewritten);
    }

    [Fact]
    public void DirtyFlag_a_recomputed_hash_is_written_by_the_next_flush_and_only_by_it()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        var first = new SourceHashCache(CachePath);
        first.HashOf(path);
        first.Flush();

        var cache = new SourceHashCache(CachePath);
        WriteFile("a.cs", "class A { int changed; }", OldStamp.AddMinutes(5));   // boyut + mtime değişti
        cache.HashOf(path);                                                       // yeniden hesaplanır → kirli
        var ledger = LedgerFileProbe.Pin(CachePath);
        cache.Flush();
        Assert.True(ledger.WasRewritten);                                         // kirli → yazar

        ledger = LedgerFileProbe.Pin(CachePath);
        cache.Flush();
        Assert.False(ledger.WasRewritten);                                        // temiz → ikinci Flush yazmaz
        Assert.True(new SourceHashCache(CachePath).IsCached(path));               // yazılan şey yeni boyut+mtime'ın özetidir
    }

    // [C1] Racy pencerede yazılmayan girdi bayrağı kirli bırakır: pencere geçince sonraki Flush onu da yazar. Saat
    // UtcNow seam'iyle enjekte edilir — Flush'ın racy kesimi de aynı saati okur (süpürme eşiğiyle tek saat).
    [Fact]
    public void DirtyFlag_an_entry_left_out_by_the_racy_window_keeps_the_ledger_dirty_until_the_window_passes()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        var cache = new SourceHashCache(CachePath) { UtcNow = () => OldStamp + SourceHashCache.RacyWindow / 2 };
        cache.HashOf(path);

        cache.Flush();                                                            // mtime pencerenin İÇİNDE → giriş yazılmaz
        Assert.False(new SourceHashCache(CachePath).IsCached(path));

        cache.UtcNow = () => OldStamp + SourceHashCache.RacyWindow + TimeSpan.FromSeconds(1);   // pencere geçti
        var ledger = LedgerFileProbe.Pin(CachePath);
        cache.Flush();                                                            // yazılamayan giriş bayrağı kirli bıraktı → şimdi yazar
        Assert.True(ledger.WasRewritten);
        Assert.True(new SourceHashCache(CachePath).IsCached(path));

        ledger = LedgerFileProbe.Pin(CachePath);
        cache.Flush();                                                            // artık temiz
        Assert.False(ledger.WasRewritten);
    }

    [Fact]
    public void DirtyFlag_pruning_a_dead_entry_writes_the_ledger_and_pruning_nothing_does_not()
    {
        string keep = WriteFile("keep.cs", "class K {}", OldStamp);
        string gone = WriteFile("gone.cs", "class G {}", OldStamp);
        var cache = new SourceHashCache(CachePath);
        cache.HashOf(keep);
        cache.HashOf(gone);
        cache.Flush();
        File.Delete(gone);
        var ledger = LedgerFileProbe.Pin(CachePath);

        Assert.Equal(1, cache.PruneMissingUnderRoot(_dir));
        Assert.True(ledger.WasRewritten);                                         // budama defteri yazar
        Assert.True(new SourceHashCache(CachePath).IsCached(keep));

        ledger = LedgerFileProbe.Pin(CachePath);
        Assert.Equal(0, cache.PruneMissingUnderRoot(_dir));
        Assert.False(ledger.WasRewritten);                                        // budanacak bir şey yoksa dokunulmaz
    }

    [Fact]
    public void DirtyFlag_a_failed_write_leaves_the_ledger_dirty_so_the_next_flush_retries()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        File.WriteAllText(CachePath, "{}");
        var cache = new SourceHashCache(CachePath);
        cache.HashOf(path);                                                       // yeni özet → kirli
        var ledger = LedgerFileProbe.Pin(CachePath);

        cache.RenameRetryDelay = _ => { };   // kilit hiç kalkmaz: retry bütçesi gerçek bekleme olmadan tükensin [D8]
        using (LedgerFileProbe.HoldLocked(CachePath))
            cache.Flush();                                                        // hedef kilitli → yazım düşer (yutulur)
        Assert.False(ledger.WasRewritten);

        cache.Flush();                                                            // kilit kalktı → bayrak kirli kaldıysa yazar
        Assert.True(ledger.WasRewritten);
    }

    // [C1 düzeltme 1 · I1] Defterin okuma tutamağı Delete-share verir (durum dosyalarıyla aynı kural, AtomicFile): hedefte
    // DELETE erişimi tutan bir taraf varken (dosyayı silen/yeniden adlandıran başka bir process) Load'un açışı reddedilmez.
    // Windows paylaşım denetimi SİMETRİKTİR; karşı taraf önce açılır (DELETE erişimi tutar) ve Load'un onunla uyuşup
    // uyuşmadığına bakılır — saat ve uyku yok. Varsayılan okuma kipi (FileShare.Read) bu tutamağı reddeder → defter boş okunur.
    // (Açık bir okuyucu hedefin üstüne rename'i Delete-share'e rağmen geciktirir; onu retry absorbe eder — sonraki test.)
    [Fact]
    public void Load_reads_a_ledger_that_another_party_holds_with_delete_access()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        var first = new SourceHashCache(CachePath);
        first.HashOf(path);
        first.Flush();

        using (LedgerFileProbe.HoldDeleteAccess(CachePath))
            Assert.False(new SourceHashCache(CachePath).WasEmpty);                // defter okundu (boş sayılmadı)
    }

    // [C1 düzeltme 1 · O1] Yazım AtomicFile'dan geçer: hedef kısa süre Delete-share'siz tutulursa rename retry ile absorbe
    // edilir ve defter güncellemesi kaybolmaz (eskiden tek denemede düşer, yutulurdu). Tutamak, retry gecikmesi dikişinden
    // ilk retry'da bırakılır — saat ve uyku yok.
    [Fact]
    public void Flush_absorbs_a_brief_sharing_violation_on_the_ledger_with_a_retry()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        File.WriteAllText(CachePath, "{}");
        var cache = new SourceHashCache(CachePath);
        cache.HashOf(path);                                                       // yeni özet → kirli
        var ledger = LedgerFileProbe.Pin(CachePath);
        using var block = LedgerFileProbe.BlockRename(CachePath);                 // Delete-share YOK → rename düşer
        cache.RenameRetryDelay = block.ReleaseOnRetry;

        cache.Flush();

        Assert.Equal(1, block.Retries);                                           // ilk deneme düştü, ikincisi geçti
        Assert.True(ledger.WasRewritten);
        Assert.True(new SourceHashCache(CachePath).IsCached(path));               // güncelleme kaybolmadı
    }

    // [C1] Bayrak eşzamanlı yazımlara güvenlidir: biri durmadan Flush eder, dördü aynı anda yeni özet ekler. Bir Flush'ın
    // bayrağı indirip yazarken araya giren işaret kaybolsaydı son Flush yazacak bir şey bulamaz ve girdiler diske HİÇ düşmezdi.
    [Fact]
    public async Task DirtyFlag_marks_made_while_a_flush_is_running_are_never_lost()
    {
        var files = Enumerable.Range(0, 200).Select(i => WriteFile($"f{i}.cs", $"class F{i} {{}}", OldStamp)).ToList();
        var cache = new SourceHashCache(CachePath);
        using var writersDone = new CancellationTokenSource();

        var flusher = Task.Run(() => { while (!writersDone.IsCancellationRequested) cache.Flush(); });
        try { await Task.WhenAll(files.Chunk(50).Select(chunk => Task.Run(() => { foreach (string f in chunk) cache.HashOf(f); }))); }
        finally { writersDone.Cancel(); }                                         // bir yazıcı hata verse de flusher durur
        await flusher;

        // Son Flush: kayıp işaret yoksa kalan her şeyi yazar. Geçici bir IO hatası bayrağı kirli bırakır, yeniden denemek
        // güvenlidir; kaybolmuş bir işareti ise hiçbir deneme geri getiremez.
        bool AllPersisted() { var reloaded = new SourceHashCache(CachePath); return files.All(reloaded.IsCached); }
        for (int attempt = 0; attempt < 5 && !AllPersisted(); attempt++) cache.Flush();

        Assert.True(AllPersisted());
    }

    // [C1] Akışla okuma/yazma disk biçimini DEĞİŞTİRMEZ: önceki yazıcının ürünü (kompakt, BOM'suz) aynen okunur ve yeni
    // yazıcı aynı baytları üretir — mevcut kullanıcı dosyaları yeni sürümde de geçerlidir.
    [Fact]
    public void The_streamed_ledger_keeps_the_compact_json_format_the_previous_writer_produced()
    {
        string path = WriteFile("a.cs", "class A {}", OldStamp);
        var info = new FileInfo(path);
        string expected = "{" + System.Text.Json.JsonSerializer.Serialize(path)
            + ":{\"Length\":" + info.Length + ",\"MtimeTicks\":" + info.LastWriteTimeUtc.Ticks + ",\"Hash\":\"LEGACY\"}}";

        File.WriteAllText(CachePath, expected);                                   // önceki yazıcının ürünü
        Assert.Equal("LEGACY", new SourceHashCache(CachePath).HashOf(path));      // aynen okunur (stat isabeti, dosya açılmadan)

        File.Delete(CachePath);
        new SourceHashCache(CachePath).Seed(path, "LEGACY");                      // yeni yazıcı
        Assert.Equal(expected, File.ReadAllText(CachePath));                      // aynı baytları yazar
    }
}
