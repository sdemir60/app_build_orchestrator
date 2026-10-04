using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [RESOLVE Faz 4 / karar 11 — kullanıcı onayı] Settings → General → BUILD: <c>Resolve cycles at full priority</c>.
/// Açıkken (varsayılan) Resolve cycles koşusu profilin işçi sayısıyla ama cap'siz ve Normal öncelikte koşar; kapalıyken
/// profili izler. Uçtan uca desen <see cref="StashOnBranchSwitchTests"/>'in aynısıdır: katalog → taslak → Save (UiState +
/// <see cref="RunViewModel.ResolveAtFullPriority"/>) → <see cref="StartRunCommand.ResolveAtFullPriority"/>; kabuğun
/// açılış seed'i ve kalıcılığı. Motorun bayrakla ne yaptığı <c>PerfProfileTests</c> ve <c>RunCoordinatorTests</c>'tedir.
/// </summary>
public class ResolveAtFullPrioritySettingsTests
{
    private const string Label = "Resolve cycles at full priority";

    private static GeneralSettingDefinition Row() =>
        Assert.Single(GeneralSettingsCatalog.Groups.SelectMany(g => g.Rows), r => r.Label == Label);

    [Fact]
    public async Task The_switch_is_on_by_default()
    {
        Assert.True(Row().Default);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        Assert.True(new RunViewModel(engine, NeverTickingBatcher(), () => "r1").ResolveAtFullPriority);
    }

    [Fact]
    public async Task The_run_command_carries_the_setting()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(run);
        var sent = new List<IpcCommand>();
        run.DebugOnCommandSent = sent.Add;

        run.ResolveAtFullPriority = false;
        await run.BuildCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(sent.OfType<StartRunCommand>()).ResolveAtFullPriority);
    }

    /// <summary>Değişen ayar konsola not düşer, değişmeyen sessiz kalır (<see cref="StashOnBranchSwitchTests"/> deseni).</summary>
    [Fact]
    public async Task A_changed_setting_notes_it_on_the_console_and_an_unchanged_one_stays_quiet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await run.ApplySettingsAsync([], @"D:\repo", [], resolveAtFullPriority: true);
        Assert.DoesNotContain(Label, run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], resolveAtFullPriority: false);
        Assert.False(run.ResolveAtFullPriority);
        Assert.Contains("Resolve cycles at full priority off — Resolve cycles follows the performance mode",
            run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], resolveAtFullPriority: true);
        Assert.Contains("Resolve cycles at full priority on — Resolve cycles runs at normal priority with no CPU cap",
            run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>Açılışta kalıcı durumdan seed edilir; VM'de değişince kalıcı duruma yazılır.</summary>
    [StaFact]
    public void The_shell_seeds_the_setting_and_persists_its_changes()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState { ResolveAtFullPriority = false });

        var (window, vm) = New(temp);

        Assert.False(vm.ResolveAtFullPriority);
        vm.ResolveAtFullPriority = true;
        Assert.True(UiStateStore(temp).Load().ResolveAtFullPriority);
        GC.KeepAlive(window);
    }

    /// <summary>Anahtarı taşımayan eski bir ui-state dosyası onaylanmış varsayılanı (açık) alır.</summary>
    [StaFact]
    public void An_older_state_file_without_the_key_keeps_the_switch_on()
    {
        using var temp = new TempDir();
        UiStateStore(temp).Save(new UiState());

        var (window, vm) = New(temp);

        Assert.True(vm.ResolveAtFullPriority);
        GC.KeepAlive(window);
    }

    private static CheckBox ResolveSwitch(SettingsDialog dialog) =>
        Assert.Single(DsResources.RealizedObjects(dialog.Page(SettingsSection.General)).OfType<CheckBox>(),
            c => AutomationProperties.GetName(c) == Label);

    /// <summary>Realize: satır General sayfasının tek <c>ToggleRow</c> şablonuyla çizilir; diyalog canlı değerle açılır,
    /// switch taslağın bayrağıdır ve Save onu uygular.</summary>
    [StaFact]
    public void The_general_page_realizes_the_switch_and_save_applies_it()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized(r => r.ResolveAtFullPriority = false);
        using var _scope = scope;

        var toggle = ResolveSwitch(dialog);
        Assert.False(toggle.IsChecked);

        toggle.IsChecked = true;
        Assert.False(run.ResolveAtFullPriority); // Save'e kadar uygulanmaz

        dialog.Save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(store.State.ResolveAtFullPriority);
        DispatcherPump.PumpUntil(() => run.ResolveAtFullPriority, TimeSpan.FromSeconds(5));
        Assert.True(run.ResolveAtFullPriority);
    }
}
