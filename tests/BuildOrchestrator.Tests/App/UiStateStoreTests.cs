using System.IO;
using BuildOrchestrator.App.Shell;

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
        state.RepositoryRoot = @"D:\src\osys"; state.Configuration = "Debug"; state.PerfMode = true;
        state.Branch = "feature/x"; state.UseWorktree = true; state.WorktreeName = "feature-x-1";
        state.LayerPatterns = ["OSYS.*.Core", "OSYS.Web.*"]; state.Autostart = true;
        store.Save(state);

        var reloaded = new JsonUiStateStore(path).Load();
        Assert.Equal(@"D:\src\osys", reloaded.RepositoryRoot);
        Assert.Equal("Debug", reloaded.Configuration);
        Assert.True(reloaded.PerfMode);
        Assert.Equal("feature/x", reloaded.Branch);
        Assert.True(reloaded.UseWorktree);
        Assert.Equal("feature-x-1", reloaded.WorktreeName);
        Assert.Equal(["OSYS.*.Core", "OSYS.Web.*"], reloaded.LayerPatterns);
        Assert.True(reloaded.Autostart);
    }
}
