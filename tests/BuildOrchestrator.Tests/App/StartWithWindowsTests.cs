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
/// </summary>
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
}
