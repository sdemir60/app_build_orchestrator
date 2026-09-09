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
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");
    private static readonly ExternalProject Ocr = new("Ocr", @"D:\ext\ocr", @"D:\ext\ocr\Ocr.sln");

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
    public void The_saved_json_uses_the_shared_contract_shape()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        new JsonUiStateStore(path).Save(new UiState { ExternalProjects = [Mail] });

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entry = document.RootElement.GetProperty("ExternalProjects").EnumerateArray().Single();
        Assert.Equal("Mail", entry.GetProperty("Name").GetString());
        Assert.Equal(@"D:\ext\mail\Mail.sln", entry.GetProperty("TargetPath").GetString());
    }
}
