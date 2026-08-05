using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sinema] Manuel kamera jestleri (spec §3.4): boş zeminde sürükleme = pan (el imleci), wheel = imleç
/// merkezli zoom, eşik altı basış = seçim kaldırma (release'te). Jestler YALNIZ sinema modunda çalışır;
/// küçük grafta bugünkü down-anında seçim kaldırma birebir korunur (<see cref="GraphClickTests"/> onu
/// pinlemeye devam eder).
///
/// <para>Testlerin ÇOĞU internal seam'leri (<c>HandlePanStart/Move/End</c>, <c>HandleWheel</c>) doğrudan sürer:
/// headless'ta gerçek mouse capture alınamaz (<c>PresentationSource</c> yok) ve mantığın tamamı seam'lerdedir.
/// Ama ctor kablosunun KENDİ kararları (sinema kolu · capture kaybı = iptal) seam'lerin üstündedir — onlar
/// <see cref="MouseInput"/> ile GERÇEK routed event yükseltilerek pinlenir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphPanZoomTests
{
    /// <summary>Eşiği AŞAN sürükleme deltası — platform eşiği (4px) mertebesinin çok üstünde, dolayısıyla
    /// makine ayarından bağımsız olarak "sürükleme"dir.</summary>
    private static readonly Vector DragDelta = new(40, -25);

    /// <summary>Jestlerin başladığı ekran noktası. Panel MERKEZİNİN (300, 186) dışında olması ŞARTTIR:
    /// merkezdeki bir imleç, "imleç merkezli zoom" ile "panel merkezli zoom"u ayırt edilemez kılar (ölçüldü —
    /// bkz. <c>GraphCameraTests.Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed</c>).</summary>
    private static readonly Point Anchor = new(430, 120);

    /// <summary>Sinema bandında ölçülmüş view + zincir kenarları. Düğüm/kenar/statü fixture'larının TAMAMI
    /// <see cref="GraphCinemaTests"/>'tedir (kopya YASAK); burada yalnız bir araya getirilirler.</summary>
    /// <param name="animations">Motion kapısı — yalnız "kamera uçuştayken dondur" testi açık ister.</param>
    private static GraphView CinemaView(out IReadOnlyList<GraphNode> nodes, bool animations = false)
    {
        nodes = GraphCinemaTests.BigNodes();
        var view = GraphCinemaTests.NewView(animations);
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        return view;
    }

    [StaFact]
    public void A_drag_beyond_the_threshold_pans_the_camera_and_enters_manual_mode()
    {
        var view = CinemaView(out _);
        var before = view.CurrentCamera;
        long t0 = Environment.TickCount64;

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta); // eşik aşıldı
        view.HandlePanEnd();

        Assert.True(view.IsManualCamera);
        // Aritmetik GraphCameraTests'te pinlidir; burada ölçülen KABLAJDIR: kamera GraphCamera.Pan'den ve tam
        // bu deltayla türetilmiş mi. NOT — gözlenen eksen X'tir: bu fixture'da graf yüksekliği (392 × 0.68 =
        // 266.6) 372'lik viewport'a SIĞDIĞI için ClampPan dikeyde her zaman ortalar ve DragDelta.Y yutulur.
        // Y ekseni saf tarafta ölçülür (GraphCameraTests, 2000×1000 graf).
        Assert.Equal(GraphCamera.Pan(before, DragDelta, view.ViewportSize, view.GraphSize), view.CurrentCamera);
        // Bırakma anı damgalanır — Task 7'nin 4sn'lik takip dönüşü bu damgadan sayar.
        Assert.InRange(view.LastManualInputTicks, t0, Environment.TickCount64);
    }

    [StaFact]
    public void Each_move_pans_by_its_OWN_delta_so_the_point_under_the_hand_tracks_the_graph()
    {
        // Delta birikirse (`_panLast` tazelenmezse) kamera imleç hızının KATLARIYLA kayar. Aynı fixture'da
        // ölçüldü: iki 40px hareketten sonra doğru Tx = −89, biriken mutantta −49.
        var view = CinemaView(out _);
        var before = view.CurrentCamera;
        var viewport = view.ViewportSize;
        var graph = view.GraphSize;

        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        view.HandlePanMove(Anchor + DragDelta + DragDelta);

        Assert.Equal(
            GraphCamera.Pan(GraphCamera.Pan(before, DragDelta, viewport, graph), DragDelta, viewport, graph),
            view.CurrentCamera);
    }

    [StaFact]
    public void Every_gesture_step_stamps_a_manual_input_so_a_slow_drag_cannot_be_interrupted()
    {
        // İki damganın da GEREKLİ olduğu yer: yalnız bırakmada damgalasaydık 4 saniyeden uzun YAVAŞ bir
        // sürüklemede Task 7'nin dönüş timer'ı jestin ortasında kamerayı geri alırdı; yalnız harekette
        // damgalasaydık hareketsiz beklenip bırakılan bir jestte damga bayat kalırdı.
        // Sayaçla ölçülür çünkü iki damga AYNI milisaniyeye düşer — duvar saati onları ayırt edemez.
        var view = CinemaView(out _);
        Assert.Equal(0, view.ManualInputCount);

        view.HandlePanStart(Anchor);
        Assert.Equal(0, view.ManualInputCount); // basış tek başına girdi DEĞİL (henüz tıklama olabilir)

        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(1, view.ManualInputCount);
        view.HandlePanMove(Anchor + DragDelta + DragDelta);
        Assert.Equal(2, view.ManualInputCount);

        view.HandlePanEnd();
        Assert.Equal(3, view.ManualInputCount);
    }

    [StaFact]
    public void A_subthreshold_press_release_on_the_ground_clears_the_selection_without_entering_manual_mode()
    {
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        var before = view.CurrentCamera;

        view.HandlePanStart(Anchor);
        // Platform drag eşiğinin ALTINDA bir kıpırdama — eşik SABİT YAZILMAZ, sistemden okunur (farklı ayarlı
        // bir makinede sabit bir sayı testi sessizce anlamsız kılardı).
        view.HandlePanMove(Anchor + new Vector(
            SystemParameters.MinimumHorizontalDragDistance / 2,
            SystemParameters.MinimumVerticalDragDistance / 2));
        Assert.False(view.IsManualCamera);        // eşik altı: henüz sürükleme DEĞİL
        Assert.Equal(before, view.CurrentCamera); // ...ve kamera kıpırdamadı

        view.HandlePanEnd(); // eşik aşılmadan bırakıldı → tıklama

        // Seçim kalkar (release'te — spec §3.4) ve manuel moda GİRİLMEZ. Kameranın kendisi seçim kalktığı için
        // normal otomatik hedefine döner (fit) — o davranış Task 4'te pinli.
        Assert.Null(view.SelectedNode);
        Assert.False(view.IsManualCamera);
    }

    [StaFact]
    public void The_wheel_zooms_at_the_cursor_and_enters_manual_mode()
    {
        var view = CinemaView(out _);
        var before = view.CurrentCamera;
        long t0 = Environment.TickCount64;

        view.HandleWheel(Anchor, 120);

        Assert.True(view.IsManualCamera);
        Assert.Equal(GraphCamera.ZoomAt(before, Anchor, GraphCamera.WheelZoomStep, view.ViewportSize, view.GraphSize),
            view.CurrentCamera);
        Assert.InRange(view.LastManualInputTicks, t0, Environment.TickCount64);
    }

    [StaFact]
    public void A_negative_wheel_delta_zooms_out_by_the_same_step()
    {
        var view = CinemaView(out _);
        var before = view.CurrentCamera;

        view.HandleWheel(Anchor, -120);

        Assert.Equal(
            GraphCamera.ZoomAt(before, Anchor, 1 / GraphCamera.WheelZoomStep, view.ViewportSize, view.GraphSize),
            view.CurrentCamera);
        Assert.True(view.CurrentCamera.Scale < before.Scale); // yön gerçekten TERS (aynı kademe, tersine)
    }

    [StaFact]
    public void Manual_mode_suppresses_automatic_retargeting()
    {
        var view = CinemaView(out var nodes);
        view.HandleWheel(Anchor, 120);
        var manual = view.CurrentCamera;

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.Equal(manual, view.CurrentCamera); // frontier kamerayı ÇEKEMEZ — manuel mod askıda tutar
    }

    [StaFact]
    public void Gestures_are_inert_outside_cinema()
    {
        var nodes = GraphCinemaTests.BigNodes(36); // tam-detay bandı: sinema KAPALI
        var view = GraphCinemaTests.NewView();
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        var before = view.CurrentCamera;

        view.HandleWheel(Anchor, 120);
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        view.HandlePanEnd();

        Assert.False(view.IsManualCamera);
        Assert.Equal(before, view.CurrentCamera); // küçük graf: bugünkü davranış birebir
    }

    [StaFact]
    public void Gestures_are_inert_before_the_camera_has_a_target()
    {
        // Ölçülmemiş view: ApplyCamera viewport guard'ında erken döner ⇒ kamera hedefi HİÇ hesaplanmadı
        // (Scale = 0). ZoomAt dünya noktasını MEVCUT kameradan türetir — 0 ölçekte ±∞/NaN çıkar ve kamera
        // onarılamaz biçimde bozulurdu.
        var nodes = GraphCinemaTests.BigNodes();
        var view = GraphTestView.New(labelFontFamily: DsResources.MonoFontFamily); // Measure/Arrange YOK
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        Assert.True(view.IsCullEnabled);              // ön-koşul: sinema bandı (jest kapısı açık)
        Assert.Equal(0.0, view.CurrentCamera.Scale);  // ön-koşul: kamera hedefi yok

        view.HandleWheel(Anchor, 120);
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        view.HandlePanEnd();

        Assert.False(view.IsManualCamera);
        Assert.Equal(default, view.CurrentCamera);
    }

    [StaFact]
    public void During_a_drag_the_ground_shows_the_hand_cursor_and_releases_it_after()
    {
        var view = CinemaView(out _);

        view.HandlePanStart(Anchor);
        Assert.Null(view.Ground.Cursor); // basış tek başına imleci DEĞİŞTİRMEZ (henüz tıklama olabilir)
        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(Cursors.Hand, view.Ground.Cursor);

        view.HandlePanEnd();

        Assert.Null(view.Ground.Cursor);
    }

    [StaFact]
    public void A_new_topology_cancels_an_in_flight_gesture_and_leaves_manual_mode()
    {
        var view = CinemaView(out var nodes);
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta); // sürükleme başladı: manuel mod + el imleci
        Assert.True(view.IsManualCamera);

        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes)); // yeni topoloji (MainWindow rebuild yolu)

        Assert.False(view.IsManualCamera); // kamera yeni grafı yeniden hedefler
        Assert.Null(view.Ground.Cursor);   // yarım kalan sürükleme el imlecini EKRANDA BIRAKMAZ
        // ...ve jest gerçekten iptal: aynı sürüklemenin devamı kamerayı artık oynatmaz.
        var retargeted = view.CurrentCamera;
        view.HandlePanMove(Anchor + DragDelta + DragDelta);
        Assert.Equal(retargeted, view.CurrentCamera);
    }

    [StaFact]
    public void A_panel_resize_still_materialises_while_the_camera_is_manual()
    {
        // Manuel mod otomatik HEDEFLEMEYİ askıya alır, cull'u DEĞİL: panel büyürse (SizeChanged) yeni görünür
        // olan düğümler yine kurulmalıdır — aksi halde kullanıcı pencereyi büyüttüğünde boş şerit görürdü.
        var view = CinemaView(out var nodes);
        view.HandleWheel(Anchor, -120); // uzaklaş → manuel mod
        int before = view.NodeVisuals.Count;
        Assert.True(before < nodes.Count, "ön-koşul: cull gerçekten bir şey eliyor");

        GraphTestView.Resize(view, new Size(1400, 800));
        view.UpdateLayout();

        Assert.True(view.NodeVisuals.Count > before,
            $"panel büyüdü ama hiçbir yeni düğüm materyalize olmadı ({before} → {view.NodeVisuals.Count})");
        Assert.True(view.IsManualCamera); // büyüme manuel modu BOZMAZ
    }

    // ---------------------------------------------------------------- kamera UÇUŞTAYKEN (motion AÇIK)

    [StaFact]
    public void Grabbing_the_graph_mid_flight_freezes_the_current_frame_not_the_animation_target()
    {
        // BU TESTİN VAR OLMA SEBEBİ: animasyon KAPALIYKEN LiveCamera her an CurrentCamera'ya eşittir, yani
        // EnterManualCamera'nın "uçuştaki kareyi dondur" satırı ölçülemez — onu silen ve CurrentCamera'yı
        // donduran iki mutant da diğer tüm jest testlerini yeşil bırakır (ölçüldü). Motion AÇIK bir view
        // gerekiyor: 460ms'lik geçiş henüz t=0'dayken kamera hâlâ ESKİ karededir.
        //
        // Üretimde gözlenen kusur: mutantta kullanıcı grafı kavradığı an graf önce animasyonun HEDEFİNE
        // sıçrar, sonra pan başlar.
        var view = CinemaView(out _, animations: true);
        var beforeFlight = view.CurrentCamera; // SetGraph animate:false ile SNAP etti — canlı transform budur
        var viewport = view.ViewportSize;
        var graph = view.GraphSize;

        view.SelectedNode = "N40"; // animasyonlu yeniden hedefleme başlar

        Assert.True(view.LastCameraAnimated, "ön-koşul: geçiş gerçekten animasyonlu dala girdi");
        var target = view.CurrentCamera;
        Assert.NotEqual(beforeFlight, target); // ön-koşul: hedef canlı kareden GERÇEKTEN farklı

        view.HandleWheel(Anchor, 120);

        // Zoom UÇUŞTAKİ kareden türer (animasyon henüz ilerlemedi ⇒ canlı kare = beforeFlight)...
        Assert.Equal(GraphCamera.ZoomAt(beforeFlight, Anchor, GraphCamera.WheelZoomStep, viewport, graph),
            view.CurrentCamera);
        // ...hedeften DEĞİL. (İki ifade birbirinden farklı olmasaydı iddia hiçbir şey ölçmezdi.)
        Assert.NotEqual(GraphCamera.ZoomAt(target, Anchor, GraphCamera.WheelZoomStep, viewport, graph),
            view.CurrentCamera);
        // Manuel uygulama animasyonsuzdur — seam son OTOMATİK geçişin bayat değerini taşımaz.
        Assert.False(view.LastCameraAnimated);
    }

    // ---------------------------------------------------------------- ctor kablosu (GERÇEK routed event)

    [StaFact]
    public void In_cinema_pressing_the_ground_keeps_the_selection_until_the_release()
    {
        // Ctor'daki sinema kolu (`HandlePanStart` başarılıysa capture al, aksi halde bugünkü DOWN-anında
        // kaldırma) SEAM'lerin ÜSTÜNDEDİR ve yalnız gerçek bir routed event onu sürer. Kapıyı kaldıran mutant
        // — basış her zaman seçimi silsin — süitin geri kalanını yeşil bırakır ama spec'in yasakladığı davranışı
        // geri getirir: sinemada grafı kavramak için basmak seçimi ANINDA siler.
        var view = CinemaView(out _);
        view.SelectedNode = "N5";

        MouseInput.PressLeft(view.Ground);

        Assert.Equal("N5", view.SelectedNode); // DOWN seçimi KALDIRMAZ (click-vs-drag ayrımı sürüyor)

        view.HandlePanEnd(); // eşik aşılmadı → tıklama, kaldırma BURADA

        Assert.Null(view.SelectedNode);
    }

    [StaFact]
    public void Losing_the_capture_cancels_the_gesture_instead_of_counting_as_a_release()
    {
        // Spec §3.4 seçimi "eşik aşılmadan BIRAKILIRSA" kaldırır. Capture kaybı (Alt+Tab, popup, başka öğenin
        // capture alması) bırakma değil İPTALDİR — kullanıcı hiçbir şeye tıklamamışken seçimini kaybetmemeli.
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        var before = view.CurrentCamera;
        view.HandlePanStart(Anchor);

        MouseInput.LoseCapture(view.Ground);

        Assert.Equal("N5", view.SelectedNode); // seçime DOKUNULMADI
        Assert.False(view.IsManualCamera);
        // ...ve jest gerçekten bitti: sonraki hareket kamerayı oynatmaz (yarım kalan sürükleme sürmez).
        view.HandlePanMove(Anchor + DragDelta);
        Assert.Equal(before, view.CurrentCamera);
        Assert.Null(view.Ground.Cursor);
    }
}
