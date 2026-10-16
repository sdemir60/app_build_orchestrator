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
/// yoksa formdaki değer korunur) ve değer değişince konsola tek satır not düşer (değişmezse sessiz). Bu zincir dört kabuk anahtarı
/// için birlikte <see cref="GeneralSettingChainTests"/>'tedir; burada iki başlangıç anahtarına özgü olan durur.</para>
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

    /// <summary>Start with Windows'un iki notu — metinler zincir tablosunda pinlidir (<see cref="GeneralSettingChains"/>).</summary>
    private static readonly string OnNote = GeneralSettingChains.Of(GeneralSetting.StartWithWindows).Shell!.OnNote;
    private static readonly string OffNote = GeneralSettingChains.Of(GeneralSetting.StartWithWindows).Shell!.OffNote;

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

    // [B3 · katlama] Kabuk anahtarlarının ortak zinciri — kayıtlı/varsayılan okuma, ui-state dosyasındaki null token, taslağın kayıtlı
    // değerle açılışı, Windows yüzeyi olmadan Save'in yazdığı ve notları (iki yön), ayar dosyası — dört anahtar için birlikte
    // GeneralSettingChainTests'tedir (bu sınıfın yedi ikiz testi oraya katlandı; eşleme B3 raporunda).

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
        Assert.Equal(1, Occurrences(bench.ConsoleText,OnNote));
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
        Assert.Equal(1, Occurrences(bench.ConsoleText,OffNote));
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

        Assert.Equal(1, Occurrences(bench.ConsoleText,"Start with Windows not changed — Access is denied."));
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
        Assert.Equal(1, Occurrences(bench.ConsoleText,OnNote));
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

    /// <summary>Kaynak guard'ı (App headless kurulamaz): gerçek registry'ye giden yazıcı App ağacında YALNIZ İKİ bilinen
    /// yerde kurulur. (1) Composition root (<c>App.xaml.cs</c>): DI'a TEK servis olarak girer; açılışın uzlaştırması da
    /// o servisi kullanır (MainWindow onu DI'dan alıp Settings'e verir). (2) <c>Program.Main</c>'in Velopack kaldırma
    /// kancası (<c>Program.cs</c>): kanca modunda process WPF/DI kurulmadan biter, servis örneği yoktur — yazıcı doğrudan
    /// <c>AutostartService.RemoveForUninstall</c>'a verilir. Testler gerçek yazıcıyı ASLA kurmaz.
    /// <para>[yayın hattı · Task 2] Eski iddia: "TEK yerde, composition root'ta". Değişme gerekçesi: kaldırma kancası
    /// DI'dan ÖNCE koşar (Velopack ana exe'yi <c>--veloapp-uninstall</c> ile çalıştırır) ve kaldırmada Run değerinin
    /// silinmesi gerekir; oraya DI'daki servis taşınamaz. Kural gevşetilmedi, kapı sayısı kadar daraltıldı: üçüncü bir
    /// kurulum yeri ya da kancanın başka bir biçimde kurulması hâlâ kırmızı verir.</para></summary>
    [Fact]
    public void The_real_registry_writer_is_built_only_in_the_composition_root_and_the_uninstall_hook_and_never_in_tests()
    {
        var rule = new Regex(@"new\s+RegistryAutostartRegistry\s*\(");
        var hits = SourceGuard.ScanApp("*.cs", rule, skipCommentLines: true);
        Assert.Equal(2, hits.Count);
        Assert.Single(hits, h => h.StartsWith("App.xaml.cs:", StringComparison.Ordinal));
        Assert.Single(hits, h => h.StartsWith("Program.cs:", StringComparison.Ordinal));
        Assert.Empty(SourceGuard.ScanTests("*.cs", rule, skipCommentLines: true));

        // Beklenen biçimler regex ile yazılır: düz metin olarak yazılsaydı yukarıdaki tarama bu dosyayı da yakalardı.
        string app = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Matches(@"sc\.AddSingleton\(_ => new AutostartService\(new\s+RegistryAutostartRegistry\(\)", app);
        Assert.Contains("Services.GetRequiredService<AutostartService>().Apply(", app, StringComparison.Ordinal);

        string program = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Program.cs"));
        Assert.Matches(@"AutostartService\.RemoveForUninstall\(new\s+RegistryAutostartRegistry\(\)\)", program);
    }
}
