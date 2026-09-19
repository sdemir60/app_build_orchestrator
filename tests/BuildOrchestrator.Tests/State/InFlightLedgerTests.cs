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
