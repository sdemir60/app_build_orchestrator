using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §3.1 · §9-4] <b>İşlem borusu:</b> <c>_beginOp</c> → <c>_neutralize</c> → <c>_mark</c> → koşu.
/// Her işlem (Build · Rebuild · Resolve) aynı borudan geçer ve bu testler borunun İLK İKİ adımını pinler —
/// üçüncüsü (dalga) <see cref="ChoreographyTests"/>'te, çünkü zamanlama kabuğun işidir.
///
/// <para>Buradaki iddiaların ortak ilkesi tasarımın kendi cümlesidir: <b>renk yalnız SON işlemin hikâyesini
/// anlatır.</b> Bir işlem başladığı anda ekranda önceki koşudan kalma hiçbir renk olamaz, ve kapsamın amber'a
/// yanması yalnız işaretleme dalgasının işidir.</para>
/// </summary>
public class OperationPipelineTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode Node(string id, string name, int buildOrder) =>
        new(id, name, id, SolutionNames: [], Dependencies: [], buildOrder,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    /// <summary>Bir Sync + bir tam koşu: A yeşil, B kırmızı, C atlanmış. Dönüş, sonraki işlemin BAŞLANGIÇ
    /// noktasıdır.</summary>
    private static RunViewModel AfterOneCompletedRun()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node("a", "A", 0), Node("b", "B", 1), Node("c", "C", 2)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 3, 0));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 3, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem("a", "A", true),
            new BuildPreviewItem("b", "B", true),
            new BuildPreviewItem("c", "C", false),
        ]));
        vm.OnEvent(new ProjectStartedEvent("r1", "a", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", "a", 1200));
        vm.OnEvent(new ProjectStartedEvent("r1", "b", "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", "b", 900, "build failed", ["A"]));
        vm.OnEvent(new ProjectSkippedEvent("r1", "c", SkipReasons.UpToDate));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 1, 1, 0, 2100));
        return vm;
    }

    private static ProjectRowViewModel Row(RunViewModel vm, string id) =>
        vm.Projects.Single(r => r.Id == id);

    // ============================================================ _neutralize

    /// <summary>
    /// [§9-4 <c>_neutralize</c>] Yeni bir işlem başlarken önceki koşunun TÜM izleri silinir — herkes düz nötr
    /// griye iner. Tasarımın cümlesi: "önceki koşunun tüm izleri silinir, herkes düz gri".
    ///
    /// <para>Bu, koreografinin ön koşuludur: dalga bir NÖTR ZEMİN üzerine yanar. Zemin nötr değilse (ekranda
    /// hâlâ önceki koşunun yeşili ve kırmızısı varsa) "renk yalnız son işlemin hikâyesini anlatır" ilkesi
    /// daha ilk karede bozulur.</para>
    /// </summary>
    [Fact]
    public async Task A_new_operation_wipes_every_trace_of_the_previous_run()
    {
        var vm = AfterOneCompletedRun();
        Assert.Equal(VisualStatus.Succeeded, Row(vm, "a").VisualStatus); // ön-koşul: renkler GERÇEKTEN orada
        Assert.Equal(VisualStatus.Failed, Row(vm, "b").VisualStatus);
        Assert.Equal(VisualStatus.Skipped, Row(vm, "c").VisualStatus);

        await vm.BuildCommand.ExecuteAsync(null);

        foreach (var row in vm.Projects)
        {
            Assert.Equal(ProjectRowState.Pending, row.State);
            Assert.Equal(0, row.DurationMs);
            Assert.Null(row.DepIssues);
            Assert.False(row.Fresh);   // başlangıç modu da düşer — artık bir işlem yürüyor
            Assert.Equal(VisualStatus.Discovered, row.VisualStatus);
        }
    }

    /// <summary>
    /// [§9-4] <c>_neutralize</c> PLANI silmez. Kapsam (<see cref="RunViewModel.ScopeFor"/>) satırların
    /// <c>WillBuild</c>'inden okunur; nötrleme onu da sıfırlasaydı dalga yanacak kimseyi bulamazdı.
    /// Prototipte de sıra bu yöndedir: <c>beginBuild</c> önce <c>st.will</c>'i yazar, sonra
    /// <c>_neutralize()</c> çağırır (build-data.js:541-547).
    /// </summary>
    [Fact]
    public async Task Neutralizing_keeps_the_plan_so_the_marking_wave_still_has_a_scope()
    {
        var vm = AfterOneCompletedRun();
        IReadOnlyList<ProjectRowViewModel>? scope = null;
        vm.OperationBegun += (_, s) => scope = s;

        await vm.BuildCommand.ExecuteAsync(null);

        // A yeşil bittiği için planı düştü (OnProjectDone: succeeded → WillBuild=false); B hâlâ bayat.
        Assert.NotNull(scope);
        Assert.Equal(["B"], scope.Select(r => r.Name));
    }

    /// <summary>
    /// [§3.1 "Sync"] Sync bir İŞLEM DEĞİLDİR — işlemlerin zeminidir. Nötrlemesi kendi kuralını izler ve
    /// başlangıç moduna DÖNDÜRÜR (kesikli), bir işlemin nötrlemesi ise başlangıç modunu DÜŞÜRÜR.
    /// İki yol aynı sıfırlamayı paylaşır, yalnız bu bayrakta ayrışır.
    /// </summary>
    [Fact]
    public void A_sync_neutralizes_too_but_lands_in_fresh_mode_instead()
    {
        var vm = AfterOneCompletedRun();

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node("a", "A", 0), Node("b", "B", 1), Node("c", "C", 2)], [], [], []));

        foreach (var row in vm.Projects)
        {
            Assert.Equal(ProjectRowState.Pending, row.State);

            Assert.True(row.Fresh);
            Assert.Equal(VisualStatus.Fresh, row.VisualStatus);
        }
    }

    /// <summary>
    /// [§9-4 · §2.2] İşlem pill'i (<see cref="RunViewModel.CurrentOperation"/>) nötrlemeden SONRA yazılır.
    ///
    /// <para>Etiketin değişmesi, kabuğun grafa "yeni bir işlem başladı, statüleri yeniden oku" dediği
    /// sinyaldir — başlangıç modunun düşüşü <c>Counters</c>'ı hareket ettirmediği için tek başına sayaca
    /// bakılamaz (bkz. <c>MainWindow.OnVmPropertyChangedForGraph</c>). Sinyal satırlar HENÜZ nötrlenmemişken
    /// çıkarsa graf, önceki koşunun renkleriyle tazelenir ve dalga o renklerin üzerine yanar.</para>
    /// </summary>
    [Fact]
    public async Task The_operation_label_is_written_only_after_the_rows_are_neutral()
    {
        var vm = AfterOneCompletedRun();
        List<VisualStatus>? atSignal = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.CurrentOperation))
                atSignal = [.. vm.Projects.Select(r => r.VisualStatus)];
        };

        await vm.RebuildCommand.ExecuteAsync(null); // etiket BUILD → REBUILD: sinyal gerçekten çıkar

        Assert.NotNull(atSignal);
        Assert.All(atSignal, v => Assert.Equal(VisualStatus.Discovered, v));
    }

    // ============================================================ nötr an (planlama penceresi)

    /// <summary>
    /// [§9-4 adım 1: "nötr an 440ms — kapsam da düz gri (amber henüz yok)"] Bir koşu yalnız İSTENDİĞİNDE
    /// (motorun cevabı beklenirken) planlanmış satır HENÜZ kuyrukta değildir: düz nötr gri durur ve amber'a
    /// yalnız işaretleme dalgası (<see cref="ProjectRowViewModel.Marked"/>) yakar.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: <c>queued</c> "bir run UÇUŞTAYKEN (IsRunning <b>ya da</b>
    /// IsStarting) planlanmış ama başlamamış satır"dı. Gerekçesi, planlama penceresinde ekranın sessiz
    /// kalmamasıydı. v1.11.0'da o pencereyi açılış koreografisi doldurur — ve kapsamı TAM OLARAK aynı küme
    /// olduğu için hiçbir bilgi kaybolmaz, yalnız anında değil dalga hâlinde belirir. Eski kural yürürlükte
    /// kalsaydı koreografinin ilk iki adımı (nötr an + dalga) hiç görünmezdi: kapsam daha tıklama anında
    /// amber olurdu.</para>
    /// </summary>
    [Fact]
    public void While_a_run_is_only_requested_the_scope_is_still_plain_grey()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node("a", "A", 0)], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem("a", "A", true)]));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 1, 0, 10));

        var row = Row(vm, "a");
        row.WillBuild = true;

        vm.IsStarting = true;  // koşu İSTENDİ — motor henüz cevap vermedi (planlama penceresi)
        Assert.Equal(GraphStatus.Discovered, row.Status);
        Assert.Equal(VisualStatus.Discovered, row.VisualStatus);

        row.Marked = true;     // ...dalga bu satıra geldi
        Assert.Equal(VisualStatus.Marked, row.VisualStatus);

        vm.IsRunning = true;   // motor cevap verdi — statü kanalı devralır
        Assert.Equal(GraphStatus.Queued, row.Status);
        Assert.Equal(VisualStatus.Queued, row.VisualStatus);
    }

    // ============================================================ liste sabitliği

    /// <summary>
    /// [§9-4 · design v1.10.0 §3.8 "liste yerinden oynamaz"] Rebuild koşuyu başlatırken proje listesini
    /// BOŞALTMAZ. Nötrleme zaten önceki koşunun sonuçlarını silmiştir; listeyi ayrıca boşaltmak, açılış
    /// koreografisinin işaretlediği satır nesnelerini ortasında yok eder ve liste remount olur.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>OnRunStarted</c> eskiden <c>if (e.Mode == RunMode.Rebuild)
    /// Projects.Clear()</c> yapardı — o zaman "yeni bir tabana dön" işini yapan tek yol buydu. v1.11.0'da o iş
    /// <c>_neutralize</c>'a geçti ve yerinde yapılıyor; Clear artık yalnız zarar veriyordu.</para>
    /// </summary>
    [Fact]
    public void A_rebuild_does_not_empty_the_list_when_the_run_starts()
    {
        var vm = AfterOneCompletedRun();
        var before = vm.Projects.ToArray();

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Rebuild, 3, 1, "Debug", 0));

        Assert.Equal(3, vm.Projects.Count);
        Assert.Equal(before, vm.Projects); // AYNI satır nesneleri — remount yok
    }
}
