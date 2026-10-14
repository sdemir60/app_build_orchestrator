using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1] <b>Her işlem bitişinin TEK sinyali.</b> <see cref="RunViewModel.EndedOperationSerial"/>, bir işlem
/// (koşu, Sync, Clean, Optimize) BİTTİĞİNDE bir artar — hangi yoldan biterse bitsin: tamamlanma, hata ya da ret, motor kaybı.
/// Yazıcı tektir (<c>MarkOperationEnded</c>); işlem sonu bellek trimi gibi tüketiciler bu sinyali dinler.
///
/// <para><b>Artmayanlar da pinlenir:</b> hiç BAŞLAMAMIŞ bir işlem bitmiş sayılmaz — gönderimi düşen Sync/Clean/Optimize, boştaki
/// bir motor kaybı (Restart'ın geçtiği yol), koşuya dönüşmeden düşen bir planlama hatası ve aynı koşunun ikinci kez bitirilmesi
/// sinyali DEĞİŞTİRMEZ. checkout ve pull işlem sayılmaz; onlar bir Sync'e zincirlenirse o Sync'in bitişi artırır.</para>
///
/// <para><b>Test yüzeyi:</b> motor yoktur (<c>MainWindowHost</c>). Gönderim varsayılan olarak SENKRON düşer; "komut gitti, cevap
/// bekleniyor" penceresi <c>AcceptSends</c> ile kurulur ve motorun cevabını test <c>vm.OnEvent(...)</c> ile verir. Fixture'ın
/// kendi açılış Sync'i de bir bitiştir (<c>MainWindowHost.SyncCompleted</c>); bu yüzden her test ölçüyü fixture'DAN SONRA alır
/// (<see cref="NewHost"/>). Motor kaybı iki girişten gelir ve ikisi de aynı bırakma yolundan geçer
/// (<c>ReleaseAfterEngineLoss</c>): <c>OnEngineExited</c> (beklenmedik ölüm) ve Restart komutu. Fixture'ın motoru var olmayan
/// bir exe'dir — Restart'ın preflight'ı düşer, gerçek bir süreç doğmaz ve bırakma yolu yine koşar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class OperationEndSignalTests
{
    /// <summary>
    /// Bitişi sınanan işlem. [idle-memory · final review 63a] Eskiden işlem bir <c>string</c> anahtardı ve bilinmeyen bir anahtar
    /// <c>BeginOperation</c>'ın <c>default</c> dalında SESSİZCE Optimize sayılırdı (yazım hatası yanlış işlemi sınar, test yine
    /// geçerdi). Enum + kapalı <c>switch</c>: bilinmeyen değer fırlatır. Bakım yardımcıları yalnız <see cref="Clean"/> ve
    /// <see cref="Optimize"/>'ı kabul eder.
    /// </summary>
    public enum Operation { Run, Sync, Clean, Optimize }

    /// <summary>Uçuştaki işlemin bitiş yolu: tamamlanma, işlemin kendi hatası ya da motor kaybı.</summary>
    public enum Ending { Completes, Fails, LosesTheEngine }

    /// <summary>Fixture + sinyalin o anki değeri. Ölçü fixture'DAN SONRA alınır: kurulum Sync'i sinyali zaten bir artırmıştır ve
    /// testler yalnız KENDİ işlemlerinin farkını ölçer.</summary>
    private static (MainWindow window, RunViewModel vm, int before) NewHost(TempDir dir)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        return (window, vm, vm.EndedOperationSerial);
    }

    // Bakım işleri (Clean/Optimize) ikizdir — aynı yüzey, aynı devir, aynı hata kümesi: testler ikisini birlikte sürer.

    private static Task Click(RunViewModel vm, Operation work) => work switch
    {
        Operation.Clean => vm.CleanCommand.ExecuteAsync(null),
        Operation.Optimize => vm.OptimizeCommand.ExecuteAsync(null),
        _ => throw NotMaintenance(work),
    };

    private static void EngineStarts(RunViewModel vm, Operation work) =>
        vm.OnEvent(work switch
        {
            Operation.Clean => new CleanStartedEvent(vm.RootPath),
            Operation.Optimize => new OptimizeStartedEvent(vm.RootPath),
            _ => throw NotMaintenance(work),
        });

    private static void EngineCompletes(RunViewModel vm, Operation work) =>
        vm.OnEvent(work switch
        {
            Operation.Clean => new CleanCompletedEvent(1, 2, 1_024, 0, 1),
            Operation.Optimize => new OptimizeCompletedEvent(ProjectCount: 1),
            _ => throw NotMaintenance(work),
        });

    private static bool Busy(RunViewModel vm, Operation work) => work switch
    {
        Operation.Clean => vm.CleanBusy,
        Operation.Optimize => vm.OptimizeBusy,
        _ => throw NotMaintenance(work),
    };

    /// <summary>Bakım işinin motor hata kodu: <c>cleanFailed</c>, <c>optimizeRejected</c>, …</summary>
    private static string ErrorCode(Operation work, string outcome) => work switch
    {
        Operation.Clean => "clean" + outcome,
        Operation.Optimize => "optimize" + outcome,
        _ => throw NotMaintenance(work),
    };

    private static ArgumentOutOfRangeException NotMaintenance(Operation work) =>
        new(nameof(work), work, "not a maintenance job (Clean or Optimize)");

    // ---------------------------------------------------------------- koşu

    [StaFact]
    public void A_run_end_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        int notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RunViewModel.EndedOperationSerial)) notifications++; };
        MainWindowHost.PreviewBuild(vm, "A");
        MainWindowHost.StartBuild(vm, "A");
        Assert.Equal(before, vm.EndedOperationSerial);   // koşunun başlaması bir bitiş değildir

        MainWindowHost.FinishBuild(vm, "A");

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        Assert.Equal(1, notifications);                  // bağlanabilir: bildirim değişimde bir kez
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Engine_loss_during_a_run_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.PreviewBuild(vm, "A");
        MainWindowHost.StartBuild(vm, "A");

        vm.OnEngineExited(139);                          // runCompleted hiç gelmeyecek: koşunun bitişi budur

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Aynı koşu ikinci kez bitirilemez: koşudan SONRA gelen ikinci <c>runCompleted</c> ya da bir motor kaybı
    /// (<c>MarkRunEnded</c> yine çalışır ama <c>EndedRunSerial</c> değişmez) yeni bir bitiş değildir.</summary>
    [StaFact]
    public void A_repeated_end_of_the_same_run_does_not_bump_again()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.RunBuild(vm, "A");
        Assert.Equal(before + 1, vm.EndedOperationSerial);   // ön-koşul: koşu bir kez bitti

        MainWindowHost.CompleteRun(vm, 1);                   // ikinci runCompleted
        Assert.Equal(before + 1, vm.EndedOperationSerial);

        vm.OnEngineExited(1);                                // koşudan sonra motor gitti
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Koşuya dönüşmeden düşen bir başlatma (<c>planFailed</c>: <c>runStarted</c> hiç gelmedi) bir koşu bitirmez —
    /// ne hiç koşu yokken ne de önceki bir koşu bittikten sonra.</summary>
    [StaFact]
    public void A_plan_failure_that_never_became_a_run_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);

        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed")); // atfedilecek Sync yok, koşu hiç başlamadı
        Assert.Equal(before, vm.EndedOperationSerial);

        MainWindowHost.RunBuild(vm, "A");
        Assert.Equal(before + 1, vm.EndedOperationSerial);   // ön-koşul: gerçek bir koşu bir kez saydı

        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed")); // sonraki başlatma planlamada düştü
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Sync

    [StaFact]
    public void A_sync_completion_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        Assert.Equal(before, vm.EndedOperationSerial);   // başlangıç bir bitiş değildir

        vm.OnEvent(MainWindowHost.SyncCompleted());

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Bitişin işareti <c>syncCompleted</c> olayıdır, meşgul bayrağı değil: başlangıcı görülmemiş bir Sync'in bitişi de
    /// sayılır (fixture'ın açılış Sync'i ve yalnız bitişi süren testler buna dayanır).</summary>
    [StaFact]
    public void A_sync_completion_counts_even_when_its_start_was_never_seen()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        _ = MainWindowHost.StartSync(vm, SyncMode.Manual);   // gönderim senkron düşer: hiçbir Sync başlamadı
        Assert.Equal(before, vm.EndedOperationSerial);

        vm.OnEvent(MainWindowHost.SyncCompleted());

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Sync'e atfedilen hata (<c>planFailed</c>) o Sync'in bitişidir: <c>syncCompleted</c> artık gelmeyecek.</summary>
    [StaFact]
    public void A_sync_that_fails_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        Assert.True(vm.SyncBusy);                        // ön-koşul: hata bu Sync'e atfedilir

        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.False(vm.SyncBusy);
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · final review 63d] İstek penceresinde düşen Sync: komut gitti (<c>AcceptSends</c>) ama <c>syncStarted</c>
    /// HİÇ gelmedi, motor doğrudan <c>planFailed</c> yayınladı. Hata Sync'e atfedilir (meşgul bayrağı istek penceresini de sayar) ve
    /// o Sync'in bitişidir — yoksa kapı sonsuza dek asılı kalırdı. Bu, <see cref="A_sync_that_fails_bumps_the_serial_once"/>'ın
    /// (başlamış Sync) tamamlayıcısıdır: ret yolu aynıdır, ama bayrak <c>_syncRequested</c>'dadır, <c>_syncInFlight</c>'ta değil.
    /// </summary>
    [StaFact]
    public void A_sync_that_fails_in_its_request_window_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);                  // komut gider; cevabı test verir
        _ = MainWindowHost.StartSync(vm, SyncMode.Manual);
        Assert.True(vm.SyncBusy);                        // ön-koşul: istek penceresi açık
        Assert.Equal(before, vm.EndedOperationSerial);   // ve syncStarted gelmedi: henüz bir şey bitmedi

        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.False(vm.SyncBusy);
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Engine_loss_during_a_sync_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));

        vm.OnEngineExited(1);                            // ne syncCompleted ne Sync hatası gelecek

        Assert.False(vm.SyncBusy);
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Gönderimi düşen Sync hiç başlamadı: kapı geri açılır ama bir işlem bitmedi.</summary>
    [StaFact]
    public async Task A_sync_whose_send_fails_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);

        await MainWindowHost.StartSync(vm, SyncMode.Manual);   // varsayılan host: gönderim senkron düşer

        Assert.False(vm.SyncBusy);                       // ön-koşul: istek penceresi kapandı
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- Clean / Optimize

    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task A_maintenance_completion_bumps_the_serial_once(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        await Click(vm, work);                           // tıklama (gönderim senkron düşer, kapı geri açılır)
        EngineStarts(vm, work);                          // motor cevap verdi: iş uçuşta
        Assert.True(Busy(vm, work));
        Assert.Equal(before, vm.EndedOperationSerial);   // başlangıç bir bitiş değildir

        EngineCompletes(vm, work);                       // devir: yüzey bırakılır, zincirli Sync istenir

        Assert.False(Busy(vm, work));
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task A_maintenance_failure_bumps_the_serial_once(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        await Click(vm, work);
        EngineStarts(vm, work);

        vm.OnEvent(new ErrorEvent(ErrorCode(work, "Failed"), "something went wrong")); // zincir YOK: hata yolunda Sync takılmaz

        Assert.False(Busy(vm, work));
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Motor reddi <c>*Started</c>'tan ÖNCE gelir (Supervisor koşu uçuştayken işi kabul etmez): iş hiç başlamadı ama
    /// komut gitti ve cevap alındı — ret bir bitiştir.</summary>
    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task A_maintenance_rejection_before_it_started_bumps_the_serial_once(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);                  // komut gider; cevabı test verir
        await Click(vm, work);
        Assert.True(Busy(vm, work));                     // ön-koşul: istek penceresi açık, *Started gelmedi

        vm.OnEvent(new ErrorEvent(ErrorCode(work, "Rejected"), "A run is in flight — stop it first."));

        Assert.False(Busy(vm, work));
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task Engine_loss_during_a_maintenance_job_bumps_the_serial_once(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        await Click(vm, work);
        EngineStarts(vm, work);

        vm.OnEngineExited(1);                            // ne *Completed ne iş hatası gelecek

        Assert.False(Busy(vm, work));
        Assert.Equal(before + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Gönderimi düşen bakım işi hiç başlamadı: kapı geri açılır ama bir işlem bitmedi.</summary>
    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task A_maintenance_job_whose_send_fails_does_not_bump_the_serial(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);

        await Click(vm, work);                           // varsayılan host: gönderim senkron düşer

        Assert.False(Busy(vm, work));                    // ön-koşul: istek penceresi kapandı
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Bakım işinin bitişi, konsol korunarak bir Sync zincirler: iki ayrı bitiş, iki ayrı artış. İlk artışta zincirli
    /// Sync kapıyı çoktan devralmıştır (bırakma Sync devralındıktan SONRA yapılır) — sinyali dinleyen, işi uçuşta görüp ilkinde
    /// bekler ve zincirin Sync'inin bitişinde tetiklenir. Bu, aşağıdaki sıra değişmezinin (artış anında biten iş uçuşta değildir)
    /// belgelenmiş istisnasıdır: işin KENDİ bayrağı artıştan önce bırakılmıştır, uçuşta olan zincirin Sync'idir; zincirin sonunda
    /// (ikinci artış) uçuşta hiçbir şey kalmaz.</summary>
    [StaTheory]
    [InlineData(Operation.Clean)]
    [InlineData(Operation.Optimize)]
    public async Task A_maintenance_job_and_its_chained_sync_end_separately(Operation work)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);                  // zincirin Sync komutu da gider: kapı motorun cevabını bekler
        var recorder = new BumpRecorder(vm);
        await Click(vm, work);
        EngineStarts(vm, work);

        EngineCompletes(vm, work);

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        Assert.False(Busy(vm, work));
        var job = Assert.Single(recorder.AtBump);
        Assert.True(job.Sync);                                      // bırakma anında zincirli Sync kapıyı tutuyordu
        Assert.False(work == Operation.Clean ? job.Clean : job.Optimize);   // işin kendi bayrağı artıştan ÖNCE bırakıldı

        MainWindowHost.ReplySync(vm, ("A", null));       // motorun Sync cevabı: başlangıç, topoloji, bitiş

        Assert.Equal(before + 2, vm.EndedOperationSerial);
        Assert.False(vm.SyncBusy);
        Assert.False(recorder.AtBump[1].AnyInFlight);    // zincirin sonunda uçuşta iş yok
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- checkout ve pull (işlem değildir)

    /// <summary>
    /// [idle-memory · final review 63e] checkout ve pull İŞLEM SAYILMAZ (yalnız zincirledikleri Sync sayılır). Reddedilen pull'a
    /// Sync zincirlenmez: iş uçuştan düşer ama sinyal değişmez — trimi bekleten tek şey <c>IsOperationInFlight</c>'ın değişimidir
    /// (<c>RunBufferReleaseTests</c>). Ön-koşul: pull gerçekten uçuşta (<see cref="RunViewModel.PullBusy"/>), ret gerçekten
    /// zincirsiz (<see cref="RunViewModel.SyncBusy"/> false kalır).
    /// </summary>
    [StaFact]
    public void A_rejected_pull_without_a_chained_sync_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);                                   // pull motora gider; cevabı test verir
        vm.OnEvent(MainWindowHost.SyncCompleted() with { Behind = 2 });   // "N behind" chip'i: pull yapılabilir
        int before = vm.EndedOperationSerial;                             // o Sync'in bitişi sayıldı; ölçü ondan SONRA
        _ = vm.PullRepositoryCommand.ExecuteAsync(null);
        Assert.True(vm.PullBusy);                                         // ön-koşul: pull uçuşta

        vm.OnEvent(new PullCompletedEvent(Succeeded: false, RefusalReason: PullRefusalReason.Dirty));   // reddedildi: zincirli Sync YOK

        Assert.False(vm.PullBusy);
        Assert.False(vm.SyncBusy);
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Düşen checkout da bir işlem bitirmez: stash'siz hata (<c>Failed</c> / <c>StashFailed</c>) ağacı değiştirmedi, Sync
    /// zincirlenmez. (Stash'ten sonra düşen checkout ağacı DEĞİŞTİRİR ve sessiz bir Sync zincirler; o Sync'in bitişi sayılır.)
    /// </summary>
    [StaTheory]
    [InlineData(CheckoutStatus.Failed)]
    [InlineData(CheckoutStatus.StashFailed)]
    public async Task A_failed_checkout_without_a_chained_sync_does_not_bump_the_serial(CheckoutStatus status)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);                                   // checkout motora gider; cevabı test verir
        var feature = new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), feature]));
        await vm.SelectBranch(feature);
        Assert.True(vm.CheckoutBusy);                                     // ön-koşul: checkout uçuşta

        vm.OnEvent(new CheckoutCompletedEvent(status, "main", "main", null, 0, null, "exit 1"));   // stash yok: ağaç değişmedi

        Assert.False(vm.CheckoutBusy);
        Assert.False(vm.SyncBusy);
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Motorun checkout reddi (<c>checkoutRejected</c>: Supervisor'da bir koşu uçuşta) da bir işlem bitirmez.</summary>
    [StaFact]
    public async Task A_rejected_checkout_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        MainWindowHost.AcceptSends(vm);
        var feature = new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), feature]));
        await vm.SelectBranch(feature);
        Assert.True(vm.CheckoutBusy);                                     // ön-koşul: checkout uçuşta

        vm.OnEvent(new ErrorEvent("checkoutRejected", "A run is in flight — stop it first."));

        Assert.False(vm.CheckoutBusy);
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- boştaki motor kaybı

    /// <summary>Hiçbir işlem uçuşta değilken motorun gitmesi hiçbir şeyi bitirmez: bırakma yolu her pencereyi yine de bırakır
    /// ama bitecek bir işlem yoktur.</summary>
    [StaFact]
    public void An_engine_loss_with_nothing_in_flight_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);

        vm.OnEngineExited(1);
        vm.OnEngineExited(null);                         // protokol hatası dalı (çıkış kodu yok)

        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>Boştaki bir motoru yeniden başlatmak (Restart) da hiçbir şeyi bitirmez — <c>RestartEngineAsync</c> bırakma
    /// yolunu eski motor için her zaman çalıştırır, işlem uçuşta olsun olmasın.</summary>
    [StaFact]
    public async Task Restarting_an_idle_engine_does_not_bump_the_serial()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);

        await vm.RestartEngineCommand.ExecuteAsync(null);   // var olmayan exe: preflight düşer, bırakma yolu yine koşar

        // [idle-memory · final review 63b] Ön-koşul: test sayacın DEĞİŞMEDİĞİNİ iddia eder; yeniden başlatma yolu hiç koşmasaydı da
        // sayaç değişmezdi ve test vakumda yeşil kalırdı. Yolun koştuğu görülür: eksik exe preflight'ta yakalandı (kalıcı "motor yok"
        // durumu, nedeni eksik dosya) ve hiçbir motor (PID) doğmadı.
        Assert.True(vm.IsEngineUnavailable);
        Assert.Equal(RunViewModel.EngineMissingMessage, vm.EngineDiedMessage);
        Assert.Null(vm.EnginePid);
        Assert.Equal(before, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- sıra değişmezleri (sinyali dinleyen kabuk bunlara dayanır)

    /// <summary>Her bildirimin adını sırasıyla, sinyalin her artışında da VM'in o anki uçuş bayraklarını ve fazını kaydeder.</summary>
    private sealed class BumpRecorder
    {
        public List<string?> Notified { get; } = [];

        public List<(bool Run, bool Sync, bool Clean, bool Optimize, bool AnyInFlight, AppPhase Phase)> AtBump { get; } = [];

        public BumpRecorder(RunViewModel vm) => vm.PropertyChanged += (_, e) =>
        {
            Notified.Add(e.PropertyName);
            if (e.PropertyName == nameof(RunViewModel.EndedOperationSerial))
                AtBump.Add((vm.IsRunInFlight, vm.SyncBusy, vm.CleanBusy, vm.OptimizeBusy, vm.IsOperationInFlight, vm.Phase));
        };
    }

    /// <summary>Bitişi sınanacak işlemi uçuşa alır: koşu başladı, Sync başladı ya da bakım işi tıklandı ve motor başlattı.</summary>
    private static async Task BeginOperation(RunViewModel vm, Operation operation)
    {
        switch (operation)
        {
            case Operation.Run:
                MainWindowHost.PreviewBuild(vm, "A");
                MainWindowHost.StartBuild(vm, "A");
                break;
            case Operation.Sync:
                vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
                break;
            case Operation.Clean:
            case Operation.Optimize:
                await Click(vm, operation);
                EngineStarts(vm, operation);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    /// <summary>Uçuştaki işlemi verilen yoldan bitirir: tamamlanma, işlemin kendi hatası ya da motor kaybı.</summary>
    private static void EndOperation(RunViewModel vm, Operation operation, Ending ending)
    {
        switch (ending)
        {
            case Ending.LosesTheEngine: vm.OnEngineExited(1); break;
            case Ending.Completes when operation == Operation.Run: MainWindowHost.FinishBuild(vm, "A"); break;
            case Ending.Completes when operation == Operation.Sync: vm.OnEvent(MainWindowHost.SyncCompleted()); break;
            case Ending.Fails when operation == Operation.Run: vm.OnEvent(new ErrorEvent("runFailed", "the build failed")); break;
            case Ending.Fails when operation == Operation.Sync: vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed")); break;
            case Ending.Fails: vm.OnEvent(new ErrorEvent(ErrorCode(operation, "Failed"), "something went wrong")); break;
            default: throw new ArgumentOutOfRangeException(nameof(ending), ending, $"no ending path for {operation}");
        }
    }

    /// <summary>
    /// [sıra değişmezi] Sinyal artarken biten işlem ARTIK uçuşta değildir: kendi meşguliyet bayrağı artıştan ÖNCE bırakılır.
    /// Bitişi dinleyen (işlem sonu bellek trimi) artış anında "uçuşta işlem var" görseydi bekler ve o işlem için kimse yeniden
    /// sormazdı — trim sessizce kaybolurdu. Tek istisna bakım işinin zincirli Sync'idir
    /// (<see cref="A_maintenance_job_and_its_chained_sync_end_separately"/>): orada Sync kapıyı devralmıştır ve kendi bitişi
    /// yeniden sorar.
    /// </summary>
    [StaTheory]
    [InlineData(Operation.Run, Ending.Completes)]
    [InlineData(Operation.Run, Ending.Fails)]
    [InlineData(Operation.Run, Ending.LosesTheEngine)]
    [InlineData(Operation.Sync, Ending.Completes)]
    [InlineData(Operation.Sync, Ending.Fails)]
    [InlineData(Operation.Sync, Ending.LosesTheEngine)]
    [InlineData(Operation.Clean, Ending.Fails)]
    [InlineData(Operation.Clean, Ending.LosesTheEngine)]
    [InlineData(Operation.Optimize, Ending.Fails)]
    [InlineData(Operation.Optimize, Ending.LosesTheEngine)]
    public async Task When_the_serial_bumps_the_ended_operation_is_no_longer_in_flight(Operation operation, Ending ending)
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        await BeginOperation(vm, operation);
        Assert.True(vm.IsOperationInFlight);             // ön-koşul: işlem uçuşta
        var recorder = new BumpRecorder(vm);

        EndOperation(vm, operation, ending);

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        var atBump = Assert.Single(recorder.AtBump);
        Assert.False(atBump.AnyInFlight, $"{operation} {ending}: artış anında işlem hâlâ uçuşta görünüyordu");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [sıra değişmezi · wave 1 son temizlik C-M3] <b>Sync'in bitişinde önce faz oturur, bitiş EN SON bildirilir</b>
    /// (<c>EndSync</c>: bırakma → <c>syncCompleted</c>'ın kendi faz kararı → <c>MarkOperationEnded</c>). Bitişi dinleyen artış anında
    /// VM'in fazını okur; bildirim fazdan ÖNCE gitseydi yarım kalmış bir faz görürdü.
    ///
    /// <para><b>Neden boş bir topoloji:</b> topolojisi olan bir çözümde bırakmanın dinlenme fazı zaten <c>Idle</c>'dır ve iki sıra da
    /// Idle verir — test vakumda yeşil kalırdı. Klasörün altında hiç proje yokken (<c>HasTopology</c> false) bırakma fazı <c>Boot</c>'a
    /// indirir ve Idle'a çıkış YALNIZ <c>syncCompleted</c>'ın kendi kararıdır (<c>OnSyncCompleted</c>'ın <c>settlePhase</c>'i). Sıra
    /// dönseydi artış anında faz <c>Boot</c> görünürdü.</para>
    /// </summary>
    [StaFact]
    public void A_sync_completion_settles_the_phase_before_it_reports_the_end()
    {
        using var dir = new TempDir();
        var (window, vm, before) = NewHost(dir);
        var recorder = new BumpRecorder(vm);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));   // çözümde hiç proje yok
        Assert.Equal(AppPhase.Syncing, vm.Phase);                 // ön-koşul: görünür Sync fazı tutuyor
        Assert.False(vm.HasTopology);                             // ön-koşul: bırakmanın dinlenme fazı Idle değil Boot

        vm.OnEvent(MainWindowHost.SyncCompleted(projectCount: 0));

        Assert.Equal(before + 1, vm.EndedOperationSerial);
        var atBump = Assert.Single(recorder.AtBump);
        Assert.Equal(AppPhase.Idle, atBump.Phase);                // bildirim anında faz çoktan Idle
        Assert.Equal(AppPhase.Idle, vm.Phase);
        GC.KeepAlive(window);
    }

    /// <summary>[sıra değişmezi] Bir koşunun bitişinde "koşu bitti" (<see cref="RunViewModel.EndedRunSerial"/>) "işlem bitti"den
    /// (<see cref="RunViewModel.EndedOperationSerial"/>) ÖNCE bildirilir. Kabuk koşunun tepside bitip bitmediğini ilk bildirimde
    /// kaydeder ve trimi ikincisinde o kayda göre ister (tepside biten koşu gösterge çıkışını bekler); sıra dönseydi trim kararı
    /// bir önceki koşunun kaydıyla verilirdi.</summary>
    [StaTheory]
    [InlineData(Ending.Completes)]
    [InlineData(Ending.Fails)]
    [InlineData(Ending.LosesTheEngine)]
    public async Task A_run_end_announces_the_run_before_the_operation(Ending ending)
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewHost(dir);
        await BeginOperation(vm, Operation.Run);
        var recorder = new BumpRecorder(vm);

        EndOperation(vm, Operation.Run, ending);

        var ends = recorder.Notified
            .Where(n => n is nameof(RunViewModel.EndedRunSerial) or nameof(RunViewModel.EndedOperationSerial))
            .ToList();
        Assert.Equal([nameof(RunViewModel.EndedRunSerial), nameof(RunViewModel.EndedOperationSerial)], ends);
        GC.KeepAlive(window);
    }
}
