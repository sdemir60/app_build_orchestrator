using System.Diagnostics;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
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
        for (int i = 0; i < 200; i++) vm.OnEvent(new ProjectLogEvent("r1", MainWindowHost.IdOf("A"), i + 1, $"line {i}"));
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
        vm.OnEvent(new ProjectLogEvent("r1", MainWindowHost.IdOf("A"), 1, "first line"));
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
        vm.OnEvent(new ProjectLogEvent("r1", MainWindowHost.IdOf("A"), 1, "line 0"));
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
    /// [perf Faz A · A3] <b>Gizli pencerede olay akışı satır kurmaz.</b> Tepsideyken derlenen bir koşunun her olayı
    /// kimsenin görmediği akışa satır ekliyor, sayacı yazıyor ve en yeni satırın daktilosunu başlatıyordu. Satırların
    /// kaynağı model (<c>RunViewModel.StreamEvents</c>; 150 kırpma kuralı da onda) zaten tam durur: görünüm gizliyken
    /// yalnız "ekran modelin gerisinde" bayrağını kaldırır, görününce satırları, sayacı ve aktif satırı modelden TEK
    /// geçişte kurar. Satırlar yazılmış hâliyle konur: gizliyken gelmiş olaylar pencere gelince sırayla yazılmaya
    /// kalkmaz (daktilo geriye dönük oynamaz). Sinyal tek görünümde doğrudan yazılır
    /// (<c>HiddenSurface.SetIsHidden</c>); kalıtımı <c>HiddenSurfacePropertyTests</c> pinler.
    /// </summary>
    [StaFact]
    public void Event_stream_rows_are_not_built_while_hidden_and_are_rebuilt_in_one_pass_without_typing_on_show()
    {
        using var dir = new TempDir();
        string[] names = [.. Enumerable.Range(0, 40).Select(i => $"P{i}")];
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names.Select(n => (n, (string?)null)).ToArray());
        var view = new BuildOrchestrator.App.Views.EventStreamView { AnimationsEnabledProvider = () => true, DataContext = vm };
        HiddenSurface.SetIsHidden(view, true);
        int rowsBefore = view.Rows.Count;
        string counterBefore = view.Counter.Text;

        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        MainWindowHost.FinishBuild(vm, names);

        Assert.True(vm.StreamEvents.Count > rowsBefore); // ön-koşul: model akışa satır üretti
        Assert.Equal(rowsBefore, view.Rows.Count);       // KIRMIZI: bugün gizliyken de her olay satır kurar
        Assert.Null(view.TypingRow);                     //          ve en yeni satırın daktilosunu başlatır
        Assert.Equal(counterBefore, view.Counter.Text);  // sayaç da yazılmaz

        HiddenSurface.SetIsHidden(view, false);

        Assert.Equal(vm.StreamEvents.Count, view.Rows.Count); // tek geçişte modelden kuruldu
        Assert.Equal($"{vm.StreamEventCount} events", view.Counter.Text);
        Assert.All(view.Rows, row => Assert.False(row.IsTyping)); // daktilo geriye dönük oynamaz
        Assert.Null(view.TypingRow);
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
        string[] names = [.. Enumerable.Range(0, 177).Select(i => $"P{i}")];
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names.Select(n => (n, (string?)null)).ToArray());
        var graph = window.Shell.GraphHost;
        window.SetSurfaceHidden(true);
        int pushesBefore = graph.UpdateStatusesCallCount;

        MainWindowHost.StartBuild(vm, names); // koşu fazı: model Running olur, graf Idle'da kalmalı
        foreach (var name in names)           // 177 proje x started/succeeded: her biri bir statü itişi isterdi
        {
            vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf(name), name));
            vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf(name), 100));
        }
        string selected = MainWindowHost.IdOf("P5");
        vm.SelectProject(selected);

        Assert.True(vm.IsRunUnderway);                                // ön-koşul: koşu sürüyor — model Running
        Assert.Equal(pushesBefore, graph.UpdateStatusesCallCount);    // KIRMIZI: bugün her olay grafa itilir
        Assert.Equal("Idle", graph.RunPhase.ToString());              // faz itilmedi
        Assert.Null(graph.SelectedNode);                              // seçim itilmedi

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();

        Assert.Equal(pushesBefore + 1, graph.UpdateStatusesCallCount); // TAM bir statü itişi
        Assert.Equal("Running", graph.RunPhase.ToString());            // faz modele eşit
        Assert.Equal(selected, graph.SelectedNode);                    // seçim modele eşit
        GC.KeepAlive(window);
    }

    /// <summary>Kontrol: AYNI olay akışı görünür pencerede grafa itilir — yani yukarıdaki test sayacın ölü olmasından
    /// değil, gizli sinyalden geçer.</summary>
    [StaFact]
    public void Graph_pushes_still_reach_the_graph_while_the_surface_is_visible()
    {
        using var dir = new TempDir();
        string[] names = ["A", "B", "C"];
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, names.Select(n => (n, (string?)null)).ToArray());
        var graph = window.Shell.GraphHost;
        int pushesBefore = graph.UpdateStatusesCallCount;

        MainWindowHost.StartBuild(vm, names);
        foreach (var name in names)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf(name), name));
            vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf(name), 100));
        }

        Assert.True(graph.UpdateStatusesCallCount > pushesBefore);
        Assert.Equal("Running", graph.RunPhase.ToString());
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
        vm.OnEvent(new ProjectLogEvent("r1", MainWindowHost.IdOf("A"), 1, "line 0"));
        long staleGen = window.ConsoleReseedGen;           // pompanın bu batch'i okuduğu andaki nesil
        window.AppendConsoleBatch("line 0\n", staleGen);   // gizliyken: belgeye basılmaz, "ekran bayat" bayrağı kalkar
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();                          // dönüşte belge modelden BİR kez kurulur (sentinel yazılır)
        Assert.Equal(1, console.RunDocumentReplacedCount); // ön-koşul: kurulum oldu
        string rebuilt = console.EditorControl.Document.Text;

        window.AppendConsoleBatch("line 0\n", staleGen);   // uçuştaki bayat batch, kurulumdan SONRA gelir

        Assert.Equal(rebuilt, console.EditorControl.Document.Text);  // KIRMIZI ayrı batcher'larda: belgeye ikinci kez iner
        Assert.True(window.ConsoleReseedGen > staleGen, "dönüş kurulumu reseed nesli ilerletmeli (sentinel)");
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
        var row = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("A"));
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
}
