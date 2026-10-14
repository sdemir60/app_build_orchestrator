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
/// [idle-memory dalga 2 / spec §6.5 — K4] Settings → General → CONSOLE: <c>Compact console</c>. Açıkken (varsayılan) konsol
/// anlatısı proje çıktısını kompakt gösterir — hatalar tam, her projenin ilk uyarıları ve kalanın sayısı; kapalıyken her
/// satır (proje logu ve disk her zaman tam). Değişiklik bir SONRAKİ işlemde geçerli olur.
///
/// <para>Bu sınıf YALNIZ ayarın zincirini sınar: katalog → taslak → ayar dosyası → Save (UiState +
/// <see cref="RunViewModel.CompactConsole"/> aynı commit'te) → kabuğun açılış seed'i ve kalıcılığı. Desen
/// <see cref="ResolveAtFullPrioritySettingsTests"/>'in aynısıdır; tek fark, bayrağın motora GİTMEMESİdir (tel üzerinde
/// alanı yoktur — kompakt gösterim App'in işidir), bu yüzden o sınıfın "komut bayrağı taşır" testinin karşılığı yoktur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class CompactConsoleSettingsTests
{
    private const string Label = "Compact console";

    private const string OnNote = "Compact console on — errors, the first warnings of each project and a warning count from the next run";
    private const string OffNote = "Compact console off — every MSBuild line is shown from the next run";

    private static GeneralSettingDefinition Row() =>
        Assert.Single(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows), r => r.Label == Label);

    [Fact]
    public async Task The_switch_is_on_by_default()
    {
        Assert.True(Row().Default);
        Assert.True(new SettingsDraftViewModel(null, @"D:\repo").CompactConsole);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        Assert.True(new RunViewModel(engine, NeverTickingBatcher(), () => "r1").CompactConsole);
    }

    /// <summary>Satır ve taslak bayrağı TEK değerdir: satır değişince taslağın özelliği de bildirir
    /// (<see cref="SettingsGeneralPageTests.The_pull_row_and_the_draft_flag_are_one_value"/> deseni).</summary>
    [Fact]
    public void The_row_and_the_draft_flag_are_one_value()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        var notified = new List<string?>();
        draft.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        draft.GeneralRow(GeneralSetting.CompactConsole).IsOn = false;
        Assert.False(draft.CompactConsole);
        Assert.Contains(nameof(SettingsDraftViewModel.CompactConsole), notified);

        draft.CompactConsole = true;
        Assert.True(draft.GeneralRow(GeneralSetting.CompactConsole).IsOn);
    }

    // ---------------------------------------------------------------- dosya biçimi

    /// <summary>Export anahtarı yazar, Import geri okur (<see cref="ResolveAtFullPrioritySettingsTests"/> deseni).
    /// Varsayılan DIŞI değer (kapalı) kullanılır: bağlantı koparsa form varsayılanda (açık) kalır ve test kırmızı verir.</summary>
    [Fact]
    public void The_setting_round_trips_through_the_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", compactConsole: false);
        string json = draft.ToFile().ToJson();
        Assert.Contains("\"compactConsole\": false", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.False(loaded.CompactConsole);
    }

    /// <summary>Anahtarı taşımayan (eski) bir dosya formdaki değeri varsayılana döndürmez — pull/stash/Resolve
    /// bayraklarının kuralı.</summary>
    [Fact]
    public void A_file_without_the_setting_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", compactConsole: false);

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.False(draft.CompactConsole);
    }

    // ---------------------------------------------------------------- Save → VM

    /// <summary>Değişen ayar VM'e uygulanır ve konsola not düşer; değişmeyen sessiz kalır
    /// (<see cref="ResolveAtFullPrioritySettingsTests"/> deseni). Not "from the next run" der: değişiklik mevcut anlatıyı
    /// yeniden yazmaz, bir sonraki işlemde geçerli olur.</summary>
    [Fact]
    public async Task A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], compactConsole: true);
        Assert.DoesNotContain(Label, run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], compactConsole: false);
        Assert.False(run.CompactConsole);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OffNote));

        // Aynı değerle ikinci Save: değişmeyen ayar sessizdir.
        await run.ApplySettingsAsync([], @"D:\repo", [], compactConsole: false);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OffNote));

        await run.ApplySettingsAsync([], @"D:\repo", [], compactConsole: true);
        Assert.True(run.CompactConsole);
        Assert.Equal(1, Occurrences(run.GetRunDocumentText(), OnNote));
    }

    // ---------------------------------------------------------------- kabuk: seed + kalıcılık

    /// <summary>Açılışta kalıcı durumdan seed edilir; VM'de değişince kalıcı duruma yazılır.</summary>
    [StaFact]
    public void The_shell_seeds_the_setting_and_persists_its_changes()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState { CompactConsole = false });

        var (window, vm) = New(temp);

        Assert.False(vm.CompactConsole);
        vm.CompactConsole = true;
        Assert.True(UiStateStore(temp).Load().CompactConsole);
        GC.KeepAlive(window);
    }

    /// <summary>Anahtarı taşımayan eski bir ui-state dosyası onaylanmış varsayılanı (açık) alır.</summary>
    [StaFact]
    public void An_older_state_file_without_the_key_keeps_the_switch_on()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState());

        var (window, vm) = New(temp);

        Assert.True(vm.CompactConsole);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- realize

    private static CheckBox CompactSwitch(SettingsDialog dialog) =>
        Assert.Single(DsResources.RealizedObjects(dialog.Page(SettingsSection.General)).OfType<CheckBox>(),
            c => AutomationProperties.GetName(c) == Label);

    /// <summary>Realize: satır General sayfasının tek <c>ToggleRow</c> şablonuyla çizilir; diyalog canlı değerle açılır,
    /// switch taslağın bayrağıdır ve Save onu uygular.</summary>
    [StaFact]
    public void The_general_page_realizes_the_switch_and_save_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(r => r.CompactConsole = false);
        using var _scope = scope;

        var toggle = CompactSwitch(dialog);
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Assert.False(run.CompactConsole); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(store.State.CompactConsole);
        DispatcherPump.PumpUntil(() => run.CompactConsole, TimeSpan.FromSeconds(5));
        Assert.True(run.CompactConsole);
    }

    /// <summary>Kapatma yönü: varsayılanla (açık) açılan diyalogda switch kapatılıp Save edilince hem UiState hem VM kapalı
    /// olur. Önceki test varsayılan DEĞERE döner (Save'in bayrağı atlaması orada da yeşil kalırdı: <c>ApplySettingsAsync</c>'in
    /// varsayılan argümanı açıktır); bu test Save'in varsayılan DIŞI bir değeri uyguladığını sınar.</summary>
    [StaFact]
    public void Switching_the_setting_off_and_saving_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using var _scope = scope;
        Assert.True(run.CompactConsole);

        CompactSwitch(dialog).IsChecked = false;
        Assert.True(run.CompactConsole); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.False(store.State.CompactConsole);
        DispatcherPump.PumpUntil(() => !run.CompactConsole, TimeSpan.FromSeconds(5));
        Assert.False(run.CompactConsole);
    }
}
