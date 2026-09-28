using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P4] Settings → General → STARTUP: <c>Start with Windows</c> ve <c>Start minimized to tray</c>.
///
/// <para><b>Kalıcılık</b> close-to-tray işinin (P3) kurduğu desenledir — <see cref="ShellSwitches"/> tablosu: Save'de
/// <c>ui-state.json</c>'a yazılır, diyalog her açılışta kayıtlı değeri gösterir, Export/Import taşır (dosyada anahtar
/// yoksa formdaki değer korunur) ve değer değişince konsola tek satır not düşer (değişmezse sessiz).</para>
///
/// <para><b>Windows tarafı</b> gerçek registry'ye ASLA dokunmaz: <see cref="IAutostartRegistry"/> yerine
/// <see cref="FakeAutostartRegistry"/>. Diyalog Start with Windows'u Windows'un GERÇEK kaydından gösterir ve Save
/// kaydı ANINDA yazar — yalnız kullanıcı anahtarı değiştirdiyse.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class StartWithWindowsTests
{
    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    // ---------------------------------------------------------------- tablo: kayıtlı / varsayılan

    /// <summary>Kayıtlı değer kataloğun varsayılanının ÖNÜNE geçer; hiç kaydedilmemişse ikisi de varsayılanı (kapalı)
    /// okur.</summary>
    [Fact]
    public void A_startup_switch_reads_its_saved_value_or_else_its_catalog_default()
    {
        Assert.False(ShellSwitches.StartWithWindows(new UiState()));
        Assert.False(ShellSwitches.StartMinimizedToTray(new UiState()));

        var saved = new UiState { Autostart = true, StartMinimizedToTray = true };
        Assert.True(ShellSwitches.StartWithWindows(saved));
        Assert.True(ShellSwitches.StartMinimizedToTray(saved));
    }

    /// <summary>Diskteki AÇIK bir <c>null</c> token'ı (elle düzenleme) <see cref="UiState.UpdateExternals"/>'ın deseniyle
    /// tolere edilir: o anahtar kataloğun varsayılanına döner, diğer anahtar ve dosyadaki DİĞER alanlar (ör.
    /// <see cref="UiState.LayoutMode"/>) KORUNUR. <c>Autostart</c> eskiden <c>bool</c>'du — bu token tüm dosyayı
    /// düşürüp yerleşimi sıfırlıyordu.</summary>
    [Fact]
    public void The_startup_switches_survive_the_json_store_and_a_null_token_reads_as_the_default()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        new JsonUiStateStore(path).Save(new UiState { Autostart = true, StartMinimizedToTray = true, LayoutMode = LayoutMode.List });

        Assert.True(ShellSwitches.StartMinimizedToTray(new JsonUiStateStore(path).Load()));

        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Autostart\": true", "\"Autostart\": null"));
        Assert.Contains("\"Autostart\": null", File.ReadAllText(path), StringComparison.Ordinal); // ön-koşul: token gerçekten yazıldı
        var reloaded = new JsonUiStateStore(path).Load();

        Assert.False(ShellSwitches.StartWithWindows(reloaded));     // null token → katalog varsayılanı (kapalı)
        Assert.True(ShellSwitches.StartMinimizedToTray(reloaded));  // diğer anahtar KORUNUR
        Assert.Equal(LayoutMode.List, reloaded.LayoutMode);         // dosyadaki diğer alanlar KORUNUR
    }

    // ---------------------------------------------------------------- taslak

    /// <summary>Diyalog açılışının ctor'u: <c>saved</c> verildiyse iki satır da kayıtlı değerden tohumlanır.</summary>
    [Fact]
    public void The_draft_opens_on_the_saved_values()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo",
            saved: new UiState { Autostart = true, StartMinimizedToTray = true });

        Assert.True(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
    }

    // ---------------------------------------------------------------- Save (Windows yüzeyi yok: yalnız kalıcılık)

    /// <summary>Save değişen anahtarı yazar ve notunu BİR kez düşer; dokunulmayan anahtar sessiz kalır (desen:
    /// <see cref="StashOnBranchSwitchTests.A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet"/>).</summary>
    [Fact]
    public async Task Save_writes_the_changed_startup_switch_and_notes_only_that_one()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load());
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true; // Start with Windows kapalı kalır

        await draft.CommitAsync(run, store);

        Assert.True(store.State.StartMinimizedToTray);
        Assert.False(ShellSwitches.StartWithWindows(store.State));
        string console = run.GetRunDocumentText();
        Assert.Equal(1, Count(console,
            "Start minimized to tray on — signing in to Windows starts the app in the tray, without a window"));
        Assert.DoesNotContain("Start with Windows", console, StringComparison.Ordinal);
    }

    /// <summary>Start with Windows açılıp kaydedilir ve not düşer; yeniden açılan diyalogda kapatılınca o da yazılır.</summary>
    [Fact]
    public async Task Turning_start_with_windows_on_and_off_saves_it_and_notes_each_change()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();

        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load());
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        await draft.CommitAsync(run, store);

        Assert.True(ShellSwitches.StartWithWindows(store.State));
        Assert.Equal(1, Count(run.GetRunDocumentText(), "Start with Windows on — the app starts when you sign in to Windows"));

        var reopened = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load());
        Assert.True(reopened.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        reopened.GeneralRow(GeneralSetting.StartWithWindows).IsOn = false;
        await reopened.CommitAsync(run, store);

        Assert.False(ShellSwitches.StartWithWindows(store.State));
        Assert.Equal(1, Count(run.GetRunDocumentText(), "Start with Windows off — signing in to Windows no longer starts the app"));
    }

    // ---------------------------------------------------------------- dosya biçimi

    [Fact]
    public void The_startup_switches_round_trip_through_the_settings_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        string json = draft.ToFile().ToJson();
        Assert.Contains("\"startWithWindows\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"startMinimizedToTray\": true", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.True(loaded.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(loaded.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
    }

    /// <summary>Anahtarları taşımayan (eski) bir dosya formdaki değerleri sıfırlamaz — pull/stash bayraklarının kuralı.
    /// (Olumsuz durumun pini: uygulama öncesi de geçer; round-trip testi okumanın gerçekten yapıldığını kanıtlar.)</summary>
    [Fact]
    public void A_file_without_the_startup_switches_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.True(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
    }

    // ---------------------------------------------------------------- Windows kaydı: gerçek durum + Save anında

    /// <summary>Diyalog Start with Windows'u Windows'un GERÇEK kaydından gösterir, kayıtlı tercihten değil: tercih
    /// "açık" ama Run değeri yoksa (ör. dışarıdan silinmiş) anahtar kapalı görünür; değer varsa açık.</summary>
    [Fact]
    public void The_draft_shows_the_windows_startup_entry_rather_than_the_saved_preference()
    {
        var registry = new FakeAutostartRegistry();
        var missing = new SettingsDraftViewModel(null, @"D:\repo",
            saved: new UiState { Autostart = true }, autostart: registry.Service());
        Assert.False(missing.GeneralRow(GeneralSetting.StartWithWindows).IsOn);

        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        var present = new SettingsDraftViewModel(null, @"D:\repo", saved: new UiState(), autostart: registry.Service());
        Assert.True(present.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
    }

    /// <summary>Anahtarı açıp Save → Windows'un başlangıç kaydı ANINDA yazılır (yeniden başlatma beklenmez): tırnaklı
    /// exe yolu + autostart argümanı; tercih kaydedilir ve konsola not düşer.</summary>
    [Fact]
    public async Task Turning_start_with_windows_on_writes_the_windows_startup_entry_at_once()
    {
        var registry = new FakeAutostartRegistry();
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        await draft.CommitAsync(run, store);

        Assert.Equal(FakeAutostartRegistry.Command, registry.CommandFor(AutostartService.DefaultValueName));
        Assert.True(ShellSwitches.StartWithWindows(store.State));
        Assert.Equal(1, Count(run.GetRunDocumentText(), "Start with Windows on — the app starts when you sign in to Windows"));
    }

    /// <summary>Anahtarı kapatıp Save → kayıt ANINDA silinir; tercih kapanır ve not düşer.</summary>
    [Fact]
    public async Task Turning_start_with_windows_off_removes_the_windows_startup_entry_at_once()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        store.Save(new UiState { Autostart = true });
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        Assert.True(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn); // ön-koşul: açık açıldı
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = false;

        await draft.CommitAsync(run, store);

        Assert.False(registry.Exists(AutostartService.DefaultValueName));
        Assert.False(ShellSwitches.StartWithWindows(store.State));
        Assert.Equal(1, Count(run.GetRunDocumentText(), "Start with Windows off — signing in to Windows no longer starts the app"));
    }

    /// <summary>Anahtara DOKUNULMADAN başka bir ayar için Save → Windows kaydına da tercihe de dokunulmaz, not
    /// düşmez. Senaryo: tercih açık ama kayıt yok, diyalog kapalı gösterir; kullanıcı yalnız Start minimized'ı
    /// değiştirir. Save'in "kapalı"yı sessizce tercihe yazması, kullanıcının vermediği bir karar olurdu.</summary>
    [Fact]
    public async Task A_save_that_does_not_touch_start_with_windows_leaves_the_entry_and_the_preference_alone()
    {
        var registry = new FakeAutostartRegistry();
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        store.Save(new UiState { Autostart = true });
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        await draft.CommitAsync(run, store);

        Assert.Equal(0, registry.Writes);
        Assert.True(store.State.Autostart);
        Assert.DoesNotContain("Start with Windows", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>Windows kaydı yazamazsa Save uygulamayı DÜŞÜRMEZ: konsola nedenle tek satır düşer, tercih DEĞİŞMEZ
    /// (bir sonraki açılış diyaloğu gerçek durumla açar) ve "on" notu yazılmaz — olmayan bir değişimi söylemez.</summary>
    [Fact]
    public async Task When_windows_refuses_the_startup_entry_the_save_does_not_crash_and_says_so()
    {
        var registry = new FakeAutostartRegistry { FailWritesWith = new UnauthorizedAccessException("Access is denied.") };
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        Assert.Null(await Record.ExceptionAsync(() => draft.CommitAsync(run, store)));

        string console = run.GetRunDocumentText();
        Assert.Equal(1, Count(console, "Start with Windows not changed — Access is denied."));
        Assert.DoesNotContain("Start with Windows on", console, StringComparison.Ordinal);
        Assert.False(ShellSwitches.StartWithWindows(store.State));
        Assert.False(registry.Exists(AutostartService.DefaultValueName));
    }

    // ---------------------------------------------------------------- Görev Yöneticisi (StartupApproved\Run)

    /// <summary>Windows'un işareti: <c>StartupApproved\Run</c>'daki ikili değerin ilk baytı TEK ise kayıt devre dışıdır.
    /// Görev Yöneticisi kapatınca <c>03</c> + kapatıldığı an, açınca <c>02</c> + sıfırlar yazar (aşağıdaki iki değer bir
    /// Windows 11 makinesinden okundu); değer yoksa Windows kaydı etkin sayar.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("02 00 00 00 00 00 00 00 00 00 00 00", false)]
    [InlineData("03 00 00 00 34 25 E4 74 1F 3C DC 01", true)]
    [InlineData("06 00 00 00 00 00 00 00 00 00 00 00", false)]
    [InlineData("07 00 00 00 00 00 00 00 00 00 00 00", true)]
    public void Task_managers_marker_reads_as_disabled_when_its_first_byte_is_odd(string? hex, bool disabled)
    {
        byte[]? data = hex is null ? null : Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));

        Assert.Equal(disabled, StartupApproval.IsDisabled(data));
    }

    /// <summary>Gerçek durumun üç hâli: kayıt yok → kapalı; kayıt var → açık; kayıt var ama Görev Yöneticisi'nde devre
    /// dışı → Windows onu başlatmaz.</summary>
    [Fact]
    public void The_windows_state_is_off_on_or_disabled_in_startup_apps()
    {
        var registry = new FakeAutostartRegistry();
        var service = registry.Service();
        Assert.Equal(AutostartState.Off, service.State);

        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        Assert.Equal(AutostartState.On, service.State);

        registry.DisableInStartupApps(AutostartService.DefaultValueName);
        Assert.Equal(AutostartState.DisabledInStartupApps, service.State);
    }

    /// <summary>Görev Yöneticisi'nde kapatılmış kayıt: anahtar KAPALI açılır (Windows onu başlatmayacak) ve satırın
    /// açıklaması bunu söyler; kayıt etkinken satır kataloğun açıklamasını taşır.</summary>
    [Fact]
    public void An_entry_turned_off_in_task_manager_opens_off_with_a_note_that_says_so()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        var enabled = new SettingsDraftViewModel(null, @"D:\repo", saved: new UiState { Autostart = true }, autostart: registry.Service());
        Assert.Equal(GeneralSettingsCatalog.Definition(GeneralSetting.StartWithWindows).Description,
            enabled.GeneralRow(GeneralSetting.StartWithWindows).Description);

        registry.DisableInStartupApps(AutostartService.DefaultValueName);
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: new UiState { Autostart = true }, autostart: registry.Service());

        var row = draft.GeneralRow(GeneralSetting.StartWithWindows);
        Assert.False(row.IsOn);
        Assert.Equal(SettingsDraftViewModel.StartWithWindowsDisabledInStartupAppsNote, row.Description);
    }

    /// <summary>[Kullanıcı kararı — seçenek 1] Görev Yöneticisi'nde kapatılmış kaydı Settings'te açıp Save → Görev
    /// Yöneticisi'nin "devre dışı" işareti kaldırılır: Windows uygulamayı yeniden başlatır, Görev Yöneticisi "Etkin"
    /// gösterir. Kullanıcının en son, açıkça verdiği karar budur.</summary>
    [Fact]
    public async Task Switching_it_back_on_clears_task_managers_mark_and_starts_with_windows_again()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        registry.DisableInStartupApps(AutostartService.DefaultValueName);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        store.Save(new UiState { Autostart = true });
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        await draft.CommitAsync(run, store);

        Assert.Equal(AutostartState.On, registry.Service().State);
        Assert.True(ShellSwitches.StartWithWindows(store.State));
        Assert.Equal(1, Count(run.GetRunDocumentText(), "Start with Windows on — the app starts when you sign in to Windows"));
    }

    /// <summary>Görev Yöneticisi'nin kararı SESSİZCE ezilmez: anahtara dokunulmayan bir Save (başka bir ayar için)
    /// ne Windows kaydına ne tercihe dokunur — kayıt Görev Yöneticisi'nde "devre dışı" olarak kalır ve kullanıcı onu
    /// orada da yeniden açabilir.</summary>
    [Fact]
    public async Task A_save_that_does_not_touch_the_switch_keeps_task_managers_choice()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        registry.DisableInStartupApps(AutostartService.DefaultValueName);
        int writesBefore = registry.Writes;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        store.Save(new UiState { Autostart = true });
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: store.Load(), autostart: registry.Service());
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        await draft.CommitAsync(run, store);

        Assert.Equal(writesBefore, registry.Writes);
        Assert.Equal(AutostartState.DisabledInStartupApps, registry.Service().State);
        Assert.True(store.State.Autostart);
    }

    /// <summary>Açılışın uzlaştırması (her açılışta Run değerini yeniden yazar) Görev Yöneticisi'nin işaretine
    /// DOKUNMAZ — işareti yalnız kullanıcının Settings'te anahtarı açması kaldırır.</summary>
    [Fact]
    public void The_startup_reconcile_does_not_override_task_managers_choice()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(AutostartService.DefaultValueName, FakeAutostartRegistry.Command);
        registry.DisableInStartupApps(AutostartService.DefaultValueName);

        registry.Service().Apply(true);

        Assert.Equal(AutostartState.DisabledInStartupApps, registry.Service().State);
    }

    /// <summary>Görev Yöneticisi → Başlangıç uygulamaları (ve İşlemler sekmesi) bir Run kaydını hedef exe'nin dosya
    /// açıklamasıyla (FileDescription) adlandırır. Açıklama SDK varsayılanında kalınca derleme adı
    /// ("BuildOrchestrator.App") görünürdü; ürün adı tek kaynaktan gelir (Directory.Build.props → Product).</summary>
    [Fact]
    public void Task_manager_lists_the_startup_entry_under_the_product_name()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.App.exe");
        Assert.True(File.Exists(exe), $"the app host is not next to the tests: {exe}"); // ön-koşul

        Assert.Equal(AppIdentity.Product, FileVersionInfo.GetVersionInfo(exe).FileDescription);
    }

    /// <summary>Kablo: MainWindow, DI'dan aldığı servisi Settings diyaloğuna verir (üretimde tek örnek — açılışın
    /// uzlaştırması da onu kullanır).</summary>
    [StaFact]
    public void The_main_window_hands_its_autostart_service_to_the_settings_dialog()
    {
        using var temp = new TempDir();
        var service = new FakeAutostartRegistry().Service();

        var (window, _) = New(temp, autostart: service);

        Assert.Same(service, window.SettingsOverlay.Autostart);
    }

    /// <summary>Kablo (kaynak guard'ı — App headless kurulamaz): gerçek registry'ye giden yazıcı App ağacında TEK
    /// yerde, composition root'ta kurulur ve DI'a TEK servis olarak girer; açılışın uzlaştırması da o servisi
    /// kullanır (MainWindow onu DI'dan alıp Settings'e verir). Testler gerçek yazıcıyı ASLA kurmaz.</summary>
    [Fact]
    public void The_real_registry_writer_is_built_once_in_the_composition_root_and_never_in_tests()
    {
        var rule = new Regex(@"new\s+RegistryAutostartRegistry\s*\(");
        string hit = Assert.Single(SourceGuard.ScanApp("*.cs", rule, skipCommentLines: true));
        Assert.StartsWith("App.xaml.cs:", hit, StringComparison.Ordinal);
        Assert.Empty(SourceGuard.ScanTests("*.cs", rule, skipCommentLines: true));

        // Beklenen biçimler regex ile yazılır: düz metin olarak yazılsaydı yukarıdaki tarama bu dosyayı da yakalardı.
        string app = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Matches(@"sc\.AddSingleton\(_ => new AutostartService\(new\s+RegistryAutostartRegistry\(\)", app);
        Assert.Contains("Services.GetRequiredService<AutostartService>().Apply(", app, StringComparison.Ordinal);
    }
}
