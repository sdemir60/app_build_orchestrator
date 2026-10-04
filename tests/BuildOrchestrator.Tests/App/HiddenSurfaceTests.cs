using System.Diagnostics;
using System.Text;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz A · A1] <b>Gizli pencerede koreografi yok.</b> Pencere tepsideyken kısayolla başlatılan bir
/// derlemede motor başlamadan önceki bekleyişin büyük kısmı kimsenin göremediği açılış koreografisiydi ve gizli
/// pencere ekran işini sürdürüyordu. "Yüzey gizli" sinyali TEK yerdedir (<see cref="HiddenSurface"/>); bu dosya
/// onu kullanan yüzeyleri pinler: açılış koreografisi (kapsam tek adımda işaretlenir, komut hemen gider), adım
/// bekletmesi (beklemez), bitiş finali (hiç oynamaz, oynuyorsa kesilir, filtre askısı hemen döner) ve dönüş
/// kurulumunun gizle-göster-gizle sırası. Koşuyu süren olay sürücüleri <c>MainWindowHost</c>'tadır
/// (<c>PreviewBuild</c> → <c>StartBuild</c> → <c>FinishBuild</c>).
///
/// <para><b>Test yüzeyleri:</b> <c>MainWindow.SetSurfaceHidden</c> sinyali doğrudan yazar — <c>MainWindowHost</c>
/// pencereyi hiç <c>Show()</c> etmez, <c>IsVisibleChanged</c> ateşlenmez (<c>OnGlobalHotkey</c>'in internal test
/// yüzeyiyle aynı gerekçe). <c>MainWindow.AnimationsForTest</c> koreografinin motion girdisini ÖRNEK BAŞINA
/// zorlar: headless'ta <c>App.Motion</c> null'dur (= reduced-motion) ve koreografi hiç oynamaz; statik bir
/// <c>MotionScope</c> paralel koşan test sınıflarına sızardı. Bitiş finali grafın KENDİ kapısını okur
/// (<c>GraphHost.AnimationsEnabledProvider</c>) — seam oraya ulaşmaz, final testleri onu ayrıca açar.</para>
///
/// <para><b>Kapsam şartı:</b> koreografi boş kapsamda hiç oynamaz (<c>OperationChoreographer.Play</c>) ve
/// <c>ScopeFor(Build)</c> kapsamı motorun planından (<c>BuildPreviewEvent</c> → <c>WillBuild</c>) türer.
/// Plansız bir fixture'da kapsam boştur: koreografi görünür pencerede bile oynamaz ve gizli-sinyal testleri
/// sahte-yeşil kalır. Bu yüzden koreografi testleri planı verir ve kapsamın dolu olduğunu ön-koşul olarak sınar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HiddenSurfaceTests
{
    [StaFact]
    public void A_run_started_while_the_surface_is_hidden_sends_the_command_without_playing_the_choreography()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
        MainWindowHost.PreviewBuild(vm, "A", "B", "C");
        Assert.Equal(3, vm.ScopeFor(RunMode.Build).Count); // ön-koşul: kapsam dolu — boş kapsamda koreografi zaten oynamaz
        window.AnimationsForTest = true; // koreografi oynayabilsin: tek engel gizli sinyal olsun
        IpcCommand? sent = null; vm.DebugSendOverride = cmd => { sent = cmd; return Task.CompletedTask; };
        window.SetSurfaceHidden(true);
        var sw = Stopwatch.StartNew();
        _ = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.PumpUntil(() => sent is not null, TimeSpan.FromSeconds(2));
        Assert.IsType<StartRunCommand>(sent);
        Assert.True(sw.ElapsedMilliseconds < MarkingChoreography.NeutralMs, "komut koreografiyi beklemeden gitmeli");
        Assert.All(vm.ScopeFor(RunMode.Build), r => Assert.True(r.Marked)); // azaltılmış-hareket dalı: kapsam tek adımda işaretlenir
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_run_that_ends_while_the_surface_is_hidden_plays_no_end_finale()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
        window.Shell.GraphHost.AnimationsEnabledProvider = () => true; // final grafın kendi kapısını okur: oynayabilsin
        window.SetSurfaceHidden(true);

        MainWindowHost.StartBuild(vm, "A", "B", "C");
        MainWindowHost.FinishBuild(vm, "A", "B", "C");

        Assert.False(window.Shell.GraphHost.IsEndFinalePlaying);
        Assert.False(window.Shell.GraphHost.IsFilterSuspended);
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI akış görünür pencerede finali oynatır — yani yukarıdaki test finalin hiç
    /// oynayamamasından değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void A_run_that_ends_while_the_surface_is_visible_still_plays_the_end_finale()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
        window.Shell.GraphHost.AnimationsEnabledProvider = () => true;

        MainWindowHost.StartBuild(vm, "A", "B", "C");
        MainWindowHost.FinishBuild(vm, "A", "B", "C");

        Assert.True(window.Shell.GraphHost.IsEndFinalePlaying);
        Assert.True(window.Shell.GraphHost.IsFilterSuspended); // final filtresiz oynar, dönüşü kendi son adımıdır
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Gizleme OYNAYAN finali keser ve filtreyi geri verir: final filtresiz oynar ve filtrenin dönüşü finalin KENDİ son
    /// adımıdır — adım düşerse kimse filtreyi geri getirmez ve pencere dönünce graf filtresiz asılı kalırdı. (Koşu
    /// ORTASINDA gizleme finali kesmez, çünkü ortada oynayan final yoktur: bkz. aşağıdaki koşu-ortası testi.)
    /// </summary>
    [StaFact]
    public void Hiding_the_surface_while_the_end_finale_plays_stops_it_and_returns_the_filter()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
        window.Shell.GraphHost.AnimationsEnabledProvider = () => true; // final oynayabilsin
        MainWindowHost.StartBuild(vm, "A", "B", "C");
        MainWindowHost.FinishBuild(vm, "A", "B", "C");
        Assert.True(window.Shell.GraphHost.IsEndFinalePlaying); // ön-koşul: pencere görünürken final oynuyor
        Assert.True(window.Shell.GraphHost.IsFilterSuspended);  // ve filtre askıda (dönüşü finalin son adımı)

        window.SetSurfaceHidden(true);

        Assert.False(window.Shell.GraphHost.IsEndFinalePlaying);
        Assert.False(window.Shell.GraphHost.IsFilterSuspended); // askı, düşen adıma bırakılmadı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Hiding_the_surface_mid_choreography_releases_the_command_at_once()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
        MainWindowHost.PreviewBuild(vm, "A", "B", "C");
        Assert.Equal(3, vm.ScopeFor(RunMode.Build).Count); // ön-koşul: kapsam dolu
        window.AnimationsForTest = true;
        IpcCommand? sent = null; vm.DebugSendOverride = cmd => { sent = cmd; return Task.CompletedTask; };

        _ = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(150)); // pencere görünür: koreografi oynuyor, komut bekler
        Assert.Null(sent);

        window.SetSurfaceHidden(true);
        DispatcherPump.PumpUntil(() => sent is not null, TimeSpan.FromMilliseconds(500));

        Assert.IsType<StartRunCommand>(sent);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Adımlar arası bekletme (<c>RunViewModel.OperationHold</c> — Clean/Optimize adımı, Settings import) gizli
    /// pencerede beklemez: kimsenin görmediği bir adımı sabit süre tutmak sıradaki işi boşuna geciktirir. Bekletmenin
    /// kapısı koreografinin kapısıyla AYNI delegedir (<c>ChoreographyMayPlay</c>) ama AYRI bir alana
    /// (<c>_stepHold</c>) bağlanır — koreografi testleri o bağı görmez; bağ kopsa tepsideki Clean→Sync zinciri her adımda
    /// görünmez bir bekleme yerdi.
    /// </summary>
    [StaFact]
    public void The_step_hold_between_operations_is_skipped_while_the_surface_is_hidden()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        window.AnimationsForTest = true; // bekletme bekleyebilsin: tek engel gizli sinyal olsun

        var visibleHold = vm.OperationHold!(300);
        Assert.False(visibleHold.IsCompleted); // ön-koşul: pencere görünürken bekletme gerçekten bekler

        window.SetSurfaceHidden(true);
        var hiddenHold = vm.OperationHold!(300);

        Assert.True(hiddenHold.IsCompleted);   // gizliyken adım anında biter
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Gizlemek koşuyu bitirmez: grafın filtre askısı <c>BeginOperation</c>'dan koşunun bitişine dek sürer ve
    /// "graf koşu boyunca filtreyi yok sayar" kuralı pencere gizlenip geri gelse de geçerlidir. Gizleme yalnız
    /// OYNAYAN finali keser (<c>CancelEndFinale</c> askıyı da kaldırır) — koşu sürerken çağrılsaydı pencere koşu
    /// ortasında filtrelenmiş grafla dönerdi.
    /// </summary>
    [StaFact]
    public async Task Hiding_the_surface_mid_run_keeps_the_graph_filter_suspended_until_the_run_ends()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        MainWindowHost.AcceptSends(vm);
        await vm.BuildCommand.ExecuteAsync(null); // işlem başladı: graf filtreyi askıya aldı
        MainWindowHost.StartBuild(vm, "A", "B");
        Assert.True(window.Shell.GraphHost.IsFilterSuspended); // ön-koşul: koşu sürerken askıda

        window.SetSurfaceHidden(true);
        Assert.True(window.Shell.GraphHost.IsFilterSuspended); // gizlemek koşuyu bitirmez

        MainWindowHost.FinishBuild(vm, "A", "B");
        Assert.False(window.Shell.GraphHost.IsFilterSuspended); // koşu bitti (final atlandı): filtre döner
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A2] <b>Gizli pencerede konsol belgesine yazılmaz.</b> Tepsideyken derlenen bir koşunun satırları
    /// kimsenin görmediği AvalonEdit belgesine basılıyor ve her basış görünmeyen bir düzen işini zorluyordu. Metin VM
    /// tamponunda zaten durur (<c>OnProjectLog</c> run metnine yazar); pencere dönünce belge o TAM metinden BİR kez
    /// kurulur. Pompa bu fixture'da hiç tick etmez (<see cref="MainWindowHost.NeverTickingBatcher"/>): batch
    /// üretimdeki hedefe (<c>MainWindow.AppendConsoleBatch</c>) pompanın yaptığı gibi test tarafından verilir.
    /// </summary>
    [StaFact]
    public void Console_batches_are_not_applied_while_hidden_and_the_document_is_rebuilt_once_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        window.SetSurfaceHidden(true);
        int docChanges = 0; console.EditorControl.Document.Changed += (_, _) => docChanges++;
        for (int i = 0; i < 200; i++) MainWindowHost.LogLine(vm, "A", i + 1, $"line {i}");
        // pompa hiç tick etmez (NeverTickingBatcher) — batch'i üretimdeki hedefe test verir (internal test yüzeyi):
        window.AppendConsoleBatch(string.Join("", Enumerable.Range(0, 200).Select(i => $"line {i}\n")), window.ConsoleReseedGen);
        Assert.Equal(0, docChanges);                     // KIRMIZI: bugün batch gizli belgeye basılır
        window.SetSurfaceHidden(false);
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount == 1, TimeSpan.FromSeconds(2)); // internal sayaç: ReplaceRunDocument çağrıları
        Assert.EndsWith("line 199", console.EditorControl.Document.Text.TrimEnd());
        Assert.Equal(1, console.RunDocumentReplacedCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Boşta açılışta overlay'de "ready" durur ve belgeye gelen İLK anlatı satırı onu siler
    /// (<c>AppendNarrativeBatch</c> → <c>ClearReadyText</c>). Gizliyken batch'ler belgeye hiç girmediği için o yol
    /// çalışmaz; dönüşte belgeyi tam metinle kuran yol "ready"yi kendisi silmezse tepsiden başlatılan İLK derleme dolu
    /// bir konsolun altında "ready" gösterirdi.
    /// </summary>
    [StaFact]
    public void The_rebuild_on_show_removes_the_idle_ready_prompt_once_the_document_has_lines()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        console.ShowReady();
        Assert.Equal(ConsoleEmptyState.Idle, console.ActiveLineText.Text); // ön-koşul: boşta "ready" görünüyor
        window.SetSurfaceHidden(true);
        MainWindowHost.LogLine(vm, "A", 1, "first line");
        window.AppendConsoleBatch("first line\n", window.ConsoleReseedGen);

        window.SetSurfaceHidden(false);
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(1, console.RunDocumentReplacedCount);                   // ön-koşul: belge dönüşte kuruldu
        Assert.EndsWith("first line", console.EditorControl.Document.Text.TrimEnd());
        Assert.Equal("", console.ActiveLineText.Text);                       // içerik varken "ready" görünmez
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Yeni işlem konsolu temizler (<c>ConsoleCleared</c> → <c>ClearRunDocument</c>); gizli pencerede belgeye
    /// dokunulmaz — temizlik de dönüşteki tek kurulumun işidir ve belge modelin o anki hâlinden kurulur.
    /// </summary>
    [StaFact]
    public void A_console_clear_while_hidden_leaves_the_document_alone_until_the_rebuild_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.AcceptSends(vm);
        var console = window.Shell.ConsoleViewControl;
        console.ShowRunDocument("Build succeeded\n"); // önceki işlemin satırı ekranda (OperationConsoleClearTests deseni)
        Assert.Contains("Build succeeded", console.EditorControl.Document.Text); // ön-koşul
        window.SetSurfaceHidden(true);
        int replacedBefore = console.RunDocumentReplacedCount;

        _ = vm.BuildCommand.ExecuteAsync(null); // temizlik ilk await'ten ÖNCE, senkron

        Assert.Contains("Build succeeded", console.EditorControl.Document.Text); // gizliyken belgeye dokunulmadı
        Assert.Equal(replacedBefore, console.RunDocumentReplacedCount);
        window.SetSurfaceHidden(false);
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount > replacedBefore, TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("Build succeeded", console.EditorControl.Document.Text); // dönüşte model neyse o: temiz
        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Göster → gizle, Loaded-öncelikli dönüş kurulumu koşmadan ÖNCE gelebilir (kullanıcı pencereyi hemen geri indirir).
    /// O koşu gizli bir ağaca kurmamalı ve "ekran bayat" bayraklarını silmemeli: bayrak erken silinirse sonraki GERÇEK
    /// gösterim bayat bir ekranla açılırdı (kendi kuyruğundaki kurulum bayrağı boş bulurdu).
    /// </summary>
    [StaFact]
    public void A_show_that_is_hidden_again_before_the_resync_runs_rebuilds_nothing_until_the_next_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        window.SetSurfaceHidden(true);
        MainWindowHost.LogLine(vm, "A", 1, "line 0");
        window.AppendConsoleBatch("line 0\n", window.ConsoleReseedGen); // gizliyken batch: konsol bayat işaretlenir
        int replacedBefore = console.RunDocumentReplacedCount;

        window.SetSurfaceHidden(false); // dönüş kurulumu Loaded önceliğiyle kuyrukta
        window.SetSurfaceHidden(true);  // pompa koşmadan tekrar gizlendi
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(replacedBefore, console.RunDocumentReplacedCount); // gizli ağaca kurulmadı

        window.SetSurfaceHidden(false);
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount > replacedBefore, TimeSpan.FromSeconds(2));

        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount); // bayrak korundu: gerçek gösterimde TAM BİR kurulum
        Assert.EndsWith("line 0", console.EditorControl.Document.Text.TrimEnd());
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · final · M2] <b>Gösterim ile dönüş kurulumu arasında gelen batch bayat belgeye basılmaz.</b> Dönüş
    /// kurulumu (<c>ResyncAfterShow</c>) Loaded önceliğiyle kuyruklanır; pompanın Normal öncelikli batch'i ondan ÖNCE
    /// koşabilir. O anda belge hâlâ gizlenmeden önceki hâlindedir: batch ona basılsaydı boşa giderdi (dönüş kurulumu belgeyi
    /// tam metinden yeniden kurar) ve belge kısa süre aradaki satırlar eksik kalırdı. Kapı bu yüzden yalnız pencerenin
    /// gizliliğine değil ekranın bayatlığına da bakar: bayrak kalkık kaldıkça batch yalnız modelde (<c>RunViewModel</c>
    /// tamponu) durur ve dönüş kurulumu hepsini TEK seferde yazar.
    /// </summary>
    [StaFact]
    public void A_console_batch_that_arrives_between_the_show_and_the_resync_is_left_for_the_single_rebuild()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        window.SetSurfaceHidden(true);
        MainWindowHost.LogLine(vm, "A", 1, "line 0");
        window.AppendConsoleBatch("line 0\n", window.ConsoleReseedGen);      // gizliyken batch: belge bayat işaretlendi
        int replacedBefore = console.RunDocumentReplacedCount;
        string textBefore = console.EditorControl.Document.Text;
        int docChanges = 0; console.EditorControl.Document.Changed += (_, _) => docChanges++;

        window.SetSurfaceHidden(false);                                      // dönüş kurulumu Loaded önceliğiyle kuyrukta: POMPALANMADI
        MainWindowHost.LogLine(vm, "A", 2, "line 1");
        window.AppendConsoleBatch("line 1\n", window.ConsoleReseedGen);      // pompanın batch'i kurulumdan ÖNCE gelir

        Assert.Equal(0, docChanges);                                         // KIRMIZI: bugün batch bayat belgeye basılır
        Assert.Equal(textBefore, console.EditorControl.Document.Text);
        Assert.Equal(replacedBefore, console.RunDocumentReplacedCount);

        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount > replacedBefore, TimeSpan.FromSeconds(2));

        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount);  // TEK yeniden kurulum
        string[] lines = console.EditorControl.Document.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Single(lines, l => l.EndsWith("line 0"));                     // belge == model: ne çift ne kayıp satır
        Assert.Single(lines, l => l.EndsWith("line 1"));
        Assert.True(Array.FindIndex(lines, l => l.EndsWith("line 0")) < Array.FindIndex(lines, l => l.EndsWith("line 1")));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · final · O1] Belgeyi modelin tam metninden kuran görünür bir yol bayrağı da düşürür. Gösterimden sonra
    /// ama dönüş kurulumundan önce bir mod geçişi (proje kartı kalkar → <c>ShowRunConsole</c>) belgeyi zaten kurar; bayrak
    /// yerinde kalsaydı dönüş kurulumu belgeyi tilt sürerken ikinci kez, tilt'siz kurardı.
    /// </summary>
    [StaFact]
    public void A_mode_switch_between_the_show_and_the_resync_leaves_the_resync_nothing_to_rebuild()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        vm.SelectProject(MainWindowHost.IdOf("A"));                          // proje kartı seçili: mod geçişinin kaynağı
        Assert.NotNull(vm.SelectedProjectId);                                // ön-koşul
        window.SetSurfaceHidden(true);
        MainWindowHost.LogLine(vm, "A", 1, "line 0");
        window.AppendConsoleBatch("line 0\n", window.ConsoleReseedGen);      // gizliyken batch: belge bayat işaretlendi
        window.SetSurfaceHidden(false);                                      // dönüş kurulumu kuyrukta: POMPALANMADI
        int replacedBefore = console.RunDocumentReplacedCount;

        vm.SelectProject(null);                                              // kart kalktı → ShowRunConsole: görünür TAM kurulum

        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount);  // ön-koşul: mod geçişi belgeyi kurdu
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200));              // Loaded-öncelikli dönüş kurulumu koşar

        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount);  // KIRMIZI: bugün dönüş belgeyi ikinci kez kurar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · final · O1] Proje belgesi için <see cref="A_mode_switch_between_the_show_and_the_resync_leaves_the_resync_nothing_to_rebuild"/>
    /// testinin eşi: gösterimden sonra ama dönüş kurulumundan önce bir proje kartı seçilir (<c>OnSelectedProjectChangedAsync</c>
    /// → <c>PlayCascade</c>); belge o anda kurulduğu için dönüş kurulumunun yapacağı bir şey kalmaz. Belge örneği
    /// (<c>ReplaceProjectDocument</c> her kurulumda yeni bir <c>TextDocument</c> koyar) ikinci kurulumun belirtisidir.
    /// </summary>
    [StaFact]
    public void A_project_log_opened_between_the_show_and_the_resync_leaves_the_resync_nothing_to_rebuild()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        var row = MainWindowHost.ProjectOf(vm, "A");
        vm.ActiveProjectId = row.Id;                                         // proje logu açık (motor round-trip'i yok: mod doğrudan kurulur)
        window.SetSurfaceHidden(true);
        window.AppendConsoleBatch("noise\n", window.ConsoleReseedGen);       // gizliyken batch: belge bayat işaretlendi
        window.SetSurfaceHidden(false);                                      // dönüş kurulumu kuyrukta: POMPALANMADI
        var documentBefore = console.EditorControl.Document;

        vm.SelectProject(row.Id);                                            // kart seçildi → görünür TAM kurulum (PlayCascade)

        var documentAfterSelect = console.EditorControl.Document;
        Assert.NotSame(documentBefore, documentAfterSelect);                 // ön-koşul: seçim yolu proje belgesini kurdu
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200));              // Loaded-öncelikli dönüş kurulumu koşar

        Assert.Same(documentAfterSelect, console.EditorControl.Document);    // KIRMIZI: bugün dönüş proje belgesini ikinci kez kurar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A3] <b>Gizli pencerede olay akışı satır kurmaz.</b> Tepsideyken derlenen bir koşunun her olayı
    /// kimsenin görmediği akışa satır ekliyor, sayacı yazıyor ve en yeni satırın daktilosunu başlatıyordu. Satırların
    /// kaynağı model (<c>RunViewModel.StreamEvents</c>; 150 kırpma kuralı da onda) zaten tam durur: görünüm gizliyken
    /// yalnız "ekran modelin gerisinde" bayrağını kaldırır, görününce satırları, sayacı ve aktif satırı modelden TEK
    /// geçişte kurar.
    ///
    /// <para>Test GERÇEK kabuğun kendi görünümünü sürer (<c>Shell.EventStreamControl</c>): sinyal pencereye yazılır ve
    /// kalıtımla torunlara iner — yani kapı ile kalıtım → <c>OnPropertyChanged</c> yolu uçtan uca sınanır. Dönüş
    /// kurulumu DP değişiminde eşzamanlıdır (pompa gerekmez). Atılan eski satırlar kendi öğe VM'lerinin
    /// <c>PropertyChanged</c>'ine abone kalmaz (<c>DataContext</c> kopar): her gösterimde birikecek sınırlı bir
    /// sızıntı olurdu.</para>
    /// </summary>
    [StaFact]
    public void Event_stream_rows_are_not_built_while_the_window_is_hidden_and_are_rebuilt_in_one_pass_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(40);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var view = window.Shell.EventStreamControl;
        var rowsBefore = view.Rows;
        string counterBefore = view.Counter.Text;
        var typingBefore = view.TypingRow;
        Assert.NotEmpty(rowsBefore); // ön-koşul: gizlenmeden önce kurulmuş satırlar var — dönüşte bırakılmaları sınanabilsin

        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);

        Assert.True(vm.StreamEvents.Count > rowsBefore.Count); // ön-koşul: model akışa satır üretti
        Assert.Equal(rowsBefore.Count, view.Rows.Count);       // KIRMIZI kapısız: bugün gizliyken de her olay satır kurar
        Assert.Same(typingBefore, view.TypingRow);             //                   ve en yeni satırın daktilosunu başlatır
        Assert.Equal(counterBefore, view.Counter.Text);        // sayaç da yazılmaz

        window.SetSurfaceHidden(false);

        Assert.Equal(vm.StreamEvents.Count, view.Rows.Count);  // tek geçişte modelden kuruldu
        Assert.Equal($"{vm.StreamEventCount} events", view.Counter.Text);
        Assert.All(rowsBefore, row => Assert.Null(row.DataContext)); // atılan satırlar öğe VM'lerinden ayrıldı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · final · O1] Olay akışı için <see cref="A_ribbon_that_is_rebound_while_hidden_is_not_rebuilt_a_second_time_on_show"/>
    /// testinin eşi: gizliyken bayrağı bir olay kaldırır, sonra görünümün DataContext'i yeniden bağlanır
    /// (<c>RebuildRows</c> tam kurulum: satırlar modelden kurulur). Bayrak yerinde kalsaydı dönüş satırları bir kez daha
    /// kurardı ve ilk kurulumu boşa çıkarırdı.
    /// </summary>
    [StaFact]
    public void An_event_stream_that_is_rebound_while_hidden_is_not_rebuilt_a_second_time_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(40);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var view = window.Shell.EventStreamControl;
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);                                  // gizliyken olaylar akar: akış bayat işaretlendi
        Assert.NotEmpty(vm.StreamEvents);                                    // ön-koşul: model akışa satır üretti
        view.DataContext = null;                                             // yeniden bağlama: DataContextChanged tam kurulumu (RebuildRows) koşar
        view.DataContext = vm;
        var rowsAfterRebind = view.Rows;
        Assert.Equal(vm.StreamEvents.Count, rowsAfterRebind.Count);          // ön-koşul: yeniden bağlama satırları modelden kurdu

        window.SetSurfaceHidden(false);

        Assert.Equal(rowsAfterRebind.Count, view.Rows.Count);
        Assert.All(view.Rows.Zip(rowsAfterRebind),                           // KIRMIZI: bayrak düşmediği için dönüş satırları bir kez daha kurar
            pair => Assert.Same(pair.Second, pair.First));
        Assert.All(rowsAfterRebind, row => Assert.NotNull(row.DataContext)); // ilk kurulum boşa çıkarılmadı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A3] Dönüşte kurulan satırlar GEÇMİŞTİR. Gizliyken akan olaylar hiçbir görünümde oynanmadı
    /// (<c>GlowPlayed</c> / <c>TypePlayed</c> false); dönüş kurulumu onları işaretlemezse her yeşil "done" satırı
    /// yüklenirken 1,1 sn'lik parıltısını başlatır (en çok 150 satır aynı anda) ve daktilo geriye dönük oynayabilirdi —
    /// karar 15: tepsideyken animasyon yok, pencere gelince ekran tek seferde kurulur.
    ///
    /// <para>İddialar belirtiye değil NEDENE pinlenir: satırın yazımı <c>TypePlayed</c>'e, parıltısı <c>GlowPlayed</c>'e
    /// bakar. Belirtiyi (<c>IsTyping</c>) sınamak bu fixture'da sahte-yeşil olurdu: olay patlamasında (fırtına penceresi)
    /// satırlar anında basılır, yazılabilir satır üretmek zaman ayrımlı bir fixture ister
    /// (<c>EventStreamTypingTests.WrittenRow</c>) ve satırlar görünürken kabuğun kendi görünümü onları zaten erken
    /// "oynandı" işaretler. Bayraklar ise gizliyken akan her satırda dönüşten ÖNCE false'tur — iddia gerçekten düşebilir.</para>
    /// </summary>
    [StaFact]
    public void Rows_rebuilt_on_show_are_history_and_never_replay_the_glow_or_the_typewriter()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(40);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);
        Assert.Contains(vm.StreamEvents, e => e.GlowEligible);                  // ön-koşul: parıldayacak (done + hatasız) satır var
        Assert.Contains(vm.StreamEvents, e => !e.GlowPlayed && !e.TypePlayed);  // ön-koşul: gizliyken akanlar hiçbir görünümde oynanmadı

        window.SetSurfaceHidden(false);

        Assert.All(vm.StreamEvents, e => Assert.True(e.GlowPlayed)); // KIRMIZI: bugün dönüşte kurulan her yeşil satır parıldar
        Assert.All(vm.StreamEvents, e => Assert.True(e.TypePlayed)); //          ve yazılabilir satır geriye dönük yazılır
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A4] <b>Gizli pencerede grafa statü, faz ve seçim itilmez.</b> Tepsideyken derlenen bir koşunun her
    /// proje olayı (ve 200 ms'lik tik) grafın tüm düğümlerinin stilini yeniden hesaplatıyordu. Kaynak model zaten tam
    /// durur: gizliyken üç itiş (<c>PushGraphStatuses</c>, <c>PushGraphRunPhase</c>, <c>PushGraphSelection</c>) yalnız
    /// "graf bayat" bayrağını kaldırır; dönüşte <c>ResyncAfterShow</c> üçünü TEK seferde iter. Topoloji
    /// (<c>RebuildGraph</c>) bu kapının dışındadır: grafın kendi <c>Visibility</c> bekletmesi vardır.
    ///
    /// <para>Dönüş kurulumu burada pompa beklenmeden doğrudan koşturulur: koşu sürerken 200 ms'lik tik her turda statü
    /// iter ve "tam bir itiş" sayımını pompanın zamanlamasına bağlardı. <c>SetSurfaceHidden(false)</c>'ın kurulumu
    /// Loaded önceliğiyle kuyruğa aldığı kablaj yukarıdaki konsol testlerinde pinlidir; bayrak sıfırlandığı için
    /// kuyruktaki çağrı ikinci kez itmez.</para>
    /// </summary>
    [StaFact]
    public void Graph_pushes_are_skipped_while_hidden_and_one_sync_brings_the_graph_up_to_date_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(MainWindowHost.OsysProjectCount);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var graph = window.Shell.GraphHost;
        window.SetSurfaceHidden(true);
        int pushesBefore = graph.UpdateStatusesCallCount;

        MainWindowHost.StartBuild(vm, names);    // koşu fazı: model Running olur, graf Idle'da kalmalı
        MainWindowHost.BuildProjects(vm, names); // OSYS büyüklüğünde proje x started/succeeded: her biri bir statü itişi isterdi
        string selected = MainWindowHost.IdOf("P5");
        vm.SelectProject(selected);

        Assert.True(vm.IsMidRunLocked);                                // ön-koşul: koşu sürüyor — model Running
        Assert.Equal(pushesBefore, graph.UpdateStatusesCallCount);    // KIRMIZI: bugün her olay grafa itilir
        Assert.Equal(GraphRunPhase.Idle, graph.RunPhase);             // faz itilmedi
        Assert.Null(graph.SelectedNode);                              // seçim itilmedi

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();

        Assert.Equal(pushesBefore + 1, graph.UpdateStatusesCallCount); // TAM bir statü itişi
        Assert.Equal(GraphRunPhase.Running, graph.RunPhase);           // faz modele eşit
        Assert.Equal(selected, graph.SelectedNode);                    // seçim modele eşit
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI olay akışı görünür pencerede grafa itilir — yani yukarıdaki test sayacın ölü olmasından
    /// değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void Graph_pushes_still_reach_the_graph_while_the_surface_is_visible()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var graph = window.Shell.GraphHost;
        int pushesBefore = graph.UpdateStatusesCallCount;

        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.BuildProjects(vm, names);

        Assert.True(graph.UpdateStatusesCallCount > pushesBefore);
        Assert.Equal(GraphRunPhase.Running, graph.RunPhase);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A4] <b>Gizli pencerede graf filtresi yenilenmez.</b> Etkin bir filtre/arama varken her proje olayı
    /// (<c>VisibleProjects</c> bildirimi) <c>RefreshGraphFilter</c>'ı çağırır ve her çağrı YENİ bir eşleşme kümesi kurup
    /// TÜM düğümlerin opaklığını yeniden hesaplatır — A4'ün kaldırdığı gizli işin ta kendisi. Kapı üç graf itişiyle AYNI
    /// bayrağı kullanır; dönüşte <c>ResyncAfterShow</c> filtreyi bir kez uygular.
    /// </summary>
    [StaFact]
    public void The_graph_filter_is_not_refreshed_while_hidden_and_is_applied_once_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(20);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var graph = window.Shell.GraphHost;
        vm.ProjectQuery = "P1";                     // etkin arama: graf filtreyi uygular
        int appliedBefore = graph.FilterAppliedCount;
        Assert.True(appliedBefore > 0);             // ön-koşul: filtre görünürken uygulanıyor
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, names);         // her proje olayı VisibleProjects bildirir

        Assert.Equal(appliedBefore, graph.FilterAppliedCount); // KIRMIZI: bugün her olay yeni küme kurup tüm düğümleri yeniden stiller
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();
        Assert.Equal(appliedBefore + 1, graph.FilterAppliedCount); // dönüşte TAM bir uygulama
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A2 fix 1 · M1] <b>Uçuştaki bayat batch.</b> Pompa bir batch'i okuyup UI thread'ine kuyruklarken (damga = o anki
    /// reseed nesli) pencere dönüp belgeyi yeniden kurabilir. Dönüş kurulumu reseed-drop sentinel'ini yazar
    /// (<c>SeedRunDocument</c>); eski damgalı batch kurulumdan SONRA gelse de belgeye ikinci kez inmez. Üretimde pencere
    /// ile VM AYNI <c>ConsoleBatcher</c>'ı paylaşır; fixture de paylaşır — ayrı örneklerle pencerenin nesli VM'in
    /// sentinel'ini hiç göremez ve bu kural sınanamazdı.
    /// </summary>
    [StaFact]
    public void A_batch_stamped_before_the_rebuild_on_show_is_dropped_instead_of_landing_twice()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        window.SetSurfaceHidden(true);
        MainWindowHost.LogLine(vm, "A", 1, "line 0");
        long staleGen = window.ConsoleReseedGen;           // pompanın bu batch'i okuduğu andaki nesil
        window.AppendConsoleBatch("line 0\n", staleGen);   // gizliyken: belgeye basılmaz, "ekran bayat" bayrağı kalkar
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();                          // dönüşte belge modelden BİR kez kurulur (sentinel yazılır)
        Assert.Equal(1, console.RunDocumentReplacedCount); // ön-koşul: kurulum oldu
        string rebuilt = console.EditorControl.Document.Text;

        window.AppendConsoleBatch("line 0\n", staleGen);   // uçuştaki bayat batch, kurulumdan SONRA gelir

        Assert.Equal(rebuilt, console.EditorControl.Document.Text);  // KIRMIZI ayrı batcher'larda: belgeye ikinci kez iner
        Assert.True(window.ConsoleReseedGen > staleGen, "dönüş kurulumu reseed nesli ilerletmeli (sentinel)");

        // Pozitif kontrol: dönüşten sonra GÜNCEL damgalı bir batch hâlâ eklenir — yukarıdaki iddia batch'lerin hiç
        // inemeyişinden değil, bayat damgadan geçer.
        window.AppendConsoleBatch("fresh\n", window.ConsoleReseedGen);
        DispatcherPump.PumpUntil(() => console.EditorControl.Document.Text.Contains("fresh"), TimeSpan.FromSeconds(2));
        Assert.Contains("fresh", console.EditorControl.Document.Text);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A2 fix 1 · M6] Konsol belgesine yazan HER yol kapılıdır — mod geçişi dahil. Tepsiden başlatılan bir koşu, proje
    /// kartı seçiliyken seçimi düşürür (<c>SelectedProjectId = null</c> → <c>ShowRunConsole</c>); belge o anda kurulsaydı
    /// gizli pencerede yeniden kurulum + layout + 340 ms'lik tilt oynardı ve dönüşteki kurulum onu bir kez daha yapardı.
    /// Başlık/VM tarafı yerinde kalır (<c>ShowRun</c>); belgeyi dönüşteki tek kurulum <c>ActiveProjectId</c>'ye bakarak kurar.
    /// </summary>
    [StaFact]
    public void A_run_that_drops_the_project_selection_while_hidden_builds_no_document_until_the_surface_returns()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        console.AnimationsEnabledProvider = () => true;  // tilt oynayabilsin: tek engel gizli sinyal olsun
        vm.SelectProject(MainWindowHost.IdOf("A"));      // proje kartı seçili
        Assert.NotNull(vm.SelectedProjectId);            // ön-koşul
        MainWindowHost.AcceptSends(vm);
        window.SetSurfaceHidden(true);
        int replacedBefore = console.RunDocumentReplacedCount;

        _ = vm.BuildCommand.ExecuteAsync(null);          // koşu başlar: konsol temizlenir, SONRA seçim düşer

        Assert.Null(vm.SelectedProjectId);                                  // ön-koşul: seçim gerçekten düştü
        Assert.Equal(replacedBefore, console.RunDocumentReplacedCount);     // KIRMIZI: bugün ShowRunDocument gizliyken kurar
        Assert.Equal(Visibility.Collapsed, console.Tilt3D.Visibility);      // tilt başlamadı
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();
        Assert.Equal(replacedBefore + 1, console.RunDocumentReplacedCount); // dönüşte TAM bir kurulum
        Assert.Equal(Visibility.Collapsed, console.Tilt3D.Visibility);      // dönüş de tilt'siz
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI akış görünür pencerede belgeyi hemen ve tilt'le kurar — yani yukarıdaki test tilt'in hiç
    /// başlayamamasından değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void A_run_that_drops_the_project_selection_while_visible_still_rebuilds_the_narrative_with_the_tilt()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        console.AnimationsEnabledProvider = () => true;
        vm.SelectProject(MainWindowHost.IdOf("A"));
        Assert.NotNull(vm.SelectedProjectId);
        MainWindowHost.AcceptSends(vm);
        int replacedBefore = console.RunDocumentReplacedCount;

        _ = vm.BuildCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedProjectId);
        Assert.True(console.RunDocumentReplacedCount > replacedBefore);    // belge hemen kuruldu
        Assert.Equal(Visibility.Visible, console.Tilt3D.Visibility);       // tilt oynuyor
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A2 fix 1 · M6] Açık proje logunun dönüş kurulumu kart seçimiyle AYNI satır kuralını kullanır: log BOŞSA sayfa boş
    /// bırakılmaz, o projenin O ANKİ durumunu anlatan metin (<c>ConsoleEmptyState.ForEmptyLog</c>) gösterilir ve proje
    /// modunda boşta "ready" satırı çıkmaz (o yalnız anlatının boş hâli içindir).
    /// </summary>
    [StaFact]
    public void The_rebuild_on_show_of_an_open_project_log_shows_the_empty_state_text_when_the_log_is_empty()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        var row = MainWindowHost.ProjectOf(vm, "A");
        var emptyState = ConsoleEmptyState.ForEmptyLog(row);
        Assert.NotEmpty(emptyState);                      // ön-koşul: boş log sayfayı boş bırakmaz
        vm.ActiveProjectId = row.Id;                      // proje logu açık (motor round-trip'i yok: mod doğrudan kurulur)
        window.SetSurfaceHidden(true);
        window.AppendConsoleBatch("noise\n", window.ConsoleReseedGen);   // gizliyken bir batch geldi: belge bayat

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();

        string text = console.EditorControl.Document.Text;
        Assert.All(emptyState, line => Assert.Contains(line, text));     // KIRMIZI: bugün boş sayfa
        Assert.Equal("", console.ActiveLineText.Text);                   // proje modunda "ready" yok
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5] <b>Gizli pencerede sticky şerit modele dokunmaz.</b> Tepsideyken derlenen bir koşunun her statü
    /// değişimi şeridin metnini, ilerleme çubuğunu ve chip'lerini yeniden yazıyordu. Kaynak model zaten tam durur: görünüm
    /// gizliyken yalnız "şerit modelin gerisinde" bayrağını kaldırır, görününce TEK geçişte (<c>RefreshAll</c>) modele yetişir.
    /// Test GERÇEK kabuğun şeridini sürer (<c>Shell.Ribbon</c>): sinyal pencereye yazılır ve kalıtımla şeride iner; dönüş
    /// kurulumu DP değişiminde eşzamanlıdır (pompa gerekmez).
    /// </summary>
    [StaFact]
    public void The_ribbon_is_not_refreshed_while_hidden_and_catches_up_in_one_pass_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(5);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var ribbon = window.Shell.Ribbon;
        string textBefore = ribbon.PhaseText.Text;
        int passesBefore = ribbon.RebuildCount;
        window.SetSurfaceHidden(true);

        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, names[0]); // bir proje derleniyor: building chip'i
        int compiling = vm.Projects.Count(p => p.IsCompiling);

        Assert.NotEqual(textBefore, vm.RibbonLine.Text);   // ön-koşul: model şerit metnini değiştirdi
        Assert.True(compiling > 0, "ön-koşul: derlenen bir proje var — yoksa chip iddiası boşta yeşil olurdu");
        Assert.Equal(textBefore, ribbon.PhaseText.Text);   // KIRMIZI kapısız: bugün her bildirim metni yeniden yazar
        Assert.Empty(ribbon.BuildingChips);                //                   ve chip'leri kurar
        Assert.Equal(passesBefore, ribbon.RebuildCount);

        window.SetSurfaceHidden(false);

        Assert.Equal(vm.RibbonLine.Text, ribbon.PhaseText.Text);   // dönüşte şerit modele eşit
        Assert.Equal(compiling, ribbon.BuildingChips.Count);       // chip'ler modelle eşit
        Assert.Equal(passesBefore + 1, ribbon.RebuildCount);       // RefreshAll TAM bir kez
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · M3] Şerit için <see cref="A_row_that_is_rebound_while_hidden_is_not_applied_a_second_time_on_show"/>'un
    /// eşi: gizliyken bayrağı bir bildirim kaldırır, sonra şeridin DataContext'i yeniden bağlanır (<c>RefreshAll</c> tam kurulum);
    /// bayrak yerinde kalsaydı dönüş şeridi bir kez daha kurardı.
    /// </summary>
    [StaFact]
    public void A_ribbon_that_is_rebound_while_hidden_is_not_rebuilt_a_second_time_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var ribbon = window.Shell.Ribbon;
        window.SetSurfaceHidden(true);
        MainWindowHost.PreviewBuild(vm, names);                  // şerit bayat: bildirim bayrağı kaldırdı
        ribbon.DataContext = null;                               // yeniden bağlama: DataContextChanged tam kurulumu (RefreshAll) koşar
        ribbon.DataContext = vm;
        int passesAfterRebind = ribbon.RebuildCount;

        window.SetSurfaceHidden(false);

        Assert.Equal(passesAfterRebind, ribbon.RebuildCount);    // KIRMIZI: bayrak düşmediği için dönüş RefreshAll'u bir kez daha koşar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5] <b>Gizli pencerede Build menüsü kurulmaz.</b> Menü içeriğinin tek girdisi toplam proje sayısıdır
    /// (Rebuild'in açıklaması: "All N projects"); <c>Counters</c> ise koşu boyunca her statü değişiminde yayınlanır. Gizliyken
    /// bildirim yalnız bayrağı kaldırır; dönüşte tek karşılaştırma yapılır: toplam değiştiyse TEK kurulum, değişmediyse hiçbiri.
    /// Test toplamı gizliyken değiştirir (Sync yeni bir proje getirir) ki dönüş kurulumu gözlenebilsin. Menü ActionBar'ın
    /// popup'ında durur: kalıtsal sinyal oraya da inmelidir.
    /// </summary>
    [StaFact]
    public void The_build_menu_is_not_rebuilt_while_hidden_and_follows_a_changed_total_once_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        var menu = window.Shell.BuildMenuControl;
        int rebuildsBefore = menu.RefreshRowsCount;
        Assert.Contains("All 3 projects", menu.Items[1].Desc);   // ön-koşul: menü modelin toplamını gösteriyor
        window.SetSurfaceHidden(true);

        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));    // gizliyken Sync yeni bir proje getirdi: toplam 3 -> 4

        Assert.Equal(4, vm.Counters.Total);                      // ön-koşul: model toplamı değişti
        Assert.Equal(rebuildsBefore, menu.RefreshRowsCount);     // KIRMIZI kapısız: bugün her Counters bildirimi menüyü yeniden kurar
        Assert.Contains("All 3 projects", menu.Items[1].Desc);   // ekran hâlâ eski toplamda

        window.SetSurfaceHidden(false);

        Assert.Equal(rebuildsBefore + 1, menu.RefreshRowsCount); // dönüşte TAM bir kurulum
        Assert.Contains("All 4 projects", menu.Items[1].Desc);   // toplam modele eşit
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · eski B5 (1)] Build menüsü YALNIZ toplam proje sayısı değişince yeniden kurulur. <c>Counters</c> bir
    /// koşuda her proje olayında yayınlanır ama menü yalnız toplamı okur: toplam aynıyken menüyü her seferinde sökmek boşa
    /// iştir. Bu kural pencere görünürken de geçerlidir (gizli kapıdan bağımsız).
    /// </summary>
    [StaFact]
    public void The_build_menu_is_rebuilt_only_when_the_total_changes()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var menu = window.Shell.BuildMenuControl;
        int rebuildsBefore = menu.RefreshRowsCount;
        int countersNotifications = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.Counters)) countersNotifications++;
        };

        MainWindowHost.RunBuild(vm, names);                      // toplam sabit: her proje olayı Counters yayınlar

        Assert.True(countersNotifications > 0, "ön-koşul: koşu Counters yayınladı — yoksa 'yeniden kurulmadı' iddiası boşta yeşil olurdu");
        Assert.Equal(rebuildsBefore, menu.RefreshRowsCount);     // KIRMIZI: bugün her Counters bildirimi menüyü yeniden kurar

        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));    // toplam 3 -> 4

        Assert.Equal(rebuildsBefore + 1, menu.RefreshRowsCount); // toplam değişince TEK kurulum
        Assert.Contains("All 4 projects", menu.Items[1].Desc);
        GC.KeepAlive(window);
    }

    /// <summary>[perf Faz A temizlik] <paramref name="model"/>'in gerçek kabuğun listesindeki satır görünümü. Container'lar üretilmiş
    /// olmalıdır (<c>MainWindowHost.Realize</c>); satır, DataContext'i modelle eşlenerek bulunur.</summary>
    private static ProjectRow RowViewOf(StickyLayerList list, ProjectRowViewModel model) =>
        list.RevealRows.Single(r => ReferenceEquals(r.DataContext, model));

    /// <summary>
    /// [perf Faz A · A5] <b>Gizli pencerede proje satırı kendini yazmaz.</b> Tepsideyken derlenen bir koşunun her statü, süre ve
    /// seçim bildirimi görünmeyen satırın glyph'ini, şeridini, süre metnini ve sağ bloğunu yeniden yazıyordu. Satır gizliyken
    /// yalnız "satır modelin gerisinde" bayrağını kaldırır; görününce <c>ApplyAll</c> tek geçişte modelden kurar (süre de onun
    /// içinden bir kez yazılır). Satır GERÇEK kabuğun listesinden alınır: sinyal pencereden satıra kalıtımla iner.
    /// </summary>
    [StaFact]
    public void A_project_row_applies_nothing_while_hidden_and_applies_once_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(5);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window); // satır container'ları üretilir (liste topolojiden SONRA doldu)
        Assert.NotEmpty(list.RevealRows);   // ön-koşul: satırlar gerçek ağaçta kuruldu
        var rowVm = MainWindowHost.ProjectOf(vm, "P1");
        var row = RowViewOf(list, rowVm);
        int allBefore = row.ApplyAllCount, durationBefore = row.ApplyDurationCount;
        var glyphBefore = row.Glyph.Status;
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, names);

        Assert.NotEqual(rowVm.VisualStatus, glyphBefore);           // ön-koşul: model satırın statüsünü değiştirdi
        Assert.Equal(allBefore, row.ApplyAllCount);
        Assert.Equal(durationBefore, row.ApplyDurationCount);       // KIRMIZI kapısız: bugün her durum/süre bildirimi süreyi yeniden yazar
        Assert.Equal(glyphBefore, row.Glyph.Status);                //                   ve glyph'i günceller

        window.SetSurfaceHidden(false);

        Assert.Equal(allBefore + 1, row.ApplyAllCount);             // ApplyAll TAM bir kez
        Assert.Equal(durationBefore + 1, row.ApplyDurationCount);   // süre onun içinden bir kez yazıldı
        Assert.Equal(rowVm.VisualStatus, row.Glyph.Status);         // satır modelle eşit
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · M1] <b>Gizlilikte gelen karar dönüşte çapraz-sönümle oynamaz.</b> Satırın son çizilen hâli başlangıç
    /// modundaysa (halka) ve karar gizliyken geldiyse, dönüş kurulumu noktayı halkadan dolu daireye 380 ms'lik geçişle çizerdi:
    /// çapraz-sönüm bir durum değişimini anlatır, gizlilikte kaçırılmış bir değişimi değil (<c>StartMode.ShouldCrossFade</c>).
    /// Satırın hareketi AÇIK kurulur ki sönüm oynayabilsin; dönüşte iki eleman da animasyonsuz ve hedef opaklıkta durmalıdır.
    /// </summary>
    [StaFact]
    public void A_decision_that_arrives_while_hidden_settles_the_status_dot_on_show_without_the_cross_fade()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window);
        var row = RowViewOf(list, MainWindowHost.ProjectOf(vm, "P1"));
        row.AnimationsEnabledProvider = () => true;                 // sönüm oynayabilsin (headless varsayılanı reduced-motion)
        Assert.Equal(StartMode.RingOpacity, row.Dot.Ring.Opacity);  // ön-koşul: son çizilen hâl başlangıç modu (halka)
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, names);                         // karar gizliyken geldi: halka → dolu daire
        window.SetSurfaceHidden(false);                             // satır dönüş kurulumunu kalıtsal sinyalin değişiminde yapar

        Assert.False(row.Dot.Ring.HasAnimatedProperties);           // KIRMIZI: bugün halka 380 ms'de sönümlenir
        Assert.False(row.Dot.Fill.HasAnimatedProperties);           //          ve dolu daire belirir
        Assert.Equal(0.0, row.Dot.Ring.Opacity);                    // hedefte, geçişsiz
        Assert.Equal(1.0, row.Dot.Fill.Opacity);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · M3] <b>Tam kurulum "satır bayat" bayrağını düşürür.</b> Gizliyken bir VM bildirimi bayrağı kaldırır; sonra
    /// satır yeni bir modele bağlanırsa (geri dönüştürülen container) <c>ApplyAll</c> modelin O ANKİ hâlini zaten kurar. Bayrak
    /// yerinde kalsaydı dönüş aynı kurulumu ikinci kez koşardı.
    /// </summary>
    [StaFact]
    public void A_row_that_is_rebound_while_hidden_is_not_applied_a_second_time_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window);
        var row = RowViewOf(list, MainWindowHost.ProjectOf(vm, "P1"));
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);                                            // satır bayat: bildirim bayrağı kaldırdı
        row.DataContext = MainWindowHost.ProjectOf(vm, "P2");                          // container yeniden kullanımı: yeni model, ApplyAll tam kurulum
        int appliesAfterRebind = row.ApplyAllCount;

        window.SetSurfaceHidden(false);

        Assert.Equal(appliesAfterRebind, row.ApplyAllCount);                           // KIRMIZI: bayrak düşmediği için dönüş ApplyAll'u bir kez daha koşar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5] <b>Gizlilikte gelen hata dönüşte shake oynatmaz.</b> Shake yalnız hata ANINDAKİ geçişin tepkisidir
    /// (<c>ApplyStateTransition</c>); dönüş kurulumu durumu doğrudan modelden alır ve geçiş görmez. Kontrol: AYNI hata görünür
    /// pencerede shake'i oynatır — yani test shake'in hiç oynayamamasından değil, gizli sinyalden geçer. Satırların hareketi AÇIK
    /// kurulur (headless varsayılanı reduced-motion).
    /// </summary>
    [StaFact]
    public void A_failure_that_arrives_while_hidden_plays_no_shake_on_show_but_a_visible_one_does()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window);
        var hiddenVm = MainWindowHost.ProjectOf(vm, "P1");
        var hiddenRow = RowViewOf(list, hiddenVm);
        var visibleRow = RowViewOf(list, MainWindowHost.ProjectOf(vm, "P2"));
        hiddenRow.AnimationsEnabledProvider = () => true;
        visibleRow.AnimationsEnabledProvider = () => true;
        MainWindowHost.PreviewBuild(vm, "P1", "P2");
        MainWindowHost.StartBuild(vm, "P1", "P2");
        MainWindowHost.StartProject(vm, "P1");                  // iki satır derleniyor: Failed geçişinin kaynağı
        MainWindowHost.StartProject(vm, "P2");
        window.SetSurfaceHidden(true);

        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("P1"), 100, "exit 1"));   // hata gizliyken geldi
        window.SetSurfaceHidden(false);

        Assert.Equal(ProjectRowState.Failed, hiddenVm.State);         // ön-koşul: model hatayı yazdı
        Assert.False(hiddenRow.ShakeTranslate.HasAnimatedProperties); // dönüşte shake YOK

        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("P2"), 100, "exit 1"));   // aynı hata görünürken
        Assert.True(visibleRow.ShakeTranslate.HasAnimatedProperties); // kontrol: shake oynar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5] <b>Gizli pencerede proje listesi kurulmaz.</b> Topoloji (Sync'in getirdiği yeni proje kümesi) modelde
    /// zaten durur; liste gizliyken yalnız "liste bayat" bayrağını kaldırır, dönüşte <c>ResyncAfterShow</c> onu TEK geçişte ve
    /// <b>reveal'siz</b> kurar — gizlilikte olan bir değişimin kademeli belirişi geriye dönük oynanmaz. Grafın yeniden
    /// kurulması (<c>RebuildGraph</c>) bu kapının dışındadır ve dönüşte tekrarlanmaz. Listenin yeniden kurulması
    /// <c>ItemContainerGenerator.ItemsChanged</c> ile sayılır; reveal oynaması <c>RevealGeneration</c> ile.
    /// </summary>
    [StaFact]
    public void The_project_list_is_not_rebuilt_while_hidden_and_is_built_once_without_reveal_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        MainWindowHost.Realize(window);
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3)); // ilk topolojinin belirişi oynadı
        int revealBefore = list.RevealGeneration;
        int itemsBefore = list.RowFlow.Items.Count;
        int rebuilds = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => rebuilds++;
        window.SetSurfaceHidden(true);

        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));   // gizliyken Sync yeni bir proje getirdi

        Assert.Equal(4, vm.Projects.Count);                     // ön-koşul: model topolojisi değişti
        Assert.Equal(0, rebuilds);                              // KIRMIZI kapısız: bugün topoloji listeyi hemen yeniden kurar
        Assert.Equal(itemsBefore, list.RowFlow.Items.Count);

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();                               // Loaded-öncelikli kurulum pompasız koşsun (A4 testlerinin deseni)
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(400)); // beliriş verilseydi burada oynardı

        Assert.Equal(1, rebuilds);                              // liste dönüşte TAM bir kez kuruldu (kuyruktaki ResyncAfterShow bayrağı artık bulmaz)
        Assert.NotEqual(itemsBefore, list.RowFlow.Items.Count); // ...ve yeni topolojiyi gösteriyor
        Assert.Equal(revealBefore, list.RevealGeneration);      // reveal OYNAMADI
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI topoloji değişimi görünür pencerede listeyi hemen kurar ve belirişi oynatır — yani yukarıdaki test
    /// belirişin hiç oynayamamasından değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void A_topology_change_while_visible_rebuilds_the_list_at_once_and_plays_the_reveal()
    {
        using var dir = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        MainWindowHost.Realize(window);
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        int revealBefore = list.RevealGeneration;
        int itemsBefore = list.RowFlow.Items.Count;

        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));

        Assert.NotEqual(itemsBefore, list.RowFlow.Items.Count);   // liste hemen kuruldu
        DispatcherPump.PumpUntil(() => list.RevealGeneration != revealBefore, TimeSpan.FromSeconds(3));
        Assert.NotEqual(revealBefore, list.RevealGeneration);     // ve beliriş oynadı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5] Gizliyken yalnız koşu olayları gelirse (topoloji ve filtre aynı) liste dönüşte yeniden KURULMAZ. Bayrak
    /// "kurulum gerçekten gerekiyor"u söyler: görünür satır kümesinin imzası değişmedikçe <c>RefreshVisibleRows</c> bayrağı
    /// kaldırmaz — aksi halde tepsideki her derleme, pencere gelince listenin tamamen yeniden kurulmasına (container üretimi,
    /// kaydırma konumu) yol açardı. Satırların kendi görünümü ayrı kapıdan yetişir (<c>ProjectRow</c>).
    /// </summary>
    [StaFact]
    public void A_hidden_run_that_changes_neither_topology_nor_filter_leaves_the_list_alone_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(5);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window);
        int rebuilds = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => rebuilds++;
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, names);
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();

        Assert.Equal(0, rebuilds);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · M2] <b>Listeyi yazan yol "liste bayat" bayrağını düşürür.</b> Dönüş (<c>SetSurfaceHidden(false)</c>) ile
    /// Loaded-öncelikli <c>ResyncAfterShow</c> arasında görünür bir topoloji değişimi listeyi (reveal'li) zaten kurar; bayrak
    /// yerinde kalsaydı ardından gelen dönüş kurulumu listeyi ikinci kez, bu kez reveal'siz kurar ve ilkinin belirişini keserdi.
    /// </summary>
    [StaFact]
    public void A_visible_topology_change_between_the_show_and_the_resync_leaves_the_resync_nothing_to_rebuild()
    {
        using var dir = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        MainWindowHost.Realize(window);
        window.SetSurfaceHidden(true);
        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));   // gizliyken topoloji: liste bayat
        window.SetSurfaceHidden(false);                         // Loaded-öncelikli ResyncAfterShow kuyrukta (pompa yok)
        int rebuilds = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => rebuilds++;

        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(5));   // dönüş ile Loaded arasında GÖRÜNÜR topoloji kurulumu
        int rebuildsAfterVisible = rebuilds;
        Assert.True(rebuildsAfterVisible > 0, "ön-koşul: görünür topoloji listeyi kurdu");

        window.ResyncAfterShow();                               // kuyruktaki dönüş kurulumu (A4 testlerinin deseni)

        Assert.Equal(rebuildsAfterVisible, rebuilds);           // KIRMIZI: bayat bayrak listeyi ikinci kez (reveal'siz) kurdururdu
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A5 · M2] Sync ekranı baştan başlatırken (<c>BlankPlanSurface</c>) liste boşalır ve yeni topoloji gelene dek
    /// BOŞ kalır. Gizlilikte kalmış bir "liste bayat" bayrağı yerinde dursaydı, bekleyen dönüş kurulumu modeldeki ESKİ topolojiyi
    /// boşaltılmış listeye geri yazardı.
    /// </summary>
    [StaFact]
    public async Task A_sync_restart_between_the_show_and_the_resync_keeps_the_blanked_list_empty()
    {
        using var dir = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        MainWindowHost.Realize(window);
        MainWindowHost.AcceptSends(vm);
        window.SetSurfaceHidden(true);
        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(4));   // gizliyken topoloji: liste bayat
        window.SetSurfaceHidden(false);                         // Loaded-öncelikli ResyncAfterShow kuyrukta (pompa yok)

        await MainWindowHost.StartSync(vm, SyncMode.Manual);    // Sync düğmesi: ekran baştan başlar, liste boşalır
        Assert.True(vm.PlanSurfaceRestarting);                  // ön-koşul: ekran gerçekten boşaldı
        Assert.Empty(list.RowFlow.Items);                       // ön-koşul: liste boş
        window.ResyncAfterShow();                               // kuyruktaki dönüş kurulumu (A4 testlerinin deseni)

        Assert.Empty(list.RowFlow.Items);                       // KIRMIZI: bayat bayrak eski topolojiyi boş listeye geri yazardı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A6] <b>Koşan bir derleme için kurulmuş gerçek kabuk, saat enjekte.</b> <c>P1</c> bitmiş (ETA'nın
    /// ortalaması var), <c>P0</c> derleniyor (satırın canlı süresi var), kalanlar sırada. Saat bu kurulumda
    /// <b>kımıldamaz</b>: koşunun da satırın da başlangıcı aynı andır; ilerlemesini <paramref name="nowMs"/>'i süren test belirler.
    /// </summary>
    private static (MainWindow Window, RunViewModel Vm) NewRunningWindow(TempDir dir, Func<long> nowMs)
    {
        string[] names = MainWindowHost.ProjectNames(4);
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, nowMs, names);
        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, "P1");
        MainWindowHost.SucceedProject(vm, "P1", 60_000); // uzun ortalama: ETA her tikte gözle görülür azalır
        MainWindowHost.StartProject(vm, "P0");
        return (window, vm);
    }

    /// <summary>
    /// [perf Faz A · A6] <b>Gizli pencerede 200 ms'lik tik canlı süreleri yazmaz.</b> Tepsideyken derlenen bir koşuda her tik koşu
    /// süresini, building satırların süresini ve ETA'yı yeniden yazıyordu — her biri bir <c>PropertyChanged</c> ve görünmeyen
    /// ekranın yeniden yazımı. Saat enjekte edilir ve 15 tik (3 sn) gerçek kabuğun tik gövdesiyle sürülür: gizliyken hiçbir
    /// canlı süre yazılmaz; dönüşte <c>ResyncAfterShow</c> hepsini TEK tikle modelden yetiştirir.
    /// </summary>
    [StaFact]
    public void The_elapsed_tick_writes_no_live_clock_while_hidden_and_the_clock_catches_up_once_on_show()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm) = NewRunningWindow(dir, () => now);
        var row = MainWindowHost.ProjectOf(vm, "P0");
        Assert.Equal(ProjectRowState.Started, row.State); // ön-koşul: satırın canlı süresi var
        long startedAtMs = now;                           // koşunun da satırın da başladığı an
        var header = window.Shell.ConsoleHeaderControl;
        int lineWritesBefore = header.SetLineCountCalls;
        int frontierBefore = window.FrontierFollowCount;
        var writes = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.ElapsedMs) or nameof(vm.EtaMs) or nameof(vm.EtaText)) writes.Add(e.PropertyName);
        };
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(row.DurationMs)) writes.Add(e.PropertyName);
        };
        window.SetSurfaceHidden(true);

        for (int i = 0; i < 15; i++) { now += 200; window.OnElapsedTick(); }

        Assert.Empty(writes); // KIRMIZI kapısız: bugün her tik ElapsedMs'i, building satırın DurationMs'ini ve ETA'yı yazar
        Assert.Equal(lineWritesBefore, header.SetLineCountCalls); // satır sayacı yazılmadı
        Assert.Equal(frontierBefore, window.FrontierFollowCount); // frontier takibi koşmadı
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow(); // Loaded-öncelikli kurulum pompasız koşsun (A4 testlerinin deseni)

        Assert.Equal(now - startedAtMs, vm.ElapsedMs);                 // dönüşte süreler modele yetişti
        Assert.Equal(now - startedAtMs, row.DurationMs);
        Assert.Equal(1, writes.Count(n => n == nameof(vm.ElapsedMs))); // ...TEK tikle
        Assert.Equal(1, writes.Count(n => n == nameof(row.DurationMs)));
        Assert.Equal(lineWritesBefore + 1, header.SetLineCountCalls); // satır sayacı dönüşte TEK kez yenilendi
        Assert.Equal(frontierBefore, window.FrontierFollowCount);     // dönüş kurulumu frontier takibi yapmaz
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI saat ilerlemesi görünür pencerede canlı süreleri yazar — yani yukarıdaki test saatin hiç
    /// yazılamamasından değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void The_elapsed_tick_still_writes_the_live_clock_while_visible()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm) = NewRunningWindow(dir, () => now);
        var row = MainWindowHost.ProjectOf(vm, "P0");
        var header = window.Shell.ConsoleHeaderControl;
        int lineWritesBefore = header.SetLineCountCalls;
        int frontierBefore = window.FrontierFollowCount;
        now += 200;

        window.OnElapsedTick();

        Assert.Equal(200, vm.ElapsedMs);
        Assert.Equal(200, row.DurationMs);
        Assert.Equal(lineWritesBefore + 1, header.SetLineCountCalls); // satır sayacı her tikte yenilenir
        Assert.Equal(frontierBefore + 1, window.FrontierFollowCount); // koşan derlemede frontier takibi koşar
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A6] <b>Gizli pencerede motor bekçisi çalışır</b> (Review Focus 2). Tik gövdesi gizliyken kapanır, bekçi
    /// kapanmaz: tepsiden Exit + susmuş motorda bekleyen çıkışı bekçinin uyarısı serbest bırakır (<c>RunViewModel.Exit.cs</c>),
    /// yani çıkış ancak tik sürdükçe gelir. Bu bir REGRESYON pinidir: bekçi kapıdan önce de koşulsuz koşar ve test kapısız
    /// da yeşildir. <c>SafeExitTests.A_silent_engine_does_not_hold_the_exit</c> ile aynı senaryo — bu kez gerçek kabuğun tikiyle
    /// ve pencere gizliyken.
    /// </summary>
    [StaFact]
    public void A_silent_engine_still_frees_a_pending_exit_while_the_surface_is_hidden()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, () => now, MainWindowHost.ProjectNames(2));
        MainWindowHost.AcceptSends(vm);
        int ready = 0;
        vm.ExitReady += (_, _) => ready++;
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++; // gerçek uygulama kapanmasın: sayaçlı sahte (CloseToTrayTests deseni)
        MainWindowHost.StartBuild(vm, "P0", "P1");
        window.SetSurfaceHidden(true); // koşu sürerken tepsiye iner
        Assert.True(window.IsSurfaceHidden); // ön-koşul: gizli sinyal gerçekten yazıldı
        vm.RequestExit();              // tepsiden Exit: graceful stop, drain beklenir
        Assert.True(vm.ExitPending);   // ön-koşul: çıkış drain'i bekliyor — bekçinin penceresi
        Assert.Equal(0, ready);

        now += RunViewModel.EngineSilenceThresholdMs - 1;
        window.OnElapsedTick();
        Assert.Null(vm.EngineOverdueMessage); // eşiğin ALTI: meşru drain
        Assert.Equal(0, ready);

        now += 1;
        window.OnElapsedTick();

        Assert.NotNull(vm.EngineOverdueMessage); // bekçi gizliyken de uyardı
        Assert.Equal(1, ready);                  // ...ve bekleyen çıkışı serbest bıraktı
        DispatcherPump.PumpUntil(() => shutdowns > 0, TimeSpan.FromSeconds(2));
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200)); // PumpUntil ilk çağrıda döner: geç gelen ikinci kapatma da yakalansın
        Assert.Equal(1, shutdowns);              // ...ve uygulama TAM bir kez kapatıldı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A9] <b>Gizli pencerede alt çubuğun sayaç chip'leri modele dokunmaz.</b> Kapılı ölçüm testi gizliyken proje
    /// başına ≈1 layout geçişi okuyordu; WPF'in layout kuyruğu tek kaynağın bu chip'ler olduğunu gösterdi: her <c>projectStarted</c>
    /// ve <c>projectSucceeded</c> <c>RunViewModel.Counters</c>'ı değiştirir ve çubuk chip değerlerini ve building ikonunu yeniden
    /// yazıyordu. Kaynak model zaten tam durur: çubuk gizliyken yalnız "chip'ler modelin gerisinde" bayrağını kaldırır, görününce
    /// TEK geçişte modele yetişir (dönüş kurulumu DP değişiminde eşzamanlıdır, pompa gerekmez).
    ///
    /// <para>Chip'ler <c>ActionBar.OnLoaded</c>'da kurulur ve headless ağaçta <c>Loaded</c> hiç ateşlenmez — orada test boşta yeşil
    /// kalırdı. Kabuk içeriği bu yüzden ekran dışı gerçek bir pencereye taşınır (<see cref="MainWindowHost.HostOffscreen"/>); sinyal
    /// o pencereye yazılır ve kalıtımla çubuğa iner. Koşu görünürken başlar ve ağaç yerleşir, sonra pencere gizlenir: tepsiye
    /// indirilen bir koşu.</para>
    /// </summary>
    [StaFact]
    public void The_action_bar_counter_chips_are_not_refreshed_while_hidden_and_catch_up_on_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(5);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        WithLoadedActionBar(window, (host, bar) =>
        {
            var building = ActionBarTests.ChipValue(bar.BuildingChip);      // chip değeri ve spinner: ActionBarTests'in TEK okuma yolları
            var spinner = ActionBarTests.ChipSpinner(bar.BuildingChip);
            MainWindowHost.PreviewBuild(vm, names);
            MainWindowHost.StartBuild(vm, names);
            window.Shell.UpdateLayout();                                // koşu görünürken başladı, ağaç yerleşti
            Assert.Equal("0", building.Text);                           // ön-koşul: chip'ler kurulu, derlenen proje yok
            Assert.Equal(Visibility.Collapsed, spinner.Visibility);
            HiddenSurface.SetIsHidden(host, true);
            var chips = LayoutValid(bar.SigmaChip, bar.BuildingChip, bar.CurrentChip, bar.FailedChip, bar.WarnChip);
            Assert.True(chips.Contains(building), "ön-koşul: building değeri ölçülmüş ve yerleştirilmiş");

            MainWindowHost.StartProject(vm, names[0]);

            Assert.Equal(1, vm.Counters.Building);                      // ön-koşul: model sayacı değişti
            Assert.Equal("0", building.Text);                           // KIRMIZI kapısız: her sayaç bildirimi değeri yeniden yazar
            Assert.Equal(Visibility.Collapsed, spinner.Visibility);     //                   ve building ikonunu çevirir
            Assert.Empty(chips.Where(e => !e.IsMeasureValid || !e.IsArrangeValid).Select(Describe)); // hiçbir chip öğesi geçersizlenmedi

            HiddenSurface.SetIsHidden(host, false);

            Assert.Equal("1", building.Text);                           // dönüşte chip'ler modele eşit
            Assert.Equal(Visibility.Visible, spinner.Visibility);
        });
        GC.KeepAlive(window);
    }

    /// <summary>[perf Faz A · temizlik 2b] Kabuğun alt çubuğu: ağaçtaki TEK örnek. Chip'lerini <c>Loaded</c>'da kurduğu için headless
    /// ağaçta chip'siz durur (bkz. <see cref="WithLoadedActionBar"/>).</summary>
    private static ActionBar ActionBarOf(MainWindow window) => DsResources.Descendants(window.Shell).OfType<ActionBar>().Single();

    /// <summary>
    /// [perf Faz A · temizlik 2b] Alt çubuk testlerinin TEK girişi: kabuk içeriğini ekran dışı gerçek bir pencereye taşır
    /// (<see cref="MainWindowHost.HostOffscreen"/>), çubuğun yüklenmesini bekler (chip'ler <c>Loaded</c>'da kurulur), gövdeyi çalıştırır ve
    /// pencereyi — gövdedeki bir iddia patlasa da — kapatır. <c>Loaded</c> ÖNCESİNİ de sınayan test
    /// (<see cref="An_action_bar_that_starts_hidden_builds_its_chips_once_from_the_current_counters_on_first_show"/>) çubuğu önce
    /// <see cref="ActionBarOf"/> ile alır, gizli işaretler ve ancak sonra buraya girer; gövdeye gelen çubuk o örnektir.
    /// </summary>
    private static void WithLoadedActionBar(MainWindow window, Action<Window, ActionBar> body)
    {
        var host = MainWindowHost.HostOffscreen(window);
        try
        {
            var bar = ActionBarOf(window);
            DispatcherPump.PumpUntil(() => bar.IsLoaded, TimeSpan.FromSeconds(5)); // chip'ler Loaded'da kurulur
            body(host, bar);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// [perf Faz A · A8] Düzeni geçerli (ölçülmüş VE yerleştirilmiş) görsel ağaç öğeleri: <paramref name="roots"/>'un kendileri ve
    /// tüm görsel torunları. Bir layout geçişi ancak bir öğenin ölçümü ya da yerleşimi geçersizlendiğinde çıkar; "hiçbir ölçüm
    /// geçersizlenmedi" iddiası bu kümenin SONRADAN da geçerli kalmasıdır. Başta geçersiz olanlar (hiç ölçülmemiş, çökük alt
    /// ağaçlar) kümede değildir ve iddiaya girmez.
    /// </summary>
    private static HashSet<UIElement> LayoutValid(params UIElement[] roots) =>
    [
        .. roots.SelectMany(root => DsResources.Descendants(root).Prepend(root)).OfType<UIElement>()
            .Where(e => e.IsMeasureValid && e.IsArrangeValid),
    ];

    /// <summary>Hata iletisinde bir öğeyi tanıtır: tipi ve en yakın adlı atası (satır container'ları adsızdır).</summary>
    private static string Describe(UIElement element)
    {
        var named = DsResources.SelfAndAncestors(element).OfType<FrameworkElement>().FirstOrDefault(f => f.Name.Length > 0);
        return named is null ? element.GetType().Name
            : ReferenceEquals(named, element) ? $"{element.GetType().Name}#{named.Name}"
            : $"{element.GetType().Name} in #{named.Name}";
    }

    /// <summary>
    /// [perf Faz A · A8] <b>Kalıcı pin: pencere gizliyken koşu olayları hiçbir görünümün ölçümünü geçersizlemez.</b> Faz A'nın amacı
    /// tepsideki derlemede UI thread'ine layout geçişi yaptırmamaktır ve bir layout geçişi ancak bir öğenin ölçümü ya da yerleşimi
    /// geçersizlendiğinde çıkar. Test OSYS büyüklüğünde bir derlemenin olay akışını gizliyken sürer — plan, başlangıç, derlenen bir satır,
    /// 300 log satırı, 3 sn'lik tikler (gerçek kabuğun tik gövdesi), bir konsol batch'i, her projenin başlayıp bitmesi ve koşunun
    /// bitişi — ve gerçekleşmiş kabuk içeriğinin TÜM görsel ağacında başta geçerli olan HER öğenin sonda da geçerli kaldığını,
    /// konsol belgesinin gizlendikten sonra hiç yeniden kurulmadığını sınar. İzlenen küme elle seçilmiş köklerden değil ağacın
    /// tamamından türer: kapısı unutulmuş yeni bir yüzey de yakalanır. Yüzeyler ayrıca tek tek kanıtlanır (şerit ve proje satırları
    /// kümeye girer; olay akışı, konsol, konsol başlığı ve graf kökün kendisi dışında en az bir torunla girer): bir yüzey kümede hiç
    /// yoksa iddia o yüzey için boşta yeşil kalırdı. Bir yüzeyin kapısı kalkarsa o yüzeyin metni/chip'i/satırı yazılır, öğeleri
    /// geçersizlenir ve test kırmızıdır (şerit, proje satırları, olay akışı ve konsol kapıları için kırmızısı gösterildi).
    ///
    /// <para>Pin yalnız headless ağaçta, dispatcher pompalanmadan görünen geçersizlemeyi yakalar: orada layout turu koşmaz ve
    /// ölçüm "geçersizlenmedi"dir. Bu pinin görmediği işleri (ör. grafa statü itişi) ilgili kapının kendi testi sınar;
    /// dispatcher'ın sonradan koşturduğu işlerin layout geçişlerini ve thread döngüsünü gerçek bir pencerede kapılı ölçüm testi
    /// okur (<see cref="HiddenSurfaceMeasurementTests"/>).</para>
    ///
    /// <para><b>Pin dışı yüzeyler:</b> headless <c>Realize</c>'da <c>Loaded</c> ateşlenmez; görünümünü <c>Loaded</c>'da kuran
    /// yüzeyler (alt çubuğun sayaç chip'leri) bu ağaçta hiç kurulmaz ve burada boşta yeşil kalırdı. Onları gerçek bir ekran dışı
    /// pencerede <see cref="The_action_bar_counter_chips_are_not_refreshed_while_hidden_and_catch_up_on_show"/> pinler. Popup içeriği
    /// de pin dışıdır: Build menüsü <c>SplitButton.MenuContent</c>'tir ve popup kapalıyken (gizli pencerede hep) pencerenin görsel
    /// ağacında değildir — kalıtsal sinyal ona inmez (<c>ActionBar</c> değeri menüye açıkça aktarır) ve içeriği izlenen kümeye hiç
    /// girmez. Onu <see cref="The_build_menu_is_not_rebuilt_while_hidden_and_follows_a_changed_total_once_on_show"/> pinler.</para>
    /// </summary>
    [StaFact]
    public void Run_events_and_console_batches_leave_the_realized_shell_measure_valid_while_the_surface_is_hidden()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(MainWindowHost.OsysProjectCount);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        var content = MainWindowHost.Realize(window); // satır container'ları üretilir; ağaç ölçülmüş ve yerleştirilmiş
        var shell = window.Shell;
        window.SetSurfaceHidden(true);
        int replacedBefore = shell.ConsoleViewControl.RunDocumentReplacedCount; // gizlemeden SONRAKİ taban: mutlak 0 değil
        var gated = LayoutValid(content); // gerçekleşmiş kabuğun TÜM görsel ağacı: elle seçilmiş kök listesi yok
        Assert.True(content.IsMeasureValid && content.IsArrangeValid);     // ön-koşul: realize edilmiş ağaç geçerli
        Assert.True(gated.Contains(shell.Ribbon), "ön-koşul: şerit ölçülmüş");
        Assert.True(list.RevealRows.Any(row => gated.Contains(row)), "ön-koşul: gerçekleşmiş proje satırları ölçülmüş");
        // Her yüzey ayrı kanıtlanır: kümede o yüzeyden hiç öğe yoksa (headless'ta çökük ya da ölçülmemiş) iddia o yüzey için boşta
        // yeşil kalırdı. Şerit ve satır ön-koşulları (yukarıda) kökün kendisiyle yetinir; aşağıdaki dört yüzey (olay akışı, konsol,
        // konsol başlığı, graf) için kökün kendisi yetmez: kök ölçülmüş olsa da içinin ölçüldüğünü göstermez, oysa kapısı
        // unutulmuş bir yazım içteki bir öğeyi (metin, satır) geçersizler — bu yüzden bu dört yüzeyde kökün DIŞINDA en az bir torun
        // kümede olmalıdır. Eksik yüzeylerin adları tek iddiada hepsiyle birlikte bildirilir.
        var surfaces = new (string Name, UIElement Root)[]
        {
            ("event stream", shell.EventStreamControl), ("console", shell.ConsoleViewControl),
            ("console header", shell.ConsoleHeaderControl), ("graph", shell.GraphHost),
        };
        Assert.Empty(surfaces.Where(s => !gated.Any(e => !ReferenceEquals(e, s.Root) && DsResources.IsSelfOrDescendantOf(e, s.Root)))
            .Select(s => s.Name));

        MainWindowHost.PreviewBuild(vm, names);                            // motorun planı
        MainWindowHost.StartBuild(vm, names);                              // runStarted
        MainWindowHost.StartProject(vm, names[0]);                         // derlenen bir satır: canlı süre ve chip'in işi olsun
        for (int i = 0; i < 300; i++) MainWindowHost.LogLine(vm, names[0], i + 1, $"line {i}");
        for (int i = 0; i < 15; i++) window.OnElapsedTick();               // 3 sn'lik 200 ms tikler (üretimdeki tik gövdesi)
        window.AppendConsoleBatch(string.Join("", Enumerable.Range(0, 300).Select(i => $"line {i}\n")), window.ConsoleReseedGen);
        MainWindowHost.SucceedProject(vm, names[0]);                       // derlenen satır biter: projectStarted ikinci kez gelmez
        MainWindowHost.BuildProjects(vm, names[1..]);                      // kalan her proje BİR kez başlar ve biter
        MainWindowHost.CompleteRun(vm, names.Length);                      // runCompleted: tüm projeler başarılı — Done satırı gerçek toplamı okur

        Assert.True(vm.StreamEvents.Count > 0 && vm.GetActiveLineCount() >= 300, "ön-koşul: olaylar ve log satırları modele ulaştı");
        Assert.True(content.IsMeasureValid && content.IsArrangeValid);
        Assert.True(shell.Ribbon.IsMeasureValid);
        Assert.Empty(gated.Where(e => !e.IsMeasureValid || !e.IsArrangeValid).Select(Describe)); // hiçbir kapılı öğe geçersizlenmedi
        Assert.Equal(replacedBefore, shell.ConsoleViewControl.RunDocumentReplacedCount); // konsol belgesi gizliyken hiç yeniden kurulmadı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · A8 · Review Focus 1] <b>Gizle → göster → gizle → göster konsolu çiftlemez.</b> Her gösterimde konsol belgesi
    /// modelin O ANKİ tam metninden BİR kez kurulur; gizliyken gelen batch'ler belgeye hiç inmez (model zaten tutar), görünürken
    /// gelenler bir kez eklenir ve kurulumdan önce okunmuş uçuştaki bayat batch kurulumdan sonra gelse de düşer. Her geçişte
    /// belgenin satır sayısı modelinkine eşittir, hiçbir satır iki kez ya da eksik durmaz. (Bayat batch'in düşme kuralının kendisi
    /// <see cref="A_batch_stamped_before_the_rebuild_on_show_is_dropped_instead_of_landing_twice"/>'ta pinlidir.)
    /// </summary>
    [StaFact]
    public void A_hide_show_hide_show_sequence_rebuilds_the_console_once_per_show_and_never_duplicates_a_line()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        int emitted = 0;
        // Motor n satır daha yazar: model alır; dönen metin pompanın bu satırlar için üreteceği batch'tir (pompa bu fixture'da
        // hiç tick etmez, batch'i üretimdeki hedefe test verir).
        string Emit(int count)
        {
            var batch = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                emitted++;
                MainWindowHost.LogLine(vm, "A", emitted, $"line {emitted}");
                batch.Append($"line {emitted}\n");
            }
            return batch.ToString();
        }
        List<string> DocumentLines() => [.. console.EditorControl.Document.Text.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r'))];
        void AssertConsoleMatchesModel()
        {
            // Belge model metninin tamamını değil yalnız bir render dilimini (en çok RenderSliceLines satır) tutar: "belge satır
            // sayısı == model satır sayısı" iddiası ancak toplam bunun altındayken doğrudur. Test büyürse iddialar yanlış
            // sayılarla değil bu mesajla patlasın.
            Assert.True(emitted < ConsoleView.RenderSliceLines,
                $"toplam {emitted} satır render dilimini ({ConsoleView.RenderSliceLines}) aştı: belge/model eşitliği artık beklenemez");
            var lines = DocumentLines();
            Assert.Equal(vm.GetActiveLineCount(), lines.Count);   // konsol satır sayısı modelle eşit
            Assert.Equal(lines.Count, lines.Distinct().Count());  // çift satır yok
            for (int n = 1; n <= emitted; n++)
                Assert.Single(lines, l => l.EndsWith($"line {n}", StringComparison.Ordinal)); // her satır tam bir kez durur
        }

        window.SetSurfaceHidden(true);
        window.AppendConsoleBatch(Emit(20), window.ConsoleReseedGen); // gizli 1: batch belgeye inmez, model tutar
        long inFlightGen = window.ConsoleReseedGen;                   // pompa bir batch'i bu nesilde okudu ...
        string inFlight = Emit(5);                                    // ... ve henüz teslim etmedi
        Assert.Equal(0, console.RunDocumentReplacedCount);

        window.SetSurfaceHidden(false);                               // gösterim 1
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount == 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, console.RunDocumentReplacedCount);
        AssertConsoleMatchesModel();                                  // belge modelin tam metninden bir kez kuruldu
        window.AppendConsoleBatch(inFlight, inFlightGen);             // uçuştaki bayat batch kurulumdan SONRA gelir
        AssertConsoleMatchesModel();                                  // ikinci kez inmez

        window.AppendConsoleBatch(Emit(10), window.ConsoleReseedGen); // görünür: yeni satırlar bir kez eklenir
        DispatcherPump.PumpUntil(() => DocumentLines().Count == vm.GetActiveLineCount(), TimeSpan.FromSeconds(2));
        AssertConsoleMatchesModel();

        window.SetSurfaceHidden(true);                                // gizli 2
        window.AppendConsoleBatch(Emit(20), window.ConsoleReseedGen);
        Assert.Equal(1, console.RunDocumentReplacedCount);            // gizliyken yeni kurulum yok

        window.SetSurfaceHidden(false);                               // gösterim 2
        DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount == 2, TimeSpan.FromSeconds(2));
        Assert.Equal(2, console.RunDocumentReplacedCount);            // gösterim başına TAM bir kurulum
        AssertConsoleMatchesModel();
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · temizlik 2b] <b>Gizli Sync: boşaltma ve yeni topoloji dönüşte tek ekranda buluşur.</b> Sync düğmesi ekranı baştan
    /// başlatır (<c>BlankPlanSurface</c> kapısızdır: liste gizliyken de boşalır ve "liste bayat" bayrağı düşer); motor yeni topolojiyi yine
    /// gizliyken getirir (bayrak yeniden kalkar, liste kurulmaz). Dönüşte liste modeldeki YENİ topolojinin tamamını gösterir: ne eski
    /// topoloji geri yazılır ne liste boş kalır. Sıranın tersi (boşaltma GÖSTERİMDEN sonra) için bkz.
    /// <see cref="A_sync_restart_between_the_show_and_the_resync_keeps_the_blanked_list_empty"/>.
    /// </summary>
    [StaFact]
    public async Task A_sync_that_blanks_the_screen_and_brings_a_new_topology_while_hidden_shows_only_the_new_topology_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(3));
        MainWindowHost.Realize(window);
        MainWindowHost.AcceptSends(vm);
        int itemsBefore = list.RowFlow.Items.Count, projectsBefore = vm.Projects.Count;
        Assert.True(itemsBefore > 0);                                   // ön-koşul: liste dolu
        window.SetSurfaceHidden(true);

        await MainWindowHost.StartSync(vm, SyncMode.Manual);            // gizliyken Sync düğmesi: ekran baştan başlar
        Assert.True(vm.PlanSurfaceRestarting);                          // ön-koşul: ekran gerçekten boşaltıldı
        Assert.Empty(list.RowFlow.Items);                               // ön-koşul: liste gizliyken de boşaldı
        MainWindowHost.ReplySync(vm, MainWindowHost.ProjectNames(5));   // motor yeni topolojiyi gizliyken getirdi: 3 -> 5

        Assert.Equal(5, vm.Projects.Count);                             // ön-koşul: model yeni topolojiyi tutuyor
        Assert.Empty(list.RowFlow.Items);                               // gizliyken liste kurulmadı

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();                                       // Loaded-öncelikli dönüş kurulumu pompasız koşsun (A4 testlerinin deseni)

        // Beklenen öğe sayısı modelden türer: eski liste + eklenen proje sayısı (3 -> 5 = +2); test topolojisi katmansızdır,
        // her proje bir liste öğesidir.
        Assert.Equal(itemsBefore + (vm.Projects.Count - projectsBefore), list.RowFlow.Items.Count); // yeni topolojinin TAMAMI: eski liste ne geri geldi ne liste boş kaldı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · temizlik 2b] <b>Gizlilikte gelen karar dönüşte ŞERİDİ de çapraz-sönümle oynatmaz.</b> Satırın dönüş kurulumu
    /// (<c>ApplyAllFresh</c>) noktanın mandalıyla birlikte şeridin mandalını da (<c>_stripeWasStartMode</c>) sıfırlar. Sıfırlanmazsa
    /// başlangıç modundan çıkan şerit, kararı gizliyken almış olsa da dönüşte çapraz-sönüm köprüsünü kurar (<c>StartMode.ShouldCrossFade</c>):
    /// bir saydamlık geçişi (ölçü değil) ama gösterimde çalışan bir animasyon clock'u — karar 15: tepsideyken animasyon yok, pencere gelince
    /// ekran tek seferde kurulur. <c>StartMode.FaintOpacity</c> v1.13.2'den beri 1.0'dır (eski değeri 0.5): köprü bugün 1.0 → 1.0 çizer ve
    /// GÖRÜNÜR bir sönüm yoktur, ama köprü yine de kurulur — pin bu yüzden opaklığı değil clock'u (<c>HasAnimatedProperties</c>) okur. Test
    /// boşta yeşil kalmasın diye iki ön-koşulu <c>VisualStatuses.IsStartMode</c> ile (şeridin kendi yüklemi) sınar: satır gizlemeden önce
    /// başlangıç modundadır ve karar gizliyken gelince ondan çıkmıştır. Nokta için aynı kural <see cref="A_decision_that_arrives_while_hidden_settles_the_status_dot_on_show_without_the_cross_fade"/>'de
    /// pinlidir ama şerit mandalı ayrı bir alandır ve o test onu görmez. Satırın hareketi AÇIK kurulur ki sönüm oynayabilsin.
    /// </summary>
    [StaFact]
    public void A_decision_that_arrives_while_hidden_settles_the_stripe_on_show_without_the_cross_fade()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, names);
        MainWindowHost.Realize(window);
        var rowVm = MainWindowHost.ProjectOf(vm, "P1");
        var row = RowViewOf(list, rowVm);
        row.AnimationsEnabledProvider = () => true;                     // sönüm oynayabilsin (headless varsayılanı reduced-motion)
        Assert.True(VisualStatuses.IsStartMode(rowVm.VisualStatus));    // ön-koşul: satır başlangıç modunda (şeridin son çizilen hâli)
        window.SetSurfaceHidden(true);

        MainWindowHost.RunBuild(vm, names);                             // karar gizliyken geldi: başlangıç modundan çıkış
        Assert.False(VisualStatuses.IsStartMode(rowVm.VisualStatus));   // ön-koşul: karar modele ulaştı (model gizliyken de yazılır)
        window.SetSurfaceHidden(false);                                 // satır dönüş kurulumunu kalıtsal sinyalin değişiminde yapar

        Assert.False(row.Stripe.HasAnimatedProperties);                 // mandal sıfırlanmazsa şerit tama çapraz-sönümle çıkar: çalışan clock
        Assert.Equal(1.0, row.Stripe.Opacity);                          // dejenere: köprü bugün 1.0 → 1.0; gözlenen şey yukarıdaki clock
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · temizlik 2b] <b>Gizliyken filtre değişimi alt çubuğun chip'lerini yenilemez.</b> A9'un chip testi yalnız
    /// <c>Counters</c> yolunu sürer; <c>ActiveFilters</c> aynı kapıdan geçer ama ayrı bildirimdir (<c>RunViewModel.ToggleFilter</c>).
    /// Gizliyken filtre açılır: chip'in işaretliliği yazılmaz ("chip'ler bayat" bayrağı kalkar); dönüşte chip'ler filtreyi yansıtır.
    /// Chip'ler <c>Loaded</c>'da kurulduğu için kabuk ekran dışı gerçek bir pencereye taşınır (<see cref="MainWindowHost.HostOffscreen"/>).
    /// </summary>
    [StaFact]
    public void The_action_bar_filter_chips_are_not_refreshed_while_hidden_and_reflect_the_filter_on_show()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(5));
        WithLoadedActionBar(window, (host, bar) =>
        {
            Assert.False(bar.BuildingChip.IsChecked);                              // ön-koşul: chip kurulu, filtre yok
            HiddenSurface.SetIsHidden(host, true);

            vm.ToggleFilter(ProjectFilter.Building);                               // gizliyken filtre açıldı

            Assert.True(vm.ActiveFilters.Contains(ProjectFilter.Building));        // ön-koşul: model filtreyi tuttu
            Assert.False(bar.BuildingChip.IsChecked);                              // kapısız: filtre değişimi chip'i hemen işaretlerdi

            HiddenSurface.SetIsHidden(host, false);

            Assert.True(bar.BuildingChip.IsChecked);                               // dönüşte chip filtreyi yansıtır
        });
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [perf Faz A · temizlik 2b] <b>Tepside başlayan çubuk chip'lerini ilk gösterimde güncel sayaçla BİR kez kurar.</b> Tepside başlayan
    /// pencerenin içeriği hiç yüklenmemiştir: chip'ler <c>Loaded</c>'da kurulur ve ondan önce gelen sayaç değişimleri "chip'ler bayat"
    /// bayrağını kaldırır (kurulacak chip yoktur). İlk gösterimdeki dönüş kurulumu kurulu olmayan chip'lere yazamaz
    /// (<c>RefreshChips</c> erken döner, bayrak yerinde kalır); ardından gelen <c>Loaded</c> chip'leri modelin O ANKİ sayacıyla kurar ve
    /// bayrağı düşürür — sonraki gizle/göster modele dokunulmadıkça chip'leri bir daha yazmaz. "Yazılmadı" kanıtı için chip değeri elle
    /// bozulur (model aynı değeri yeniden yazsaydı gözlenemezdi): bozuk değer yerinde kalmalıdır.
    ///
    /// <para><b>Kurulum:</b> bu testte gerçek bir "tepside başla" yolu yok (<c>MainWindow</c> hiç <c>Show()</c> edilmez) ve
    /// <c>HostOffscreen</c> içeriği ekran dışı pencereye alırken <c>Loaded</c>'ı kendi içinde teslim eder — ondan ÖNCE gizleyip sonra
    /// göstermek mümkün değildir. Eşdeğeri: pencere headless ağaçta (<c>Loaded</c> hiç gelmez) gizli işaretlenir ve sayaç gizliyken
    /// değişir; <c>HostOffscreen</c> ilk gösterimdir (içerik gerçek pencereye girer, gizli sinyal pencereden kalkar, <c>Loaded</c> chip'leri
    /// kurar). Üretimdeki sıra budur: görünürlük sinyali, ardından <c>Loaded</c>.</para>
    /// </summary>
    [StaFact]
    public void An_action_bar_that_starts_hidden_builds_its_chips_once_from_the_current_counters_on_first_show()
    {
        using var dir = new TempDir();
        string[] names = MainWindowHost.ProjectNames(5);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names);
        var bar = ActionBarOf(window);
        Assert.False(bar.IsLoaded);                                     // ön-koşul: headless ağaçta çubuk hiç yüklenmez
        Assert.Null(bar.BuildingChip);                                  // ön-koşul: chip'ler henüz kurulmadı (Loaded'da kurulurlar)
        window.SetSurfaceHidden(true);                                  // tepside başlama: içerik gizli işaretli ve yüklenmemiş
        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.StartProject(vm, names[0]);                      // gizliyken sayaç değişti: bildirim yalnız bayrağı kaldırdı
        Assert.Equal(1, vm.Counters.Building);                          // ön-koşul: model sayacı değişti

        WithLoadedActionBar(window, (host, _) =>                        // ilk gösterim: gizli sinyal pencereden kalkar, Loaded chip'leri kurar
        {
            Assert.NotNull(bar.BuildingChip);                           // Loaded chip'leri kurdu (yukarıda kurulu değildi)
            var building = ActionBarTests.ChipValue(bar.BuildingChip);
            var spinner = ActionBarTests.ChipSpinner(bar.BuildingChip);

            Assert.Equal("1", building.Text);                           // chip'ler modelin O ANKİ sayacıyla kuruldu
            Assert.Equal(Visibility.Visible, spinner.Visibility);
            Assert.Equal($"{vm.Counters.Total}", ActionBarTests.ChipValue(bar.SigmaChip).Text);

            building.Text = "unchanged-sentinel";                       // yeniden yazıldı mı gözlenebilsin: model "1"i yazardı
            HiddenSurface.SetIsHidden(host, true);
            HiddenSurface.SetIsHidden(host, false);                     // model değişmedi: dönüş chip'leri yeniden yazmaz

            Assert.Equal("unchanged-sentinel", building.Text);          // bayat kalan bayrak dönüşte ikinci bir tam kurulum koşturur
        });
        GC.KeepAlive(window);
    }
}
