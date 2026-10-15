using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory dalga 3 / spec §6.4 — Task 19] Settings → General → ENGINE: <c>Stop the engine when idle</c>. Açıkken (varsayılan)
/// beş dakika işsiz kalan motor süreci çıkar ve sonraki iş onu yeniden başlatır; kapalıyken motor kendiliğinden hiç durdurulmaz.
///
/// <para>Bu sınıf YALNIZ ayarın zincirini sınar: katalog → taslak → ayar dosyası → Save (UiState +
/// <see cref="RunViewModel.StopEngineWhenIdle"/> aynı commit'te) → kabuğun açılış seed'i ve kalıcılığı. Uykunun KENDİSİ bu sınıfın konusu
/// değildir: boşta kuralı ve tik <see cref="EngineSleepTests"/>'te, uyandırma kapısı Task 21'de. Desen <see cref="CompactConsoleSettingsTests"/>'in aynısıdır;
/// iki fark vardır: (a) "beş dakika"nın TEK sahibi <see cref="EngineIdleRule.IdleAfter"/>'dır — katalog açıklaması (spec'in birebir
/// cümlesi) ile "on" notu sayıyı sözcükle yazar; sabitin literal pini kuralın testindedir (<see cref="EngineIdleRuleTests"/>, karar 29), iki
/// metnin sabitle ayrışmasını bu sınıfın testi yakalar; (b) fixture ayarı varsayılan KAPALI açar (<see cref="MainWindowHost.New"/>'in
/// <c>stopEngineWhenIdle</c> parametresi) — saati ileri alan testler fixture motorunu uyutmasın.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class StopEngineWhenIdleSettingsTests
{
    private const string Label = "Stop the engine when idle";

    private const string OnNote = $"{Label} on — the engine exits after five idle minutes";
    private const string OffNote = $"{Label} off — the engine is no longer stopped when idle";

    private static GeneralSettingDefinition Row() =>
        Assert.Single(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows), r => r.Label == Label);

    [Fact]
    public async Task The_switch_is_on_by_default()
    {
        Assert.True(Row().Default);
        Assert.True(new SettingsDraftViewModel(null, @"D:\repo").StopEngineWhenIdle);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        Assert.True(new RunViewModel(engine, NeverTickingBatcher(), () => "r1").StopEngineWhenIdle);
    }

    /// <summary>Satır ve taslak bayrağı TEK değerdir: satır değişince taslağın özelliği de bildirir
    /// (<see cref="SettingsGeneralPageTests.The_pull_row_and_the_draft_flag_are_one_value"/> deseni).</summary>
    [Fact]
    public void The_row_and_the_draft_flag_are_one_value()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        var notified = new List<string?>();
        draft.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        draft.GeneralRow(GeneralSetting.StopEngineWhenIdle).IsOn = false;
        Assert.False(draft.StopEngineWhenIdle);
        Assert.Contains(nameof(SettingsDraftViewModel.StopEngineWhenIdle), notified);

        draft.StopEngineWhenIdle = true;
        Assert.True(draft.GeneralRow(GeneralSetting.StopEngineWhenIdle).IsOn);
    }

    /// <summary>
    /// Varsayılanın TEK tanımı kataloğun satırıdır (<see cref="GeneralSettingDefinition.Default"/>): taze bir VM, taze bir taslak ve
    /// anahtarı taşımayan bir ui-state'ten açılan kabuk onu gösterir — kendi literalini taşıyan bir yer kataloğun değeri değişince ondan
    /// ayrışır ve bu test kırmızı verir (<see cref="CompactConsoleSettingsTests.A_fresh_vm_a_fresh_draft_and_a_key_less_seed_show_the_catalogs_default"/>
    /// deseni; aynı tuzak bu ayarda baştan kapalıdır).
    /// </summary>
    [StaFact]
    public void A_fresh_vm_a_fresh_draft_and_a_key_less_seed_show_the_catalogs_default()
    {
        bool catalog = GeneralSettingsCatalog.Definition(GeneralSetting.StopEngineWhenIdle).Default;
        var fresh = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        Assert.Equal(catalog, fresh.StopEngineWhenIdle);
        Assert.Equal(catalog, new SettingsDraftViewModel(null, @"D:\repo").StopEngineWhenIdle);

        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState());                          // anahtar yok
        var (window, seeded) = New(temp, stopEngineWhenIdle: null);      // pencerenin KENDİ seed'i
        Assert.Equal(catalog, seeded.StopEngineWhenIdle);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Ayarın etiketi kataloğun satırında BİR KEZ yazılır; not satırı (<c>RunViewModel</c>) ve başka hiçbir üretim kaynağı onu literal
    /// olarak tekrarlamaz — etiket değişirse not kendiliğinden izler (<see cref="OnNote"/>/<see cref="OffNote"/> etiketi katalogdan alan
    /// bir metindir). Tarama yalnız string literallerine bakar (yorumlar hariç); vakum karşıtı olarak katalogdaki literalin bulunduğu
    /// ayrıca doğrulanır.
    /// </summary>
    [Fact]
    public void The_label_is_a_literal_only_in_the_catalog()
    {
        var rule = new Regex(Regex.Escape(Label), RegexOptions.CultureInvariant);
        string catalogFile = Path.Combine("BuildOrchestrator.App", "ViewModels", "GeneralSettings.cs");

        Assert.NotEmpty(SourceGuard.ScanSrcLiterals("GeneralSettings.cs", rule)); // tarama kataloğun literalini GÖRÜYOR
        var offenders = SourceGuard.ScanSrcLiterals("*.cs", rule, allowedFiles: [catalogFile]);

        Assert.True(offenders.Count == 0,
            "etiket kataloğun dışında literal olarak yazılmış (kopya YASAK):" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", offenders));
    }

    // ---------------------------------------------------------------- "beş dakika" tek sahip

    // [karar 29] Sabitin literal pini (5 dk) kuralın sınıfıyla birlikte durur: EngineIdleRuleTests.The_idle_window_stays_five_minutes.
    // Burada kalan, iki kullanıcı metninin o sabitle uyuşmasıdır.

    /// <summary>
    /// [karar 11 · C18] Katalog açıklaması (spec'in birebir cümlesi) ve "on" notu süreyi SÖZCÜKLE yazar; ikisi de sabitin bugünkü değeriyle
    /// aynı sözcüğü söylemelidir. Sabit değişirse (ör. 10 dk) bu test, iki metin ve aşağıdaki sözcük tablosu birlikte güncellenene dek
    /// kırmızıdır — metin sessizce yalan olamaz. "Beş" sözcüğü yalnız bu tabloda (ve iki metinde) yazılıdır.
    /// </summary>
    [Fact]
    public async Task The_catalog_text_and_the_on_note_say_the_window_the_rule_uses()
    {
        string words = EngineIdleRule.IdleAfter.TotalMinutes switch
        {
            5 => "five",
            double minutes => throw new Xunit.Sdk.XunitException(
                $"EngineIdleRule.IdleAfter {minutes} dakika oldu: katalog açıklaması, \"on\" notu ve bu tablo birlikte güncellenmeli."),
        };

        Assert.Contains($"After {words} minutes without work", Row().Description, StringComparison.Ordinal);

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", StopEngineWhenIdle = false };
        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: true);
        Assert.Contains($"the engine exits after {words} idle minutes", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- dosya biçimi

    /// <summary>Export anahtarı yazar, Import geri okur (<see cref="CompactConsoleSettingsTests.The_setting_round_trips_through_the_file"/> deseni).
    /// Varsayılan DIŞI değer (kapalı) kullanılır: bağlantı koparsa form varsayılanda (açık) kalır ve test kırmızı verir. Anahtarın dosyadaki
    /// yeri de pinlidir: System.Text.Json alanları BİLDİRİM sırasıyla yazar — <c>compactConsole</c>'dan hemen sonra, kabuk anahtarlarının
    /// ilki olan <c>startWithWindows</c>'ten önce (<c>SettingsFile</c>'ın sınıf özeti bu sırayı söyler).</summary>
    [Fact]
    public void The_setting_round_trips_through_the_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", stopEngineWhenIdle: false);
        string json = draft.ToFile().ToJson();
        Assert.Contains("\"stopEngineWhenIdle\": false", json, StringComparison.Ordinal);

        int compactIndex = json.IndexOf("\"compactConsole\"", StringComparison.Ordinal);
        int engineIndex = json.IndexOf("\"stopEngineWhenIdle\"", StringComparison.Ordinal);
        int shellIndex = json.IndexOf("\"startWithWindows\"", StringComparison.Ordinal);
        Assert.True(compactIndex >= 0 && compactIndex < engineIndex,
            $"stopEngineWhenIdle ({engineIndex}) compactConsole'dan ({compactIndex}) SONRA gelmeli");
        Assert.True(engineIndex < shellIndex, $"stopEngineWhenIdle ({engineIndex}) startWithWindows'tan ({shellIndex}) ÖNCE gelmeli");

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.False(loaded.StopEngineWhenIdle);
    }

    /// <summary>Anahtarı taşımayan (eski) bir dosya formdaki değeri varsayılana döndürmez — pull/stash/Resolve/Compact
    /// bayraklarının kuralı.</summary>
    [Fact]
    public void A_file_without_the_setting_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", stopEngineWhenIdle: false);

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.False(draft.StopEngineWhenIdle);
    }

    // ---------------------------------------------------------------- Save → VM

    /// <summary>Değişen ayar VM'e uygulanır ve konsola not düşer; değişmeyen sessiz kalır
    /// (<see cref="CompactConsoleSettingsTests.A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet"/> deseni).
    /// Kapatma notu "no longer stopped" der (kontrolcü kararı 10): ayarı kapatmak uykudaki motoru uyandırmaz, motor bir sonraki işe dek
    /// uykuda kalır — "motor çalışıyor" demek o an yanlış olurdu.</summary>
    [Fact]
    public async Task A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: true);
        Assert.DoesNotContain(Label, run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: false);
        Assert.False(run.StopEngineWhenIdle);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OffNote));

        // Aynı değerle ikinci Save: değişmeyen ayar sessizdir.
        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: false);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OffNote));

        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: true);
        Assert.True(run.StopEngineWhenIdle);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OnNote));
    }

    /// <summary>[dalga 2 · karar 16 deseni] Ayarı TAŞIMAYAN bir uygulama onu değiştirmez ve not yazmaz: parametre "verilmedi ⇒ olduğu gibi
    /// kalsın"dır (<c>null</c>). Settings'in Save'i değeri her zaman verir (<see cref="SettingsDraftViewModel.CommitAsync"/>); ayarı hiç
    /// anmayan bir çağıran — başka bir ayarı sınayan test — ayarı kendiliğinden açıp konsola not düşmemeli.</summary>
    [Fact]
    public async Task An_apply_that_does_not_carry_the_setting_leaves_it_unchanged_and_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", StopEngineWhenIdle = false };

        await run.ApplySettingsAsync([], @"D:\repo", []);

        Assert.False(run.StopEngineWhenIdle);
        Assert.DoesNotContain(Label, run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kabuk: seed + kalıcılık

    /// <summary>Açılışta kalıcı durumdan seed edilir; VM'de değişince kalıcı duruma yazılır. Fixture'ın ayar ataması kapatılır
    /// (<c>stopEngineWhenIdle: null</c>): test pencerenin KENDİ seed'ini gözler — fixture'ın varsayılanı (kapalı) onu ezerdi.</summary>
    [StaFact]
    public void The_shell_seeds_the_setting_and_persists_its_changes()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState { StopEngineWhenIdle = false });

        var (window, vm) = New(temp, stopEngineWhenIdle: null);

        Assert.False(vm.StopEngineWhenIdle);
        vm.StopEngineWhenIdle = true;
        Assert.True(UiStateStore(temp).Load().StopEngineWhenIdle);
        GC.KeepAlive(window);
    }

    /// <summary>Anahtarı taşımayan eski bir ui-state dosyası onaylanmış varsayılanı (açık) alır — pencerenin kendi seed'i
    /// (<c>stopEngineWhenIdle: null</c>: fixture'ın kapalı varsayılanı onu ezerdi).</summary>
    [StaFact]
    public void An_older_state_file_without_the_key_keeps_the_switch_on()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState());

        var (window, vm) = New(temp, stopEngineWhenIdle: null);

        Assert.True(vm.StopEngineWhenIdle);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [karar 9] Fixture sözleşmesi: <see cref="MainWindowHost.New"/> ayarı KAPALI açar (üretimin varsayılanı açıktır) — saati beş
    /// dakikadan fazla ileri alan testlerin fixture motorunu uyutmaması buna bağlıdır; uyku yalnız bunu açıkça isteyen testte
    /// (<c>stopEngineWhenIdle: true</c>) işler. Saatli topolojili kurulum (<see cref="MainWindowHost.NewWithProjectsAndClock"/>) da
    /// aynı varsayılandan başlar: uyku testleri o yoldan gelir.
    /// </summary>
    [StaFact]
    public void The_fixture_opens_with_the_setting_off_unless_the_test_opts_in()
    {
        using var defaultDir = new TempDir();
        var (offWindow, offVm) = New(defaultDir);
        Assert.False(offVm.StopEngineWhenIdle);

        using var optInDir = new TempDir();
        var (onWindow, onVm) = New(optInDir, stopEngineWhenIdle: true);
        Assert.True(onVm.StopEngineWhenIdle);

        using var clockedDir = new TempDir();
        var (clockedWindow, clockedVm, _) = NewWithProjectsAndClock(clockedDir, () => 0L, ("A", null));
        Assert.False(clockedVm.StopEngineWhenIdle);

        GC.KeepAlive(offWindow);
        GC.KeepAlive(onWindow);
        GC.KeepAlive(clockedWindow);
    }

    // ---------------------------------------------------------------- realize

    private static CheckBox EngineSwitch(SettingsDialog dialog) =>
        Assert.Single(DsResources.RealizedObjects(dialog.Page(SettingsSection.General)).OfType<CheckBox>(),
            c => AutomationProperties.GetName(c) == Label);

    /// <summary>Realize: satır General sayfasının tek <c>ToggleRow</c> şablonuyla çizilir; diyalog canlı değerle açılır
    /// (<c>SettingsDialog.Open</c> VM'in değerini taslağa geçirir), switch taslağın bayrağıdır ve Save onu uygular.</summary>
    [StaFact]
    public void The_general_page_realizes_the_switch_and_save_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(r => r.StopEngineWhenIdle = false);
        using var _scope = scope;

        var toggle = EngineSwitch(dialog);
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Assert.False(run.StopEngineWhenIdle); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(store.State.StopEngineWhenIdle);
        DispatcherPump.PumpUntil(() => run.StopEngineWhenIdle, TimeSpan.FromSeconds(5));
        Assert.True(run.StopEngineWhenIdle);
    }

    /// <summary>Kapatma yönü: varsayılanla (açık) açılan diyalogda switch kapatılıp Save edilince hem UiState hem VM kapalı olur —
    /// Save'in varsayılan DIŞI bir değeri uyguladığının pini (<see cref="CompactConsoleSettingsTests.Switching_the_setting_off_and_saving_applies_it"/>
    /// ile aynı gerekçe: parametre <c>bool?</c>'dur ve verilmemesi "dokunma" demektir, yani bu test Save'in değeri taşıdığını ayırt eder).</summary>
    [StaFact]
    public void Switching_the_setting_off_and_saving_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        Assert.True(run.StopEngineWhenIdle);

        EngineSwitch(dialog).IsChecked = false;
        Assert.True(run.StopEngineWhenIdle); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.False(store.State.StopEngineWhenIdle);
        DispatcherPump.PumpUntil(() => !run.StopEngineWhenIdle, TimeSpan.FromSeconds(5));
        Assert.False(run.StopEngineWhenIdle);
    }
}
