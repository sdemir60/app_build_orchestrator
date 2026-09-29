using System.Windows;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] <b>Ayarlar kök OLMADAN kaydedilebilir.</b> Repository root artık Save'in koşulu
/// değildir: boş kök "workspace yok" demektir ve uygulama ilk açılış görünümüne döner — liste panelinde kurulum
/// daveti (design v1.8.0 §2.4). Save'in tek giriş noktası (<see cref="RunViewModel.ApplySettingsAsync"/>) iki
/// seviyede sınanır: VM (kök, faz, plan yüzeyi, git yüzeyi, konsol) ve kabuk — pencere üretim kablajıyla kurulur
/// (<see cref="MainWindowHost"/>) ve EKRAN okunur.
///
/// <para><b>Ölçülen kusur (kullanıcı testi):</b> Settings'te Clear'dan sonra Save kapalıydı (<c>Repository root is
/// required</c>) — ayarsız duruma Settings'ten dönülemiyordu, bu yüzden kurulum daveti de hiç görülemiyordu. Save
/// geçseydi bile boş kök "değişiklik yok" sayılır ve eski workspace yerinde kalırdı.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SettingsWithoutRootTests
{
    private const string RepositoryRoot = @"D:\repo";

    /// <summary>Sync'lenmiş ve üstünde bir koşunun izi duran workspace: iki satır, <c>main</c> branch'i, 3 commit
    /// geride; konsolda koşunun planlama satırı, akışta koşunun satırları.</summary>
    private static RunViewModel OpenWorkspace(EngineHost engine)
    {
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = RepositoryRoot };
        ProjectNode[] nodes = [MainWindowHost.Node("Alpha", 0), MainWindowHost.Node("Beta", 1)];
        vm.OnEvent(new SyncStartedEvent(RepositoryRoot, "main"));
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, nodes.Length, 0, Behind: 3, ActiveBranch: "main"));
        vm.OnEvent(new PlanProgressEvent("planning — 2 projects")); // konsolda önceki işlemin izi
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("Alpha"), 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));
        return vm;
    }

    // ---------------------------------------------------------------- workspace açıkken kök boş kaydedilir

    /// <summary>Açık bir workspace'te kökü boş kaydetmek workspace'i KAPATIR: kök boşalır, faz <c>Empty</c>'ye döner,
    /// plan yüzeyi (satırlar, topoloji → graf) boşalır ve motora hiçbir şey gitmez. Yalnız boşluktan oluşan kök de
    /// boştur.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Saving_an_empty_root_closes_the_workspace_and_sends_nothing(string? root)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = OpenWorkspace(engine);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        int topologyChanges = 0;
        vm.TopologyChanged += (_, _) => topologyChanges++;

        await vm.ApplySettingsAsync([], root, []);

        Assert.Equal("", vm.RootPath);
        Assert.False(vm.HasWorkspace);
        Assert.Equal(AppPhase.Empty, vm.Phase);
        Assert.Empty(vm.Projects);
        Assert.False(vm.HasTopology);
        Assert.Equal(1, topologyChanges);
        Assert.Empty(sent);
    }

    /// <summary>Kapanan workspace'in git yüzeyi, seçimi ve filtresi de gider: branch chip'i boşalır, <c>N behind</c>
    /// chip'i düşer, seçili satır ve filtre (chip'ler + arama) sıfırlanır — ilk açılışta da hiçbiri yoktur.</summary>
    [Fact]
    public async Task Closing_the_workspace_forgets_the_branch_the_behind_count_the_selection_and_the_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = OpenWorkspace(engine);
        vm.SelectProject(MainWindowHost.IdOf("Alpha"));
        vm.ToggleFilter(ProjectFilter.Failed);
        vm.ProjectQuery = "Al";
        Assert.Equal("main", vm.Branch);   // ön-koşul
        Assert.True(vm.CanShowBehind);     // ön-koşul: 3 behind

        await vm.ApplySettingsAsync([], null, []);

        Assert.Equal("", vm.Branch);
        Assert.Null(vm.Behind);
        Assert.False(vm.CanShowBehind);
        Assert.Null(vm.SelectedProjectId);
        Assert.Empty(vm.ActiveFilters);
        Assert.Equal("", vm.ProjectQuery);
    }

    /// <summary>Kapanış bir bölüm sonudur: eski workspace'in konsol ve akış satırları gider, ilk açılıştaki gibi boş
    /// sayfa kalır. Bu Save'in kendi notları (burada katman notu) o YENİ sayfaya yazılır — kaybolmaz.</summary>
    [Fact]
    public async Task Closing_the_workspace_starts_a_new_console_page_that_carries_only_this_saves_notes()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = OpenWorkspace(engine);
        Assert.NotEqual("", vm.GetRunDocumentText());                           // ön-koşul: koşunun izi var
        Assert.True(vm.StreamEventCount > 0, "ön-koşul: akışta koşunun izi yok — vakum");

        await vm.ApplySettingsAsync([], null, []);

        Assert.Equal(["Layers removed — single project list"],
            vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(0, vm.StreamEventCount);
        Assert.Empty(vm.StreamEvents);
    }

    /// <summary>Koşu sürerken kök boş kaydedilirse kapanış ERTELENİR — her kök değişimiyle aynı kural: kök ve satırlar
    /// yerinde kalır, konsola tek satır düşer.</summary>
    [Fact]
    public async Task Saving_an_empty_root_while_a_run_is_in_flight_is_deferred_like_any_root_change()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = OpenWorkspace(engine);
        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 2, 1, "Debug"));
        Assert.True(vm.IsMidRunLocked); // ön-koşul: koşu uçuşta

        await vm.ApplySettingsAsync([], null, []);

        Assert.Equal(RepositoryRoot, vm.RootPath);
        Assert.Equal(2, vm.Projects.Count);
        Assert.Contains(RunViewModel.RepositoryChangeDeferredLine(runInFlight: true), vm.GetRunDocumentText());
    }

    /// <summary>Kapandıktan sonra girilen kök bir İLK KURULUMDUR: workspace açılır, TEK Sync yeni kökte gider ve konsola
    /// "Repository root → …" notu düşmez (ilk kurulum sessizdir — orada zaten Sync başlar).</summary>
    [Fact]
    public async Task A_root_saved_after_closing_opens_the_workspace_like_a_first_setup()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = OpenWorkspace(engine);
        await vm.ApplySettingsAsync([], null, []);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.ApplySettingsAsync([], @"D:\other", []);

        Assert.True(vm.HasWorkspace);
        Assert.Equal(@"D:\other", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.DoesNotContain(RunViewModel.RepositoryRootChangedLine(@"D:\other"), vm.GetRunDocumentText());
    }

    /// <summary>Kabukta kapanış ilk açılış ekranını geri getirir: liste boşalır ve kurulum daveti görünür (kart: kök
    /// <c>Not set</c>, katman <c>Optional</c>), graf Sync-öncesi kutusunu gösterir, şerit <c>Not configured</c> der
    /// ve boş kök kalıcı duruma yazılır — bir sonraki açılış da davetle başlar.</summary>
    [StaFact]
    public async Task Saving_an_empty_root_brings_back_the_first_run_screen()
    {
        using var temp = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(temp, ("Alpha", null), ("Beta", null));
        Assert.Equal(Visibility.Collapsed, window.Shell.ListInviteOverlay.Visibility); // ön-koşul: liste dolu

        await vm.ApplySettingsAsync([], null, []);

        Assert.Equal(Visibility.Visible, window.Shell.ListInviteOverlay.Visibility);
        Assert.Empty(list.RowFlow.Items);
        Assert.Equal(("Not set", "Optional"), window.Shell.SetupChecklist);
        Assert.True(window.Shell.GraphHost.IsEmptyStateVisible);
        Assert.Equal("Not configured — repository root not set", window.Shell.PART_Ribbon.PhaseText.Text);
        Assert.Equal("", MainWindowHost.UiStateStore(temp).Load().RepositoryRoot);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.8.0 §3.1 · prototip panel başlıkları] Workspace yokken panel başlıkları sayaç taşımaz (graf
    /// <c>N projects · M dependencies</c>, konsol <c>N lines</c>, akış <c>N events</c>) ve PROJECTS başlığında liste
    /// araçları (<c>build-order</c> etiketi, filtre kutusu) yoktur — sayılacak ya da süzülecek bir şey yoktur. Açık
    /// workspace'te hepsi durur; kapanınca gider.</summary>
    [StaFact]
    public async Task Without_a_workspace_the_panel_headers_hide_their_counts_and_the_list_tools()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        AssertPanelHeaders(window.Shell, Visibility.Visible); // ön-koşul: workspace açık

        await vm.ApplySettingsAsync([], null, []);

        AssertPanelHeaders(window.Shell, Visibility.Collapsed);
        GC.KeepAlive(window);
    }

    /// <summary>Panel başlıklarının workspace'e bağlı beş öğesi — TEK yerde sayılır.</summary>
    private static void AssertPanelHeaders(BuildOrchestrator.App.ShellRoot shell, Visibility expected)
    {
        Assert.Equal(expected, shell.GraphHost.CountsText.Visibility);
        Assert.Equal(expected, shell.ConsoleHeaderControl.LinesText.Visibility);
        Assert.Equal(expected, shell.EventStreamControl.Counter.Visibility);
        Assert.Equal(expected, ((UIElement)shell.PART_ProjectsHeader.LeftContent!).Visibility); // build-order + filtre chip'i
        Assert.Equal(expected, shell.ProjectFilterBox.Visibility);
    }

    /// <summary>[design v1.8.0 §3.1] Kapanıştan sonra konsolun prompt satırı ilk açılıştaki gibi workspace bekler.</summary>
    [StaFact]
    public async Task Closing_the_workspace_puts_the_console_prompt_back_to_waiting_for_one()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        Assert.NotEqual("Waiting for a workspace", window.Shell.ConsoleViewControl.ActiveLineText.Text); // ön-koşul

        await vm.ApplySettingsAsync([], null, []);

        Assert.Equal("Waiting for a workspace", window.Shell.ConsoleViewControl.ActiveLineText.Text);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- first run

    /// <summary>[design v1.8.0 §3.1] İlk açılışta (kök yok) konsolun prompt satırı workspace bekler — graf ve liste
    /// panelinin bekleme metinleriyle aynı dili konuşur.</summary>
    [StaFact]
    public void The_first_run_console_prompt_waits_for_a_workspace()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        Assert.Equal("Waiting for a workspace", window.Shell.ConsoleViewControl.ActiveLineText.Text);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.8.0 §3.1] İlk açılışta panel başlıkları sayaç ve liste araçları taşımaz.</summary>
    [StaFact]
    public void The_first_run_panel_headers_hide_their_counts_and_the_list_tools()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        AssertPanelHeaders(window.Shell, Visibility.Collapsed);
        GC.KeepAlive(window);
    }

    /// <summary>İlk açılışta kök girmeden katman kaydetmek geçerli bir Save'dir: davet kalır ve kartın <c>Layers</c>
    /// satırı kaydedilen sayıyı gösterir (<c>N defined</c>) — kök hâlâ <c>Not set</c>.</summary>
    [StaFact]
    public async Task Saving_layers_without_a_root_on_first_run_counts_them_on_the_invitation()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        Assert.Equal(("Not set", "Optional"), window.Shell.SetupChecklist); // ön-koşul: katman yok

        await vm.ApplySettingsAsync(
            [new LayerPattern(0, "^A", "Core"), new LayerPattern(1, "^B", "Api"), new LayerPattern(2, "^C", "Client")],
            null, []);

        Assert.Equal(("Not set", "3 defined"), window.Shell.SetupChecklist);
        GC.KeepAlive(window);
    }
}
