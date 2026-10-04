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
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
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

    // ---------------------------------------------------------------- dosya biçimi

    /// <summary>[fix 1B — I4] Export anahtarı yazar, Import geri okur (<see cref="StashOnBranchSwitchTests"/> deseni).
    /// Varsayılan DIŞI değer (kapalı) kullanılır: bağlantı koparsa form varsayılanda (açık) kalır ve test kırmızı verir.</summary>
    [Fact]
    public void The_setting_round_trips_through_the_file()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", resolveAtFullPriority: false);
        string json = draft.ToFile().ToJson();
        Assert.Contains("\"resolveAtFullPriority\": false", json, StringComparison.Ordinal);

        var loaded = new SettingsDraftViewModel(null, @"D:\repo");
        loaded.LoadFrom(SettingsFile.TryParse(json)!);

        Assert.False(loaded.ResolveAtFullPriority);
    }

    /// <summary>[fix 1B — I4] Anahtarı taşımayan (eski) bir dosya formdaki değeri varsayılana döndürmez — pull/stash
    /// bayraklarının kuralı.</summary>
    [Fact]
    public void A_file_without_the_setting_leaves_the_form_untouched()
    {
        var draft = new SettingsDraftViewModel(null, @"D:\repo", resolveAtFullPriority: false);

        draft.LoadFrom(SettingsFile.From(@"D:\repo", []));

        Assert.False(draft.ResolveAtFullPriority);
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
        // [DEĞİŞEN KURAL — fix 1A · M5] Sonek eskiden "Resolve cycles follows…" / "Resolve cycles runs…" idi. Save koşu
        // sürerken de yazılır ve not uçuştaki koşuyu değiştiriyormuş gibi okunuyordu; motor koşu başındaki anahtarı korur.
        // Sonek artık bir SONRAKİ Resolve koşusunu söyler. Etiket önekini kod katalogdan türetir; test literal metni pinler.
        Assert.Contains("Resolve cycles at full priority off — the next Resolve cycles run follows the performance mode",
            run.GetRunDocumentText(), StringComparison.Ordinal);

        await run.ApplySettingsAsync([], @"D:\repo", [], resolveAtFullPriority: true);
        Assert.Contains("Resolve cycles at full priority on — the next Resolve cycles run runs at normal priority with no CPU cap",
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
