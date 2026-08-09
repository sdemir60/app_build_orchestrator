using System.IO;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>Geçici bir dizin — <c>using</c> ömrü bitince kaskatla silinir (persist round-trip testleri için).</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "bo-uistate-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* CI'da kilitli dosya — sızıntı testin sonucunu etkilemez */ }
    }
}

/// <summary>
/// [T35] <see cref="UiState"/>'in 2×2 yerleşim alanlarıyla genişlemesi JSON store round-trip'inden geçmeli;
/// mevcut kabuk alanları (TrayBalloonShown/Hotkey) bozulmamalı (şema genişlemesi geriye dönük tolere edilir).
/// </summary>
public class UiStateStoreTests
{
    [Fact]
    public void Layout_survives_a_store_round_trip()
    {
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
        var state = store.Load();
        state.LayoutMode = LayoutMode.Focus; state.ColPct = 61; state.LeftPct = 33; state.RightPct = 76;
        store.Save(state);
        var reloaded = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json")).Load();
        Assert.Equal(LayoutMode.Focus, reloaded.LayoutMode);
        Assert.Equal(61, reloaded.ColPct);
        Assert.True(reloaded.TrayBalloonShown == false && reloaded.Hotkey == "Alt+B");  // mevcut alanlar bozulmadi
    }

    [Fact]
    public void Workflow_preferences_survive_a_store_round_trip()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        var store = new JsonUiStateStore(path);
        var state = store.Load();
        state.RepositoryRoot = @"D:\src\osys"; state.Configuration = "Debug"; state.PerfMode = "Full";
        state.Branch = "feature/x"; state.UseWorktree = true; state.WorktreeName = "feature-x-1";
        // [D7] LayerPatterns artık List<LayerPattern> (Order/Regex/Name) — eskiden List<string>'ti.
        state.LayerPatterns = [new LayerPattern(0, "OSYS.*.Core", "Core"), new LayerPattern(1, "OSYS.Web.*", "Web")];
        state.Autostart = true;
        store.Save(state);

        var reloaded = new JsonUiStateStore(path).Load();
        Assert.Equal(@"D:\src\osys", reloaded.RepositoryRoot);
        Assert.Equal("Debug", reloaded.Configuration);
        Assert.Equal("Full", reloaded.PerfMode); // [D6] PerfMode artık string ("Full"/"Balanced"/"Light")
        Assert.Equal("feature/x", reloaded.Branch);
        Assert.True(reloaded.UseWorktree);
        Assert.Equal("feature-x-1", reloaded.WorktreeName);
        Assert.Equal([new LayerPattern(0, "OSYS.*.Core", "Core"), new LayerPattern(1, "OSYS.Web.*", "Web")], reloaded.LayerPatterns);
        Assert.True(reloaded.Autostart);
    }

    /// <summary>[Task 11] Kill switch'in KALICI yüzeyi. Üç iddia:
    /// (a) ÜRÜN varsayılanı AÇIK — hiç dokunulmamış bir <see cref="UiState"/> döngüleri derler;
    /// (b) kapatma round-trip'ten sağ çıkar (aksi halde anahtar her açılışta kendiliğinden geri açılırdı);
    /// (c) alanı hiç TAŞIMAYAN (bu sürümden önce yazılmış) bir ui-state.json AÇIK okunur — mevcut kullanıcılar
    /// yükseltmede özelliği kapalı bulmaz. (c) sessizce bozulabilir: alan <c>= true</c> initializer'ı yerine
    /// düz <c>bool</c> olarak yazılırsa (a) ve (b) yeşil kalır ama (c) kırmızıya döner.</summary>
    [Fact]
    public void Build_dependency_cycles_defaults_on_and_survives_a_store_round_trip()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");

        Assert.True(new UiState().BuildDependencyCycles);        // (a) ürün varsayılanı: AÇIK

        var store = new JsonUiStateStore(path);
        var state = store.Load();
        Assert.True(state.BuildDependencyCycles);                // dosya YOKken de açık
        state.BuildDependencyCycles = false;
        state.ColPct = 61;
        store.Save(state);

        var reloaded = new JsonUiStateStore(path).Load();        // (b) kapatma kalıcı
        Assert.False(reloaded.BuildDependencyCycles);
        Assert.Equal(61, reloaded.ColPct);                       // komşu alanlar bozulmadı
    }

    [Fact] // (c) Alanı hiç taşımayan ESKİ bir ui-state.json: eksik alan → ürün varsayılanı (AÇIK).
    public void A_ui_state_written_before_the_switch_existed_loads_with_cycles_enabled()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{ "ColPct": 61, "Branch": "feature/x", "UseWorktree": true }""");

        var reloaded = new JsonUiStateStore(path).Load();

        Assert.True(reloaded.BuildDependencyCycles);
        Assert.Equal(61, reloaded.ColPct);       // non-vacuous: dosya GERÇEKTEN okundu (varsayılana düşmedi)
        Assert.Equal("feature/x", reloaded.Branch);
    }

    [Fact] // [D6 fold] PerfMode bool→string? göçü: diskteki eski bool token'ı TÜM Load'u devirmemeli (startup wipe YOK).
    public void A_legacy_boolean_perf_mode_on_disk_is_tolerated_and_the_rest_survives()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        // Eski şema (PerfMode bir BOOL'du) + kalıcı yerleşim/tercih alanları:
        File.WriteAllText(path,
            """{ "ColPct": 61, "LeftPct": 33, "PerfMode": false, "Branch": "feature/x", "UseWorktree": true }""");

        var reloaded = new JsonUiStateStore(path).Load();

        Assert.Equal(61, reloaded.ColPct);        // yerleşim korundu (bayat token Load'u DEVİRMEDİ)
        Assert.Equal(33, reloaded.LeftPct);
        Assert.Null(reloaded.PerfMode);           // legacy bool → null (VM Balanced/4 varsayılanı korunur)
        Assert.Equal("feature/x", reloaded.Branch);
        Assert.True(reloaded.UseWorktree);
    }

    [Fact] // [D7 şema göçü] LayerPatterns List<string>→List<LayerPattern>: diskteki eski değer HEP boş `[]`'ti
    public void A_legacy_empty_layer_patterns_array_on_disk_round_trips_without_wiping_the_rest()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        // Eski şemada yazılmış (LayerPatterns hep boş kalmıştı) + kalıcı yerleşim:
        File.WriteAllText(path, """{ "ColPct": 61, "LayerPatterns": [], "Branch": "feature/x" }""");

        var reloaded = new JsonUiStateStore(path).Load();

        Assert.Equal(61, reloaded.ColPct);         // boş dizi Load'u DEVİRMEDİ (startup wipe YOK)
        Assert.Empty(reloaded.LayerPatterns);      // [] → boş List<LayerPattern>
        Assert.Equal("feature/x", reloaded.Branch);
    }

    [Fact] // [D7 re-review][Fix7] Yukarıdaki test yalnız boş `[]`'ı sınar (List<string> ve List<LayerPattern>
    // için AYNI şekilde deserialize olur — göçü GERÇEKTEN egzersiz etmez). Diskte DOLU bir eski
    // List<string> HİÇ var olmadı (D7, alanın İLK yazıcısı — yukarıdaki test bunu belgeler); burada
    // varsayımsal olarak öyle bir değer olsaydı GERÇEK davranış PİNLENİR: string dizisi elemanları
    // List<LayerPattern>'e (obje bekleyen) deserialize edilemez → JsonException → Load() TÜM state'i
    // varsayılana DÜŞÜRÜR (PerfMode'daki gibi toleranslı bir converter YOK — kapsam dışı, brief D7).
    public void A_legacy_non_empty_string_array_layer_patterns_on_disk_is_a_type_mismatch_and_wipes_the_whole_state()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path,
            """{ "ColPct": 61, "LayerPatterns": ["OSYS.*.Core"], "Branch": "feature/x" }""");

        var reloaded = new JsonUiStateStore(path).Load();

        // Wipe: JsonException devraldı → varsayılan UiState (kalıcı yerleşim/tercih KAYBOLDU).
        Assert.Equal(50, reloaded.ColPct);       // varsayılan (61 DEĞİL — Load() new UiState() döndü)
        Assert.Null(reloaded.Branch);            // varsayılan (feature/x DEĞİL)
        Assert.Empty(reloaded.LayerPatterns);    // varsayılan (= [])
    }

    [Fact] // [D7 re-review][Fix2] Açık bir JSON "null" token'ı System.Text.Json'da `= []` initializer'ını EZER
    // ve alanı GERÇEK null yapar (JsonException FIRLAMAZ — Load() kendisi güvenli). Çöken taraf MainWindow'un
    // `saved.LayerPatterns.Count` null-safe OLMAYAN okumasıydı (ayrı satır düzeltmesi — burada yalnız Load()'un
    // kendisinin patlamadığı belgelenir).
    public void A_null_layer_patterns_token_loads_without_throwing_and_the_field_is_null()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        File.WriteAllText(path, """{ "ColPct": 61, "LayerPatterns": null, "Branch": "feature/x" }""");

        var reloaded = new JsonUiStateStore(path).Load(); // FIRLAMAMALI

        Assert.Equal(61, reloaded.ColPct);      // diğer alanlar korunur (Load() TÜM state'i devirmedi)
        Assert.Equal("feature/x", reloaded.Branch);
        Assert.Null(reloaded.LayerPatterns);    // açık null → alan GERÇEKTEN null (initializer EZİLDİ)
    }
}
