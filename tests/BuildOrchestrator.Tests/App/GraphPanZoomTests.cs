using System.Windows;
using System.Windows.Automation;
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
    /// bkz. <c>GraphCameraTests.Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed</c>).
    /// <see cref="GraphCinemaTests"/> de kullanır (manuel moddayken etiket kararı) — jest noktası tek yerde,
    /// düğüm/statü fixture'larının ters yönde paylaşılmasıyla aynı gerekçe (kopya YASAK).</summary>
    internal static readonly Point Anchor = new(430, 120);

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

    /// <summary>
    /// [final review · I1] Cull'un JESTİN KENDİSİ sırasında koşması ayrı bir kuraldır ve ayrı ölçülür.
    ///
    /// <para><see cref="A_panel_resize_still_materialises_while_the_camera_is_manual"/> yalnız
    /// <c>SizeChanged</c> yolunu (<c>ApplyCamera</c>'nın manuel dalı) ölçer: <c>before</c> sayacını jestten
    /// SONRA aldığı için jestin kendi <c>UpdateMaterialization</c>'ını silen mutant onu yeşil bırakır
    /// (ölçüldü) — <c>before</c> yalnız küçülür, iki iddiası da geçmeye devam eder.</para>
    ///
    /// <para>Fixture KOŞU BİTMİŞ (<c>IsSettled</c>) ve seçimsizdir; hasarın en ağır olduğu rejim budur:
    /// koşarken 200 ms'lik statü tick'i <c>ApplyCamera</c> üzerinden cull'u yine çalıştırır ve boş şeridi
    /// ≤200 ms'de onarır, durgunda ise onaracak HİÇBİR yol yoktur — kullanıcı pencereyi yeniden
    /// boyutlandırana kadar ekranın kenarı boş kalır.</para>
    /// </summary>
    private static GraphView SettledCinemaView(out IReadOnlyList<GraphNode> nodes)
    {
        var view = CinemaView(out nodes);
        view.IsSettled = true;
        return view;
    }

    [StaFact]
    public void Zooming_out_materialises_the_newly_visible_nodes_even_with_the_run_finished()
    {
        var view = SettledCinemaView(out var nodes);
        int before = view.NodeVisuals.Count;
        Assert.True(before < nodes.Count, "ön-koşul: cull gerçekten bir şey eliyor");

        view.HandleWheel(Anchor, -120); // uzaklaş → görünür dünya dikdörtgeni büyür

        Assert.True(view.NodeVisuals.Count > before,
            $"uzaklaşıldı ama hiçbir yeni düğüm kurulmadı ({before} → {view.NodeVisuals.Count})");
    }

    [StaFact]
    public void Dragging_materialises_the_nodes_the_pan_brings_into_view_even_with_the_run_finished()
    {
        var view = SettledCinemaView(out var nodes);
        int before = view.NodeVisuals.Count;
        Assert.True(before < nodes.Count, "ön-koşul: cull gerçekten bir şey eliyor");

        view.HandlePanStart(Anchor);
        // Sağa doğru sürükleme grafı sağa iter ⇒ SOLDAKİ bant görünür alana girer. Tek adım yeterli olmalı,
        // ama jest gerçekçi olsun diye üç adım sürülür (her adım kendi deltasıyla panler).
        for (int i = 1; i <= 3; i++) view.HandlePanMove(Anchor + PanRight * i);

        Assert.True(view.NodeVisuals.Count > before,
            $"pan yapıldı ama hiçbir yeni düğüm kurulmadı ({before} → {view.NodeVisuals.Count})");
    }

    /// <summary>Yatay, eşik ÜSTÜ bir sürükleme adımı. <see cref="DragDelta"/>'dan ayrıdır: orada dikey bileşen
    /// bu fixture'da <c>ClampPan</c> tarafından yutulur (graf dikeyde sığar), burada ölçülen şey yeni düğüm
    /// KURULMASI olduğu için deltanın tamamı yatay tutulur.</summary>
    private static readonly Vector PanRight = new(60, 0);

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

    // ---------------------------------------------------------------- takip dönüşü (spec §3.5)
    // Düğüm/kenar/statü fixture'larının TAMAMI GraphCinemaTests'tedir (kopya YASAK); burada yalnız birleştirilir.
    // Gecikme değeri de tekrarlanmaz: sınırın iki yakası GraphCamera.FollowResumeDelayMs'ten türetilir.

    /// <summary>Damganın <c>TickCount64</c> uzayındaki tam sınırı — üretimin sabiti, kopyası değil.</summary>
    private static long ResumeDelayTicks => (long)GraphCamera.FollowResumeDelayMs;

    [StaFact]
    public void Follow_resumes_only_after_the_delay_and_only_with_a_target()
    {
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building)); // hedef VAR
        view.HandleWheel(Anchor, 120);                                                       // manuel mod
        long t0 = view.LastManualInputTicks;
        // Ön-koşul: zoom kamerayı cepheden GERÇEKTEN ayırdı — aksi halde aşağıdaki ölçek iddiası boşlukta kalırdı.
        Assert.NotEqual(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale);

        Assert.False(view.TryResumeFollow(t0 + ResumeDelayTicks - 1)); // 1 ms eksik → dönüş YOK
        Assert.True(view.IsManualCamera);

        Assert.True(view.TryResumeFollow(t0 + ResumeDelayTicks));      // tam sınır → takip devralır
        Assert.False(view.IsManualCamera);
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale); // cepheyi yeniden çerçeveledi
    }

    [StaFact]
    public void Manual_camera_persists_while_there_is_nothing_to_follow()
    {
        // Kamera kullanıcıyla KAVGA ETMEZ (spec §3.5): koşu bittiyse ve seçim yoksa "dönülecek" tek yer
        // kuşbakışı merkezdir — kullanıcıyı oraya geri sürüklemek onun gezindiği yeri elinden almak olurdu.
        var view = CinemaView(out _);
        view.IsSettled = true;
        view.HandleWheel(Anchor, 120);
        long t0 = view.LastManualInputTicks;

        Assert.False(view.TryResumeFollow(t0 + 25 * ResumeDelayTicks)); // süre KAT KAT geçse de dönüş yok
        Assert.True(view.IsManualCamera);
    }

    [StaFact]
    public void A_selection_counts_as_a_follow_target()
    {
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        view.HandleWheel(Anchor, 120);
        long t0 = view.LastManualInputTicks;
        Assert.NotEqual(GraphCamera.SelectionScale, view.CurrentCamera.Scale); // ön-koşul: zoom seçimden ayırdı

        Assert.True(view.TryResumeFollow(t0 + ResumeDelayTicks));

        Assert.Equal(GraphCamera.SelectionScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void Selecting_a_project_resumes_follow_immediately_instead_of_waiting_out_the_delay()
    {
        // Duraklama OTOMATİK yeniden hedeflemeye (statü tick'i) karşıdır; seçim ise kullanıcının KENDİ
        // navigasyonudur (§13.7'nin tek jesti: liste satırı / graf düğümü / stream satırı). Bastırılırsa
        // kullanıcı "tıkladım, hiçbir şey olmadı" görür — bu bir kusurdur, özellik değil.
        var view = CinemaView(out _);
        view.HandleWheel(Anchor, 120);
        Assert.True(view.IsManualCamera);
        Assert.NotEqual(GraphCamera.SelectionScale, view.CurrentCamera.Scale); // ön-koşul

        view.SelectedNode = "N5";

        Assert.False(view.IsManualCamera);                                  // 4 sn BEKLENMEDİ
        Assert.Equal(GraphCamera.SelectionScale, view.CurrentCamera.Scale); // ...ve kamera gerçekten gitti
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
        Assert.False(view.IsFollowResumeTimerArmed);
    }

    [StaFact]
    public void Clearing_the_selection_does_not_end_manual_mode_because_null_is_not_a_place_to_go()
    {
        // [fix round 1 · C1] E1'in aynası ve SINIRI. null bir "gidilecek yer" değildir: seçim boş zemine
        // tıklayarak (HandlePanEnd) ya da grafta karşılığı olmayan bir projeye geçilerek
        // (MainWindow.PushGraphSelection null iter) kalkabilir. Koşulsuz dönseydik takip edilecek hiçbir şey
        // yokken kamera kullanıcının excursion'ını 460 ms'de silip kuşbakışına yapışırdı — TryResumeFollow'un
        // hedef klozunun (Manual_camera_persists_while_there_is_nothing_to_follow) yasakladığı davranışın
        // ta kendisi, yalnız BAŞKA bir kapıdan.
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        view.HandleWheel(Anchor, 120);
        var excursion = view.CurrentCamera;
        Assert.True(view.IsManualCamera);
        // Ön-koşul: dönülseydi kamera GERÇEKTEN başka bir yere giderdi (koşu yok ⇒ hedef kuşbakışı fit).
        Assert.NotEqual(GraphCamera.FitScale(view.ViewportSize, view.GraphSize), excursion.Scale);

        view.SelectedNode = null;

        Assert.True(view.IsManualCamera);          // manuel mod SÜRER
        Assert.Equal(excursion, view.CurrentCamera); // ...ve kamera kullanıcının bıraktığı yerde durur
    }

    // ---------------------------------------------------------------- manuel çıkışta Zeno latch'leri sıfırlanır

    /// <summary>Kalabalık katmanın ORTASINA oturan bir cephe: kuşbakışı kelepçesine (ClampPan) takılmadığı için
    /// odak farkı kamerada gerçekten görünür. Genişlik 5'tir ve bu ALT SINIRDIR: bir aralıklık kayma cepheyi
    /// <c>34/genişlik</c> px oynatır, 4'te 8.5px (eşiğin ÜSTÜ — bastırma hiç olmazdı, test boşa düşerdi).</summary>
    private const int FrontierStart = 17;
    private const int FrontierWidth = 5;

    /// <summary>Panelin manuel excursion sırasındaki büyümesi. Ölçek hedefini 0.0316 kaydırır — 0.05'lik Zeno
    /// eşiğinin ALTINDA, yani bayat bir latch onu bastırırdı (testin ön-koşulu bunu ayrıca doğrular).</summary>
    private const double PanelGrowthPx = 13.0;

    private static Point[] Centres(GraphView view, IEnumerable<string> names) => [.. names.Select(view.NodeCenter)];

    /// <summary>Cephenin ağırlık merkezi — ÜRETİMİN kendi fonksiyonundan, latch'siz. Elde ortalama almak
    /// ResolveFocus'un aritmetiğini teste kopyalamak olurdu.</summary>
    private static Point Cog(GraphView view, IReadOnlyList<string> names) =>
        GraphCamera.ResolveFocus(null, Centres(view, names), settled: false, view.GraphSize, previousFocus: null);

    [StaFact]
    public void Leaving_manual_mode_retargets_the_focus_even_under_the_frontier_threshold()
    {
        // Kullanıcı saniyelerce gezindi; bu arada gelen statü güncellemeleri manuel guard'da kesildi, yani
        // latch EXCURSION ÖNCESİNE aittir. Gerçek senaryo (geniş paralel cephe): bir proje biter, bir aralık
        // sağındaki komşusu başlar → ağırlık merkezi 34/5 = 6.8px, yani 8px eşiğinin ALTINDA oynar. Latch
        // korunsaydı kamera dönerken ESKİ odağa oturur ve cepheyi ıskalardı.
        var view = CinemaView(out var nodes);
        string[] layer0 = [.. nodes.Where(n => n.Layer == 0).Select(n => n.Name)];
        string[] frontier = [.. layer0[FrontierStart..(FrontierStart + FrontierWidth)]];
        string finishing = frontier[^1];
        string starting = layer0[FrontierStart + FrontierWidth];
        string[] swapped = [.. frontier[..^1], starting];

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, frontier, GraphStatus.Building));
        var focusBefore = Cog(view, frontier);
        Assert.Equal(focusBefore, view.PreviousFocus); // ön-koşul: frontier odağı GERÇEKTEN latch'lendi

        view.HandleWheel(Anchor, 120); // manuel excursion
        var moved = GraphCinemaTests.WithStatus(nodes, swapped, GraphStatus.Building);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(moved, finishing, GraphStatus.Succeeded));

        var focusAfter = Cog(view, swapped);
        Assert.Equal(focusBefore, view.PreviousFocus);                     // ön-koşul: latch bayat kaldı
        Assert.NotEqual(focusBefore, focusAfter);                          // ön-koşul: cephe gerçekten kaydı...
        Assert.False(GraphCamera.ShouldRetarget(focusBefore, focusAfter)); // ...ama eşiğin ALTINDA

        view.ResumeFollowNow();

        double scale = GraphCamera.FrontierScale(view.ViewportSize, Centres(view, swapped));
        var viewport = view.ViewportSize;
        var graph = view.GraphSize;
        Assert.Equal(GraphCamera.Compute(viewport, graph, focusAfter, scale), view.CurrentCamera);
        // Bayat latch korunsaydı kamera BAŞKA bir yere otururdu — iddia boşlukta durmuyor.
        Assert.NotEqual(GraphCamera.Compute(viewport, graph, focusBefore, scale), view.CurrentCamera);
    }

    [StaFact]
    public void Leaving_manual_mode_retargets_the_scale_even_under_the_rescale_threshold()
    {
        // Odak eşiğinin ÖLÇEK kardeşi — ve kasten YALNIZ ölçeği oynatır: cephe hiç değişmez (odak sabit),
        // excursion sırasında yalnız PANEL büyür. Manuel guard SizeChanged'i de kestiği için latch bayattır.
        var view = CinemaView(out var nodes);
        string[] column = ["N0", "N1", "N2", "N3"]; // dört katmanın İLK düğümü: tek sütun, dikey cephe
        var centres = Centres(view, column);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, column, GraphStatus.Building));

        double before = GraphCamera.FrontierScale(view.ViewportSize, centres);
        Assert.Equal(before, view.PreviousScale); // ön-koşul: frontier ölçeği GERÇEKTEN latch'lendi

        view.HandleWheel(Anchor, 120); // manuel excursion
        GraphTestView.Resize(view, new Size(view.ActualWidth, view.ActualHeight + PanelGrowthPx));
        view.UpdateLayout();

        double after = GraphCamera.FrontierScale(view.ViewportSize, centres);
        Assert.Equal(before, view.PreviousScale);              // ön-koşul: latch bayat kaldı
        Assert.NotEqual(before, after);                        // ön-koşul: hedef ölçek gerçekten değişti...
        Assert.False(GraphCamera.ShouldRescale(before, after)); // ...ama eşiğin ALTINDA
        Assert.Equal(Cog(view, column), view.PreviousFocus);   // ön-koşul: odak SABİT — ölçülen yalnız ölçek

        view.ResumeFollowNow();

        Assert.Equal(after, view.CurrentCamera.Scale);
    }

    // ---------------------------------------------------------------- tek atımlık tetik (spec §3.6)

    /// <summary>Uzun bir sürüklemenin hareket sayısı — üretimde saniyede 100+'tır; buradaki sayı yalnız
    /// "damga tazelenir ama tetik yeniden kurulmaz" ayrımını gözle görülür kılacak kadar büyüktür.</summary>
    private const int LongDragMoves = 12;

    [StaFact]
    public void A_long_drag_refreshes_the_stamp_on_every_move_but_arms_the_resume_trigger_once()
    {
        // NoteManualInput HER harekette koşar (Task 6: yavaş bir sürükleme jestin ortasında kesilmesin diye).
        // Tetiği de her harekette Stop()/Start() etmek "sürekli çalışan YENİ bir şey" olurdu (spec §3.6).
        // Sayaçla ölçülür: iki damga aynı milisaniyeye düşer, duvar saati onları ayırt edemez.
        var view = CinemaView(out _);
        view.SelectedNode = "N5"; // dönülecek bir hedef olsun (hedefsiz manuel mod tetik KURMAZ)
        view.HandlePanStart(Anchor);

        for (int i = 1; i <= LongDragMoves; i++) view.HandlePanMove(Anchor + DragDelta * i);
        view.HandlePanEnd();

        Assert.Equal(LongDragMoves + 1, view.ManualInputCount); // her hareket + bırakma damgalandı
        Assert.Equal(1, view.FollowResumeArmCount);             // ...ama tetik BİR kez kuruldu
        Assert.True(view.IsFollowResumeTimerArmed);
    }

    /// <summary>Tick'in erken geldiği varsayılan aralık: damgadan bu yana geçen süre. Kalanı (gecikme eksi bu)
    /// tam olarak hesaplanabilir kılar — "tam süre mi kalan mı" ayrımı ancak böyle gözlenir.</summary>
    private const long EarlyTickElapsedMs = 1000;

    [StaFact]
    public void A_tick_that_lands_before_the_delay_rearms_for_the_remainder()
    {
        // ESKİ HÂLİ (bu task'ın ilk turu · A_tick_that_lands_inside_a_still_running_gesture_rearms_for_the_
        // remainder): aynı iddiayı SÜRMEKTE OLAN bir sürüklemenin (HandlePanStart + HandlePanMove) ortasında
        // ölçüyordu ve kalan 3 sn bekliyordu. DEĞİŞME GEREKÇESİ (ölçüldü, fix round 1): tuş BASILIYKEN dönüş
        // sayacı hiç başlamaz (GraphView.FollowResumeSince) ⇒ o kurulumda kalan artık TAM 4 sn'dir ve test
        // "kalan mı tam süre mi" ayrımını ölçemez olurdu. Ayrım bu yüzden tuşun BIRAKILDIĞI bir manuel
        // oturumda (wheel) ölçülür; duran sürüklemenin kendi kuralı ayrı testtedir.
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building)); // hedef VAR
        view.HandleWheel(Anchor, 120);
        long t0 = view.LastManualInputTicks;
        Assert.Equal(1, view.FollowResumeArmCount);

        view.HandleFollowResumeTick(t0 + EarlyTickElapsedMs); // süre dolmadan geldi

        Assert.True(view.IsManualCamera);           // erken tick kamerayı ÇALMAZ
        Assert.Equal(2, view.FollowResumeArmCount); // ...ve tetik yeniden kuruldu
        Assert.True(view.IsFollowResumeTimerArmed);
        Assert.Equal(                               // KALAN kadar: tam süre verilseydi dönüş katlanarak gecikirdi
            TimeSpan.FromMilliseconds(GraphCamera.FollowResumeDelayMs - EarlyTickElapsedMs),
            view.FollowResumeInterval);
    }

    [StaFact]
    public void A_held_gesture_never_hands_the_camera_back_and_the_trigger_does_not_spin()
    {
        // [fix round 1 · I1] Damga mekanizması yalnız HAREKET EDEN sürüklemeyi korur (HandlePanMove her
        // harekette damgalar). Kullanıcı okumak için elini kıpırdatmadan tutarsa damga bayatlar ve 4 sn sonra
        // takip devralırdı — tuş HÂLÂ BASILIYKEN kamera hedefe uçardı.
        //
        // İkinci iddia aynı derecede önemli: kuralı TryResumeFollow'a bir `if (_panPressed) return false;`
        // olarak yazmak yetmez. Tetiği kuran taraf ham damgayı okumaya devam etseydi "kalan = 0" hesaplar ve
        // tetiği 1 ms sonrasına kurardı ⇒ jest boyunca ~1 kHz dönen bir DispatcherTimer (spec §3.6 ihlali).
        // Bu yüzden aralık da TAM süre olmalıdır: sayaç henüz BAŞLAMAMIŞTIR.
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building)); // hedef VAR
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        long t0 = view.LastManualInputTicks;
        var held = view.CurrentCamera;

        view.HandleFollowResumeTick(t0 + ResumeDelayTicks); // el kıpırdamadı, süre "doldu"

        Assert.True(view.IsManualCamera);      // kamera KULLANICIDA kalır...
        Assert.Equal(held, view.CurrentCamera); // ...ve hiç oynamaz
        Assert.Equal(2, view.FollowResumeArmCount);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphCamera.FollowResumeDelayMs), view.FollowResumeInterval);

        view.HandlePanEnd(); // tuş bırakıldı → sayaç ANCAK ŞİMDİ işlemeye başlar
        long t1 = view.LastManualInputTicks;

        Assert.False(view.TryResumeFollow(t1 + ResumeDelayTicks - 1));
        Assert.True(view.TryResumeFollow(t1 + ResumeDelayTicks));
    }

    [StaFact]
    public void A_selection_arriving_mid_drag_does_not_leave_the_gesture_panning_outside_manual_mode()
    {
        // [fix round 1 · I1] Manuel mod jest SÜRERKEN dışarıdan bitebilir: klavyeyle/listeden gelen bir seçim
        // MainWindow.PushGraphSelection üzerinden SelectedNode'a yazar ve E1 kuralı takibi hemen döndürür.
        // Manuel moda giriş yalnız eşiğin aşıldığı karede yapılsaydı, elin altındaki graf o andan sonra
        // `_manualCamera == false` iken kayardı: pil kapalı kalır, dönüş tetiği hiç kurulmaz ve ilk statü
        // tick'i kamerayı kullanıcının elinden alırdı.
        var view = CinemaView(out _);
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        Assert.True(view.IsManualCamera);

        view.SelectedNode = "N5";        // dış kaynaklı seçim → takip devralır
        Assert.False(view.IsManualCamera); // ön-koşul: manuel mod GERÇEKTEN bitti (E1)

        view.HandlePanMove(Anchor + DragDelta + DragDelta); // ...ama el hâlâ grafın üstünde

        Assert.True(view.IsManualCamera);                            // kullanıcı kontrolü geri aldı
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility); // pil de doğru durumu gösteriyor
        Assert.True(view.IsFollowResumeTimerArmed);                  // ...ve dönüş yine garanti
    }

    [StaFact]
    public void Nothing_to_follow_means_no_trigger_at_all_so_the_view_stays_asleep()
    {
        // Kardeş kural: dönülecek yer yokken uyanacak bir tetik de OLMAZ (boşta dönen bir timer §3.6 ihlalidir).
        var view = CinemaView(out _);
        view.IsSettled = true;
        view.HandleWheel(Anchor, 120);

        Assert.True(view.IsManualCamera);
        Assert.Equal(1, view.ManualInputCount);     // girdi damgalandı...
        Assert.Equal(0, view.FollowResumeArmCount); // ...ama kurulacak bir tetik yok
        Assert.False(view.IsFollowResumeTimerArmed);
    }

    [StaFact]
    public void A_run_that_starts_while_the_camera_is_manual_revives_the_resume_trigger()
    {
        // Hedefsiz manuel modda tetik yoktur; hedef koşu SIRASINDA doğarsa onu uyandıran statü tick'idir —
        // aksi halde kullanıcı, ekranda bir build akarken manuel modda sonsuza dek asılı kalırdı.
        var view = CinemaView(out var nodes);
        view.IsSettled = true;
        view.HandleWheel(Anchor, 120);
        Assert.Equal(0, view.FollowResumeArmCount);

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.True(view.IsManualCamera);            // 4 sn dolmadı → henüz dönmedi
        Assert.Equal(1, view.FollowResumeArmCount);  // ...ama dönüş artık GARANTİ
        Assert.True(view.IsFollowResumeTimerArmed);
    }

    [StaFact]
    public void A_gesture_cancelled_by_a_capture_loss_still_leaves_a_live_resume_trigger()
    {
        // Tetik yalnız jest SONUNDA (HandlePanEnd) kurulsaydı burada HİÇ kurulmazdı: capture kaybı
        // ResetPanGesture'a gider ve başka damga atılmaz. Alt+Tab'layan kullanıcı manuel modda ASILI kalırdı —
        // buradaki hedef SEÇİMDİR, yani onu uyandıracak bir statü tick'i de yok.
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        view.HandlePanStart(Anchor);
        view.HandlePanMove(Anchor + DragDelta);
        Assert.True(view.IsManualCamera);

        MouseInput.LoseCapture(view.Ground);

        Assert.True(view.IsManualCamera);           // iptal manuel modu bitirmez (kamera bırakıldığı yerde)
        Assert.True(view.IsFollowResumeTimerArmed); // ...ama dönüş yine de gelecek
    }

    [StaFact]
    public void A_new_topology_stops_the_pending_trigger_and_hides_the_pill()
    {
        var view = CinemaView(out var nodes);
        var building = GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building);
        view.UpdateStatuses(building);
        view.HandleWheel(Anchor, 120);
        Assert.True(view.IsFollowResumeTimerArmed);
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);

        // Statü KORUNUR (hedef hâlâ var) — pilin kapanmasının tek açıklaması manuel modun bitmesi olsun.
        view.SetGraph(building, GraphCinemaTests.ChainEdges(building));

        Assert.False(view.IsManualCamera);
        Assert.False(view.IsFollowResumeTimerArmed);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
    }

    [StaFact]
    public void An_empty_graph_hides_the_pill_even_though_the_camera_never_runs()
    {
        // Gerçek yol: kullanıcı gezinirken Sync 0 proje bulur. SetGraph boş grafta ERKEN döner, yani pili
        // tazeleyen huni (ApplyCamera) HİÇ koşmaz — pil ekranda asılı kalırdı.
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(Anchor, 120);
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);

        view.SetGraph([], []);

        Assert.True(view.IsEmptyStateVisible); // ön-koşul: gerçekten boş-durum yoluna girdik
        Assert.False(view.IsManualCamera);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
    }

    [StaFact]
    public void The_pill_sits_just_left_of_the_counter_so_the_machine_output_never_moves()
    {
        // Pil, başlık DockPanel'inde sayaçtan SONRA bildirilir (= sayacın İÇİNDE kalır). Ters sırada — brief'in
        // önerdiği gibi sayaçtan ÖNCE — sayaç, pil her belirip kaybolduğunda pilin genişliği kadar SIÇRAR;
        // design-v1 §1.2'nin makine çıktısı panelin sağ kenarına çakılı durmalıdır.
        var view = CinemaView(out var nodes);
        // Pilin ailesi (AppFonts.Ui) bir pack:// URI'sidir ve Application olmadan çözülmez ⇒ TrackedTextBlock
        // 0 ölçer ve pil yalnız padding+border kadar (16px) yer kaplardı; aşağıdaki "pil yer kaplıyor"
        // ön-koşulu o hâlde metinden bağımsız olarak geçerdi. file:// tabanlı aile enjekte edilir ki ön-koşul
        // gerçekten iddia ettiği şeyi (pil METNİYLE birlikte yer kaplıyor) ölçsün.
        view.FollowPillLabel.FontFamily = DsResources.MonoFontFamily;
        view.UpdateLayout(); // SetGraph sayacın METNİNİ yazar; yerleşim ancak burada oturur
        double before = CountsLeft(view);

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(Anchor, 120);
        view.UpdateLayout();

        var pill = view.FollowPillElement;
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility); // ön-koşul: pil gerçekten belirdi...
        Assert.True(pill.ActualWidth > 0, "pil yer kaplamıyor — iddia boşlukta kalırdı");
        Assert.Equal(before, CountsLeft(view)); // ...ama sayaç kıpırdamadı
        // ...ve pil TAM sayacın solunda duruyor (başlığın soluna ya da sayacın sağına kaçmadı).
        Assert.Equal(
            CountsLeft(view),
            pill.TranslatePoint(new Point(pill.ActualWidth, 0), view).X + pill.Margin.Right,
            precision: 6);
    }

    private static double CountsLeft(GraphView view) => view.CountsText.TranslatePoint(new Point(0, 0), view).X;

    [StaFact]
    public void Unloading_the_view_stops_the_pending_trigger_so_the_dispatcher_cannot_root_it()
    {
        // [M-d deseni] Uçuştaki bir DispatcherTimer dispatcher tarafından köklenir ve view ağaçtan düşse bile
        // onu (ve tüm graf ağacını) canlı tutardı — dash clock'la AYNI sınıf sızıntı.
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(Anchor, 120);
        Assert.True(view.IsFollowResumeTimerArmed);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        Assert.False(view.IsFollowResumeTimerArmed);
    }

    // ---------------------------------------------------------------- FOLLOW PAUSED pili (spec §3.5)

    [StaFact]
    public void The_pill_shows_while_follow_is_suspended_and_a_click_resumes_immediately()
    {
        var view = CinemaView(out var nodes);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility); // başlangıç: gizli

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility); // hedef var ama takip ÇALIŞIYOR
        view.HandleWheel(Anchor, 120);
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);   // hedef + manuel → görünür

        var click = MouseInput.PressLeft(view.FollowPillElement);      // XAML'deki GERÇEK handler kablosu

        Assert.True(click.Handled); // tıklama başlığın ötesine sızmaz
        Assert.False(view.IsManualCamera);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale);
        Assert.False(view.IsFollowResumeTimerArmed);
    }

    [StaFact]
    public void The_pill_stays_hidden_while_there_is_nothing_to_follow()
    {
        // Tıklansa hiçbir şey olmayacak bir kısayolu göstermek yalan olurdu (TryResumeFollow'un hedef klozu).
        var view = CinemaView(out _);
        view.IsSettled = true;
        view.HandleWheel(Anchor, 120);

        Assert.True(view.IsManualCamera);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
    }

    [StaFact]
    public void The_pill_hides_again_when_the_run_ends_while_the_camera_is_still_manual()
    {
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(Anchor, 120);
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Succeeded)); // cephe boşaldı

        Assert.True(view.IsManualCamera); // kullanıcı hâlâ gezinmede...
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility); // ...ama dönülecek yer kalmadı
    }

    [StaFact]
    public void The_pill_carries_the_shared_copy_and_the_uia_name()
    {
        var view = CinemaView(out _);

        Assert.Equal(BuildOrchestrator.App.ViewModels.InteractionText.GraphFollowPaused, view.FollowPillText);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.GraphFollowPill,
            AutomationProperties.GetName(view.FollowPillElement));
    }

    /// <summary>Realize testinde token sözlüğüne enjekte edilen yarıçap — hiçbir <c>Radius.*</c> token'ının
    /// değeri DEĞİLDİR, yani "bağ canlı mı" sorusunu ham bir kopyadan ayırt eder.</summary>
    private const double SwappedRadius = 17.0;

    [StaFact]
    public void The_pill_realises_in_a_real_window_with_its_tokens_resolved()
    {
        // Headless süit XAML'i InitializeComponent ile çözer, ama DynamicResource bağları ancak GERÇEK bir
        // kaynak kapsamında TALEP edilince çözülür ve WPF okuma yolunda TİP doğrulaması YAPMAZ (c6e9a21 sınıfı
        // hata sessizce geçerdi). Pil bu yüzden üretimin merge zinciriyle bir pencerede realize edilir ve
        // başlıkta gerçekten YER KAPLADIĞI ölçülür.
        var view = GraphTestView.New(labelFontFamily: DsResources.MonoFontFamily);
        // Pilin ailesi (AppFonts.Ui) bir pack:// URI'sidir ve Application olmadan çözülmez ⇒ TrackedTextBlock
        // ölçümü 0 döner. Metnin yerleşime GERÇEKTEN katıldığını görebilmek için file:// tabanlı aile enjekte
        // edilir (GraphView.LabelFontFamily seam'iyle aynı gerekçe).
        view.FollowPillLabel.FontFamily = DsResources.MonoFontFamily;
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, view, width: 600, height: 400);

        var nodes = GraphCinemaTests.BigNodes();
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(Anchor, 120);
        view.UpdateLayout();

        var pill = view.FollowPillElement;
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);
        Assert.True(view.FollowPillLabel.ActualWidth > 0, "pil metni hiç ölçülmedi (glyph yolu kurulmadı)");
        Assert.True(pill.ActualWidth > view.FollowPillLabel.ActualWidth,
            $"pil metnini sarmıyor ({pill.ActualWidth} ≤ {view.FollowPillLabel.ActualWidth})");
        Assert.True(pill.ActualHeight > 0);
        // Token bağları GERÇEKTEN çözüldü: fırçalarda örnek BİREBİR sözlüktekidir (ham bir hex ayrı bir
        // SolidColorBrush üretir ve Assert.Same düşer).
        Assert.Same(view.FindResource("Brush.SurfaceRaised"), pill.Background);
        Assert.Same(view.FindResource("Brush.Border"), pill.BorderBrush);
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(pill));

        // CornerRadius bir DEĞER TİPİDİR: Assert.Same yok, dolayısıyla `CornerRadius="4"` yazan bir mutant
        // token'la aynı sayıyı verir ve sessizce hayatta kalırdı (ölçüldü). Ayırt edici olan bağın CANLI
        // olmasıdır — token değişince pil de değişmelidir (§14.5: DynamicResource, StaticResource DEĞİL).
        Assert.Equal((CornerRadius)view.FindResource("Radius.Sm"), pill.CornerRadius);
        view.Resources["Radius.Sm"] = new CornerRadius(SwappedRadius);
        view.UpdateLayout();
        Assert.Equal(new CornerRadius(SwappedRadius), pill.CornerRadius);
        GC.KeepAlive(window);
    }
}
