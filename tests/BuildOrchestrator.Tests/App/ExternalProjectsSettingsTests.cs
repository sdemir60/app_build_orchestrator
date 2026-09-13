using System.IO;
using System.Linq;
using System.Text.Json;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Harici proje listesinin kalıcılığı: kullanıcının Ayarlar'da sıraladığı liste diskte yaşar ve her açılışta
/// aynı sırayla geri gelir. Eski <c>ui-state.json</c> dosyaları — alan hiç yokken ya da açıkça <c>null</c>
/// yazılmışken — yüklenmeye devam eder; bir bayat token tüm yerleşimi sıfırlayamaz.
/// </summary>
public class ExternalProjectsSettingsTests
{
    private static readonly ExternalProject Mail = new(@"D:\ext\mail");
    private static readonly ExternalProject Ocr = new(@"D:\ext\ocr\Ocr.sln");

    [Fact]
    public void The_external_list_survives_a_store_round_trip_in_order()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        store.Save(new UiState { ExternalProjects = [Ocr, Mail] });

        var loaded = store.Load();

        Assert.Equal([Ocr, Mail], loaded.ExternalProjects);
    }

    [Fact]
    public void A_state_file_written_before_externals_existed_still_loads()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{"ColPct":42,"RepositoryRoot":"D:\\repo"}""");

        var loaded = new JsonUiStateStore(path).Load();

        Assert.Empty(loaded.ExternalProjects);
        Assert.Equal(42, loaded.ColPct);
    }

    [Fact]
    public void An_explicit_null_token_does_not_wipe_the_rest_of_the_layout()
    {
        // Açıkça null yazılmış bir liste alanı, koleksiyonu null bırakıp ilk okumada NullReference'a
        // dönüşebilirdi — o zaman TÜM yerleşim sıfırlanırdı (startup wipe).
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{"ColPct":37,"ExternalProjects":null}""");

        var loaded = new JsonUiStateStore(path).Load();

        Assert.NotNull(loaded.ExternalProjects);
        Assert.Empty(loaded.ExternalProjects);
        Assert.Equal(37, loaded.ColPct);
    }

    [Fact]
    public void The_update_flag_round_trips_when_it_is_turned_off()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        store.Save(new UiState { UpdateExternals = false });

        Assert.False(store.Load().UpdateExternals);
    }

    [Fact]
    public void A_state_file_written_before_the_update_flag_existed_reads_as_not_set()
    {
        // Alan NULLABLE: "hiç yazılmamış" ile "false yazılmış" ayrımı taşınmak zorunda — MainWindow ilkini
        // varsayılan AÇIK olarak seed eder, yani bayrak öncesi bir dosya bugünkü davranışı korur.
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{"ColPct":42}""");

        Assert.Null(new JsonUiStateStore(path).Load().UpdateExternals);
    }

    [Fact]
    public void An_explicit_null_update_flag_does_not_wipe_the_rest_of_the_layout()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{"ColPct":31,"UpdateExternals":null}""");

        var loaded = new JsonUiStateStore(path).Load();

        Assert.Null(loaded.UpdateExternals);
        Assert.Equal(31, loaded.ColPct);
    }

    [Fact]
    public void The_saved_json_uses_the_shared_contract_shape()
    {
        // Diskteki şekil Contracts tipinin kendisidir (yalnız yol) — App-yerel ikinci bir kopya yoktur.
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        new JsonUiStateStore(path).Save(new UiState { ExternalProjects = [Ocr] });

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entry = document.RootElement.GetProperty("ExternalProjects").EnumerateArray().Single();
        Assert.Equal(@"D:\ext\ocr\Ocr.sln", entry.GetProperty("Path").GetString());
        // [DEĞİŞEN KURAL] Eski iddia: kayıt bir `Vcs` alanı da taşıyordu (0=Git, 1=TFVC). TFVC kolu
        // kaldırıldı — alan artık YAZILMAZ; eski dosyalarda görülürse okunurken yok sayılır
        // (bkz. UiStateStoreTests).
        Assert.False(entry.TryGetProperty("Vcs", out _));
    }
}
