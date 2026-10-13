using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz C · C4 · idle-memory dalga 1] <b>Biten koşunun tamponları bırakılır; her işlemin ardından bellek bir kez
/// toplanır.</b> OSYS büyüklüğünde bir koşu yüz binlerce satır nesnesini canlı satır tamponunda (<c>RunViewModel</c>
/// <c>_liveLines</c>) biriktirir; tampon yalnız koşu SÜRERKEN, bir proje logu açılırken diskteki snapshot'ın ötesindeki kuyruğu
/// dikmek için vardır. Koşu bitince disk tamdır ve tampon ölü yüktür; açık projenin metin tamponu da yalnız ekrandaki sayfa
/// için yaşar. Bu dosya üç şeyi pinler: (1) bırakma — koşu sonu, bekleyen dikişe saygı, proje değişimi; (2) bırakmanın yeni
/// işlemin temizliğini DEĞİŞTİRMEDİĞİ; (3) işlem sonu tek seferlik toplama (bellek trimi): bir işlem (koşu, Sync, Clean,
/// Optimize) bitti, uçuşta işlem ve cevabı beklenen log yüklemesi yok; pencere görünürse bitiş finali bitti ve pay geçti,
/// gizliyse tepside biten koşunun göstergesi çıkışını tamamladı. Ayrıca Faz A2'den ertelenen boşluğu kapatır: gizli konsol
/// yetişmesinin PROJE LOGU kolu.
///
/// <para><b>Test yüzeyleri:</b> motor yoktur (<c>MainWindowHost</c>): <c>LoadProjectLogAsync</c>'in gönderimi senkron düşer
/// ama dikiş silahlı kalır (gecikmiş bir chunk hâlâ eşleşir) ve motorun yanıtı (<c>ProjectLogChunkEvent</c>) testten
/// verilir. Tepsi göstergesi headless'ta kurulmaz; çıkış bildirimini test <c>MainWindow.OnTrayIndicatorExitFinished</c>'a
/// doğrudan verir (<c>OnGlobalHotkey</c> deseni). Fixture her pencereye boş bir toplayıcı takar (süitte gerçek agresif
/// toplama koşmaz) ve açılış Sync'inin trimini dönmeden yürütür; toplamayı sayan testler kendi sahtesini takar
/// (<c>MainWindow.MemoryCollector</c>). Toplama uygulama boştayken ertelendiği için testler sinyalden sonra dispatcher'ı
/// boşaltır (<c>DispatcherPump.DrainToIdle</c>); boşaltma zamanlayıcı ATEŞLEMEZ, bu yüzden görünür yolu boşaltmayla süren testler
/// payı sıfırlar (<c>MainWindow.TrimGraceAfterFinale</c>; <see cref="TrimHost"/>). Payın kendisini sınayan testler onu kısa tutup
/// pompalar (<c>DispatcherPump.PumpUntil</c>) ya da test boyunca dolmayacak kadar uzun tutar. Headless'ta bitiş finali oynamaz
/// (animasyonlar kapalı: "final yok" dalı bitişi hemen duyurur); finali bekleyen yolu sınayan test grafın kapısını açar. Gerçek toplamayı
/// yalnız üretim toplayıcısının geçerliliğini sınayan test, bir kez koşturur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RunBufferReleaseTests
{
    /// <summary>Bir proje logunu açar: kart seçilir, yükleme istenir ve motorun yanıtı gelir. Yanıtı ayrı vermek isteyen test
    /// <see cref="RequestProjectLog"/> ile <see cref="ReplyWithProjectLog"/>'u ayrı çağırır.</summary>
    private static void OpenProjectLog(RunViewModel vm, string name, string diskText, int through)
    {
        RequestProjectLog(vm, name);
        ReplyWithProjectLog(vm, name, diskText, through);
    }

    /// <summary>Kart seçilir ve proje logu istenir; yanıt henüz gelmedi (gönderim düşer, dikiş silahlı kalır).</summary>
    private static void RequestProjectLog(RunViewModel vm, string name)
    {
        string id = MainWindowHost.IdOf(name);
        vm.SelectProject(id);
        _ = vm.LoadProjectLogAsync(id);
    }

    /// <summary>Motorun yanıtı: diskteki snapshot <paramref name="through"/>. satıra kadar <paramref name="diskText"/>'tir.</summary>
    private static void ReplyWithProjectLog(RunViewModel vm, string name, string diskText, int through) =>
        vm.OnEvent(new ProjectLogChunkEvent(MainWindowHost.IdOf(name), 0, diskText, IsLast: true, ThroughLineNumber: through));

    /// <summary>Gerçek toplama yerine sayaç: toplayıcının kaç kez çağrıldığını tutar.</summary>
    private sealed class CollectionCounter
    {
        public int Count { get; private set; }

        public void Collect() => Count++;
    }

    /// <summary>İşlem sonu trimini süren testlerin TEK kurulumu: fixture (tek proje "A"), toplamaları sayan sahte toplayıcı ve
    /// verilen pay. Varsayılan pay SIFIRDIR: boşaltma (<c>DrainToIdle</c>) zamanlayıcı ateşlemez, görünür yolun yürütmesi payı
    /// beklemeden boşta kuyruğa düşer. Payı sınayan testler kendi süresini verir.</summary>
    private static (global::BuildOrchestrator.App.MainWindow window, RunViewModel vm, CollectionCounter collections) TrimHost(
        TempDir dir, TimeSpan grace = default)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        window.TrimGraceAfterFinale = grace;
        var collections = new CollectionCounter();
        window.MemoryCollector = collections.Collect;
        return (window, vm, collections);
    }

    [StaFact]
    public void A_finished_run_releases_its_live_line_buffer()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        MainWindowHost.StartBuild(vm, "A", "B");
        MainWindowHost.LogLine(vm, "A", 1, "a line");
        MainWindowHost.LogLine(vm, "B", 1, "b line");
        Assert.Equal(2, vm.LiveLineCount);   // ön-koşul: koşu sürerken tampon dolu — açılan bir proje logu kuyruğunu buradan alır

        MainWindowHost.FinishBuild(vm, "A", "B");

        Assert.Equal(0, vm.LiveLineCount);   // KIRMIZI: bugün tampon bir sonraki işleme dek yaşar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [Review Focus 3] Yükleme uçuştayken koşu biter, motorun yanıtı sonra gelir. Tampon dikişin KUYRUĞUNU taşır
    /// (snapshot'ın ötesindeki satırlar yalnız orada durur): yanıttan önce bırakılsaydı dikilen belge son satırlardan yoksun
    /// kalırdı. Bırakma yanıt dikildikten sonra olur. "Uçuşta" ölçütü <c>LoadProjectLogAsync</c>'in dönüşü değil bekleyen
    /// dikiştir: gönderim düşse bile dikiş silahlı kalır ve kuyruk ona aittir.
    /// </summary>
    [StaFact]
    public void A_run_that_ends_while_a_project_log_is_loading_keeps_its_tail_for_the_stitch_and_releases_it_after_the_reply()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        string a = MainWindowHost.IdOf("A");
        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "on disk");
        MainWindowHost.LogLine(vm, "A", 2, "tail");      // snapshot'ın ötesinde: yalnız canlı tamponda
        RequestProjectLog(vm, "A");                      // yükleme istendi, yanıt henüz yok

        MainWindowHost.CompleteRun(vm, 1);               // yanıttan ÖNCE koşu biter

        Assert.Equal(2, vm.LiveLineCount);               // bırakma yanıtı bekler: kuyruk yerinde
        ReplyWithProjectLog(vm, "A", "on disk\n", through: 1);   // motorun snapshot'ı 1. satıra kadar

        Assert.Equal("on disk\ntail\n", vm.GetProjectDocumentText(a)); // kuyruk dikişe girdi: kayıp yok
        Assert.Equal(0, vm.LiveLineCount);               // KIRMIZI: yanıt döndü, bırakılmalı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Proje tamponu yalnız EKRANDAKİ sayfa için yaşar: konsol başka bir projeye geçince eskinin tamponu düşer ve geri
    /// seçilince diskten yeniden yüklenir. Bırakma anı seçimin değiştiği an değil gösterimin ayrıldığı andır — yeni projenin
    /// yanıtı gelene dek eski sayfa ekrandadır.
    /// </summary>
    [StaFact]
    public void Moving_the_console_to_another_project_drops_the_old_buffer_and_reopening_it_reloads_from_disk()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string a = MainWindowHost.IdOf("A"), b = MainWindowHost.IdOf("B");
        OpenProjectLog(vm, "A", "a on disk\n", through: 0);
        Assert.Equal(a, vm.ActiveProjectId);                        // ön-koşul: A'nın sayfası ekranda
        Assert.Equal("a on disk\n", vm.GetProjectDocumentText(a));  // ön-koşul: tampon dolu

        OpenProjectLog(vm, "B", "b on disk\n", through: 0);

        Assert.Equal(b, vm.ActiveProjectId);
        Assert.Equal("", vm.GetProjectDocumentText(a));             // KIRMIZI: bugün A'nın tamponu bir sonraki işleme dek yaşar
        Assert.Equal("b on disk\n", vm.GetProjectDocumentText(b));  // ekrandaki proje yerinde

        OpenProjectLog(vm, "A", "a changed on disk\n", through: 0); // geri seçildi: diskten yeniden yüklenir

        Assert.Equal("a changed on disk\n", vm.GetProjectDocumentText(a));
        Assert.Equal("", vm.GetProjectDocumentText(b));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Pin (davranış DEĞİŞMEZ): yeni işlem tamponların tümünü tek adımda temizler ve <c>ConsoleCleared</c>'i bildirir —
    /// bırakma bunun yerine geçmez ve onu eksiltmez. Bırakma burada ertelenmiş durumdadır (bekleyen dikiş), yani
    /// temizlenecek bir şey gerçekten vardır.
    /// </summary>
    [StaFact]
    public void A_new_operation_still_clears_every_console_buffer_in_one_step()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.AcceptSends(vm);
        string a = MainWindowHost.IdOf("A");
        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "old line");
        // Yetim dikiş: kart seçilir, yükleme istenir, yanıt gelmeden kart bırakılır — gecikmiş yanıt dikişi yine yazar ama sayfa
        // açılmaz (RunViewModelTests'teki deselect deseni). Bu tamponu yalnız yeni işlemin temizliği boşaltır.
        RequestProjectLog(vm, "A");
        vm.SelectProject(null);
        ReplyWithProjectLog(vm, "A", "old line\n", through: 1);
        Assert.Equal("old line\n", vm.GetProjectDocumentText(a));   // ön-koşul: proje tamponu dolu (eskiden hiç dolmuyordu — iddia boştu)
        Assert.Equal(1, vm.GetProjectLineCount(a));                 // ön-koşul: satır sayısı da
        RequestProjectLog(vm, "A");                                  // yeniden açılıyor: yanıt henüz yok
        MainWindowHost.CompleteRun(vm, 1);               // dikiş bekliyor: canlı tampon yerinde
        Assert.Equal(1, vm.LiveLineCount);               // ön-koşul: temizlenecek canlı satır var
        Assert.Contains("old line", vm.GetRunDocumentText());
        int cleared = 0; vm.ConsoleCleared += (_, _) => cleared++;

        _ = vm.BuildCommand.ExecuteAsync(null);          // yeni işlem: temizlik ilk await'ten ÖNCE, senkron

        Assert.Equal(0, vm.LiveLineCount);
        Assert.DoesNotContain("old line", vm.GetRunDocumentText());
        Assert.Equal("", vm.GetProjectDocumentText(a));
        Assert.Equal(0, vm.GetProjectLineCount(a));
        Assert.True(cleared > 0, "ConsoleCleared bildirilmeli: kabuk ekrandaki belgeyi de boşaltır");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Gizli + koşu bitti + gösterge çıkış evresini tamamladı → bellek BİR kez toplanır. Koşu sürerken gelen bildirim o anda
    /// toplamaz (koşu bitince bitiş sinyali toplamayı ister) ve aynı koşunun ikinci bitiş sinyali yeniden toplamaz.
    /// </summary>
    [StaFact]
    public void A_hidden_window_collects_memory_once_when_the_run_has_ended_and_the_indicator_has_left()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int collections = 0;
        window.MemoryCollector = () => collections++;
        window.SetSurfaceHidden(true);
        MainWindowHost.StartBuild(vm, "A");

        window.OnTrayIndicatorExitFinished(vm.RunSerial);            // koşu sürerken: henüz toplanmaz — sinyal bu koşu için kaydedilir
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections);

        MainWindowHost.FinishBuild(vm, "A");
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections);                    // KIRMIZI: bugün hiçbir şey toplamıyor

        window.OnTrayIndicatorExitFinished(vm.RunSerial);            // aynı koşunun ikinci bitiş sinyali
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections);
        GC.KeepAlive(window);
    }

    /// <summary>"Bir kez" koşu kimliğine bağlıdır (<c>RunViewModel.RunSerial</c>): her koşunun bitişi kendi toplamasını alır.</summary>
    [StaFact]
    public void Each_run_that_ends_while_hidden_gets_its_own_collection()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int collections = 0;
        window.MemoryCollector = () => collections++;
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, "A");
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections);                    // KIRMIZI: bugün hiçbir şey toplamıyor

        MainWindowHost.RunBuild(vm, "A");                // ikinci koşu: başlangıç bayrağı sıfırlar
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();
        Assert.Equal(2, collections);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz C · son toparlama B2] Göstergenin çıkışı, nefes sırasında yeni bir koşu başlasa bile ÖNCEKİ koşuya aittir. Çıkış
    /// bildirimi nefesten sonra gelir; N'in nefesi içinde N+1 başlarsa bildirimin anındaki koşu kimliği N+1'dir ve çıkış ona
    /// yazılırdı: N+1 biter bitmez toplama istenir, bloklayan GC N+1'in göstergesinin ekrandaki çıkış animasyonu sırasında koşardı
    /// ("balondan sonra" kuralının ihlali). Doğrusu: N+1 uçuştayken hiçbir şey toplanmaz, N+1 bitince de ancak N+1'in çıkışı
    /// bitince — ve o zaman bir kez.
    /// </summary>
    [StaFact]
    public void A_run_that_starts_during_the_indicator_breath_does_not_take_over_the_previous_runs_exit()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int collections = 0;
        window.MemoryCollector = () => collections++;
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, "A");                // N biter; göstergenin çıkışı N için başlar
        int ended = vm.RunSerial;
        MainWindowHost.StartBuild(vm, "A");              // N'in nefesi içinde N+1 başladı
        window.OnTrayIndicatorExitFinished(ended);       // N'in çıkışı (nefesten sonra): kimlik çıkışın başladığı andaki koşu
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections);                    // N+1 uçuşta: ertelenen toplama atlanır
        Assert.True(vm.RunSerial > ended, "ön-koşul: N+1 başladı");

        MainWindowHost.FinishBuild(vm, "A");             // N+1 biter; göstergesinin çıkışı henüz bitmedi
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections);                    // KIRMIZI: N+1'in çıkış animasyonu sürerken bloklayan GC koşuyordu

        window.OnTrayIndicatorExitFinished(vm.RunSerial);            // N+1'in çıkışı bitti
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] <b>Eski iddia</b> (<c>A_visible_window_does_not_collect_after_a_run</c>): "pencere görünürken koşu
    /// sonrası hiç toplanmaz — kullanıcı ekrandadır, bloklayan toplama UI'yi durdurur"; aynı çıkış bildirimi yalnız gizliyken
    /// topluyordu. <b>Neden değişti (ölçüm, 2026-10-13 — <c>.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md</c>):</b>
    /// koşu sonrası boştaki App görünür yolda 798 MB, tepsi yolunda 444 MB; fark yalnız toplamadan. Görünürde de işlem başına
    /// BİR kez toplanır — bitiş finalinden sonra, pay geçince ve uygulama boştayken. Headless'ta final oynamaz ("final yok" dalı
    /// bitişi hemen duyurur) ve pay sıfırdır (<see cref="TrimHost"/>). Eski testin ikinci yarısı (gizlenip aynı bildirim gelince)
    /// burada "trim edilmiş işlem ikinci kez toplanmaz" olarak kalır.
    /// </summary>
    [StaFact]
    public void A_visible_window_collects_once_after_the_end_finale()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);

        MainWindowHost.RunBuild(vm, "A");                // görünürde biter: final oynamaz, bitişi hemen duyurulur
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu

        window.SetSurfaceHidden(true);                   // sonraki sinyaller (gizlenme, gösterge çıkışı) aynı işlemi yeniden toplamaz
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);
        GC.KeepAlive(window);
    }

    /// <summary>Görünür pencerede trim, bitişten (finalden) sonra PAY kadar bekler: pay dolmadan toplanmaz, dolunca bir kez.
    /// Üretimin görünür yolu yalnız bu zamanlayıcıdan geçer; bu test onu dolana dek pompalar. Trimi boşaltmayla süren testler payı
    /// sıfırlar (<see cref="TrimHost"/>'un varsayılanı); sıfırlamayanlar onu bilerek uzun tutar — pay dolmadan olanı sınarlar:
    /// gizlenme, yeniden kurulan pay, kapanış — ya da kısa tutup pompalar.</summary>
    [StaFact]
    public void A_visible_trim_runs_once_the_grace_has_passed()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir, grace: TimeSpan.FromMilliseconds(200));

        MainWindowHost.RunBuild(vm, "A");
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // pay sürüyor (boşaltma zamanlayıcı ateşlemez)

        DispatcherPump.PumpUntil(() => collections.Count > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu
        GC.KeepAlive(window);
    }

    /// <summary>Görünür yolda her yeni istek koşan payı BAŞTAN kurar: pay en son sinyalden sayılır (tipik olarak bitiş finalinin
    /// sonundan — koşunun bitiş sinyali finalden önce gelir). Gözlem için ikinci istekten önce pay uzatılır: ilk payın kısa süresi
    /// yerinde kalsaydı toplama onun sonunda olurdu; yeniden kurulan pay test boyunca dolmaz.</summary>
    [StaFact]
    public void A_new_request_restarts_a_running_grace()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir, grace: TimeSpan.FromMilliseconds(200));
        MainWindowHost.RunBuild(vm, "A");                // pay kuruldu (200 ms)
        window.TrimGraceAfterFinale = TimeSpan.FromMinutes(10);
        window.RequestTrimWhenDue();                     // pay dolmadan yeni bir sinyal geldi (ör. finalin sonu)

        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(600));
        Assert.Equal(0, collections.Count);              // ilk payın süresi çoktan geçti: trim yeniden kurulan payın sonuna kaydı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Görünür pencerede bloklayan toplama bitiş finalinin ORTASINA düşmez: final oynarken toplanmaz, final bitince
    /// (<c>GraphView.EndFinaleFinished</c>) istenir. Koşunun bitiş sinyali finalden ÖNCE gelir (<c>MarkRunEnded</c>, ardından
    /// <c>Phase = Done</c> finali başlatır): ilk istek finalden önce zamanlanır, yürütme anında finali oynarken bulup vazgeçer ve
    /// işlemi tüketmez — "trim edildi" yalnız toplama gerçekten yürütülünce yazılır.
    /// </summary>
    [StaFact]
    public void A_visible_trim_waits_while_the_end_finale_plays_and_runs_after_it()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        var graph = window.Shell.GraphHost;
        graph.AnimationsEnabledProvider = () => true;    // final grafın kendi kapısını okur: gerçekten oynasın

        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.FinishBuild(vm, "A");
        Assert.True(graph.IsEndFinalePlaying);           // ön-koşul: final oynuyor
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // final oynarken toplanmaz

        graph.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1) + 1);   // final doğal yoldan biter
        Assert.False(graph.IsEndFinalePlaying);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu
        GC.KeepAlive(window);
    }

    /// <summary>Boşta ertelenen trim yürütülmeden yeni bir koşu başlarsa vazgeçilir ve işlem tüketilmez: toplama o koşunun
    /// sonunda, bir kez yapılır.</summary>
    [StaFact]
    public void A_trim_deferred_into_a_new_run_waits_for_that_run()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);

        MainWindowHost.RunBuild(vm, "A");
        MainWindowHost.StartBuild(vm, "A");              // boştaki yürütme koşmadan yeni koşu başladı
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);

        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu
        GC.KeepAlive(window);
    }

    /// <summary>"Uçuşta işlem" yalnız koşu değildir: boştaki yürütme koşmadan bir Sync başlarsa toplama onu da bekler ve Sync'in
    /// bitişinde yapılır.</summary>
    [StaFact]
    public void A_trim_deferred_into_a_sync_waits_for_that_sync()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);

        MainWindowHost.RunBuild(vm, "A");
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));   // boştaki yürütme koşmadan Sync başladı
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // Sync sürerken toplanmaz (yürütme anında yalnız koşu sorulsaydı toplardı)

        vm.OnEvent(MainWindowHost.SyncCompleted());
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu
        GC.KeepAlive(window);
    }

    /// <summary>
    /// İşlem SAYILMAYAN bir iş de trimi bekletir ve trim onun sonunda yapılır (spec §6.1: "arada yeni işlem başladıysa yapılmaz, o
    /// işlemin sonunda yapılır"). Boşta ertelenen yürütme iş uçuştayken vazgeçer; iş ise hiçbir işlem bitişi üretmeden düşer —
    /// reddedilen pull'a Sync zincirlenmez, reddedilen başlatmanın koşusu hiç başlamadı. Trimi yeniden isteyen, "uçuşta işlem var
    /// mı" sorusunun değişimidir (<c>RunViewModel.IsOperationInFlight</c>); iki iş ona iki ayrı yoldan iner: workspace bayrakları
    /// ve koşu kilidi.
    /// </summary>
    [StaTheory]
    [InlineData("pull")]        // reddedilen pull: zincirli Sync yok
    [InlineData("run start")]   // motor başlatmayı reddetti (runInProgress): koşu hiç başlamadı
    public void A_trim_skipped_for_a_job_that_ends_without_an_operation_end_runs_when_the_job_is_gone(string job)
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        MainWindowHost.AcceptSends(vm);                  // iş motora gider; cevabını test verir
        if (job == "pull") vm.OnEvent(MainWindowHost.SyncCompleted() with { Behind = 2 });   // "N behind" chip'i: pull yapılabilir
        MainWindowHost.RunBuild(vm, "A");                // biten işlem: yürütme boşta kuyrukta

        _ = job == "pull" ? vm.PullRepositoryCommand.ExecuteAsync(null) : vm.BuildCommand.ExecuteAsync(null);   // yürütmeden önce iş başladı
        Assert.True(vm.IsOperationInFlight);             // ön-koşul
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // iş uçuşta: yürütme vazgeçti

        vm.OnEvent(job == "pull"
            ? new PullCompletedEvent(Succeeded: false)
            : new ErrorEvent("runInProgress", "A run is already in progress — 'r1' was rejected."));
        Assert.False(vm.IsOperationInFlight);            // iş bitti — bir işlem bitişi olmadan
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: trim bir sonraki SAYILAN işlemin sonuna kayıyordu
        GC.KeepAlive(window);
    }

    /// <summary>Sync de bir işlemdir: bitişi (koşu olmadan) bir trim alır.</summary>
    [StaFact]
    public void A_sync_end_also_trims()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);

        _ = MainWindowHost.StartSync(vm, SyncMode.Manual);
        vm.OnEvent(MainWindowHost.SyncCompleted());
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural yalnız tepside biten koşudan sonra topluyordu
        GC.KeepAlive(window);
    }

    /// <summary>Görünürde biten işlemin trimi pay içinde beklerken pencere gizlenirse pay beklenmez: gizlide duraklama
    /// görünmez, toplama hemen (boşta) yapılır. Görünür biten koşunun göstergesi hiç çıkmaz; gizlenince de beklenmez.</summary>
    [StaFact]
    public void Hiding_the_window_trims_a_finished_operation_at_once()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir, grace: TimeSpan.FromMinutes(10));   // görünürde pay dolmayacak

        MainWindowHost.RunBuild(vm, "A");
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // pay sürüyor

        window.SetSurfaceHidden(true);                   // gizlendi: pay beklenmez
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde biten koşuyu hiç toplamıyordu
        GC.KeepAlive(window);
    }

    /// <summary>Gösterge yalnız TEPSİDE biten koşunundur: görünürde biten koşunun çıkış bildirimi hiç gelmez. Pencere sonra
    /// gizlenir ve tepside bir Sync biter — o Sync, gelmeyecek bir çıkışı beklemeden trim alır.</summary>
    [StaFact]
    public void A_hidden_sync_end_after_a_visibly_ended_run_does_not_wait_for_an_indicator()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        MainWindowHost.RunBuild(vm, "A");                // görünürde biter: göstergesi yoktur
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // ön-koşul: koşunun kendi trimi

        window.SetSurfaceHidden(true);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        vm.OnEvent(MainWindowHost.SyncCompleted());      // tepside biten Sync
        DispatcherPump.DrainToIdle();
        Assert.Equal(2, collections.Count);              // KIRMIZI: "son koşunun göstergesi çıkmadı" diye beklenirdi
        GC.KeepAlive(window);
    }

    /// <summary>Tepside biten koşunun göstergesi çıkışını bitirmeden pencere geri gelirse o çıkış trimin önünde beklenmez:
    /// görünür pencerede gösterge yoktur, trim görünür yoldan (finalden ve paydan sonra) yapılır.</summary>
    [StaFact]
    public void Showing_the_window_before_the_indicator_left_trims_on_the_visible_path()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, "A");                // tepside biter: gösterge çıkışı bekleniyor
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // ön-koşul: gizliyken çıkış beklenir

        window.SetSurfaceHidden(false);                  // çıkış gelmeden pencere döndü
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural yalnız gizliyken topluyordu
        GC.KeepAlive(window);
    }

    /// <summary>Gizliyken boşta kuyruğa alınan yürütme koşmadan pencere geri gelirse geri alınır ve trim görünür yoldan, dönüşten PAY
    /// kadar sonra yapılır: görünür yolda pay en son sinyalden sayılır — dönüş de bir sinyaldir — ve kuyruktaki yürütme payı
    /// atlayamaz.</summary>
    [StaFact]
    public void Showing_the_window_withdraws_a_trim_queued_while_hidden_and_waits_for_the_grace()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir, grace: TimeSpan.FromMilliseconds(200));
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, "A");                // tepside biter
        window.OnTrayIndicatorExitFinished(vm.RunSerial);   // gösterge çıktı: yürütme boşta kuyrukta (gizli yol: pay yok)

        window.SetSurfaceHidden(false);                  // yürütme koşmadan pencere döndü
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // kuyruktaki yürütme geri alındı: pay sürüyor

        DispatcherPump.PumpUntil(() => collections.Count > 0, TimeSpan.FromSeconds(5));
        Assert.Equal(1, collections.Count);              // pay dolunca bir kez
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Cevabı beklenen bir proje log yüklemesi trimi tutar: dikişin kuyruğu canlı tampondadır ve yükleme sürerken toplanmaz.
    /// Yüklemenin sonu — dikiş ya da motorun "log yok" yanıtı — trimi yeniden ister (Review Focus 2: bekleyen yükleme yüzünden
    /// ertelenen trim kaybolmaz).
    /// </summary>
    [StaTheory]
    [InlineData(true)]   // dikiş (ProjectLogChunkEvent)
    [InlineData(false)]  // "log yok" (logNotFound)
    public void A_pending_project_log_load_holds_the_trim_until_its_reply(bool stitched)
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        MainWindowHost.AcceptSends(vm);                  // yükleme isteği gerçekten gider: cevap bekleniyor
        MainWindowHost.StartBuild(vm, "A");
        RequestProjectLog(vm, "A");
        Assert.True(vm.HasPendingLogLoad);               // ön-koşul

        MainWindowHost.CompleteRun(vm, 1);               // koşu yanıttan ÖNCE biter
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);              // yükleme bekliyor

        if (stitched) ReplyWithProjectLog(vm, "A", "on disk\n", through: 0);
        else vm.OnEvent(new ErrorEvent("logNotFound", "no log for A"));
        Assert.False(vm.HasPendingLogLoad);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: yüklemenin sonu trimi yeniden istemezse işlem trimsiz kalır
        GC.KeepAlive(window);
    }

    /// <summary>Gönderimi düşen bir yükleme bilerek silahlı kalır — gecikmiş bir chunk hâlâ dikilir — ama trimi TUTMAZ: cevap
    /// hiç gelmeyebilir ve tek bir düşen gönderim, başka bir log açılana dek her trimi kilitlerdi.</summary>
    [StaFact]
    public void A_project_log_load_whose_send_failed_does_not_hold_the_trim()
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        string a = MainWindowHost.IdOf("A");
        MainWindowHost.StartBuild(vm, "A");
        RequestProjectLog(vm, "A");                      // varsayılan host: gönderim senkron düşer
        Assert.False(vm.HasPendingLogLoad);              // cevap beklenmiyor

        MainWindowHost.CompleteRun(vm, 1);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, collections.Count);              // KIRMIZI: eski kural görünürde hiç toplamıyordu

        ReplyWithProjectLog(vm, "A", "on disk\n", through: 0);   // yine de gecikmiş bir yanıt dikilir: yükleme silahlıydı
        Assert.Equal("on disk\n", vm.GetProjectDocumentText(a));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Motor giderse — çöktü ya da kullanıcı Restart'a bastı — motora gitmiş bir proje log isteğinin cevabı da gelmez. Yükleme,
    /// gönderimi düşen yükleme gibi silahlı kalır (motorun ölmeden yazdığı gecikmiş bir chunk hâlâ dikilir) ama trimi artık
    /// TUTMAZ: tutsaydı başka bir log açılana dek her işlem sonu trimini — yeniden başlatmanın Sync'i dahil — kilitlerdi.
    /// </summary>
    [StaTheory]
    [InlineData(false)]  // motor öldü
    [InlineData(true)]   // Restart: var olmayan exe ile yeniden başlatma düşer, eski motorun bırakma yolu yine koşar
    public async Task A_project_log_load_whose_engine_is_gone_does_not_hold_the_trim(bool restart)
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir);
        string a = MainWindowHost.IdOf("A");
        MainWindowHost.AcceptSends(vm);                  // istek motora gitti: cevap bekleniyor
        MainWindowHost.StartBuild(vm, "A");
        RequestProjectLog(vm, "A");
        Assert.True(vm.HasPendingLogLoad);               // ön-koşul

        if (restart) await vm.RestartEngineCommand.ExecuteAsync(null);
        else vm.OnEngineExited(1);
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, collections.Count);              // KIRMIZI: cevabı gelmeyecek yükleme trimi sonsuza dek tutuyordu
        Assert.False(vm.HasPendingLogLoad);
        ReplyWithProjectLog(vm, "A", "on disk\n", through: 0);   // yine de gecikmiş bir yanıt dikilir: yükleme silahlıydı
        Assert.Equal("on disk\n", vm.GetProjectDocumentText(a));
        GC.KeepAlive(window);
    }

    /// <summary>Pencere kapanınca bekleyen trim düşer: koşan pay durur, boşta kuyruktaki yürütme geri alınır — kapanan pencere
    /// toplamaz. Kapanışı gerçek yapan çıkıştır: tepsi → Exit (uçuşta iş yok, çıkış hemen hazır), ardından gelen kapanış pencereyi
    /// kapatır (<c>CloseToTrayTests</c> deseni; uygulamayı kapatan ve pencereyi öne getiren dikişler sahtedir).</summary>
    [StaTheory]
    [InlineData(0)]     // pay yok: yürütme boşta kuyrukta
    [InlineData(200)]   // pay sürüyor
    public void Closing_the_window_drops_a_pending_trim(int graceMs)
    {
        using var dir = new TempDir();
        var (window, vm, collections) = TrimHost(dir, grace: TimeSpan.FromMilliseconds(graceMs));
        window.ShutdownApplication = () => { };
        window.BringForward = () => { };
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        MainWindowHost.RunBuild(vm, "A");                // trim bekliyor

        window.ExitFromTray();
        window.Close();
        Assert.True(closed);                             // ön-koşul: pencere gerçekten kapandı

        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(graceMs + 300));
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, collections.Count);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz C · C-1] Üretim toplayıcısı (gerçek <c>GC.Collect</c>, bir kez) fırlatmaz. Agresif kip yalnız bloklayan biçimle
    /// geçerlidir; <c>blocking: false</c> her çağrıda <see cref="ArgumentException"/> fırlatıyordu ve süit bunu göremedi — diğer
    /// testler toplayıcıyı sayaçlı bir sahteyle değiştirir. Süitte gerçek toplama yalnız burada koşar.
    /// <para><b>Kurulum değişti, iddia değişmedi:</b> eskiden fixture penceresinin varsayılanı üretim toplayıcısıydı. İşlem sonu
    /// trimi her işlem bitişinde (görünürde de) toplamaya başlayınca fixture her pencereye boş bir toplayıcı takar — yoksa süitin
    /// her işlem bitişi gerçek agresif GC koştururdu. Üretim toplayıcısını açıkça isteyen tek test budur.</para>
    /// </summary>
    [StaFact]
    public void The_production_memory_collector_does_not_throw()
    {
        using var dir = new TempDir();
        var (window, _) = MainWindowHost.New(dir, productionMemoryCollector: true);

        var error = Record.Exception(window.MemoryCollector);   // üretim varsayılanı: sahte takılmadı

        Assert.Null(error);   // KIRMIZI: GC.Collect(2, Aggressive, blocking: false, compacting: true) → ArgumentException
        GC.KeepAlive(window);
    }

    /// <summary>Tepside süren bir koşu: pencere gizli, A'nın bir satırı canlı tamponda. Toplayıcı sahtedir ve çağrıldığı andaki
    /// canlı tampon boyunu kaydeder — "bırakma → toplama" sırası böyle gözlenir.</summary>
    private static (global::BuildOrchestrator.App.MainWindow window, RunViewModel vm, List<int> seen) HiddenRunWithOneLine(TempDir dir)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var seen = new List<int>();
        window.MemoryCollector = () => seen.Add(vm.LiveLineCount);
        window.SetSurfaceHidden(true);
        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "a line");
        Assert.Equal(1, vm.LiveLineCount);   // ön-koşul: koşu sürerken tampon dolu
        return (window, vm, seen);
    }

    /// <summary>
    /// [perf Faz C · M-2] Toplama iki sinyalin birleşimidir — koşu bitti VE gösterge çıkışını bitirdi — ve hangisi sonra gelirse
    /// toplamayı o ister. Reduced-motion'da tepside Stop'ta gösterge <c>runStopped</c> anında çıkar (faz <c>Stopped</c>) ama koşu
    /// <c>runCompleted</c>'a dek uçuştadır: bitiş sinyali sonra gelir ve toplamayı o ister — bir kez, bırakılmış tamponla.
    /// </summary>
    [StaFact]
    public void A_run_stopped_in_the_tray_under_reduced_motion_is_collected_once_when_it_completes()
    {
        using var dir = new TempDir();
        var (window, vm, seen) = HiddenRunWithOneLine(dir);

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));   // faz Stopped: reduced-motion'da gösterge çıkışı ŞİMDİ biter
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();
        Assert.Empty(seen);                                       // koşu hâlâ uçuşta: runCompleted gelmedi

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 100));
        DispatcherPump.DrainToIdle();

        Assert.Equal([0], seen);   // KIRMIZI: runCompleted toplamayı yeniden sormuyordu — o koşu hiç toplanmıyordu
        GC.KeepAlive(window);
    }

    /// <summary>[perf Faz C · C4] Koşu-bitiren hata (<c>runFailed</c>) da bir koşu bitişidir: <c>runCompleted</c> gelmez, tampon yine
    /// bırakılır ve tepside toplama bırakılmış tamponla olur.</summary>
    [StaFact]
    public void A_run_that_fails_in_the_tray_releases_its_buffer_before_the_collection()
    {
        using var dir = new TempDir();
        var (window, vm, seen) = HiddenRunWithOneLine(dir);

        vm.OnEvent(new ErrorEvent("runFailed", "the build failed"));
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();

        Assert.Equal(0, vm.LiveLineCount);   // KIRMIZI: hata bitişinde tampon bırakılmıyordu
        Assert.Equal([0], seen);             // bir kez ve bırakılmış tamponla
        GC.KeepAlive(window);
    }

    /// <summary>[perf Faz C · C4] Motor kaybı da bir koşu bitişidir: tampon bırakılır ve tepside toplama bırakılmış tamponla
    /// olur.</summary>
    [StaFact]
    public void A_run_whose_engine_dies_in_the_tray_releases_its_buffer_before_the_collection()
    {
        using var dir = new TempDir();
        var (window, vm, seen) = HiddenRunWithOneLine(dir);

        vm.OnEngineExited(1);
        window.OnTrayIndicatorExitFinished(vm.RunSerial);
        DispatcherPump.DrainToIdle();

        Assert.Equal(0, vm.LiveLineCount);   // KIRMIZI: motor kaybında tampon bırakılmıyordu
        Assert.Equal([0], seen);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz C · M-1] Bekleyen yüklemenin "log yok" yanıtı (<c>logNotFound</c>) da yüklemenin sonudur: koşu yanıttan önce
    /// bittiyse ertelenen bırakma yanıtla yapılır — dikiş yanıtıyla aynı kural (bekleyen yüklemenin sonu tek yerde).
    /// </summary>
    [StaFact]
    public void A_log_not_found_reply_releases_the_buffer_of_a_run_that_ended_while_the_log_was_loading()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "a line");
        RequestProjectLog(vm, "A");                      // yükleme istendi, yanıt henüz yok
        MainWindowHost.CompleteRun(vm, 1);               // yanıttan ÖNCE koşu biter
        Assert.Equal(1, vm.LiveLineCount);               // ön-koşul: bırakma yanıtı bekliyor

        vm.OnEvent(new ErrorEvent("logNotFound", "no log for A"));

        Assert.Equal(0, vm.LiveLineCount);   // KIRMIZI: logNotFound yanıtı ertelenen bırakmayı sormuyordu
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz C · C4] Konsol anlatıya dönünce (<c>ShowRun</c>) açık projenin tamponu da düşer — proje→proje geçişiyle aynı kural:
    /// bırakma anı gösterimin ayrıldığı andır; yeniden açılış diskten yükler.
    /// </summary>
    [StaFact]
    public void Returning_the_console_to_the_narrative_drops_the_project_buffer()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        string a = MainWindowHost.IdOf("A");
        OpenProjectLog(vm, "A", "a on disk\n", through: 0);
        Assert.Equal(a, vm.ActiveProjectId);                        // ön-koşul: A'nın sayfası ekranda
        Assert.Equal("a on disk\n", vm.GetProjectDocumentText(a));  // ön-koşul: tampon dolu
        Assert.Equal(1, vm.GetProjectLineCount(a));

        vm.ShowRun();

        Assert.Null(vm.ActiveProjectId);
        Assert.Equal("", vm.GetProjectDocumentText(a));
        Assert.Equal(0, vm.GetProjectLineCount(a));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A2'den ertelenen] Gizli konsol yetişmesinin PROJE LOGU kolu: pencere gizliyken bir projenin logu açıkken
    /// satırlar gelir ve koşu biter; dönüşte proje belgesi <b>tilt'siz</b> ve <b>kayıpsız</b> modelin tam metninden bir kez
    /// kurulur. Faz C'de koşu sonu canlı tamponu bırakır — açık projenin metin tamponu (<c>_projectText</c>) bundan etkilenmez
    /// ve dönüş kurulumunun kaynağı odur; bu test o bağı da korur.
    /// </summary>
    [StaFact]
    public void A_project_log_open_while_a_run_ends_hidden_is_rebuilt_whole_and_without_the_tilt_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        console.AnimationsEnabledProvider = () => true;                  // tilt oynayabilsin: tek engel gizli sinyal olsun
        var row = MainWindowHost.ProjectOf(vm, "A");
        vm.ActiveProjectId = row.Id;                                     // proje logu açık (motor round-trip'i yok: mod doğrudan kurulur)
        var documentBefore = console.EditorControl.Document;
        string textBefore = documentBefore.Text;
        window.SetSurfaceHidden(true);

        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "first");
        window.AppendConsoleBatch("first\n", window.ConsoleReseedGen);   // pompanın batch'i: gizliyken belgeye basılmaz, "ekran bayat" kalkar
        MainWindowHost.LogLine(vm, "A", 2, "second");
        MainWindowHost.FinishBuild(vm, "A");                             // koşu gizliyken biter: canlı tampon bırakılır
        Assert.Equal(0, vm.LiveLineCount);                               // KIRMIZI: bırakma bugün yok (dönüş kurulumu ondan bağımsız)
        Assert.Equal(textBefore, console.EditorControl.Document.Text);   // gizliyken belgeye hiçbir satır basılmadı (yerinde ekleme de yok)

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();

        string text = console.EditorControl.Document.Text;
        Assert.NotSame(documentBefore, console.EditorControl.Document);  // proje belgesi dönüşte yeniden kuruldu
        Assert.Equal(1, MainWindowHost.Occurrences(text, "first"));      // kayıpsız ve çiftsiz: belge == model
        Assert.Equal(1, MainWindowHost.Occurrences(text, "second"));
        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
        Assert.Equal(Visibility.Collapsed, console.Tilt3D.Visibility);   // tilt'siz: tilt mod geçişinin işidir
        GC.KeepAlive(window);
    }
}
