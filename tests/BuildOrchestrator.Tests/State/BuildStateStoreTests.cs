using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [T27] BuildStateStore: global `build-state.json` — projectId anahtarlı, single-writer serialized,
/// atomik temp+rename yazım (bkz. EvaluationCache/StaleObjDetector deseni). Hiçbir testte sleep/poll yok [D8];
/// eşzamanlılık Barrier ile deterministik olarak kurulur, single-writer Task.WaitAll ile beklenir (poll değil).
/// </summary>
public class BuildStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bo-buildstate-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_root, "build-state.json");

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact] // dosya yok → boş, throw yok
    public void Load_returns_empty_when_file_missing()
    {
        var store = new BuildStateStore(_root);
        var map = store.Load();
        Assert.Empty(map);
    }

    [Fact] // dosya var ama boş içerik → boş, throw yok
    public void Load_returns_empty_when_file_is_empty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath, "");
        var store = new BuildStateStore(_root);
        Assert.Empty(store.Load());
    }

    [Fact] // bozuk JSON → warn/boş dön, never-throw (StaleObjDetector deseni)
    public void Load_returns_empty_when_file_is_corrupt()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(StatePath, Encoding.UTF8.GetBytes("{ not valid json ]"));
        var store = new BuildStateStore(_root);
        var map = store.Load();
        Assert.Empty(map);
    }

    [Fact] // [Review Important 1] elle bozulmuş dosyada case-variant duplicate anahtar (Ordinal→OrdinalIgnoreCase
           // kopyasında ArgumentException fırlatırdı) → Load() ASLA fırlamaz, ignore-case dedup ile hayatta kalan
           // kayıt (JSON'daki SON değer) döner.
    public void Load_dedups_case_variant_duplicate_keys_instead_of_throwing()
    {
        Directory.CreateDirectory(_root);
        // JsonSerializer.Deserialize<Dictionary<string,BuildState>> varsayılan Ordinal comparer kullandığından
        // bu iki anahtar (yalnız case'te farklı) doğrudan JSON'a elle yazılabilir — normal Upsert bunu üretemez,
        // senaryo elle bozulmuş / başka bir process'in yazdığı dosyayı simüle eder.
        string json = """
        {
          "C:\\repo\\A.csproj": { "ProjectId": "C:\\repo\\A.csproj", "BuiltSignature": "sig-upper" },
          "c:\\repo\\a.csproj": { "ProjectId": "c:\\repo\\a.csproj", "BuiltSignature": "sig-lower" }
        }
        """;
        File.WriteAllText(StatePath, json);

        var store = new BuildStateStore(_root);
        var map = store.Load(); // throw ATMAMALI

        var surviving = Assert.Single(map);
        Assert.Equal("sig-lower", surviving.Value.BuiltSignature); // JSON'daki SON görülen anahtar kazanır
    }

    [Fact] // Save+Load round-trip: tüm alanlar (LastRunAt/LastDurationMs dahil) tam korunur
    public void Upsert_then_load_round_trips_all_fields_exactly()
    {
        var store = new BuildStateStore(_root);
        var a = new BuildState(
            ProjectId: @"C:\repo\A\A.csproj",
            BuiltSignature: "sig-a-123",
            BuiltCommit: "deadbeef",
            LastResult: BuildResult.Succeeded,
            LastRunAt: new DateTimeOffset(2026, 7, 18, 10, 30, 45, 123, TimeSpan.FromHours(3)),
            LastBranch: "main",
            LastDurationMs: 45_678L);
        var b = new BuildState(
            ProjectId: @"C:\repo\B\B.csproj",
            BuiltSignature: "sig-b-456",
            BuiltCommit: "cafef00d",
            LastResult: BuildResult.Failed,
            LastRunAt: new DateTimeOffset(2026, 7, 18, 11, 0, 0, 0, TimeSpan.Zero),
            LastBranch: "feature/x",
            LastDurationMs: 12_000L);
        var c = new BuildState(@"C:\repo\C\C.csproj", BuiltSignature: null); // minimal / tüm opsiyoneller null

        store.Upsert(a);
        store.Upsert(b);
        store.Upsert(c);

        var map = new BuildStateStore(_root).Load(); // taze örnek — bellek değil disk okunuyor
        Assert.Equal(3, map.Count);

        var la = map[a.ProjectId];
        Assert.Equal(a.ProjectId, la.ProjectId);
        Assert.Equal(a.BuiltSignature, la.BuiltSignature);
        Assert.Equal(a.BuiltCommit, la.BuiltCommit);
        Assert.Equal(a.LastResult, la.LastResult);
        Assert.Equal(a.LastRunAt, la.LastRunAt);
        Assert.Equal(a.LastBranch, la.LastBranch);
        Assert.Equal(a.LastDurationMs, la.LastDurationMs);

        var lb = map[b.ProjectId];
        Assert.Equal(b.BuiltSignature, lb.BuiltSignature);
        Assert.Equal(b.LastResult, lb.LastResult);
        Assert.Equal(b.LastRunAt, lb.LastRunAt);
        Assert.Equal(b.LastDurationMs, lb.LastDurationMs);

        var lc = map[c.ProjectId];
        Assert.Null(lc.BuiltSignature);
        Assert.Null(lc.BuiltCommit);
        Assert.Null(lc.LastResult);
        Assert.Null(lc.LastRunAt);
        Assert.Null(lc.LastBranch);
        Assert.Null(lc.LastDurationMs);
    }

    [Fact] // var olan projectId'nin Upsert'i o kaydı değiştirir, diğerleri dokunulmaz kalır
    public void Upsert_of_existing_project_overwrites_only_that_entry()
    {
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState("P1", "sig1", LastDurationMs: 100));
        store.Upsert(new BuildState("P2", "sig2", LastDurationMs: 200));

        store.Upsert(new BuildState("P1", "sig1-updated", LastDurationMs: 999));

        var map = store.Load();
        Assert.Equal(2, map.Count);
        Assert.Equal("sig1-updated", map["P1"].BuiltSignature);
        Assert.Equal(999, map["P1"].LastDurationMs);
        Assert.Equal("sig2", map["P2"].BuiltSignature); // dokunulmadı
        Assert.Equal(200, map["P2"].LastDurationMs);
    }

    [Fact] // single-writer: çok sayıda eşzamanlı Upsert serialize edilir, hiçbiri kaybolmaz
    public async Task Concurrent_upserts_from_many_projects_are_all_persisted_without_loss()
    {
        var store = new BuildStateStore(_root);
        const int count = 30;

        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(() => store.Upsert(new BuildState($"P{i}", $"sig{i}", LastDurationMs: i))))
            .ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30)); // blocking değil, awaited — poll yok

        var map = new BuildStateStore(_root).Load();
        Assert.Equal(count, map.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.True(map.TryGetValue($"P{i}", out var s), $"P{i} kayboldu");
            Assert.Equal($"sig{i}", s.BuiltSignature);
            Assert.Equal(i, s.LastDurationMs);
        }
    }

    [Fact] // atomiklik: reader, writer eşzamanlı yazarken hiçbir zaman yarım/bozuk JSON görmez (D8: Barrier ile deterministik overlap, sleep yok)
    public async Task Concurrent_reads_never_observe_partial_or_corrupt_json_while_writing()
    {
        Directory.CreateDirectory(_root);
        var store = new BuildStateStore(_root);
        string bigSig = new string('x', 100_000); // yeteri kadar büyük payload — yazım süresi ölçülebilir olsun
        const int rounds = 25;
        var barrier = new System.Threading.Barrier(2);
        Exception? corruption = null;
        Exception? writerFailure = null;
        var roundTimeout = TimeSpan.FromSeconds(10); // her round için üst sınır — partner düşerse sonsuza dek beklemek yerine fail-fast

        var writer = Task.Run(() =>
        {
            for (int r = 0; r < rounds; r++)
            {
                if (!barrier.SignalAndWait(roundTimeout)) return; // reader düştü — sonsuz beklemek yerine çık
                try
                {
                    // AYNI projectId'yi her round'da üzerine yazar — map boyutu sabit kalır (tek büyük kayıt),
                    // amaç map büyümesi değil, tek büyük kaydın atomik rename'i sırasında reader'ın asla yarım
                    // görmemesi; her round'da içerik round numarasıyla değişir (son round'un kazandığı doğrulanabilir).
                    store.Upsert(new BuildState("P", bigSig + "-r" + r, "commit" + r, BuildResult.Succeeded,
                        DateTimeOffset.UtcNow, "main", r));
                }
                catch (Exception ex)
                {
                    writerFailure = ex;
                    return;
                }
            }
        });

        var reader = Task.Run(() =>
        {
            for (int r = 0; r < rounds; r++)
            {
                if (!barrier.SignalAndWait(roundTimeout)) return; // writer düştü — sonsuz beklemek yerine çık
                // writer'ın bu round'daki yazımıyla aynı anda dosyayı doğrudan (Store.Load'un never-throw
                // sarmalayıcısını BYPASS ederek) tekrar tekrar okuyup parse eder — amaç, temp+rename atomikliğini
                // Store'un kendi hata yutan Load()'u ARKASINDAN değil, doğrudan dosya üzerinden doğrulamak.
                for (int i = 0; i < 50; i++)
                {
                    if (!File.Exists(StatePath)) continue;
                    try
                    {
                        byte[] bytes;
                        using (var fs = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var ms = new MemoryStream())
                        {
                            fs.CopyTo(ms);
                            bytes = ms.ToArray();
                        }
                        if (bytes.Length == 0) continue; // dosya henüz ilk kez oluşturulmadan var olabilir (create+rename arası) — geçerli ara durum değil ama zararsız, atla
                        JsonDocument.Parse(bytes); // yarım/bozuk JSON ise fırlatır
                    }
                    catch (IOException)
                    {
                        // paylaşım/erişim çakışması olabilir (rename tam o anda) — bu bozukluk DEĞİL, atla
                    }
                    catch (Exception ex)
                    {
                        corruption = ex;
                    }
                }
            }
        });

        await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(60)); // blocking değil, awaited — poll yok
        Assert.Null(writerFailure);
        Assert.Null(corruption);
        Assert.Equal(bigSig + "-r" + (rounds - 1), store.Load()["P"].BuiltSignature); // son yazan kazandı, kayıp yok
    }

    [Fact] // [Review Important 2] MoveAtomicWithRetry: hedef dosya geçici olarak (Delete-share VERİLMEDEN) kilitliyken
           // Upsert atomik rename'i retry ile başarır. D8: koordinasyon TCS ile — lock ALINDIĞI kesin bilinene kadar
           // beklenir (poll yok). [Flaky-test hardening] Eskiden lock-holder kilidi SABİT Task.Delay(40) sonra
           // bırakıyordu: paralel suite yükü altında (xUnit sınıfları paralel koşar) hem bu 40ms hem retry
           // Thread.Sleep(5)'leri gerçek zamanda uzayabiliyor, Upsert bazen 100ms bütçesini tüketmeden kilit
           // bırakılmadan tükeniyordu (282/284 run'da gözlemlenmiş ara sıra fail, izole çalışmada geçiyordu).
           // Artık wall-clock tahmini YOK: internal OnRenameRetry hook'u ile Upsert'in İLK retry'a girdiği an
           // (gerçek gözlemlenen ilerleme) bir TCS ile sinyallenir, lock-holder TAM O ANDA kilidi bırakır — bir
           // sonraki rename denemesi başarılı olur. Makine ne kadar yavaş/hızlı olursa olsun deterministik.
    public async Task Upsert_retries_and_succeeds_when_target_file_is_transiently_locked_without_delete_share()
    {
        Directory.CreateDirectory(_root);
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState("Seed", "seed-sig")); // hedef dosyayı önceden var eder (kilitlenecek dosya)

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRetryObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Yalnız BİR kez sinyallemek yeterli — ilk retry gözlemlenir gözlemlenmez kilit bırakılacak, sonraki
        // retry denemeleri zaten olmayacak (ya da olsa da TrySetResult ikinci çağrıda no-op).
        store.OnRenameRetry = _ => firstRetryObserved.TrySetResult();

        var lockHolder = Task.Run(async () =>
        {
            // FileShare.Read (Delete YOK): File.Move(overwrite:true) hedefi silmek/değiştirmek zorunda olduğundan
            // bu handle açıkken sharing-violation ile başarısız olur — MoveAtomicWithRetry'ın retry yolunu tetikler.
            using var fs = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            lockAcquired.SetResult();
            await firstRetryObserved.Task; // Upsert'in İLK retry'a girdiği, GÖZLEMLENEN an — wall-clock tahmini yok
            // fs Dispose burada (using bloğu sonunda) kilidi bırakır; bir sonraki rename denemesi başarılı olur.
        });

        await lockAcquired.Task; // dosya kilidi KESİN alınmış — deterministik senkronizasyon noktası, poll yok

        var upsertTask = Task.Run(() => store.Upsert(new BuildState("P1", "sig1")));

        await Task.WhenAll(lockHolder, upsertTask).WaitAsync(TimeSpan.FromSeconds(10));

        var map = new BuildStateStore(_root).Load();
        Assert.Equal("sig1", map["P1"].BuiltSignature);
        Assert.Equal("seed-sig", map["Seed"].BuiltSignature); // kilit sırasında var olan diğer kayıt kaybolmadı
    }

    [Fact] // [Review Important 2, optional] MoveAtomicWithRetry retry bütçesini (20 deneme) aşarsa Upsert net bir
           // IOException/UnauthorizedAccessException ile fırlar — concern #2 (kalıcı kilit) belgeleniyor. Ayrıca
           // [Review Minor 4] doğrulaması: throw sonrası tmp dosyası öksüz kalmamalı. D8: lock hiç serbest
           // bırakılmadan Upsert'in exhaust olup fırlamasını doğrudan await ediyoruz — poll yok, tek bekleme noktası.
    public async Task Upsert_throws_and_cleans_up_tmp_file_when_target_stays_locked_past_retry_budget()
    {
        Directory.CreateDirectory(_root);
        var store = new BuildStateStore(_root);
        store.Upsert(new BuildState("Seed", "seed-sig"));

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseLock = new ManualResetEventSlim(false);

        var lockHolder = Task.Run(() =>
        {
            using var fs = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            lockAcquired.SetResult();
            releaseLock.Wait(TimeSpan.FromSeconds(10)); // retry bütçesi tükenene kadar SERBEST BIRAKILMAZ
        });

        await lockAcquired.Task;

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => Task.Run(() => store.Upsert(new BuildState("P2", "sig2"))))
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(thrown is IOException or UnauthorizedAccessException, $"beklenmeyen exception tipi: {thrown.GetType()}");

        releaseLock.Set();
        await lockHolder.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(Directory.GetFiles(_root, "*.tmp")); // [Minor 4] öksüz tmp kalmamalı
        Assert.DoesNotContain("P2", new BuildStateStore(_root).Load().Keys); // başarısız Upsert veri bırakmadı
    }
}
