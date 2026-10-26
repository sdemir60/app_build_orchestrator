using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;
using ICSharpCode.AvalonEdit.Document;
using static BuildOrchestrator.Tests.App.MainWindowHost;
using static BuildOrchestrator.Tests.App.ConsoleDocument;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3a] Konsol modları (design-v1 §2.5): başlık anlatı↔proje-log geçişi + "N lines" TAM tampon sayacı +
/// boş-durum metinleri (birebir/verbatim). Header kod-tarafı sürülür (küçük test edilebilir yüzey).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleModesTests
{
    // ---------------------------------------------------------------- başlık modları

    // [v1.18.0 §9] Sol grubun tamamı (Back + ad + rozetler) artık TEK bir konteynerdir
    // (ProjectLogGroup, Console/ConsoleHeader.xaml) — mod değişince o hücre bir kerede aç/kapa olur; alt
    // öğelerin KENDİ Visibility'si ayrı ayrı sürülmez. Bu yüzden "gizli mi" iddiası artık gruba, "doğru
    // dolduruldu mu" iddiası içeriğe (Text/ToolTip) bakar — style/ikon/aralık pinleri
    // ConsoleHeaderDesignTests'tedir (realize gerektirir). [kullanıcı kararı 2026-10-25] Grupta eskiden statü glyph'i ve
    // statü sözcüğü de vardı ve buradaki testler onların içeriğini de okurdu; ikisi başlıktan kalktı (gerekçe ve yeni
    // kural: ConsoleHeaderDesignTests).

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

    /// <summary>[kullanıcı kararı 2026-10-25] Eski ad ve iddia: <c>..._shows_name_status_and_dep_issue_badge_...</c> — Failed
    /// satırda statü sözcüğü "Failed" ve glyph <c>VisualStatus.Failed</c> idi; başlık artık statü göstermez.</summary>
    [StaFact]
    public void Header_project_log_mode_shows_name_and_dep_issue_badge_with_full_names_in_the_tooltip()
    {
        var header = new ConsoleHeader();

        // [v1.18.0] Tooltip'in TAM listeyi yazdığını (satırın "+N" kısaltmasının AKSİNE) görmek için iki isim.
        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Sales.Core", ProjectRowState.Failed, inCycle: false,
            depIssues: ["OSYS.Sales.Data", "OSYS.Sales.Contracts"], namePrefix: "OSYS."), 87);

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.Equal(Visibility.Collapsed, header.ConsoleLabel.Visibility);
        Assert.Equal(Visibility.Visible, header.ProjectLogGroup.Visibility);
        Assert.Equal("OSYS.Sales.Core", header.ProjectNameText.Text);
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
        // [kullanıcı kararı 2026-10-25] Eskiden burada Started satırın glyph'inin Building olduğu da okunurdu; başlık artık
        // statü göstermez.
    }

    [StaFact]
    public void Header_project_log_without_warnings_hides_both_badges_and_switching_back_hides_the_group()
    {
        var header = new ConsoleHeader();

        header.ShowProjectLog(ConsoleHeaderRow.For("OSYS.Base", ProjectRowState.Succeeded, inCycle: false,
            depIssues: null, namePrefix: ""), 5);
        Assert.Equal(Visibility.Collapsed, header.DepIssueBadge.Visibility);
        Assert.Equal(Visibility.Collapsed, header.CycleBadge.Visibility);
        // [kullanıcı kararı 2026-10-25] Eskiden burada statü sözcüğünün "Succeeded" olduğu da okunurdu; başlık artık statü
        // göstermez.

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

        CommandPress.Click(header.BackButton);

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
        var batcher = NeverTickingBatcher(); // pump asla ilerlemez
        var vm = new RunViewModel(engine, batcher, () => "r1");
        var view = new ConsoleView();
        var header = new ConsoleHeader();

        // Önceki anlatı/run içeriği + gövdeye senkron kur (kabuğun anlatıya dönüş yolu: ShowRunConsole).
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));
        vm.SeedRunDocument(view.ShowRunDocument);
        const string previousRunText = "Sync complete — 7 changed projects";
        Assert.Contains(previousRunText, view.Document.Text);

        // Bir kart seçilmiş gibi: istekten önce gelen satırlar diskte ve motorun snapshot'ındadır (motor satırı olayını
        // yaymadan önce diske yazar) — dikiş onları chunk'tan alır ve ActiveProjectId'yi kurar (kart-tıklaması yolu). Canlı
        // satır tamponu yalnız istekten sonra gelenleri tutar (idle-memory spec §6.2).
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 2, "Restored A.csproj"));
        vm.SelectProject(projectId); // [D4 review §2] üretimde seçim load'dan önce kurulur (proje modu koşulu)
        var (load, request) = ProjectLogIpc.Request(vm, projectId);
        vm.OnEvent(ProjectLogIpc.Reply(request, projectId, "Determining projects to restore...\nRestored A.csproj\n",
            through: 2));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // Tıklama anı: başlık + gövde SENKRON proje-loguna geçer (pump beklenmeden).
        header.ShowProjectLog(ConsoleHeaderRow.For("A", ProjectRowState.Started, inCycle: false,
            depIssues: null, namePrefix: ""), vm.GetActiveLineCount());
        // [B2 fix 1 · O1] Sayfa logun metnidir, bölünmez (kabuğun kuralı — MainWindow.ProjectDocumentText; eskiden konsolun satır kuralıyla
        // bölünüp görünümde yeniden birleştiriliyordu).
        vm.SeedProjectDocument(projectId, view.PlayCascade); // kabuğun kart seçimi: logun metni olduğu gibi

        Assert.Equal(ConsoleHeader.HeaderMode.ProjectLog, header.Mode);
        Assert.DoesNotContain(previousRunText, view.Document.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// [dalga 3 · karar 44] <b>Derlenen bir projenin sayfası, dikilmiş logun TAMAMIDIR — ilk satırından.</b> İstekten önce diske yazılan log
    /// (motorun snapshot'ı, 1000 satır), istekle cevap arasında gelen canlı satırlar (dikiş: snapshot'ta da olanlar tekrar edilmez) ve sayfa
    /// açıldıktan sonra gelen satırlar (pompa → kabuğun <c>Route.Raw</c>'ı → <c>AppendBatch</c>) sayfada sırayla, tekrarsız ve eksiksiz
    /// durur; sayfa tepesinde açılır.
    /// <para>KIRMIZI (karardan önce): sayfa dikilmiş logun yalnız son 200 satırıyla kuruluyordu — belge <c>log813</c>'ten başlıyordu (öncesi
    /// görünümün listesinde, tepeye kaydırınca gelirdi). Dikişin kendisi (VM, <c>OnProjectLogChunk</c>) değişmedi; bu test onu sayfanın
    /// tamamı üzerinden sınar.</para>
    /// </summary>
    [StaFact]
    public async Task A_building_projects_page_is_its_whole_stitched_log_from_the_first_line_with_later_lines_after_it()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var view = LaidOutView();
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.SelectProject(projectId); // üretimde seçim yüklemeden önce kurulur
        var (load, request) = ProjectLogIpc.Request(vm, projectId);
        foreach (int n in Enumerable.Range(995, 16)) // istekten sonra gelenler: 995..1000 snapshot'ta da var, 1001..1010 yalnız tamponda
            vm.OnEvent(new ProjectLogEvent("r1", projectId, n, $"log{n}"));
        vm.OnEvent(ProjectLogIpc.Reply(request, projectId, string.Concat(Enumerable.Range(1, 1000).Select(n => $"log{n}\n")), through: 1000));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        vm.SeedProjectDocument(projectId, view.PlayCascade); // kabuğun kart seçimi: logun metni olduğu gibi (bölünmez)
        view.AppendBatch("log1011\nlog1012\n");                                             // sayfa açıldıktan sonra gelenler

        Assert.Equal(Enumerable.Range(1, 1012).Select(n => $"log{n}"), LinesOf(view.Document));
        Assert.Equal(0, view.Editor.VerticalOffset);
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
        // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Kanıt satırı bir süre YAŞI da söyledi ("Last
        // successful build: 2h ago (a3f81c2)") — satırın karar etiketiyle AYNI yaş, iki yerde. Yaş kalktı
        // (bkz. DecisionLabel'in sınıf özeti: biri yanıltıyordu, ikisi de gürültüsüne değmiyordu); satır
        // yeniden yalnız revizyonu söylüyor ve bu tarafta bir saat OKUNMUYOR.
        // [DEĞİŞEN KURAL — kullanıcı kararı 2026-09-21] Revizyon da kalktı: eski iddia araç derlediyse ikinci
        // satırın "Last successful build: a3f81c2" demesiydi. Karar içerikten verilir, commit ona girmez; sha
        // satırda durunca kullanıcı kararı commit'e bağlı sandı (ağaç kirliyken sha zaten tam doğru da değildi).
        // Araç derlediyse artık kanıt satırı YOKTUR; yalnız "hiç derlenmedi" / "dışarıda derlendi" kalır.
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        // Atlanmış — motorun söylediği gerekçeyle (SkipReasons, tek doğruluk kaynağı).
        Assert.Equal(
            ["Up to date — nothing to compile in this run."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped,
                skipReason: SkipReasons.UpToDate, currentSha: sha)));

        // Koşu uçuşta, sıra bu satırda değil — plan gerekçesi will-build'den gelir.
        // [Task 1 review fix — I-2] "Queued" artık yalnız runActive'e değil, BU koşunun kendi kuyruğuna
        // (InRunQueue) da bağlı — bkz. Row helper'ının ve ConsoleEmptyState.Pending'in yorumu.
        Assert.Equal(
            ["Queued — the signature changed since the last successful build."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha,
                runActive: true, inRunQueue: true)));

        // Koşu uçuşta AMA bu satır BU koşunun kendi kuyruğunda DEĞİL (tek proje koşusunda bayat bir komşu) —
        // "Queued" DEĞİL, düz plan metni.
        Assert.Equal(
            ["Will build — the signature changed since the last successful build."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha,
                runActive: true, inRunQueue: false)));

        // Koşu YOK: aynı plan "Will build" diye okunur — kuyruk, ancak bir koşu varken vardır.
        // [DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] Eski cümle "its last build failed" idi; artık satırın
        // kendi kelimesiyle AYNI dili konuşur ("failed" — bu KAYNAKTA hata verdi). [Task 6 review round 1 —
        // DÜZELTME] Bu, DecisionLabel'in Title'ıyla AYNI KAYNAKTAN gelmiyor — ikisi ayrı literal, yalnız
        // kelime seçimi bilerek tutarlı (kopya YASAK burada UYGULANMADI, yalnız SÖZCÜK ortak).
        Assert.Equal(
            ["Will build — it failed at this source."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.LastFailed, currentSha: sha)));

        // Hiç derlenmemiş: kanıt satırı gerekçeyi tekrarlayacağı için YAZILMAZ.
        Assert.Equal(
            ["Will build — this tool has never built it."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.NeverBuilt)));

        // [DEĞİŞEN KURAL — Build cycle derler] Döngü üyesi plandan konuşur, düz satır gibi. Eski iddia: döngü üyeliği
        // plandan ÖNCE gelirdi ("In a dependency cycle — Build never compiles one; use Resolve cycles."), çünkü Sync bir
        // SCC üyesine her zaman false verirdi ve o "false"u "güncel" diye okumak yalan olurdu. Sync artık üyeye Build'in
        // kararıyla gerçek bir WillBuild verir (ARCHITECTURE §7.4, §8.1); kirli üye "Will build", güncel üye "Up to date".
        Assert.Equal(
            ["Will build — this tool has never built it."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.NeverBuilt, inCycle: true)));

        // Sync hiç koşmadı: hollow. "Güncel" demek yalan olurdu.
        Assert.Equal(
            ["Not analysed yet — run Sync to see what this project will do.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending)));

        // Derleniyor: kanıt henüz oluşmadı, akış birazdan gelir — TEK satır.
        Assert.Equal(
            ["No log yet — output streams here once the build starts."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Started)));
    }

    /// <summary>[B1] Hükmü verilmiş grupta kaydı atılan taşınan üyenin sayfası: motor onu <c>cycle did not converge at this
    /// signature</c> ile atladı. Sayfa projenin bu koşuda DERLENMEDİĞİNİ ve kaydının atıldığını da söyler — "did not converge"
    /// tek başına satırın neden gri (never built) okuduğunu açıklamaz.</summary>
    [Fact]
    public void A_discarded_carry_page_says_the_project_was_not_compiled_and_its_record_was_discarded()
        => Assert.Equal(
            ["The dependency cycle did not converge at this signature; this project was not compiled and its record was discarded."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped, skipReason: SkipReasons.CycleNonConvergent,
                willBuild: true, willBuildReason: WillBuildReason.NeverBuilt, inCycle: true, currentSha: "a3f81c2")));

    /// <summary>[Task 7 — Faz 3, spec 2026-09-18 §5.4] BuiltOutside bir DİSK OLGUSUDUR: proje bu araç dışında
    /// derlenmiş ve çıktısı güncel. Sayfa bunu TEK cümlede söyler.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> İki eski iddia da düştü. (1) Kanıt satırı
    /// dışarıdaki derlemenin YAŞINI söylerdi ("Built outside this tool: 5m ago", kaynağı <c>OutputBuiltAt</c>);
    /// yaş tüm yüzeylerden kalktı. (2) Yaş gidince geriye İKİ satır kaldı — "Up to date — built outside this
    /// tool." + "Built outside this tool" — yani sayfa aynı şeyi iki kez söylüyordu. Kanıtın gerekçeyi tekrar
    /// ettiği yerde yazılmama kuralı (<c>RepeatsReason</c>) artık bu gerekçeyi de kapsar. Defterdeki KENDİ son
    /// başarısı (<c>CurrentSha</c>) de kanıt olarak yazılmaz: o çıktıyı bu araç üretmedi, kendi son build'inin
    /// commit'ini göstermek yanlış bir kanıt olurdu (bkz. <see cref="ConsoleEmptyState"/> sınıf özeti).</para></summary>
    [Fact]
    public void A_row_built_outside_says_so_once_and_never_shows_the_tools_own_sha()
    {
        Assert.Equal(
            ["Up to date — built outside this tool."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                willBuildReason: WillBuildReason.BuiltOutside)));

        // Defterde bu aracın kendi son başarısı DURUYOR olsa bile kanıt satırı açılmaz.
        Assert.Equal(
            ["Up to date — built outside this tool."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                willBuildReason: WillBuildReason.BuiltOutside,
                currentSha: "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b")));
    }

    /// <summary>[Task 2 review fix I-1] Resolve cycles'ta kapsam dışı bir satır motorun pre-skip'ini State'e
    /// TAŞIMAZ (bkz. RunViewModel.OnProjectSkipped) — Pending kalır ve SkipReason'ı taşır. Sayfa motorun GERÇEKTEN
    /// söylediği (kapsam dışı) gerekçeyi gösterir, satırın plan bayrağından türeyen cümleyi DEĞİL.
    ///
    /// <para><b>[DEĞİŞEN GİRDİ — Resolve → Build düzeltmesi]</b> Eski hâl yalnız <c>willBuild: false</c> ile
    /// sınardı: Resolve önizlemesi bayrağı her pre-skip'te false'a zorlar ve satır bunu taşırdı, tehlike "Up to
    /// date" yalanıydı. Önizleme artık bayrağı YAZMAZ (RunViewModel.OnBuildPreview): bayrak bir sonraki düz
    /// Build'in cevabı olarak kalır — kirli projede <c>true</c>, güncel projede <c>false</c>. Tehlike ikisidir:
    /// "Will build" de "Up to date" de bu koşu hakkında değildir; sayfa iki durumda da aynı gerekçeyi söyler.</para></summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Out_of_cycle_scope_pending_row_states_the_real_reason_not_up_to_date(bool willBuild)
    {
        Assert.Equal(
            ["Not needed by a dependency cycle — outside this run's scope.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: willBuild,
                skipReason: SkipReasons.OutOfCycleScope)));
    }

    /// <summary>[Task 5 review round 1 — M-10] Bu proje bir bağımlılığa karşı bekliyorsa "Will build" YALANDIR
    /// — motor bu projeyi kökü hâlâ hatalıysa atlayabilir. Sayfanın tek kaynağı
    /// <see cref="RowWarning.WaitingForDependencyText"/>'tir (kopya YASAK).
    /// <para><b>[Task 6 review round 1 — DÜZELTME]</b> Bu cümle satırın uyarı üçgeniyle (<see cref="RowWarning.For"/>)
    /// AYNI DEĞİLDİR — üçgen daraltılmış slot için kısaltır (<c>Dependency issue: Sales.Core +2</c>), sayfa geniş
    /// alanda tüm kökleri yazıp bekleme kuyruğunu ekler. Paylaşılan TEK şey kök adlandırma dili
    /// (<see cref="RowWarning.DepIssuePrefix"/> + kısaltma), tam cümle DEĞİL.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4]</b> Eski iddia: bu koşu GERÇEKTEN bekletiyorsa
    /// (<c>Conditional=true</c>) bağımlılık cümlesi, ZORLUYORSA (<c>Conditional=false</c> — satırdan Build,
    /// Rebuild, bir SCC üyesi) genel "Will build in this run." — çünkü eski cümle
    /// <c>DecisionLabel.For</c>'un Title'ından geliyordu ve o da conditional'a bakıyordu. DecisionLabel artık
    /// conditional'ı hiç okumuyor (bkz. o sınıfın özeti) ve bu sayfa artık ONDAN değil doğrudan
    /// <c>RowWarning</c>'den okuyor — ayrım BURADA da kalktı: <c>WaitingForDependency</c> gerekçesi tek başına
    /// yeter, kapsamın zorlayıp zorlamadığı sayfanın cümlesini artık ETKİLEMEZ.</para>
    /// </summary>
    [Fact]
    public void A_waiting_row_states_the_dependency_it_is_waiting_on_not_will_build()
    {
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        Assert.Equal(
            ["Dependency issue: Sales.Data — rebuilds once that dependency is healthy again."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: true,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.", currentSha: sha)));

        // Kapsam ZORLASA bile (Conditional=false) AYNI cümle — söz artık kapsamdan bağımsız bir disk olgusudur.
        Assert.Equal(
            ["Dependency issue: Sales.Data — rebuilds once that dependency is healthy again."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: false,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.", currentSha: sha)));
    }

    /// <summary>[final review — I1] Motor projeyi GERÇEKTEN koşullu olduğu için atladıysa
    /// (<see cref="SkipReasons.DependencyStillFailing"/>) sayfa, kullanıcının o sayfayı açmasının TEK nedenini
    /// söyler: hangi bağımlılık. Gerekçe, bekleyen satırınkiyle (yukarıdaki test) AYNI cümledir — kopya YASAK,
    /// tek kaynak <see cref="RowWarning.WaitingForDependencyText"/>. Eskiden bu dal switch'te YOKTU ve sayfa
    /// genel "Skipped in this run." diyordu.
    ///
    /// <para><b>[Task 6 review round 1]</b> <c>ConsoleEmptyState</c>'in bu dalı eskiden <c>row.Conditional</c>'a
    /// bakan bir guard taşıyordu (Pending dalıyla TUTARSIZ — o dal Task 6'da bu bakışı bıraktı). Guard KALDIRILDI:
    /// motor bu <c>SkipReason</c>'ı yalnız <c>ConditionalRebuild.AppliesTo</c>'nun (Core) o proje için TRUE
    /// dediği durumlarda üretir (<c>RunCoordinator.TrySkipWhileDependencyStillFails</c>, yalnız
    /// <c>run.ConditionalIds</c> içindeki projeler için çağrılır — AYNI <c>AppliesTo</c> çağrısı <c>row.Conditional</c>'ı
    /// da besler), yani bu dal tetiklendiğinde <c>row.Conditional</c> zaten <c>true</c>'dur. İkinci senaryo
    /// (<c>conditional: false</c>) bunu pinler: cümle Conditional'dan BAĞIMSIZ aynı kalır.</para></summary>
    [Fact]
    public void A_row_skipped_because_its_dependency_is_still_failing_names_that_dependency()
    {
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        Assert.Equal(
            ["Dependency issue: Sales.Data — rebuilds once that dependency is healthy again."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped,
                skipReason: SkipReasons.DependencyStillFailing, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: true,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.", currentSha: sha)));

        // conditional:false pratikte olmaz (yukarıdaki not), ama guard KALKTIĞI için cümle YİNE de aynı —
        // savunmacı durumda bile sessizce "Skipped in this run."a düşmez.
        Assert.Equal(
            ["Dependency issue: Sales.Data — rebuilds once that dependency is healthy again."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped,
                skipReason: SkipReasons.DependencyStillFailing, willBuild: true,
                willBuildReason: WillBuildReason.WaitingForDependency, conditional: false,
                dependencyRoots: ["OSYS.Sales.Data"], namePrefix: "OSYS.", currentSha: sha)));
    }

    /// <summary>[T16] Pending + willBuild=false + <see cref="WillBuildReason.UpToDate"/>, sha DOLU
    /// (<c>ConsoleEmptyState.cs:126-129</c>): metin Skipped dalının "...in this run." EKİNİ TAŞIMAZ — bu satır
    /// motorun BU KOŞUDA söylediği bir şey değil, imza karşılaştırmasından türeyen bir PLANDIR. Kanıt satırı da
    /// yoktur (sha dolu → <c>Evidence</c> null döner).</summary>
    [Fact]
    public void A_pending_up_to_date_row_says_nothing_to_compile_without_the_in_this_run_wording()
    {
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        Assert.Equal(
            ["Up to date — nothing to compile."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                willBuildReason: WillBuildReason.UpToDate, currentSha: sha)));
    }

    /// <summary>
    /// Bu koşunun KUYRUĞUNDAKİ satır, plan bayrağı "bir sonraki düz Build onu atlar" dese de bu koşunun işidir: Rebuild
    /// güncel projeyi derler, Clean temizler, satırın ▶'si hedefini derler. Sayfa başlığıyla (<c>Queued</c>) aynı şeyi
    /// söyler — <c>nothing to compile</c> DEMEZ. Dışarıda derlenmiş çıktının kanıt satırı yine yazılır.
    /// <para><b>Ölçülen kusur (inceleme, 2026-10-12):</b> erken dönen "güncel" dalı kuyruğa bakmıyordu — Rebuild'in
    /// kuyruktaki güncel satırında başlık <c>Queued</c>, gövde <c>Up to date — nothing to compile.</c> diyordu; satırın
    /// ▶'siyle derlenen güncel hedef de önizleme plan bayrağını artık yazmadığı için aynı çelişkiye düşüyordu (önceden
    /// <c>Queued in this run.</c>).</para>
    /// </summary>
    [Fact]
    public void A_queued_row_reads_as_this_runs_work_even_when_its_plan_says_up_to_date()
    {
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";

        Assert.Equal(
            ["Queued in this run."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                willBuildReason: WillBuildReason.UpToDate, currentSha: sha, runActive: true, inRunQueue: true)));
        Assert.Equal(
            ["Queued in this run.", "Built outside this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: false,
                willBuildReason: WillBuildReason.BuiltOutside, currentSha: sha, runActive: true, inRunQueue: true)));
    }

    /// <summary>[T16] LastFailed + sha YOK (<c>ConsoleEmptyState.cs:201</c> çevresi): <c>RepeatsReason</c>'ın
    /// metin-tekrarı guard'ı yalnız NeverBuilt/OutputMissing gerekçeleri için geçerlidir — LastFailed onların
    /// dışında kalır, bu yüzden kanıt satırı YAZILIR ve İKİ satır birden görünür.</summary>
    [Fact]
    public void A_last_failed_row_with_no_recorded_success_shows_both_the_failure_and_the_never_built_evidence()
    {
        Assert.Equal(
            ["Will build — it failed at this source.", "Never built by this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.LastFailed)));
    }

    /// <summary>[T16] Motor bu koşuda "up to date" diye ATLADI (Skipped/<see cref="SkipReasons.UpToDate"/> —
    /// gerekçe metni <c>ConsoleEmptyState.cs:83</c>'ten, "...in this run." ekiyle) AMA defterdeki kayıt bu
    /// çıktının dışarıda derlendiğini söylüyor (<see cref="WillBuildReason.BuiltOutside"/>, kanıt
    /// <c>ConsoleEmptyState.cs:199</c>'dan). İki metin BİREBİR AYNI DEĞİL (biri Skipped dalından, biri
    /// Pending dalının kendi cümlesinden) — <c>RepeatsReason</c>'ın metin-eşitliği guard'ı burada TUTMAZ ve
    /// kanıt satırı yine eklenir.</summary>
    [Fact]
    public void A_row_skipped_as_up_to_date_still_names_an_externally_built_output_as_evidence()
    {
        Assert.Equal(
            ["Up to date — nothing to compile in this run.", "Built outside this tool"],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Skipped, skipReason: SkipReasons.UpToDate,
                willBuildReason: WillBuildReason.BuiltOutside)));
    }

    /// <summary>[kullanıcı kararı 2026-10-25] Proje sayfası gerçek sebebi söyler. İmzası yalnız configuration geçişiyle değişmiş
    /// proje "imza değişti" demez: son derlemesi başka bir configuration'daydı. Bu araç dışında derlenmiş ve kendi kontrolünden
    /// taze çıkan, ama arkasındaki bağımlılık yeniden derlenecek proje "bir bağımlılığın çıktısı daha yeni" demez — o an daha
    /// yeni bir çıktı yoktur, bağımlılığı önce yeniden derlenecektir. Olgu yoksa iki cümle de bugünkü gibidir.</summary>
    [Fact]
    public void The_page_names_a_configuration_switch_and_a_dependency_that_rebuilds_first()
    {
        const string sha = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b"; // araç derledi: kanıt satırı yok, tek satır

        Assert.Equal(["Will build — it was last built in Debug."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha, lastBuiltConfiguration: "Debug")));
        Assert.Equal(["Will build — the signature changed since the last successful build."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.SignatureChanged, currentSha: sha)));

        Assert.Equal(["Will build — one of its dependencies will be rebuilt first."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.OutputStale, currentSha: sha, behindRebuildingDependency: true)));
        Assert.Equal(["Will build — a dependency's output is newer than its build output."],
            ConsoleEmptyState.ForEmptyLog(Row(ProjectRowState.Pending, willBuild: true,
                willBuildReason: WillBuildReason.OutputStale, currentSha: sha)));
    }

    /// <summary>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20] Helper'ın iki zaman parametresi
    /// (<c>lastBuiltAt</c>, <c>outputBuiltAt</c>) kalktı: sayfa artık hiçbir yaş yazmadığı için kurulacak bir
    /// zaman da yok.</summary>
    private static ProjectRowViewModel Row(
        ProjectRowState state, string? skipReason = null, bool? willBuild = null,
        WillBuildReason? willBuildReason = null, bool inCycle = false, string? currentSha = null,
        bool runActive = false, bool? inRunQueue = null,
        bool conditional = false, IReadOnlyList<string>? dependencyRoots = null, string namePrefix = "",
        string? lastBuiltConfiguration = null, bool behindRebuildingDependency = false) =>
        new(@"C:\p\a.csproj", "A", state)
        {
            // [kullanıcı kararı 2026-10-25] Gerekçe cümlesinin iki olgusu: son derlemenin (farklı) configuration'ı ve
            // önce yeniden derlenecek bağımlılık.
            LastBuiltConfiguration = lastBuiltConfiguration,
            BehindRebuildingDependency = behindRebuildingDependency,
            SkipReason = skipReason,
            WillBuild = willBuild,
            WillBuildReason = willBuildReason,
            InCycle = inCycle,
            CurrentSha = currentSha,
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
    /// <b>Logu olmayan bir projeye tıklamak da o projenin sayfasını AÇAR.</b> Eskiden motor "log yok"
    /// dediğinde proje modu hiç kurulmuyor, konsol run anlatısında kalıyordu — atlanmış bir
    /// projenin log dosyası HİÇ yazılmadığı için (gerekçe yalnız <c>decision.log</c>'a gider) bu, en sık
    /// tıklanan durumdu ve tıklama "hiçbir şey yapmıyor" gibi görünüyordu.
    /// <para>[DEĞİŞEN KURULUM — idle-memory I1] "Log yok" cevabı eskiden <c>error(logNotFound)</c>'du; artık isteğin kimliğini
    /// yankılayan tek, boş, bayraklı bir chunk'tır. İddia aynı.</para>
    /// </summary>
    [Fact]
    public async Task Clicking_a_project_without_a_log_still_opens_its_page()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\skipped.csproj";
        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.UpToDate));

        vm.SelectProject(projectId);
        var (load, request) = ProjectLogIpc.Request(vm, projectId);
        vm.OnEvent(ProjectLogIpc.NotFound(request, projectId));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(projectId, vm.ActiveProjectId);
        Assert.True(vm.ShouldShowLoadedProject(projectId));
        // Sayfanın metni boş: kabuk boş metinde projenin boş durum sayfasını kurar (MainWindow.ProjectDocumentText) — "log yok"
        // cevabı boş bir snapshot olarak dikilse de bu kural aynen işler [idle-memory I1].
        Assert.Equal("", vm.GetProjectDocumentText(projectId));
        // Ve gerekçe satırda tutuluyor — sayfa metni onu okuyacak.
        var row = Assert.Single(vm.Projects);
        Assert.Equal(SkipReasons.UpToDate, row.SkipReason);
    }

    // ---------------------------------------------------------------- proje sayfası logun metninin kendisidir

    /// <summary>
    /// [dalga 3 · B2 fix 1 · O1] <b>Proje sayfası logun METNİNİN KENDİSİDİR</b> — kabuk sayfayı bölmez, yeniden birleştirmez
    /// (<c>MainWindow.ProjectDocumentText</c>); belge logu AvalonEdit'in kendi ayraç kuralıyla gösterir. İki fark pinlenir. <b>Fark 1 — sondaki
    /// boş satır:</b> bir derleme logunun boş bir satırla bitmesi sık görülür; sayfa onu da gösterir ve sonradan gelen canlı satır boş satırın
    /// ALTINA düşer. <b>Fark 2 — tek başına '\r':</b> belge onu satır ayracı sayar; sayfa, log + canlı satırların belgede gösterdiği satırların
    /// aynısıdır (kâhin AvalonEdit'in kendi belgesi) — tekrar yok, kayıp yok.
    /// <para>[DEĞİŞEN KURAL — final review 90] Kabuk sayfayı kendi kuralıyla bölüyordu (<c>TrimEnd('\n').Split('\n')</c>): sondaki BÜTÜN boş
    /// satırlar düşüyor, sonradan gelen canlı satır onların yerine oturuyordu (KIRMIZI: Fark 1); tek başına '\r' listede bir girdi kalıp belgede
    /// iki satır oluyor, sayfa son dilim + backlog'ken indeks kayıyordu — tepeye kaydırmada dikişte bir satır iki kez (KIRMIZI:
    /// <c>line161</c>). Final review 90 kabuğu konsolun satır kuralına (<c>ConsoleLines.Split</c>) bağlamıştı.</para>
    /// <para>[DEĞİŞEN KURAL — dalga 3 · karar 44-45 · B2 fix 1 O1/M2] Sayfa logun tamamıdır ve hiç izlenmez (indeksli backlog yok, kırpma yok):
    /// Fark 2'nin kayması artık doğamaz ve ayrı '\r' testi (<c>A_lone_carriage_return_in_a_project_log_shows_the_page_line_for_line</c>) hiçbir
    /// şeyi ayırt etmiyordu (inceleme M2) — iddiası buraya katlandı; Fark 1'in testi (<c>A_project_page_keeps_the_empty_line_its_log_ends_with</c>)
    /// da. O1 bölme → birleştirme turunu kaldırdı: sayfa metni olduğu gibi alır. KIRMIZI (O1'den önce): belgenin metni logunkinden farklıydı —
    /// '\r' bölünüp '\n' olarak geri yazılıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_is_its_log_text_itself_line_for_line()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        var console = window.Shell.ConsoleViewControl;
        var row = MainWindowHost.ProjectOf(vm, "A");
        vm.ActiveProjectId = row.Id;                                     // proje logu açık (motor round-trip'i yok: mod doğrudan kurulur)
        MainWindowHost.LogLine(vm, "A", 1, "restore 50%\rrestore 100%"); // Fark 2: tek başına '\r'
        MainWindowHost.LogLine(vm, "A", 2, "Build succeeded.");
        MainWindowHost.LogLine(vm, "A", 3, "");                          // Fark 1: log boş bir satırla bitiyor
        string log = vm.GetProjectDocumentText(row.Id);
        Assert.Equal("restore 50%\rrestore 100%\nBuild succeeded.\n\n", log); // ön-koşul

        vm.SelectProject(row.Id);                                        // kart seçildi → sayfa kurulur (PlayCascade)
        window.AppendConsoleBatch("Time Elapsed 00:00:01.23\n", window.ConsoleReseedGen); // canlı satır, pompanın yolundan

        string page = log + "Time Elapsed 00:00:01.23\n";
        Assert.Equal(page, console.Document.Text);                       // sayfa logun metnidir
        string[] shown = ["restore 50%", "restore 100%", "Build succeeded.", "", "Time Elapsed 00:00:01.23"]; // Fark 2 + Fark 1
        Assert.Equal(shown, ConsoleDocument.LinesOf(console.Document));
        Assert.Equal(ConsoleDocument.LinesOf(new TextDocument(page)), ConsoleDocument.LinesOf(console.Document)); // AvalonEdit'in kuralı
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- N lines = TAM tampon (Ek A #23)

    /// <summary>"N lines" anlatının tamamını sayar (render dilimini değil) — satır satır.
    /// <para>[DEĞİŞEN KURULUM — idle-memory dalga 2 · spec §6.5] VM kompakt konsolu açıkça KAPALI kurar: eskiden her proje satırı
    /// anlatıya aynen girerdi; üretim varsayılanı artık kompakt kiptir ve buradaki üç bilgi satırını gizler (sayaç 1: yalnız
    /// başlık). İddia aynı, tam kipte — kompakt kipte sayacın anlatının gerçek satırlarını (başlık ve özet dahil) saydığı
    /// <see cref="CompactConsoleTests"/>'tedir.</para></summary>
    [Fact]
    public async Task GetActiveLineCount_reflects_full_run_buffer_length_line_for_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { CompactConsole = false };

        Assert.Equal(0, vm.GetActiveLineCount());

        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 1, "Determining projects to restore..."));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 2, "Restored a.csproj"));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 3, "Build succeeded"));

        // ActiveProjectId null (run modu) → aktif tampon = run dokümanı; sayaç satır satır artar.
        Assert.Equal(3, vm.GetActiveLineCount());
    }

    /// <summary>
    /// [idle-memory · dalga 1] Çok satırlı bir anlatı satırı (çok satırlı hata mesajı — motorun <c>ex.Message</c>'ı, git stderr)
    /// belgede kaç satırsa "N lines" o kadar sayar ve anlatı onu o satırlar olarak tutar.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eskiden sayaç GİRDİ başına birdi: iki satırlık bir hata belgede iki satır, başlıkta bir
    /// satırdı. Değişme gerekçesi: anlatının tek sahibi (<c>NarrativeStore</c>) aynı zamanda konsolun chunk loader indeksidir ve
    /// indeks belge satırıyla birebir yürümek zorundadır (kırpılan belge satırı sayısı indekse eklenir; ayrışırsa yukarı
    /// kaydırma tekrar ya da delik getirir). Store çok satırlı girdiyi belgenin ayraç kuralıyla böler; sayaç da onu sayar.</para>
    /// </summary>
    [Fact]
    public async Task GetActiveLineCount_counts_the_lines_a_multi_line_narrative_entry_shows()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ErrorEvent("runFailed", "first line\r\n   at Second.Line()"));

        Assert.Equal(2, vm.GetActiveLineCount());
        Assert.Equal("[error] runFailed: first line\n   at Second.Line()\n", vm.GetRunDocumentText());
    }
}
