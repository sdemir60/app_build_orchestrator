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
        var editor = new SettingsDraftViewModel(null, @"D:\repo");
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

        var draft = new SettingsDraftViewModel(run.LayerPatterns, null);

        Assert.Equal(
            ["OSYS.Types", "OSYS.Business", "OSYS.Orchestration", "OSYS.UI"],
            draft.Layers.Select(r => r.Name));
        Assert.Equal(@"^OSYS\.Types\.", draft.Layers[0].Regex);

        // Taslağın dolu gelmesi tek başına HİÇBİR ŞEY uygulamaz/kaydetmez — açılışta seed YOKtur.
        Assert.Null(run.LayerPatterns);
        Assert.Empty(store.State.LayerPatterns);
    }

    [Fact] // Kayıtlı liste BOŞ ama null DEĞİL: "tüm katmanları sil + Save" sonrası canlı durum tam olarak budur
           // (LayerPatterns = boş liste). Diyalog yeniden açıldığında yine varsayılanlar görünmelidir.
    public void A_draft_built_from_an_emptied_layer_list_still_shows_the_defaults()
    {
        IReadOnlyList<LayerPattern> emptied = []; // "hepsini sil + Save" sonrası RunViewModel.LayerPatterns

        var draft = new SettingsDraftViewModel(emptied, null);

        // Varsayılanların BİREBİR metni A_fresh_draft_is_prefilled_with_the_default_layers'ta pinlidir; burada
        // pinlenen kural "boş liste null ile AYNI davranır" — ctor koşulu `initial is not null`'a kayarsa bu
        // taslak SIFIR satırla açılır ve karşılaştırma düşer.
        Assert.Equal(
            new SettingsDraftViewModel(null, null).Layers.Select(r => (r.Name, r.Regex)),
            draft.Layers.Select(r => (r.Name, r.Regex)));
        Assert.NotEmpty(draft.Layers); // non-vacuous: iki taraf da boş olsaydı karşılaştırma anlamsız kalırdı
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

    [Fact] // "Restore default layers": düzenlenmiş taslağı varsayılanlara döndürür, Save'siz KALICI DEĞİL.
    public async Task Restore_default_layers_replaces_the_draft_without_touching_the_live_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var store = NewStore();
        IReadOnlyList<LayerPattern> live = [new LayerPattern(0, "^A", "Alpha")];
        run.LayerPatterns = live;
        var draft = new SettingsDraftViewModel(run.LayerPatterns, null);

        draft.LoadSampleLayers();

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

        var editor = new SettingsDraftViewModel(null, null); // taze taslak = 4 varsayılan
        Assert.Equal(4, editor.Layers.Count);

        await editor.CommitAsync(run, store);

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
        Assert.Equal(4, store.State.LayerPatterns.Count); // varsayılan taslak da aynı Save'de persist edildi
    }

    // ================================================================ [K5 · design v1.14.0 §9] EXTERNAL PROJECTS

    /// <summary>[K5] Save katman adı kuralıyla AYNI sertlikte üçüncü bir koşulla bloklanır: herhangi bir harici
    /// kartın path'i BOŞ (trim sonrası). <c>AddExternal</c>'ın kendisi de burada pinlenir: boş path + Git
    /// varsayılan (§9 birebir: "boş path'li, Git kaynaklı kart ekler").</summary>
    [Fact]
    public void Save_is_blocked_only_by_an_empty_external_path_never_by_a_filled_one()
    {
        var editor = new SettingsDraftViewModel(null, @"D:\repo");
        for (int i = editor.Layers.Count - 1; i >= 0; i--) editor.RemoveLayer(editor.Layers[i]); // katman gürültüsü at

        int canSaveNotifications = 0;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsDraftViewModel.CanSave)) canSaveNotifications++;
        };

        editor.AddExternal();
        Assert.True(canSaveNotifications > 0, "AddExternal sonrası CanSave bildirimi YOK");
        var row = Assert.Single(editor.Externals);
        Assert.Equal("", row.Path);
        Assert.Equal(VcsKind.Git, row.Vcs); // §9: "boş path'li, Git kaynaklı kart ekler"
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
        editor.Externals[0].Vcs = VcsKind.Tfvc;
        editor.AddExternal();
        editor.Externals[1].Path = "   "; // boş — düşer

        var built = editor.BuildExternals();

        Assert.Equal([new ExternalProject(@"C:\a", VcsKind.Tfvc)], built);
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

        var editor = new SettingsDraftViewModel(null, @"D:\repo"); // 4 varsayılan katman
        editor.AddExternal();
        editor.Externals[0].Path = @"C:\src\shared\Delta.Common\Delta.Common.csproj";
        editor.Externals[0].Vcs = VcsKind.Tfvc;

        await editor.CommitAsync(run, store);

        Assert.Contains("External projects → 1 — built before the repository projects", run.GetRunDocumentText());
        Assert.Equal(
            [new ExternalProject(@"C:\src\shared\Delta.Common\Delta.Common.csproj", VcsKind.Tfvc)],
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
            [new ExternalProject(@"C:\a", VcsKind.Git), new ExternalProject(@"C:\b", VcsKind.Tfvc)]);
        Assert.Contains("External projects → 2 — built before the repository projects", run.GetRunDocumentText());
        Assert.Equal(2, run.ExternalProjects.Count);

        // 2 → 2 (FARKLI path'ler, AYNI sayı): sayı DEĞİŞMEDİ → İKİNCİ bir not satırı EKLENMEZ — ama liste yine
        // GERÇEKTEN güncellenir (not-gating yalnız KONSOLU susturur, veriyi DONDURMAZ).
        await run.ApplySettingsAsync(patterns, @"D:\repo",
            [new ExternalProject(@"C:\c", VcsKind.Git), new ExternalProject(@"C:\d", VcsKind.Git)]);
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
        // [K5] EXTERNAL PROJECTS'in açıklaması da 3 Run'dan kurulu (aynı "before" vurgusu deseni) — artık İKİ
        // 3-Run'lı blok var, bu yüzden LAYERS'ınki "regex" sözcüğüyle ayırt edilir (yalnız Layers açıklaması taşır).
        string description = string.Concat(
            blocks.Single(b => b.Inlines.Count == 3 && b.Inlines.OfType<Run>().Any(r => r.Text.Contains("regex")))
                .Inlines.OfType<Run>().Select(r => r.Text));
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
    /// (primary) · <c>Restore default layers</c> (ghost) — davranışları <see cref="SettingsDialogTests"/>'te
    /// pinlidir (<c>Saving_layers_writes_the_exact_console_note_and_persists_the_patterns</c> ·
    /// <c>Cancel_discards_the_draft</c> · <c>Restore_default_layers_replaces_the_draft_without_touching_the_live_state</c>);
    /// burada pinlenen yalnız etiketlerin BİREBİR metnidir.</summary>
    [StaFact]
    public void Settings_dialog_footer_and_add_layer_button_labels_are_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var buttons = DsResources.RealizedObjects(dialog).OfType<Button>().ToList();
        Assert.Contains(buttons, b => Equals(b.Content, "Cancel"));
        // Fixture'ın kökü DOLUDUR (HasWorkspace) → düğme "Save"dir. First run'daki "Save and sync" varyantı
        // ayrı bir testte pinlenir (design v1.8.0 §2.9).
        Assert.Contains(buttons, b => Equals(b.Content, "Save"));
        // [DEĞİŞEN KURAL — §2.9] Sol ghost düğmenin adı "Restore default layers" idi; tasarım metni
        // "Load sample layers"dır ve daha doğrudur: varsayılan konfigürasyon BOŞTUR, bu düğme örnekleri DOLDURUR.
        Assert.Contains(buttons, b => Equals(b.Content, "Load sample layers"));

        // "Add layer": Content bir StackPanel'dir (ikon + TextBlock) — etiket ayrı aranır.
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

    /// <summary>[K5] Gövde sırası BİREBİR: WORKSPACE → EXTERNAL PROJECTS → LAYERS (§9: "harici projeler
    /// derleme sırasının başında olduğu için katmanlardan önce durur"). Geometri kanıtı (TranslatePoint) —
    /// tree-walk sırasına değil GERÇEK ekran konumuna bakar.</summary>
    [StaFact]
    public void Settings_dialog_sections_appear_in_workspace_external_layers_order()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            run => run.ExternalProjects = [new ExternalProject(@"C:\a", VcsKind.Git)]);
        using var _scope = scope;

        var blocks = DsResources.RealizedObjects(dialog).OfType<TextBlock>().ToList();
        double YOf(string text) => blocks.Single(b => b.Text == text).TranslatePoint(new Point(0, 0), dialog).Y;

        double workspaceY = YOf("WORKSPACE");
        double externalY = YOf("EXTERNAL PROJECTS");
        double layersY = YOf("LAYERS");

        Assert.True(workspaceY < externalY, "WORKSPACE, EXTERNAL PROJECTS'ten önce durmalı");
        Assert.True(externalY < layersY, "EXTERNAL PROJECTS, LAYERS'tan önce durmalı");
    }

    /// <summary>[K5] design v1.14.0 §9 BİREBİR: caps başlığı, açıklama (3 Run — "before" vurgusu ayrı) ve
    /// boş-durum kutusunun metni. "before" text-secondary + 500 taşır (§9: "before sözcüğü text-secondary, 500").
    /// <para><b>[DEĞİŞEN KURAL]</b> §9'un cümlesi "They are built before everything else, <i>in this order</i>"
    /// idi. "before" iddiası KORUNUR ve doğrudur (ayrılmış <c>External</c> katmanı, index −1); "in this order"
    /// DÜŞTÜ — kart sırası yalnız çalışma kopyalarının tazelenme sırasıdır, harici projeler arasındaki derleme
    /// sırası topolojiden gelir. 3-Run yapısı, vurgulanan sözcük ve tipografi korunur.</para>
    /// <para><b>[DEĞİŞEN KURAL — design v1.16.0 §2.9]</b> Metin, kart sırasının ne olmadığını AÇIKÇA söyleyen
    /// bir cümleyle bitiyor: sıranın tek anlamı çalışma kopyalarının güncellenme sırasıdır. Önceki hâli "in
    /// this order"ı düşürmüştü ama yerine hiçbir şey koymamıştı — kullanıcı sıralamanın neye yaradığını
    /// tahmin etmek zorunda kalıyordu.</para></summary>
    [StaFact]
    public void Settings_dialog_pins_the_external_projects_caption_description_and_empty_state_box_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var blocks = DsResources.RealizedObjects(dialog).OfType<TextBlock>().ToList();
        var texts = blocks.Select(t => t.Text).ToList();
        Assert.Contains("EXTERNAL PROJECTS", texts);

        var description = blocks.Single(b =>
            b.Inlines.Count == 3 && b.Inlines.OfType<Run>().Any(r => r.Text == "before"));
        Assert.Equal(
            """Projects outside the repository root — a folder, a solution or a project file, and whether it comes from Git or TFVC. The working copy root is found from the path upwards. They are built before everything else; the rest follows the layers below. Card order only sets the order the working copies are updated — among themselves they build in dependency order.""",
            string.Concat(description.Inlines.OfType<Run>().Select(r => r.Text)));

        var emphasis = description.Inlines.OfType<Run>().Single(r => r.Text == "before");
        Assert.Equal(dialog.FindResource("Brush.TextSecondary"), emphasis.Foreground);
        Assert.Equal(dialog.FindResource("FontWeight.Emphasis"), emphasis.FontWeight);

        Assert.Contains("No external projects — only what is discovered under the repository root is built.", texts);
    }

    /// <summary>[K5] Harici liste — katmanların AKSİNE — VARSAYILAN OLARAK BOŞTUR (bir "seed" kavramı yok);
    /// boş-durum kutusu bu yüzden TAZE diyalogda görünür ve ilk kart eklenince kaybolur (Layers'ın tersi
    /// başlangıç durumu, AYNI mekanizma).</summary>
    [StaFact]
    public void External_empty_state_box_appears_only_when_the_list_is_empty()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var box = DsResources.RealizedObjects(dialog).OfType<Grid>().Single(g => g.Name == "ExternalEmptyState");
        Assert.Equal(Visibility.Visible, box.Visibility); // taze diyalog: harici liste BAŞTAN boş

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        draft.AddExternal();
        dialog.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, box.Visibility);
    }

    /// <summary>[K5] "Add external project": VM satırı (boş path + Git) VE gerçekten realize edilen bir kart
    /// (path input'u ekranda, doğru satıra bağlı) — "kart" iddiasının GEOMETRİK değil ama GERÇEK kanıtı.</summary>
    [StaFact]
    public void Add_external_project_appends_a_realized_card_with_an_empty_path_and_git_selected()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var addButton = DsResources.RealizedObjects(dialog).OfType<Button>()
            .Single(b => b.Content is StackPanel panel
                         && panel.Children.OfType<TextBlock>().Any(t => t.Text == "Add external project"));
        addButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout();

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        var row = Assert.Single(draft.Externals);
        Assert.Equal("", row.Path);
        Assert.Equal(VcsKind.Git, row.Vcs); // §9: "boş path'li, Git kaynaklı kart ekler"

        var pathInput = DsResources.Descendants(dialog).OfType<TextBox>()
            .Single(t => BuildOrchestrator.App.Controls.DsChrome.GetWatermark(t) == @"C:\src\shared\Delta.Common\Delta.Common.csproj");
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
