using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3a] Konsol modları (design-v1 §2.5): başlık anlatı↔proje-log geçişi + "N lines" TAM tampon sayacı +
/// boş-durum metinleri (birebir/verbatim). Header kod-tarafı sürülür (küçük test edilebilir yüzey).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleModesTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // ---------------------------------------------------------------- başlık modları

    // [v1.18.0 §9] Sol grubun tamamı (Back + ad + statü + rozetler) artık TEK bir konteynerdir
    // (ProjectLogGroup, Console/ConsoleHeader.xaml) — mod değişince o hücre bir kerede aç/kapa olur; alt
    // öğelerin KENDİ Visibility'si ayrı ayrı sürülmez. Bu yüzden "gizli mi" iddiası artık gruba, "doğru
    // dolduruldu mu" iddiası içeriğe (Text/Status/ToolTip) bakar — style/ikon/aralık pinleri
    // ConsoleHeaderDesignTests'tedir (realize gerektirir).

    [StaFact]
    public void Header_narrative_mode_shows_caps_label_and_hides_the_project_log_group()
    {
        var header = new ConsoleHeader();

        header.ShowNarrative(12);

        Assert.Equal(ConsoleHeader.HeaderMode.Narrative, header.Mode);
        Assert.Equal(Visibility.Visible, header.ConsoleLabel.Visibility);
        Assert.Equal(Visibility.Collapsed, header.ProjectLogGroup.Visibility);
        Assert.Equal("12 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_project_log_mode_shows_name_status_and_dep_issue_badge_with_full_names_in_the_tooltip()
    {
        var header = new ConsoleHeader();

        // [v1.18.0] Tooltip'in TAM listeyi yazdığını (satırın "+N" kısaltmasının AKSİNE) görmek için iki isim.
        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Sales.Core", ProjectRowState.Failed, inCycle: false,
            depIssues: ["OSYS.Sales.Data", "OSYS.Sales.Contracts"], namePrefix: "OSYS."), 87);

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.Equal(Visibility.Collapsed, header.ConsoleLabel.Visibility);
        Assert.Equal(Visibility.Visible, header.ProjectLogGroup.Visibility);
        Assert.Equal("OSYS.Sales.Core", header.ProjectNameText.Text);
        Assert.Equal("Failed", header.StatusNameText.Text);
        Assert.Equal(GraphStatus.Failed, header.StatusGlyphIcon.Status);
        Assert.Equal(Visibility.Visible, header.DepIssueBadge.Visibility);
        Assert.Equal("Dependency issue: Sales.Data, Sales.Contracts — last successful output referenced",
            header.DepIssueTooltip.Content);
        Assert.Equal(Visibility.Collapsed, header.CycleBadge.Visibility);
        Assert.Equal("87 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_project_log_shows_the_cycle_badge_independently_of_the_dep_issue_badge()
    {
        // [v1.18.0] Prototipte (BuildApp.jsx:2615-2628) ikisi de KENDİ koşuluna bağlıdır ve AYNI ANDA
        // görünebilir — satırdaki tek üçgenin öncelik sırasının (RowWarning.For) AKSİNE.
        var header = new ConsoleHeader();

        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Base", ProjectRowState.Started, inCycle: true,
            depIssues: null, namePrefix: "OSYS."), 3);

        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        Assert.Equal(Visibility.Visible, header.CycleBadge.Visibility);
        Assert.Equal(RowWarning.InCycle, header.CycleTooltip.Content);
        Assert.Equal(GraphStatus.Building, header.StatusGlyphIcon.Status); // Started → Building
    }

    [StaFact]
    public void Header_project_log_without_warnings_hides_both_badges_and_switching_back_hides_the_group()
    {
        var header = new ConsoleHeader();

        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Base", ProjectRowState.Succeeded, inCycle: false,
            depIssues: null, namePrefix: ""), 5);
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        Assert.Equal(Visibility.Collapsed, header.CycleBadge.Visibility);
        Assert.Equal("Succeeded", header.StatusNameText.Text);

        header.ShowNarrative(3); // geri dönüş moddu tekrar anlatıya çevirir
        Assert.Equal(ConsoleHeader.HeaderMode.Narrative, header.Mode);
        Assert.Equal(Visibility.Collapsed, header.ProjectLogGroup.Visibility);
        Assert.Equal("3 lines", header.LinesText.Text);
    }

    [StaFact]
    public void Header_back_button_raises_BackRequested()
    {
        var header = new ConsoleHeader();
        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Base", ProjectRowState.Succeeded, inCycle: false,
            depIssues: null, namePrefix: ""), 0);
        bool raised = false;
        header.BackRequested += (_, _) => raised = true;

        header.BackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.True(raised);
    }

    [StaFact]
    public void SetLineCount_updates_only_the_counter_text()
    {
        var header = new ConsoleHeader();
        header.ShowNarrative(0);

        header.SetLineCount(1440);

        Assert.Equal("1440 lines", header.LinesText.Text);
    }

    // ---------------------------------------------------------------- [D4/Solution B] reseed flicker: senkron doc-set

    [StaFact] // [D4 review §3] gerçek adıyla: SeedProjectDocument'ın SENKRON doküman-set'ini doğrular (tam
              // orchestration/guard dizisi ConsoleForwardWiringTests seam testlerinde ayrıca sürülür).
    public async Task SeedProjectDocument_swaps_the_body_synchronously_in_the_same_ui_turn_as_the_header()
    {
        // [D4 Step 1] Mod değişiminde konsol dokümanı TIKLAMA ANINDA (senkron, pump'a bağlı DEĞİL) kurulur —
        // başlık ile gövde AYNI UI turunda değişir. Kanıt: pump HİÇ tick atmayan bir batcher. Eski (pump-apply)
        // reseed yolunda apply yalnız pump sentinel'e uğrayınca çağrılırdı → gövde eski içeriği gösterirdi (RED);
        // Solution B'de doküman senkron kurulur → gövde eski içeriği ARTIK göstermez (GREEN).
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var batcher = new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)); // pump asla ilerlemez
        var vm = new RunViewModel(engine, batcher, () => "r1");
        var view = new ConsoleView();
        var header = new ConsoleHeader();

        // Önceki anlatı/run içeriği + gövdeye senkron kur.
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));
        view.ShowRunDocument(vm.GetRunDocumentText());
        const string previousRunText = "Sync complete — 7 changed projects";
        Assert.Contains(previousRunText, view.Document.Text);

        // Bir kart seçilmiş gibi: canlı log tamponlanır ve dikişle ActiveProjectId kurulur (kart-tıklaması yolu).
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 2, "Restored A.csproj"));
        vm.SelectProject(projectId); // [D4 review §2] üretimde seçim load'dan önce kurulur (proje modu koşulu)
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // Tıklama anı: başlık + gövde SENKRON proje-loguna geçer (pump beklenmeden).
        header.ShowProjectLog(ConsoleHeaderRow.For("A", ProjectRowState.Started, inCycle: false,
            depIssues: null, namePrefix: ""), vm.GetActiveLineCount());
        vm.SeedProjectDocument(projectId, text =>
            view.PlayCascade(text.Length == 0 ? [] : text.TrimEnd('\n').Split('\n')));

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.DoesNotContain(previousRunText, view.Document.Text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- boş-durum metinleri (verbatim §2.5)

    /// <summary>
    /// <b>Logu olmayan bir projenin sayfası GERÇEK durumunu anlatır: gerekçe + kanıt.</b>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia, design-v1'in ÖRNEK metinlerini birebir pinliyordu
    /// (<c>Skipped(sha)</c>, <c>Queued(deps)</c>) — içlerinde uydurma veri vardı ("yesterday 18:42") ve ikisi de
    /// üretimde HİÇ ÇAĞRILMIYORDU: yüzey kurulmuş, hiçbir yere bağlanmamıştı. Yani pinlenen tek şey
    /// kullanılmayan bir literaldi. Değişme gerekçesi (kullanıcı): her projeye tıklandığında sayfası açılmalı ve
    /// o sayfa, log yoksa bile projenin o anki durumunu söylemeli.</para>
    ///
    /// <para>Metin statüyü TEKRAR ETMEZ (başlık onu zaten gösterir): ilk satır NEDEN, ikinci satır elde ne
    /// olduğu. Kanıt gerekçeyi tekrarlıyorsa ("hiç derlenmedi") yazılmaz.</para>
    /// </summary>
    [Fact]
    public void An_empty_project_page_states_the_reason_and_the_evidence()
    {
        // [DEĞİŞEN KURAL — v1.16.0] Kanıt satırı eskiden yalnız revizyonu söylüyordu ("Last built a3f81c2"),
        // çünkü son başarılı derlemenin ZAMANI bu tarafta yoktu. Motor artık onu da taşıyor; satır iki soruyu
        // birlikte cevaplıyor ve yaş biçimi satırın "up to date · 2h" etiketiyle AYNI (kullanıcı iki yerde iki
        // farklı zaman görmez).
        var now = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
        var twoHoursAgo = now.AddHours(-2);
        // Kısaltma YALNIZ gerçek bir git sha'sına (40 hex) uygulanır — kanıt satırı da o kuralı okur.
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        // Atlanmış — motorun söylediği gerekçeyle (SkipReasons, tek doğruluk kaynağı).
        Assert.Equal(
            ["Up to date — nothing to compile in this run.", "Last successful build: 2h ago (a3f81c2)"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped,
                skipReason: SkipReasons.UpToDate, currentSha: sha, lastBuiltAt: twoHoursAgo), now));

        // Koşu uçuşta, sıra bu satırda değil — plan gerekçesi will-build'den gelir.
        // [Task 1 review fix — I-2] "Queued" artık yalnız runActive'e değil, BU koşunun kendi kuyruğuna
        // (InRunQueue) da bağlı — bkz. Row helper'ının ve ConsoleEmptyState.Pending'in yorumu.
        Assert.Equal(
            ["Queued — the signature changed since the last successful build.", "Last successful build: 2h ago (a3f81c2)"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha,
                runActive: true, inRunQueue: true, lastBuiltAt: twoHoursAgo), now));

        // Koşu uçuşta AMA bu satır BU koşunun kendi kuyruğunda DEĞİL (tek proje koşusunda bayat bir komşu) —
        // "Queued" DEĞİL, düz plan metni.
        Assert.Equal(
            ["Will build — the signature changed since the last successful build.", "Last successful build: 2h ago (a3f81c2)"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha,
                runActive: true, inRunQueue: false, lastBuiltAt: twoHoursAgo), now));

        // Koşu YOK: aynı plan "Will build" diye okunur — kuyruk, ancak bir koşu varken vardır.
        // Zaman bilinmiyorsa (eski kayıt) satır yalnız revizyonu söyler — uydurma bir yaş yazılmaz.
        Assert.Equal(
            ["Will build — its last build failed.", "Last successful build: a3f81c2"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.LastFailed, currentSha: sha), now));

        // Hiç derlenmemiş: kanıt satırı gerekçeyi tekrarlayacağı için YAZILMAZ.
        Assert.Equal(
            ["Will build — this tool has never built it."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.NeverBuilt)));

        // Döngü üyeliği plandan ÖNCE gelir: Sync bir SCC üyesine her zaman false verir (ARCHITECTURE §7.4),
        // o "false"u "güncel" diye okumak yalan olurdu.
        Assert.Equal(
            ["In a dependency cycle — Build never compiles one; use Resolve cycles.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false, inCycle: true)));

        // Sync hiç koşmadı: hollow. "Güncel" demek yalan olurdu.
        Assert.Equal(
            ["Not analysed yet — run Sync to see what this project will do.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending)));

        // Derleniyor: kanıt henüz oluşmadı, akış birazdan gelir — TEK satır.
        Assert.Equal(
            ["No log yet — output streams here once the build starts."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Started)));
    }

    /// <summary>[Task 2 review fix I-1] Resolve cycles'ta kapsam dışı bir satır motorun pre-skip'ini State'e
    /// TAŞIMAZ (bkz. RunViewModel.OnProjectSkipped) — Pending kalır ve önizleme WillBuild'i FALSE zorlamıştır
    /// (RunCoordinator.cs, tüm pre-skip'ler için — kapsam dışı da GERÇEKTEN güncel de aynı yoldan geçer). Satır
    /// yine de SkipReason'ı taşır, tam bu yüzden: sayfa motorun GERÇEKTEN söylediği (kapsam dışı) gerekçeyi
    /// gösterir, WillBuild=false'tan türeyen "Up to date" YALANINI DEĞİL — bir proje GERÇEKTEN kirli olsa bile.</summary>
    [Fact]
    public void Out_of_cycle_scope_pending_row_states_the_real_reason_not_up_to_date()
    {
        Assert.Equal(
            ["Not needed by a dependency cycle — outside this run's scope.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                skipReason: SkipReasons.OutOfCycleScope)));
    }

    /// <summary>[Task 5 review round 1 — M-10] Bu koşu GERÇEKTEN koşullu bekletiyorsa (<c>Conditional</c>)
    /// "Will build" YALANDIR — motor bu projeyi kökü hâlâ hatalıysa atlayabilir. Sayfa artık satırın kendi
    /// etiketiyle (<see cref="DecisionLabel"/>) AYNI cümleyi söyler — kopya YASAK, tek kaynak orada.</summary>
    [Fact]
    public void A_conditionally_waiting_row_states_the_dependency_it_is_waiting_on_not_will_build()
    {
        var now = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        Assert.Equal(
            ["Dependency issue: Sales.Data — rebuilds once that dependency is healthy again.",
                "Last successful build: 2h ago (a3f81c2)"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: true,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.",
                currentSha: sha, lastBuiltAt: now.AddHours(-2)), now));

        // [DEĞİŞEN KURAL — YOK] Aynı gerekçe ama bu koşu ZORLUYORSA (Conditional=false — satırdan Build,
        // Rebuild, bir SCC üyesi) söz tutulmaz: davranış DEĞİŞMEDİ, genel "Will build in this run." dalına düşer
        // — DecisionLabel'in aynı ayrımı (bkz. o dosyanın "conditional" parametresi) burada da geçerli.
        Assert.Equal(
            ["Will build in this run.", "Last successful build: 2h ago (a3f81c2)"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: false,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.",
                currentSha: sha, lastBuiltAt: now.AddHours(-2)), now));
    }

    private static ProjectRowViewModel Row(
        ProjectRowState state, string? skipReason = null, bool? willBuild = null,
        WillBuildReason? willBuildReason = null, bool inCycle = false, string? currentSha = null,
        bool runActive = false, DateTimeOffset? lastBuiltAt = null, bool? inRunQueue = null,
        bool conditional = false, IReadOnlyList<string>? dependencyRoots = null, string namePrefix = "") =>
        new(@"C:\p\a.csproj", "A", state)
        {
            SkipReason = skipReason,
            WillBuild = willBuild,
            WillBuildReason = willBuildReason,
            InCycle = inCycle,
            CurrentSha = currentSha,
            LastBuiltAt = lastBuiltAt,
            IsRunActive = runActive,
            // [Task 1 review fix — I-2] Belirtilmezse runActive'i izler (eski tek-bayraklı davranışla aynı
            // çağıran deneyimi) — yalnız iki senaryonun ayrıştığı yeni testler açıkça geçer.
            InRunQueue = inRunQueue ?? runActive,
            // [Task 5 review round 1 — M-10] Koşullu bekleme (WaitingForDependency) senaryosu için.
            Conditional = conditional,
            DependencyRoots = dependencyRoots,
            NamePrefix = namePrefix,
        };

    /// <summary>
    /// <b>Logu olmayan bir projeye tıklamak da o projenin sayfasını AÇAR.</b> Eskiden motor
    /// <c>logNotFound</c> dediğinde proje modu hiç kurulmuyor, konsol run anlatısında kalıyordu — atlanmış bir
    /// projenin log dosyası HİÇ yazılmadığı için (gerekçe yalnız <c>decision.log</c>'a gider) bu, en sık
    /// tıklanan durumdu ve tıklama "hiçbir şey yapmıyor" gibi görünüyordu.
    /// </summary>
    [Fact]
    public async Task Clicking_a_project_without_a_log_still_opens_its_page()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı
        var vm = new RunViewModel(engine, new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1");
        const string projectId = @"C:\p\skipped.csproj";
        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.UpToDate));

        vm.SelectProject(projectId);
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ErrorEvent("logNotFound", projectId));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(projectId, vm.ActiveProjectId);
        Assert.True(vm.ShouldShowLoadedProject(projectId));
        // Ve gerekçe satırda tutuluyor — sayfa metni onu okuyacak.
        var row = Assert.Single(vm.Projects);
        Assert.Equal(SkipReasons.UpToDate, row.SkipReason);
    }

    // ---------------------------------------------------------------- N lines = TAM tampon (Ek A #23)

    [Fact]
    public async Task GetActiveLineCount_reflects_full_run_buffer_length_line_for_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal(0, vm.GetActiveLineCount());

        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 2, "Restored a.csproj"));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 3, "Build succeeded"));

        // ActiveProjectId null (run modu) → aktif tampon = run dokümanı; sayaç satır satır artar.
        Assert.Equal(3, vm.GetActiveLineCount());
    }
}
