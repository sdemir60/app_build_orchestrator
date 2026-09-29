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
    private const string Root = @"D:\repo";
    private const string Entry = AutostartService.DefaultValueName;
    private const string OnNote = "Start with Windows on — the app starts when you sign in to Windows";
    private const string OffNote = "Start with Windows off — signing in to Windows no longer starts the app";

    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    /// <summary>Taslak — diyaloğun kurduğu gibi (<c>SettingsDialog.Open</c>).</summary>
    private static SettingsDraftViewModel NewDraft(UiState? saved = null, AutostartService? autostart = null) =>
        new(null, Root, saved: saved, autostart: autostart);

    /// <summary>Save'in tezgâhı — kurulum TEK yerde (kopya YASAK): motoru HİÇ başlatılmayan bir koşu VM'i (konsol
    /// notları için), bellek-içi store ve sahte Windows kaydı. Konak <c>new EngineHost(TestPaths.SupervisorExe)</c>
    /// biçiminde kurulur: <see cref="Supervisor.SupervisorIsolationGuardTests"/> başlatılmayan konağı bu biçimden
    /// tanır.</summary>
    private sealed class SaveBench : IAsyncDisposable
    {
        private readonly EngineHost _engine = new EngineHost(TestPaths.SupervisorExe);

        public SaveBench() => Run = new RunViewModel(_engine, NeverTickingBatcher(), () => "r1") { RootPath = Root };

        public RunViewModel Run { get; }
        public SettingsDialogHost.FakeStore Store { get; } = new();
        public FakeAutostartRegistry Registry { get; } = new();
        public string ConsoleText => Run.GetRunDocumentText();

        /// <summary>Diyaloğun açılışı: store'dan tohumlanan taslak; <paramref name="windows"/> ⇒ Windows kaydı yüzeyi
        /// (üretimde hep vardır; yalnız kalıcılığı sınayan testler onu kapatır).</summary>
        public SettingsDraftViewModel Open(bool windows = true) => NewDraft(Store.Load(), windows ? Registry.Service() : null);

        public Task SaveAsync(SettingsDraftViewModel draft) => draft.CommitAsync(Run, Store);

        /// <summary>Uygulamanın kaydı Windows'ta var (bir önceki Save'in ya da açılışın yazdığı gibi).</summary>
        public void RegisterEntry() => Registry.Set(Entry, FakeAutostartRegistry.Command);

        public ValueTask DisposeAsync() => _engine.DisposeAsync();
    }

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
        string path = UiStatePath(temp);
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
        var draft = NewDraft(new UiState { Autostart = true, StartMinimizedToTray = true });

        Assert.True(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
    }

    // ---------------------------------------------------------------- Save (Windows yüzeyi yok: yalnız kalıcılık)

    /// <summary>Save değişen anahtarı yazar ve notunu BİR kez düşer; dokunulmayan anahtar sessiz kalır (desen:
    /// <see cref="StashOnBranchSwitchTests.A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet"/>).</summary>
    [Fact]
    public async Task Save_writes_the_changed_startup_switch_and_notes_only_that_one()
    {
        await using var bench = new SaveBench();
        var draft = bench.Open(windows: false);
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true; // Start with Windows kapalı kalır

        await bench.SaveAsync(draft);

        Assert.True(bench.Store.State.StartMinimizedToTray);
        Assert.False(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText,
            "Start minimized to tray on — signing in to Windows starts the app in the tray, without a window"));
        Assert.DoesNotContain("Start with Windows", bench.ConsoleText, StringComparison.Ordinal);
    }

    /// <summary>Start with Windows açılıp kaydedilir ve not düşer; yeniden açılan diyalogda kapatılınca o da yazılır.</summary>
    [Fact]
    public async Task Turning_start_with_windows_on_and_off_saves_it_and_notes_each_change()
    {
        await using var bench = new SaveBench();
        var draft = bench.Open(windows: false);
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        await bench.SaveAsync(draft);

        Assert.True(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText, OnNote));

        var reopened = bench.Open(windows: false);
        Assert.True(reopened.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        reopened.GeneralRow(GeneralSetting.StartWithWindows).IsOn = false;
        await bench.SaveAsync(reopened);

        Assert.False(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText, OffNote));
    }

    // ---------------------------------------------------------------- dosya biçimi

    [Fact]
    public void The_startup_switches_round_trip_through_the_settings_file()
    {
        var draft = NewDraft();
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        string json = draft.ToFile().ToJson();
        Assert.Contains("\"startWithWindows\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"startMinimizedToTray\": true", json, StringComparison.Ordinal);

        var loaded = NewDraft();
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.True(loaded.GeneralRow(GeneralSetting.StartWithWindows).IsOn);
        Assert.True(loaded.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn);
    }

    /// <summary>Anahtarları taşımayan (eski) bir dosya formdaki değerleri sıfırlamaz — pull/stash bayraklarının kuralı.
    /// (Olumsuz durumun pini: uygulama öncesi de geçer; round-trip testi okumanın gerçekten yapıldığını kanıtlar.)</summary>
    [Fact]
    public void A_file_without_the_startup_switches_leaves_the_form_untouched()
    {
        var draft = NewDraft();
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        draft.LoadFrom(SettingsFile.From(Root, []));

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
        Assert.False(NewDraft(new UiState { Autostart = true }, registry.Service()).GeneralRow(GeneralSetting.StartWithWindows).IsOn);

        registry.Set(Entry, FakeAutostartRegistry.Command);
        Assert.True(NewDraft(new UiState(), registry.Service()).GeneralRow(GeneralSetting.StartWithWindows).IsOn);
    }

    /// <summary>Anahtarı açıp Save → Windows'un başlangıç kaydı ANINDA yazılır (yeniden başlatma beklenmez): tırnaklı
    /// exe yolu + autostart argümanı; tercih kaydedilir ve konsola not düşer.</summary>
    [Fact]
    public async Task Turning_start_with_windows_on_writes_the_windows_startup_entry_at_once()
    {
        await using var bench = new SaveBench();
        var draft = bench.Open();
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        await bench.SaveAsync(draft);

        Assert.Equal(FakeAutostartRegistry.Command, bench.Registry.CommandFor(Entry));
        Assert.True(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText, OnNote));
    }

    /// <summary>Anahtarı kapatıp Save → kayıt ANINDA silinir; tercih kapanır ve not düşer.</summary>
    [Fact]
    public async Task Turning_start_with_windows_off_removes_the_windows_startup_entry_at_once()
    {
        await using var bench = new SaveBench();
        bench.RegisterEntry();
        bench.Store.Save(new UiState { Autostart = true });
        var draft = bench.Open();
        Assert.True(draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn); // ön-koşul: açık açıldı
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = false;

        await bench.SaveAsync(draft);

        Assert.False(bench.Registry.Exists(Entry));
        Assert.False(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText, OffNote));
    }

    /// <summary>Anahtara DOKUNULMADAN başka bir ayar için Save → Windows kaydına da tercihe de dokunulmaz, not
    /// düşmez. Senaryo: tercih açık ama kayıt yok, diyalog kapalı gösterir; kullanıcı yalnız Start minimized'ı
    /// değiştirir. Save'in "kapalı"yı sessizce tercihe yazması, kullanıcının vermediği bir karar olurdu.</summary>
    [Fact]
    public async Task A_save_that_does_not_touch_start_with_windows_leaves_the_entry_and_the_preference_alone()
    {
        await using var bench = new SaveBench();
        bench.Store.Save(new UiState { Autostart = true });
        var draft = bench.Open();
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        await bench.SaveAsync(draft);

        Assert.Equal(0, bench.Registry.Writes);
        Assert.True(bench.Store.State.Autostart);
        Assert.DoesNotContain("Start with Windows", bench.ConsoleText, StringComparison.Ordinal);
    }

    /// <summary>Windows kaydı yazamazsa Save uygulamayı DÜŞÜRMEZ: konsola nedenle tek satır düşer, tercih DEĞİŞMEZ
    /// (bir sonraki açılış diyaloğu gerçek durumla açar) ve "on" notu yazılmaz — olmayan bir değişimi söylemez.</summary>
    [Fact]
    public async Task When_windows_refuses_the_startup_entry_the_save_does_not_crash_and_says_so()
    {
        await using var bench = new SaveBench();
        bench.Registry.FailWritesWith = new UnauthorizedAccessException("Access is denied.");
        var draft = bench.Open();
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        Assert.Null(await Record.ExceptionAsync(() => bench.SaveAsync(draft)));

        Assert.Equal(1, Count(bench.ConsoleText, "Start with Windows not changed — Access is denied."));
        Assert.DoesNotContain("Start with Windows on", bench.ConsoleText, StringComparison.Ordinal);
        Assert.False(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.False(bench.Registry.Exists(Entry));
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

        registry.Set(Entry, FakeAutostartRegistry.Command);
        Assert.Equal(AutostartState.On, service.State);

        registry.DisableInStartupApps(Entry);
        Assert.Equal(AutostartState.DisabledInStartupApps, service.State);
    }

    /// <summary>Görev Yöneticisi'nde kapatılmış kayıt: anahtar KAPALI açılır (Windows onu başlatmayacak) ve satırın
    /// açıklaması bunu söyler; kayıt etkinken satır kataloğun açıklamasını taşır.</summary>
    [Fact]
    public void An_entry_turned_off_in_task_manager_opens_off_with_a_note_that_says_so()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(Entry, FakeAutostartRegistry.Command);
        Assert.Equal(GeneralSettingsCatalog.Definition(GeneralSetting.StartWithWindows).Description,
            NewDraft(new UiState { Autostart = true }, registry.Service()).GeneralRow(GeneralSetting.StartWithWindows).Description);

        registry.DisableInStartupApps(Entry);
        var row = NewDraft(new UiState { Autostart = true }, registry.Service()).GeneralRow(GeneralSetting.StartWithWindows);

        Assert.False(row.IsOn);
        Assert.Equal(SettingsDraftViewModel.StartWithWindowsDisabledInStartupAppsNote, row.Description);
    }

    /// <summary>[Kullanıcı kararı — seçenek 1] Görev Yöneticisi'nde kapatılmış kaydı Settings'te açıp Save → Görev
    /// Yöneticisi'nin "devre dışı" işareti kaldırılır: Windows uygulamayı yeniden başlatır, Görev Yöneticisi "Etkin"
    /// gösterir. Kullanıcının en son, açıkça verdiği karar budur.</summary>
    [Fact]
    public async Task Switching_it_back_on_clears_task_managers_mark_and_starts_with_windows_again()
    {
        await using var bench = new SaveBench();
        bench.RegisterEntry();
        bench.Registry.DisableInStartupApps(Entry);
        bench.Store.Save(new UiState { Autostart = true });
        var draft = bench.Open();
        draft.GeneralRow(GeneralSetting.StartWithWindows).IsOn = true;

        await bench.SaveAsync(draft);

        Assert.Equal(AutostartState.On, bench.Registry.Service().State);
        Assert.True(ShellSwitches.StartWithWindows(bench.Store.State));
        Assert.Equal(1, Count(bench.ConsoleText, OnNote));
    }

    /// <summary>Görev Yöneticisi'nin kararı SESSİZCE ezilmez: anahtara dokunulmayan bir Save (başka bir ayar için)
    /// ne Windows kaydına ne tercihe dokunur — kayıt Görev Yöneticisi'nde "devre dışı" olarak kalır ve kullanıcı onu
    /// orada da yeniden açabilir.</summary>
    [Fact]
    public async Task A_save_that_does_not_touch_the_switch_keeps_task_managers_choice()
    {
        await using var bench = new SaveBench();
        bench.RegisterEntry();
        bench.Registry.DisableInStartupApps(Entry);
        bench.Store.Save(new UiState { Autostart = true });
        int writesBefore = bench.Registry.Writes;
        var draft = bench.Open();
        draft.GeneralRow(GeneralSetting.StartMinimizedToTray).IsOn = true;

        await bench.SaveAsync(draft);

        Assert.Equal(writesBefore, bench.Registry.Writes);
        Assert.Equal(AutostartState.DisabledInStartupApps, bench.Registry.Service().State);
        Assert.True(bench.Store.State.Autostart);
    }

    /// <summary>Açılışın uzlaştırması (her açılışta Run değerini yeniden yazar) Görev Yöneticisi'nin işaretine
    /// DOKUNMAZ — işareti yalnız kullanıcının Settings'te anahtarı açması kaldırır.</summary>
    [Fact]
    public void The_startup_reconcile_does_not_override_task_managers_choice()
    {
        var registry = new FakeAutostartRegistry();
        registry.Set(Entry, FakeAutostartRegistry.Command);
        registry.DisableInStartupApps(Entry);

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

    // ---------------------------------------------------------------- kablo

    /// <summary>MainWindow, DI'dan aldığı servisi Settings diyaloğuna verir (üretimde tek örnek — açılışın uzlaştırması
    /// da onu kullanır).</summary>
    [StaFact]
    public void The_main_window_hands_its_autostart_service_to_the_settings_dialog()
    {
        using var temp = new TempDir();
        var service = new FakeAutostartRegistry().Service();

        var (window, _) = New(temp, autostart: service);

        Assert.Same(service, window.SettingsOverlay.Autostart);
    }

    /// <summary>Kaynak guard'ı (App headless kurulamaz): gerçek registry'ye giden yazıcı App ağacında TEK yerde,
    /// composition root'ta kurulur ve DI'a TEK servis olarak girer; açılışın uzlaştırması da o servisi kullanır
    /// (MainWindow onu DI'dan alıp Settings'e verir). Testler gerçek yazıcıyı ASLA kurmaz.</summary>
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
