using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 1] Settings → General → <c>Close to tray</c> ve <c>Show notifications</c>'ın KALICILIĞI:
/// <see cref="ShellSwitches"/> tablosu üzerinden Save'de <c>ui-state.json</c>'a yazılır, diyalog her açılışta
/// kayıtlı değeri gösterir, Export/Import taşır (dosyada anahtar yoksa formdaki değer korunur) ve değer değişince
/// konsola tek satır not düşer (değişmezse sessiz). <b>Davranışları bu dosyada BAĞLANMAZ</b> — pencere kapanışını
/// <c>MainWindow.OnClosing</c> [Task 3], üç tray-balloon yolunu <c>FirstCloseBalloonGate</c>/
/// <c>TrayBuildIndicatorController</c>/<c>SecondInstanceGate</c> [Task 4] okur (<see cref="ShellSwitches.CloseToTray"/>/
/// <see cref="ShellSwitches.ShowNotifications"/> üzerinden).
///
/// <para>Uçtan uca desen <see cref="StashOnBranchSwitchTests"/>'in aynısıdır; farkı, tek bir bayrak yerine bir
/// TABLO (<see cref="ShellSwitches.All"/>) ile iki anahtarın AYNI ANDA kalıcı olmasıdır.</para>
/// </summary>
public class ShellSwitchesTests
{
    // ---------------------------------------------------------------- IsOn: varsayılan / kayıtlı

    /// <summary>Hiç kaydedilmemiş bir durumda ikisi de kataloğun varsayılanını (açık) okur.</summary>
    [Fact]
    public void An_unsaved_switch_reads_its_catalog_default()
    {
        var state = new UiState();

        Assert.True(ShellSwitches.IsOn(state, GeneralSetting.CloseToTray));
        Assert.True(ShellSwitches.IsOn(state, GeneralSetting.ShowNotifications));
    }

    /// <summary>Kayıtlı bir değer kataloğun varsayılanının ÖNÜNE geçer.</summary>
    [Fact]
    public void A_saved_switch_reads_its_saved_value()
    {
        var state = new UiState { CloseToTray = false, ShowNotifications = false };

        Assert.False(ShellSwitches.IsOn(state, GeneralSetting.CloseToTray));
        Assert.False(ShellSwitches.IsOn(state, GeneralSetting.ShowNotifications));
    }

    // ---------------------------------------------------------------- json store

    /// <summary>Diskteki AÇIK bir <c>null</c> token'ı (elle düzenleme) <see cref="UiState.UpdateExternals"/>'ın
    /// deseniyle AYNI şekilde tolere edilir: o anahtar kataloğun varsayılanına döner, dosyadaki DİĞER alanlar
    /// (ör. <see cref="UiState.LayoutMode"/>) ve diğer switch KORUNUR.</summary>
    [Fact]
    public void The_switches_survive_the_json_store_and_a_null_token_reads_as_the_default()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        new JsonUiStateStore(path).Save(new UiState { CloseToTray = false, ShowNotifications = false, LayoutMode = LayoutMode.List });

        Assert.False(new JsonUiStateStore(path).Load().CloseToTray);

        File.WriteAllText(path, File.ReadAllText(path).Replace("\"CloseToTray\": false", "\"CloseToTray\": null"));
        var reloaded = new JsonUiStateStore(path).Load();

        Assert.True(ShellSwitches.IsOn(reloaded, GeneralSetting.CloseToTray));        // null token → katalog varsayılanı
        Assert.False(ShellSwitches.IsOn(reloaded, GeneralSetting.ShowNotifications)); // diğer switch KORUNUR
        Assert.Equal(LayoutMode.List, reloaded.LayoutMode);                          // dosyadaki diğer alanlar KORUNUR
    }

    // ---------------------------------------------------------------- taslak

    /// <summary>Diyalog açılışının ctor'u: <c>saved</c> verildiyse iki satır da kayıtlı değerden tohumlanır.</summary>
    [Fact]
    public void The_draft_opens_on_the_saved_values()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", saved: new UiState { CloseToTray = false, ShowNotifications = false });

        Assert.False(draft.GeneralRow(GeneralSetting.CloseToTray).IsOn);
        Assert.False(draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn);
    }

    // ---------------------------------------------------------------- Save

    /// <summary>Save İKİ anahtarı da state'e yazar; konsola yalnız DEĞİŞENİN notu düşer, değişmeyen sessiz kalır
    /// (desen: <see cref="StashOnBranchSwitchTests.A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet"/>).</summary>
    [Fact]
    public async Task Save_writes_both_switches_and_notes_only_the_changed_ones()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn = false; // Close to tray taslak varsayılanında (açık) kalır

        await draft.CommitAsync(run, store);

        Assert.True(store.State.CloseToTray);
        Assert.False(store.State.ShowNotifications);
        Assert.Contains("Show notifications off — no tray notifications are shown", run.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Close to tray", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- dosya biçimi

    [Fact]
    public void The_switches_round_trip_through_the_settings_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.GeneralRow(GeneralSetting.CloseToTray).IsOn = false;
        draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn = false;

        string json = draft.ToFile().ToJson();
        Assert.Contains("\"closeToTray\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"showNotifications\": false", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.False(loaded.GeneralRow(GeneralSetting.CloseToTray).IsOn);
        Assert.False(loaded.GeneralRow(GeneralSetting.ShowNotifications).IsOn);
    }

    /// <summary>Anahtarları taşımayan (eski) bir dosya formdaki değerleri sıfırlamaz — pull/stash bayraklarının kuralı.</summary>
    [Fact]
    public void A_file_without_the_switches_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        draft.GeneralRow(GeneralSetting.CloseToTray).IsOn = false;
        draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn = false;

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.False(draft.GeneralRow(GeneralSetting.CloseToTray).IsOn);
        Assert.False(draft.GeneralRow(GeneralSetting.ShowNotifications).IsOn);
    }
}
