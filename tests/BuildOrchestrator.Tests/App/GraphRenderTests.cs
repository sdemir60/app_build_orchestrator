using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet] <see cref="GraphView"/>'ın WPF KABLAJI — design v1.3.0 §2.3. Saf aritmetik
/// <see cref="QuietGraphLayout"/>/<see cref="GraphCamera"/>'da (ayrı testler); düğüm GÖRSELİNİN kendisi
/// <see cref="QuietGraphNodeTests"/>'te. Burada motion (açılış dalgası + hero + building animasyonu), seçim
/// sönmesi, kamera kablajı, panel başlığı ve boş durum yaşar.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphRenderTests
{
    // 3 katman: Base → Data.Core → (Server.Api, Web.Portal).
    private static IReadOnlyList<GraphNode> Nodes(
        GraphStatus baseStatus = GraphStatus.Discovered,
        GraphStatus dataStatus = GraphStatus.Discovered,
        GraphStatus apiStatus = GraphStatus.Discovered,
        GraphStatus portalStatus = GraphStatus.Discovered) =>
    [
        new("OSYS.Base", 0, baseStatus),
        new("OSYS.Data.Core", 1, dataStatus),
        new("OSYS.Server.Api", 2, apiStatus),
        new("OSYS.Web.Portal", 2, portalStatus),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
    [
        new("OSYS.Base", "OSYS.Data.Core"),
        new("OSYS.Data.Core", "OSYS.Server.Api"),
        new("OSYS.Data.Core", "OSYS.Web.Portal"),
    ];

    // [A13/T1 fix-1 · S1] Sözlük merge'i GraphTestView'da (TEK yer).
    private static GraphView NewView(
        bool animationsEnabled, double width = 600, double height = 400, IMotionSettings? motion = null)
        => GraphTestView.Sized(
            new Size(width, height),
            () => motion?.AnimationsEnabled ?? animationsEnabled,
            motion);

    // ---------------------------------------------------------------- ilk açılış dalgası

    [Fact]
    public void The_layer_stagger_is_55ms_per_layer_capped_at_330ms()
    {
        Assert.Equal(0.0, GraphView.RevealDelayMs(0));
        Assert.Equal(55.0, GraphView.RevealDelayMs(1));
        Assert.Equal(275.0, GraphView.RevealDelayMs(5));
        Assert.Equal(330.0, GraphView.RevealDelayMs(6));
        Assert.Equal(330.0, GraphView.RevealDelayMs(20)); // tavan
    }

    [StaFact]
    public void Nodes_start_fully_transparent_so_the_staggered_reveal_never_flashes()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(), Edges());

        // CSS `both` fill paritesi (feasibility §3.4): gecikme boyunca opaklık 0 tutulur — flash YOK.
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(0.0, v.Cell.Opacity));
        // [A13/T3c · c10] BuildApp.jsx:29 `translateY(-5px)` — yalnız NotNull YETMEZDİ (50px olsa da geçerdi).
        var rise = Assert.IsType<TranslateTransform>(view.NodeVisuals["OSYS.Base"].Cell.RenderTransform);
        Assert.Equal(-5.0, rise.Y);
    }

    [StaFact]
    public void Reduced_motion_places_the_nodes_instantly_with_no_stagger()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Cell.Opacity));
    }

    // ---------------------------------------------------------------- [E3/T41/DD9] reveal = hero

    [StaFact]
    public void The_reveal_stagger_takes_the_sync_reveal_hero_while_it_plays()
    {
        var coordinator = new MotionCoordinator();
        var view = NewView(true);
        view.HeroCoordinator = coordinator;

        view.SetGraph(Nodes(), Edges());

        // Reveal animate modunda hero'ya girdi ve reveal PENCERESİ boyunca TUTUYOR — release henüz tetiklenmedi
        // (canlı bir DispatcherTimer'a bağlı; senkron bu noktada tick etmemiştir).
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(0.0, v.Cell.Opacity)); // dalga oynuyor
    }

    [StaFact]
    public void The_reveal_releases_the_sync_reveal_hero_when_it_completes()
    {
        var coordinator = new MotionCoordinator();
        var view = NewView(true);
        view.HeroCoordinator = coordinator;

        view.SetGraph(Nodes(), Edges());
        Assert.True(coordinator.IsHeroActive);
        // (1) CANLI bir release ZAMANLANDI — ölü Completed-after-BeginAnimation yolu DEĞİL.
        Assert.True(view.HasPendingRevealRelease);

        // (2) Reveal tamamlandığında release tetiklenir (gerçek tick beklemeden, mevcut kuşak damgasıyla).
        view.ReleaseRevealHeroIfCurrent(view.RevealGeneration);

        Assert.False(coordinator.IsHeroActive);
        Assert.False(view.HasPendingRevealRelease);
    }

    [StaFact]
    public void A_stale_reveal_completion_does_not_release_the_current_reveal_hero()
    {
        var coordinator = new MotionCoordinator();
        var view = NewView(true);
        view.HeroCoordinator = coordinator;

        view.SetGraph(Nodes(), Edges());          // reveal kuşağı #1
        int gen1 = view.RevealGeneration;

        view.SetGraph(Nodes(), Edges());          // hızlı ikinci SetGraph → #1'i bırakır ve #2'yi yeniden alır
        Assert.NotEqual(gen1, view.RevealGeneration);
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);

        // #1'in gecikmiş (stale) release'i ateşlense bile #2'nin TAZE hero'suna dokunmaz.
        view.ReleaseRevealHeroIfCurrent(gen1);

        Assert.True(coordinator.IsHeroActive);
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);
    }

    [StaFact]
    public void A_reveal_yields_to_an_already_running_hero_and_places_the_nodes_instantly()
    {
        var coordinator = new MotionCoordinator();
        Assert.True(coordinator.TryBeginHero("frontier")); // DD9: başka bir hero zaten oynuyor
        var view = NewView(true);
        view.HeroCoordinator = coordinator;

        view.SetGraph(Nodes(), Edges());

        // Reveal reddedildi → düğümler ANİ yerleşir (Opacity 1, dalga yok); aktif hero DEĞİŞMEDİ.
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Cell.Opacity));
        Assert.Equal("frontier", coordinator.CurrentHeroKey);
    }

    [StaFact]
    public void Re_SetGraph_releases_the_previous_reveal_hero_before_taking_it_again()
    {
        var coordinator = new MotionCoordinator();
        var view = NewView(true);
        view.HeroCoordinator = coordinator;
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);

        // İkinci SetGraph önceki reveal hero'sunu bırakıp yeniden alır — ref-count sızmaz.
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); // unload reveal hero'sunu bırakmalı
        Assert.False(coordinator.IsHeroActive);
    }

    // ---------------------------------------------------------------- beads (§2.3 building animasyonu)

    /// <summary>
    /// Yörünge TALEP ÜZERİNE kurulur ve TÜM yörüngeler TEK paylaşımlı saate bağlanır.
    ///
    /// <para><b>Eski iddia:</b> <c>A_building_node_breathes_1_to_half_and_back_over_1_6s_at_30fps</c> —
    /// building düğüm DS <c>ds-node-pulse</c> ile 1.6s'de nefes alıyordu. v1.3.0 §2.3 nabzı kaldırdı ve
    /// yerine düğümün 2.8px dışında dolanan amber noktaları koydu. 30fps tavanı KORUNDU.</para>
    /// </summary>
    [StaFact]
    public void Every_beads_orbit_hangs_off_ONE_shared_clock_no_matter_how_many_nodes_build()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building, apiStatus: GraphStatus.Building), Edges());

        Assert.NotNull(view.NodeVisuals["OSYS.Data.Core"].Beads);
        Assert.NotNull(view.NodeVisuals["OSYS.Server.Api"].Beads);
        Assert.Null(view.NodeVisuals["OSYS.Base"].Beads); // building olmayan düğüm yörünge kurmaz

        var clock = view.BeadsClock;
        Assert.NotNull(clock);
        var spin = Assert.IsType<DoubleAnimation>(clock.Timeline);
        Assert.Equal(0.0, spin.From);
        Assert.Equal(-view.BeadsGeometry.Perimeter, spin.To!.Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphBeads.CycleMs), spin.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, spin.RepeatBehavior);
        Assert.Equal(GraphView.DecorativeFrameRate, Timeline.GetDesiredFrameRate(spin));
    }

    /// <summary>§2.3: "Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup
    /// kaybolmaz." Son building düğüm bittiğinde saat ANINDA bırakılmaz.</summary>
    [StaFact]
    public void The_shared_clock_keeps_spinning_after_the_last_node_stops_building_and_is_released_later()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.BeadsClock);

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Succeeded));
        Assert.NotNull(view.BeadsClock); // hâlâ dönüyor — noktalar sönerken de dönmeli

        view.HandleBeadsSpindownTick();
        Assert.Null(view.BeadsClock);
    }

    /// <summary>Spin-down penceresi içinde YENİ bir cephe doğarsa saat KORUNUR.</summary>
    [StaFact]
    public void A_new_build_inside_the_spindown_window_keeps_the_clock_alive()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Succeeded));

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Succeeded, apiStatus: GraphStatus.Building));
        view.HandleBeadsSpindownTick(); // gecikmiş tetik ateşlense bile

        Assert.NotNull(view.BeadsClock);
    }

    /// <summary>Panel yeniden boyutlanınca düğüm boyutu → yörünge ÇEVRESİ değişir ⇒ desen ve saat yeniden
    /// kurulur; aksi halde noktalar yeni çevreye tam bölünmez ve ek yerinde bindirirdi.</summary>
    [StaFact]
    public void Resizing_the_panel_rebuilds_the_pattern_because_the_orbit_perimeter_changed()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        double before = view.BeadsGeometry.Perimeter;

        GraphTestView.Resize(view, new Size(300, 200));
        view.UpdateLayout();

        Assert.NotEqual(before, view.BeadsGeometry.Perimeter);
        var orbit = view.NodeVisuals["OSYS.Data.Core"].Beads!;
        Assert.Equal(view.BeadsGeometry.Side, orbit.Width, 6);
        Assert.Equal(GraphBeads.DashArrayFor(view.BeadsGeometry), orbit.StrokeDashArray);
        Assert.NotNull(view.BeadsClock); // hâlâ derleniyor → saat yeni çevreyle geri kuruldu
        Assert.Equal(-view.BeadsGeometry.Perimeter, ((DoubleAnimation)view.BeadsClock!.Timeline).To!.Value, 6);
    }

    /// <summary>[M-d] Yeni topoloji eski görselleri atar — paylaşımlı saat onlarla birlikte bırakılır, aksi
    /// halde timing engine 30fps'te uyanık kalırdı.</summary>
    [StaFact]
    public void Re_SetGraph_releases_the_shared_beads_clock()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.BeadsClock);

        view.SetGraph(Nodes(), Edges()); // building yok

        Assert.Null(view.BeadsClock);
    }

    [StaFact]
    public void Unloading_the_view_releases_a_running_beads_clock()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.BeadsClock);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        Assert.Null(view.BeadsClock);
    }

    /// <summary>§2.3 son madde: <c>prefers-reduced-motion</c>'da beads TAMAMEN kapalı — yörünge hiç kurulmaz.</summary>
    [StaFact]
    public void Reduced_motion_builds_no_beads_at_all()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());

        Assert.Null(view.NodeVisuals["OSYS.Data.Core"].Beads);
        Assert.Null(view.BeadsClock);
    }

    /// <summary>[M-2] Canlı reduced-motion. <b>Eski iddia:</b> bu test akan kenar dash saatinin
    /// (<c>SharedDashClock</c>) ve building NABZININ durduğunu pinliyordu — kalıcı kenar ağı kalktı, nabzın
    /// yerini beads aldı.</summary>
    [StaFact]
    public void Flipping_the_motion_signal_at_runtime_stops_the_building_animation_immediately()
    {
        var motion = new FakeMotionSettings { AnimationsEnabled = true };
        var view = NewView(true, motion: motion);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // aboneliği kur
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.BeadsClock);

        motion.Flip(false); // OS reduced-motion'a geçti — bir sonraki UpdateStatuses BEKLENMEZ

        Assert.Null(view.BeadsClock);
        Assert.False(view.NodeVisuals["OSYS.Data.Core"].BeadsVisible);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        motion.Flip(true); // abonelik bırakıldı → view artık tepki vermez
        Assert.Null(view.BeadsClock);
    }

    private sealed class FakeMotionSettings : IMotionSettings
    {
        public bool AnimationsEnabled { get; set; }
        public event EventHandler? AnimationsEnabledChanged;
        public TimeSpan Effective(TimeSpan token) => AnimationsEnabled ? token : TimeSpan.Zero;
        public void Flip(bool enabled)
        {
            AnimationsEnabled = enabled;
            AnimationsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---------------------------------------------------------------- seçim sönmesi

    /// <summary>
    /// §2.3 "Seçim": odak kümesi (seçili + doğrudan deps + doğrudan dependents) tam opak, geri kalan HER ŞEY
    /// opacity <b>0.1</b>.
    ///
    /// <para><b>Eski iddia:</b> komşu olmayan düğümler <b>0.25</b>'e sönüyordu ve test ayrıca kenar
    /// opaklıklarını (dokunulmayan kenar 0.16, seçime değen 1.6px) pinliyordu. v1.3.0 §2.3 hem sönme
    /// seviyesini düşürdü hem kalıcı kenar ağını kaldırdı.</para>
    /// </summary>
    [StaFact]
    public void Selection_dims_every_node_outside_the_focus_set_to_ten_percent()
    {
        var view = NewView(false); // reduced-motion: sönme ANINDA uygulanır (deterministik)
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Server.Api";

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Server.Api"].Body.Opacity); // seçili
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data.Core"].Body.Opacity);  // doğrudan bağımlılık
        Assert.Equal(GraphView.UnfocusedNodeOpacity, view.NodeVisuals["OSYS.Base"].Body.Opacity);
        Assert.Equal(GraphView.UnfocusedNodeOpacity, view.NodeVisuals["OSYS.Web.Portal"].Body.Opacity);
        Assert.Equal(0.1, GraphView.UnfocusedNodeOpacity, 6); // otorite sayısı (§2.3)
    }

    [StaFact]
    public void Clearing_the_selection_restores_every_node()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Server.Api";

        view.SelectedNode = null;

        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Body.Opacity));
    }

    // ---------------------------------------------------------------- panel başlığı + boş durum

    [StaFact]
    public void The_panel_header_counts_projects_and_dependencies_from_the_data()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        Assert.Equal("4 projects · 3 dependencies", view.HeaderCountsText);
        // [I-2] design-v1 §1.2: sayaç MAKİNE ÇIKTISIDIR → gömülü Geist Mono (sistem Consolas'ı tasarımın parçası
        // değildir). Kardeş chrome da aynı TEK aileyi (AppFonts.Mono) kullanır — pack URI hiçbir yerde kopyalanmaz.
        Assert.Same(AppFonts.Mono, view.HeaderCountsFontFamily);
        Assert.Equal("./#Geist Mono", AppFonts.Mono.Source);
    }

    /// <summary>[A13/T3c · c3 → fix-1 · C3] README §2.3: "Panel başlığı (28px, surface, alt border-subtle)" —
    /// GraphView bu başlığı <see cref="PanelHeader"/> KULLANMADAN kendi Border'ıyla çizer (GraphView.xaml).
    ///
    /// <para>İddia <b>canlı bağ</b> seviyesindedir: token'ın DEĞERİ değişince başlık GERÇEKTEN onu izler
    /// (çıplak bir <c>Height="28"</c> literali bu testi düşürür).</para>
    ///
    /// <para><b>Not (A13.2 kural 6):</b> mutasyon paylaşılan sözlüğe DOKUNMAZ — override, atılacak görünümün
    /// KENDİ <see cref="FrameworkElement.Resources"/>'ına konur ve testin sonunda geri alınır.</para></summary>
    [StaFact]
    public void The_panel_header_is_28px_over_surface_with_a_border_subtle_bottom_line()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var header = Assert.IsType<Border>(((DockPanel)view.Content).Children[0]);
        Assert.Equal((double)view.FindResource("Size.PanelHeaderHeight"), header.Height);
        Assert.Equal(28.0, header.Height); // otorite literali (README §2.3)
        Assert.Same(view.FindResource("Brush.Surface"), header.Background);
        Assert.Same(view.FindResource("Brush.BorderSubtle"), header.BorderBrush);
        Assert.Equal(new Thickness(0, 0, 0, 1), header.BorderThickness);

        // CANLI BAĞ: token'ı görünümün kendi kapsamında ez → başlık izler.
        view.Resources["Size.PanelHeaderHeight"] = 50.0;
        Assert.Equal(50.0, header.Height);
        view.Resources.Remove("Size.PanelHeaderHeight");
        Assert.Equal(28.0, header.Height); // override kalktı — üretim değeri geri geldi
    }

    [StaFact]
    public void Before_sync_the_ground_shows_the_dashed_empty_state_box()
    {
        var view = NewView(false);

        Assert.True(view.IsEmptyStateVisible);
        Assert.Equal("Graph appears after Sync", view.EmptyStateText);

        view.SetGraph(Nodes(), Edges());
        Assert.False(view.IsEmptyStateVisible);
    }

    // ---------------------------------------------------------------- kamera

    /// <summary>
    /// Kamera bir <c>ScaleTransform</c> + <c>TranslateTransform</c> grubudur ve SEÇİMDE odak kümesini
    /// sığdırır.
    ///
    /// <para><b>Eski iddia:</b> hedef <c>GraphCamera.Compute(viewport, graphSize, nodeCenter)</c> idi — yani
    /// yalnız seçili düğümün MERKEZİ, grafı panele sığdıran bir ölçekle. §2.3 bunu "odakla & sığdır" yaptı:
    /// seçili düğüm + doğrudan komşularının sınır kutusu panele sığdırılır (zoom da hedefin parçasıdır).</para>
    /// </summary>
    [StaFact]
    public void The_camera_uses_a_scale_plus_translate_transform_group_and_fits_the_focus_set()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var group = Assert.IsType<TransformGroup>(view.World.RenderTransform);
        Assert.IsType<ScaleTransform>(group.Children[0]);   // CSS `translate(...) scale(...)` = önce ölçek
        Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal(new Point(0, 0), view.World.RenderTransformOrigin);
        Assert.Equal(GraphCamera.Default, view.CurrentCamera); // seçim yok → varsayılan görünüm

        view.SelectedNode = "OSYS.Web.Portal";

        // Odak kümesi = Web.Portal + Data.Core (tek doğrudan bağımlılığı).
        var portal = view.NodeCenter("OSYS.Web.Portal");
        var data = view.NodeCenter("OSYS.Data.Core");
        var bounds = new Rect(
            Math.Min(portal.X, data.X), Math.Min(portal.Y, data.Y),
            Math.Abs(portal.X - data.X), Math.Abs(portal.Y - data.Y));
        var expected = GraphCamera.FocusAndFit(
            view.ViewportSize, bounds, view.NodeSize,
            new Vector(QuietGraphLayout.ContentInset, QuietGraphLayout.ContentInset));
        Assert.Equal(expected, view.CurrentCamera);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_camera_with_no_animation()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.False(view.LastCameraAnimated);
        var group = (TransformGroup)view.World.RenderTransform;
        Assert.Equal(view.CurrentCamera.Scale, ((ScaleTransform)group.Children[0]).ScaleX);
        Assert.Equal(view.CurrentCamera.Tx, ((TranslateTransform)group.Children[1]).X);
    }

    [StaFact]
    public void With_motion_enabled_the_camera_animates_over_460ms()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.True(view.LastCameraAnimated);
        Assert.Equal(460.0, GraphCamera.TransitionMs);
    }

    [StaFact]
    public void An_unchanged_camera_target_does_not_restart_the_460ms_transition()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Web.Portal";
        Assert.True(view.LastCameraAnimated);
        var applied = view.CurrentCamera;

        // Statü güncellemesi kamerayı DEĞİŞTİRMEZ (§2.3: koşu sırasında kamera durur) — uçuştaki geçiş
        // yeniden doğmamalı.
        view.UpdateStatuses(Nodes(apiStatus: GraphStatus.Building));

        Assert.Equal(applied, view.CurrentCamera);
    }

    /// <summary>
    /// AYIRT EDİCİ — §2.3: kamera koşu sırasında DURUR. Building bir düğüm doğması kamerayı oynatmaz.
    ///
    /// <para><b>Eski iddia:</b> <c>Only_a_FRONTIER_focus_is_remembered…</c> tam tersini pinliyordu: kamera
    /// building frontier'in ağırlık merkezine yöneliyor ve o odağı 8px'lik bir Zeno eşiğiyle hatırlıyordu.
    /// v1.3.0 frontier takibini tamamen kaldırdı; kameranın tek otomatik hedefi seçimdir.</para>
    /// </summary>
    [StaFact]
    public void A_build_that_starts_never_moves_the_camera_because_only_a_selection_targets_it()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(GraphCamera.Default, view.CurrentCamera);

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Building));
        Assert.Equal(GraphCamera.Default, view.CurrentCamera);

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Succeeded, apiStatus: GraphStatus.Building));
        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
    }
}
