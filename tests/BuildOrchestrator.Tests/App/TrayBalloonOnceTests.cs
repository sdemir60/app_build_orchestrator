using System.IO;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62/K5] `X` pencereyi KAPATMAZ, tepsiye küçültür; kullanıcı bunu bir kez öğrenmeli — v7 kararı K5:
/// <b>YALNIZ ilk X'te</b> bir <b>OS tray balloon</b> gösterilir (uygulama İÇİ toast design §8'de YASAK).
/// "Tek sefer" bayrağı kalıcıdır (uygulama yeniden başlasa da geri gelmez) — burada hem karar hem
/// kalıcılık test edilir.
/// </summary>
public class TrayBalloonOnceTests
{
    private sealed class InMemoryStore : IUiStateStore
    {
        public UiState State = new();
        public int Saves;
        public UiState Load() => State;
        public void Save(UiState state) { State = state; Saves++; }
    }

    [Fact]
    public void Balloon_is_claimed_on_the_first_close_to_tray_only()
    {
        var store = new InMemoryStore();
        var gate = new FirstCloseBalloonGate(store);

        Assert.True(gate.ClaimShow());
        Assert.False(gate.ClaimShow());
        Assert.False(gate.ClaimShow());
        Assert.Equal(1, store.Saves); // bayrak YALNIZ ilk seferde yazılır
    }

    [Fact]
    public void Flag_survives_a_restart_through_the_json_store()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bo-uistate-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "ui-state.json");
        try
        {
            Assert.True(new FirstCloseBalloonGate(new JsonUiStateStore(path)).ClaimShow());

            // Yeni process = yeni store instance, AYNI dosya → ikinci X'te balloon YOK.
            Assert.False(new FirstCloseBalloonGate(new JsonUiStateStore(path)).ClaimShow());
            Assert.True(new JsonUiStateStore(path).Load().TrayBalloonShown);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Missing_or_corrupt_state_file_falls_back_to_defaults_without_throwing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bo-uistate-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "ui-state.json");
        try
        {
            Assert.False(new JsonUiStateStore(path).Load().TrayBalloonShown); // dosya hiç yok
            Assert.Equal(HotkeyBinding.DefaultGesture, new JsonUiStateStore(path).Load().Hotkey);

            Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{ bozuk json");

            var state = new JsonUiStateStore(path).Load();
            Assert.False(state.TrayBalloonShown);
            Assert.Equal(HotkeyBinding.DefaultGesture, state.Hotkey);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
