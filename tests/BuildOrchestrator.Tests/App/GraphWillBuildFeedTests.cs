using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>Liste ile graf AYNI hikâyeyi anlatır.</b> İki yüzey ayrışırsa kullanıcı hangisine güveneceğini bilemez;
/// bu dosya zincirin GERÇEK ucunu ölçer — VM'e olay akıtılır ve <b>çizilmiş</b> düğümün glyph rengine bakılır.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §9-1/§9-3]</b> Eski iddia: <i>"Sync'ten sonra listede amber olan
/// projenin graf küpü de amber olmalıdır"</i> — yani <c>BuildPreviewEvent</c> küpü PLAN rengiyle boyardı
/// (<c>Brush.DotDirty</c>/<c>Brush.DotClean</c>). v1.11.0 plan kanalını kaldırdı ve Sync'i BAŞLANGIÇ MODU
/// yaptı: <i>"Sync ve uygulama açılışı hiçbir şeyi renklendirmez"</i>. Yeni iddia bu yüzden terstir — önizleme
/// grafta HİÇBİR renk üretmez; küp bir işlem başlayana kadar nötr kalır.</para>
///
/// <para>Beslemenin kendisi (önizleme → graf) hâlâ ölçülüyor: kusur bir zamanlar tam oradaydı (graf yalnız
/// topoloji değişiminde kurulup bir daha haber almıyordu) ve şimdi işlem başlangıcının grafa ulaştığı
/// pinleniyor.</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> v1.11.0 iddiası ("önizleme grafta HİÇBİR renk üretmez,
/// küp bir işlem başlayana kadar nötr kalır; başlangıç modu işlem başlayınca düşer") da değişti. Gerekçe
/// (kullanıcı ölçümü): Sync sonrası neyin güncel olduğu renkten okunmuyordu. Yeni iddia: önizleme kararı her
/// node'u kendi çıktı durumuyla boyar (güncel yeşil, derlenecek düz gri) ve başlangıç modu KARAR geldiğinde
/// düşer; işlemin başlaması onu düşürmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphWillBuildFeedTests
{
    // NodeVisuals proje Id'siyle anahtarlanır (ad benzersiz değildir) — fixture'ın Id kuralı MainWindowHost'ta.
    private static GraphNodeVisual VisualOf(MainWindow window, string name) =>
        window.Shell.GraphHost.NodeVisuals[MainWindowHost.IdOf(name)];

    private static System.Windows.Media.Color CoreColour(MainWindow window, string name) =>
        DsResources.ColorOf(VisualOf(window, name).Icon.Stroke);

    /// <summary>[Task 2] Realize edilmiş listede bir proje Id'sinin satırı — <c>HoverEchoWiringTests.RowOf</c>'un
    /// yaklaşımı (DataContext eşlemesi); o metot private olduğu için buraya KOPYALANMAZ, aynı yaklaşım bu
    /// dosyanın kendi yardımcısı olarak yeniden yazılır.</summary>
    private static ProjectRow ListRowOf(System.Windows.FrameworkElement content, string id) =>
        DsResources.RealizedObjects(content).OfType<ProjectRow>()
            .Single(r => r.DataContext is BuildOrchestrator.App.ViewModels.ProjectRowViewModel vm && vm.Id == id);

    /// <summary>[DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski ad/iddia:
    /// <c>A_build_preview_after_sync_leaves_every_cube_neutral_because_sync_shows_no_plan</c> — plan bilinse
    /// bile renk yok, ikisi de başlangıç modunun nötr küpünü taşır. Yeni iddia: Sync kararı renk verir.</summary>
    [StaFact]
    public void A_build_preview_after_sync_paints_every_node_with_its_standing()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);

        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Dirty"), "Dirty", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(MainWindowHost.IdOf("Clean"), "Clean", false, Reason: WillBuildReason.UpToDate),
        ]));
        content.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(window, "Brush.TextFaint"), CoreColour(window, "Dirty"));      // derlenecek: gri
        Assert.Equal(DsResources.TokenColor(window, "Brush.StatusSuccessText"), CoreColour(window, "Clean")); // güncel: yeşil
        Assert.Empty(VisualOf(window, "Dirty").Square.StrokeDashArray); // karar var: başlangıç modu DEĞİL
    }

    /// <summary>Karar gelince başlangıç modu DÜŞER ve bu graf'a ULAŞIR: kesikli çerçeve düze döner. Besleme
    /// kusuru (graf haber almıyor) tam burada ölçülür.
    /// <para>[DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski ad/iddia:
    /// <c>Starting_an_operation_drops_the_fresh_mode_and_the_graph_hears_about_it</c> — başlangıç modu bir
    /// İŞLEM başlayınca düşer. Yeni iddia: başlangıç modu kararın yokluğudur; işlemin başlaması (runStarted)
    /// onu düşürmez, önizleme kararı düşürür.</para></summary>
    [StaFact]
    public void A_decision_drops_the_start_mode_and_the_graph_hears_about_it()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);
        Assert.NotEmpty(VisualOf(window, "Dirty").Square.StrokeDashArray); // ön-koşul

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        content.UpdateLayout();
        Assert.NotEmpty(VisualOf(window, "Dirty").Square.StrokeDashArray); // işlem başladı ama karar yok

        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Dirty"), "Dirty", true, Reason: WillBuildReason.NeverBuilt),
        ]));
        content.UpdateLayout();

        DispatcherPump.PumpUntil(
            () => VisualOf(window, "Dirty").Square.StrokeDashArray.Count == 0,
            TimeSpan.FromSeconds(3));
        Assert.Empty(VisualOf(window, "Dirty").Square.StrokeDashArray);
    }

    /// <summary>[design v1.20.0 §2.3 · Task 4 review I-1] <b>Kararları düşüren bir değişim graf'a listeyle AYNI
    /// ANDA ulaşır</b>: her node kesikli başlangıç moduna döner. Ölçülen kusur: satır anında başlangıç moduna
    /// düşüyordu ama graf yalnız sayaç/işlem/önizleme sinyallerinde besleniyordu — sayaçlar değişmediği için eski
    /// yeşil/gri/kırmızı grafta kalıyordu.
    /// <para>[spec 2026-09-18 §1-1/8] Tetik eskiden aktif olmayan bir branch'in seçimiydi (ad:
    /// <c>A_branch_change_drops_every_node_back_to_the_dashed_start_mode</c>); o seçim artık kararları düşürmez.
    /// Kararları satırları KORUYARAK düşüren tek yol Settings'ten repo değişimidir ve ardından gelen Sync'in
    /// listeyi boşaltmadığı durum motorun erişilemez olduğu durumdur — iddia aynı, tetik o.</para></summary>
    [StaFact]
    public async Task A_repository_change_drops_every_node_back_to_the_dashed_start_mode()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Dirty", null), ("Clean", null));
        var content = MainWindowHost.Realize(window);
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Dirty"), "Dirty", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(MainWindowHost.IdOf("Clean"), "Clean", false, Reason: WillBuildReason.UpToDate),
        ]));
        content.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(window, "Brush.StatusSuccessText"), CoreColour(window, "Clean")); // ön-koşul

        vm.OnEngineUnavailable(System.IO.Path.Combine(dir.Path, "missing.exe")); // Sync gitmez, liste kalır
        await vm.ApplySettingsAsync([], System.IO.Path.Combine(dir.Path, "other-repo"), []);
        content.UpdateLayout();

        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Unknown, r.VisualStatus)); // liste düştü
        foreach (var name in new[] { "Dirty", "Clean" })
        {
            Assert.Equal(VisualStatus.Unknown, VisualOf(window, name).Model.Visual);     // graf da AYNI anda
            Assert.NotEmpty(VisualOf(window, name).Square.StrokeDashArray);
        }
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextFaint"), CoreColour(window, "Clean"));
    }

    /// <summary>[Task 1 review fix — I-1] <b>Dalganın yaktığı kapsam, runStarted ile bu run'ın kendi
    /// buildPreview'i arasında SÖNMEMELİDİR.</b> Kuyruk artık <see cref="ProjectRowViewModel.InRunQueue"/>'dan
    /// türediği ve o YALNIZ bu run'ın kendi önizlemesinden yazıldığı için, <c>runStarted</c> ile
    /// <c>buildPreview</c> arasında gerçek bir IPC boşluğu vardır (Supervisor bu ikisi arasında
    /// <c>stateStore.Load</c> + proje başına <c>OwnFilesChanged</c> hesaplar, <c>RunCoordinator.cs</c> ~885-905)
    /// — <c>MainWindow</c>, <c>IsRunning</c> true olur olmaz kapsamın işaretini (<see cref="ProjectRowViewModel.Marked"/>)
    /// SİLERSE, o boşlukta ne <c>Marked</c> ne <c>InRunQueue</c> true'dur ve kapsam bir kare için gri görünüp
    /// hemen ardından geri yanar (ARCHITECTURE §14.3: "the amber the marking wave lit must not go out when the
    /// run begins" ihlali). Kapsam DIŞI bayat bir komşu satır ise (Sync'ten kalma <c>WillBuild=true</c>, bu
    /// run'ın önizlemesine hiç girmeyen) hiçbir an amber OLMAMALIDIR — Task 1'in asıl konusu budur.</summary>
    /// <para><b>Neden pikselden DEĞİL, satırdan okunuyor:</b> gerçek koreografi her <c>Marked</c> yazımından
    /// SONRA grafa kendi <c>PushGraph()</c>'ını çağırır (<c>OperationChoreographer.Play</c>); burada dalganın
    /// ZAMANLAMASI değil, MainWindow'un <c>runStarted</c>/<c>BuildPreviewApplied</c> anlarında
    /// <c>Marked</c>/<c>InRunQueue</c>'ya DOKUNMA kararı test ediliyor — bunun için satırın kendi
    /// <see cref="ProjectRowViewModel.Marked"/>/<see cref="ProjectRowViewModel.VisualStatus"/>'u yeterli ve
    /// grafın kendi push zamanlamasından bağımsızdır (grafın AYNI değeri okuduğu zaten <c>GraphBinder.StatusOf</c>
    /// ile ayrı pinlidir, Task 1).</para>
    [StaFact]
    public void The_marked_scope_stays_amber_across_runStarted_and_a_stale_sibling_never_lights()
    {
        using var dir = new TempDir();
        var (_, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        string idB = MainWindowHost.IdOf("B");

        // Sync'ten kalma: ikisi de dirty. B, gelecek tek-proje koşusunun önizlemesine hiç girmeyecek "bayat" komşu.
        // [T4 review ledger (b)] Gerekçeli önizleme: "renk değişmez" iddiası gerçek bir çıktı durumunda
        // (derlenecek → gri) sınanır; gerekçesiz karar Unknown→Unknown'dan başka bir şey ölçemezdi.
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(idA, "A", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(idB, "B", true, Reason: WillBuildReason.SignatureChanged),
        ]));

        var a = vm.Projects.Single(p => p.Id == idA);
        var b = vm.Projects.Single(p => p.Id == idB);
        // [DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski iddia: bayat komşu nötr gri (VisualStatus.Discovered) kalır.
        // Discovered kalktı; komşu artık kendi çıktı durumunu taşır — iddia "koşu onun rengine DOKUNMAZ"dır.
        var bBefore = b.VisualStatus;
        Assert.Equal(VisualStatus.Stale, bBefore);

        // Dalganın çıktısı burada SİMÜLE edilir: A işaretlendi (koreografinin zamanlamasını test etmiyoruz,
        // yalnız MainWindow'un runStarted/BuildPreviewApplied wiring'ini).
        a.Marked = true;
        Assert.Equal(VisualStatus.Marked, a.VisualStatus); // ön-koşul: dalga yaktı

        // Satırdan Build: yalnız A hedef. runStarted, bu koşunun kendi önizlemesinden ÖNCE gelir — MainWindow'un
        // IsRunning aboneliği (gerçek kablo) burada senkron tetiklenir.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));

        // [I-1] Kapsam (A) amber KALMALI — runStarted, önizleme gelene dek işareti SİLMEMELİ.
        Assert.True(a.Marked);
        Assert.True(a.VisualStatus is VisualStatus.Marked or VisualStatus.Queued); // amber — hangi kanaldan olursa olsun
        // Bayat komşu (B) hiçbir an amber OLMAMALI (kök neden A).
        Assert.Equal(bBefore, b.VisualStatus);

        // Motorun planı tek düğüme kesilir: önizleme YALNIZ hedefi taşır — devir burada olur.
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(idA, "A", true, Reason: WillBuildReason.SignatureChanged)]));

        Assert.False(a.Marked); // işaret artık gereksiz — InRunQueue statü kanalını devraldı
        Assert.Equal(GraphStatus.Queued, a.Status);
        Assert.Equal(VisualStatus.Queued, a.VisualStatus); // hâlâ amber — kesintisiz devir
        Assert.Equal(bBefore, b.VisualStatus);
    }

    /// <summary>[design v1.20.0 §2.7 · Task 7 review 4] Önizleme işaretleri siler (MainWindow, BuildPreviewApplied);
    /// sayaçlar ve görünür liste işaret SİLİNDİKTEN SONRA türemelidir. Eskiden sayaç önizlemenin satır yazımından
    /// hemen sonra, işaretler henüz yanarken hesaplanıyordu: kuyruğa girmeyen işaretli satır o an Marked
    /// göründüğü için hiçbir durum kovasına girmiyordu ve bir sonraki olaya kadar ✓ · ○ · ✗'in toplamından
    /// eksik kalıyordu.</summary>
    [StaFact]
    public void After_the_preview_a_marked_row_left_out_of_the_queue_is_counted_in_its_state_bucket()
    {
        using var dir = new TempDir();
        var (_, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");
        string idB = MainWindowHost.IdOf("B");
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(idA, "A", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(idB, "B", false, Reason: WillBuildReason.UpToDate),
        ]));
        var a = vm.Projects.Single(p => p.Id == idA);
        var b = vm.Projects.Single(p => p.Id == idB);

        a.Marked = true; // dalga ikisini de yaktı (kapsam tüm workspace)
        b.Marked = true;
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug", 0));
        // Motorun planı: A kuyrukta, B güncel — kuyruğa girmez.
        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(idA, "A", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(idB, "B", false, Reason: WillBuildReason.UpToDate),
        ]));

        Assert.False(b.Marked);                              // ön-koşul: işaret silindi
        Assert.Equal(VisualStatus.Current, b.VisualStatus);  // ...ve B yeşil görünüyor
        var c = vm.Counters;
        Assert.Equal(1, c.Current);                          // gösterilen = sayılan
        Assert.Equal(1, c.Current + c.Stale + c.Broken);     // A kuyrukta (amber) → kovasız
    }

    /// <summary>[Task 1 review fix — I-1, Rebuild dalı] AYNI süreklilik Rebuild'de de geçerlidir —
    /// <see cref="RunViewModel.OnRunStarted"/> yalnız Rebuild modunda EK olarak <c>NeutralizeRows</c> çağırır
    /// (Rebuild'in komut dışı bir yoldan başlama ihtimaline karşı savunma, bkz. o çağrının yorumu). O çağrı
    /// <c>IsRunning=true</c>'nun property-changed KASKADI TAMAMEN bittikten SONRA (yani MainWindow'un Marked'ı
    /// KORUDUĞU karardan SONRA) çalışır — eğer hâlâ <c>Marked=false</c> yazsaydı, tam da I-1'in düzelttiği
    /// boşluğu Rebuild'de YENİDEN açardı. <c>NeutralizeRows</c>'un <c>clearMarks: false</c> çağrısı bunu
    /// önler.</summary>
    [StaFact]
    public void The_marked_scope_also_stays_amber_across_a_rebuilds_own_runStarted()
    {
        using var dir = new TempDir();
        var (_, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
        string idA = MainWindowHost.IdOf("A");

        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(idA, "A", true)]));
        var a = vm.Projects.Single(p => p.Id == idA);

        a.Marked = true; // dalganın çıktısı simüle edilir
        Assert.Equal(VisualStatus.Marked, a.VisualStatus); // ön-koşul

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        Assert.True(a.Marked); // OnRunStarted'ın Rebuild'e özel NeutralizeRows'u işareti EZMEMELİ
        Assert.True(a.VisualStatus is VisualStatus.Marked or VisualStatus.Queued);
    }

    /// <summary>
    /// [Task 2 — sync/durum kapsamı TDD planı] <b>PİN.</b> Build bitti/başarısız oldu → proje listesindeki satır
    /// durumu ile graf düğümünün rengi AYNI hikâyeyi anlatıyor mu — mimari tek kaynak kullanır
    /// (<see cref="ProjectRowViewModel.VisualStatus"/> → <see cref="VisualStatuses.For"/> →
    /// <see cref="VisualStatuses.NodeBorderBrushKey"/>/<see cref="VisualStatuses.StripeBrushKey"/>), ama hiçbir
    /// test koşu SONUÇLARINDAN sonra iki yüzeyi gerçek pencerede karşılaştırmıyordu.
    ///
    /// <para>Dört proje: <c>Ok</c> kanıtlı başarı (yeşil), <c>Bad</c> <see cref="ProjectFailedEvent"/>
    /// (<c>Evidence: true</c> — kırmızı), <c>Flaky</c> aynı olay ama <c>Evidence: false</c> — KIRMIZI DEĞİL,
    /// bayat/gri ailesinde kalır (bkz. <see cref="VisualStatusTests.A_failure_that_is_not_evidence_reads_as_its_stale_standing"/>),
    /// ve <c>Cyc</c> bir döngü (<see cref="ProjectRowViewModel.InCycle"/>) üyesi: küpü HER DURUMDA amber, şeridi
    /// kendi gerçek (bayat) durumunu taşır — ikisi karışmaz.</para>
    /// </summary>
    [StaFact]
    public void After_run_results_land_the_row_and_its_graph_node_agree_on_colour()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(
            dir, ("Ok", null), ("Bad", null), ("Flaky", null), ("Cyc", null));
        var content = MainWindowHost.Realize(window);
        MainWindowHost.AcceptSends(vm);

        // "Cyc" bir döngü üyesi olacak — NewWithProjects'in düz Node() yardımcısı InCycle taşımaz (hep false).
        // Topoloji burada AYNI YAPIYLA (imza değişmez: TopologySignature Id/Ad/LayerIndex/LayerName/Dependencies
        // okur, InCycle'ı BİLEREK dışarıda bırakır — RunViewModel.Workspace.cs'teki o alanın yorumuna bkz.) ama
        // Cyc.InCycle=true olarak yeniden yayınlanır: satır VE _vm.Topology reconciliation'da (OnWorkspaceTopology)
        // güncellenir; graf az sonraki BuildPreviewEvent'in RowDecisionsChanged'ıyla bunu okur (PushGraphStatuses
        // her seferinde GraphBinder.Nodes'u TAZE hesaplar).
        var withCycle = new[]
        {
            MainWindowHost.Node("Ok", 0), MainWindowHost.Node("Bad", 1), MainWindowHost.Node("Flaky", 2),
            new ProjectNode(MainWindowHost.IdOf("Cyc"), "Cyc", MainWindowHost.IdOf("Cyc"), ["Osys"], [], 3,
                null, null, InCycle: true, WillBuild: null),
        };
        vm.OnEvent(new WorkspaceTopologyEvent(withCycle, [], [], []));

        // Event script'i RunViewModelStateTests.A_second_build_keeps_the_greens_of_the_first'teki diziyle AYNI
        // kalıp: build preview + reason'lar, sonra proje eventleri, sonra RunCompleted.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(MainWindowHost.IdOf("Ok"), "Ok", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(MainWindowHost.IdOf("Bad"), "Bad", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(MainWindowHost.IdOf("Flaky"), "Flaky", true, Reason: WillBuildReason.SignatureChanged),
            new BuildPreviewItem(MainWindowHost.IdOf("Cyc"), "Cyc", true, Reason: WillBuildReason.SignatureChanged),
        ]));
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("Ok"), "Ok"));
        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("Ok"), 900));
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("Bad"), "Bad"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("Bad"), 900, "exit 1", Evidence: true));
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("Flaky"), "Flaky"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("Flaky"), 900, "timeout", Evidence: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 2, 0, 0, 2700));
        content.UpdateLayout();

        ProjectRow? cycListRow = null;
        foreach (string name in new[] { "Ok", "Bad", "Flaky", "Cyc" })
        {
            var row = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf(name));
            var node = VisualOf(window, name);
            var listRow = ListRowOf(content, row.Id);
            if (name == "Cyc") cycListRow = listRow;

            // Liste ve graf AYNI görsel durumu okur — mimarinin tek kaynağı (VisualStatus) ikisine de ulaşmış.
            Assert.Equal(row.VisualStatus, node.Model.Visual);
            // Düğüm çerçevesi TOKEN'IN realize edilmiş fırçasıyla AYNI renk.
            Assert.Equal(DsResources.TokenColor(window, VisualStatuses.NodeBorderBrushKey(row.VisualStatus)),
                DsResources.ColorOf(node.Square.Stroke));
            // Satır şeridi TOKEN'IN realize edilmiş fırçasıyla AYNI renk.
            Assert.Equal(DsResources.TokenColor(window, VisualStatuses.StripeBrushKey(row.VisualStatus)),
                DsResources.ColorOf(listRow.Stripe.Fill));
        }

        // Kanıtsız hata (Flaky) KIRMIZI DEĞİL — bayat/gri ailesinde kalır.
        var flaky = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("Flaky"));
        Assert.Equal(VisualStatus.Stale, flaky.VisualStatus);
        Assert.NotEqual(VisualStatus.Failed, flaky.VisualStatus);

        // Döngü üyesinin küpü HER DURUMDA amber; şeridi KENDİ gerçek (bayat) rengini korur — ikisi KARIŞMAZ.
        var cyc = vm.Projects.Single(p => p.Id == MainWindowHost.IdOf("Cyc"));
        Assert.True(cyc.InCycle);
        Assert.Equal(VisualStatus.Stale, cyc.VisualStatus);
        Assert.Equal(DsResources.TokenColor(window, "Brush.AmberText"), CoreColour(window, "Cyc"));
        Assert.Equal("Brush.StatusSkippedBorder", VisualStatuses.StripeBrushKey(cyc.VisualStatus)); // gri aile, amber DEĞİL
        Assert.NotEqual(CoreColour(window, "Cyc"), DsResources.ColorOf(cycListRow!.Stripe.Fill));
    }
}
