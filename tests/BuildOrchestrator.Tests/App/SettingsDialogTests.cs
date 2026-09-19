using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        // [DEĞİŞEN KURAL — design v1.8.0 §2.9] CanSave'in İKİNCİ koşulu eklendi: repository root BOŞ olamaz
        // ("uygulamanın çalışması için zorunlu tek ayar budur"). Bu testin konusu KATMAN validasyonudur, bu
        // yüzden kök dolu bir zeminde ölçülür — root kuralının kendisi ayrı bir testte pinlenir.
        // [DEĞİŞEN KURAL — design v1.19.0 §2.9] Taze taslak BOŞTUR (OSYS ön-dolumu kalktı) ve Add layer BOŞ satır
        // ekler; eskiden varsayılanlar önce boşaltılıyor, eklenen satır "Layer 1" adıyla Save'i açık bırakıyordu.
        var editor = new SettingsDraftViewModel(null, @"D:\repo");
        Assert.Empty(editor.Layers);

        // [D7 re-review][Fix6] Save butonunun IsEnabled bağlaması CanSave'in PropertyChanged YAYIMLADIĞINA
        // dayanır (XAML: IsEnabled="{Binding CanSave}") — bu olmadan buton canlı GÜNCELLENMEZ (yalnız ilk
        // bind anındaki değerde donar). Her tetikleyicide (Add/Name/Regex-geçersiz/Remove) bir bildirim sayılır.
        int canSaveNotifications = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsDraftViewModel.CanSave)) canSaveNotifications++;
        };

        editor.AddLayer(); // ad ve regex boş
        Assert.True(canSaveNotifications > 0, "Add layer sonrası CanSave bildirimi YOK");
        var row = Assert.Single(editor.Layers);

        // Boş ad bloklar; ad dolunca boş regex GEÇERLİdir → Save bloklanMAZ.
        Assert.False(editor.CanSave);
        row.Name = "Core";
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

    /// <summary>Kayıtlı katman YOKKEN taslak BOŞTUR — Layers sayfası boş-durum kutusuyla açılır.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9, kullanıcı kararı 2026-09-16]</b> ESKİ İDDİA
    /// (<c>A_fresh_draft_is_prefilled_with_the_default_layers</c>): taslak dört OSYS varsayılanıyla
    /// (<c>OSYS.Types</c> … <c>OSYS.UI</c>, <c>LayerDefaults</c>) DOLU gelirdi. Gerekçe: araç ürüne özel bir ön-dolum
    /// taşımaz; yeni satırlar yalnız ürün-bağımsız PLACEHOLDER gösterir (<see cref="LayerPlaceholders"/>).</para></summary>
    [Fact]
    public async Task A_fresh_draft_has_no_layers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.Null(run.LayerPatterns); // kayıtlı katman yok

        var draft = new SettingsDraftViewModel(run.LayerPatterns, null);

        Assert.Empty(draft.Layers);
    }

    /// <summary>Kayıtlı liste BOŞ ama null DEĞİL ("tüm katmanları sil + Save" sonrası canlı durum) — taslak yine
    /// BOŞ açılır.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> ESKİ İDDİA
    /// (<c>A_draft_built_from_an_emptied_layer_list_still_shows_the_defaults</c>): boş liste null gibi
    /// varsayılanları gösterirdi. Ön-dolum kalktığı için ikisi de boş taslaktır.</para></summary>
    [Fact]
    public void A_draft_built_from_an_emptied_layer_list_is_empty()
    {
        IReadOnlyList<LayerPattern> emptied = []; // "hepsini sil + Save" sonrası RunViewModel.LayerPatterns

        Assert.Empty(new SettingsDraftViewModel(emptied, null).Layers);
    }

    /// <summary>[design v1.19.0 §2.9] <c>Add layer</c> BOŞ bir satır ekler (ad ve desen <c>""</c>).
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ davranış satırı <c>Layer N</c> adıyla eklerdi (N = yeni satır sayısı);
    /// artık değer değil placeholder gösterilir.</para></summary>
    [Fact]
    public void Add_layer_appends_an_empty_row()
    {
        var draft = new SettingsDraftViewModel([new LayerPattern(0, "^A", "Alpha")], @"D:\repo");

        draft.AddLayer();

        Assert.Equal(2, draft.Layers.Count);
        Assert.Equal("", draft.Layers[1].Name);
        Assert.Equal("", draft.Layers[1].Regex);
    }

    /// <summary>[design v1.19.0 §2.9] Satır placeholder'larının TEK kaynağı — ürün adı taşımayan standart katman
    /// iskeleti, sıra ve metin birebir (<c>LAYER_PLACEHOLDERS</c>).</summary>
    [Fact]
    public void Layer_placeholders_are_the_six_product_neutral_pairs_in_order()
    {
        Assert.Equal(
            [
                ("Core", @"^MyApp\.(Core|Common)\."),
                ("Infrastructure", @"^MyApp\.(Data|Infrastructure)\."),
                ("Domain", @"^MyApp\.Domain\."),
                ("Services", @"^MyApp\.Services\."),
                ("Api", @"\.Api$"),
                ("Client", @"^MyApp\.(Web|Client|Mobile)\."),
            ],
            LayerPlaceholders.Pairs);
    }

    /// <summary>[design v1.19.0 §2.9] Her satır placeholder'ını SATIR İNDEKSİNE göre alır ve 6'dan sonra başa
    /// döner (7. satır = 1. çift).</summary>
    [Fact]
    public void Layer_rows_take_their_placeholders_from_the_row_index_and_wrap_after_six()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        for (int i = 0; i < 7; i++) draft.AddLayer();

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(LayerPlaceholders.Pairs[i].Name, draft.Layers[i].NamePlaceholder);
            Assert.Equal(LayerPlaceholders.Pairs[i].Pattern, draft.Layers[i].PatternPlaceholder);
        }
        Assert.Equal("Core", draft.Layers[6].NamePlaceholder);
        Assert.Equal(@"^MyApp\.(Core|Common)\.", draft.Layers[6].PatternPlaceholder);
    }

    /// <summary>[design v1.19.0 §2.9] Placeholder satıra değil İNDEKSE aittir: sürükle-bırak (<c>Move</c>) ya da
    /// silme sonrası her satır YENİ indeksinin çiftini gösterir.</summary>
    [Fact]
    public void Reordering_or_removing_layers_moves_the_placeholders_to_the_new_indexes()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.AddLayer();
        draft.AddLayer();
        draft.AddLayer();
        var first = draft.Layers[0];

        draft.Layers.Move(0, 2); // DragReorderBehavior'ın kullandığı bildirim

        Assert.Same(first, draft.Layers[2]);
        Assert.Equal("Domain", first.NamePlaceholder);
        Assert.Equal("Core", draft.Layers[0].NamePlaceholder);

        draft.RemoveLayer(draft.Layers[0]);
        Assert.Equal("Infrastructure", first.NamePlaceholder); // artık indeks 1
    }

    /// <summary>[design v1.19.0 §2.9] Save kapalıyken footer'ın tek satırlık NEDENİ — CanSave'in AYNI koşullarından,
    /// öncelik sırasıyla: kök → harici path → katman adı → desen. Save açıkken neden YOKTUR.</summary>
    [Fact]
    public void The_save_blocked_reason_follows_the_design_priority_and_clears_when_save_is_allowed()
    {
        var draft = new SettingsDraftViewModel([new LayerPattern(0, "([", "")], null, [new ExternalProject("")]);
        var reasons = new List<string?>();
        draft.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsDraftViewModel.SaveBlockedReason)) reasons.Add(draft.SaveBlockedReason);
        };

        Assert.Equal("Repository root is required", draft.SaveBlockedReason);
        Assert.False(draft.CanSave);

        draft.RepositoryRoot = @"D:\repo";
        Assert.Equal("Every external project needs a path", draft.SaveBlockedReason);

        draft.Externals[0].Path = @"C:\a";
        Assert.Equal("Every layer needs a name", draft.SaveBlockedReason);

        draft.Layers[0].Name = "Core";
        Assert.Equal("Check the highlighted pattern", draft.SaveBlockedReason);

        draft.Layers[0].Regex = "^A";
        Assert.Null(draft.SaveBlockedReason);
        Assert.True(draft.CanSave);

        // Her geçiş bildirildi — footer satırı canlı güncellenir.
        Assert.Equal(
            ["Every external project needs a path", "Every layer needs a name", "Check the highlighted pattern", null],
            reasons.Distinct());
    }

    [Fact] // Kayıtlı katman VARSA taslak onların kopyasıdır — varsayılan kullanıcının tanımlarını ASLA ezmez.
    public async Task A_draft_built_from_saved_layers_never_shows_the_defaults()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        run.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")];

        var draft = new SettingsDraftViewModel(run.LayerPatterns, null);

        var row = Assert.Single(draft.Layers);
        Assert.Equal("Alpha", row.Name);
        Assert.Equal("^A", row.Regex);
    }

    [Fact]
    public async Task Cancel_discards_the_draft()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = NewStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;

        // Diyalog taslağı canlı pattern'lerin KOPYASI üzerinde çalışır.
        // [genişletildi] Cancel artık repo seçimini de atar: taslak kökü değişse bile canlı kök DOKUNULMAZ.
        var editor = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath) { RepositoryRoot = @"D:\new\repo" };
        editor.Layers[0].Name = "CHANGED";
        editor.Layers[0].Regex = "^B";
        editor.AddLayer();

        // Cancel = commit YOK → canlı pattern'lere DOKUNULMAZ (taslak atılır).
        Assert.Same(live, run.LayerPatterns);
        Assert.Single(run.LayerPatterns!);
        Assert.Equal("Alpha", run.LayerPatterns![0].Name);
        Assert.Equal("^A", run.LayerPatterns[0].Regex);
        Assert.Equal(@"D:\repo", run.RootPath); // Commit çağrılmadı → kök eski

        // [D7 re-review][Fix5] Ayrımcı güç kanıtı: AYNI (mutasyona uğramış) taslak ŞİMDİ commit edilirse canlı
        // GERÇEKTEN değişmeli — bu, yukarıdaki "değişmedi" iddiasının taslak/canlı izolasyonunu (Cancel = bu
        // Commit'in YOKLUĞU) test ettiğini kanıtlar; aksi halde ctor'un yeni satır VM'leri kurması nedeniyle
        // aliasing zaten fiziksel olarak imkânsız olduğundan iddia hep-doğru (anlamsız) kalırdı.
        // Commit KÖKÜ de taşır: aynı ayrımcı kanıt bekleyen repo kökü için de gerekir (aksi halde "Commit
        // çağrılmadı → kök eski" iddiası, kökü hiç uygulamayan bir Commit'te de yeşil kalırdı). Commit'in
        // sürdüğü Sync gönderimi engine hiç başlatılmadığı için hataya düşer ve TrySendAsync onu yutar —
        // burada gözlenen tek etki kökün kendisidir.
        await editor.CommitAsync(run, store);
        Assert.Equal(2, run.LayerPatterns!.Count);
        Assert.Equal("CHANGED", run.LayerPatterns[0].Name);
        Assert.Equal("^B", run.LayerPatterns[0].Regex);
        Assert.Equal(@"D:\new\repo", run.RootPath);
    }

    /// <summary>Save: BİREBİR konsol notu + <see cref="RunViewModel.LayerPatterns"/> + UiState persist'i.
    /// <para><b>Eski iddia (değişti):</b> bu test önce "Load sample layers"in 6 örnek katmanını, sonra dört OSYS
    /// varsayılanını pinliyordu. [design v1.19.0] Ön-dolum tamamen kalktı; taslak burada AÇIKÇA iki katmanla
    /// kurulur — pinlenen kural aynı: Save notu, pattern sırası ve persist şekli.</para></summary>
    [Fact]
    public async Task Saving_layers_writes_the_exact_console_note_and_persists_the_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();

        var editor = new SettingsDraftViewModel(null, null);
        editor.AddLayer();
        editor.Layers[0].Name = " Core ";
        editor.Layers[0].Regex = @"^MyApp\.Core\.";
        editor.AddLayer();
        editor.Layers[1].Name = "Api";

        await editor.CommitAsync(run, store);

        // (a) BİREBİR konsol notu (BuildApp.jsx:1423).
        Assert.Contains("Layer definitions updated — 2 layers", run.GetRunDocumentText());

        // (b) RunViewModel.LayerPatterns set edildi (Order = 0..1, üstten alta, ad trim'li).
        Assert.NotNull(run.LayerPatterns);
        Assert.Equal([0, 1], run.LayerPatterns!.Select(p => p.Order));
        Assert.Equal("Core", run.LayerPatterns[0].Name);
        Assert.Equal(@"^MyApp\.Core\.", run.LayerPatterns[0].Regex);

        // (c) UiState'e persist edildi (aynı şekil).
        Assert.Equal(2, store.State.LayerPatterns.Count);
        Assert.Equal(run.LayerPatterns, store.State.LayerPatterns);

        // Emptied → farklı BİREBİR not + persist boşalır.
        var empty = new SettingsDraftViewModel(run.LayerPatterns, null);
        for (int i = empty.Layers.Count - 1; i >= 0; i--) empty.RemoveLayer(empty.Layers[i]);
        await empty.CommitAsync(run, store);
        Assert.Contains("Layers removed — single project list", run.GetRunDocumentText());
        Assert.Empty(store.State.LayerPatterns);
    }

    /// <summary>[D7 · K10] "Change…": kök değişir, durumlar sıfırlanır, YENİ kökte otomatik Sync başlar.
    /// <para><b>Kapsam değişti:</b> bu test artık YALNIZ kabuğun "Choose Folder" yolunu pinler. Settings
    /// diyaloğunun "Change…" düğmesi bu yola girmez — orada seçim Save'e ertelenir
    /// (<c>Picking_a_folder_only_updates_the_draft</c> / <c>Saving_applies_the_pending_repository_root_and_syncs_once</c>).</para></summary>
    [Fact]
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
        await run.ApplySettingsAsync(patterns, @"D:\repo", []); // kök DEĞİŞMEDİ — Sync yine gider

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

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"D:\new\repo", []);

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    /// <summary>[spec 2026-09-18 §1-13 · review I3] Sync artık listeyi kendisi boşaltmaz; Save'in GERÇEK kök
    /// değişimi Sync'ten önce plan yüzeyini boşaltır (eski reponun satırları ekranda kalmaz) ve yapısal imzayı
    /// unutturur — yeni kökün topolojisi, eskisiyle aynı yapıda olsa bile, reveal'le gelir.</summary>
    [Fact]
    public async Task Saving_a_new_root_empties_the_list_so_the_next_topology_reveals()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\old" };
        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, false, null);
        run.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));
        Assert.Single(run.Projects); // ön-koşul
        int topologyChanges = 0;
        run.TopologyChanged += (_, _) => topologyChanges++;

        await run.ApplySettingsAsync([], @"D:\new\repo", []);

        Assert.Empty(run.Projects);
        Assert.False(run.HasTopology);
        Assert.Equal(1, topologyChanges);

        run.OnEvent(new WorkspaceTopologyEvent([node], [], [], [])); // aynı yapı
        Assert.Equal(2, topologyChanges);                            // yine de reveal
    }

    [Fact] // Kök HİÇ seçilmemişken Save: katmanlar kaydedilir ama gidecek bir kök yoktur → Sync GİTMEZ.
    public async Task Applying_settings_without_a_repository_root_keeps_the_layers_but_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, null, []);

        Assert.Same(patterns, run.LayerPatterns);
        Assert.Empty(sent);
    }

    /// <summary>MANŞET yolculuk: hiç repo seçmemiş bir kullanıcı Settings'i açar, kökü seçer ve Save'e basar —
    /// kök uygulanır (faz Empty→Boot) ve TEK Sync YENİ kökte gider (README §"Using it" 1. madde).
    /// <para><b>Neden ayrı test:</b> <c>Applying_settings_without_a_repository_root…</c> kökü <c>null</c>
    /// geçtiğinden boş-kök kapısının <c>ApplyRepositoryRoot</c>'tan SONRA olduğunu kanıtlayamaz — kapı yukarı
    /// taşınsa (ya da <c>repositoryRoot</c> parametresine karşı yazılsa) o test yine yeşil kalırdı. Burada kök
    /// PARAMETREYLE gelir ve çağrı anında <c>RootPath</c> hâlâ boştur: kapı yukarıda olsaydı Save sessizce
    /// Boot'ta takılıp Sync göndermezdi.</para></summary>
    [Fact]
    public async Task Saving_the_first_repository_root_applies_it_and_syncs_at_that_root()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1"); // kök HİÇ seçilmemiş
        Assert.Equal("", run.RootPath);
        Assert.Equal(AppPhase.Empty, run.Phase);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\repo", []);

        Assert.Equal(@"D:\repo", run.RootPath);
        Assert.True(run.HasWorkspace);
        Assert.Equal(AppPhase.Boot, run.Phase);   // OnRootPathChanged Empty→Boot
        Assert.Same(patterns, run.LayerPatterns);
        var sync = Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Equal(@"D:\repo", sync.RootPath);
        Assert.Same(patterns, sync.LayerPatterns); // katmanlar Sync'ten ÖNCE uygulandı
    }

    /// <summary>Koşu UÇUŞTAyken Save: katmanlar kaydedilir, kök DEĞİŞMEZ, Sync GİTMEZ (koşan build'in kökü
    /// çekilmez) — ve düşürülen kök SESSİZCE kaybolmaz.
    /// <para>Konsol notu ZORUNLUdur: diyaloğun yol etiketi "Change…" anında YENİ yolu göstererek seçimi
    /// ONAYLAR (etiket taslaktan okur). Kapı burada kökü uygulamadığına göre kullanıcı, onaylanmış görünen
    /// seçiminin ertelendiğini yalnız konsoldan öğrenebilir.</para></summary>
    [Fact]
    public async Task Applying_settings_mid_run_defers_the_repository_change_and_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", IsStarting = true };
        Assert.True(run.IsMidRunLocked);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\other\repo", []);

        Assert.Same(patterns, run.LayerPatterns);   // katmanlar YİNE uygulanır (sessizce kaybolmaz)
        Assert.Equal(@"D:\repo", run.RootPath);     // kök değişmedi
        Assert.Empty(sent);
        Assert.Contains("Repository change deferred — run in flight", run.GetRunDocumentText()); // BİREBİR
        Assert.Equal("Repository change deferred — run in flight", RunViewModel.RepositoryChangeDeferredLine(runInFlight: true));
    }

    /// <summary>[final review M3] Bir workspace işi (burada pull) uçuştayken Save: koşudaki kapının aynısı — katmanlar
    /// uygulanır, kök ertelenir (konsola tek satır), İKİNCİ bir Sync GİTMEZ. Pull'un zincirlediği Sync zaten yeni
    /// katmanları taşır. Motor gerçektir (izole) ki pull gönderimi başarılı olsun ve pull kapısı açık kalsın.</summary>
    [Fact]
    public async Task Applying_settings_while_a_pull_is_in_flight_defers_the_repository_change_and_sends_no_sync()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(TestPaths.WideStartupTimeout);
        await engine.StartAsync();
        using var root = new TempDir();
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = root.Path };
        run.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0, Behind: 2));
        await run.PullRepositoryCommand.ExecuteAsync(null);
        Assert.True(run.PullBusy); // ön-koşul: pull uçuşta
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\other\repo", []);

        Assert.Same(patterns, run.LayerPatterns);
        Assert.Equal(root.Path, run.RootPath);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
        Assert.Contains(RunViewModel.RepositoryChangeDeferredLine(runInFlight: false), run.GetRunDocumentText());
    }

    /// <summary>[final review M3] Choose Folder da aynı kapıdadır: bir Sync uçuştayken kök değişmez ve Sync gitmez.</summary>
    [Fact]
    public async Task Choosing_a_folder_while_a_sync_is_in_flight_changes_nothing()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.True(run.SyncBusy); // ön-koşul
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.ChangeRepositoryAsync(@"D:\new\repo");

        Assert.Equal(@"D:\repo", run.RootPath);
        Assert.Empty(sent);
    }

    [Fact] // Erteleme notu YALNIZ gerçekten bekleyen bir kök değişimi varsa yazılır — sıradan (katman-only)
           // bir mid-run Save'de konsola gürültü DÜŞMEZ. Aynı kök (Windows yolu → case-insensitive) değişim DEĞİLDİR.
    public async Task Applying_settings_mid_run_says_nothing_when_no_repository_change_is_pending()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", IsStarting = true };
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"d:\REPO", []); // aynı kök, farklı harf durumu

        string text = run.GetRunDocumentText();
        Assert.Contains("Layer definitions updated — 1 layers", text); // non-vacuous: konsol boş değil
        Assert.DoesNotContain("Repository change deferred", text);
        Assert.Empty(sent);
    }

    /// <summary>Motor ERİŞİLEMEZ (hiç doğamadı) iken Save: katmanlar uygulanır ve kök taşınır — bunların ikisi
    /// de motora dokunmaz — ama Sync GİTMEZ.
    /// <para>Gerekçe <c>RunViewModel.IsEngineUnavailable</c>'da yazılıdır: bu durumda gönderim zaten hataya
    /// düşer ve şeritteki KALICI mesajla çelişen ikinci bir hata satırı üretirdi. Save, <c>SyncCommand</c>'ın
    /// aksine bir düğme değildir (devre dışı bırakılamaz) — kapı metodun içinde olmak zorundadır.</para></summary>
    [Fact]
    public async Task Applying_settings_while_the_engine_is_unavailable_keeps_the_layers_but_sends_no_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEngineUnavailable(@"C:\nowhere\supervisor.exe");
        Assert.True(run.IsEngineUnavailable);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")];
        await run.ApplySettingsAsync(patterns, @"D:\new\repo", []);

        Assert.Same(patterns, run.LayerPatterns);       // katmanlar kaydedilir
        Assert.Equal(@"D:\new\repo", run.RootPath);     // kök de uygulanır (kalıcı duruma yazılır)
        Assert.Empty(sent);                             // ama TEK bir komut bile gönderilmez
        Assert.DoesNotContain("failed to send", run.GetRunDocumentText());
    }

    [Fact] // "Change…" TEK BAŞINA hiçbir şey uygulamaz: kök değişmez, satırlar sıfırlanmaz, komut GİTMEZ.
    public async Task Picking_a_folder_only_updates_the_draft()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new ProjectStartedEvent("r1", @"D:\repo\a.csproj", "A"));
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath);
        draft.RepositoryRoot = @"D:\new\repo"; // "Change…" yalnız BUNU yapar

        Assert.Equal(@"D:\repo", run.RootPath);
        Assert.Equal(ProjectRowState.Started, Assert.Single(run.Projects).State); // hollow reset YOK
        Assert.Empty(sent);                                                       // Sync YOK
    }

    [Fact] // Save: bekleyen kök UYGULANIR, satırlar hollow, TEK Sync yeni kökte — ve katmanlar da persist edilir.
    public async Task Saving_applies_the_pending_repository_root_and_syncs_once()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        run.OnEvent(new ProjectStartedEvent("r1", @"D:\repo\a.csproj", "A"));
        var store = NewStore();
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath) { RepositoryRoot = @"D:\new\repo" };

        await draft.CommitAsync(run, store);

        Assert.Equal(@"D:\new\repo", run.RootPath);
        Assert.All(run.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));
        Assert.Equal(@"D:\new\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Empty(store.State.LayerPatterns); // boş taslak da aynı Save'de persist edildi (ön-dolum yok)
    }

    // ================================================================ [K5 · design v1.14.0 §9] EXTERNAL PROJECTS

    /// <summary>[K5] Save katman adı kuralıyla AYNI sertlikte üçüncü bir koşulla bloklanır: herhangi bir harici
    /// kartın path'i BOŞ (trim sonrası). <c>AddExternal</c>'ın kendisi de burada pinlenir: boş path + Git
    /// varsayılan (§9 birebir: "boş path'li, Git kaynaklı kart ekler").</summary>
    [Fact]
    public void Save_is_blocked_only_by_an_empty_external_path_never_by_a_filled_one()
    {
        var editor = new SettingsDraftViewModel(null, @"D:\repo");

        int canSaveNotifications = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsDraftViewModel.CanSave)) canSaveNotifications++;
        };

        editor.AddExternal();
        Assert.True(canSaveNotifications > 0, "AddExternal sonrası CanSave bildirimi YOK");
        var row = Assert.Single(editor.Externals);
        Assert.Equal("", row.Path);   // §9: "boş path'li kart ekler" (kart artık bir kaynak seçimi TAŞIMAZ)
        Assert.False(editor.CanSave); // boş path → bloklar

        canSaveNotifications = 0;
        row.Path = "   "; // yalnız boşluk da BOŞtur (trim) — katman adı kuralıyla AYNI
        Assert.True(canSaveNotifications > 0, "Path değişimi sonrası CanSave bildirimi YOK");
        Assert.False(editor.CanSave);

        row.Path = @"C:\src\shared\Delta.Common\Delta.Common.csproj";
        Assert.True(editor.CanSave); // dolu path → artık bloklamaz

        canSaveNotifications = 0;
        editor.RemoveExternal(row);
        Assert.True(canSaveNotifications > 0, "RemoveExternal sonrası CanSave bildirimi YOK");
        Assert.Empty(editor.Externals);
        Assert.True(editor.CanSave);
    }

    /// <summary>[K5] <c>BuildExternals</c> Export'un VE Save'in PAYLAŞTIĞI TEK dönüşümdür: path TRIM'lenir, boş
    /// (yalnız boşluk dahil) path'ler DÜŞER — prototip <c>ext.filter((x) =&gt; x.path)</c>'in birebir portu.</summary>
    [Fact]
    public void BuildExternals_trims_paths_and_drops_blank_ones()
    {
        var editor = new SettingsDraftViewModel(null, @"D:\repo");
        editor.AddExternal();
        editor.Externals[0].Path = "  C:\\a  ";
        editor.AddExternal();
        editor.Externals[1].Path = "   "; // boş — düşer

        var built = editor.BuildExternals();

        Assert.Equal([new ExternalProject(@"C:\a")], built);
    }

    /// <summary>[K5] Save: harici projeler katmanlarla AYNI commit'te UiState'e yazılır ve
    /// <see cref="RunViewModel.ExternalProjects"/>'e uygulanır; konsol notu sayı 0'dan artınca BİREBİR budur.
    /// <para>Not "built before the repository projects" der ve bu DOĞRUDUR: harici projeler ayrılmış
    /// <c>External</c> katmanındadır (index −1), yani build-order'da ana repo projelerinden önce gelirler.</para></summary>
    [Fact]
    public async Task Saving_externals_persists_them_alongside_layers_in_the_same_commit()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();

        var editor = new SettingsDraftViewModel(null, @"D:\repo");
        editor.AddExternal();
        editor.Externals[0].Path = @"C:\src\shared\Delta.Common\Delta.Common.csproj";

        await editor.CommitAsync(run, store);

        Assert.Contains("External projects → 1 — built before the repository projects", run.GetRunDocumentText());
        Assert.Equal(
            [new ExternalProject(@"C:\src\shared\Delta.Common\Delta.Common.csproj")],
            run.ExternalProjects);
        Assert.Equal(run.ExternalProjects, store.State.ExternalProjects); // AYNI commit'te UiState'e de yazıldı
    }

    /// <summary>[K5] Save notu — katman notundan (<c>ApplyLayerPatterns</c>, HER Save'de koşulsuz) FARKLI kural:
    /// harici projeler notu YALNIZ SAYI DEĞİŞTİYSE yazılır. Üç geçiş: 0→2 (not VAR), 2→2 farklı içerik (not YOK,
    /// ama liste GERÇEKTEN güncellenir), 2→0 (temizlendi notu).</summary>
    [Fact]
    public async Task Applying_settings_writes_the_external_note_only_when_the_count_changes()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        IReadOnlyList<LayerPattern> patterns = [new LayerPattern(0, "^A", "Alpha")]; // sabit — bu testin konusu DEĞİL

        // 0 → 2: sayı DEĞİŞTİ → not YAZILIR (N ≥ 1 deseni).
        await run.ApplySettingsAsync(patterns, @"D:\repo",
            [new ExternalProject(@"C:\a"), new ExternalProject(@"C:\b")]);
        Assert.Contains("External projects → 2 — built before the repository projects", run.GetRunDocumentText());
        Assert.Equal(2, run.ExternalProjects.Count);

        // 2 → 2 (FARKLI path'ler, AYNI sayı): sayı DEĞİŞMEDİ → İKİNCİ bir not satırı EKLENMEZ — ama liste yine
        // GERÇEKTEN güncellenir (not-gating yalnız KONSOLU susturur, veriyi DONDURMAZ).
        await run.ApplySettingsAsync(patterns, @"D:\repo",
            [new ExternalProject(@"C:\c"), new ExternalProject(@"C:\d")]);
        Assert.Equal(1, CountOccurrences(run.GetRunDocumentText(), "External projects → 2"));
        Assert.Equal(@"C:\c", run.ExternalProjects[0].Path);

        // 2 → 0: sayı DEĞİŞTİ (0'a düştü) → "cleared" notu.
        await run.ApplySettingsAsync(patterns, @"D:\repo", []);
        Assert.Contains("External projects cleared", run.GetRunDocumentText());
    }

    /// <summary>[K5] Hiç harici proje YOKKEN (0) ve verilen liste de BOŞSA (0) Save gürültü ÜRETMEMELİDİR —
    /// "değişmediyse not yok" kuralının en sık koşacağı yol (katman-only bir Save).</summary>
    [Fact]
    public async Task Applying_settings_with_no_external_projects_and_none_before_writes_no_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"D:\repo", []);

        Assert.DoesNotContain("External projects", run.GetRunDocumentText());
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
        return count;
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
    /// <summary>[A13/T3a · a3] Boş-katman kesikli kutusunun metni BİREBİR ve görünürlüğü: kayıtlı katman yokken
    /// taze diyalogda GÖRÜNÜR, ilk satır eklenince gizlenir, son satır silinince geri gelir.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> ESKİ İDDİA
    /// (<c>Empty_state_box_appears_only_after_every_layer_row_is_deleted</c>): taze diyalog dört OSYS varsayılanıyla
    /// açıldığı için kutu ancak tüm satırlar silinince görünürdü. Ön-dolum kalktı; kutu baştan görünür. LAYERS caps
    /// başlığı ve uzun açıklama da kalktı — sayfa başlığı PaneHead'dir
    /// (<see cref="SettingsDialogLayoutTests.Every_page_opens_with_its_pane_head"/>).</para></summary>
    [StaFact]
    public void Layers_empty_state_box_shows_on_a_fresh_dialog_and_hides_while_a_row_exists()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();

        var box = dialog.EmptyState;
        Assert.Contains(DsResources.RealizedObjects(box).OfType<TextBlock>(),
            t => t.Text == "No layers yet — projects show as a single list in build order.");
        Assert.Equal(Visibility.Visible, box.Visibility);

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        draft.AddLayer();
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, box.Visibility);

        draft.RemoveLayer(draft.Layers[0]);
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Visible, box.Visibility);
    }

    /// <summary>[A13/T3a · a9] design-v1 §2.9: <c>Add layer</c> (ghost, ikon+etiket) · <c>Cancel</c> · <c>Save</c>
    /// (primary) — davranışları <see cref="SettingsDialogTests"/>'te pinlidir
    /// (<c>Saving_layers_writes_the_exact_console_note_and_persists_the_patterns</c> · <c>Cancel_discards_the_draft</c>);
    /// burada pinlenen yalnız etiketlerin BİREBİR metnidir.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> ESKİ İDDİA: footer solunda ghost <c>Load sample layers</c>
    /// düğmesi vardı (daha önce <c>Restore default layers</c>). Ön-dolum kalktığı için düğme de kalktı — yokluğu
    /// <see cref="SettingsDialogLayoutTests.The_footer_has_no_sample_layers_button_and_carries_the_design_tooltips"/>'te
    /// pinlidir.</para></summary>
    [StaFact]
    public void Settings_dialog_footer_and_add_layer_button_labels_are_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.Layers);
        dialog.UpdateLayout();

        var buttons = DsResources.RealizedObjects(dialog).OfType<Button>().ToList();
        Assert.Contains(buttons, b => Equals(b.Content, "Cancel"));
        // Fixture'ın kökü DOLUDUR (HasWorkspace) → düğme "Save"dir. First run'daki "Save and sync" varyantı
        // ayrı bir testte pinlenir (design v1.8.0 §2.9).
        Assert.Contains(buttons, b => Equals(b.Content, "Save"));

        // "Add layer": etiket paylaşılan Ds.Settings.AddRow şablonunun (ikon + TextBlock) içinde çizilir.
        var texts = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Add layer", texts);
    }

    /// <summary>[design v1.8.0 §2.9] Diyalogdaki <c>Browse…</c>: yalnız TASLAK (ve onu gösteren mono input)
    /// güncellenir; canlı kök ve motor DOKUNULMAZ — uygulanması Save'e ertelenir.
    /// <para><b>[DEĞİŞEN KURAL]</b> Düğmenin adı <c>Change…</c> idi ve yanında düzenlenemez bir yol ETİKETİ
    /// (<c>RepoPathText</c>) dururdu. v1.8.0 repository root'u Settings'in İLK bölümü yaptı: etiket yerini
    /// düzenlenebilir mono bir input'a bıraktı, düğme de <c>Browse…</c> oldu.</para></summary>
    [StaFact]
    public void Browse_updates_only_the_draft_until_save()
    {
        var (dialog, run, _, scope) = SettingsDialogHost.OpenRealized(pickFolder: () => @"D:\picked\repo");
        using var _scope = scope;
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var browse = DsResources.RealizedObjects(dialog).OfType<Button>()
            .Single(b => b.Content is StackPanel panel
                         && panel.Children.OfType<TextBlock>().Any(t => t.Text == "Browse…"));
        browse.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout();

        Assert.Equal(@"D:\picked\repo", dialog.RootInput.Text);  // input YENİ yolu gösterir
        Assert.Equal(@"D:\picked\repo", dialog.Draft!.RepositoryRoot);
        Assert.Equal(@"D:\repo", run.RootPath);                   // canlı kök ESKİ (fixture kökü)
        Assert.Empty(sent);                                       // Sync YOK
    }

    /// <summary>[design v1.8.0 §2.9] First run'da (henüz workspace yok) kaydetmek AYNI ZAMANDA kurulumdur ve
    /// düğme bunu söyler: <c>Save and sync</c>. Workspace açıldıktan sonra yalnız <c>Save</c>.</summary>
    [StaFact]
    public void The_first_run_save_button_says_save_and_sync()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run => run.RootPath = "");
        using var _scope = scope;

        Assert.Equal("Save and sync", dialog.Save.Content);
    }

    /// <summary>[design v1.8.0 §2.9] "Root boşken Save disabled — uygulamanın çalışması için zorunlu tek ayar
    /// budur."</summary>
    [Fact]
    public void Save_is_blocked_while_the_repository_root_is_empty()
    {
        var editor = new SettingsDraftViewModel(null, null);
        Assert.False(editor.CanSave);

        editor.RepositoryRoot = @"D:\src\osys";
        Assert.True(editor.CanSave);

        editor.RepositoryRoot = "   ";   // yalnız boşluk da BOŞtur
        Assert.False(editor.CanSave);
    }

    // ================================================================ [K5 · design v1.14.0 §9] EXTERNAL PROJECTS

    /// <summary>[design v1.19.0 §2.9] Bölüm SIRASI artık rayın sırasıdır: General · Workspace · External projects ·
    /// Layers — harici projeler hâlâ katmanlardan ÖNCE durur (derleme sırasının başındadırlar).
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ İDDİA
    /// (<c>Settings_dialog_sections_appear_in_workspace_external_layers_order</c>): tek kolonlu gövdede WORKSPACE →
    /// EXTERNAL PROJECTS → LAYERS caps başlıkları alt alta dururdu ve sıra dikey konumdan ölçülürdü. Gövde sol raylı
    /// iki panele bölündü; sıra rayda ölçülür, ölçü ayrıntısı
    /// <see cref="SettingsDialogLayoutTests.The_rail_is_196px_on_surface_with_the_four_sections_in_order"/>'tedir.</para></summary>
    [StaFact]
    public void Settings_sections_appear_in_general_workspace_external_layers_order_on_the_rail()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        SettingsSection[] order = [SettingsSection.General, SettingsSection.Workspace, SettingsSection.External, SettingsSection.Layers];
        var ys = order.Select(s => dialog.RailItem(s).TranslatePoint(new Point(0, 0), dialog).Y).ToList();
        Assert.Equal(ys.OrderBy(y => y), ys);
        Assert.Equal(4, ys.Distinct().Count());
    }

    /// <summary>[K5] Harici projeler boş-durum kutusunun metni BİREBİR (v1.19.0'da DEĞİŞMEDİ).
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> ESKİ İDDİA
    /// (<c>Settings_dialog_pins_the_external_projects_caption_description_and_empty_state_box_verbatim</c>): bölüm
    /// <c>EXTERNAL PROJECTS</c> caps başlığı ve "before" vurgulu üç Run'lık uzun açıklamayla açılırdı. v1.19.0 uzun
    /// gerekçe metinlerini kaldırdı; sayfa tek satırlık PaneHead ile açılır
    /// (<see cref="SettingsDialogLayoutTests.Every_page_opens_with_its_pane_head"/>).</para></summary>
    [StaFact]
    public void Settings_dialog_pins_the_external_projects_empty_state_box_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        Assert.Contains(DsResources.RealizedObjects(dialog.ExternalEmptyState).OfType<TextBlock>(),
            t => t.Text == "No external projects — only what is discovered under the repository root is built.");
    }

    /// <summary>[K5] Harici liste — katmanların AKSİNE — VARSAYILAN OLARAK BOŞTUR (bir "seed" kavramı yok);
    /// boş-durum kutusu bu yüzden TAZE diyalogda görünür ve ilk kart eklenince kaybolur (Layers'ın tersi
    /// başlangıç durumu, AYNI mekanizma).</summary>
    [StaFact]
    public void External_empty_state_box_appears_only_when_the_list_is_empty()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var box = dialog.ExternalEmptyState;
        Assert.Equal(Visibility.Visible, box.Visibility); // taze diyalog: harici liste BAŞTAN boş

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        draft.AddExternal();
        dialog.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, box.Visibility);
    }

    /// <summary>[K5] "Add external project": VM satırı (boş path) VE gerçekten realize edilen bir kart
    /// (path input'u ekranda, doğru satıra bağlı) — "kart" iddiasının GEOMETRİK değil ama GERÇEK kanıtı.
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.9]</b> Watermark ürüne özeldi
    /// (<c>C:\src\shared\Delta.Common\Delta.Common.csproj</c>); artık ürün-bağımsız <c>MyApp.Common</c> örneğidir.
    /// Kart yalnız External projects sayfası görünürken realize olur.</para></summary>
    [StaFact]
    public void Add_external_project_appends_a_realized_card_with_an_empty_path_and_git_selected()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        dialog.ShowSection(SettingsSection.External);
        dialog.UpdateLayout();

        var addButton = DsResources.RealizedObjects(dialog).OfType<Button>()
            .Single(b => Equals(b.Content, "Add external project"));
        addButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout();

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        var row = Assert.Single(draft.Externals);
        Assert.Equal("", row.Path);   // §9: "boş path'li kart ekler"

        var pathInput = DsResources.Descendants(dialog).OfType<TextBox>()
            .Single(t => BuildOrchestrator.App.Controls.DsChrome.GetWatermark(t) == @"C:\src\shared\MyApp.Common\MyApp.Common.csproj");
        Assert.Same(row, pathInput.DataContext);
    }

    /// <summary>[K5] Save katman adı kuralıyla AYNI sertlikte: boş bir harici kart path'i düğmeyi disable eder,
    /// doldurulunca geri açar — bu WPF seviyesinde <c>dialog.Save.IsEnabled</c> (CanSave binding'i) üzerinden.</summary>
    [StaFact]
    public void Save_is_disabled_while_an_external_card_has_an_empty_path()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        draft.AddExternal();
        dialog.UpdateLayout();

        Assert.False(dialog.Save.IsEnabled);

        draft.Externals[0].Path = @"C:\a";
        dialog.UpdateLayout();

        Assert.True(dialog.Save.IsEnabled);
    }
}
