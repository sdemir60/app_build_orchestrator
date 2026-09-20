using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Serbest gezinme": boş zeminde sürükleme = pan (el imleci), wheel = imleç merkezli
/// zoom (0.7–5.0, ×1.14), ≤3px hareketle biten basış = TIKLAMA.
///
/// <para><b>Eski iddialar (artık geçersiz):</b></para>
/// <list type="bullet">
///   <item><b>Jestler yalnız "sinema modunda" (&gt;150 düğüm) çalışırdı.</b> O kapı (<c>FullDetailMaxNodes</c>)
///     v1.3.0 ile tamamen kalktı: graf her boyutta aynı davranır, jestler HER grafta canlıdır.</item>
///   <item><b>Sürükleme eşiği platformdan gelirdi</b> (<c>SystemParameters.MinimumHorizontalDragDistance</c>).
///     §2.3 sayıyı açıkça veriyor: "≤3px hareket tıklama sayılır, üstü pan" — tasarım sabiti kazanır.</item>
///   <item><b>Takip dönüşü ve <c>FOLLOW PAUSED</c> pili.</b> Kamera artık kendiliğinden hareket etmediği için
///     (tek otomatik hedefi seçim) "kamerayı geri vermek" diye bir durum yok; pil ve 4 saniyelik dönüş
///     mekanizması bu dosyadan tamamen silindi.</item>
/// </list>
///
/// <para>Testlerin çoğu internal seam'leri (<c>HandlePanStart/Move/End</c>, <c>HandleWheel</c>) doğrudan sürer:
/// headless'ta gerçek mouse capture alınamaz (<c>PresentationSource</c> yok) ve mantığın tamamı seam'lerdedir.
/// Ama ctor kablosunun KENDİ kararları (capture kaybı = iptal) seam'lerin üstündedir — onlar
/// <see cref="MouseInput"/> ile GERÇEK routed event yükseltilerek pinlenir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphPanZoomTests
{
    /// <summary>Eşiği (3px) AŞAN sürükleme deltası.</summary>
    private static readonly Vector DragDelta = new(40, -25);

    /// <summary>Jestlerin başladığı ekran noktası. Panel MERKEZİNİN dışında olması ŞARTTIR: merkezdeki bir
    /// imleç, "imleç merkezli zoom" ile "panel merkezli zoom"u ayırt edilemez kılar.</summary>
    private static readonly Point Anchor = new(430, 120);

    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", "OSYS.Base", 0, GraphStatus.Discovered),
        new("OSYS.Data", "OSYS.Data", 1, GraphStatus.Discovered),
        new("OSYS.Api", "OSYS.Api", 2, GraphStatus.Discovered),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
        [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")];

    private static GraphView NewView(bool animations = false)
    {
        var view = GraphTestView.Realized(new Size(600, 400), () => animations);
        view.SetGraph(Nodes(), Edges());
        return view;
    }

    // ---------------------------------------------------------------- pan

    [StaFact]
    public void A_drag_beyond_the_threshold_pans_the_camera()
    {
        var view = NewView();
        var before = view.CurrentCamera;

        Assert.True(view.HandlePanStart(Anchor));
        view.HandlePanMove(Anchor + DragDelta);

        Assert.Equal(before.Scale, view.CurrentCamera.Scale, 6); // pan ölçeği değiştirmez
        Assert.Equal(before.Tx + DragDelta.X, view.CurrentCamera.Tx, 6);
        Assert.Equal(before.Ty + DragDelta.Y, view.CurrentCamera.Ty, 6);
    }

    /// <summary>Her hareket KENDİ deltasıyla öteler — deltalar birikirse graf imleç hızının katlarıyla kayar
    /// ve elin altındaki nokta grafı takip etmez.</summary>
    [StaFact]
    public void Each_move_pans_by_its_OWN_delta_so_the_point_under_the_hand_tracks_the_graph()
    {
        var view = NewView();
        var before = view.CurrentCamera;

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + new Vector(20, 0));
        view.HandlePanMove(Anchor + new Vector(40, 0));
        view.HandlePanMove(Anchor + new Vector(60, 0));

        Assert.Equal(before.Tx + 60, view.CurrentCamera.Tx, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — §2.3'ün 3px kuralı. Eşik BASIŞ NOKTASINDAN ölçülür (her karede sıfırlanan deltadan
    /// değil), aksi halde yavaş bir sürükleme hiç eşiği aşamazdı.
    /// </summary>
    [StaFact]
    public void A_three_pixel_wiggle_stays_a_click_but_a_fourth_pixel_makes_it_a_pan()
    {
        var view = NewView();
        var before = view.CurrentCamera;

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + new Vector(2, 1)); // |dx|+|dy| = 3 → hâlâ tıklama
        Assert.Equal(before, view.CurrentCamera);

        view.HandlePanMove(Anchor + new Vector(2, 2)); // toplam 4 → pan
        Assert.NotEqual(before, view.CurrentCamera);
    }

    /// <summary>Eşik altı basış-bırakış bir TIKLAMADIR ve §2.3'ün iki kollu kuralı işler: seçim VARSA
    /// bırakılır.</summary>
    [StaFact]
    public void A_subthreshold_press_release_on_the_ground_clears_the_selection()
    {
        var view = NewView();
        view.SelectedNode = "OSYS.Data";

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + new Vector(1, 1));
        view.HandlePanEnd();

        Assert.Null(view.SelectedNode);
    }

    /// <summary>Seçim YOKKEN aynı tıklama görünümü VARSAYILANA döndürür (§2.3) — kullanıcı grafı nereye
    /// sürüklerse sürüklesin tek tıkla geri gelir. Pan kelepçesinin yerine geçen kurtarma jesti budur.</summary>
    [StaFact]
    public void A_click_on_empty_ground_with_no_selection_returns_the_view_to_the_default()
    {
        var view = NewView();
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta); // grafı kaydır
        view.HandlePanEnd();
        Assert.NotEqual(GraphCamera.Default, view.CurrentCamera);

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + new Vector(1, 0));
        view.HandlePanEnd();

        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
    }

    /// <summary>Sürükleme SONRASI bırakma boş-alan tıklaması TETİKLEMEZ (§2.3) — seçim korunur.</summary>
    [StaFact]
    public void Releasing_after_a_real_drag_does_not_count_as_a_click()
    {
        var view = NewView();
        view.SelectedNode = "OSYS.Data";

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        view.HandlePanEnd();

        Assert.Equal("OSYS.Data", view.SelectedNode);
    }

    [StaFact]
    public void During_a_drag_the_ground_shows_the_hand_cursor_and_releases_it_after()
    {
        var view = NewView();
        Assert.Null(view.Ground.ReadLocalValue(FrameworkElement.CursorProperty) as Cursor);

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(Cursors.Hand, view.Ground.Cursor);

        view.HandlePanEnd();
        Assert.Null(view.Ground.ReadLocalValue(FrameworkElement.CursorProperty) as Cursor);
    }

    // ---------------------------------------------------------------- wheel

    [StaFact]
    public void The_wheel_zooms_at_the_cursor_by_the_1_14_step()
    {
        var view = NewView();
        var before = view.CurrentCamera;

        view.HandleWheel(Anchor, 120);

        Assert.Equal(GraphCamera.ZoomAt(before, Anchor, GraphCamera.WheelZoomStep), view.CurrentCamera);
    }

    [StaFact]
    public void A_negative_wheel_delta_zooms_out_by_the_same_step()
    {
        var view = NewView();
        var before = view.CurrentCamera;

        view.HandleWheel(Anchor, -120);

        Assert.Equal(GraphCamera.ZoomAt(before, Anchor, 1 / GraphCamera.WheelZoomStep), view.CurrentCamera);
    }

    /// <summary>
    /// AYIRT EDİCİ — jestler HER graf boyutunda canlıdır.
    ///
    /// <para><b>Eski iddia:</b> <c>Gestures_are_inert_outside_cinema</c> — 3 düğümlük bir grafta wheel bu
    /// panele ait DEĞİLDİ. v1.3.0 o kapıyı kaldırdı.</para>
    /// </summary>
    [StaFact]
    public void Gestures_work_on_a_three_node_graph_because_there_is_no_cinema_gate_any_more()
    {
        var view = NewView();

        view.HandleWheel(Anchor, 120);

        Assert.NotEqual(GraphCamera.DefaultScale, view.CurrentCamera.Scale);
    }

    /// <summary>Boş grafta (henüz Sync yok) jest başlamaz — kamera aritmetiği mevcut kameradan türer ve
    /// gösterilecek hiçbir şey yokken kamerayı oynatmak anlamsızdır.</summary>
    [StaFact]
    public void Gestures_are_inert_on_an_empty_graph()
    {
        var view = GraphTestView.Realized(new Size(600, 400));

        Assert.False(view.HandlePanStart(Anchor));
        view.HandleWheel(Anchor, 120);

        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
    }

    // ---------------------------------------------------------------- ctor kablosu (GERÇEK routed event)

    /// <summary>Zemine basmak seçimi DOWN anında kaldırmaz — basış bir sürüklemenin başı olabilir, karar
    /// release'e aittir (click-vs-drag ayrımı).</summary>
    [StaFact]
    public void Pressing_the_ground_keeps_the_selection_until_the_release()
    {
        var view = NewView();
        view.SelectedNode = "OSYS.Data";

        MouseInput.PressLeft(view.Ground);

        Assert.Equal("OSYS.Data", view.SelectedNode);
    }

    /// <summary>Capture BAŞKA bir sebeple düşerse (Alt+Tab, popup) bu bir BIRAKMA DEĞİL İPTALDİR: jest
    /// durumu ve el imleci temizlenir, seçime DOKUNULMAZ.</summary>
    [StaFact]
    public void Losing_the_capture_cancels_the_gesture_instead_of_counting_as_a_release()
    {
        var view = NewView();
        view.SelectedNode = "OSYS.Data";
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(Cursors.Hand, view.Ground.Cursor);

        view.Ground.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = UIElement.LostMouseCaptureEvent,
        });

        Assert.Equal("OSYS.Data", view.SelectedNode); // İPTAL: seçim korunur
        Assert.Null(view.Ground.ReadLocalValue(FrameworkElement.CursorProperty) as Cursor);
        // Jest gerçekten bitti: sonraki hareket kamerayı oynatmaz.
        var frozen = view.CurrentCamera;
        view.HandlePanMove(Anchor + new Vector(200, 200));
        Assert.Equal(frozen, view.CurrentCamera);
    }

    /// <summary>Yeni topoloji uçuştaki jesti iptal eder ve el imlecini bırakır — kullanıcının gezdiği
    /// koordinatların yeni grafta karşılığı yoktur.</summary>
    [StaFact]
    public void A_new_topology_cancels_an_in_flight_gesture_and_resets_the_camera()
    {
        var view = NewView();
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(Cursors.Hand, view.Ground.Cursor);

        view.SetGraph(Nodes(), Edges());

        Assert.Null(view.Ground.ReadLocalValue(FrameworkElement.CursorProperty) as Cursor);
        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
        var frozen = view.CurrentCamera;
        view.HandlePanMove(Anchor + new Vector(200, 200));
        Assert.Equal(frozen, view.CurrentCamera);
    }

    // ---------------------------------------------------------------- [task 2] hedef ≠ ekran

    /// <summary>
    /// [task 2] <b>Kusur:</b> <c>ApplyGraph</c> yalnız <c>CurrentCamera</c> (HEDEF) alanını <c>Default</c>'a
    /// yazıyordu; ekrandaki <c>_cameraScale</c>/<c>_cameraTranslate</c> dokunulmadan kalıyordu. Ardından
    /// <c>AnimateCameraTo</c>'nun "hedef değişmediyse no-op" kapısı (<c>camera == CurrentCamera</c>) hedef
    /// zaten Default olduğu için hemen dönüyor ve ekranı Default'a SNAP'leyen çağrı hiç yapılmıyordu. Sonuç:
    /// graf yeniden kurulduktan sonra önceki zoom/pan EKRANDA kalıyordu — <see cref="GraphView.CurrentCamera"/>
    /// Default derken kullanıcı hâlâ eski zoomlu görünümü görüyordu.
    /// </summary>
    [StaFact]
    public void Rebuilding_the_graph_snaps_the_ON_SCREEN_camera_to_default_not_just_the_target()
    {
        var view = NewView();
        view.HandleWheel(Anchor, 120); // canlı kamerayı zoom'lu hedefe götürür (animasyon kapalı ⇒ hedef=ekran)
        Assert.NotEqual(GraphCamera.Default, view.LiveCameraForTest); // ön-koşul: ekran zoomlu

        view.SetGraph(Nodes(), Edges());

        Assert.Equal(GraphCamera.Default, view.LiveCameraForTest);
    }

    /// <summary>
    /// [task 2] Aynı kusurun ikinci belirtisi: graf yeniden kurulduktan sonra boş alana tıklamak hiçbir şey
    /// YAPMIYORDU, çünkü hedef zaten (yanlışlıkla) Default'tu ve <c>AnimateCameraTo</c> no-op'a düşüyordu —
    /// kullanıcı yeniden zoom oynatmadan ekran asla düzelmiyordu.
    /// </summary>
    [StaFact]
    public void A_click_after_a_graph_rebuild_still_returns_the_ON_SCREEN_camera_to_default()
    {
        var view = NewView();
        view.HandleWheel(Anchor, 120);
        view.SetGraph(Nodes(), Edges());

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + new Vector(1, 0)); // eşik altı — tıklama
        view.HandlePanEnd();

        Assert.Equal(GraphCamera.Default, view.LiveCameraForTest);
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-19] Bir işlem başlarken (Build tıklaması — <see cref="GraphView.BeginOperation"/>)
    /// kamera varsayılan görünüme döner. <b>Eski davranış:</b> seçim yoksa kamera "yalnız seçimle hareket eder"
    /// kuralıyla yerinde kalıyordu; zoom'lu bir grafta açılış dalgası ve koşu ekran dışında oynuyordu.
    /// </summary>
    [StaFact]
    public void Starting_an_operation_returns_a_zoomed_camera_to_default()
    {
        var view = NewView();
        view.HandleWheel(Anchor, 120);
        Assert.NotEqual(GraphCamera.Default, view.LiveCameraForTest); // ön-koşul: ekran zoomlu

        view.BeginOperation();

        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
        Assert.Equal(GraphCamera.Default, view.LiveCameraForTest);
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-19 · regresyon guard'ı] Yukarıdaki pin <see cref="GraphView"/>'ı DOĞRUDAN
    /// sürer; ÜRETİMDE arada bir kablo vardır: Build komutu → VM'in <c>OperationChoreography</c> delegesi →
    /// kabuk → <c>GraphHost.BeginOperation()</c> (<c>MainWindow.xaml.cs</c>, koreografi delegesi). O kablo
    /// koparsa (delege hiç kurulmazsa ya da <c>BeginOperation</c> çağrısı oradan düşerse) birim testi yeşil
    /// kalır ve kullanıcı yine zoom'lu grafta koşu izler. Burada GERÇEK pencere kurulur, graf üretimdeki wheel
    /// seam'iyle zoom'lanır ve ÜRETİM Build komutu koşturulur.
    ///
    /// <para><b>Hareket KAPALI.</b> Bu kabuk graf motion'ını <c>App.Motion</c>'dan alır ve headless'ta o
    /// null'dır. Açık olsaydı <c>ApplyCamera(animate: true)</c> gerçek bir 460 ms'lik WPF saati başlatırdı;
    /// ekrandaki kamera (<c>LiveCamera</c>) ancak duvar saati dolunca Default'a otururdu ve testin onu
    /// beklemesi determinizmi bozardı. Kapalı yolda hedef ile ekran aynı turda Default olur — bu testin
    /// pinlediği şey kablonun KENDİSİDİR, animasyonlu dal değil (onu <c>BeginOperation</c>'ın kendi pini
    /// kapsar).</para>
    /// </summary>
    [StaFact]
    public async Task Pressing_Build_in_the_real_window_returns_a_zoomed_camera_to_the_fitted_view()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        MainWindowHost.AcceptSends(vm);
        var graph = window.Shell.GraphHost;

        graph.HandleWheel(Anchor, 120);
        Assert.NotEqual(GraphCamera.Default, graph.LiveCameraForTest); // ön-koşul: ekran GERÇEKTEN zoomlu

        await vm.BuildCommand.ExecuteAsync(null); // üretim yolu: komut → koreografi → BeginOperation

        Assert.Equal(GraphCamera.Default, graph.CurrentCamera);
        Assert.Equal(GraphCamera.Default, graph.LiveCameraForTest);
        GC.KeepAlive(window);
    }
}
