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

    [Fact] // Kayıtlı liste BOŞ ama null DEĞİL: "tüm katmanları sil + Save" sonrası canlı durum tam olarak budur
           // (LayerPatterns = boş liste). Diyalog yeniden açıldığında yine varsayılanlar görünmelidir.
    public void A_draft_built_from_an_emptied_layer_list_still_shows_the_defaults()
    {
        IReadOnlyList<LayerPattern> emptied = []; // "hepsini sil + Save" sonrası RunViewModel.LayerPatterns

        var draft = new SettingsDraftViewModel(emptied);

        // Varsayılanların BİREBİR metni A_fresh_draft_is_prefilled_with_the_default_layers'ta pinlidir; burada
        // pinlenen kural "boş liste null ile AYNI davranır" — ctor koşulu `initial is not null`'a kayarsa bu
        // taslak SIFIR satırla açılır ve karşılaştırma düşer.
        Assert.Equal(
            new SettingsDraftViewModel(null).Layers.Select(r => (r.Name, r.Regex)),
            draft.Layers.Select(r => (r.Name, r.Regex)));
        Assert.NotEmpty(draft.Layers); // non-vacuous: iki taraf da boş olsaydı karşılaştırma anlamsız kalırdı
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

        var editor = new SettingsDraftViewModel(null); // taze taslak = 4 varsayılan
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
        var empty = new SettingsDraftViewModel(run.LayerPatterns);
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
        await run.ApplySettingsAsync(patterns, @"D:\repo", run.BuildDependencyCycles); // kök DEĞİŞMEDİ — Sync yine gider

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

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"D:\new\repo", run.BuildDependencyCycles);

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
        await run.ApplySettingsAsync(patterns, null, run.BuildDependencyCycles);

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
        await run.ApplySettingsAsync(patterns, @"D:\repo", run.BuildDependencyCycles);

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
        await run.ApplySettingsAsync(patterns, @"D:\other\repo", run.BuildDependencyCycles);

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

        await run.ApplySettingsAsync([new LayerPattern(0, "^A", "Alpha")], @"d:\REPO", run.BuildDependencyCycles); // aynı kök, farklı harf durumu

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
        await run.ApplySettingsAsync(patterns, @"D:\new\repo", run.BuildDependencyCycles);

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

    // ---------------------------------------------------------------- [Task 11] build dependency cycles

    [Fact] // Taslak CANLI değerle açılır (kopya, Save'e kadar canlıya dokunulmaz) — katmanlar/kök ile aynı sözleşme.
    public async Task A_draft_opens_with_the_live_build_dependency_cycles_value()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { BuildDependencyCycles = false };

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath, run.BuildDependencyCycles);

        Assert.False(draft.BuildDependencyCycles);
        draft.BuildDependencyCycles = true;      // taslağı değiştirmek CANLI durumu ETKİLEMEZ (Cancel = commit yok)
        Assert.False(run.BuildDependencyCycles);
    }

    /// <summary>[Task 11] Save: anahtar hem KALICI duruma yazılır hem CANLI VM'e uygulanır — ve uygulama
    /// Sync'ten ÖNCE olur, yani aynı Save'in gönderdiği <see cref="SyncWorkspaceCommand"/> YENİ değeri taşır
    /// (katman pattern'lerindeki sıra kuralının aynısı; ters sırada komut ESKİ değerle giderdi ve Idle'daki
    /// will-dot'lar bir Sync boyunca yalan söylerdi).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_persists_the_cycle_switch_and_applies_it_before_the_sync(bool on)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", BuildDependencyCycles = !on };
        var store = NewStore();
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var draft = new SettingsDraftViewModel(run.LayerPatterns, run.RootPath, run.BuildDependencyCycles)
            { BuildDependencyCycles = on };

        await draft.CommitAsync(run, store);

        Assert.Equal(on, run.BuildDependencyCycles);                 // canlı VM
        Assert.Equal(on, store.State.BuildDependencyCycles);          // kalıcı durum
        Assert.Equal(on, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).BuildDependencyCycles); // SIRA kanıtı
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
        Assert.Contains(buttons, b => Equals(b.Content, "Save"));
        Assert.Contains(buttons, b => Equals(b.Content, "Restore default layers"));

        // "Add layer": Content bir StackPanel'dir (ikon + TextBlock) — etiket ayrı aranır.
        var texts = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Add layer", texts);
    }

    /// <summary>[Task 11] Kill switch'in GÖRSEL yüzeyi. Yeni bir kontrol/şablon YOKTUR — mevcut
    /// <c>Ds.Chip</c> <see cref="ToggleButton"/> stili (ActionBar'ın branch/perf chip'leriyle AYNI) yeniden
    /// kullanılır, bu yüzden ayrı bir realize testi de gerekmez: burada pinlenen, chip'in GERÇEKTEN realize
    /// olduğu, o stille kurulduğu ve taslağa İKİ YÖNLÜ bağlı olduğudur.</summary>
    [StaFact]
    public void Settings_dialog_binds_the_cycle_chip_to_the_draft_in_both_directions()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(configure: run => run.BuildDependencyCycles = false);
        using var _scope = scope;

        var chip = DsResources.RealizedObjects(dialog).OfType<ToggleButton>()
            .Single(t => t.Name == "PART_CyclesChip");
        Assert.Same(dialog.TryFindResource("Ds.Chip"), chip.Style);   // yeni şablon YOK — mevcut DS stili
        Assert.Equal("Build dependency cycles", chip.Content);

        var draft = (SettingsDraftViewModel)dialog.DataContext;
        Assert.False(draft.BuildDependencyCycles);
        Assert.False(chip.IsChecked);            // canlı KAPALI değer chip'e indi

        chip.IsChecked = true;                   // kullanıcı tıklaması
        dialog.UpdateLayout();
        Assert.True(draft.BuildDependencyCycles); // ve taslağa geri çıktı (TwoWay)
    }

    /// <summary>[Task 11] Bölüm başlığı + açıklaması BİREBİR — anahtarın ne yaptığı diyaloğun kendi sesiyle
    /// yazılır (LAYERS/REPOSITORY bölümleriyle aynı caps-başlık + açıklama deseni).</summary>
    [StaFact]
    public void Settings_dialog_pins_the_dependency_cycles_caption_and_description_verbatim()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;

        var texts = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("DEPENDENCY CYCLES", texts);
        Assert.Contains(
            "Projects that depend on each other form a cycle. When this is on they are built together, " +
            "one after another, in repeated rounds until two rounds in a row come back clean. When it is " +
            "off they are skipped, as they were before.",
            texts);
    }

    /// <summary>Diyalogda "Change…": yalnız yol ETİKETİ güncellenir; canlı kök ve motor DOKUNULMAZ.</summary>
    [StaFact]
    public void Change_button_updates_only_the_dialog_label_until_save()
    {
        var (dialog, run, _, scope) = SettingsDialogHost.OpenRealized(pickFolder: () => @"D:\picked\repo");
        using var _scope = scope;
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        var change = DsResources.RealizedObjects(dialog).OfType<Button>().Single(b => Equals(b.Content, "Change…"));
        change.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        dialog.UpdateLayout();

        var label = DsResources.RealizedObjects(dialog).OfType<TextBlock>().Single(t => t.Name == "RepoPathText");
        Assert.Equal(@"D:\picked\repo", label.Text);   // etiket YENİ yolu gösterir
        Assert.Equal(@"D:\repo", run.RootPath);        // canlı kök ESKİ (fixture kökü)
        Assert.Empty(sent);                            // Sync YOK
    }
}
