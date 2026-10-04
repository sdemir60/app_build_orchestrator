using System.Windows;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz C · C4] <b>Biten koşunun tamponları bırakılır; tepside biten koşunun ardından bellek bir kez toplanır.</b>
/// OSYS büyüklüğünde bir koşu yüz binlerce satır nesnesini canlı satır tamponunda (<c>RunViewModel</c> <c>_liveLines</c>)
/// biriktirir; tampon yalnız koşu SÜRERKEN, bir proje logu açılırken diskteki snapshot'ın ötesindeki kuyruğu dikmek için
/// vardır. Koşu bitince disk tamdır ve tampon ölü yüktür; açık projenin metin tamponu da yalnız ekrandaki sayfa için
/// yaşar. Bu dosya üç şeyi pinler: (1) bırakma — koşu sonu, bekleyen dikişe saygı, proje değişimi; (2) bırakmanın yeni
/// işlemin temizliğini DEĞİŞTİRMEDİĞİ; (3) tepside biten koşunun ardından tek seferlik toplama (kapı: pencere gizli +
/// koşu bitti + gösterge çıkış evresini tamamladı). Ayrıca Faz A2'den ertelenen boşluğu kapatır: gizli konsol
/// yetişmesinin PROJE LOGU kolu.
///
/// <para><b>Test yüzeyleri:</b> motor yoktur (<c>MainWindowHost</c>): <c>LoadProjectLogAsync</c>'in gönderimi senkron düşer
/// ama dikiş silahlı kalır (gecikmiş bir chunk hâlâ eşleşir) ve motorun yanıtı (<c>ProjectLogChunkEvent</c>) testten
/// verilir. Tepsi göstergesi headless'ta kurulmaz; çıkış bildirimini test <c>MainWindow.OnTrayIndicatorExitFinished</c>'a
/// doğrudan verir (<c>OnGlobalHotkey</c> deseni) ve gerçek <c>GC.Collect</c> yerine sayaçlı bir sahte takar
/// (<c>MainWindow.MemoryCollector</c>) — süit gerçek toplama tetiklemez.</para>
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
        RequestProjectLog(vm, "A");
        MainWindowHost.CompleteRun(vm, 1);               // dikiş bekliyor: canlı tampon yerinde
        Assert.Equal(1, vm.LiveLineCount);               // ön-koşul: temizlenecek canlı satır var
        Assert.Contains("old line", vm.GetRunDocumentText());
        int cleared = 0; vm.ConsoleCleared += (_, _) => cleared++;

        _ = vm.BuildCommand.ExecuteAsync(null);          // yeni işlem: temizlik ilk await'ten ÖNCE, senkron

        Assert.Equal(0, vm.LiveLineCount);
        Assert.DoesNotContain("old line", vm.GetRunDocumentText());
        Assert.Equal("", vm.GetProjectDocumentText(a));
        Assert.True(cleared > 0, "ConsoleCleared bildirilmeli: kabuk ekrandaki belgeyi de boşaltır");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Gizli + koşu bitti + gösterge çıkış evresini tamamladı → bellek BİR kez toplanır. Koşu sürerken gelen bildirim
    /// (yeni bir koşu başlamış olabilir) ve aynı koşunun ikinci bitiş sinyali toplamaz.
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

        window.OnTrayIndicatorExitFinished();            // koşu sürerken: toplanacak bir şey yok
        Assert.Equal(0, collections);

        MainWindowHost.FinishBuild(vm, "A");
        window.OnTrayIndicatorExitFinished();
        Assert.Equal(1, collections);                    // KIRMIZI: bugün hiçbir şey toplamıyor

        window.OnTrayIndicatorExitFinished();            // aynı koşunun ikinci bitiş sinyali
        Assert.Equal(1, collections);
        GC.KeepAlive(window);
    }

    /// <summary>Bayrak koşu BAŞINDA sıfırlanır: her koşunun bitişi kendi toplamasını alır.</summary>
    [StaFact]
    public void Each_run_that_ends_while_hidden_gets_its_own_collection()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int collections = 0;
        window.MemoryCollector = () => collections++;
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, "A");
        window.OnTrayIndicatorExitFinished();
        Assert.Equal(1, collections);                    // KIRMIZI: bugün hiçbir şey toplamıyor

        MainWindowHost.RunBuild(vm, "A");                // ikinci koşu: başlangıç bayrağı sıfırlar
        window.OnTrayIndicatorExitFinished();
        Assert.Equal(2, collections);
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: pencere görünürken (çıkış sırasında geri gelmiş olabilir) hiç toplanmaz — kullanıcı ekrandadır. Aynı
    /// bildirim gizliyken toplar: yani sıfır, sayacın ölü olmasından değil görünürlükten gelir.</summary>
    [StaFact]
    public void A_visible_window_does_not_collect_after_a_run()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int collections = 0;
        window.MemoryCollector = () => collections++;
        MainWindowHost.RunBuild(vm, "A");

        window.OnTrayIndicatorExitFinished();
        Assert.Equal(0, collections);                    // pencere görünür

        window.SetSurfaceHidden(true);
        window.OnTrayIndicatorExitFinished();
        Assert.Equal(1, collections);                    // aynı bildirim gizliyken toplar
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
        window.SetSurfaceHidden(true);

        MainWindowHost.StartBuild(vm, "A");
        MainWindowHost.LogLine(vm, "A", 1, "first");
        window.AppendConsoleBatch("first\n", window.ConsoleReseedGen);   // pompanın batch'i: gizliyken belgeye basılmaz, "ekran bayat" kalkar
        MainWindowHost.LogLine(vm, "A", 2, "second");
        MainWindowHost.FinishBuild(vm, "A");                             // koşu gizliyken biter: canlı tampon bırakılır
        Assert.Equal(0, vm.LiveLineCount);                               // KIRMIZI: bırakma bugün yok (dönüş kurulumu ondan bağımsız)
        Assert.Same(documentBefore, console.EditorControl.Document);     // gizliyken belgeye dokunulmadı

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
