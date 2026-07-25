using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D7/T66] Settings diyaloğu — LAYERS editör VM'i (<see cref="LayerEditorViewModel"/>). Saf/WPF'siz [Fact]'ler:
/// Save-validation kuralı, taslak commit/rollback (Cancel), ve Save'in BİREBİR konsol notu + persistence'ı.
/// RunViewModel D8 desenine göre kurulur (EngineHost hiç başlatılmaz — VM'in AppendRunLine yolu engine'e
/// dokunmaz).
/// </summary>
public class SettingsDialogTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>Bellek-içi UiStateStore — persistence round-trip'ini WPF/dosya olmadan gözlemler.</summary>
    private sealed class FakeStore : IUiStateStore
    {
        public UiState State { get; private set; } = new();
        public UiState Load() => State;
        public void Save(UiState state) => State = state;
    }

    [Fact]
    public void Save_is_blocked_only_by_an_empty_name_or_an_uncompilable_regex_never_by_an_empty_pattern()
    {
        var editor = new LayerEditorViewModel(null);

        // [D7 re-review][Fix6] Save butonunun IsEnabled bağlaması CanSave'in PropertyChanged YAYIMLADIĞINA
        // dayanır (XAML: IsEnabled="{Binding CanSave}") — bu olmadan buton canlı GÜNCELLENMEZ (yalnız ilk
        // bind anındaki değerde donar). Her tetikleyicide (Add/Name/Regex-geçersiz/Remove) bir bildirim sayılır.
        int canSaveNotifications = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerEditorViewModel.CanSave)) canSaveNotifications++;
        };

        editor.AddLayer(); // "Layer 1", regex boş
        Assert.True(canSaveNotifications > 0, "Add layer sonrası CanSave bildirimi YOK");
        var row = Assert.Single(editor.Layers);

        // Boş regex GEÇERLİdir → Save bloklanMAZ.
        Assert.Equal("", row.Regex);
        Assert.False(row.RegexInvalid);
        Assert.True(editor.CanSave);

        // Boş ad → bloklar (yalnız boşluk da boş sayılır — trim).
        canSaveNotifications = 0;
        row.Name = "   ";
        Assert.True(canSaveNotifications > 0, "Name değişimi sonrası CanSave bildirimi YOK");
        Assert.False(editor.CanSave);

        // Ad dolu + derlenemeyen regex → bloklar (input invalid).
        row.Name = "Core";
        canSaveNotifications = 0;
        row.Regex = "([";
        Assert.True(row.RegexInvalid);
        Assert.False(editor.CanSave);
        Assert.True(canSaveNotifications > 0, "Regex geçersize dönünce CanSave bildirimi YOK");

        // Regex tekrar boş → yine GEÇERLİ (boş pattern asla bloklamaz).
        row.Regex = "";
        Assert.False(row.RegexInvalid);
        Assert.True(editor.CanSave);

        canSaveNotifications = 0;
        editor.RemoveLayer(row);
        Assert.True(canSaveNotifications > 0, "Remove layer sonrası CanSave bildirimi YOK");
    }

    [Fact]
    public async Task Cancel_discards_the_draft()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = new FakeStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;

        // Diyalog taslağı canlı pattern'lerin KOPYASI üzerinde çalışır.
        var editor = new LayerEditorViewModel(run.LayerPatterns);
        editor.Layers[0].Name = "CHANGED";
        editor.Layers[0].Regex = "^B";
        editor.AddLayer();

        // Cancel = commit YOK → canlı pattern'lere DOKUNULMAZ (taslak atılır).
        Assert.Same(live, run.LayerPatterns);
        Assert.Single(run.LayerPatterns!);
        Assert.Equal("Alpha", run.LayerPatterns![0].Name);
        Assert.Equal("^A", run.LayerPatterns[0].Regex);

        // [D7 re-review][Fix5] Ayrımcı güç kanıtı: AYNI (mutasyona uğramış) taslak ŞİMDİ commit edilirse canlı
        // GERÇEKTEN değişmeli — bu, yukarıdaki "değişmedi" iddiasının taslak/canlı izolasyonunu (Cancel = bu
        // Commit'in YOKLUĞU) test ettiğini kanıtlar; aksi halde ctor'un yeni satır VM'leri kurması nedeniyle
        // aliasing zaten fiziksel olarak imkânsız olduğundan iddia hep-doğru (anlamsız) kalırdı.
        editor.Commit(run, store);
        Assert.Equal(2, run.LayerPatterns!.Count);
        Assert.Equal("CHANGED", run.LayerPatterns[0].Name);
        Assert.Equal("^B", run.LayerPatterns[0].Regex);
    }

    [Fact]
    public async Task Saving_layers_writes_the_exact_console_note_and_persists_the_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = new FakeStore();

        // 6 örnek katman → Save.
        var editor = new LayerEditorViewModel(null);
        editor.LoadSampleLayers();
        Assert.Equal(6, editor.Layers.Count);

        editor.Commit(run, store);

        // (a) BİREBİR konsol notu (BuildApp.jsx:1423).
        Assert.Contains("Layer definitions updated — 6 layers", run.GetRunDocumentText());

        // (b) RunViewModel.LayerPatterns set edildi (Order = 0..5, üstten alta).
        Assert.NotNull(run.LayerPatterns);
        Assert.Equal(6, run.LayerPatterns!.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], run.LayerPatterns.Select(p => p.Order));
        Assert.Equal("Layer 0 — Core", run.LayerPatterns[0].Name);
        Assert.Equal(@"^OSYS\.(Base$|Common\.)", run.LayerPatterns[0].Regex);

        // (c) UiState'e persist edildi (aynı şekil).
        Assert.Equal(6, store.State.LayerPatterns.Count);
        Assert.Equal(run.LayerPatterns, store.State.LayerPatterns);

        // Emptied → farklı BİREBİR not + persist boşalır.
        var empty = new LayerEditorViewModel(run.LayerPatterns);
        for (int i = empty.Layers.Count - 1; i >= 0; i--) empty.RemoveLayer(empty.Layers[i]);
        empty.Commit(run, store);
        Assert.Contains("Layers removed — single project list", run.GetRunDocumentText());
        Assert.Empty(store.State.LayerPatterns);
    }

    [Fact] // [D7 · K10] "Change…": kök değişir, durumlar sıfırlanır, YENİ kökte otomatik Sync başlar.
    public async Task Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        run.OnEvent(new ProjectStartedEvent("r1", @"C:\old\a.csproj", "A")); // eski repo'da bir satır (Started)
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State);

        IpcCommand? sent = null;
        run.DebugOnCommandSent = c => sent = c;

        await run.ChangeRepositoryAsync(@"D:\new\repo");

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.True(run.HasWorkspace);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State)); // durumlar sıfırlandı (hollow)
        var sync = Assert.IsType<SyncWorkspaceCommand>(sent);                          // otomatik Sync gönderildi
        Assert.Equal(@"D:\new\repo", sync.RootPath);                                   // yeni kökte
    }

    [Fact] // [D7 re-review][Fix3] Aynı kökü (case-insensitive — Windows yolu) YENİDEN seçmek no-op olmalı.
    public async Task Repicking_the_current_repository_root_is_a_no_op()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new ProjectStartedEvent("r1", @"D:\repo\a.csproj", "A")); // aktif bir satır (Started)
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State);

        IpcCommand? sent = null;
        run.DebugOnCommandSent = c => sent = c;

        await run.ChangeRepositoryAsync(@"d:\REPO"); // aynı kök, farklı harf durumu

        Assert.Equal(@"D:\repo", run.RootPath);                                    // kök değişmedi
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State);  // satırlar sıfırlanmadı (hollow YOK)
        Assert.Null(sent);                                                         // yeniden Sync GÖNDERİLMEDİ
    }
}
