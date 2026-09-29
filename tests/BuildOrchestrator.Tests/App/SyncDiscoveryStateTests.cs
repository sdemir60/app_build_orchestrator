using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.24.0 · plan K1/K4/K5] Sync keşfi sürerken VM'in keşif durumu: ne zaman kurulur (hangi Sync
/// tetiklerinde), ne zaman düşer (topoloji ve Sync'in her bitiş yolu), sayaç nasıl okunur (kümülatif, yalnız keşif
/// açıkken) ve kırılım ne zaman gösterilir (istek anındaki harici tanım var mı).
/// <para>Harness <see cref="RunViewModelStateTests"/> ile aynıdır: başlatılmamış <see cref="EngineHost"/>, gönderim
/// <see cref="MainWindowHost.AcceptSends"/> ile "gider", motorun cevabını test <c>vm.OnEvent(...)</c> ile verir.
/// Her Sync kipi ÜRETİMDEKİ girişinden başlatılır (<see cref="MainWindowHost.StartSync"/>). D8: sleep/poll yok.</para>
/// </summary>
public class SyncDiscoveryStateTests
{
    private const string Root = @"D:\repo";

    private static RunViewModel NewVm()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot,
            RootPath = Root,
        };
        MainWindowHost.AcceptSends(vm);
        return vm;
    }

    /// <summary>Satırları ekranda olan, bir kez Sync'lenmiş workspace (faz Idle).</summary>
    private static RunViewModel NewVmWithRows()
    {
        var vm = NewVm();
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.Single(vm.Projects); // ön-koşul: liste dolu
        return vm;
    }

    /// <summary>VM'in yayınladığı özellik adları, sırasıyla.</summary>
    private static List<string> RecordNotifications(RunViewModel vm)
    {
        var names = new List<string>();
        vm.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? "");
        return names;
    }

    // ---------------------------------------------------------------- ne zaman görünür (K1)

    /// <summary>Açılış Sync'i (motor hazır, <see cref="SyncMode.Appended"/>): liste henüz hiç dolmadı — proje kümesi
    /// bilinmiyor, keşif bloğu görünür.</summary>
    [Fact]
    public async Task The_startup_sync_with_no_rows_shows_the_discovery_state()
    {
        var vm = NewVm();
        Assert.Empty(vm.Projects);

        await MainWindowHost.StartSync(vm, SyncMode.Appended);

        Assert.True(vm.IsDiscovering);
    }

    /// <summary>Ekranı baştan başlatan üç kip (Sync düğmesi, branch değişimi, Debug|Release): VM'in satırları durur
    /// ama ekran boşalır (<see cref="RunViewModel.PlanSurfaceRestarting"/>) — keşif bloğu görünür.</summary>
    [Theory]
    [InlineData(SyncMode.Manual)]
    [InlineData(SyncMode.BranchChange)]
    [InlineData(SyncMode.ConfigurationChange)]
    public async Task A_sync_that_restarts_the_plan_surface_shows_the_discovery_state(SyncMode mode)
    {
        var vm = NewVmWithRows();

        await MainWindowHost.StartSync(vm, mode);

        Assert.True(vm.PlanSurfaceRestarting); // ön-koşul: ekran gerçekten boşaldı
        Assert.True(vm.IsDiscovering);
    }

    /// <summary>Clean ve Optimize tıklamada listeyi boşaltır; bitişte devrettikleri Sync keşif bloğunu gösterir.
    /// <para>[plan K2] İşin KENDİ penceresi (tıklama → cleanStarted → iş sürerken) keşif DEĞİLDİR: orada Sync yok,
    /// "Discovering projects" doğru olmazdı — o pencere olduğu gibi kalır.</para></summary>
    [Theory]
    [InlineData("clean")]
    [InlineData("optimize")]
    public async Task The_sync_a_maintenance_task_hands_over_to_shows_the_discovery_state_but_the_task_itself_does_not(string task)
    {
        var vm = NewVmWithRows();

        if (task == "clean")
        {
            await vm.CleanCommand.ExecuteAsync(null);
            vm.OnEvent(new CleanStartedEvent(Root));
        }
        else
        {
            await vm.OptimizeCommand.ExecuteAsync(null);
            vm.OnEvent(new OptimizeStartedEvent(Root));
        }
        Assert.Empty(vm.Projects);       // ön-koşul: liste tıklamada boşaldı
        Assert.False(vm.IsDiscovering);  // K2: işin kendi penceresi keşif değildir

        vm.OnEvent(task == "clean" ? new CleanCompletedEvent(1, 2, 1_024, 0, 1) : new OptimizeCompletedEvent(1));

        Assert.True(vm.SyncRequested);   // ön-koşul: devir gerçekten bir Sync istedi
        Assert.True(vm.IsDiscovering);
    }

    /// <summary>Settings Save'de kök değişimi: plan yüzeyi boşalır, yeni kökün Sync'i keşif bloğunu gösterir.</summary>
    [Fact]
    public async Task A_root_change_shows_the_discovery_state()
    {
        var vm = NewVmWithRows();

        await vm.ApplySettingsAsync([], @"D:\other", []);

        Assert.Equal(@"D:\other", vm.RootPath); // ön-koşul: kök gerçekten değişti
        Assert.Empty(vm.Projects);
        Assert.True(vm.IsDiscovering);
    }

    /// <summary>[K1] Sessiz Sync ekranda iz bırakmaz — liste doluyken de, boşken de keşif bloğu çıkmaz.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_silent_sync_never_shows_the_discovery_state(bool withRows)
    {
        var vm = withRows ? NewVmWithRows() : NewVm();
        var names = RecordNotifications(vm);

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh)); // ön-koşul: Sync gerçekten gitti

        Assert.False(vm.IsDiscovering);
        Assert.DoesNotContain(nameof(RunViewModel.IsDiscovering), names);
    }

    /// <summary>[K1 · tasarımın kendi istisnası] Satırlar dururken gelen Appended Sync (başarılı pull, yalnız harici
    /// liste değişen Save) listeyi korur — keşif bloğu çıkmaz.</summary>
    [Theory]
    [InlineData("pull")]
    [InlineData("externalsOnlySave")]
    public async Task An_appended_sync_that_keeps_the_rows_does_not_show_the_discovery_state(string trigger)
    {
        var vm = NewVmWithRows();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        if (trigger == "pull") vm.OnEvent(new PullCompletedEvent(Succeeded: true));
        else await vm.ApplySettingsAsync([], Root, [new ExternalProject(@"D:\ext\Shared")]);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>()); // ön-koşul: Appended Sync gerçekten gitti
        Assert.Single(vm.Projects);
        Assert.False(vm.IsDiscovering);
    }

    // ---------------------------------------------------------------- ne zaman düşer

    /// <summary>Topoloji gelince keşif biter — ve reveal'den ÖNCE: <see cref="RunViewModel.TopologyChanged"/>
    /// (liste + graf yeniden kurulur, reveal oynar) ateşlendiğinde keşif çoktan kapanmış olmalıdır; aksi hâlde reveal
    /// gizli bir yüzeye oynardı.</summary>
    [Fact]
    public async Task The_topology_ends_the_discovery_before_the_surface_is_rebuilt()
    {
        var vm = NewVm();
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        Assert.True(vm.IsDiscovering); // ön-koşul
        bool? discoveringAtRebuild = null;
        vm.TopologyChanged += (_, _) => discoveringAtRebuild = vm.IsDiscovering;

        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        VmTopology.Seed(vm);

        Assert.False(vm.IsDiscovering);
        Assert.False(discoveringAtRebuild); // yüzey yeniden kurulurken keşif çoktan kapanmıştı (null = hiç kurulmadı)
    }

    /// <summary>Sync topoloji getirmeden biterse keşif de biter — hiçbir yolda asılı kalmaz: gönderim düştü,
    /// <c>planFailed</c>, motor kaybı, topolojisiz tamamlanma.</summary>
    [Theory]
    [InlineData("sendFails")]
    [InlineData("planFailed")]
    [InlineData("engineExited")]
    [InlineData("completedWithoutTopology")]
    public async Task A_sync_that_ends_without_a_topology_ends_the_discovery(string ending)
    {
        var vm = NewVm();
        if (ending == "sendFails") vm.DebugSendOverride = null; // başlatılmamış motor: gönderim senkron düşer
        var names = RecordNotifications(vm);

        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        switch (ending)
        {
            case "planFailed":
                vm.OnEvent(new SyncStartedEvent(Root, "main"));
                vm.OnEvent(new ErrorEvent("planFailed", "disk unreadable"));
                break;
            case "engineExited":
                vm.OnEngineExited(1);
                break;
            case "completedWithoutTopology":
                vm.OnEvent(new SyncStartedEvent(Root, "main"));
                vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0));
                break;
        }

        Assert.False(vm.SyncBusy); // ön-koşul: Sync gerçekten bitti
        Assert.False(vm.IsDiscovering);
        // Vakum değil: keşif gerçekten açıldı ve kapandı (iki bildirim).
        Assert.Equal(2, names.Count(n => n == nameof(RunViewModel.IsDiscovering)));
    }

    // ---------------------------------------------------------------- sayaç (K4)

    /// <summary>Sayaç KÜMÜLATİFTİR: her olay o ana kadarki toplamı taşır, App toplamaz — son değeri okur. Toplam
    /// iki alanın toplamıdır; keşif başında sıfırdır. Her olay sayacı yeniden duyurur.</summary>
    [Fact]
    public async Task The_counter_reads_the_latest_cumulative_report()
    {
        var vm = NewVm();
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        Assert.Equal((0, 0, 0), (vm.DiscoveredRepositoryProjects, vm.DiscoveredExternalProjects, vm.DiscoveredProjects));
        var names = RecordNotifications(vm);

        vm.OnEvent(new SyncDiscoveryEvent(29, 0));
        Assert.Equal((29, 0, 29), (vm.DiscoveredRepositoryProjects, vm.DiscoveredExternalProjects, vm.DiscoveredProjects));

        vm.OnEvent(new SyncDiscoveryEvent(29, 2));
        Assert.Equal((29, 2, 31), (vm.DiscoveredRepositoryProjects, vm.DiscoveredExternalProjects, vm.DiscoveredProjects));
        Assert.Equal(2, names.Count(n => n == nameof(RunViewModel.DiscoveredProjects)));
    }

    /// <summary>Sayaç yalnız keşif açıkken okunur: sessiz bir Sync'in ya da topolojiden sonra gelen bir rapor ekranda
    /// hiçbir şeyi değiştirmez.</summary>
    [Theory]
    [InlineData("silentSync")]
    [InlineData("afterTopology")]
    public async Task Reports_outside_the_discovery_are_ignored(string when)
    {
        var vm = NewVm();
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        vm.OnEvent(new SyncDiscoveryEvent(5, 0));
        VmTopology.Seed(vm);
        if (when == "silentSync")
        {
            vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
            Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh)); // ön-koşul
            vm.OnEvent(new SyncStartedEvent(Root, "main"));
        }
        Assert.Equal(5, vm.DiscoveredProjects); // ön-koşul: keşif içindeki rapor okundu
        var names = RecordNotifications(vm);

        vm.OnEvent(new SyncDiscoveryEvent(9, 1));

        Assert.False(vm.IsDiscovering);
        Assert.Equal((5, 0), (vm.DiscoveredRepositoryProjects, vm.DiscoveredExternalProjects));
        Assert.DoesNotContain(nameof(RunViewModel.DiscoveredProjects), names);
    }

    /// <summary>Her keşif sıfırdan sayar ("keşif başında 0 found") — önceki keşfin son sayısı taşınmaz.</summary>
    [Fact]
    public async Task Each_discovery_starts_counting_from_zero()
    {
        var vm = NewVm();
        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        vm.OnEvent(new SyncStartedEvent(Root, "main"));
        vm.OnEvent(new SyncDiscoveryEvent(3, 1));
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.Equal(4, vm.DiscoveredProjects); // ön-koşul

        await MainWindowHost.StartSync(vm, SyncMode.Manual);

        Assert.True(vm.IsDiscovering);
        Assert.Equal((0, 0), (vm.DiscoveredRepositoryProjects, vm.DiscoveredExternalProjects));
    }

    // ---------------------------------------------------------------- kırılım (K5)

    /// <summary>Kırılım (<c> · N repository · N external</c>) Sync isteği anında en az bir harici tanım varsa gösterilir
    /// — o an bulunmuş olmasa da. Değer istek anının görüntüsüdür: Sync sürerken liste değişse de bu keşifte aynı kalır.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_breakdown_follows_the_external_definitions_at_request_time(bool hasExternals)
    {
        var vm = NewVm();
        if (hasExternals) vm.ExternalProjects = [new ExternalProject(@"D:\ext\Shared")];

        await MainWindowHost.StartSync(vm, SyncMode.Appended);
        vm.ExternalProjects = hasExternals ? [] : [new ExternalProject(@"D:\ext\Shared")];

        Assert.True(vm.IsDiscovering); // ön-koşul
        Assert.Equal(hasExternals, vm.DiscoveryShowsBreakdown);
    }
}
