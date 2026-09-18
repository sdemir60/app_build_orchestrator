using System.IO;
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
/// [spec 2026-09-18 §6.3] Settings → General → BRANCHES: <c>Stash and switch branches</c>. Branch chip'inden
/// checkout'ta ağaç kirliyse ne olacağını söyler — açık: değişiklikler (izlenmeyenler dahil) stash'lenir ve geçilir;
/// kapalı (varsayılan): checkout durur ve kullanıcıdan önce commit/stash ister.
///
/// <para>Uçtan uca desen <see cref="PullBeforeBuildTests"/>'in aynısıdır: taslak → ayar dosyası → Save (UiState +
/// <see cref="RunViewModel.StashOnBranchSwitch"/> aynı commit'te) → kabuğun açılış seed'i ve kalıcılığı. Değer
/// motora her checkout komutuyla gider (<see cref="BranchCheckoutTests"/>).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class StashOnBranchSwitchTests
{
    private const string Label = "Stash and switch branches";

    // ---------------------------------------------------------------- varsayılan

    /// <summary>Kapalı başlar: araç kullanıcının commit'lenmemiş işini kendiliğinden kenara koymaz.</summary>
    [Fact]
    public void It_is_off_by_default()
    {
        var row = Assert.Single(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows), r => r.Setting == GeneralSetting.StashOnBranchSwitch);
        Assert.False(row.Default);
        Assert.False(new SettingsDraftViewModel(null, @"D:\repo").StashOnBranchSwitch);
        Assert.False(new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1").StashOnBranchSwitch);
    }

    /// <summary>Satır ve taslak bayrağı TEK değerdir (pull satırının deseni).</summary>
    [Fact]
    public void The_row_and_the_draft_flag_are_one_value()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo");
        var notified = new List<string?>();
        draft.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.Single(draft.GeneralGroups.SelectMany(g => g.Rows), r => r.Definition.Setting == GeneralSetting.StashOnBranchSwitch).IsOn = true;

        Assert.True(draft.StashOnBranchSwitch);
        Assert.Contains(nameof(SettingsDraftViewModel.StashOnBranchSwitch), notified);
    }

    // ---------------------------------------------------------------- dosya biçimi

    [Fact]
    public void The_stash_setting_round_trips_through_the_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", stashOnBranchSwitch: true);
        string json = draft.ToFile().ToJson();
        Assert.Contains("\"stashOnBranchSwitch\": true", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.True(loaded.StashOnBranchSwitch);
    }

    /// <summary>Anahtarı taşımayan (eski) bir dosya formdaki değeri sıfırlamaz — pull bayrağının kuralı.</summary>
    [Fact]
    public void A_file_without_the_setting_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", stashOnBranchSwitch: true);

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.True(draft.StashOnBranchSwitch);
    }

    // ---------------------------------------------------------------- Save

    [Fact]
    public async Task Save_persists_the_setting_and_applies_it_in_the_same_commit()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var store = new SettingsDialogHost.FakeStore();
        var draft = new SettingsDraftViewModel(null, @"D:\repo") { StashOnBranchSwitch = true };

        await draft.CommitAsync(run, store);

        Assert.True(run.StashOnBranchSwitch);
        Assert.True(store.State.StashOnBranchSwitch);
    }

    /// <summary>Değişen değer konsola tek not düşer (pull bayrağının deseni); değişmeyen değer sessizdir.</summary>
    [Fact]
    public async Task A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], stashOnBranchSwitch: false);
        Assert.DoesNotContain("Stash and switch", run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], stashOnBranchSwitch: true);
        Assert.Contains("Stash and switch branches on — uncommitted changes are stashed before a branch switch",
            run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], stashOnBranchSwitch: false);
        Assert.Contains("Stash and switch branches off — a branch switch stops while there are uncommitted changes",
            run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kabuk: seed + kalıcılık

    /// <summary>Açılışta kalıcı durumdan seed edilir; VM'de değişince kalıcı duruma yazılır.</summary>
    [StaFact]
    public void The_shell_seeds_the_setting_and_persists_its_changes()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "ui-state.json");
        new JsonUiStateStore(path).Save(new UiState { StashOnBranchSwitch = true });

        var (window, vm) = New(temp);

        Assert.True(vm.StashOnBranchSwitch);
        vm.StashOnBranchSwitch = false;
        Assert.False(new JsonUiStateStore(path).Load().StashOnBranchSwitch);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- görünüm

    private static CheckBox StashSwitch(SettingsDialog dialog) =>
        Assert.Single(DsResources.RealizedObjects(dialog.Page(SettingsSection.General)).OfType<CheckBox>(),
            c => AutomationProperties.GetName(c) == Label);

    /// <summary>Diyalog canlı değerle açılır; switch taslağın bayrağıdır ve Save onu uygular.</summary>
    [StaFact]
    public void The_general_page_carries_the_switch_and_save_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(r => r.StashOnBranchSwitch = true);
        using var _scope = scope;

        var toggle = StashSwitch(dialog);
        Assert.True(toggle.IsChecked);

        toggle.IsChecked = false;
        Assert.False(dialog.Draft!.StashOnBranchSwitch);
        Assert.True(run.StashOnBranchSwitch); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.False(store.State.StashOnBranchSwitch);
        DispatcherPump.PumpUntil(() => !run.StashOnBranchSwitch, TimeSpan.FromSeconds(5));
        Assert.False(run.StashOnBranchSwitch);
    }
}
