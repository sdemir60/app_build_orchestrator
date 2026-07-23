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
}
