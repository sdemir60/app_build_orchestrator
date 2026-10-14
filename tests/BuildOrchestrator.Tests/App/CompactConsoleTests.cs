using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.5 — K4] Kompakt konsolun VM'e ve başlığa bağlanışı. Süzgecin tablosu saf testindedir
/// (<see cref="CompactConsoleFilterTests"/>); burası onun koşu anlatısına NASIL girdiğini pinler:
/// <list type="bullet">
/// <item>Proje çıktısı anlatıya süzgeçten geçerek yazılır — projenin başlığı, hatalar, ilk üç tekil uyarı ve sonuçta (başarı, hata,
/// atlama) tek özet; "N lines" anlatının gerçek satırlarını sayar ve pompaya giden satırlar store'dakilerin aynısıdır.</item>
/// <item>Proje logu her zaman tamdır: açık sayfa canlı satırların TAMAMINI alır, bekleyen bir yüklemenin kuyruğu her satırı tutar.</item>
/// <item>Başlık (<c>building &lt;ad&gt;</c>) projenin ilk satırıyla aynı kilit bölümünde üretilir: pompadan geç gelen
/// <c>projectStarted</c>'a bağlı değildir, işlem başına bir kezdir ve projeyi listedeki adıyla anar.</item>
/// <item>Kip işlem başında yakalanır: koşu ortasındaki Save o koşuyu değiştirmez, bir sonraki işlem yeni kiple yazar; işlem yokken
/// değişiklik hemen geçer. Başlıktaki <c>compact</c> etiketi bu etkin kipi gösterir — yalnız anlatı modunda ve workspace varken.</item>
/// </list>
/// VM testleri çıplak VM'le koşar (üretim varsayılanı: kompakt açık); kabuk testleri fixture'ın kapalı varsayılanını açar
/// (<see cref="MainWindowHost.New"/>'ün <c>compactConsole</c>'u).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class CompactConsoleTests
{
    /// <summary>Proje logunun ilk satırı: motorun yazdığı MSBuild komut satırı (tırnakla başlar — bilgi satırı).</summary>
    private const string CommandLine = "\"C:\\Program Files\\MSBuild.exe\" C:\\p\\A.csproj -t:Build -v:minimal";

    private const string Error = "  C:\\r\\A.cs(11,1): error CS0103: The name 'x' does not exist";
    private const string Succeeded = "Build succeeded.";

    // Uyarı satırları ortak kurucudan (MainWindowHost.WarningLine): her satır numarası ayrı, tekil bir uyarıdır.

    /// <summary>Bir projenin tipik logu: komut satırı, on tekil uyarı, bir hata, bitiş satırı.</summary>
    private static string[] ProjectLog() =>
        [CommandLine, .. Enumerable.Range(1, 10).Select(i => WarningLine(i)), Error, Succeeded];

    private static void Stream(RunViewModel vm, string name, IReadOnlyList<string> lines, int firstLineNumber = 1)
    {
        for (int i = 0; i < lines.Count; i++) LogLine(vm, name, firstLineNumber + i, lines[i]);
    }

    private static IReadOnlyList<string> NarrativeSince(RunViewModel vm, int from) => vm.Narrative.Slice(from, vm.Narrative.Count);

    private static string Text(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    // ---------------------------------------------------------------- anlatı

    /// <summary>Brief tablosu VM düzeyinde: projenin başlığı, ilk üç tekil uyarı, hata, gizlenen yedi uyarının özeti; komut ve
    /// bitiş satırı anlatıya girmez. "N lines" anlatının gerçek satırlarını sayar — başlık ve özet dahil — ve pompaya giden satırlar
    /// store'dakilerin aynısıdır (dalga 1 değişmezi: belgenin ilk satırı store indeksidir).</summary>
    [Fact]
    public async Task Compact_mode_writes_the_header_errors_the_first_warnings_and_a_summary()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var console = DrainableBatcher();
        var vm = new RunViewModel(engine, console, () => "r1");
        Assert.True(vm.CompactConsole);                       // üretim varsayılanı
        StartBuild(vm, "A");
        StartProject(vm, "A");
        int before = vm.Narrative.Count;

        Stream(vm, "A", ProjectLog());
        SucceedProject(vm, "A");
        CompleteRun(vm, 1);

        Assert.Equal(["building A", WarningLine(1), WarningLine(2), WarningLine(3), Error, "A: 7 more warnings — CS1591 ×7 — see the project log"],
            NarrativeSince(vm, before));
        Assert.Equal(before + 6, vm.GetActiveLineCount());    // "N lines" = anlatının gerçek satırları
        Assert.Equal(vm.GetRunDocumentText(), await DrainPosted(console)); // pompaya giden = store'daki
    }

    /// <summary>Kapalı kipte bugünkü davranış: her satır anlatıya, başlık ve özet yok. İşlem yokken değişiklik hemen etkindir.</summary>
    [Fact]
    public async Task Full_mode_writes_every_line_and_no_header_or_summary()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { CompactConsole = false };
        Assert.False(vm.CompactConsoleInEffect);
        StartBuild(vm, "A");
        StartProject(vm, "A");
        int before = vm.Narrative.Count;

        Stream(vm, "A", ProjectLog());
        SucceedProject(vm, "A");

        Assert.Equal(ProjectLog(), NarrativeSince(vm, before));
    }

    /// <summary>[karar 5 · preflight C6] MSBuild bitiş özetinde her tanıyı yeniden yazar (girintili, proje ekli): anlatıda tekrar
    /// görünmez, özetin sayısına girmez — dört tekil uyarılı projede "1 more warning", "5" değil.</summary>
    [Fact]
    public async Task Msbuilds_end_of_build_repeats_are_neither_shown_again_nor_counted()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string Suffix = @" [C:\p\A.csproj]";
        string[] inline = [WarningLine(1) + Suffix, WarningLine(2) + Suffix, WarningLine(3) + Suffix, WarningLine(4) + Suffix];
        StartBuild(vm, "A");
        StartProject(vm, "A");
        int before = vm.Narrative.Count;

        Stream(vm, "A", [CommandLine, .. inline, Succeeded, .. inline.Select(w => "  " + w), "    4 Warning(s)", "    0 Error(s)"]);
        SucceedProject(vm, "A");

        Assert.Equal(["building A", inline[0], inline[1], inline[2], "A: 1 more warning — CS1591 ×1 — see the project log"],
            NarrativeSince(vm, before));
    }

    /// <summary>
    /// [karar 10 · preflight C7] <c>projectStarted</c> UI pompasından, satırlar IPC thread'inden doğrudan gelir: satırlar olayı
    /// geçebilir. Başlık projenin ilk satırıyla AYNI kilit bölümünde üretilir — her zaman kendi satırlarından önce ve işlem başına bir
    /// kez: geç gelen olay da, döngünün ikinci turu da onu yinelemez.
    /// </summary>
    [Fact]
    public async Task The_header_precedes_the_projects_lines_even_when_they_overtake_its_started_event()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartBuild(vm, "A");
        int before = vm.Narrative.Count;

        LogLine(vm, "A", 1, CommandLine);
        LogLine(vm, "A", 2, WarningLine(1));                         // projectStarted henüz işlenmedi
        StartProject(vm, "A");                                // pompadan geç gelir
        LogLine(vm, "A", 3, WarningLine(2));
        StartProject(vm, "A");                                // döngünün ikinci turu: yeni dispatch
        LogLine(vm, "A", 4, WarningLine(3));

        Assert.Equal(["building A", WarningLine(1), WarningLine(2), WarningLine(3)], NarrativeSince(vm, before));
    }

    /// <summary>[karar 10] Başlık ve özet projeyi koşunun adıyla anar — ad kimlikten türemeyebilir (<c>AssemblyName</c>). Koşunun
    /// önizlemesi adı verir; önizlemede olmayan bir kimlik dosya adına düşer (deterministik).</summary>
    [Fact]
    public async Task The_header_and_the_summary_name_the_project_as_the_run_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartBuild(vm, "A");
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(IdOf("A"), "Alpha.Core", true)]));
        int before = vm.Narrative.Count;

        Stream(vm, "A", [WarningLine(1), WarningLine(2), WarningLine(3), WarningLine(4)]);
        SucceedProject(vm, "A");
        LogLine(vm, "Z", 1, WarningLine(1, "CS0168"));                // önizlemede yok

        Assert.Equal(["building Alpha.Core", WarningLine(1), WarningLine(2), WarningLine(3),
                "Alpha.Core: 1 more warning — CS1591 ×1 — see the project log", "building Z", WarningLine(1, "CS0168")],
            NarrativeSince(vm, before));
    }

    /// <summary>[karar 10] Satırlar koşunun önizlemesini de geçebilir (o da pompadan gelir): ad, işlem başında satır listesinden
    /// yakalanır — listedeki ad dosya adından farklı olsa da başlık onu söyler.</summary>
    [Fact]
    public async Task Lines_that_overtake_the_runs_preview_still_name_the_project_as_the_list_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\src\OSYS" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node("A", 0) with { Name = "Alpha.Core" }], [], [], []));
        vm.OnEvent(SyncCompleted(1));
        AcceptSends(vm);

        _ = vm.BuildCommand.ExecuteAsync(null);               // yeni işlem: temizlik ilk await'ten ÖNCE, senkron
        int before = vm.Narrative.Count;
        LogLine(vm, "A", 1, WarningLine(1));                         // runStarted ve önizleme henüz pompada

        Assert.Equal(["building Alpha.Core", WarningLine(1)], NarrativeSince(vm, before));
    }

    /// <summary>[karar 11 · spec "her sonuç" · preflight C17] Özet her sonuçta gelir: başarı, hata ve atlama (ör. döngüsü yakınsamayan
    /// taşınan üye — önceki turlarda gizlenen uyarıları konsolda özetlenmeden kalmamalı). Her proje kendi özetini bir kez alır.</summary>
    [Fact]
    public async Task Every_project_result_brings_its_summary_succeeded_failed_and_skipped()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartBuild(vm, "A", "B", "C");
        foreach (var name in new[] { "A", "B", "C" })
        {
            StartProject(vm, name);
            Stream(vm, name, [.. Enumerable.Range(1, 5).Select(i => WarningLine(i))]);
        }

        SucceedProject(vm, "A");
        vm.OnEvent(new ProjectFailedEvent("r1", IdOf("B"), 100, "exit 1"));
        vm.OnEvent(new ProjectSkippedEvent("r1", IdOf("C"), SkipReasons.CycleNonConvergent));

        string narrative = vm.GetRunDocumentText();
        foreach (var name in new[] { "A", "B", "C" })
            Assert.Equal(1, Occurrences(narrative, $"{name}: 2 more warnings — CS1591 ×2 — see the project log"));
    }

    /// <summary>
    /// [fix 1 · M2] Motor koşu ortasında kaybolursa uçuştaki projenin sonucu hiç gelmez: gizlediği uyarılar özetsiz kalıp bir sonraki
    /// işlemin sıfırlamasında düşerdi. Koşunun HER bitişi — motor kaybı dahil — bekleyen özetleri anlatıya yazar, koşunun sonundaki
    /// boşaltmadan ÖNCE: özet koşu klasöründeki anlatıda da durur.
    /// <para>KIRMIZI: motor kaybı (<c>ReleaseAfterEngineLoss</c>) hiçbir proje sonucu üretmez — özet yazılmıyordu.</para>
    /// </summary>
    [Fact]
    public async Task A_project_cut_off_by_an_engine_loss_still_gets_its_summary()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        using var logs = new SpoolDir(vm.Narrative);
        StartBuildWithLogDirectory(vm, logs.Path, "A");
        StartProject(vm, "A");
        Stream(vm, "A", [.. Enumerable.Range(1, 5).Select(i => WarningLine(i))]);

        vm.OnEngineExited(1);                                       // motor öldü: A'nın sonucu hiç gelmeyecek

        const string Summary = "A: 2 more warnings — CS1591 ×2 — see the project log";
        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), Summary));
        Assert.Equal(1, Occurrences(logs.ReadText(), Summary));     // koşunun sonundaki boşaltmadan önce yazıldı: dosyada
    }

    /// <summary>[fix 1 · M7] Kompakt anlatı koşu klasörüne de kompakt iner: dosya store'un AYNISIDIR — başlık, gösterilen satırlar ve
    /// özet, aynı sırayla (görünüm tahliye edilen geçmişi oradan okur). Normal bitişte bekleyen özet yoktur: özet bir kez. Spool klasörle
    /// birlikte kapanır (<see cref="SpoolDir"/>): tutamaç ve klasör sızmaz.</summary>
    [Fact]
    public async Task A_compact_run_writes_the_same_lines_into_the_run_folder_in_the_stores_order()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        using var logs = new SpoolDir(vm.Narrative);
        StartBuildWithLogDirectory(vm, logs.Path, "A");
        StartProject(vm, "A");
        int before = vm.Narrative.Count;

        Stream(vm, "A", ProjectLog());
        SucceedProject(vm, "A");
        CompleteRun(vm, 1);                                         // koşunun sonu tamponu diske indirir

        Assert.True(vm.Narrative.IsSpooling, "ön-koşul: koşunun anlatısı koşu klasörüne yayıldı");
        Assert.Equal(["building A", WarningLine(1), WarningLine(2), WarningLine(3), Error,
                "A: 7 more warnings — CS1591 ×7 — see the project log"], NarrativeSince(vm, before));
        Assert.Equal(vm.GetRunDocumentText(), logs.ReadText());     // dosya = store, aynı sıra, özet bir kez
    }

    /// <summary>
    /// [fix 1 · M2b · M4] Kompakt kipte gizlenen bir proje satırı IPC thread'inde hiç ayırma yapmaz — VM'in kilidi altında, saniyede
    /// binlerce satır: ısınmadan sonra 0 bayt. Proje ad haritasında yoktur (önizleme gelmedi): başlığın adı projenin YALNIZ ilk satırında
    /// sorulur.
    /// <para>KIRMIZI: ad her satırda soruluyor ve haritada olmayan proje için geri dönüş adı her satırda yeniden türetiliyordu
    /// (yeni bir string); sınıflandırma da girintili satırı kopyalıyor, tanı aramasında string birleştiriyordu.</para>
    /// </summary>
    [Fact]
    public async Task A_hidden_project_line_allocates_nothing_on_the_ipc_path()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartBuild(vm, "Z");
        string[] texts =
            [CommandLine, WarningLine(1), WarningLine(2), WarningLine(3), WarningLine(4), "  Copying file from X to Y", "", "    0 Hata"];
        ProjectLogEvent[] events = [.. texts.Select((text, i) => new ProjectLogEvent("r1", IdOf("Z"), i + 1, text))];
        foreach (var e in events) vm.OnEvent(e);                    // ısınma: başlık yazıldı, ilk üç uyarı gösterildi — sonrası gizli

        long allocated = PerfMeasure.AllocatedBy(() =>
        {
            for (int round = 0; round < 200; round++)
                foreach (var e in events) vm.OnEvent(e);
        });

        Assert.Equal(0, allocated);
    }

    // ---------------------------------------------------------------- proje logu her zaman tam

    /// <summary>
    /// [karar 9 · preflight C1] Açık proje sayfası canlı satırların TAMAMINI alır — modeli, sayacı ve pompaya giden metni. Aynı
    /// <c>Post</c> anlatıya da sayfaya da gider (yönü kabuk seçer): süzülmüş metni postlamak açık sayfanın canlı satırlarını
    /// kaçırırdı. Anlatı proje modunda da birikir — süzülmüş hâliyle.
    /// </summary>
    [Fact]
    public async Task An_open_project_page_receives_every_line_while_the_narrative_stays_compact()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var console = DrainableBatcher();
        var vm = new RunViewModel(engine, console, () => "r1");
        AcceptSends(vm);
        StartBuild(vm, "A");
        StartProject(vm, "A");
        string a = IdOf("A");
        vm.SelectProject(a);
        var (load, request) = ProjectLogIpc.Request(vm, a);
        vm.OnEvent(ProjectLogIpc.Reply(request, a, "", through: 0)); // sayfa açıldı, henüz satır yok
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(a, vm.ActiveProjectId);                   // ön-koşul
        int before = vm.Narrative.Count;

        Stream(vm, "A", ProjectLog());

        string full = Text(ProjectLog());
        Assert.Equal(full, vm.GetProjectDocumentText(a));      // sayfanın modeli tam
        Assert.Equal(ProjectLog().Length, vm.GetActiveLineCount()); // başlığın sayacı tam
        Assert.Equal(["building A", WarningLine(1), WarningLine(2), WarningLine(3), Error], NarrativeSince(vm, before));
        Assert.Equal(full, await DrainPosted(console));             // açık sayfaya giden canlı satırlar tam
    }

    /// <summary>Bekleyen bir proje log yüklemesi kompakt kipte de her satırı tutar (canlı satır tamponu TAM olayları taşır) ve dikilen
    /// sayfa tamdır: disk + istekten sonraki kuyruk.</summary>
    [Fact]
    public async Task A_pending_project_log_keeps_and_stitches_every_line_while_compact()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        AcceptSends(vm);
        string[] log = ProjectLog();
        StartBuild(vm, "A");
        StartProject(vm, "A");
        Stream(vm, "A", log[..3]);                            // istekten önce: diskte
        string a = IdOf("A");
        vm.SelectProject(a);
        var (load, request) = ProjectLogIpc.Request(vm, a);

        Stream(vm, "A", log[3..], firstLineNumber: 4);        // istekten sonra: dikişin kuyruğu
        Assert.Equal(log.Length - 3, vm.LiveLineCount);        // gizlenen satırlar da tamponda

        vm.OnEvent(ProjectLogIpc.Reply(request, a, Text(log[..3]), through: 3));
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Text(log), vm.GetProjectDocumentText(a));
        Assert.Equal(0, vm.LiveLineCount);
    }

    // ---------------------------------------------------------------- kip işlem başına

    /// <summary>[karar 8 · spec "bir sonraki işlemde" · preflight C4] Koşu ortasındaki Save koşunun kipini değiştirmez: süzgeç o koşu
    /// boyunca aynı kalır ("ilk 3" kuralı ortada bozulmaz) ve başlık etiketi ekrandaki anlatının kipini söylemeye devam eder. Bir
    /// sonraki işlem yeni kiple yazar.</summary>
    [Fact]
    public async Task A_save_during_a_run_keeps_its_mode_and_the_next_operation_takes_the_new_one()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\src\OSYS" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node("A", 0)], [], [], []));
        vm.OnEvent(SyncCompleted(1));
        AcceptSends(vm);
        StartBuild(vm, "A");
        StartProject(vm, "A");
        LogLine(vm, "A", 1, WarningLine(1));

        await vm.ApplySettingsAsync([], vm.RootPath, [], compactConsole: false); // koşu ortasında Save
        Assert.False(vm.CompactConsole);
        Assert.True(vm.CompactConsoleInEffect);               // koşunun kipi değişmez

        Stream(vm, "A", [WarningLine(2), WarningLine(3), WarningLine(4), WarningLine(5), Succeeded], firstLineNumber: 2);
        SucceedProject(vm, "A");
        CompleteRun(vm, 1);
        string run = vm.GetRunDocumentText();
        Assert.Equal(1, Occurrences(run, "A: 2 more warnings — CS1591 ×2 — see the project log"));
        Assert.Equal(0, Occurrences(run, Succeeded));
        Assert.True(vm.CompactConsoleInEffect);               // yeni işlem yok: ekrandaki anlatı hâlâ kompakt yazılmıştır

        _ = vm.BuildCommand.ExecuteAsync(null);               // yeni işlem: kip burada yakalanır
        Assert.False(vm.CompactConsoleInEffect);
        StartBuild(vm, "A");
        StartProject(vm, "A");
        LogLine(vm, "A", 1, Succeeded);
        Assert.Equal(1, Occurrences(vm.GetRunDocumentText(), Succeeded));
    }

    /// <summary>[spec §6.5 "sayaçlar işlem başında sıfırlanır" · preflight C9d] Yeni işlem süzgecin sayaçlarını ve başlıklarını
    /// sıfırlar: aynı uyarılar yeni koşuda yeniden ilk üç olarak görünür, başlık ve özet yeniden yazılır.</summary>
    [Fact]
    public async Task A_new_operation_shows_each_projects_first_warnings_again()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"C:\src\OSYS" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node("A", 0)], [], [], []));
        vm.OnEvent(SyncCompleted(1));
        AcceptSends(vm);
        string[] warnings = [.. Enumerable.Range(1, 5).Select(i => WarningLine(i))];
        StartBuild(vm, "A");
        StartProject(vm, "A");
        Stream(vm, "A", warnings);
        SucceedProject(vm, "A");
        CompleteRun(vm, 1);

        _ = vm.BuildCommand.ExecuteAsync(null);               // yeni işlem
        StartBuild(vm, "A");
        StartProject(vm, "A");
        int before = vm.Narrative.Count;
        Stream(vm, "A", warnings);
        SucceedProject(vm, "A");

        Assert.Equal(["building A", WarningLine(1), WarningLine(2), WarningLine(3), "A: 2 more warnings — CS1591 ×2 — see the project log"],
            NarrativeSince(vm, before));
    }

    // ---------------------------------------------------------------- başlık etiketi

    /// <summary>Kabuk: başlıktaki <c>compact</c> etiketi VM'in etkin kipini izler — işlem yokken ayar değişince hemen.</summary>
    [StaFact]
    public void The_header_shows_the_compact_tag_only_while_the_narrative_is_written_compact()
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewWithProjects(dir, ("A", null));
        var tag = window.Shell.ConsoleHeaderControl.CompactText;
        Assert.Equal(Visibility.Collapsed, tag.Visibility);   // fixture kapalı açar

        vm.CompactConsole = true;
        DispatcherPump.DrainToIdle();
        Assert.Equal(Visibility.Visible, tag.Visibility);

        vm.CompactConsole = false;
        DispatcherPump.DrainToIdle();
        Assert.Equal(Visibility.Collapsed, tag.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 12 · preflight C8] Etiket yalnız anlatı modunda ve workspace varken görünür: proje sayfası her zaman tamdır
    /// (orada "compact" yalan olurdu) ve workspace yokken sayacın kendisi de yoktur.</summary>
    [StaFact]
    public void The_compact_tag_shows_only_in_narrative_mode_with_a_workspace()
    {
        var header = new ConsoleHeader();
        header.SetCompact(true);
        Assert.Equal(Visibility.Visible, header.CompactText.Visibility);

        header.ShowProjectLog(ConsoleHeaderRow.For("A", ProjectRowState.Succeeded, inCycle: false, depIssues: null, namePrefix: ""), 12);
        Assert.Equal(Visibility.Collapsed, header.CompactText.Visibility);
        header.ShowNarrative(12);
        Assert.Equal(Visibility.Visible, header.CompactText.Visibility);

        header.SetHasWorkspace(false);
        Assert.Equal(Visibility.Collapsed, header.CompactText.Visibility);
        header.SetHasWorkspace(true);
        Assert.Equal(Visibility.Visible, header.CompactText.Visibility);

        header.SetCompact(false);
        Assert.Equal(Visibility.Collapsed, header.CompactText.Visibility);
    }
}
