using System.Diagnostics;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
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

    /// <summary>[perf Faz A · A3/A4] <c>P0..P{count-1}</c> proje adları — hem fixture'a hem koşu sürücülerine gider.</summary>
    private static string[] Names(int count) => [.. Enumerable.Range(0, count).Select(i => $"P{i}")];

    /// <summary><c>MainWindowHost.NewWithProjects</c>'in beklediği (ad, içerik) çiftleri; içerik yok.</summary>
    private static (string, string?)[] ProjectPairs(string[] names) => [.. names.Select(n => (n, (string?)null))];

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
        string[] names = Names(40);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
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
        string[] names = Names(40);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);
        Assert.NotEmpty(vm.StreamEvents.Where(e => e.GlowEligible));            // ön-koşul: parıldayacak (done + hatasız) satır var
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
        string[] names = Names(177);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        var graph = window.Shell.GraphHost;
        window.SetSurfaceHidden(true);
        int pushesBefore = graph.UpdateStatusesCallCount;

        MainWindowHost.StartBuild(vm, names);    // koşu fazı: model Running olur, graf Idle'da kalmalı
        MainWindowHost.BuildProjects(vm, names); // 177 proje x started/succeeded: her biri bir statü itişi isterdi
        string selected = MainWindowHost.IdOf("P5");
        vm.SelectProject(selected);

        Assert.True(vm.IsRunUnderway);                                // ön-koşul: koşu sürüyor — model Running
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
        string[] names = Names(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
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
        string[] names = Names(20);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
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
        string[] names = Names(5);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        var ribbon = window.Shell.Ribbon;
        string textBefore = ribbon.PhaseText.Text;
        int passesBefore = ribbon.RebuildCount;
        window.SetSurfaceHidden(true);

        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf(names[0]), names[0])); // bir proje derleniyor: building chip'i
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
        string[] names = Names(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
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
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(Names(3)));
        var menu = window.Shell.BuildMenuControl;
        int rebuildsBefore = menu.RefreshRowsCount;
        Assert.Contains("All 3 projects", menu.Items[1].Desc);   // ön-koşul: menü modelin toplamını gösteriyor
        window.SetSurfaceHidden(true);

        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));    // gizliyken Sync yeni bir proje getirdi: toplam 3 -> 4

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
        string[] names = Names(3);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        var menu = window.Shell.BuildMenuControl;
        int rebuildsBefore = menu.RefreshRowsCount;
        int countersNotifications = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildOrchestrator.App.ViewModels.RunViewModel.Counters)) countersNotifications++;
        };

        MainWindowHost.RunBuild(vm, names);                      // toplam sabit: her proje olayı Counters yayınlar

        Assert.True(countersNotifications > 0, "ön-koşul: koşu Counters yayınladı — yoksa 'yeniden kurulmadı' iddiası boşta yeşil olurdu");
        Assert.Equal(rebuildsBefore, menu.RefreshRowsCount);     // KIRMIZI: bugün her Counters bildirimi menüyü yeniden kurar

        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));    // toplam 3 -> 4

        Assert.Equal(rebuildsBefore + 1, menu.RefreshRowsCount); // toplam değişince TEK kurulum
        Assert.Contains("All 4 projects", menu.Items[1].Desc);
        GC.KeepAlive(window);
    }

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
        string[] names = Names(5);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        MainWindowHost.Realize(window); // satır container'ları üretilir (liste topolojiden SONRA doldu)
        Assert.NotEmpty(list.RevealRows);   // ön-koşul: satırlar gerçek ağaçta kuruldu
        var rowVm = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P1"));
        var row = list.RevealRows.Single(r => ReferenceEquals(r.DataContext, rowVm));
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
        string[] names = Names(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        MainWindowHost.Realize(window);
        var rowVm = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P1"));
        var row = list.RevealRows.Single(r => ReferenceEquals(r.DataContext, rowVm));
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
        string[] names = Names(3);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
        MainWindowHost.Realize(window);
        var rowVm = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P1"));
        var row = list.RevealRows.Single(r => ReferenceEquals(r.DataContext, rowVm));
        window.SetSurfaceHidden(true);
        MainWindowHost.RunBuild(vm, names);                                            // satır bayat: bildirim bayrağı kaldırdı
        row.DataContext = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P2"));  // container yeniden kullanımı: yeni model, ApplyAll tam kurulum
        int appliesAfterRebind = row.ApplyAllCount;

        window.SetSurfaceHidden(false);

        Assert.Equal(appliesAfterRebind, row.ApplyAllCount);                           // KIRMIZI: bayrak düşmediği için dönüş ApplyAll'u bir kez daha koşar
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
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(Names(3)));
        MainWindowHost.Realize(window);
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3)); // ilk topolojinin belirişi oynadı
        int revealBefore = list.RevealGeneration;
        int itemsBefore = list.RowFlow.Items.Count;
        int rebuilds = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => rebuilds++;
        window.SetSurfaceHidden(true);

        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));   // gizliyken Sync yeni bir proje getirdi

        Assert.Equal(4, vm.Projects.Count);                     // ön-koşul: model topolojisi değişti
        Assert.Equal(0, rebuilds);                              // KIRMIZI kapısız: bugün topoloji listeyi hemen yeniden kurar
        Assert.Equal(itemsBefore, list.RowFlow.Items.Count);

        window.SetSurfaceHidden(false);
        window.ResyncAfterShow();                               // Loaded-öncelikli kurulum pompasız koşsun (A4 testlerinin deseni)
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(400)); // beliriş verilseydi burada oynardı

        Assert.True(rebuilds > 0);                              // liste dönüşte kuruldu
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
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(Names(3)));
        MainWindowHost.Realize(window);
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        int revealBefore = list.RevealGeneration;
        int itemsBefore = list.RowFlow.Items.Count;

        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));

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
        string[] names = Names(5);
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(names));
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
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(Names(3)));
        MainWindowHost.Realize(window);
        window.SetSurfaceHidden(true);
        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));   // gizliyken topoloji: liste bayat
        window.SetSurfaceHidden(false);                         // Loaded-öncelikli ResyncAfterShow kuyrukta (pompa yok)
        int rebuilds = 0;
        list.RowFlow.ItemContainerGenerator.ItemsChanged += (_, _) => rebuilds++;

        MainWindowHost.ReplySync(vm, ProjectPairs(Names(5)));   // dönüş ile Loaded arasında GÖRÜNÜR topoloji kurulumu
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
        var (window, vm, list) = MainWindowHost.NewWithProjects(dir, ProjectPairs(Names(3)));
        MainWindowHost.Realize(window);
        MainWindowHost.AcceptSends(vm);
        window.SetSurfaceHidden(true);
        MainWindowHost.ReplySync(vm, ProjectPairs(Names(4)));   // gizliyken topoloji: liste bayat
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
    private static (BuildOrchestrator.App.MainWindow Window, RunViewModel Vm) NewRunningWindow(TempDir dir, Func<long> nowMs)
    {
        string[] names = Names(4);
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, nowMs, ProjectPairs(names));
        MainWindowHost.PreviewBuild(vm, names);
        MainWindowHost.StartBuild(vm, names);
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("P1"), "P1"));
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("P1"), 60_000)); // uzun ortalama: ETA her tikte gözle görülür azalır
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("P0"), "P0"));
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
        var row = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P0"));
        Assert.Equal(ProjectRowState.Started, row.State); // ön-koşul: satırın canlı süresi var
        long startedAtMs = now;                           // koşunun da satırın da başladığı an
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
        window.SetSurfaceHidden(false);
        window.ResyncAfterShow(); // Loaded-öncelikli kurulum pompasız koşsun (A4 testlerinin deseni)

        Assert.Equal(now - startedAtMs, vm.ElapsedMs);                 // dönüşte süreler modele yetişti
        Assert.Equal(now - startedAtMs, row.DurationMs);
        Assert.Equal(1, writes.Count(n => n == nameof(vm.ElapsedMs))); // ...TEK tikle
        Assert.Equal(1, writes.Count(n => n == nameof(row.DurationMs)));
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
        var row = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("P0"));
        now += 200;

        window.OnElapsedTick();

        Assert.Equal(200, vm.ElapsedMs);
        Assert.Equal(200, row.DurationMs);
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
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, () => now, ProjectPairs(Names(2)));
        MainWindowHost.AcceptSends(vm);
        int ready = 0;
        vm.ExitReady += (_, _) => ready++;
        MainWindowHost.StartBuild(vm, "P0", "P1");
        window.SetSurfaceHidden(true); // koşu sürerken tepsiye iner
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
        GC.KeepAlive(window);
    }
}
