using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D7/T66] Settings diyaloğu — LAYERS editör VM'i (<see cref="SettingsDraftViewModel"/>). Saf/WPF'siz [Fact]'ler:
/// Save-validation kuralı, taslak commit/rollback (Cancel), ve Save'in BİREBİR konsol notu + persistence'ı.
/// RunViewModel D8 desenine göre kurulur (EngineHost hiç başlatılmaz — VM'in AppendRunLine yolu engine'e
/// dokunmaz).
///
/// <para>[A13/T3 fix-1 · C10] Bu sınıfta <b>WPF YOKTUR</b> ve sınıf-düzeyi <c>[Collection]</c> KALDIRILDI:
/// T3a iki <c>StaFact</c> eklerken tüm sınıfı seri koleksiyona sokmuş, beş saf <c>[Fact]</c>'i de gereksizce
/// paralellikten çıkarmıştı. Realize edilen (StaFact) kalemler artık <see cref="SettingsDialogViewTests"/>'te.</para>
/// </summary>
public class SettingsDialogTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>[fix-1 · C13] Bellek-içi UiStateStore tek yerde: <see cref="SettingsDialogHost.FakeStore"/>.</summary>
    private static SettingsDialogHost.FakeStore NewStore() => new();

    [Fact]
    public void Save_is_blocked_only_by_an_empty_name_or_an_uncompilable_regex_never_by_an_empty_pattern()
    {
        var editor = new SettingsDraftViewModel(null);
        // [değişti] Taze taslak ARTIK 4 varsayılan satırla gelir (LayerDefaults). Bu testin konusu
        // Save-validation'dır — tek satırlık bir zeminde ölçülür, o yüzden varsayılanlar önce boşaltılır.
        for (int i = editor.Layers.Count - 1; i >= 0; i--) editor.RemoveLayer(editor.Layers[i]);

        // [D7 re-review][Fix6] Save butonunun IsEnabled bağlaması CanSave'in PropertyChanged YAYIMLADIĞINA
        // dayanır (XAML: IsEnabled="{Binding CanSave}") — bu olmadan buton canlı GÜNCELLENMEZ (yalnız ilk
        // bind anındaki değerde donar). Her tetikleyicide (Add/Name/Regex-geçersiz/Remove) bir bildirim sayılır.
        int canSaveNotifications = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsDraftViewModel.CanSave)) canSaveNotifications++;
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

    [Fact] // Kayıtlı katman YOKKEN taslak varsayılanlarla DOLU gelir — kullanıcı hiç uğraşmadan Save diyebilsin.
    public async Task A_fresh_draft_is_prefilled_with_the_default_layers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        Assert.Null(run.LayerPatterns); // kayıtlı katman yok

        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        Assert.Equal(
            ["OSYS.Types", "OSYS.Business", "OSYS.Orchestration", "OSYS.UI"],
            draft.Layers.Select(r => r.Name));
        Assert.Equal(@"^OSYS\.Types\.", draft.Layers[0].Regex);

        // Taslağın dolu gelmesi tek başına HİÇBİR ŞEY uygulamaz/kaydetmez — açılışta seed YOKtur.
        Assert.Null(run.LayerPatterns);
        Assert.Empty(store.State.LayerPatterns);
    }

    [Fact] // Kayıtlı katman VARSA taslak onların kopyasıdır — varsayılan kullanıcının tanımlarını ASLA ezmez.
    public async Task A_draft_built_from_saved_layers_never_shows_the_defaults()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        run.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")];

        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        var row = Assert.Single(draft.Layers);
        Assert.Equal("Alpha", row.Name);
        Assert.Equal("^A", row.Regex);
    }

    [Fact] // "Restore default layers": düzenlenmiş taslağı varsayılanlara döndürür, Save'siz KALICI DEĞİL.
    public async Task Restore_default_layers_replaces_the_draft_without_touching_the_live_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;
        var draft = new SettingsDraftViewModel(run.LayerPatterns);

        draft.RestoreDefaults();

        Assert.Equal(4, draft.Layers.Count);
        Assert.Equal("OSYS.Types", draft.Layers[0].Name);
        Assert.Equal("OSYS.UI", draft.Layers[3].Name);
        Assert.Same(live, run.LayerPatterns);        // canlı pattern'lere DOKUNULMADI
        Assert.Empty(store.State.LayerPatterns);     // diske yazılmadı
    }

    [Fact]
    public async Task Cancel_discards_the_draft()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;

        // Diyalog taslağı canlı pattern'lerin KOPYASI üzerinde çalışır.
        var editor = new SettingsDraftViewModel(run.LayerPatterns);
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

    /// <summary>Save: BİREBİR konsol notu + <see cref="RunViewModel.LayerPatterns"/> + UiState persist'i.
    /// <para><b>Eski iddia (değişti):</b> bu test "Load sample layers"in 6 örnek katmanını
    /// (<c>Layer 0 — Core</c> / <c>^OSYS\.(Base$|Common\.)</c>) pinliyordu. Örnek katmanlar kaldırıldı,
    /// yerlerini OSYS varsayılanları (<see cref="LayerDefaults"/>, 4 katman) aldı; pinlenen kural aynı —
    /// Save notu, pattern sırası ve persist şekli.</para></summary>
    [Fact]
    public async Task Saving_layers_writes_the_exact_console_note_and_persists_the_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();

        var editor = new SettingsDraftViewModel(null); // taze taslak = 4 varsayılan
        Assert.Equal(4, editor.Layers.Count);

        editor.Commit(run, store);

        // (a) BİREBİR konsol notu (BuildApp.jsx:1423).
        Assert.Contains("Layer definitions updated — 4 layers", run.GetRunDocumentText());

        // (b) RunViewModel.LayerPatterns set edildi (Order = 0..3, üstten alta).
        Assert.NotNull(run.LayerPatterns);
        Assert.Equal([0, 1, 2, 3], run.LayerPatterns!.Select(p => p.Order));
        Assert.Equal("OSYS.Types", run.LayerPatterns[0].Name);
        Assert.Equal(@"^OSYS\.Types\.", run.LayerPatterns[0].Regex);

        // (c) UiState'e persist edildi (aynı şekil).
        Assert.Equal(4, store.State.LayerPatterns.Count);
        Assert.Equal(run.LayerPatterns, store.State.LayerPatterns);

        // Emptied → farklı BİREBİR not + persist boşalır.
        var empty = new SettingsDraftViewModel(run.LayerPatterns);
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

        // [A13/T2 · 2.2] Sync ARTIK İKİ komut gönderir (sync + listBranches) — "son gönderilen" yerine TÜMÜ
        // toplanır ve aranan komut TÜRÜNE göre seçilir. Assert GEVŞEMEDİ, KESİNLEŞTİ: Sync'in yeni kökte
        // gittiği hâlâ aynı sıkılıkta pinlenir, üstüne envanterin de istendiği eklenir.
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.ChangeRepositoryAsync(@"D:\new\repo");

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.True(run.HasWorkspace);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State)); // durumlar sıfırlandı (hollow)
        var sync = Assert.Single(sent.OfType<SyncWorkspaceCommand>());                 // otomatik Sync gönderildi
        Assert.Equal(@"D:\new\repo", sync.RootPath);                                   // yeni kökte
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<ListBranchesCommand>()).RootPath);
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

    [Fact] // Save = senkronize et: yalnız katmanlar değişse (kök AYNI) bile TEK Sync gider ve YENİ pattern'leri taşır.
    public async Task Applying_settings_sends_one_sync_that_carries_the_new_layer_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, @"^OSYS\.Types\.", "OSYS.Types")];
        await run.ApplySettingsAsync(patterns, @"D:\repo"); // kök DEĞİŞMEDİ — Sync yine gider

        var sync = Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Equal(@"D:\repo", sync.RootPath);
        Assert.Same(patterns, sync.LayerPatterns);   // SIRA kanıtı: katmanlar Sync'ten ÖNCE uygulandı
        Assert.Same(patterns, run.LayerPatterns);
        Assert.Contains("Layer definitions updated — 1 layers", run.GetRunDocumentText());
    }

    [Fact] // Save kökü de değiştirdiyse: kök yeni, satırlar hollow, Sync YENİ kökte ve TEK.
    public async Task Applying_settings_with_a_new_root_resets_rows_and_syncs_at_the_new_root()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\old" };
        run.OnEvent(new ProjectStartedEvent("r1", @"C:\old\a.csproj", "A"));
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State);

        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"D:\new\repo");

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    [Fact] // Kök HİÇ seçilmemişken Save: katmanlar kaydedilir ama gidecek bir kök yoktur → Sync GİTMEZ.
    public async Task Applying_settings_without_a_repository_root_keeps_the_layers_but_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, null);

        Assert.Same(patterns, run.LayerPatterns);
        Assert.Empty(sent);
    }

    [Fact] // Koşu UÇUŞTAyken Save: katmanlar kaydedilir, kök DEĞİŞMEZ, Sync GİTMEZ (koşan build'in kökü çekilmez).
    public async Task Applying_settings_mid_run_keeps_the_root_and_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", IsStarting = true };
        Assert.True(run.IsMidRunLocked);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\other\repo");

        Assert.Same(patterns, run.LayerPatterns);   // katmanlar YİNE uygulanır (sessizce kaybolmaz)
        Assert.Equal(@"D:\repo", run.RootPath);     // kök değişmedi
        Assert.Empty(sent);
    }

}

/// <summary>
/// [A13/T3a · a2/a3/a9 → fix-1 · B6/C10] Settings diyaloğunun GERÇEKTEN realize edilen (WPF) kalemleri.
/// Kurulum <see cref="SettingsDialogHost"/>'tadır (tek yer); saf VM testleri <see cref="SettingsDialogTests"/>'te
/// ve orası artık seri koleksiyonda DEĞİL.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class SettingsDialogViewTests
{
    /// <summary>[A13/T3a · a2/a3] design-v1 §2.9 BİREBİR: <c>LAYERS</c> caps başlığı, açıklama cümlesi ("Other"
    /// mono Run'la BİRLEŞİK okunur — <c>TextBlock.Text</c> tüm Inline'ları düzleştirir) ve boş-katman kesikli
    /// kutu metni. Kutu METNİ burada pinlenir; GÖRÜNÜRLÜK kuralı
    /// <c>Empty_state_box_appears_only_after_every_layer_row_is_deleted</c>'tedir.</summary>
    [StaFact]
    public void Settings_dialog_pins_the_layers_caption_description_and_empty_state_box_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var blocks = DsResources.RealizedObjects(dialog).OfType<TextBlock>().ToList();
        var texts = blocks.Select(t => t.Text).ToList();
        Assert.Contains("LAYERS", texts);

        // description TextBlock 3 <Run>'dan kurulu — headless'ta TextBlock.Text (ContentStart/End tabanlı)
        // Inlines'ı yansıtmaz; Run'lar doğrudan birleştirilir (aynı okunabilir metin, farklı okuma yolu).
        string description = string.Concat(
            blocks.Single(b => b.Inlines.Count == 3).Inlines.OfType<Run>().Select(r => r.Text));
        Assert.Equal(
            "Projects are grouped by the first matching pattern (regex on the project name), top to bottom; " +
            "card order is the layer order in the list. Non-matching projects fall under Other.",
            description);

        Assert.Contains("No layers yet — projects show as a single list in build order.", texts);
    }

    /// <summary>Boş-durum kutusu ARTIK taze diyalogda görünmez: taslak varsayılanlarla dolu açılır. Kutu
    /// yalnız kullanıcı TÜM satırları silince ortaya çıkar.
    /// <para><b>Eski iddia (değişti):</b> <c>Settings_dialog_pins_the_layers_caption_description_and_empty_state_box_verbatim</c>
    /// kutuyu "katman yokken (taze LayerPatterns null) görünür" diye pinliyordu. Varsayılan taslak geldiğinden
    /// taze diyalogda 4 satır vardır; kuralın kendisi (satır yoksa kutu) korunur, tetikleyicisi değişti.</para></summary>
    [StaFact]
    public void Empty_state_box_appears_only_after_every_layer_row_is_deleted()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var box = DsResources.RealizedObjects(dialog).OfType<Grid>().Single(g => g.Name == "EmptyState");
        Assert.Equal(Visibility.Collapsed, box.Visibility); // taze diyalog: 4 varsayılan satır var

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        for (int i = draft.Layers.Count - 1; i >= 0; i--) draft.RemoveLayer(draft.Layers[i]);
        dialog.UpdateLayout();

        Assert.Equal(Visibility.Visible, box.Visibility);
    }

    /// <summary>[A13/T3a · a9] design-v1 §2.9: <c>Add layer</c> (ghost, ikon+etiket) · <c>Cancel</c> · <c>Save</c>
    /// (primary) · <c>Restore default layers</c> (ghost) — davranışları zaten pinliydi (Save/Cancel/LoadSample
    /// testleri), etiketlerin BİREBİR metni testsizdi.</summary>
    [StaFact]
    public void Settings_dialog_footer_and_add_layer_button_labels_are_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var buttons = DsResources.RealizedObjects(dialog).OfType<Button>().ToList();
        Assert.Contains(buttons, b => Equals(b.Content, "Cancel"));
        Assert.Contains(buttons, b => Equals(b.Content, "Save"));
        Assert.Contains(buttons, b => Equals(b.Content, "Restore default layers"));

        // "Add layer": Content bir StackPanel'dir (ikon + TextBlock) — etiket ayrı aranır.
        var texts = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Add layer", texts);
    }
}
