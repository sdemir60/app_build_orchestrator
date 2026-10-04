using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [PERF Faz C/C1 düzeltme 1] <see cref="AtomicFile"/>'ın AKIŞ varyantları — iki büyük defterin (evaluation-cache.json,
/// source-hash-cache.json) okuma/yazım yolu. Metin varyantlarıyla aynı kural: okuma Delete-share'li, rename retry'lı,
/// başarısız yazımda geçici dosya kalmaz. Hiçbir testte uyku/poll yok [D8]: retry gecikmesi enjekte edilir ve tutamak
/// gecikme dikişinden bırakılır (<see cref="LedgerFileProbe.BlockRename"/>).
/// </summary>
public sealed class AtomicFileStreamTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bo-atomicfile-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* test temizliği */ }
    }

    private string Existing(string content = "old")
    {
        string path = Path.Combine(_dir, "ledger.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static Action<Stream> Streams(string text) => stream => stream.Write(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Write_creates_the_directory_and_writes_what_the_callback_streams()
    {
        string path = Path.Combine(_dir, "nested", "deeper", "ledger.json");

        AtomicFile.Write(path, Streams("hello"), _ => { });

        Assert.Equal("hello", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    /// <summary>[son toparlama B2 · O-3] JSON çifti: akışla yazılan değer akıştan aynen okunur; yazım atomik yolu (klasör, temp) kullanır.</summary>
    [Fact]
    public void WriteJson_and_ReadJson_round_trip_through_the_stream_path()
    {
        string path = Path.Combine(_dir, "nested", "ledger.json");
        var value = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var options = new JsonSerializerOptions { WriteIndented = false };

        AtomicFile.WriteJson(path, value, options, _ => { });

        Assert.Equal("{\"a\":1,\"b\":2}", File.ReadAllText(path));
        Assert.Equal(value, AtomicFile.ReadJson<Dictionary<string, int>>(path, options));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    /// <summary>[son toparlama B2 · O-3] Bozuk JSON ÇAĞIRANA yayılır: "bozuk defter = boş defter" kararı defterindir, yardımcının değil.</summary>
    [Fact]
    public void ReadJson_lets_a_corrupt_file_propagate_to_the_caller()
    {
        string path = Existing("{ not json");

        Assert.ThrowsAny<JsonException>(() => AtomicFile.ReadJson<Dictionary<string, int>>(path, new JsonSerializerOptions()));
    }

    [Fact]
    public void Write_replaces_an_existing_file_without_leaving_a_temp_file()
    {
        string path = Existing();

        AtomicFile.Write(path, Streams("new"), _ => { });

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    // Windows, hedefte AÇIK bir tutamak varken — tutamak Delete-share verse BİLE — rename-over'ı reddeder (ölçüldü:
    // FileShare.ReadWrite | FileShare.Delete okuyucusu açıkken File.Move UnauthorizedAccessException verir); tutamak kapanınca
    // geçer. Delete-share'li okuyucu yazıcıyı geciktirebilir ama yazım KAYBOLMAZ: bütçeli retry pencereyi absorbe eder.
    // Okuyucunun ilk retry'da bırakılması "okuyucu bir süre açık kaldı"nın deterministik modelidir (saat ve uyku yok);
    // retry sayısı iddia EDİLMEZ — rename-over'ı açık tutamağa rağmen geçiren bir Windows sürümünde de test doğru kalır.
    [Fact]
    public void A_reader_that_shares_delete_may_delay_the_replace_but_never_loses_it()
    {
        string path = Existing();
        var reader = AtomicFile.OpenReadSharingDelete(path);                      // okuyucu açık

        try { AtomicFile.Write(path, Streams("new"), _ => reader.Dispose()); }    // ilk retry'da okuyucu kapanır
        finally { reader.Dispose(); }

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    // Retry bütçesi bitince: tutamak hiç bırakılmazsa Write ORİJİNAL istisnayla düşer, hedef bozulmaz ve geçici dosya kalmaz
    // (defterler bu istisnayı yutar ve kirli kalır — bkz. defter testleri). FileShare.Read tutamağı rename'i her Windows
    // sürümünde KESİN reddeder.
    [Fact]
    public void A_target_held_past_the_retry_budget_makes_Write_throw_and_leaves_no_temp_file()
    {
        string path = Existing();
        int retries = 0;

        Exception? thrown;
        using (LedgerFileProbe.BlockRename(path))                                 // FileShare.Read, hiç bırakılmaz
            thrown = Record.Exception(() => AtomicFile.Write(path, Streams("new"), _ => retries++));

        Assert.True(thrown is IOException or UnauthorizedAccessException, $"unexpected exception: {thrown?.GetType()}");
        Assert.True(retries > 0);                                                 // retry bütçesi denendi, yine de geçemedi
        Assert.Equal("old", File.ReadAllText(path));                              // hedef bozulmadı
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));                          // geçici dosya öksüz kalmadı
    }

    // (b) Kısa süreli paylaşım ihlali: tutamak ilk retry'da bırakılır → ikinci deneme geçer.
    [Fact]
    public void A_brief_sharing_violation_on_the_target_is_absorbed_by_the_retry()
    {
        string path = Existing();
        using var block = LedgerFileProbe.BlockRename(path);                      // FileShare.Read: rename düşer

        AtomicFile.Write(path, Streams("new"), block.ReleaseOnRetry);             // ilk retry'da tutamak bırakılır

        Assert.Equal(1, block.Retries);                                           // ilk deneme düştü, ikincisi geçti
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void A_failing_writer_leaves_the_target_untouched_and_no_temp_file()
    {
        string path = Existing();

        var thrown = Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.WriteByte(1);                                                  // yarım içerik temp'e düştü
            throw new InvalidOperationException("boom");
        }, _ => { }));

        Assert.Equal("boom", thrown.Message);                                     // ORİJİNAL istisna yayılır
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    // Defterlerin Load testlerinin kullandığı oracle'ın kendisi: DELETE erişimi tutan tutamak varsayılan okuma kipini
    // reddeder, OpenReadSharingDelete'i reddetmez. Bu doğrulanmasa o testler boşuna yeşil kalabilirdi.
    [Fact]
    public void A_delete_access_holder_refuses_the_default_read_mode_but_not_the_sharing_delete_mode()
    {
        string path = Existing();

        using (LedgerFileProbe.HoldDeleteAccess(path))
        {
            var refused = Record.Exception(() => File.OpenRead(path).Dispose());
            Assert.True(refused is IOException or UnauthorizedAccessException, $"expected a sharing violation, got {refused?.GetType()}");

            AtomicFile.OpenReadSharingDelete(path).Dispose();                     // aynı tutamakla uyuşur
        }
    }
}
