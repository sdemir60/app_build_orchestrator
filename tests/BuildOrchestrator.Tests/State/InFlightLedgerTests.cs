using System;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [spec 2026-09-18 §5.5 · karar 12] Uçuştaki projelerin defteri (<c>run-inflight.json</c>) ve açılıştaki çökme
/// kurtarması. Sonraki açılışın gördüğü şey YALNIZ dosyadır — bu yüzden iddialar hep diskten, yeni bir örnekle
/// okunur (bellekteki küme motorla birlikte ölür).
/// </summary>
public sealed class InFlightLedgerTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
    private const string A = @"C:\r\A\A.csproj";
    private const string B = @"C:\r\B\B.csproj";

    public void Dispose() => _dir.Dispose();

    private InFlightLedger NewLedger() => new(_dir.Path);
    private string LedgerPath => Path.Combine(_dir.Path, InFlightLedger.FileName);

    /// <summary>Kilitli bir hedefe atomik rename Windows'ta IOException ya da UnauthorizedAccessException verir.</summary>
    private static void AssertWriteFails(Action write)
    {
        var ex = Record.Exception(write);
        Assert.True(ex is IOException or UnauthorizedAccessException, $"expected an IO failure, got {ex?.GetType().Name ?? "none"}");
    }

    [Fact]
    public void Add_and_remove_mirror_the_set_on_disk_and_clear_deletes_the_file()
    {
        var ledger = NewLedger();

        ledger.Add(A);
        ledger.Add(B);
        Assert.Equal([A, B], [.. NewLedger().ReadListed().Order()]);

        ledger.Remove(A);
        Assert.Equal([B], NewLedger().ReadListed());

        ledger.Clear();
        Assert.False(File.Exists(LedgerPath));
        Assert.Empty(NewLedger().ReadListed());
    }

    /// <summary>Bozuk dosyada kimin uçuşta olduğu BİLİNMEZ: kurtarma uydurulmaz (boş liste), defter hiçbir kayda
    /// dokunmaz ve dosya silinir — bir sonraki açılış aynı bozuk dosyaya takılmaz.</summary>
    [Fact]
    public void A_corrupt_file_recovers_nothing_and_is_deleted()
    {
        var store = new BuildStateStore(_dir.Path);
        store.Upsert(new BuildState(A, "sigA", LastResult: BuildResult.Succeeded, LastRunAt: Now.AddDays(-1)));
        File.WriteAllText(LedgerPath, "{ this is not json");

        var recovered = NewLedger().Recover(store, Now);

        Assert.Empty(recovered);
        Assert.False(File.Exists(LedgerPath));
        Assert.Equal(BuildResult.Succeeded, store.Load()[A].LastResult);
    }

    [Fact]
    public void Recover_marks_each_listed_project_as_an_unevidenced_failure_and_empties_the_file()
    {
        var store = new BuildStateStore(_dir.Path);
        var earlier = Now.AddDays(-1);
        store.Upsert(new BuildState(A, "sigA", LastResult: BuildResult.Succeeded, LastRunAt: earlier));
        store.Upsert(new BuildState(B, "sigB", LastResult: BuildResult.Succeeded, LastRunAt: earlier));
        var crashed = NewLedger();
        crashed.Add(A); // motor A derlenirken öldü — B uçuşta değildi

        var recovered = NewLedger().Recover(store, Now);

        Assert.Equal([A], recovered);
        var a = store.Load()[A];
        Assert.Equal(BuildResult.Failed, a.LastResult);
        Assert.Equal(Now, a.LastRunAt);
        Assert.Null(a.FailedSignature);
        Assert.Equal("sigA", a.BuiltSignature);
        Assert.Equal(BuildResult.Succeeded, store.Load()[B].LastResult); // uçuşta olmayan dokunulmaz
        Assert.False(File.Exists(LedgerPath));
    }

    /// <summary>[Task 10 fix I2] Kurtarma defter yazımında patlarsa (build-state.json kilitli) kurtarılamayan satırlar
    /// AYNI motor ömründe de kaybolmaz: sonraki bir koşunun Add'i ve Clear'ı dosyayı bellekteki kümeyle baştan
    /// yazar ama kurtarılmamışları da taşır — bir sonraki açılış onları yeniden dener.</summary>
    [Fact]
    public void Entries_that_could_not_be_recovered_survive_a_later_run_in_the_same_engine_lifetime()
    {
        var store = new BuildStateStore(_dir.Path) { RenameRetryDelay = _ => { } };
        store.Upsert(new BuildState(A, "sigA", LastResult: BuildResult.Succeeded));
        var crashed = NewLedger();
        crashed.Add(A);
        crashed.Add(B);
        var ledger = NewLedger();

        using (new FileStream(Path.Combine(_dir.Path, "build-state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            AssertWriteFails(() => ledger.Recover(store, Now)); // rename hedefi kilitli → yazım düşer

        const string C = @"C:\r\C\C.csproj";
        ledger.Add(C);   // bu motor ömründe bir koşu başladı
        ledger.Clear();  // ...ve bitti

        Assert.Equal([A, B], [.. NewLedger().ReadListed().Order()]);
    }

    /// <summary>[Task 10 fix I2] Kurtarılamayanlar bir sonraki koşunun başında yeniden denenir: kilit kalkınca kayıt
    /// kanıtsız hata olur ve satır defterden düşer.</summary>
    [Fact]
    public void Retrying_the_recovery_invalidates_what_a_failed_recovery_left_behind()
    {
        var store = new BuildStateStore(_dir.Path) { RenameRetryDelay = _ => { } };
        store.Upsert(new BuildState(A, "sigA", LastResult: BuildResult.Succeeded));
        NewLedger().Add(A);
        var ledger = NewLedger();
        using (new FileStream(Path.Combine(_dir.Path, "build-state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            AssertWriteFails(() => ledger.Recover(store, Now));

        ledger.RetryRecovery(store, Now);

        Assert.Equal(BuildResult.Failed, store.Load()[A].LastResult);
        Assert.Empty(NewLedger().ReadListed());
        Assert.False(File.Exists(LedgerPath));
    }

    /// <summary>[Task 10 fix M4] Yalnız ayrıştırılamayan içerik "bozuk"tur. Dosya OKUNAMIYORSA (kilit, izin) içerik
    /// bilinmez ama bozuk da değildir: istisna çağırana (Program'ın uyarı dalı) yayılır ve dosya yerinde kalır — bir
    /// sonraki açılış yeniden dener.</summary>
    [Fact]
    public void An_unreadable_file_is_not_treated_as_corrupt_and_stays_for_the_next_start()
    {
        var store = new BuildStateStore(_dir.Path);
        NewLedger().Add(A);

        // Delete paylaşılır, Read paylaşılmaz: okuma sharing-violation verir, silme ise GEÇERDİ — bozuk sayılsaydı
        // dosya sessizce silinirdi.
        using (new FileStream(LedgerPath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            Assert.ThrowsAny<IOException>(() => NewLedger().Recover(store, Now));

        Assert.True(File.Exists(LedgerPath));
        Assert.Equal([A], NewLedger().ReadListed());
    }

    /// <summary>Kaydı olmayan proje zaten derlenecektir; kurtarma defterde yeni kayıt AÇMAZ — ama listelenmiştir,
    /// yani konsol sayısına girer (N proje yeniden derlenecek).</summary>
    [Fact]
    public void Recover_without_a_record_opens_none()
    {
        var store = new BuildStateStore(_dir.Path);
        NewLedger().Add(A);

        var recovered = NewLedger().Recover(store, Now);

        Assert.Equal([A], recovered);
        Assert.Empty(store.Load());
        Assert.False(File.Exists(LedgerPath));
    }
}
