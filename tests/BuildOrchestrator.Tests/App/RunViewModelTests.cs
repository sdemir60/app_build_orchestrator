using System.Collections.Specialized;
using System.IO;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 12] RunViewModel: event → satır/proje durumu, elapsed, log yükleme dikişi, Stop/Continue komut
/// gönderimi. <see cref="RunViewModel.OnEvent"/> HERHANGİ bir thread'den (test thread'i dahil) doğrudan
/// çağrılabilir — VM'in kendisi Dispatcher/AvalonEdit türü TAŞIMAZ (UI-thread-agnostic çekirdek).
/// Determinizm [D8]: sleep/poll yok — Stop/Continue testleri gerçek Supervisor process'i üzerinden
/// TaskCompletionSource ile event bekler (EngineHostTests/RunCoordinatorTests ile aynı desen).
/// </summary>
public class RunViewModelTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // [B1/F1 · fix-1] Bu dosyada GERÇEK Supervisor process'i BAŞLATAN (StartAsync çağıran) sekiz test var ve
    // hepsi aynı köke bağlı: üretim varsayılanı (5s, EngineHost.StartupTimeout) yük altında yetmiyor. fix-1
    // öncesi bunlardan yalnız ÜÇÜ (ilk ölçülenler) yamalıydı; yük altındaki koşumda yamasız kalanlardan ikisi
    // (Continue_sends_StartRunCommand_with_ContinueMode + OnEngineExited_while_IsStarting_...) yine
    // EngineHost.StartAsync'te TimeoutException ile düştü — bkz. task-B1-report.md İŞ 4. Artık SEKİZİ DE
    // enjekte ediyor; StartAsync ÇAĞIRMAYAN diğer engine'ler (çoğunluk) bu süreyi hiç kullanmadığı için
    // dokunulmadı. Sabitin tek sahibi TestPaths.WideStartupTimeout; üretim varsayılanı DEĞİŞMEDİ.
    private static readonly TimeSpan WideStartupTimeout = TestPaths.WideStartupTimeout;

    // ---------------------------------------------------------------- 1) satır ekleme/güncelleme (saf OnEvent)

    [Fact]
    public async Task ProjectStarted_adds_a_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(@"C:\p\a.csproj", row.Id);
        Assert.Equal("A", row.Name);
        Assert.Equal(ProjectRowState.Started, row.State);
    }

    // ---------------------------------------------------------------- satır araması: proje Id'leri DOSYA YOLUDUR
    //
    // Windows'ta yol karşılaştırması harf-duyarsızdır ve bu dosyadaki her arama/sözlük zaten
    // OrdinalIgnoreCase'dir — ama iki yer (OnProjectDone, EnsureRow) düz `==` ile kalmıştı. Bugün üretimde
    // ayrışma gözlenmedi (worktree rebase'i kök önekini cmd.RootPath'ten, kuyruğu diskten aldığı için Sync
    // ile Build aynı yazımı üretir), fakat ayrışırsa bedeli SESSİZDİR: tamamlanma satırı bulunamaz (savunmacı
    // return) → satır sonsuza dek "building" kalır; EnsureRow ise aynı projeye ikinci bir satır açar. Kural
    // artık tek helper'dadır (FindRow) — bu iki test onu iki uçtan pinler.

    [Fact]
    public async Task Project_completion_matches_the_row_case_insensitively()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\P\A.CSPROJ", 1200));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Succeeded, row.State); // "building"de asılı KALMAZ
        Assert.Equal(1200, row.DurationMs);
    }

    [Fact]
    public async Task A_differently_cased_id_does_not_create_a_duplicate_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\P\A.CSPROJ", SkipReasons.UpToDate));

        var row = Assert.Single(vm.Projects); // kopya satır YOK
        Assert.Equal(ProjectRowState.Skipped, row.State);
    }

    [Fact]
    public async Task Selecting_a_project_flows_IsSelected_to_the_matching_row_and_toggles_off_on_repeat()
    {
        // [D1 · C1 debt] Seçim RunViewModel.SelectedProjectId'de yaşar; kart görsel seçili durumunu satır
        // VM'inin IsSelected'ından okur (kanonik same-click deselect korunur).
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        var a = vm.Projects.Single(p => p.Id == @"C:\p\a.csproj");
        var b = vm.Projects.Single(p => p.Id == @"C:\p\b.csproj");

        vm.SelectProject(a.Id);
        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);

        vm.SelectProject(b.Id); // seçim taşınır — eski satır bırakılır
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);

        vm.SelectProject(b.Id); // aynı satıra tekrar → deselect
        Assert.False(b.IsSelected);
        Assert.Null(vm.SelectedProjectId);
    }

    [Fact]
    public async Task Topology_carries_the_solution_name_onto_each_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys.sln"], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));

        Assert.Equal("Osys.sln", Assert.Single(vm.Projects).SolutionName);
    }

    /// <summary>[Fix wave 1, Finding 1] cycle verisi IPC'de VAR: <c>ProjectNode.InCycle</c> topolojiden satıra
    /// taşınır ve <c>Status</c> onu — <b>satır hakkında bu koşuda henüz bir şey söylenmemişken</b> — cycle
    /// görsel statüsüne çevirir.
    /// <para><b>[DEĞİŞEN KURAL]</b> İkinci iddia tersine döndü. Eskiden pre-skip edilen bir üyede görsel
    /// "cycle KALIR (skipped değil)" diye pinliydi; şimdi motor konuştuğunda glyph MOTORUN cevabını gösterir
    /// (<c>Skipped</c>) ve döngü üyeliği dep-slotundaki rozete taşınır. Gerekçe ölçüldü: eski kuralla bir
    /// Build'den sonra döngüdeki her satır Sync'ten hemen sonraki hâliyle BİREBİR aynı görünüyordu — "bu koşu
    /// onları atladı" ile "bunlar bir döngüde" ayırt edilemiyordu; ve döngüleri gerçekten derleyen koşu
    /// (<c>RunMode.Cycles</c>) geldiğinde aynı kural sonucu da gizlerdi. Rozetin kendisi
    /// <c>ProjectRowTests</c>'te pinlidir.</para></summary>
    [Fact]
    public async Task A_cycle_member_row_keeps_its_real_status_and_carries_membership_separately()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys.sln"], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: true, WillBuild: null);
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.InCycle);
        // [DEĞİŞEN KURAL — design v1.7.0 §5] Üyelik STATÜ DEĞİLDİR: Sync sonrası satır hâlâ Discovered'dır,
        // üyelik kendi kanalında (nokta + uyarı üçgeni + graf çekirdeği) yaşar ve statüyü asla gizlemez.
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status);

        // Motor konuştu: glyph ONUN cevabıdır; üyelik satırda (InCycle) DURUR.
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.InDependencyCycle));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Skipped, row.Status);
        Assert.True(row.InCycle);
    }

    // [Fix wave 1, Finding 1] queued verisi TÜRETİLİR: willBuild=true + Pending + run uçuşta → Queued;
    // run bitince (IsRunning düşer) yine Discovered.
    [Fact]
    public async Task A_planned_pending_row_is_queued_during_a_run_and_discovered_once_it_ends()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // IsRunning=true
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Pending, row.State);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, row.Status); // planlanmış, henüz başlamadı

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // IsRunning=false
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status); // run bitti → dinlenme
    }

    // [Task 1 — kök neden A] Kuyruk artık BU koşunun kendi buildPreview'inden türer, genel WillBuild
    // bayrağından DEĞİL. Tek proje koşusunda motorun planı tek düğüme kesilir (ProjectRunScope) — önizleme
    // yalnız hedefi taşır. Sync'ten kalan bayat WillBuild=true'yu taşıyan diğer bir satır bu yüzden koşu
    // boyunca Discovered kalmalı, runStarted ile buildPreview arasında da (bir an) amber'a düşmemeli.
    [Fact]
    public async Task A_single_project_run_leaves_a_stale_sibling_row_discovered_never_queued()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string targetId = @"C:\p\a.csproj";
        const string staleId = @"C:\p\b.csproj";

        // Sync'in tam önizlemesi: ikisi de dirty (WillBuild=true).
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(targetId, "A", true),
            new BuildPreviewItem(staleId, "B", true),
        ]));
        var target = vm.Projects.Single(p => p.Id == targetId);
        var stale = vm.Projects.Single(p => p.Id == staleId);
        Assert.True(target.WillBuild);
        Assert.True(stale.WillBuild); // ön-koşul: B hâlâ "dirty" — bayat bilgi koşu boyunca KORUNUR

        // Satırdan Build: yalnız A hedef. runStarted, kendi önizlemesinden ÖNCE gelir.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, target.Status); // önizleme YOK → kuyruk yok
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, stale.Status);

        // Motorun planı tek düğüme kesilir: önizleme YALNIZ hedefi taşır.
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(targetId, "A", true)]));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, target.Status);     // planlandı
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, stale.Status);   // bayat B kuyrukta DEĞİL
        Assert.True(stale.WillBuild); // [ayrışma yok] WillBuild kapsam/karar için hâlâ true — yalnız kuyruk rengi ayrıştı

        vm.OnEvent(new ProjectStartedEvent("r1", targetId, "A"));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Building, target.Status);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, stale.Status); // koşu boyunca değişmez

        vm.OnEvent(new ProjectSucceededEvent("r1", targetId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, stale.Status); // koşu sonunda da Discovered

        // Tek eşleme yeri: graf de AYNI statüyü okur.
        Assert.Equal(target.Status, GraphBinder.StatusOf(target, synced: true));
        Assert.Equal(stale.Status, GraphBinder.StatusOf(stale, synced: true));
    }

    // [koşullu yeniden derleme] Motor koşullu projeyi önizlemede Conditional=true ile işaretler: WillBuild=true
    // kalır (pre-skip edilmedi) ama kesin derlenecek DEĞİLDİR — kökü hâlâ hatalıysa "dependency still failing"
    // ile atlanır. Kuyruk (amber) yalnız kesin derleneceklerdir; koşullu satır gri bekler. Kök düzeldiyse
    // projectStarted gelir ve normal yoldan Building'e geçer.
    [Fact]
    public async Task A_conditional_project_in_a_build_run_is_not_queued()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string rootId = @"C:\p\up.csproj";
        const string waitingId = @"C:\p\down.csproj";

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(rootId, "Up", true, null, WillBuildReason.LastFailed),
            new BuildPreviewItem(waitingId, "Down", true, null, WillBuildReason.WaitingForDependency,
                Conditional: true, DependencyRoots: ["Up"]),
        ]));

        var root = vm.Projects.Single(p => p.Id == rootId);
        var waiting = vm.Projects.Single(p => p.Id == waitingId);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, root.Status);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, waiting.Status);
        Assert.Equal(waiting.Status, GraphBinder.StatusOf(waiting, synced: true));

        vm.OnEvent(new ProjectStartedEvent("r1", waitingId, "Down")); // kök düzeldi, motor derliyor
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Building, waiting.Status);
    }

    // [Task 2/cycles — kök neden B] Resolve cycles'ta kuyruk YALNIZ döngü üyelerine yazılır (InRunQueueFor artık
    // modu da okur): kapsam İÇİNDEKİ bayat bir upstream bağımlılık WillBuild=true olsa da gri bekler,
    // projectStarted'la normal yoldan Building'e geçer. Kapsam DIŞI bir proje motorun kendi pre-skip'ini
    // (SkipReasons.OutOfCycleScope) State'e hiç TAŞIMAZ: state boyunca ve run bitince de Pending/Discovered
    // kalır, atlandı sayacı onu SAYMAZ — [review fix I-1] SkipReason'ı YİNE DE
    // taşır (ConsoleEmptyStateTests bunun neden gerekli olduğunu ayrıca pinler). Kapsam içi GERÇEK bir "up to
    // date" skip (SkipReasons.UpToDate) ise normal yoldan Skipped'a geçmeye ve sayılmaya devam eder.
    // [DEĞİŞEN KURAL — design v1.20.0 §2.7] Burada ayrıca "atlandı filtresi kapsam dışını listelemez, kapsam içi
    // gerçek skip'i listeler" pinleniyordu; atlandı filtresi (chip'iyle birlikte) kalktı — chip'ler artık durum
    // filtreleridir ve satırı koşu statüsüyle değil gösterdiği durumla listeler (ProjectFilterTests). Koşu
    // tablosunun "N skipped"i (şerit) aşağıda pinlenmeye devam eder.
    [Fact]
    public async Task A_cycles_run_queues_only_members_and_leaves_out_of_scope_rows_untouched()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        const string memberId = @"C:\p\member.csproj";
        const string staleDepId = @"C:\p\staledep.csproj";
        const string upToDateDepId = @"C:\p\uptodate.csproj";
        const string outOfScopeId = @"C:\p\outofscope.csproj";

        static ProjectNode Node(string id, string name, int order, bool inCycle) => new(
            id, name, id, SolutionNames: [], Dependencies: [], BuildOrder: order,
            LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

        vm.OnEvent(new WorkspaceTopologyEvent(
        [
            Node(memberId, "Member", 0, inCycle: true),
            Node(staleDepId, "StaleDep", 1, inCycle: false),
            Node(upToDateDepId, "UpToDateDep", 2, inCycle: false),
            Node(outOfScopeId, "OutOfScope", 3, inCycle: false),
        ], [], [], []));

        var member = vm.Projects.Single(p => p.Id == memberId);
        var staleDep = vm.Projects.Single(p => p.Id == staleDepId);
        var upToDateDep = vm.Projects.Single(p => p.Id == upToDateDepId);
        var outOfScope = vm.Projects.Single(p => p.Id == outOfScopeId);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 4, 1, "Debug", 0));
        // Cycles'ta motorun plan'ı TÜM workspace'i kapsar (tek-proje Build'in aksine) — kapsam dışı da
        // WillBuild=false ile önizlemede GÖRÜNÜR (RunCoordinator.cs'in seed mekanizması); yalnız InRunQueue
        // kararı üyelikle daralır.
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(memberId, "Member", true),
            new BuildPreviewItem(staleDepId, "StaleDep", true),
            new BuildPreviewItem(upToDateDepId, "UpToDateDep", false),
            new BuildPreviewItem(outOfScopeId, "OutOfScope", false),
        ]));

        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, member.Status);      // üye: kuyrukta (amber)
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, staleDep.Status); // kapsam içi bayat bağımlılık: gri bekler
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, upToDateDep.Status);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, outOfScope.Status);

        // Kapsam içi bayat bağımlılık normal yoldan derlenir.
        vm.OnEvent(new ProjectStartedEvent("r1", staleDepId, "StaleDep"));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Building, staleDep.Status);
        vm.OnEvent(new ProjectSucceededEvent("r1", staleDepId, 100));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Succeeded, staleDep.Status);

        // Kapsam içi gerçek "up to date" skip normal Skipped'a geçer ve SAYILIR.
        vm.OnEvent(new ProjectSkippedEvent("r1", upToDateDepId, SkipReasons.UpToDate));
        Assert.Equal(ProjectRowState.Skipped, upToDateDep.State);
        Assert.Equal(SkipReasons.UpToDate, upToDateDep.SkipReason);

        // Kapsam dışı pre-skip motorun kendi gerekçesiyle gelir; State'i HİÇ etkilemez (Pending kalır) ama
        // [review fix I-1] SkipReason'ı YİNE DE taşır — satırın TEK kanıtı budur (WillBuild motor tarafından
        // false ZORLANMIŞ, bkz. ConsoleEmptyState.Pending'in yorumu), ConsoleEmptyStateTests bunu ayrıca pinler.
        vm.OnEvent(new ProjectSkippedEvent("r1", outOfScopeId, SkipReasons.OutOfCycleScope));
        Assert.Equal(ProjectRowState.Pending, outOfScope.State);
        Assert.Equal(SkipReasons.OutOfCycleScope, outOfScope.SkipReason);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, outOfScope.Status);

        // Üye derlenir.
        vm.OnEvent(new ProjectStartedEvent("r1", memberId, "Member"));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Building, member.Status);
        vm.OnEvent(new ProjectSucceededEvent("r1", memberId, 50));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Succeeded, member.Status);

        // Atlandı sayacı yalnız kapsam içi skip'i sayar (kapsam dışı hiç sayılmaz).
        Assert.Equal(1, vm.Counters.Skipped);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 0, 1, 0, 200));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, outOfScope.Status); // run sonunda da Discovered
        Assert.Equal(1, vm.Counters.Skipped); // run sonunda da yalnız kapsam içi skip
    }

    // [Task 2 review fix I-1] Kapsam dışı bir satırın SkipReason'ı State'ten BAĞIMSIZ taşınır — konsol sayfası
    // motorun GERÇEKTEN söylediği gerekçeyi gösterir, ConsoleEmptyState'in WillBuild=false'tan (motor bunu
    // TÜM pre-skip'ler için zorlar, kapsam dışı da güncel de) "Up to date" TÜRETMESİNİ engeller. Bu, satır
    // seviyesinde ConsoleModesTests'in ayrı bir testinde de pinlenir (ConsoleEmptyState.Pending).
    [Fact]
    public async Task Out_of_cycle_scope_row_keeps_its_SkipReason_so_its_project_page_states_the_real_cause()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string outOfScopeId = @"C:\p\outofscope.csproj";

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(outOfScopeId, "OutOfScope", false)]));
        vm.OnEvent(new ProjectSkippedEvent("r1", outOfScopeId, SkipReasons.OutOfCycleScope));

        var row = vm.Projects.Single(p => p.Id == outOfScopeId);
        Assert.Equal(ProjectRowState.Pending, row.State); // görsel/sayaç yüzeyi ETKİLENMEZ
        Assert.Equal(SkipReasons.OutOfCycleScope, row.SkipReason); // ama kanıt taşınır
    }

    // [Task 2 review fix I-2] Motorun kendi RunCompletedEvent.Skipped'i kapsam dışı pre-skip'leri de sayar
    // (RunCoordinator.cs'in seed'i) — App'in RunCounters.Skipped'i (satır State'inden türer) artık bunları hiç
    // saymadığı için (bkz. OnProjectSkipped) aynı run için stream'in kapanış satırı ile ribbon/sayaç FARKLI
    // sayı gösterirdi ("6 skipped" vs "1 skipped"). Kapanış satırı motorun sayısından
    // _outOfScopeSkipCount'u düşerek ikisini hizalar.
    [Fact]
    public async Task Completion_stream_line_and_the_apps_own_skipped_counter_agree_when_a_cycles_run_has_out_of_scope_pre_skips()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string memberId = @"C:\p\member.csproj";
        var outOfScopeIds = new[] { @"C:\p\out0.csproj", @"C:\p\out1.csproj", @"C:\p\out2.csproj" };

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 1 + outOfScopeIds.Length, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(memberId, "Member", true),
            .. outOfScopeIds.Select(id => new BuildPreviewItem(id, id, false)),
        ]));
        foreach (string id in outOfScopeIds) vm.OnEvent(new ProjectSkippedEvent("r1", id, SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectStartedEvent("r1", memberId, "Member"));
        vm.OnEvent(new ProjectSucceededEvent("r1", memberId, 50));

        // Motorun kendi toplamı: 3 kapsam dışı + 0 kapsam içi skip = 3. App'in kendi sayacı 0 kapsam içi
        // skip gördü (üye derlendi, kapsam dışı hiç sayılmaz) — ikisi FARKLI sayılardır, kapanış satırı bunu
        // motorun sayısından düzeltmelidir.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 1, Failed: 0, Skipped: 3, Queued: 0, DurationMs: 200));

        Assert.Equal(0, vm.Counters.Skipped); // App'in kendi kapsam-farkında sayacı
        var doneLine = Assert.Single(vm.StreamEvents, e => e.Kind == StreamKind.Done);
        Assert.Equal(StreamText.Completed(failed: 0, succeeded: 1, skipped: 0, depAffected: 0, durationMs: 200), doneLine.Text);
    }

    // [Task 2 review fix M-1] Cycles'ta kapsam dışı satırlar artık hiçbir zaman terminal olmuyor (bkz.
    // OnProjectSkipped) — UpdateEta'nın "completed" sayısı bunları saymazsa SONSUZA DEK eksik kalırdı (o
    // satırlar hiç "bitmeyecek"), X/N fallback'i (ve smoothing sonrası ETA) kalıcı olarak abartırdı.
    // _outOfScopeSkipCount bu boşluğu kapatır — motor bu projeleri zaten "bitirmiştir" (pre-skip), App'in kendi
    // "completed" sayacı da bunu yansıtmalı.
    [Fact]
    public async Task Eta_text_counts_out_of_scope_pre_skips_as_completed_in_a_cycles_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string memberId = @"C:\p\member.csproj";
        const string out1 = @"C:\p\out1.csproj";
        const string out2 = @"C:\p\out2.csproj";

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 3, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(memberId, "Member", true),
            new BuildPreviewItem(out1, "Out1", false),
            new BuildPreviewItem(out2, "Out2", false),
        ]));

        vm.OnEvent(new ProjectSkippedEvent("r1", out1, SkipReasons.OutOfCycleScope));
        vm.OnEvent(new ProjectSkippedEvent("r1", out2, SkipReasons.OutOfCycleScope));

        // 3 toplam; kapsam dışı 2'si App'in satır State'inde ASLA terminal olmayacak (bkz. OnProjectSkipped)
        // ama motor onları zaten bitirdi — completed 2 olmalı (yalnız üye M hâlâ Pending), 0 DEĞİL.
        Assert.Equal("2/3 · 0s", vm.EtaText);
    }

    // [Task 2 review fix M-4] Task 2'nin RibbonTextTests'teki birim testleri RibbonText.Compose'u izole
    // çağırıyordu; bu test AYNI iddiayı uçtan uca (gerçek VM event sırasıyla) pinler — tek-proje bir Build
    // durdurulduğunda "not built" sayısı workspace'teki HER Pending satırı değil, bu run'ın KENDİ (tek elemanlı)
    // kapsamını sayar.
    [Fact]
    public async Task Stopping_a_single_project_run_reports_not_built_scoped_to_the_runs_own_set_not_the_whole_workspace()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\repo" }; // RibbonLine HasWorkspace ister

        // Sync'in tam önizlemesi: workspace'te 3 proje, ikisi dirty.
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", true),  // kapsam dışı kalacak bayat kardeş
            new BuildPreviewItem(@"C:\p\c.csproj", "C", false), // zaten güncel
        ]));

        // Satırdan Build: yalnız A hedef — motorun önizlemesi (Task 1) yalnız hedefi taşır.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));

        // Eski (c.Queued tabanlı) formül B'yi ve C'yi de sayardı (ikisi de hâlâ Pending) → "3 not built".
        // Doğrusu yalnız A'dır: bu run'ın kendi kuyruğu (WillBuildCount=1, FinishedOfWillBuild=0).
        Assert.Equal("▸ Stopped — 0/1 · 1 not built", vm.RibbonLine.Text);
    }

    // [Task 1 review fix — M-4] InRunQueue'nun BİTİŞ noktası PropagateRunActive'dir (IsRunActive düşerken) —
    // Stop de, motor ölümü de IsRunning'i (dolayısıyla IsRunActive'i) false yapar, ikisi de kuyruğu düşürmeli.
    // Aksi halde durdurulan/motoru ölen bir run'ın kuyruğa aldığı satır ekranda KALICI amber asılı kalırdı.
    [Fact]
    public async Task Stopping_a_run_drops_InRunQueue_so_the_queued_row_returns_to_discovered()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        var row = Assert.Single(vm.Projects);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, row.Status); // ön-koşul: kuyrukta

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));

        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status); // kuyruk da düştü
    }

    [Fact]
    public async Task Engine_death_drops_InRunQueue_so_the_queued_row_returns_to_discovered()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        var row = Assert.Single(vm.Projects);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, row.Status); // ön-koşul: kuyrukta

        vm.OnEngineExited(139);

        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status); // kuyruk da düştü
    }

    // [Fix wave 1, Minor 6] TickElapsed building satırların CANLI süresini ilerletir; building OLMAYAN satırlara
    // dokunmaz. Deterministik saat enjekte edilir (D8: sleep/poll yok).
    [Fact]
    public async Task TickElapsed_advances_only_the_building_rows_live_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long clock = 0;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => clock);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 2, 2, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A")); // building (started at 0)
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 1000)); // terminal, DurationMs=1000

        clock = 5000;
        vm.TickElapsed();

        var a = vm.Projects.Single(p => p.Id == @"C:\p\a.csproj");
        var b = vm.Projects.Single(p => p.Id == @"C:\p\b.csproj");
        Assert.Equal(5000, a.DurationMs); // building → canlı ilerledi
        Assert.Equal(1000, b.DurationMs); // succeeded → dokunulmadı
    }

    [Fact]
    public async Task ProjectSucceeded_updates_state_and_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Succeeded, row.State);
        Assert.Equal(2400, row.DurationMs);
    }

    [Fact]
    public async Task ProjectFailed_updates_state_and_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));

        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\b.csproj", 900, "exit 1"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Failed, row.State);
        Assert.Equal(900, row.DurationMs);
    }

    [Fact] // cycle üyeleri Started OLMADAN doğrudan Skipped gelir (RunCoordinator PreSkipped) — satır yine de eklenmeli
    public async Task ProjectSkipped_without_a_prior_started_still_adds_a_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\x.csproj", "bağımlılık döngüsünde"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Skipped, row.State);
    }

    // ---------------------------------------------------------------- 2) elapsed

    [Fact]
    public async Task RunStarted_sets_ElapsedMs_from_ElapsedMsAtStart()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 177, 6, "Debug", ElapsedMsAtStart: 4200));

        Assert.Equal(4200, vm.ElapsedMs);
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public async Task RunCompleted_stops_the_clock_at_the_engine_reported_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, DurationMs: 9999));

        Assert.Equal(9999, vm.ElapsedMs);
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- 3) log yükleme dikişi

    [Fact]
    public async Task LoadProjectLogAsync_stitches_only_buffered_lines_after_ThroughLineNumber_no_duplicates()
    {
        // Engine hiç başlatılmadı: LoadProjectLogAsync'in SendAsync'i senkron fırlar ve VM içinde yutulur —
        // dikiş TAMAMEN yerel state'ten (buffered projectLog + chunk) üretilir, gerçek IPC gerekmez.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 4; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"line{i}"));

        // [D4 review §2] Üretimde kart seçimi (SelectedProjectId) LoadProjectLogAsync'ten ÖNCE kurulur
        // (OnSelectedProjectChangedAsync onu SelectedProjectId değişimiyle tetikler); proje modu ancak seçim hâlâ
        // o projedeyse kurulur. Testler bu koşulu birebir modellemeli.
        vm.SelectProject(projectId);
        var load = vm.LoadProjectLogAsync(projectId); // pending state SENKRON kurulur (ilk await'e kadar)
        vm.OnEvent(new ProjectLogChunkEvent(projectId, Sequence: 0, "line1\nline2\n", IsLast: true, ThroughLineNumber: 2));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\nline3\nline4\n", vm.GetProjectDocumentText(projectId));
        Assert.Equal(projectId, vm.ActiveProjectId);
    }

    [Fact] // [Fix wave 1, Finding 2] dikiş kilidi kapanması ile ActiveProjectId ataması AYNI kilit altında olmalı
    public async Task LoadProjectLogAsync_does_not_drop_a_live_line_racing_the_stitch_finalize()
    {
        // ActiveProjectId ataması eskiden kilit DIŞINDAYDI: kilit kapanıp _projectText yazıldıktan SONRA,
        // ActiveProjectId GÜNCELLENMEDEN ÖNCEKİ dar aralıkta gelen bir canlı ProjectLog satırı, _liveLines'a
        // eklenir (kilitli) ama ActiveProjectId hâlâ eski değeri taşıdığından _projectText'e YAZILMAZ — snapshot
        // da o satırı zaten kapatmış olur, satır kalıcı olarak kaybolur. DebugAfterStitchLockExited kancası,
        // TAM O aralığın (artık fix ile var OLMAYAN) sınırında senkron (tek thread, sleep/poll YOK — D8) bir
        // canlı satır enjekte ederek bunu kanıtlar: fix'ten ÖNCE bu satır kaybolur, fix'ten SONRA (atama artık
        // kilit içinde olduğundan kanca zaten güncellenmiş ActiveProjectId'yi görür) satır projeye düşer.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, "line1"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 2, "line2"));

        vm.DebugAfterStitchLockExited = () => vm.OnEvent(new ProjectLogEvent("r1", projectId, 3, "race-line3"));

        // [D4 review §2] Seçim üretimde load'dan önce kurulur; proje modu (ActiveProjectId) ancak seçim hâlâ
        // o projedeyken kurulur — bu test tam da o modun kurulduğunu (race-line3'ün projeye düşmesi) doğrular.
        vm.SelectProject(projectId);
        // ThroughLineNumber=0: disk chunk boş, dikiş TÜMÜYLE tamponlanmış canlı satırlardan (line1, line2 —
        // kilit çalıştığı ANDA _liveLines'ta zaten var) üretilir; race-line3 kanca ile kilit KAPANDIKTAN SONRA
        // enjekte edilir — snapshot'ın parçası DEĞİLDİR, yalnız (fix ile) güncel ActiveProjectId sayesinde canlı eklenir.
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, Sequence: 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\nrace-line3\n", vm.GetProjectDocumentText(projectId));
    }

    [Fact] // [Fix wave 1(It-3), Finding 1] ikinci Rebuild'de dosya sıfırdan yazılır (satır no'ları 1'den başlar) —
           // eski run'ın _liveLines/_projectText'te kalan tortusu yeni dikişe SIZMAMALI.
    public async Task Second_Rebuild_clears_stitch_buffers_so_old_run_lines_do_not_leak_into_new_stitch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — startRun senkron atılır, VM içinde yutulur
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        // --- ilk run: P için 1..4 satır, run tamamlanır ---
        await vm.RebuildCommand.ExecuteAsync(null);
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 4; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"old-line{i}"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        // --- ikinci Rebuild: proje log dosyası sıfırdan yazılıyor, satır no'ları yeniden 1'den başlıyor ---
        await vm.RebuildCommand.ExecuteAsync(null); // buffer'lar burada temizlenmeli (fix ÖNCESİ: temizlenmez)
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 2; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"new-line{i}"));

        // Karta tıklama: disk snapshot ikinci run'ın 1. satırını kapatıyor (ThroughLineNumber=1)
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "new-line1\n", IsLast: true, ThroughLineNumber: 1));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        var text = vm.GetProjectDocumentText(projectId);
        Assert.Equal("new-line1\nnew-line2\n", text);
        Assert.DoesNotContain("old-line", text);
    }

    [Fact] // [Fix wave 1(It-3), Finding 2] Skipped proje (cycle üyesi) hiç log dosyası taşımaz — Supervisor
           // error(logNotFound) döner; pending Completion tamamlanmazsa await SONSUZA DEK asılı kalır.
    public async Task LoadProjectLogAsync_completes_on_logNotFound_instead_of_hanging_forever()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\skipped.csproj";
        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, "bağımlılık döngüsünde"));

        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ErrorEvent("logNotFound", projectId));

        // Sınırlı bekleme [D8]: fix ÖNCESİ hiçbir şey Completion'ı tamamlamaz → WaitAsync timeout ile FAIL.
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.ActiveProjectId); // dikiş hiç kurulmadı — proje moduna geçilmedi, VM tutarlı kaldı
    }

    [Fact]
    public async Task LoadProjectLogAsync_multi_chunk_history_is_assembled_in_arrival_order()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "line1\n", IsLast: false, ThroughLineNumber: 2));
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 1, "line2\n", IsLast: true, ThroughLineNumber: 2));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\n", vm.GetProjectDocumentText(projectId));
    }

    // ---------------------------------------------------------------- 4) run dokümanı proje modunda bile birikir

    [Fact]
    public async Task ProjectLog_always_accumulates_into_the_run_document_even_in_project_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        const string b = @"C:\p\b.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));

        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // Artık proje modundayız (ActiveProjectId == a); B için gelen canlı satır YİNE run dokümanına düşmeli.
        vm.OnEvent(new ProjectLogEvent("r1", b, 1, "b-line"));

        Assert.Contains("b-line", vm.GetRunDocumentText());
        Assert.DoesNotContain("b-line", vm.GetProjectDocumentText(a)); // farklı projenin satırı A'nın dokümanına sızmadı
    }

    [Fact]
    public async Task ShowRun_returns_to_run_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.SelectProject(a); // [D4 review §2] proje modu ancak seçim o projedeyken kurulur
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(a, vm.ActiveProjectId);

        vm.ShowRun();

        Assert.Null(vm.ActiveProjectId);
    }

    [Fact] // [D4 review §2] Hızlı select→deselect: gecikmiş chunk ActiveProjectId'yi TAKMAMALI → konsol donmamalı
    public async Task Deselecting_before_the_project_log_chunk_arrives_does_not_freeze_the_run_console()
    {
        // Kanala düşen anlatı satırlarını gözlemlemek için: tüm VM işlemleri ÖNCE (kanala Post eder), sonra pump'ı
        // TEK completing-tick ile boşalt (ConsoleBatcherTests deseni — gerçek 50ms/sleep YOK, D8). Fix ÖNCESİ
        // OnProjectLogChunk ActiveProjectId'yi KOŞULSUZ "a"ya set eder → deselect'ten sonra "a"da TAKILI kalır →
        // AppendRunLine (ActiveProjectId null gate'i) sonraki anlatı satırlarını post EDEMEZ → konsol sessizce DONAR.
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct) { batcher!.Complete(); return Task.CompletedTask; }
        batcher = new ConsoleBatcher(Tick);
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı → SendAsync senkron fırlar
        var vm = new RunViewModel(engine, batcher, () => "r1")
        {
            WallClock = () => new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero),
        };
        const string a = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));

        // (1) kart A seç → yükleme uçuşta (engine ölü → SendAsync senkron fırlar; pending yine de kurulur ve
        //     SendAsync-throws yolu onu BİLEREK null'LAMAZ — gecikmiş bir chunk hâlâ eşleşip dikişi tamamlayabilsin).
        vm.SelectProject(a);
        var load = vm.LoadProjectLogAsync(a);
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // (2) IPC dönmeden kullanıcı A'yı bırakır.
        vm.SelectProject(null);
        Assert.Null(vm.SelectedProjectId);

        // (3) A'nın gecikmiş IsLast chunk'ı gelir — dikiş HER ZAMAN yapılır AMA seçim artık A değil → mod KURULMAZ.
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "disk\n", IsLast: true, ThroughLineNumber: 0));
        Assert.Null(vm.ActiveProjectId);                            // TAKILI DEĞİL (fix'siz kod burada "a" bırakır)
        Assert.Equal("disk\n", vm.GetProjectDocumentText(a));       // dikiş yine de hazır (re-select için)

        // (4) sonraki bir anlatı satırı YİNE konsol kanalına post edilir (donma yok).
        vm.OnEvent(new SyncProgressEvent("Sync complete — all good", "info"));

        var flushes = new List<string>();
        await batcher.PumpAsync((text, _) => flushes.Add(text), CancellationToken.None);
        Assert.Contains("Sync complete — all good", string.Concat(flushes)); // anlatı kanala ulaştı → DONMADI
    }

    // ---------------------------------------------------------------- 5) hata-yalnız outcome'lar runCompleted BEKLEMEZ

    [Fact]
    public async Task Error_only_outcome_reenables_rebuild_without_a_runCompleted()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0)); // koşu başladı: IsRunning=true

        vm.OnEvent(new ErrorEvent("runFailed", "koşu beklenmedik bir istisnayla düştü"));

        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // stop-during-planning: runStarted HİÇ gelmedi, runStopped var ama runCompleted YOK
    public async Task RunStopped_without_a_prior_RunStarted_reenables_rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        // Rebuild tıklanır tıklanmaz IsRunning=true OLMAZ (yalnız runStarted ile olur) — App bu senaryoda
        // buton durumu için TAMAMEN engine event'lerine güvenir; burada doğrudan senaryoyu event'lerle kurarız.
        vm.OnEvent(new ErrorEvent("planFailed", "disk okunamadı"));

        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // runInProgress KOŞAN run'ı ETKİLEMEMELİ — IsRunning olduğu gibi kalır
    public async Task RunInProgress_error_does_not_disturb_an_active_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        vm.OnEvent(new ErrorEvent("runInProgress", "zaten koşuyor"));

        Assert.True(vm.IsRunning);
    }

    /// <summary>[B1] Motor, run slotunu (<c>_runActive</c>) TÜM event'ler yazıldıktan SONRA bırakır
    /// (<c>ExecuteRunAsync</c>'in finally'si) — yani <c>runCompleted</c> App'e ulaştıktan sonra kısa bir pencere
    /// boyunca slot HÂLÂ doludur. Butonlar o anda açıldığı için hızlı bir tıklama <c>runInProgress</c> alır.
    /// <para><c>IsStarting</c> REDDEDİLEN isteğin kendi bayrağıdır: temizlenmezse UI kilit penceresinde
    /// (<see cref="RunViewModel.IsMidRunLocked"/>) SONSUZA DEK donar — Build/Rebuild disabled kalır, Stop
    /// görünür ama arkada durdurulacak bir şey yoktur. Koşan run'a dokunulmaz; kilit gerçekten koşuyorsa
    /// <see cref="RunViewModel.IsRunning"/> üzerinden zaten sürer (kardeş test).</para></summary>
    [Fact]
    public async Task A_rejected_start_releases_its_own_starting_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r2") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        // Kilit penceresi doğrudan kurulur: bu harness'ta engine YOK, gönderim senkron düşer ve BeginRunAsync
        // bayrağı kendi hata dalında zaten geri açar — yani üretim yolu bu ön-koşulu üretemez. Test zaten
        // "bayrak nasıl kondu"yu değil, REDDİ GÖREN OnError'ın onu bırakıp bırakmadığını sürüyor.
        vm.IsStarting = true;
        Assert.True(vm.IsMidRunLocked); // ön-koşul: UI kilit penceresinde

        vm.OnEvent(new ErrorEvent("runInProgress", "A run is already in progress — 'r2' was rejected."));

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsMidRunLocked);              // UI kilitte donmadı
        Assert.True(vm.BuildCommand.CanExecute(null)); // kullanıcı tekrar deneyebilir
    }

    // ---------------------------------------------------------------- 6) Stop/Continue komut gönderimi (gerçek Supervisor)

    /// <summary>Stop <c>StopKind.Graceful</c> gönderir: yeni proje dispatch edilmez, uçuştaki child'lar
    /// post-build copy dahil BİTİRİLİR.
    /// <para><b>Continue kalktıktan sonra da graceful:</b> tek toparlanma yolu Build olduğu için seçim artık
    /// "kaç projelik iş çöpe gidiyor" sorusudur. Drain'de biten projeler <c>PersistBuildStateOnSuccess</c> ile
    /// bankaya girer ve bir sonraki Build onları ATLAR; hard kill ise o projeleri
    /// <c>failed("stopped")</c> yapıp stored state'lerini geçersizleştirir, yani paralellik kadar yarım derleme
    /// çöpe gider ve Build'de baştan derlenir. Stop'un bedeli graceful'de sıfırdır. Bekleme görünürlüğü ayrı
    /// çözüldü (<c>Stopping</c> fazı + pasif buton).</para></summary>
    [Fact]
    public async Task Stop_sends_a_graceful_stop_and_the_engine_acks_it_as_not_hard()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var stopped = new TaskCompletionSource<RunStoppedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e => { if (e is RunStoppedEvent s) stopped.TrySetResult(s); };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // App'in "run aktif" inancı

        await vm.StopCommand.ExecuteAsync(null);

        // Gerçek Supervisor'da eşleşen bir run YOK (yalnız VM'in inancı kuruldu) → TryRequestStop false →
        // host yolu hemen runStopped döner. WasHard, gönderilen StopKind'ın gözlenebilir kanıtıdır — Hard bu
        // bayrağı her zaman true yapardı.
        var result = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("r1", result.RunId);
        Assert.False(result.WasHard);
    }

    /// <summary>[Stopping] Graceful stop uçuştaki child'ların bitmesini bekler; o pencerede uygulamanın
    /// TIKLAMAYI ALDIĞINI göstermesi gerekir. Faz <see cref="AppPhase.Stopping"/>'e geçer ve
    /// <c>StopCommand</c> pasifleşir (aynı Stop'a ikinci kez basmak yeni bir stopRun ÜRETMEZ) — ama kilit
    /// (<see cref="RunViewModel.IsMidRunLocked"/>) SÜRER: motor hâlâ koşuyor, branch/configuration
    /// açılmamalı ve split-button geri gelmemeli.</summary>
    [Fact]
    public async Task Stop_moves_the_phase_to_stopping_and_disables_the_stop_command_while_the_lock_holds()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase);   // ön-koşul
        Assert.True(vm.StopCommand.CanExecute(null));

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Stopping, vm.Phase);
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.True(vm.IsMidRunLocked);
    }

    /// <summary>[Stopping] Faz TIKLAMA ANINDA yazılır — gönderimin dönmesi BEKLENMEZ (gecikmeli bir engine'de
    /// buton saniyelerce "Stop" kalırdı). <c>DebugOnCommandSent</c> gönderimden hemen ÖNCE senkron tetiklenir,
    /// yani burada gözlenen değer "komut yola çıkarken UI'nın hâli"dir.</summary>
    [Fact]
    public async Task The_phase_flips_to_stopping_before_the_command_is_even_sent()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        AppPhase? phaseAtSend = null;
        vm.DebugOnCommandSent = _ => phaseAtSend = vm.Phase;

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Stopping, phaseAtSend);
    }

    /// <summary>[Stopping] Engine hazır değilken gönderim SENKRON fırlar: hiçbir runStopped/runCompleted
    /// gelmeyeceği için faz <see cref="AppPhase.Stopping"/>'te ASILI kalırdı — buton sonsuza dek pasif,
    /// şerit sonsuza dek "Stopping". Gönderim başarısızsa faz geri alınır (<c>BeginRunAsync</c>'in
    /// "gönderim başarısız → IsStarting geri açılır" deseninin ikizi).</summary>
    [Fact]
    public async Task A_stop_that_cannot_be_sent_puts_the_phase_back()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // BAŞLATILMAZ → SendAsync fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Running, vm.Phase);
        Assert.True(vm.StopCommand.CanExecute(null)); // kullanıcı tekrar deneyebilir
    }

    [Fact]
    public async Task Rebuild_is_disabled_while_running_and_reenabled_after_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6b) [Fix wave 1, Finding 1] CanExecuteChanged canlı UI'a ULAŞMALI
    // CommunityToolkit RelayCommand CommandManager.RequerySuggested'a ABONE OLMAZ — [NotifyCanExecuteChangedFor]
    // olmadan gerçek pencerede Stop butonu hiç yeniden sorgulanmaz (hep disabled kalır). Bu testler
    // CanExecute'in DOĞRU DEĞERİ değil, event'in GERÇEKTEN ATEŞLENDİĞİNİ kanıtlar.

    [Fact]
    public async Task RunStarted_raises_CanExecuteChanged_for_Rebuild_and_Stop()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        bool rebuildChanged = false, stopChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // IsRunning false→true

        Assert.True(rebuildChanged); // CanRebuild = !IsRunning
        Assert.True(stopChanged);    // CanStop = IsRunning — Kısıt 3: kullanıcı Stop'a hiç basamaz olmasın diye
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunCompleted_after_a_running_state_raises_CanExecuteChanged_for_Stop_via_IsRunning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        bool stopChanged = false;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // IsRunning true→false

        Assert.True(stopChanged);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6b-2) [Fix wave 1(It-3), Finding 3] planlama sırasında Stop erişilebilir olmalı
    // Supervisor runStarted'dan ÖNCE planlama yapar (scan/graph/topo) ve stop-during-planning'i destekler
    // (ack-debt yolu) — App bunu yalnız IsRunning'e bakarak engelliyordu; IsStarting bu boşluğu kapatır.

    [Fact]
    public async Task RebuildCommand_enables_Stop_and_disables_Rebuild_before_runStarted_arrives()
    {
        // [Fix wave 2, Finding 1] Gerçek (başlatılmış) engine kullanılır: gönderim GERÇEKTEN başarılı olmalı
        // ki "planlama sürüyor, runStarted henüz gelmedi" penceresi doğru simüle edilsin — engine hiç
        // başlatılmamış olsaydı SendAsync senkron fırlardı ve (Finding 1 fix'i ile) IsStarting hemen geri
        // açılırdı; bu artık "send başarısız" senaryosu olur, "planlama sürüyor" değil. Event pump vm.OnEvent'e
        // bağlanmadığından Supervisor'ın gerçek yanıtı (varsa) bu testi etkilemez — yalnız elle enjekte edilen
        // RunStartedEvent state'i değiştirir.
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] bkz. sınıf başındaki sabit — aynı üçlünün ilki
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — runStarted HENÜZ gelmedi — yalnız IsStarting=true

        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        Assert.True(vm.StopCommand.CanExecute(null)); // runStarted sonrası da Stop erişilebilir kalmalı
        Assert.False(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // stop-during-planning ack: runStarted hiç gelmedi, runStopped geldi → Rebuild tekrar aktif olmalı
    public async Task RunStopped_without_runStarted_after_Rebuild_reenables_Rebuild_and_disables_Stop()
    {
        // [Fix wave 2, Finding 1] bkz. yukarıdaki test — gerçek engine gerekir ki RunStoppedEvent geldiğinde
        // IsStarting GERÇEKTEN true olsun (aksi halde unstarted-engine senaryosunda gönderim zaten başarısız
        // olup IsStarting'i erkenden false yapar — test sonucu tesadüfen aynı kalır ama artık "stop-during-
        // planning" senaryosunu DOĞRULAMAZ).
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] bkz. yukarıdaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact] // [Fix wave 3] RunCoordinator.ExecuteRunAsync'in dış catch'i planlama SIRASINDA (runStarted'dan ÖNCE)
           // beklenmedik bir istisnada "runFailed" ErrorEvent'i yayınlar — bu kod eskiden RunEndingErrorCodes'ta
           // yoktu, bu yüzden IsStarting kalıcı true kalır, Rebuild/Continue sonsuza dek kilitli kalırdı.
    public async Task RunFailed_error_during_planning_reenables_Rebuild_and_disables_Stop()
    {
        // bkz. yukarıdaki iki test — gerçek (başlatılmış) engine gerekir ki runFailed geldiğinde IsStarting
        // GERÇEKTEN true olsun (planlama-sırasında-beklenmedik-hata senaryosu).
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] bkz. sınıf başındaki sabit — aynı üçlünün üçüncüsü
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi
        Assert.True(vm.IsStarting);
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent("runFailed", "planlama sırasında beklenmedik hata"));

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6d) [Fix wave 2, Finding 1] gönderim senkron BAŞARISIZ olursa IsStarting geri açılmalı

    [Fact] // engine hiç başlamadı/öldü → SendAsync senkron fırlar → IsStarting KALICI takılmamalı (hiç event gelmeden)
    public async Task RebuildCommand_recovers_IsStarting_when_the_initial_send_fails_synchronously()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — writer null, SendAsync senkron fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarısız — hiçbir engine event'i asla gelmeyecek

        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6e) [Fix wave 2, Finding 2] engine ölüyken kart tıklaması ASILI KALMAMALI

    [Fact] // SendAsync senkron fırlar, hiçbir event Completion'ı tamamlamaz eskiden — LoadProjectLogAsync sonsuza dek asılı kalırdı
    public async Task LoadProjectLogAsync_completes_when_engine_is_dead_without_any_event()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — SendAsync senkron fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dead.csproj";

        var load = vm.LoadProjectLogAsync(projectId);

        // Sınırlı bekleme [D8]: fix ÖNCESİ hiçbir şey Completion'ı tamamlamaz → WaitAsync timeout ile FAIL (hang kanıtı).
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.ActiveProjectId); // dikiş hiç kurulmadı — boş/loglu-olmayan doküman, proje moduna geçilmedi
    }

    // ---------------------------------------------------------------- 6c) [Fix wave 1] TickElapsed enjekte edilen saatle deterministik

    [Fact]
    public async Task TickElapsed_uses_the_injected_clock_deterministically()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long fakeNow = 1_000_000;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", ElapsedMsAtStart: 500));
        Assert.Equal(500, vm.ElapsedMs);

        fakeNow += 250;
        vm.TickElapsed();

        Assert.Equal(750, vm.ElapsedMs);
    }

    [Fact]
    public async Task TickElapsed_does_nothing_once_stopped()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long fakeNow = 1_000_000;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, DurationMs: 4242));

        fakeNow += 10_000;
        vm.TickElapsed();

        Assert.Equal(4242, vm.ElapsedMs); // IsRunning=false → TickElapsed no-op, engine'in kesin süresi korunur
    }

    // ---------------------------------------------------------------- 6f) [Task 16 — It-2 devir §8] EngineExited → run-state reset (wedge fix)
    // EngineHost.EngineExited sinyali eskiden VM'e hiç bağlı DEĞİLDİ (yalnız MainWindow'daki banner'ı
    // güncelliyordu) — engine startRun sonrası runStarted'dan ÖNCE ya da run ORTASINDA ölürse hiçbir IPC
    // event'i asla gelmeyeceğinden IsStarting/IsRunning SONSUZA DEK kilitli kalırdı, "Restart
    // Engine" bile açmıyordu. OnEngineExited bu kamayı kapatır.

    [Fact] // startRun gönderildi, runStarted HENÜZ gelmedi (IsStarting=true) — engine bu pencerede ölürse butonlar açılmalı
    public async Task OnEngineExited_while_IsStarting_resets_run_state_and_reenables_Rebuild()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] yük altında ÖLÇÜLEN kırmızı — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi
        Assert.True(vm.IsStarting);

        vm.OnEngineExited(1);

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null)); // butonlar artık un-wedged
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact] // run ORTASINDA (IsRunning=true, runStarted zaten geldi) engine ölürse yine sıfırlanmalı
    public async Task OnEngineExited_while_IsRunning_resets_run_state_and_reenables_Rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);

        vm.OnEngineExited(139);

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // hiçbir run aktif değilken (idle, zaten temiz) engine ölürse no-op — state bozulmamalı, fırlamamalı
    public async Task OnEngineExited_with_nothing_running_is_a_noop()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(null); // framing-hatası senaryosu (exit code yok) — fırlamamalı

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // normal runCompleted akışı ZATEN sıfırlamıştı — ardından gelen engine-death bu temiz durumu bozmamalı (idempotent)
    public async Task OnEngineExited_after_a_normal_runCompleted_does_not_corrupt_already_reset_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(0); // supervisor bu run'dan SONRA, sıradan bir sebeple kapanmış olabilir

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null)); // hâlâ un-wedged
    }

    [Fact] // engine-died VM-observable bir durum/hata metnine yansımalı (pixel It-4 — burada yalnız VM-state)
    public async Task OnEngineExited_sets_an_observable_engine_died_message()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEngineExited(139);

        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
        // [E2/FIX3] Gevşek Contains("139") tek başına Türkçe bir regresyonu (ör. "Motor beklenmedik…") yine
        // GEÇİRİRDİ (İngilizce-sweep folder'ını boşa çıkarır). Üretim literaline (OnEngineExited) TAM pinle.
        Assert.StartsWith("Engine stopped unexpectedly", vm.EngineDiedMessage, StringComparison.Ordinal);
        Assert.Contains("139", vm.EngineDiedMessage); // exit kodu korunur
    }

    [Fact] // [Review fix, Task 16] EngineDiedMessage engine ölümünden sonra KALICI kalmamalı — sonraki run gerçekten
    // başladığında (runStarted, IPC round-trip kanıtı) VM'in artık CANLI/güncel bir engine'e bağlı olduğu kesinleşir.
    public async Task OnEngineExited_then_next_runStarted_clears_EngineDiedMessage()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEngineExited(139);
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage)); // önce kama-sonrası mesaj var

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Rebuild, 1, 1, "Debug", 0)); // sonraki run gerçekten başladı

        Assert.Null(vm.EngineDiedMessage); // eski ölüm mesajı artık geçerli engine durumunu YANLIŞ yansıtmamalı
    }

    [Fact] // [E2/F3 fold] Engine run ORTASINDA ölürse: Phase Running'de asılı kalmamalı → tutarlı bir terminale (Stopped)
    // çekilir VE EngineDiedMessage kurulur (İngilizce, exit kodu KORUNUR). İkisi de tek atımda pinlenir.
    public async Task OnEngineExited_mid_run_pulls_phase_to_a_terminal_stopped_and_sets_the_message()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase); // gerçekten mid-run

        vm.OnEngineExited(139);

        Assert.Equal(AppPhase.Stopped, vm.Phase);                 // [F3] terminal Phase (Running'de asılı kalmaz)
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
        Assert.StartsWith("Engine stopped unexpectedly", vm.EngineDiedMessage, StringComparison.Ordinal); // [E2/FIX3] İngilizce pinlenir
        Assert.Contains("139", vm.EngineDiedMessage);             // exit kodu korunur
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
    }

    [Fact] // [E2/F3] Engine RESTING bir fazda (Idle) ölürse Phase'i Stopped'a çekmek YANILTICI olurdu — dokunulmaz.
    public async Task OnEngineExited_while_idle_leaves_the_resting_phase_untouched()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", FetchDegraded: false, 3, 0)); // Phase → Idle
        Assert.Equal(AppPhase.Idle, vm.Phase);

        vm.OnEngineExited(1);

        Assert.Equal(AppPhase.Idle, vm.Phase); // resting faz korunur
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
    }

    [Fact] // [Fix wave 1, Finding 1 deseniyle tutarlı] CanExecuteChanged GERÇEKTEN ateşlenmeli, yoksa gerçek pencerede buton hiç yeniden sorgulanmaz
    public async Task OnEngineExited_raises_CanExecuteChanged_for_Rebuild_Stop_and_Continue()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        await vm.RebuildCommand.ExecuteAsync(null); // IsStarting=true
        bool rebuildChanged = false, stopChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEngineExited(1);

        Assert.True(rebuildChanged);
        Assert.True(stopChanged);
    }

    // ---------------------------------------------------------------- 7) gerçek uçtan uca (Rebuild → satırlar + IsRunning)

    [SkippableFact] // vswhere/VS kurulu değilse msbuildNotFound gelir — RunCoordinatorTests ile aynı desen
    public async Task Rebuild_wires_through_the_real_engine_and_populates_rows()
    {
        string root = Directory.CreateTempSubdirectory("bo-vm-rebuild-").FullName;
        // X ↔ Y cycle fixture (RunCoordinatorTests ile aynı desen): iki üyeli bir SCC — [cycle rounds] artık
        // pre-skip edilmez, turlarla derlenir.
        foreach (var (self, other) in new[] { ("X", "Y"), ("Y", "X") })
        {
            Directory.CreateDirectory(Path.Combine(root, self));
            await File.WriteAllTextAsync(Path.Combine(root, self, self + ".csproj"),
                $"""
                <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.{self}</AssemblyName></PropertyGroup>
                  <ItemGroup><Reference Include="OSYS.{other}"><HintPath>..\{other}\bin\OSYS.{other}.dll</HintPath></Reference></ItemGroup>
                </Project>
                """);
        }

        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = root };
        var final = new TaskCompletionSource<IpcEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e =>
        {
            vm.OnEvent(e);
            if (e is RunCompletedEvent or ErrorEvent { Code: "msbuildNotFound" }) final.TrySetResult(e);
        };

        await vm.RebuildCommand.ExecuteAsync(null);
        // [cycle rounds] Hang-guard; 15 sn idi. Fixture artık GERÇEKTEN derliyor (2 tur × 2 üye) — gerekçe ve
        // ölçüm sabitin tek sahibinde: TestPaths.WideRunTimeout. İddiaların hiçbiri süreye bakmaz.
        var outcome = await final.Task.WaitAsync(TestPaths.WideRunTimeout);
        if (outcome is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);

        var done = Assert.IsType<RunCompletedEvent>(outcome);
        // [DEĞİŞEN KURAL — iki kez] Bu iddia önce "X↔Y pre-skip edilir" (Skipped=2) idi; turlar Build/Rebuild'in
        // içine katlanınca "gerçekten derlenir" (Skipped=0) oldu; turlar KENDİ moduna (RunMode.Cycles, Sync'in
        // yanındaki düğme) taşınınca yeniden pre-skip'e döndü. Sebep ölçümdür: katlanmış hâlde iki dakikalık
        // bir Build on beş dakikaya çıkıyordu. Rebuild bir SCC'ye artık HİÇ dokunmaz.
        // Testin ASIL iddiası (Rebuild gerçek motora kablolu, satırlar doluyor, IsRunning düşüyor) her üç
        // sürümde de aynı kaldı.
        Assert.Equal(2, done.Skipped);
        Assert.Equal(2, vm.Projects.Count);
        Assert.All(vm.Projects, p => Assert.Equal(ProjectRowState.Skipped, p.State));
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- 8) [Task 17] depIssue VM state

    [Fact]
    public async Task ProjectSucceeded_with_depIssues_sets_HasDepIssue_and_DepIssues_roots_on_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100, DepIssues: ["B", "C"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.HasDepIssue);
        Assert.Equal(["B", "C"], row.DepIssues);
    }

    [Fact]
    public async Task ProjectFailed_with_depIssues_sets_HasDepIssue_and_DepIssues_roots_on_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\b.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "B"));

        vm.OnEvent(new ProjectFailedEvent("r1", projectId, 100, "exit 1", DepIssues: ["X"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.HasDepIssue);
        Assert.Equal(["X"], row.DepIssues);
    }

    [Fact]
    public async Task ProjectSucceeded_without_depIssues_leaves_HasDepIssue_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\clean.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Clean"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // DepIssues null

        var row = Assert.Single(vm.Projects);
        Assert.False(row.HasDepIssue);
        Assert.Null(row.DepIssues);
    }

    // ---------------------------------------------------------------- 8b) [cycle rounds/Task 8] round + unsettled/unconverged VM state

    [Fact]
    public async Task ProjectSucceeded_carries_CycleUnsettled_onto_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100, DepIssues: null, CycleUnsettled: true));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnsettled);
    }

    [Fact]
    public async Task ProjectSucceeded_without_cycleUnsettled_leaves_the_row_flag_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // CycleUnsettled default false

        var row = Assert.Single(vm.Projects);
        Assert.False(row.CycleUnsettled);
    }

    [Fact]
    public async Task ProjectSkipped_carries_CycleUnconverged_onto_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged);
    }

    /// <summary>
    /// [cycles] "Kalıcı kırık döngü" bayrağının ASIL kaynağı: koşunun kendi yakınsamama kararı.
    ///
    /// <para>Bayrak eskiden yalnız motorun pre-skip'inden gelirdi ("önceki koşuda yakınsamamıştı, hiç
    /// denemiyorum"). O pre-skip kalktı — açık bir Resolve basışı artık her zaman taze bir deneme yapar — ve
    /// bayrağın tek üreticisi de onunla birlikte kalkmıştı. Yeni kaynak hem daha dürüst hem daha erken:
    /// hatırlanan bir geçmiş değil ŞU koşunun kanıtı, ve kullanıcı bunu ikinci bir basışı beklemeden, tam da
    /// denemenin bittiği koşuda görür.</para>
    /// </summary>
    [Fact]
    public async Task A_cycle_that_ends_without_progress_marks_all_its_members_as_unconverged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj", b = @"C:\p\b.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [new ProjectNode(a, "A", a, [], [], 0, null, null, true, null),
             new ProjectNode(b, "B", b, [], [], 0, null, null, true, null)],
            [[a, b]], [], []));

        // Grup turlarını harcadı: A yeşil bitti, B patladı — ve grup YAKINSAMADI.
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", b, 100, "boom"));
        vm.OnEvent(new CycleCompletedEvent("r1", a, CycleOutcome.NoProgress, 2, 2, 1, 400));

        // İkisi de işaretli: sıkışan GRUPTUR, tek tek üyeler değil — yeşil biten üyenin çıktısı da bayat.
        Assert.All(vm.Projects, p => Assert.True(p.CycleUnconverged));
        // Sayaç statüden bağımsız okur: üyeler Failed/Succeeded, Skipped DEĞİL.
        Assert.Equal(2, vm.Counters.StuckCycles);
    }

    /// <summary>Yakınsayan grup hiçbir üyesini işaretlemez — kontrol grubu.</summary>
    [Fact]
    public async Task A_cycle_that_converges_marks_nothing()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100));

        vm.OnEvent(new CycleCompletedEvent("r1", a, CycleOutcome.Converged, 1, 2, 0, 400));

        Assert.False(Assert.Single(vm.Projects).CycleUnconverged);
        Assert.Equal(0, vm.Counters.StuckCycles);
    }

    [Fact]
    public async Task ProjectSkipped_without_cycleUnconverged_leaves_the_row_flag_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\b.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.UpToDate)); // CycleUnconverged default false

        var row = Assert.Single(vm.Projects);
        Assert.False(row.CycleUnconverged);
    }

    /// <summary>[cycle rounds/Task 9 review fix 1] Kök neden: <c>CycleUnconverged</c>'i yazan TEK yer
    /// <see cref="RunViewModel"/>'in <c>OnProjectSkipped</c>'idir; satır nesneleri segmentler arası HAYATTA
    /// KALIR (<c>Projects.Clear()</c> yalnız <see cref="RunMode.Rebuild"/>'de) — kaynak düzeltilip proje
    /// GERÇEKTEN derlenirse bayat bayrak temizlenmezse "az önce düzelen proje" kalıcı-kırık gibi render edilir
    /// (Task 9'un önlemeye çalıştığı yanlış bilginin TERSİ). <c>OnProjectDone</c> artık her terminal derleme
    /// sonucunda (Succeeded/Failed — ikisi de proje GERÇEKTEN invoke edildi demektir) bayrağı temizler.</summary>
    [Fact]
    public async Task ProjectSucceeded_after_a_prior_CycleUnconverged_skip_clears_the_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));
        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged); // ön-koşul: bayrak gerçekten set edildi

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        Assert.False(row.CycleUnconverged);
    }

    /// <summary>[cycle rounds/Task 9 review fix 1] Aynı temizlik <c>Failed</c> için de geçerli — proje bu run'da
    /// GERÇEKTEN invoke edildiyse (başarılı ya da başarısız fark etmez) artık "hiç invoke edilmeden pre-skip"
    /// hikayesi doğru DEĞİLDİR.</summary>
    [Fact]
    public async Task ProjectFailed_after_a_prior_CycleUnconverged_skip_clears_the_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));
        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged); // ön-koşul

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", projectId, 100, "boom"));

        Assert.False(row.CycleUnconverged);
    }

    [Fact]
    public async Task RunCompleted_sets_DepIssueCount_from_the_event()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", 0));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 1, 0, 0, 500, DepIssueCount: 2));

        Assert.Equal(2, vm.DepIssueCount);
    }

    // ---------------------------------------------------------------- 9) [Task 17] will-build + succeeded→clean live transition

    [Fact]
    public async Task BuildPreviewEvent_pre_populates_rows_with_WillBuild_dirty_clean_and_hollow()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\dirty.csproj", "Dirty", true),
            new BuildPreviewItem(@"C:\p\clean.csproj", "Clean", false),
            new BuildPreviewItem(@"C:\p\hollow.csproj", "Hollow", null),
        ]));

        Assert.Equal(3, vm.Projects.Count);
        Assert.True(vm.Projects.Single(p => p.Name == "Dirty").WillBuild);
        Assert.False(vm.Projects.Single(p => p.Name == "Clean").WillBuild);
        Assert.Null(vm.Projects.Single(p => p.Name == "Hollow").WillBuild);
    }

    // ---------------------------------------------------------------- [W1] per-proje sha (BuiltCommit) wire

    [Fact]
    public async Task BuildPreviewEvent_fills_each_rows_current_sha_from_the_built_commit()
    {
        // [W1] Sha çiftinin sol yarısı artık GERÇEKTEN akar (It-4b'de üretimde HEP null'dı). Değer HAM taşınır —
        // 7 haneye kısaltma kartın işidir (bkz. ProjectRowTests), VM veriyi kırpmaz.
        const string built = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\built.csproj", "Built", true, built),
            new BuildPreviewItem(@"C:\p\never.csproj", "Never", true), // hiç derlenmemiş
        ]));

        Assert.Equal(built, vm.Projects.Single(p => p.Name == "Built").CurrentSha);
        Assert.Null(vm.Projects.Single(p => p.Name == "Never").CurrentSha); // uydurulmaz
    }

    [Fact]
    public async Task A_later_segments_preview_refreshes_the_current_sha_of_an_already_terminal_row()
    {
        // [W1 · Task 1] OnBuildPreview'daki terminal-satır `continue` guard'ı YALNIZ WillBuild'i korur (segment 1'in
        // canlı succeeded→clean geçişi ezilmesin) VE YALNIZ koşu sürerken (RunActive) — bu yüzden run burada
        // GERÇEKTEN sürüyor olmalı (RunStartedEvent + henüz RunCompleted YOK), aksi halde bu artık "segment 2"
        // değil, koşu bittikten sonraki bağımsız bir tazeleme olurdu (bkz. aşağıdaki A_post_run_preview_* testleri).
        // CurrentSha guard'dan ÖNCE atanır: segment 2'nin okuduğu build-state segment 1'in persist'ini içerir,
        // yani derlenmiş satırın sol yarısı ancak burada tazelenebilir.
        // [DEĞİŞEN KURAL — Task 1] Eski iddia: guard KOŞULSUZDU — terminal satırın WillBuild'i HİÇBİR önizlemeyle
        // yazılmazdı; test bu yüzden RunStartedEvent'siz kuruluyordu. Değişme gerekçesi: koşu bittikten sonra gelen
        // önizleme (pencereye dönüşün sessiz Sync'i) de guard'a çarpıyor, arka planda değişen proje yeşil kalıyordu.
        // Koruma artık yalnız koşu sürerken geçerli; bu test onu o koşulda pinler.
        const string oldSha = "1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string newSha = "2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string projectId = @"C:\p\dirty.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true, oldSha)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // satır artık terminal + clean

        // Continue segmenti: preview BAYAT willBuild=true taşır ama build-state TAZE commit'i taşır. Run HÂLÂ
        // sürüyor (RunCompleted henüz gelmedi) — guard bu yüzden hâlâ devrede.
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true, newSha)]));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(newSha, row.CurrentSha); // sha TAZELENDİ
        Assert.False(row.WillBuild);          // ama canlı succeeded→clean geçişi KORUNDU
    }

    [Fact]
    public async Task A_post_run_preview_refreshes_a_succeeded_rows_standing()
    {
        // [Task 1] Koşu BİTMİŞ (RunCompleted geldi, RunActive artık false), satır Succeeded + temiz. Pencereye
        // dönüşün tetiklediği sessiz Sync'in önizlemesi o satır için WillBuild=true derse — ör. döngü üyesi
        // arka planda değişti — satırın kararı da tazelenmeli: elle Sync beklemeden gri/"modified" olmalı.
        const string projectId = @"C:\p\cycle.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Cycle", true, Reason: WillBuildReason.NeverBuilt)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Cycle"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Succeeded, row.State);
        Assert.False(row.WillBuild); // ön koşul: koşu az önce temizledi

        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(projectId, "Cycle", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));

        Assert.True(row.WillBuild);
        Assert.Equal(WillBuildReason.SignatureChanged, row.WillBuildReason);
        Assert.Equal(StandingStatus.Stale, row.Standing);
        Assert.Equal(VisualStatus.Stale, row.VisualStatus); // gri/"modified", yeşil ✓ DEĞİL
    }

    [Fact]
    public async Task A_post_run_preview_refreshes_a_skipped_rows_standing()
    {
        // [Task 1] Aynı kusur, döngü üyesinin normal bittiği hal: run bitince Skipped. Kusurun asıl senaryosu
        // budur (kullanıcı testi 2026-09-19) — döngü üyeleri Build'de hep Skipped biter.
        const string projectId = @"C:\p\member.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Member", false, Reason: WillBuildReason.UpToDate)]));
        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.UpToDate));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 1, 0, 50));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Skipped, row.State);

        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(projectId, "Member", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));

        Assert.True(row.WillBuild);
        Assert.Equal(WillBuildReason.SignatureChanged, row.WillBuildReason);
        Assert.Equal(StandingStatus.Stale, row.Standing);
        Assert.Equal(VisualStatus.Stale, row.VisualStatus);
    }

    [Fact]
    public async Task A_post_run_preview_can_turn_a_failed_row_green_again()
    {
        // [Task 1] Failed biten satır için de aynı kural: önizleme artık güncel diyorsa satır yeşile döner —
        // koşu bitmiş olmak kırmızıyı sonsuza dek DONDURMAZ.
        const string projectId = @"C:\p\broken.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Broken", true, Reason: WillBuildReason.SignatureChanged)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Broken"));
        vm.OnEvent(new ProjectFailedEvent("r1", projectId, 100, "exit 1", Evidence: true));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 1, 0, 0, 100));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Failed, row.State);

        // Kullanıcı elle düzeltip başka bir yolla derledi — sessiz Sync artık güncel diyor.
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Broken", false, Reason: WillBuildReason.UpToDate)]));

        Assert.False(row.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, row.WillBuildReason);
        Assert.Equal(StandingStatus.Current, row.Standing);
        Assert.Equal(VisualStatus.Current, row.VisualStatus); // yeşil, kırmızı DEĞİL
    }

    [Fact]
    public async Task A_post_run_preview_never_raises_InRunQueue_on_a_terminal_row()
    {
        // [Task 1 review fix round 1] InRunQueue'nun belgelenen değişmezi (ProjectRowViewModel.InRunQueue'nun
        // XML yorumu): YALNIZ koşan run'ın KENDİ BuildPreviewEvent'inden yazılır, koşu bitince
        // PropagateRunActive onu düşürür (RunActive=false ⇒ InRunQueue=false, ~satır 1507) ve bir sonraki
        // run'ın kendi önizlemesine kadar bir daha YÜKSELMEZ. Bu task terminal satırların karar alanlarını
        // (WillBuild/Reason/Conditional/DependencyRoots) koşu bittikten sonra da tazeliyor — ama InRunQueue
        // AYRI bir kanaldır (run-scoped) ve AYNI guard'ı paylaşamaz: koşu sürmüyorken gelen bir önizleme
        // kuyruğu bir daha YÜKSELTMEMELİDİR, aksi halde bu değişmez sessizce bozulur (bugün gözlemlenemez —
        // TEK okuyucu <c>Status</c>'un <c>IsRunActive &amp;&amp; InRunQueue</c> dalı ve terminal State zaten
        // ondan ÖNCE eşleşir — ama alanın kendi doğruluğu bağımsız korunmalı).
        const string projectId = @"C:\p\cycle.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Cycle", true, Reason: WillBuildReason.NeverBuilt)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Cycle"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        var row = Assert.Single(vm.Projects);
        Assert.False(row.InRunQueue); // ön koşul: koşu bitince PropagateRunActive kuyruğu düşürdü

        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(projectId, "Cycle", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));

        Assert.True(row.WillBuild);   // karar tazelendi — bu task'ın asıl davranışı, KIRILMADI
        Assert.False(row.InRunQueue); // ama kuyruk YİNE düşük — koşu sürmüyor, bu YÜKSELMEMELİ
    }

    /// <summary>
    /// [design v1.16.0 §2.4] Satırın karar etiketi CANLI geçişi izler: bir proje bu koşuda derlendiği anda
    /// satır "up to date" yazar, patladığı anda "failed". Motorun bir sonraki önizlemesi BEKLENMEZ — o
    /// önizleme bir Sync'e kadar gelmeyebilir ve satır o süre boyunca artık doğru olmayan bir gerekçeyi
    /// ("modified") taşırdı.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Test, tazelenen olgular arasında
    /// <c>LastBuiltAt</c>'i de okurdu (satır "up to date · just now" yazabilsin diye). Yaş kalktı, alan
    /// satırdan da kalktı; canlı geçişin tazelediği olgular gerekçe + "kendi dosyası değişti mi"dir.</para>
    /// </summary>
    [Fact]
    public async Task A_finished_project_updates_the_facts_its_decision_label_reads()
    {
        const string id = @"C:\p.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", true, null, WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(WillBuildReason.SignatureChanged, row.WillBuildReason);   // ön koşul: "modified" diyordu

        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120));

        Assert.False(row.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, row.WillBuildReason);
        Assert.False(row.OwnFilesChanged);
    }

    /// <summary>[Task 7 — Faz 3, spec 2026-09-18 §5, P8] Bu araç bir projeyi başarıyla derlediği an, önceki
    /// "bu araç dışında derlendi" gerekçesi ARTIK GEÇERSİZDİR — çıktı şimdi aracın kendi eseri.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Burada İKİ test vardı ve ikisi de bir ZAMAN
    /// alanını pinliyordu: <c>A_preview_with_an_output_time_reaches_the_row</c> (önizlemenin
    /// <c>OutputBuiltAt</c>'i satıra AYNEN ulaşır) ve <c>A_successful_build_clears_the_output_time</c> (başarı
    /// o kanıtı <c>null</c>'a çeker — <c>FailedAt</c> ile aynı kural). O alanların TEK okuyucusu etiketin
    /// "built outside this tool 5m ago" yaşıydı; yaş kalkınca alan satırdan da kalktı. Geriye alanın
    /// ANLAMI kaldı ve onu artık gerekçenin kendisi taşır: başarıdan sonra satır "dışarıda derlendi"
    /// DEMEZ.</para></summary>
    [Fact]
    public async Task A_successful_build_ends_the_built_outside_reason()
    {
        const string id = @"C:\p.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", false, null, WillBuildReason.BuiltOutside,
                OutputBuiltAt: new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero))]));
        var row = Assert.Single(vm.Projects);
        Assert.Equal(WillBuildReason.BuiltOutside, row.WillBuildReason); // ön koşul

        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120));

        Assert.Equal(WillBuildReason.UpToDate, row.WillBuildReason);
    }

    /// <summary>
    /// [Task 4 — kök neden C] Bu koşuda dep-issue'lu biten bir başarı "succeeded→clean" (UpToDate) geçişine
    /// GİRMEZ: bağımlılığı hâlâ hatalıydı, çıktı bayat bir bağımlılığa link'li. Satır <c>WaitingForDependency</c>
    /// gerekçesine geçer (kökler event'ten — Sync'i beklemez) ve <c>Conditional=true</c> olur (kesin
    /// derlenecekler kümesine girmez).
    /// <b>[DEĞİŞEN KURAL]</b> Eskiden HER başarı (dep-issue'lu dahil) <c>UpToDate</c>'e düşerdi.
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Test ayrıca <c>LastBuiltAt</c>'in ŞİMDİ'ye
    /// güncellendiğini de okurdu ("kart az önce derlendi"); o alan satırdan kalktı (etiket yaş taşımıyor).
    /// </summary>
    [Fact]
    public async Task A_success_with_a_live_dep_issue_transitions_to_waiting_for_dependency_not_up_to_date()
    {
        const string id = @"C:\p.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", true, null, WillBuildReason.DepIssue, OwnFilesChanged: false)]));

        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120, DepIssues: ["Up"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.WillBuild);       // hâlâ "dirty" — kök düzelene kadar bayat kalır (WillBuildEvaluator'la AYNI)
        Assert.Equal(WillBuildReason.WaitingForDependency, row.WillBuildReason);
        Assert.True(row.Conditional);     // kesin derlenecekler kümesine (dalga/kuyruk/_willBuildIds) GİRMEZ
        Assert.Equal(["Up"], row.DependencyRoots);
    }

    /// <summary>
    /// [Task 4 review — C1] Bu koşuda dep-issue'lu biten bir satırın etiketi bir SONRAKİ Sync'te AYNI kalmalı:
    /// disk hâli değişmedi (kayıtlı kökler, imza), yalnız defter yeniden okundu.
    ///
    /// <para><b>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4]</b> Eski iddia satırın "affected · up to date ·
    /// just now" dediğiydi (Task 4'ün <c>conditional</c> ayrımı: motor bu koşuyu gerçekten bekletiyorsa yuva
    /// kökleri tooltip'inde tekrarlardı). O ayrım <see cref="DecisionLabel"/>'den TAMAMEN kalktı:
    /// <see cref="WillBuildReason.WaitingForDependency"/> artık <see cref="WillBuildReason.UpToDate"/> ile
    /// BİREBİR okunur — satır düz "up to date" der, hangi kökün beklendiğini yalnız uyarı üçgeni söyler. Testin
    /// ASIL iddiası (Sync'ten sonra etiket TİTREMEZ) DEĞİŞMEDİ, yalnız beklenen sözcük değişti.</para>
    /// </summary>
    [Fact]
    public async Task A_dep_issue_wait_label_survives_a_sync_without_flipping()
    {
        const string id = @"C:\p\a.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", true, null, WillBuildReason.DepIssue, OwnFilesChanged: false)]));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120, DepIssues: ["Up"]));

        var row = Assert.Single(vm.Projects);
        RowDecision Label() => DecisionLabel.For(row.WillBuild, row.WillBuildReason, row.OwnFilesChanged,
            row.LocalEdits, row.InCycle);
        var beforeSync = Label();
        Assert.Equal("up to date", beforeSync.Word);
        Assert.False(beforeSync.Stale);

        // Run biter, sonra bir Sync koşar — NeutralizeRows() State'i Pending'e döndürür (IsRunning
        // false olmalı), Sync'in kendi önizlemesi disk hâlini (değişmemiş) aynen yansıtır.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(id, "A", true, null, WillBuildReason.WaitingForDependency,
            OwnFilesChanged: false, Conditional: true, DependencyRoots: ["Up"])]));

        var afterSync = Label();
        Assert.Equal(beforeSync, afterSync); // etiket TİTREMEZ
    }

    /// <summary>
    /// [Task 4 review round 1+2 — I1 (i)] Bir SCC üyesi dep-issue'lu bitse bile canlı geçiş onu TEK BAŞINA
    /// koşullu SANMAMALI: <c>ConditionalRebuild.AppliesTo</c>'nun <c>!cycleGroupMember</c> kuralıyla aynı
    /// gerekçe — üye grubuyla derlenir (Cycles, turlar) ya da bir Build koşusunda hiç dispatch edilmez;
    /// "rebuilds once that dependency is healthy again" tek başına verilen bir SÖZDÜR ve üye için asla tutulmaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL — round 2]</b> Round 1'in iddiası satırın <c>UpToDate</c>'e (Task 4 öncesi
    /// davranış) döndüğüydü. Eksikti: grup YAKINSADIYSA (<c>CycleUnsettled=false</c>) defter GERÇEKTEN not+kök
    /// yazar ve bir sonraki Sync'in <c>WillBuildEvaluator</c>'ı bu üyeyi <c>WaitingForDependency</c> okur
    /// (<c>WillBuild</c> döngü kapsamı yüzünden yine <c>false</c>'a zorlanır, ama gerekçe bir disk olgusu
    /// olarak hesaplanmaya devam eder — §13.2). Satır <c>UpToDate</c> yazarsa Sync'ten SONRA
    /// <c>WaitingForDependency</c>'ye FLİP EDER — round 1'in kapatmadığı boşluk tam buydu. Artık canlı geçiş
    /// motorun bir sonraki önizlemesiyle BİREBİR AYNI üçlüyü (<c>WillBuild=false</c>, <c>WaitingForDependency</c>,
    /// <c>Conditional=false</c>) üretir; <c>DependencyRoots</c> de dolar (tooltip roots'u Sync'te de gelir).</para>
    /// </summary>
    [Fact]
    public async Task A_converged_cycle_member_success_with_a_dep_issue_waits_without_being_conditional()
    {
        const string id = @"C:\p\a.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0) with { InCycle = true }], [[id]], [], []));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120, DepIssues: ["Up"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.InCycle); // ön-koşul
        Assert.False(row.Conditional);   // TEK BAŞINA asla koşullu değil — grup mekanizmasına tabi
        Assert.False(row.WillBuild);     // döngü kapsamı yüzünden zorlanır (Build bir SCC'yi asla derlemez)
        Assert.Equal(WillBuildReason.WaitingForDependency, row.WillBuildReason); // ama disk olgusu budur
        Assert.Equal(["Up"], row.DependencyRoots);
    }

    /// <summary>
    /// [Task 4 review round 2 — I1] Bir sonraki Sync (post-round-2) bu üye için AYNEN bu üçlüyü üretir — etiket
    /// TİTREMEMELİ.
    ///
    /// <para><b>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4]</b> Eski iddia satırın "affected"/soluk-değil
    /// (<c>Stale=true</c>) dediğiydi: <c>Conditional=false</c> (üye tek başına asla koşullu değil, grup
    /// mekanizmasına tabi) olduğu için eski <c>DecisionLabel</c> genel default dalına düşüyordu.
    /// <c>DecisionLabel</c> artık <c>Conditional</c>'ı hiç okumuyor (bkz. sınıf özeti) — reason
    /// <see cref="WillBuildReason.WaitingForDependency"/> olduğu sürece kapsamın zorlayıp zorlamadığından
    /// bağımsız düz "up to date" yazar. Testin ASIL iddiası (Sync'ten sonra etiket TİTREMEZ) DEĞİŞMEDİ.</para>
    /// </summary>
    [Fact]
    public async Task A_converged_cycle_member_wait_label_survives_a_sync_without_flipping()
    {
        const string id = @"C:\p\a.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0) with { InCycle = true }], [[id]], [], []));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120, DepIssues: ["Up"]));

        var row = Assert.Single(vm.Projects);
        RowDecision Label() => DecisionLabel.For(row.WillBuild, row.WillBuildReason, row.OwnFilesChanged,
            row.LocalEdits, row.InCycle);
        var beforeSync = Label();
        // Reason bir disk olgusudur; WaitingForDependency artık UpToDate ile birebir okunur — kapsamın
        // zorlayıp zorlamadığı (Conditional=false, üye tek başına asla koşullu değil) etiketi ETKİLEMEZ.
        Assert.Equal("up to date", beforeSync.Word);
        Assert.False(beforeSync.Stale);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0) with { InCycle = true }], [[id]], [], []));
        // Sync'in GERÇEKTEN üreteceği önizleme (WillBuildEvaluator: outOfScope⇒WillBuild=false, gerekçe yine de
        // WaitingForDependency; AppliesTo: WillBuild==true şartı düşer ⇒ Conditional=false).
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(id, "A", false, null, WillBuildReason.WaitingForDependency,
            OwnFilesChanged: false, Conditional: false, DependencyRoots: ["Up"])]));

        var afterSync = Label();
        Assert.Equal(beforeSync, afterSync); // etiket TİTREMEZ
    }

    /// <summary>
    /// [Task 4 review — I1 (ii)] Yakınsamayan bir grubun üyesi (<c>CycleUnsettled=true</c>, <c>Trusted=false</c> —
    /// arkasında durulamayan bir başarı, <c>RunCoordinator</c> onu PERSIST ETMEZ) canlı geçişte koşullu SANILMAZ.
    ///
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eski iddia: satır <c>UpToDate</c> okur ("defter bu
    /// başarıdan hiçbir şey öğrenmedi, bugünkü olguya dön"). Yanlıştı: motor aynı anda defterine
    /// <c>LastResult=Failed</c>, <c>FailedSignature=null</c> yazar (<c>InvalidateBuildStateOnFailure</c>) ve bir
    /// sonraki Sync'in <c>WillBuildEvaluator</c>'ı bunu <c>NeverBuilt</c> okur — satır canlıda yeşil ✓
    /// ("Up to date", ✓ sayacında), Sync'ten sonra gri ○ idi. Karar artık motorundur ve olayla gelir
    /// (<c>ProjectSucceededEvent.Trusted</c>); satır Sync'in diyeceğini şimdiden der: gri, "To build".</para>
    /// </summary>
    [Fact]
    public async Task An_untrusted_cycle_member_success_reads_never_built_like_the_next_sync()
    {
        const string id = @"C:\p\a.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0) with { InCycle = true }], [[id]], [], []));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120, DepIssues: ["Up"], CycleUnsettled: true, Trusted: false));

        var row = Assert.Single(vm.Projects);
        Assert.False(row.Conditional);
        Assert.Null(row.DependencyRoots);
        // Sync'in önizlemesi: defter kanıtsız hata ⇒ NeverBuilt; kapsam dışı üye ⇒ WillBuild=false.
        Assert.Equal(WillBuildReason.NeverBuilt, row.WillBuildReason);
        Assert.False(row.WillBuild);
        Assert.Equal(VisualStatus.Stale, row.VisualStatus);                  // gri, yeşil ✓ DEĞİL
        Assert.Equal("To build", StatusGlyph.LabelFor(row.VisualStatus));  // ekran okuyucu da aynı şeyi duyar
        Assert.Equal((0, 1), (vm.Counters.Current, vm.Counters.Stale));    // ✓ değil ○ sayılır
        Assert.Equal(1, vm.Counters.Succeeded);                              // koşunun tablosu yine "başarılı" der

        // Bir sonraki Sync'in GERÇEKTEN üreteceği önizleme — satır titremez.
        var before = (row.WillBuild, row.WillBuildReason, row.VisualStatus);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(id, "A", false, null, WillBuildReason.NeverBuilt)]));
        Assert.Equal(before, (row.WillBuild, row.WillBuildReason, row.VisualStatus));
    }

    /// <summary>[final review I1] Kontrol grubu: güvenilir (varsayılan) bir döngü üyesi başarısı yeşil kalır.</summary>
    [Fact]
    public async Task A_trusted_cycle_member_success_stays_green()
    {
        const string id = @"C:\p\a.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(id, "A", 0) with { InCycle = true }], [[id]], [], []));
        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(WillBuildReason.UpToDate, row.WillBuildReason);
        Assert.Equal("Up to date", StatusGlyph.LabelFor(row.VisualStatus));
        Assert.Equal((1, 0), (vm.Counters.Current, vm.Counters.Stale));
    }

    /// <summary>Dep-issue'suz bir başarı canlı geçişte bugünkü gibi kalır — carried item'in ETKİLEMEDİĞİ satır.</summary>
    [Fact]
    public async Task A_success_without_a_dep_issue_still_transitions_to_up_to_date()
    {
        const string id = @"C:\p.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", true, null, WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));

        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", id, 120)); // DepIssues null

        var row = Assert.Single(vm.Projects);
        Assert.False(row.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, row.WillBuildReason);
        Assert.False(row.Conditional);
        Assert.Null(row.DependencyRoots);
    }

    /// <summary>Patlayan proje "failed" olgusuna geçer — bir sonraki koşuda yeniden denenecektir.
    /// <para>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Test ayrıca satırın <c>LastBuiltAt</c>'inin null
    /// kaldığını okurdu ("başarı yok → yaş da yok"); yaş kalkınca o alan satırdan da kalktı.</para>
    /// <para>[R-M4b] Fixture motorun GERÇEK olayını taşır: <c>"exit N"</c> (<c>RunCoordinator.ReasonFor</c>) ve
    /// motorun kanıt kararı (<c>Evidence: true</c> — defter yazımıyla aynı kapı). Eski fixture uydurma bir metin
    /// (<c>"CS0103"</c>) veriyordu ve kanıt bayrağı yoktu; kanıtsız hatanın yolu RunViewModelStateTests'te.</para></summary>
    [Fact]
    public async Task A_failed_project_reports_the_failure_as_its_reason()
    {
        const string id = @"C:\p.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(id, "A", true, null, WillBuildReason.SignatureChanged)]));

        vm.OnEvent(new ProjectStartedEvent("r1", id, "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", id, 90, "exit 1", null, Evidence: true));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(WillBuildReason.LastFailed, row.WillBuildReason);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — v1.16.0] Bu test "hedef sha her satıra İTİLİR ve olay sırasından bağımsızdır" diye
    /// pinliyordu: <c>buildPreview</c> deterministik olarak <c>syncCompleted</c>'dan önce geldiği için kart
    /// hedefi ata ağaçtan ÇEKSEYDİ satır onu null'ken okur ve bir daha tazelenmezdi. Satırda artık hedef sha
    /// YOK — sağ yuvada kararın kendisi duruyor ve hedef commit motorda kalıyor (konsol satırı + pull).
    /// Yerini alan iddia, aynı olay sırası sorusunun YENİ hâlidir: <c>syncCompleted</c>'ın taşıdığı MESAFE
    /// (<c>Behind</c>) alt bardaki chip'e ulaşmalı.
    /// </summary>
    [Fact]
    public async Task Sync_completed_carries_the_distance_from_the_remote_to_the_action_bar()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Null(vm.Behind);
        Assert.False(vm.CanShowBehind);       // Sync yapılmadan chip yok

        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4", FetchDegraded: false, 1, 0, Behind: 3));

        Assert.Equal(3, vm.Behind);
        Assert.True(vm.CanShowBehind);

        // Güncel: chip düşer (sayı biliniyor ama sıfır).
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4", FetchDegraded: false, 1, 0, Behind: 0));
        Assert.False(vm.CanShowBehind);

        // Çevrimdışı: mesafe BİLİNMEZ → chip yine yok (uydurma sayı gösterilmez).
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4", FetchDegraded: true, 1, 0));
        Assert.Null(vm.Behind);
        Assert.False(vm.CanShowBehind);
    }

    [Fact] // buildPreview arrives BEFORE the per-project events; ProjectStarted on an already-previewed row must still flip it to Started
    public async Task ProjectStarted_after_a_buildPreview_row_still_transitions_the_row_to_Started()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Started, row.State);
        Assert.True(row.WillBuild); // preview'ın dirty=true'su korunur — henüz succeeded değil
    }

    [Fact] // v7Δ8: proje succeeded olduğu ANDA (bu run içinde canlı) willBuild=false (clean/güncel) olur
    public async Task ProjectSucceeded_flips_WillBuild_to_clean_immediately()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        var row = Assert.Single(vm.Projects);
        Assert.False(row.WillBuild);
    }

    [Fact] // hollow (WillBuild=null — imza yok/pre-Sync) preview'dan SONRA da (henüz succeeded olmadıkça) null kalmalı
    public async Task Hollow_WillBuild_is_preserved_until_a_project_succeeds()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\hollow.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Hollow", null)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Hollow"));

        Assert.Null(Assert.Single(vm.Projects).WillBuild); // henüz succeeded değil — hollow korunur

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        Assert.False(Assert.Single(vm.Projects).WillBuild); // succeeded → artık clean, hollow değil
    }

    [Fact] // preview hiç gelmeden (buildPreview YOK) doğrudan ProjectStarted gelirse satır yine hollow (null) başlar
    public async Task ProjectStarted_without_a_prior_buildPreview_defaults_WillBuild_to_hollow_null()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        Assert.Null(Assert.Single(vm.Projects).WillBuild);
    }

    [Fact] // [Review fix, Task 17] RunCoordinator, Continue segmentinde AYNI (dondurulmuş) plan'dan türetilmiş
           // buildPreview'ı YENİDEN yayınlar (Projects Continue'da temizlenmez) — bu yeniden-yayın, segment 1'de
           // gerçekleşen succeeded→clean canlı geçişini EZMEMELİ
    public async Task BuildPreviewEvent_on_a_Continue_segment_does_not_clobber_an_already_clean_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";

        // segment 1: preview (dirty) → started → succeeded (canlı clean geçişi)
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        Assert.False(Assert.Single(vm.Projects).WillBuild); // clean

        // Sonraki koşu aynı (bayat) planı yeniden preview eder — WillBuild=true (dirty)
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));

        Assert.False(Assert.Single(vm.Projects).WillBuild); // succeeded→clean geçişi HÂLÂ ayakta — ezilmedi
    }

    // ---------------------------------------------------------------- 10) [Task 17] ETA text (Core.Incremental.EtaCalculator)

    [Fact] // run başlar başlamaz (hiç completion yok) — ETA NUMARASI YOK, yalnız X/N · elapsed (EtaCalculator'ın "ilk koşu" fallback'i)
    public async Task EtaText_shows_XofN_fallback_before_any_completion_no_bogus_number()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", ElapsedMsAtStart: 0));

        Assert.Equal("0/3 · 0s", vm.EtaText);
    }

    [Fact] // bir proje 10s'de tamamlandı, 2 proje daha kuyrukta (henüz başlamadı) — kalan tahmini bu run'ın
           // GÖZLEMLENEN süresinden (EtaCalculator'ın BuildState.LastDurationMs YERİNE bu run'ın ortalaması) üretilir
    public async Task EtaText_reflects_the_calculator_estimate_after_a_completion_using_observed_durations()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        // A 10s sürdü; B/C henüz hiç başlamadı (queued=2) → ortalama=10s, (10s+10s)/1 parallelism = 20s, +0 (building yok)
        // → ilk tick smoothing yok, raw AYNEN → 20000ms → yuvarlanmış "~20s left"
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", DurationMs: 10_000));

        Assert.Equal("~20s left", vm.EtaText);
    }

    [Fact] // ham tahmin AlmostDoneThresholdMs (4000ms) altına düşünce numerik değer YOK, "· almost done"
    public async Task EtaText_shows_almost_done_when_the_raw_estimate_is_small()
    {
        long fakeNow = 0;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 2, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));

        fakeNow = 1000; // A 1000ms sürdü, B de aynı anda (t=0) başlamıştı → B şu an 1000ms building
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", DurationMs: 1000));
        // completed=1 (A, avg=1000ms), building=[B: elapsed=1000, est=1000] → remaining=max(0,1000-1000)=0
        // queued=max(0, 2-1-1)=0 → raw=(0+0)/1 + 400(building var) = 400ms < 4000ms eşiği

        Assert.Equal("· almost done", vm.EtaText);
    }

    // ---------------------------------------------------------------- [A5/T69] sync / branch / topoloji

    private static ProjectNode Node(string id, string name, int buildOrder, bool? willBuild = null,
        IReadOnlyList<string>? deps = null, string? layerName = null) =>
        new(id, name, id, ["Osys"], deps ?? [], buildOrder, layerName is null ? null : 0, layerName, false, willBuild);

    [Fact]
    public async Task Sync_events_update_phase_target_sha_and_topology()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.Equal(AppPhase.Empty, vm.Phase);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0, willBuild: true), Node(@"C:\p\b.csproj", "B", 1, willBuild: false)],
            Cycles: [],
            Solutions: [new SolutionRef("Osys", @"C:\p\Osys.sln")],
            LayerWarnings: []));
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4c0affee", FetchDegraded: false, 2, 0,
            ChangedCount: 1, ToBuildCount: 1, UpToDateCount: 1));

        Assert.Equal(AppPhase.Idle, vm.Phase);          // syncing → idle
        Assert.Equal("b7e91d4c0affee", vm.TargetSha);
        Assert.False(vm.FetchDegraded);
        Assert.Equal(["A", "B"], vm.Topology.Select(n => n.Name));
        Assert.Equal("Osys", Assert.Single(vm.Solutions).Name);
    }

    [Fact]
    public async Task Sync_completed_records_a_degraded_fetch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", FetchDegraded: true, 1, 0));

        Assert.True(vm.FetchDegraded);
        Assert.Equal(AppPhase.Idle, vm.Phase);
    }

    // [A5/T69] Topoloji, proje listesini BUILD-ORDER'da yeniden kurar; D1 (katman gruplaması) ve D5 (graf)
    // TopologyChanged'i dinler. [A13.2] Koleksiyon RESET'İ YASAK — liste yerinde uzlaştırılır (Add/Remove/Move).
    [Fact]
    public async Task Workspace_topology_rebuilds_the_project_list_in_build_order_and_raises_TopologyChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        // Önceki bir run'ın kalıntısı: sıra TERS ve topolojide olmayan bir proje var
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\gone.csproj", "Gone"));

        var resets = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)vm.Projects).CollectionChanged += (_, e) => resets.Add(e.Action);
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])],
            Cycles: [], Solutions: [], LayerWarnings: []));

        Assert.Equal([@"C:\p\a.csproj", @"C:\p\b.csproj"], vm.Projects.Select(p => p.Id)); // build-order
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, resets);                // [A13.2]
        Assert.Equal(1, topologyChanged);
        Assert.All(vm.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));      // Sync = yeni taban
    }

    // [E2/§5-b verify-then-fix] AYNI graf yapısının yeniden yayınlanması (mid-run Sync gibi) TopologyChanged'i
    // YENİDEN ateşlememeli (SetGraph = tam inşa + reveal stagger + kamera re-home; koşan grafı bozar). YALNIZ
    // düğüm/kenar YAPISI değişince ateşlenir; statü (InCycle/WillBuild) değişimi ateşlemez (o UpdateStatuses'tan akar).
    [Fact]
    public async Task Identical_workspace_topology_does_not_re_raise_TopologyChanged_but_a_structural_change_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        WorkspaceTopologyEvent Topo(params ProjectNode[] nodes) =>
            new(Nodes: nodes, Cycles: [], Solutions: [], LayerWarnings: []);

        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])));
        Assert.Equal(1, topologyChanged); // ilk topoloji: graf ilk kez kurulur

        // AYNI yapı yeniden gelir (yeni ProjectNode örnekleri, aynı Id/Ad/kenar) — re-reveal YOK.
        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])));
        Assert.Equal(1, topologyChanged);

        // Yapı GERÇEKTEN değişir (yeni düğüm eklenir) — yeniden kurulmalı.
        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"]),
            Node(@"C:\p\c.csproj", "C", 2)));
        Assert.Equal(2, topologyChanged);
    }

    // [E2/FIX2] §5b imzası grafın GEOMETRİ sürücüsünü (LayerIndex) İÇERMELİDİR. LayerName, LayerIndex'in vekili
    // DEĞİLDİR: kullanıcı D7 katman pattern'lerini düzenleyip (ör. ortaya boş katman ekleyip / Other kovasını
    // iten eşleşmeyen bir pattern ekleyip) bir grup düğümün LayerIndex'ini kaydırdığında Id/Ad/LayerName/
    // Dependencies VE OrderBy(LayerIndex) yayın sırası BYTE-AYNI kalabilir. LayerIndex imzada yoksa
    // TopologyChanged ateşlenmez → SetGraph koşmaz → düğümler bayat satırlarda (bir tam satır kayması +
    // eksik/fazla boş katman bandı) kalır. SADECE LayerIndex değişince de yeniden kurulmalı.
    [Fact]
    public async Task A_layer_index_only_shift_re_raises_TopologyChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        // Aynı Id/Ad/LayerName/Dependencies + AYNI yayın sırası; SADECE LayerIndex farklı.
        ProjectNode NodeAt(int layerIndex) => new(
            @"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys"], Dependencies: [], BuildOrder: 0,
            LayerIndex: layerIndex, LayerName: "Edge", InCycle: false, WillBuild: null);
        WorkspaceTopologyEvent Topo(ProjectNode n) => new([n], [], [], []);

        vm.OnEvent(Topo(NodeAt(0)));
        Assert.Equal(1, topologyChanged); // ilk topoloji: graf ilk kez kurulur

        vm.OnEvent(Topo(NodeAt(0))); // hiçbir şey değişmedi — ateşlenmez
        Assert.Equal(1, topologyChanged);

        vm.OnEvent(Topo(NodeAt(1))); // SADECE LayerIndex kaydı → graf satırı yer değiştirir → yeniden kurulmalı
        Assert.Equal(2, topologyChanged);
    }

    // Topolojinin hemen ardından gelen buildPreview, satırların will-dot'unu kurar (mevcut handler — İKİNCİ
    // bir will-build yolu açılmaz). Sync sonrası hiçbir düğüm hollow KALMAZ.
    [Fact]
    public async Task Build_preview_after_topology_fills_the_will_build_dots()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)],
            Cycles: [], Solutions: [], LayerWarnings: []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),
        ]));

        Assert.True(vm.Projects[0].WillBuild);
        Assert.False(vm.Projects[1].WillBuild);
    }

    // Koşan bir run'ın CANLI satır durumu, araya giren bir Sync tarafından SİLİNMEZ (mid-run Sync koruması).
    [Fact]
    public async Task Workspace_topology_does_not_reset_row_state_while_a_run_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400));

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0)], Cycles: [], Solutions: [], LayerWarnings: []));

        Assert.Equal(ProjectRowState.Succeeded, Assert.Single(vm.Projects).State);
    }

    // Başarısız bir Sync (ör. kök bir git repo'su değil) syncCompleted YAYINLAMAZ — yalnız planFailed gelir.
    // Faz Syncing'de ASILI KALMAMALIDIR, aksi halde şerit sonsuza dek "▸ Sync — git fetch origin…" gösterirdi.
    [Fact]
    public async Task A_failed_sync_releases_the_syncing_phase_instead_of_hanging_there()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);

        vm.OnEvent(new ErrorEvent("planFailed", "'D:\\repo' is not a usable git repository: ..."));

        Assert.Equal(AppPhase.Boot, vm.Phase); // topoloji hiç gelmedi → "repo var, Sync yapılmadı"
    }

    [Fact]
    public async Task A_failed_resync_falls_back_to_idle_when_a_topology_is_already_known()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", false, 1, 0));

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new ErrorEvent("planFailed", "boom"));

        Assert.Equal(AppPhase.Idle, vm.Phase); // önceki topoloji hâlâ geçerli
    }

    // [Fix wave 1 — Finding 2] Sync SALT-OKURDUR ve KOŞAN bir run sırasında da tetiklenebilir (bkz.
    // OnWorkspaceTopology'nin mid-run koruması). A5, `planFailed`'ı Sync'in de hata kodu yaptı; başarısız bir
    // Sync CANLI run state'ini yıkarsa motor derlemeye devam ederken UI run'ı bitmiş gösterir (Stop erişilemez
    // olur) ve sonraki projectSucceeded/runCompleted YIKILMIŞ bir state'e düşer.
    [Fact]
    public async Task A_failed_sync_during_a_live_run_releases_the_phase_without_tearing_down_the_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        // [topoloji kapısı] Koşan bir run ancak bir Sync'ten SONRA var olabilir — senaryo bu yüzden topolojiyle kurulur.
        VmTopology.Seed(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        Assert.True(vm.IsRunning);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new ErrorEvent("planFailed", "'D:\\repo' is not a usable git repository: ..."));

        Assert.True(vm.IsRunning);                      // run YAŞIYOR — motor hâlâ derliyor
        Assert.True(vm.StopCommand.CanExecute(null));   // Stop erişilebilir kaldı [Kısıt 3]
        Assert.Equal(AppPhase.Idle, vm.Phase);          // faz Syncing'de ASILI kalmadı (elde topoloji var → Idle)

        // Motor derlemeye devam etti: sonraki event'ler hâlâ AYAKTA bir state'e düşer
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        Assert.Equal(ProjectRowState.Succeeded, Assert.Single(vm.Projects).State);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1500));
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // Ayrım ÇİFT YÖNLÜ olmalı: Sync in-flight iken gelen bir planFailed, run PLANLAMA penceresindeyse
    // (IsStarting — `planFailed`'ın run tarafındaki TEK üretim penceresi) yine run'ı bitirir; aksi halde
    // hiçbir engine event'i gelmeyeceğinden IsStarting kalıcı true kalır ve butonlar sonsuza dek kilitlenir.
    [Fact]
    public async Task A_plan_failure_while_a_run_is_still_starting_still_ends_the_run_even_if_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.IsStarting = true; // Rebuild gönderildi, runStarted HENÜZ gelmedi = planlama penceresi
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        vm.OnEvent(new ErrorEvent("planFailed", "planlama patladı"));

        Assert.False(vm.IsStarting);
        // [Fix wave 1, C2 review Finding 1] _syncInFlight BİLEREK true kalır (çakışan pencere — yukarıdaki
        // TryConsumeSyncFailure yorumu), yani VM'e göre bir Sync HÂLÂ uçuşta olabilir; RebuildCommand artık
        // buna da bakıyor (CanRebuildOrRetry) — mid-Sync clearBuffers'ın canlı transkripti bozma riskiyle
        // TUTARLI biçimde burada da engelli kalır.
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.Equal(AppPhase.Boot, vm.Phase); // faz yine de bırakılır
    }

    // Ayrım KODA da bakar: `runFailed` Sync'in ASLA yayınlamadığı (yalnız RunCoordinator'ın dış catch'inden
    // gelen, run ORTASINDA da gelebilen) bir koddur — uçuşta bir Sync olması onu Sync'e atfettirmemelidir,
    // yoksa gerçek bir run çöküşünde IsRunning kalıcı true kalır (buton kaması).
    [Fact]
    public async Task A_mid_run_runFailed_still_ends_the_run_even_while_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        vm.OnEvent(new ErrorEvent("runFailed", "beklenmeyen hata"));

        Assert.False(vm.IsRunning);
        // [Fix wave 1, C2 review Finding 1] Sync HÂLÂ GERÇEKTEN uçuşta (Phase == Syncing, aşağıda doğrulanır) —
        // RebuildCommand artık _syncInFlight'a da baktığından (CanRebuildOrRetry) burada BİLEREK engelli kalır:
        // run bitmiş olsa da canlı Sync transkripti hâlâ SyncProgressEvent ile büyüyor olabilir.
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.Equal(AppPhase.Syncing, vm.Phase); // Sync HÂLÂ uçuşta — fazı bu hata bırakmaz
    }

    [Fact]
    public async Task Branch_list_event_fills_branches()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "abc1234", true, false),
            new BranchRef("origin/main", "abc1234", false, true),
        ]));

        Assert.Equal(["main", "origin/main"], vm.Branches.Select(b => b.Name));
        Assert.True(vm.Branches[0].IsActive);

        // İkinci liste ÖNCEKİNİ değiştirir, üstüne eklemez
        vm.OnEvent(new BranchListEvent([new BranchRef("feature-x", "def5678", false, false)]));
        Assert.Equal(["feature-x"], vm.Branches.Select(b => b.Name));
    }

    // [D8] Gerçek 50ms beklenmez — tick tamamen kontrol edilir (ConsoleBatcherTests deseni).
    [Fact]
    public async Task Sync_progress_lines_reach_the_console_batcher()
    {
        int ticks = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            ticks++;
            if (ticks == 2) batcher!.Complete(); // 1. tick birikenleri flush eder, 2. tick pump'ı kapatır
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, batcher, () => "r1")
        {
            // [D4/T56-UI] Anlatı satırı "HH:MM:SS " damgasıyla bileşilir (design-v1 §2.5); saat deterministik enjekte.
            WallClock = () => new DateTimeOffset(2026, 7, 23, 12, 4, 7, TimeSpan.Zero),
        };

        vm.OnEvent(new SyncProgressEvent("git fetch origin main", "cmd"));
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));

        var flushes = new List<string>();
        await batcher.PumpAsync((text, _) => flushes.Add(text), CancellationToken.None);

        // Batcher'a düşen satırlar artık "HH:MM:SS " önekli (ham ▸ satırı ONUN İÇİNDE — colorizer damga+▸'yi çözer).
        Assert.Equal(
            ["git fetch origin main\nSync complete — 7 changed projects, 14 to build\n"],
            flushes);
        // Konsol dokümanına da düşer (run dokümanı aktifken)
        Assert.Contains("git fetch origin main", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- [spec 2026-09-18 §6.2] Sync kipleri
    //
    // Sessiz Sync testleri BAŞLATILMIŞ bir motor kullanır: gönderim BAŞARILI olmalıdır — düşen bir gönderim
    // Sync'in kipini bırakır (bkz. A_silent_sync_whose_send_fails_leaves_no_mode_or_start_time). VM motorun
    // EventReceived'ına bağlanmaz; motorun cevabı vm.OnEvent(...) ile verilir (Stop testlerinin deseni). D8:
    // sleep/poll yok.

    private const string A = @"C:\p\a.csproj";
    private const string B = @"C:\p\b.csproj";

    private static async Task<EngineHost> StartedEngineAsync(SupervisorSandbox sandbox)
    {
        var engine = sandbox.IsolatedEngineHost(WideStartupTimeout); // [§5.5] izole önbellek — bkz. SupervisorSandbox
        await engine.StartAsync();
        return engine;
    }

    /// <summary>İki satırlı, Sync'lenmiş bir workspace: A güncel, B derlenecek; akışta ilk Sync'in satırı, konsolda
    /// önceki bir işlemin satırı durur.</summary>
    private static RunViewModel SyncedTwoRowVm(EngineHost engine)
    {
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        ReplySync(vm, upToDateB: false);
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        return vm;
    }

    /// <summary>Motorun bir Sync'e verdiği cevap: başlangıç, transkript, topoloji, önizleme, tamamlanma.</summary>
    private static void ReplySync(RunViewModel vm, bool upToDateB, params SyncProgressEvent[] transcript)
    {
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        foreach (var line in transcript) vm.OnEvent(line);
        vm.OnEvent(new WorkspaceTopologyEvent([Node(A, "A", 0), Node(B, "B", 1)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(A, "A", false, Reason: WillBuildReason.UpToDate),
            new BuildPreviewItem(B, "B", !upToDateB, Reason: upToDateB ? WillBuildReason.UpToDate : WillBuildReason.SignatureChanged,
                OwnFilesChanged: !upToDateB),
        ]));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0, ToBuildCount: upToDateB ? 0 : 1,
            UpToDateCount: upToDateB ? 2 : 1, HeadSha: "1111111111111111111111111111111111111111", ActiveBranch: "main"));
    }

    /// <summary>Commit'in sessiz Sync'i konsolu ve olay akışını KORUR, fetch yapmaz, kalıcı işlem pill'i yazmaz,
    /// seçimi düşürmez; bitince akışa tek satır düşer: <c>synced after commit</c> — hiçbir şey değişmemiş olsa da.</summary>
    [Fact]
    public async Task A_silent_sync_keeps_the_console_and_stream_and_adds_one_line()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        vm.SelectProject(A);
        int streamBefore = vm.StreamEvents.Count;
        Assert.True(streamBefore > 0, "ön-koşul: akışta önceki Sync'in satırı yok — vakum");
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Commit));

        var cmd = Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.False(cmd.Fetch);
        Assert.Null(vm.CurrentOperation);
        Assert.Equal(A, vm.SelectedProjectId);
        Assert.Contains("previous operation line", vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.Equal(streamBefore, vm.StreamEvents.Count);

        ReplySync(vm, upToDateB: false);

        Assert.Equal(A, vm.SelectedProjectId);
        Assert.Equal(streamBefore + 1, vm.StreamEvents.Count);
        Assert.Equal(StreamText.SyncedAfterCommit, vm.StreamEvents[^1].Text);
        Assert.Contains("previous operation line", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>Sessiz Sync'in transkripti (dim/info/cmd) konsola yazılmaz; uyarı ve hata satırları yazılır —
    /// sessizlik kötü haberi gizlemez.</summary>
    [Fact]
    public async Task A_silent_sync_shows_warnings_but_not_the_transcript()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        await vm.SyncSilentlyAsync(SilentSyncReason.Commit);

        ReplySync(vm, upToDateB: false,
            new SyncProgressEvent("HEAD 1111111 · up to date with origin/main", "info"),
            new SyncProgressEvent("Scanning 1 solutions", "dim"),
            new SyncProgressEvent("warning: 2 projects produce a.dll", "warn"),
            new SyncProgressEvent("error: something broke", "error"));

        string console = vm.GetRunDocumentText();
        Assert.DoesNotContain("HEAD 1111111", console, StringComparison.Ordinal);
        Assert.DoesNotContain("Scanning 1 solutions", console, StringComparison.Ordinal);
        Assert.Contains("warning: 2 projects produce a.dll", console, StringComparison.Ordinal);
        Assert.Contains("error: something broke", console, StringComparison.Ordinal);
    }

    /// <summary>Pencereye dönüşün sessiz Sync'i hiçbir kararı değiştirmediyse akışa HİÇBİR şey yazmaz.</summary>
    [Fact]
    public async Task A_silent_sync_with_no_changes_writes_nothing_on_activation()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        int streamBefore = vm.StreamEvents.Count;

        await vm.SyncSilentlyAsync(SilentSyncReason.Refresh);
        ReplySync(vm, upToDateB: false);

        Assert.Equal(streamBefore, vm.StreamEvents.Count);
    }

    /// <summary>Pencereye dönüşün sessiz Sync'i kararı değişen satırları sayar: B derlenecekken güncel oldu.</summary>
    [Fact]
    public async Task A_silent_sync_that_changes_a_decision_names_how_many_projects_changed()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        int streamBefore = vm.StreamEvents.Count;

        await vm.SyncSilentlyAsync(SilentSyncReason.Refresh);
        ReplySync(vm, upToDateB: true);

        Assert.Equal(streamBefore + 1, vm.StreamEvents.Count);
        Assert.Equal(StreamText.SyncedProjectsChanged(1), vm.StreamEvents[^1].Text);
    }

    /// <summary>[Task 1] Sayaç <see cref="RunViewModel.DecisionKeys"/> üzerinden <c>row.Standing</c> + karar
    /// etiketini okur; koşu bitmiş (Succeeded) bir satırın kararı sessiz Sync'le değiştiyse sayaç bunu da
    /// görmeli — terminal satır guard'ın dışında kalmamalı.</summary>
    [Fact]
    public async Task A_silent_sync_counts_a_refreshed_terminal_row_as_changed()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        const string projectId = A;

        // Build koşusu bitti: proje Succeeded + temiz.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new WorkspaceTopologyEvent([Node(projectId, "A", 0)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "A", true, Reason: WillBuildReason.NeverBuilt)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        int streamBefore = vm.StreamEvents.Count;

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([Node(projectId, "A", 0)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(projectId, "A", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true)]));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0, ToBuildCount: 1, UpToDateCount: 0,
            HeadSha: "1111111111111111111111111111111111111111", ActiveBranch: "main"));

        Assert.Equal(streamBefore + 1, vm.StreamEvents.Count);
        Assert.Equal(StreamText.SyncedProjectsChanged(1), vm.StreamEvents[^1].Text);
        Assert.True(Assert.Single(vm.Projects).WillBuild);
    }

    /// <summary>Sessiz Sync kapı kapalıyken (başka bir Sync uçuşta) hiçbir şey göndermez.</summary>
    [Fact]
    public async Task A_silent_sync_does_not_start_while_another_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = SyncedTwoRowVm(engine);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        Assert.False(await vm.SyncSilentlyAsync(SilentSyncReason.Commit));
        Assert.Empty(sent);
    }

    /// <summary>[Faz 2/T7 · spec §6.2] Dışarıdan gelen branch değişimi yeni bölüm açar: konsol temizlenir, ilk satır
    /// koordinatörün verdiği switch satırıdır, fetch yapılmaz (checkout'un cevabıyla aynı yol).</summary>
    [Fact]
    public async Task An_external_branch_switch_opens_a_section_led_by_the_switched_line()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        string line = Core.Planning.PlanProgressLines.SwitchedBranch("main", "feature", "2222222");

        Assert.True(await vm.SyncAfterExternalBranchChangeAsync(line));

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        string console = vm.GetRunDocumentText();
        Assert.StartsWith(line, console, StringComparison.Ordinal);
        Assert.DoesNotContain("previous operation line", console, StringComparison.Ordinal);
    }

    /// <summary>[Faz 2/T7] Sync uçuştayken gelen commit tetiği bekler; Sync bitince VM'in meşguliyet bildirimi onu UI
    /// kuyruğuna atar ve BİR sessiz Sync koşar. [review M6] HEAD ve izleyici sahtedir — makineden bağımsız.</summary>
    [Fact]
    public async Task A_head_trigger_during_a_sync_runs_after_the_sync_ends()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        var posted = new Queue<Action>();
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("main", CommittedSha), () => new FakeHeadWatcher());
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Commit);
        Assert.Empty(sent);
        Assert.Empty(posted);

        ReplySync(vm, upToDateB: false);
        Drain(posted);

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        vm.DisableAutoSync();
    }

    /// <summary>[review I1 · spec §6.1] Pull + izleyici tek Sync'tir: pull uçuştayken (motor komut döngüsünü
    /// bloklar, başlangıç olayı yok) gelen reflog tetiği bekler; pull'un zincirli Sync'i yeni HEAD'i ölçer ve bekleyen
    /// tetik HEAD eşit olduğu için atlanır.</summary>
    [Fact]
    public async Task A_pull_plus_the_watcher_is_one_sync()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        var posted = new Queue<Action>();
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("main", CommittedSha), () => new FakeHeadWatcher());
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.PullRepositoryCommand.ExecuteAsync(null);
        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Other); // pull'un reflog satırı
        Drain(posted);
        vm.OnEvent(new PullCompletedEvent(Succeeded: true));
        Drain(posted);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0, HeadSha: CommittedSha, ActiveBranch: "main"));
        Drain(posted);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        vm.DisableAutoSync();
    }

    /// <summary>[review I2] Kök değişince eski kökün son Sync HEAD'i ve anları unutulur — yoksa yeni kökteki ilk
    /// tetik eski branch'le kıyaslanıp sahte bir "Switched to" bölümü açardı.</summary>
    [Fact]
    public async Task A_root_change_forgets_the_last_sync_head()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = SyncedTwoRowVm(engine);
        Assert.NotNull(vm.LastSyncHead); // ön-koşul

        await vm.ChangeRepositoryAsync(@"D:\other-repo");

        Assert.Null(vm.LastSyncHead);
        Assert.Null(vm.LastSyncCompletedAtMs);
        Assert.Null(vm.LastSyncStartedAtMs); // motor başlamadı: kök değişiminin Sync'i gönderilemedi
    }

    // ---------------------------------------------------------------- [T8 · spec §6.1 · §6.2 · karar 10] koşu sırasında branch

    private const string RunLogDirectory = @"D:\logs\2026-09-19_10-00-00";

    /// <summary>Sync'lenmiş iki satırlı workspace'te B'yi derleyen bir koşu uçuşta; HEAD sahte ve <paramref name="head"/>
    /// değişkeninden okunur, UI kuyruğu <paramref name="posted"/>'tır.</summary>
    private static RunViewModel MidRunVm(EngineHost engine, Queue<Action> posted, Func<Core.Git.HeadState> head)
    {
        var vm = SyncedTwoRowVm(engine);
        vm.EnableAutoSync(posted.Enqueue, _ => head(), () => new FakeHeadWatcher());
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0, LogDirectory: RunLogDirectory));
        vm.OnEvent(new BuildPreviewEvent([ // motor runStarted'ın hemen ardından önizlemeyi yayınlar: kapsam = B
            new BuildPreviewItem(A, "A", false, Reason: WillBuildReason.UpToDate),
            new BuildPreviewItem(B, "B", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true),
        ]));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        Drain(posted);
        return vm;
    }

    /// <summary>Koşu sırasında dışarıdan branch değişimi koşuyu BİR kez keser (<c>StopKind.Interrupt</c>) ve akışa
    /// "interrupted by branch change" düşer; Sync koşu bitene dek bekler.</summary>
    [Fact]
    public async Task A_branch_switch_mid_run_interrupts_once()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("feature", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);
        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);

        var stop = Assert.Single(sent.OfType<StopRunCommand>());
        Assert.Equal(new StopRunCommand("r1", StopKind.Interrupt), stop);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
        Assert.Single(vm.StreamEvents, e => e.Text == StreamText.InterruptedByBranchChange);
        vm.DisableAutoSync();
    }

    /// <summary>[final review M4] Kullanıcının Stop'u onaylandı (<c>runStopped</c> geldi, <c>runCompleted</c> bekleniyor):
    /// o arada gelen branch değişimi kesme GÖNDERMEZ, akışa "interrupted by branch change" düşmez ve koşunun özeti
    /// yazılmaz — koşu kullanıcının isteğiyle durdu, branch değişimiyle değil. Tetik bekler ve koşu bitince bölümü
    /// (özetsiz) açar.</summary>
    [Fact]
    public async Task A_branch_switch_after_the_users_stop_was_acknowledged_does_not_interrupt()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("feature", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;
        await vm.StopCommand.ExecuteAsync(null);
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Drain(posted);

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);

        Assert.DoesNotContain(sent.OfType<StopRunCommand>(), s => s.Kind == StopKind.Interrupt);
        Assert.DoesNotContain(vm.StreamEvents, e => e.Text == StreamText.InterruptedByBranchChange);
        Assert.NotNull(vm.AutoSync!.PendingTrigger);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 0, 500));
        Drain(posted);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        string text = vm.GetRunDocumentText().ReplaceLineEndings("\n");
        Assert.DoesNotContain(Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(0, 1, RunLogDirectory), text, StringComparison.Ordinal);
        Assert.StartsWith(Core.Planning.PlanProgressLines.SwitchedBranch("main", "feature", "2222222"), text, StringComparison.Ordinal);
        vm.DisableAutoSync();
    }

    /// <summary>Koşu sırasında commit koşuyu kesmez ve hiçbir şey yapmaz.</summary>
    [Fact]
    public async Task A_commit_mid_run_does_nothing()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("main", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Commit);
        Drain(posted);

        Assert.Empty(sent);
        Assert.DoesNotContain(vm.StreamEvents, e => e.Text == StreamText.InterruptedByBranchChange);
        vm.DisableAutoSync();
    }

    /// <summary>Kesilen koşu bitince yeni bölüm açılır: konsolun ilk satırı koşunun özeti (kaç proje bitti, kaçı
    /// derlenmedi, log klasörü), ardından switch satırı; Sync fetch etmez (spec §6.2). Kesmeden sonra biten B
    /// (Trusted=false) "built" sayılmaz.</summary>
    [Fact]
    public async Task After_the_interrupted_run_a_new_section_starts_with_its_summary()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("feature", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);
        vm.OnEvent(new ProjectSucceededEvent("r1", B, 300, Trusted: false));
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 0, 500));
        Drain(posted);

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        string summary = Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(0, 1, RunLogDirectory);
        string switched = Core.Planning.PlanProgressLines.SwitchedBranch("main", "feature", "2222222");
        Assert.StartsWith(summary + "\n" + switched, vm.GetRunDocumentText().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        vm.DisableAutoSync();
    }

    /// <summary>Motor her Sync isteğine hemen başlangıçla cevap verir (<c>syncStarted</c>) — gönderilen her yeni
    /// <see cref="SyncWorkspaceCommand"/> için bir kez.</summary>
    private static void AnswerNewSyncs(RunViewModel vm, IEnumerable<IpcCommand> sent, ref int answered)
    {
        int syncs = sent.OfType<SyncWorkspaceCommand>().Count();
        for (; answered < syncs; answered++) vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
    }

    /// <summary>[T8 fix round 1 · I1] <c>runStopped</c> ile <c>runCompleted</c> ayrı flush'larla, ayrı UI
    /// kuyruğu işleriyle gelir. Koşu <c>runCompleted</c>'a dek uçuşta sayılır: arada kuyruğa düşen değerlendirme
    /// Sync başlatmaz; yeni bölüm <c>runCompleted</c>'tan SONRA açılır — bölümde eski koşunun "Stopped" satırı yok,
    /// faz Sync'e aittir.</summary>
    [Fact]
    public async Task The_new_section_opens_only_after_the_interrupted_run_completed()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("feature", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;
        int answered = 0;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);
        vm.OnEvent(new ProjectSucceededEvent("r1", B, 300, Trusted: false));
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Drain(posted);
        AnswerNewSyncs(vm, sent, ref answered);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 0, 500));
        Drain(posted);
        AnswerNewSyncs(vm, sent, ref answered);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Equal(AppPhase.Syncing, vm.Phase);
        Assert.DoesNotContain(vm.StreamEvents, e => e.Text == StreamText.Stopped(0));
        vm.DisableAutoSync();
    }

    /// <summary>[T8 fix round 1 · I1] Host'un geç <c>runStopped</c> onayı (kesme, koşu kapanırken gitti) ya da başka
    /// bir koşunun olayı, bitmiş koşunun ardından başlamış Sync'in fazına ve akışına dokunmaz.</summary>
    [Fact]
    public async Task A_late_run_end_event_does_not_touch_the_next_operation()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("feature", CommittedSha));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase); // ön-koşul
        int streamBefore = vm.StreamEvents.Count;

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunStoppedEvent("r0", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r0", RunOutcome.Stopped, 0, 0, 0, 3, 500));

        Assert.Equal(AppPhase.Syncing, vm.Phase);
        Assert.Equal(streamBefore, vm.StreamEvents.Count);
        vm.DisableAutoSync();
    }

    /// <summary>[T8 fix round 1 · M3] Özetin kapsamı koşullu projeleri de sayar: A kesin, C koşullu derlenecekti;
    /// A kesmeden önce güvenilir bitti, C hiç başlamadı → "1 built, 1 not built".</summary>
    [Fact]
    public async Task The_summary_counts_conditional_projects_that_were_not_built()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = SyncedTwoRowVm(engine);
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("feature", CommittedSha), () => new FakeHeadWatcher());
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(A, "A", true, Reason: WillBuildReason.SignatureChanged, OwnFilesChanged: true),
            new BuildPreviewItem(B, "B", true, Reason: WillBuildReason.WaitingForDependency, Conditional: true),
        ]));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 300));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 1, 500));
        Drain(posted);

        Assert.StartsWith(Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(1, 1, null),
            vm.GetRunDocumentText(), StringComparison.Ordinal);
        vm.DisableAutoSync();
    }

    /// <summary>[T8 fix round 1 · M5] Açılış koreografisi oynarken branch değişirse Build isteği geri alınır ve yeni
    /// bölümün ilk satırı bunu açıklar: "0 built, N not built" (log klasörü yok — koşu başlamadı).</summary>
    [Fact]
    public async Task An_interrupt_during_the_opening_choreography_explains_the_build_click()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = SyncedTwoRowVm(engine);
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("feature", CommittedSha), () => new FakeHeadWatcher());
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        var build = vm.BuildCommand.ExecuteAsync(null);
        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.BranchSwitch);
        choreography.SetResult();
        await build;
        Drain(posted);

        Assert.Empty(sent.OfType<StartRunCommand>());
        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        string summary = Core.Planning.PlanProgressLines.RunInterruptedByBranchChange(0, 1, null);
        string switched = Core.Planning.PlanProgressLines.SwitchedBranch("main", "feature", "2222222");
        Assert.StartsWith(summary + "\n" + switched, vm.GetRunDocumentText().ReplaceLineEndings("\n"), StringComparison.Ordinal);
        vm.DisableAutoSync();
    }

    /// <summary>Güvenlik ağı: izleyici HEAD değişimini kaçırdıysa koşu bitince yakalanır — aynı branch'te yeni commit
    /// sessiz Sync'tir; kesme yoktu, özet yok.</summary>
    [Fact]
    public async Task A_head_change_missed_by_the_watcher_is_caught_when_the_run_ends()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("main", CommittedSha));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        vm.OnEvent(new ProjectSucceededEvent("r1", B, 300));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Drain(posted);

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        Assert.Empty(sent.OfType<StopRunCommand>());
        vm.DisableAutoSync();
    }

    /// <summary>Güvenlik ağı her koşudan sonra Sync üretmez: HEAD son Sync'tekiyle aynıysa hiçbir şey gönderilmez.</summary>
    [Fact]
    public async Task A_run_that_ends_on_the_synced_head_sends_no_sync()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = MidRunVm(engine, posted, () => new Core.Git.HeadState("main", "1111111111111111111111111111111111111111"));
        var sent = new System.Collections.Concurrent.ConcurrentQueue<IpcCommand>(); // gönderim devamları paralel koşabilir
        vm.DebugOnCommandSent = sent.Enqueue;

        vm.OnEvent(new ProjectSucceededEvent("r1", B, 300));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Drain(posted);

        Assert.Empty(sent);
        vm.DisableAutoSync();
    }

    /// <summary>Pull/commit sonrası HEAD'in sahte sha'sı — <see cref="ReplySync"/>'in ölçtüğünden farklı.</summary>
    private const string CommittedSha = "2222222222222222222222222222222222222222";

    /// <summary>UI kuyruğuna atılmış işleri sırayla koşar (sahte Dispatcher).</summary>
    private static void Drain(Queue<Action> posted)
    {
        while (posted.TryDequeue(out var action)) action();
    }

    /// <summary>[review I1] Sessiz Sync şeritte de görünmez: faz <c>Syncing</c>'e geçmez (şerit "▸ Sync — git
    /// fetch…" demez), önceki işlemin pill'i canlanmaz; bitince faz olduğu yerde kalır.</summary>
    [Fact]
    public async Task A_silent_sync_keeps_the_phase_and_the_pill_is_not_live()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 1, 0, 500));
        Assert.Equal(AppPhase.Done, vm.Phase); // ön-koşul
        string? opBefore = vm.CurrentOperation;
        Assert.NotNull(opBefore);
        string ribbonBefore = vm.RibbonLine.Text;

        await vm.SyncSilentlyAsync(SilentSyncReason.Commit);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.Equal(AppPhase.Done, vm.Phase); // pill canlı değil: faz Syncing değil, koşu kilidi yok
        Assert.False(vm.IsMidRunLocked);
        Assert.Equal(opBefore, vm.CurrentOperation);
        Assert.Equal(ribbonBefore, vm.RibbonLine.Text);

        ReplySync(vm, upToDateB: false);
        Assert.Equal(AppPhase.Done, vm.Phase);
    }

    /// <summary>[review I2] Sessiz Sync kötü haberi silmez: düşen bir koşunun şeritteki "Run failed — …" metni
    /// Sync boyunca ve sonrasında durur.</summary>
    [Fact]
    public async Task A_silent_sync_keeps_a_failed_runs_error_on_the_ribbon()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0));
        vm.OnEvent(new ErrorEvent("runFailed", "msbuild crashed"));
        Assert.Equal("msbuild crashed", vm.RunErrorMessage); // ön-koşul
        string ribbonBefore = vm.RibbonLine.Text;

        await vm.SyncSilentlyAsync(SilentSyncReason.Commit);
        ReplySync(vm, upToDateB: false);

        Assert.Equal("msbuild crashed", vm.RunErrorMessage);
        Assert.Equal(ribbonBefore, vm.RibbonLine.Text);
    }

    /// <summary>[review I1] Branch değişiminin Sync'i fetch etmez — şerit de fetch iddia etmez.</summary>
    [Fact]
    public async Task A_branch_change_sync_does_not_claim_a_fetch_on_the_ribbon()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x",
            "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1", 0, null, null));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "feature/x"));

        Assert.Equal(AppPhase.Syncing, vm.Phase);
        Assert.DoesNotContain("git fetch", vm.RibbonLine.Text, StringComparison.Ordinal);
        Assert.Equal(RibbonText.SyncingWithoutFetch, vm.RibbonLine.Text);
    }

    /// <summary>[review M1] Gönderimi düşen bir Sync kipini bırakır (sonraki transkript satırı gizlenmez) ve
    /// başlangıç zamanı YAZMAZ — hiçbir Sync başlamadı; başarılı gönderim yazar.
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden düşen gönderimde de <c>true</c> dönerdi (yalnız kapı
    /// soruluyordu) ve koordinatör tetiği kaybederdi. Artık dönüş "motora gitti mi"dir: düşen gönderim <c>false</c>
    /// verir ve tetik bekler.</para></summary>
    [Fact]
    public async Task A_silent_sync_whose_send_fails_leaves_no_mode_or_start_time()
    {
        long now = 7000;
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1", () => now)
        {
            RootPath = @"D:\repo",
        };

        Assert.False(await vm.SyncSilentlyAsync(SilentSyncReason.Commit)); // gönderim senkron düşer

        Assert.Null(vm.LastSyncStartedAtMs);
        vm.OnEvent(new SyncProgressEvent("a later transcript line", "info"));
        Assert.Contains("a later transcript line", vm.GetRunDocumentText(), StringComparison.Ordinal);

        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var sentVm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now) { RootPath = @"D:\repo" };
        Assert.True(await sentVm.SyncSilentlyAsync(SilentSyncReason.Commit));
        Assert.Equal(7000, sentVm.LastSyncStartedAtMs);
    }

    /// <summary>[final review M1] Son Sync'in bölüm açıp açmadığı kaydedilir — koordinatör, beklerken yutulan bir branch
    /// değişimini yalnız bölümsüz bir Sync yuttuysa yeniden anlatır: Sync düğmesi bölüm açar, sessiz Sync açmaz.</summary>
    [Fact]
    public async Task The_last_sync_records_whether_it_opened_a_section()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var vm = SyncedTwoRowVm(engine);

        await vm.SyncCommand.ExecuteAsync(null);
        ReplySync(vm, upToDateB: false);
        Assert.True(vm.LastSyncOpenedSection);

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Commit));
        ReplySync(vm, upToDateB: false);
        Assert.False(vm.LastSyncOpenedSection);
    }

    /// <summary>Sync düğmesi (Manual) yeni bir bölüm açar: konsol ve akış temizlenir, fetch yapılır, pill yazılır.</summary>
    [Fact]
    public async Task The_sync_button_still_clears_the_console()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = SyncedTwoRowVm(engine);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        Assert.DoesNotContain("previous operation line", vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.Empty(vm.StreamEvents);
        Assert.Equal(OperationLabel.Sync, vm.CurrentOperation);
    }

    /// <summary>[spec 2026-09-18 §6.2 "Uygulama açılışı"] Motorun İLK hazır oluşu, bir workspace varken, fetch'li bir
    /// Sync başlatır ve boot satırları kalır (transkript altına eklenir); workspace yoksa gidecek bir kök yoktur.</summary>
    [Fact]
    public async Task The_first_engine_ready_syncs_with_the_transcript()
    {
        var noRepo = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
            { LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot };
        var noRepoSent = new List<IpcCommand>();
        noRepo.DebugOnCommandSent = noRepoSent.Add;
        noRepo.OnEngineReady("1.0.0", 42);
        Assert.Empty(noRepoSent);

        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
            { RootPath = @"D:\repo", LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot };
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEngineReady("1.0.0", 42);
        await Task.Yield();

        Assert.True(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        Assert.Contains("Engine ready — v1.0.0", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>[final review I1 · spec 2026-09-18 §5.5 · §6.2] Çökmeden sonra "Restart engine": yeniden başlatılan motor
    /// ilk açılışla AYNI hazır yolundan geçer — konsola "Engine ready — v…" düşer, PID/sürüm yenilenir — ve bir
    /// workspace açıkken TEK bir Appended Sync koşar: kurtarılan projelerin satırları gri "never built" okunur.
    /// Test gerçek restart yolunu (<c>RestartEngineCommand</c>, izole motor, elle yazılmış uçuş defteri) sürer.
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden bu test <c>OnEngineReady</c>'yi iki kez çağırır ve ikinci
    /// hazır oluşun Sync BAŞLATMADIĞINI pinlerdi ("restart dünyayı değiştirmez"). Oysa üretimde restart yolu
    /// <c>OnEngineReady</c>'yi hiç çağırmıyordu (ne satır ne PID) ve motor çökmeden sonra kurtarma yaptığında ekran
    /// eski kararları gösteriyordu — kurtarmanın tam da gerektiği an.</para></summary>
    [Fact]
    public async Task A_restarted_engine_prints_its_ready_line_and_syncs_once()
    {
        using var sandbox = new SupervisorSandbox();
        new BuildOrchestrator.Core.State.InFlightLedger(sandbox.CacheRoot).Add(@"C:\r\A\A.csproj"); // koşu ortasında ölmüş motor
        await using var engine = sandbox.IsolatedEngineHost(WideStartupTimeout);
        using var root = new TempDir();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
            { RootPath = root.Path, LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot };
        vm.OnEngineExited(3);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.RestartEngineCommand.ExecuteAsync(null);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Contains("Engine ready — v", vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.NotNull(vm.EnginePid);
        Assert.True(vm.SyncBusy); // Sync kapıyı tutuyor — restart'ın temizliği onu geri açmadı
    }

    /// <summary>[Task 11] Eski worktree havuzu klasörü hâlâ diskteyse motorun bu oturumdaki İLK hazır oluşunda
    /// konsola tek satırlık bir ipucu yazılır; motorun yeniden hazır oluşu (restart) bunu TEKRARLAMAZ — aynı
    /// ilk-kez kapısı (<c>_engineWasReady</c>) açılış Sync'iyle paylaşılır. Kök testte gerçek %LOCALAPPDATA%'a
    /// değil, enjekte edilen <see cref="RunViewModel.LegacyWorktreePoolRoot"/>'a bakar.</summary>
    [Fact]
    public void The_first_engine_ready_hints_at_a_leftover_legacy_pool_and_a_restart_does_not()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-legacy-pool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
            {
                LegacyWorktreePoolRoot = root,
            };
            string hint = BuildOrchestrator.Core.Paths.LegacyWorktreePool.Hint(root)!;

            vm.OnEngineReady("1.0.0", 42);
            Assert.Contains(hint, vm.GetRunDocumentText(), StringComparison.Ordinal);

            vm.OnEngineReady("1.0.0", 43);
            Assert.Equal(1, vm.GetRunDocumentText().Split(hint, StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>[spec 2026-09-18 §5.5 · karar 12] Motor açılışta kesilmiş bir koşu kurtardıysa konsol bunu söyler —
    /// metin Core'daki tek kaynaktan. Kurtarılacak bir şey yoksa satır yoktur. Satır <c>engineReady</c> OLAYINDAN
    /// yazılır: olay hem ilk açılışta hem motor yeniden başlatılınca aynı akıştan gelir.</summary>
    [Fact]
    public void An_engine_that_recovered_an_interrupted_run_says_how_many_projects_will_rebuild()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        string line = BuildOrchestrator.Core.Planning.PlanProgressLines.PreviousRunInterrupted(3);

        vm.OnEvent(new EngineReadyEvent(42, "1.0.0"));
        Assert.DoesNotContain("previous run was interrupted", vm.GetRunDocumentText(), StringComparison.Ordinal);

        // engineReady olay akışından gelir — ilk açılışta da, Restart engine'de de aynı dal.
        vm.OnEvent(new EngineReadyEvent(43, "1.0.0", InterruptedProjects: 3));
        Assert.Contains(line, vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.Equal("previous run was interrupted; 3 projects will rebuild", line);
    }

    /// <summary>[spec 2026-09-18 §6.1] Tamamlanan Sync branch değerini checkout edilmiş branch'e hizalar ve son
    /// Sync'in HEAD'ini + zamanını kaydeder (çift Sync kontrolünün kaynağı).</summary>
    [Fact]
    public void A_completed_sync_aligns_the_branch_and_records_the_head()
    {
        long now = 1000;
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1", () => now)
        {
            RootPath = @"D:\repo",
        };

        now = 5000;
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", ""));
        vm.OnEvent(new SyncCompletedEvent("feature/x", "sha1234", false, 0, 0,
            HeadSha: "2222222222222222222222222222222222222222", ActiveBranch: "feature/x"));

        Assert.Equal("feature/x", vm.Branch);
        Assert.Equal(("feature/x", "2222222222222222222222222222222222222222"), vm.LastSyncHead);
        Assert.Equal(5000, vm.LastSyncCompletedAtMs);
        Assert.Equal(5000, vm.LastSyncAtMs); // [review M3] başlangıç hiç yazılmadıysa da tamamlanma okunur
    }

    // ---------------------------------------------------------------- [T9 · spec §6.4 · karar 22] git işlemi yarıdayken

    private const Core.Git.GitOperation Merge = Core.Git.GitOperation.Merge;

    /// <summary>Sync'lenmiş iki satırlı workspace; git işlemi <paramref name="op"/>'tan okunur (sahte probe), yoklama
    /// zamanlayıcısı <paramref name="timer"/>'dır.</summary>
    private static RunViewModel GitGatedVm(EngineHost engine, Func<Core.Git.GitOperation> op, FakePollTimer timer)
    {
        var vm = SyncedTwoRowVm(engine);
        vm.InspectGitOperation = _ => op();
        vm.GitOperationPollTimer = timer;
        return vm;
    }

    /// <summary>Kendiliğinden Sync merge yarıdayken koşmaz: tetik bekler ve akışa işlem başına BİR kez
    /// <c>waiting for git — Merge in progress — finish or abort it in git</c> düşer.</summary>
    [Fact]
    public async Task A_head_trigger_waits_while_a_merge_is_in_progress()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var vm = GitGatedVm(engine, () => Merge, new FakePollTimer());
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("main", CommittedSha), () => new FakeHeadWatcher());
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Commit);
        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Other);
        Drain(posted);

        Assert.Empty(sent);
        Assert.Equal(Merge, vm.GitOperation);
        Assert.Single(vm.StreamEvents, e => e.Text == "waiting for git — Merge in progress — finish or abort it in git");
        vm.DisableAutoSync();
    }

    /// <summary>İşaret kalkınca (yoklamanın tıkı) bekleyen tetik normal yoldan değerlendirilir: BİR sessiz Sync.</summary>
    [Fact]
    public async Task When_the_marker_goes_the_waiting_sync_runs()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = await StartedEngineAsync(sandbox);
        var posted = new Queue<Action>();
        var op = Merge;
        var timer = new FakePollTimer();
        var vm = GitGatedVm(engine, () => op, timer);
        vm.EnableAutoSync(posted.Enqueue, _ => new Core.Git.HeadState("main", CommittedSha), () => new FakeHeadWatcher());
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        await vm.AutoSync!.HeadTriggerAsync(Core.Git.HeadMove.Commit);
        timer.Tick(); // işaret hâlâ duruyor
        Drain(posted);
        Assert.Empty(sent);

        op = Core.Git.GitOperation.None;
        timer.Tick();
        Drain(posted);

        Assert.False(Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Fetch);
        vm.DisableAutoSync();
    }

    /// <summary><c>index.lock</c> 30 s kesintisiz durursa konsola BİR kez takılı kilit uyarısı düşer; araç kilidi
    /// silmez. Gerçek bir git dizini ve gerçek probe — kilit dosyası testin sonunda hâlâ yerindedir.</summary>
    [Fact]
    public void A_lock_held_for_30_seconds_warns_once_and_is_never_deleted()
    {
        using var root = new TempDir();
        string lockFile = Path.Combine(root.Path, ".git", "index.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);
        File.WriteAllText(lockFile, "");
        long now = 1000;
        var timer = new FakePollTimer();
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1", () => now)
        {
            GitOperationPollTimer = timer,
        };
        vm.RootPath = root.Path;
        static int Warnings(RunViewModel vm) => vm.GetRunDocumentText().Split('\n')
            .Count(l => l.Contains(Core.Git.GitOperationText.StuckLock, StringComparison.Ordinal));

        Assert.Equal(Core.Git.GitOperation.CommandRunning, vm.GitOperation);
        now += 29_999;
        timer.Tick();
        Assert.Equal(0, Warnings(vm));

        now += 1;
        timer.Tick();
        now += 10_000;
        timer.Tick();
        vm.OnWindowActivated();

        Assert.Equal(1, Warnings(vm));
        Assert.True(File.Exists(lockFile));
        Assert.True(timer.IsRunning);
    }

    /// <summary>Merge yarıdayken checkout ve pull kilitlidir; kilitli chip'lerin tooltip'i nedeni söyler. Kapı
    /// gönderimden önce yeniden yoklanır: bayat bir "boşta" değeriyle bile komut gitmez.</summary>
    [Fact]
    public async Task Checkout_and_pull_are_locked_mid_merge()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var op = Core.Git.GitOperation.None;
        var vm = GitGatedVm(engine, () => op, new FakePollTimer());
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("feature/x", "bbbbbbbbbbbb", false, false),
        ]));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0, Behind: 2, ActiveBranch: "main"));
        vm.OnWindowActivated();
        Assert.True(vm.CanSwitchBranch);                           // ön-koşul: kilit yalnız git işleminden
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));
        Assert.Null(vm.GitOperationTooltip);

        op = Merge; // bayat: henüz yoklanmadı
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        await vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbbbbbb", false, false));
        await vm.PullRepositoryCommand.ExecuteAsync(null);

        Assert.Empty(sent);
        Assert.False(vm.CanSwitchBranch);
        Assert.False(vm.PullRepositoryCommand.CanExecute(null));
        Assert.Equal("Merge in progress — finish or abort it in git", vm.GitOperationTooltip);
    }

    /// <summary>Build merge yarıdayken engellenmez: konsol temizlendikten sonra planlamanın başında TEK uyarı satırı,
    /// komut yine gider.</summary>
    [Fact]
    public async Task Build_mid_merge_warns_once_and_still_runs()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = GitGatedVm(engine, () => Merge, new FakePollTimer());
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        const string warning = "a merge is in progress — files with conflict markers will not compile";
        Assert.Contains("previous operation line", vm.GetRunDocumentText(), StringComparison.Ordinal); // ön-koşul: tohum satırı

        Assert.True(vm.BuildCommand.CanExecute(null));
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Single(sent.OfType<StartRunCommand>());
        var lines = vm.GetRunDocumentText().Split('\n');
        Assert.DoesNotContain(lines, l => l.Contains("previous operation line", StringComparison.Ordinal)); // temizlendi
        Assert.Single(lines, l => l.Contains(warning, StringComparison.Ordinal));
        int requested = Array.IndexOf(lines, RunViewModel.RunRequestedLine(RunMode.Build));
        Assert.True(requested >= 0, "ön-koşul: build requested satırı yok");
        Assert.Equal(warning, lines[requested + 1]); // planlamanın başı: istek satırının hemen ardından
    }

    /// <summary>[T9 fix round 1 · M2] Zamanlayıcı kök uygulandıktan SONRA verilse de (kabuğun sırası: kayıtlı kök
    /// seed'i, sonra zamanlayıcı) atama anında yoklanır — tepsiden merge yarıdayken açılış da yoklar.</summary>
    [Fact]
    public void A_poll_timer_assigned_after_the_root_starts_polling_mid_merge()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
        };
        vm.InspectGitOperation = _ => Merge;
        var timer = new FakePollTimer();

        vm.GitOperationPollTimer = timer;

        Assert.True(timer.IsRunning);
        Assert.Equal(Merge, vm.GitOperation);
    }

    /// <summary>Sync düğmesi merge yarıdayken de çalışır; temizlikten sonra ilk satır ağacın yarım olduğunu söyler.</summary>
    [Fact]
    public async Task The_sync_button_still_runs_mid_merge_and_says_so()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = GitGatedVm(engine, () => Merge, new FakePollTimer());
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        Assert.True(vm.SyncCommand.CanExecute(null));
        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        string console = vm.GetRunDocumentText();
        Assert.StartsWith("the working tree is mid-merge — results may change once it finishes", console, StringComparison.Ordinal);
        Assert.DoesNotContain("previous operation line", console, StringComparison.Ordinal);
    }

    /// <summary>Yoklama YALNIZ bir işaret dururken çalışır (2 s): işaret yokken ve git dizini olmayan kökte kurulmaz,
    /// işaret kalkınca durur.</summary>
    [Fact]
    public void Polling_runs_only_while_a_marker_exists()
    {
        using var noGit = new TempDir();
        var timer = new FakePollTimer();
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
        {
            GitOperationPollTimer = timer,
        };
        vm.RootPath = noGit.Path; // gerçek probe: git dizini yok
        vm.OnWindowActivated();
        Assert.Equal(Core.Git.GitOperation.None, vm.GitOperation);
        Assert.False(timer.IsRunning);

        var op = Core.Git.GitOperation.Rebase;
        vm.InspectGitOperation = _ => op;
        vm.OnWindowActivated();
        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);
        timer.Tick();
        Assert.True(timer.IsRunning);

        op = Core.Git.GitOperation.None;
        timer.Tick();
        Assert.False(timer.IsRunning);
        Assert.Equal(Core.Git.GitOperation.None, vm.GitOperation);
    }
}
