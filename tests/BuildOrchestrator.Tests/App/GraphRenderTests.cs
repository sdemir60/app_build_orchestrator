using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T63] <see cref="GraphView"/> Shapes yolu (≤~150 düğüm) — design-v1 §2.3 düğüm/kenar/seçim/rozet/stagger
/// render'ı. Saf aritmetik <see cref="GraphLayout"/>/<see cref="GraphCamera"/>/<see cref="EdgeStyleResolver"/>'da
/// (ayrı testler); burada YALNIZ WPF kablajı doğrulanır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphRenderTests
{
    // 3 katman: Base → Data.Core → (Server.Api, Web.Portal). Bütün dalları (building/failed/depIssue) sergiler.
    private static IReadOnlyList<GraphNode> Nodes(
        GraphStatus baseStatus = GraphStatus.Discovered,
        GraphStatus dataStatus = GraphStatus.Discovered,
        GraphStatus apiStatus = GraphStatus.Discovered,
        GraphStatus portalStatus = GraphStatus.Discovered,
        bool portalDepIssue = false) =>
    [
        // [D5] Prefix "OSYS." — GraphNode.ShortName artık veri-türevli öneki taşır (GraphBinder üretir); bu
        // izole render testinde önek elle verilir ki etiket kısa adı ("Base"/"Server.Api") göstersin.
        new("OSYS.Base", 0, baseStatus, Prefix: "OSYS."),
        new("OSYS.Data.Core", 1, dataStatus, Prefix: "OSYS."),
        new("OSYS.Server.Api", 2, apiStatus, Prefix: "OSYS."),
        new("OSYS.Web.Portal", 2, portalStatus, HasDepIssue: portalDepIssue, Prefix: "OSYS."),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
    [
        new("OSYS.Base", "OSYS.Data.Core"),
        new("OSYS.Data.Core", "OSYS.Server.Api"),
        new("OSYS.Data.Core", "OSYS.Web.Portal"),
    ];

    private static GraphView NewView(
        bool animationsEnabled, double width = 600, double height = 400, IMotionSettings? motion = null)
    {
        var view = new GraphView
        {
            MotionSettings = motion,
            AnimationsEnabledProvider = () => motion?.AnimationsEnabled ?? animationsEnabled,
        };
        // pack:// / Application.Resources olmadan (headless host) token'lar çözülmez — Tokens/Motion sözlükleri
        // dosyadan merge edilir (FontAssetTests/TokenBrushesTests ile AYNI TestAssets deseni). Böylece
        // SetResourceReference ile bağlanan fırçalar ve Duration/KeySpline token'ları gerçekten çözülür.
        // [T64 review · fix wave 1] Icons.xaml de merge edilir: düğüm ikonu ve dep-hata üçgeni artık kodda
        // gömülü path DEĞİL, bu sözlükten çözülen geometrilerdir (CopyLogTests.NewHeaderWithIcons ile aynı desen).
        foreach (string name in new[] { "Tokens.xaml", "Motion.xaml", "Icons.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        return view;
    }

    // ---------------------------------------------------------------- düğüm (26px, 4px radius KARE)

    [StaFact]
    public void A_node_is_a_26px_square_with_a_4px_corner_radius()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var square = view.NodeVisuals["OSYS.Base"].Square;
        Assert.Equal(26.0, square.Width);
        Assert.Equal(26.0, square.Height);
        Assert.Equal(4.0, square.RadiusX);
        Assert.Equal(4.0, square.RadiusY);
    }

    [StaFact]
    public void A_discovered_node_gets_a_dashed_frame_wpf_border_cannot_dash_so_it_is_a_rectangle()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(baseStatus: GraphStatus.Discovered, dataStatus: GraphStatus.Succeeded), Edges());

        Assert.NotEmpty(view.NodeVisuals["OSYS.Base"].Square.StrokeDashArray);
        Assert.Empty(view.NodeVisuals["OSYS.Data.Core"].Square.StrokeDashArray);
    }

    [StaFact]
    public void The_node_label_is_the_short_name_in_10px_mono_with_a_LOCAL_Ideal_formatting_mode()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var label = view.NodeVisuals["OSYS.Server.Api"].Label;
        Assert.Equal("Server.Api", label.Text);
        Assert.Equal(10.0, label.FontSize);
        // feasibility §3.4/§4.4: Display, scale transform altında BOZULUR — graf etiketlerinde LOKAL Ideal override
        // (kök MainWindow Display'i DEĞİŞMEZ, T65).
        Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(label));
        // DS: etiket text-dim, seçiliyken text-primary (varsayılan siyah Foreground'u miras almaz).
        Assert.Equal(view.TryFindResource("Brush.TextDim"), label.Foreground);
        view.SelectedNode = "OSYS.Server.Api";
        Assert.Equal(view.TryFindResource("Brush.TextPrimary"), label.Foreground);
    }

    [StaFact]
    public void Selecting_a_node_shows_its_amber_ring_and_thickens_the_square_border_to_2px()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Base"].SelectionRing.Visibility);
        Assert.Equal(1.5, view.NodeVisuals["OSYS.Base"].Square.StrokeThickness);

        view.SelectedNode = "OSYS.Base";

        Assert.Equal(Visibility.Visible, view.NodeVisuals["OSYS.Base"].SelectionRing.Visibility);
        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Data.Core"].SelectionRing.Visibility);
        // [M-1] DS DependencyGraphNode: `border: ${selected ? 2 : 1.5}px …`
        Assert.Equal(2.0, view.NodeVisuals["OSYS.Base"].Square.StrokeThickness);
        Assert.Equal(1.5, view.NodeVisuals["OSYS.Data.Core"].Square.StrokeThickness);

        view.SelectedNode = null;
        Assert.Equal(1.5, view.NodeVisuals["OSYS.Base"].Square.StrokeThickness); // seçim kalkınca geri döner
    }

    // ---------------------------------------------------------------- dep-hata rozeti

    [StaFact]
    public void A_dep_issue_node_gets_a_13px_circle_badge_holding_a_filled_red_triangle()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(portalDepIssue: true), Edges());

        var withBadge = view.NodeVisuals["OSYS.Web.Portal"];
        Assert.Equal(Visibility.Visible, withBadge.Badge.Visibility);
        Assert.Equal(13.0, withBadge.Badge.Width);
        Assert.Equal(13.0, withBadge.Badge.Height);
        // 13px daire: zemin surface-base, 1px kırmızı border
        Assert.Equal(view.TryFindResource("Brush.SurfaceBase"), withBadge.BadgeCircle.Fill);
        Assert.Equal(view.TryFindResource("Brush.StatusFailBorder"), withBadge.BadgeCircle.Stroke);
        Assert.Equal(1.0, withBadge.BadgeCircle.StrokeThickness);
        // İçinde DOLU kırmızı üçgen ▲ (stroke YOK — dolu)
        Assert.Equal(view.TryFindResource("Brush.StatusFailText"), withBadge.BadgeTriangle.Fill);
        Assert.Null(withBadge.BadgeTriangle.Stroke);
        Assert.False(withBadge.BadgeTriangle.Data.IsEmpty());

        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Server.Api"].Badge.Visibility);
    }

    [StaFact]
    public void The_node_icon_and_the_dep_badge_are_the_dictionary_geometries_not_copies_parsed_in_code()
    {
        // [T64 review · fix wave 1] Bu iki geometri GraphView'de inline `Geometry.Parse` sabitiydi ve
        // Icons.xaml'deki metinle KARAKTER KARAKTER aynıydı — biri düzeltilince öteki sessizce eski şekli
        // çizmeye devam ederdi ve hiçbir test bunu görmezdi. REFERANS eşitliği tek doğruluk kaynağını pinler:
        // kodda yeniden parse edilen bir kopya AYRI bir nesne olacağından bu iddia kırılır ("aynı metin" değil,
        // "aynı NESNE"). Geometriler donmuş ve paylaşımlıdır (Icons.xaml başlık yorumu) — paylaşım doğrudur.
        var view = NewView(false);
        view.SetGraph(Nodes(portalDepIssue: true), Edges());

        var visual = view.NodeVisuals["OSYS.Web.Portal"];
        // [B2→D5 fold] Aynı anahtar iki tarafta da çözülemeseydi ikisi de null olur ve Assert.Same(null, null)
        // BOŞUNA geçerdi (sahte pass) — önce geometrilerin GERÇEKTEN çözüldüğünü pinle, sonra referans eşitliğini.
        Assert.NotNull(visual.Icon.Data);
        Assert.NotNull(visual.BadgeTriangle.Data);
        Assert.Same(view.TryFindResource(GraphView.PackageIconKey), visual.Icon.Data);
        Assert.Same(view.TryFindResource(GraphView.WarningTriangleIconKey), visual.BadgeTriangle.Data);
    }

    [StaFact]
    public void Node_and_edge_colours_are_resolved_from_the_foundation_token_brushes_not_hardcoded_hex()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(baseStatus: GraphStatus.Failed, dataStatus: GraphStatus.Building), Edges());

        var failed = view.NodeVisuals["OSYS.Base"];
        Assert.Equal(view.TryFindResource("Brush.StatusFail"), failed.Square.Stroke);
        Assert.Equal(view.TryFindResource("Brush.StatusFailSoft"), failed.Square.Fill);
        Assert.Equal(view.TryFindResource("Brush.StatusFailText"), failed.Icon.Stroke);

        // Base failed ⇒ Base→Data.Core hata dalıdır; hedefi building olduğu için AKAR ama kırmızı çizilir.
        var edge = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(view.TryFindResource("Brush.StatusFailBorder"), edge.Path.Stroke);
        Assert.True(edge.Style!.IsFlowing);
    }

    // ---------------------------------------------------------------- ilk açılış: katman stagger'ı

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
        Assert.NotNull(view.NodeVisuals["OSYS.Base"].Cell.RenderTransform); // 5px yukarıdan gelir
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
        // (canlı bir DispatcherTimer'a bağlı; senkron bu noktada tick etmemiştir). Bkz.
        // The_reveal_releases_the_sync_reveal_hero_when_it_completes: release GERÇEKTEN çalışır.
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(0.0, v.Cell.Opacity)); // stagger oynuyor (gecikme boyunca 0)
    }

    [StaFact]
    public void The_reveal_releases_the_sync_reveal_hero_when_it_completes()
    {
        var coordinator = new MotionCoordinator();
        var view = NewView(true);
        view.HeroCoordinator = coordinator;

        view.SetGraph(Nodes(), Edges());
        Assert.True(coordinator.IsHeroActive);
        // (1) CANLI bir release ZAMANLANDI — ölü Completed-after-BeginAnimation yolu DEĞİL. (Fix'in özü: eski kod
        // burada hiçbir tetik kurmuyordu; hero bir sonraki SetGraph/Unloaded'a kadar sonsuza dek tutuluyordu.)
        Assert.True(view.HasPendingRevealRelease);

        // (2) Reveal tamamlandığında release tetiklenir (gerçek tick beklemeden, mevcut kuşak damgasıyla) → hero BIRAKILIR.
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

        // #1'in gecikmiş (stale) release'i ateşlense bile #2'nin TAZE hero'suna dokunmaz (gen1 != mevcut kuşak).
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

        // Reveal reddedildi → düğümler ANİ yerleşir (Opacity 1, stagger yok); aktif hero DEĞİŞMEDİ.
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

        // İkinci SetGraph önceki reveal hero'sunu bırakıp yeniden alır — ref-count sızmaz (hero tek girişte kalır).
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(GraphView.RevealHeroKey, coordinator.CurrentHeroKey);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); // unload reveal hero'sunu bırakmalı
        Assert.False(coordinator.IsHeroActive);
    }

    // ---------------------------------------------------------------- akan dash — TEK paylaşımlı clock

    [StaFact]
    public void Flowing_edges_are_UIElement_paths_bound_to_one_single_shared_dash_clock()
    {
        var view = NewView(true);
        // [M-a] Aşağıdaki "clock local değerin üstüne biniyor mu" ispatı GERÇEK bir compositor tick'i ister —
        // AnimationHost'un belgelediği (T59, ScrollAnimatorTests'te de aynı desen) doğrulanmış kısıt: bir
        // PresentationSource'a (HWND) bağlı olmayan elemanda ApplyAnimationClock HİÇBİR gözlenebilir etki
        // üretmez (GetValue hep taban/local değeri döner, tick hiç olmaz).
        var window = AnimationHost.ShowOffscreen(view, width: 600, height: 400);
        // İki kenar birden akar: Data.Core→Server.Api ve Data.Core→Web.Portal (ikisi de building).
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building, portalStatus: GraphStatus.Building), Edges());

        Assert.Equal(2, view.FlowingEdgePaths.Count);
        Assert.All(view.FlowingEdgePaths, p => Assert.Equal([4.0, 7.0], p.StrokeDashArray));
        var clock = view.SharedDashClock;
        Assert.NotNull(clock);

        // [M-4] "clock var" yetmez — HER akan Path'in StrokeDashOffset'i GERÇEKTEN clock'a bağlı olmalı
        // (DrawingContext Pen.DashStyle.Offset güvenilmez olduğu için A13.2 bu kablajı şart koşar).
        // [M-a] İspat: local değeri GÖRÜNÜR bir şeye zorla (99) — GetAnimationBaseValue burada AYIRT EDİCİ
        // DEĞİLDİR (clock bağlı olsun olmasın hep son atanan local değeri döner, hiç atanmamışsa varsayılan 0 —
        // önceki assert bu yüzden tautolojikti). Asıl kanıt: clock GERÇEKTEN üstüne biniyorsa (tick'ten SONRA)
        // GetValue hâlâ 99 OLAMAZ — görünen offset'i ÜRETEN şey clock'tur, düz bir atama değil.
        Assert.All(view.FlowingEdgePaths, p =>
        {
            Assert.True(p.HasAnimatedProperties, "akan Path clock'a bağlı değil");
            DispatcherPump.PumpUntil(
                () => DependencyPropertyHelper.GetValueSource(p, ShapePath.StrokeDashOffsetProperty).IsAnimated,
                TimeSpan.FromSeconds(2));
            Assert.True(DependencyPropertyHelper.GetValueSource(p, ShapePath.StrokeDashOffsetProperty).IsAnimated,
                "compositor hiç tick etmedi — clock canlı değil");
            p.StrokeDashOffset = 99;
            Assert.NotEqual(99.0, (double)p.GetValue(ShapePath.StrokeDashOffsetProperty));
        });
        // Akmayan bir kenarda animasyon YOK — yukarıdaki bayrak akan kenarlara özgüdür.
        Assert.False(view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base").Path.HasAnimatedProperties);

        // Akan küme değişse bile clock AYNI nesnedir — tüm akan kenarlar tek clock'ta faz-kilitli kalır.
        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Building, apiStatus: GraphStatus.Building));
        Assert.Same(clock, view.SharedDashClock);
        Assert.Equal(2, view.FlowingEdgePaths.Count);
        GC.KeepAlive(window); // canlı tutulmalı — kapatmak şart değil (AnimationHost dokümantasyonu)
    }

    [StaFact]
    public void The_shared_dash_animation_loops_two_full_periods_at_30fps()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());

        // TEK kök clock (timing engine'de tek abonelik) + kalınlık başına iki dal — hepsi aynı kökten.
        var root = view.SharedDashClock;
        Assert.NotNull(root);
        Assert.Equal(2, root.Children.Count);
        Assert.Same(root, view.ThinDashClock!.Parent);
        Assert.Same(root, view.ThickDashClock!.Parent);
        // Dekoratif sonsuz animasyon → 30fps tavanı (feasibility §3.4). DesiredFrameRate yalnız KÖK'te geçerlidir.
        Assert.Equal(30, Timeline.GetDesiredFrameRate(root.Timeline));

        var thin = Assert.IsType<DoubleAnimation>(view.ThinDashClock.Timeline);
        var thick = Assert.IsType<DoubleAnimation>(view.ThickDashClock.Timeline);
        // 1px → -22 birim × 1px = 22px; 1.6px → -13.75 birim × 1.6px = 22px. AYNI mutlak yol, AYNI süre ⇒ faz kilidi.
        Assert.Equal(-22.0, thin.To);
        Assert.Equal(-13.75, thick.To);
        foreach (var a in new[] { thin, thick })
        {
            Assert.Equal(TimeSpan.FromMilliseconds(900), a.Duration.TimeSpan);
            Assert.Equal(RepeatBehavior.Forever, a.RepeatBehavior);
        }
    }

    [StaFact]
    public void A_selected_1_6px_flowing_edge_divides_its_dash_and_gets_the_thick_branch_of_the_same_clock()
    {
        var view = NewView(true);
        // Base failed ⇒ Base→Data.Core hata dalı; Data.Core building ⇒ AKAR. Base seçilince kenar 1.6px olur.
        view.SetGraph(Nodes(baseStatus: GraphStatus.Failed, dataStatus: GraphStatus.Building), Edges());
        view.SelectedNode = "OSYS.Base";

        var edge = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(1.6, edge.Path.StrokeThickness);
        // A13.2: 1.6px'te değerler BÖLÜNÜR ⇒ mutlakta yine 4px/7px (bölünmeseydi 6.4/11.2px olurdu).
        Assert.Equal([4.0 / 1.6, 7.0 / 1.6], edge.Path.StrokeDashArray);
        Assert.True(edge.Path.HasAnimatedProperties);
        // ... ve hâlâ TEK kök clock: kalın dal da aynı kökün çocuğudur.
        Assert.Same(view.SharedDashClock, view.ThickDashClock!.Parent);
    }

    [StaFact]
    public void A_selected_1_6px_static_error_edge_divides_its_dash_and_never_animates()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(baseStatus: GraphStatus.Failed), Edges()); // Data.Core building DEĞİL ⇒ statik hata dalı
        view.SelectedNode = "OSYS.Base";

        var edge = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(1.6, edge.Path.StrokeThickness);
        Assert.Equal([3.0 / 1.6, 4.0 / 1.6], edge.Path.StrokeDashArray); // mutlakta 3px/4px
        Assert.False(edge.Path.HasAnimatedProperties);
        Assert.Null(view.SharedDashClock); // akan kenar yok ⇒ clock hiç kurulmaz
    }

    [StaFact]
    public void The_shared_clock_is_released_when_the_last_flowing_edge_stops_and_rebuilt_on_demand()
    {
        // [M-3] Boşta 30fps'te uyanık bir timing engine bırakma.
        var view = NewView(true);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.SharedDashClock);

        view.UpdateStatuses(Nodes(apiStatus: GraphStatus.Succeeded)); // akan kenar kalmadı
        Assert.Empty(view.FlowingEdgePaths);
        Assert.Null(view.SharedDashClock);

        view.UpdateStatuses(Nodes(apiStatus: GraphStatus.Succeeded, portalStatus: GraphStatus.Building));
        Assert.NotNull(view.SharedDashClock); // talep üzerine yeniden kurulur
        Assert.All(view.FlowingEdgePaths, p => Assert.True(p.HasAnimatedProperties));
    }

    [StaFact]
    public void Unloading_the_view_releases_a_running_shared_dash_clock()
    {
        // [M-d] M-3 yalnız "son akan kenar durdu" yolunu kapatır — view TAMAMEN ağaçtan kalktığında da (henüz
        // akan kenar varken) clock bırakılmalı, aksi halde timing engine view'dan bağımsız 30fps'te uyanık kalır.
        var view = NewView(true);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.SharedDashClock);

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        Assert.Null(view.SharedDashClock);
    }

    [StaFact]
    public void Reduced_motion_keeps_the_dash_but_never_starts_a_clock()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());

        Assert.Null(view.SharedDashClock);
        var flowing = Assert.Single(view.FlowingEdgePaths);
        Assert.Equal([4.0, 7.0], flowing.StrokeDashArray); // statik kesikli
        Assert.Equal(0.0, flowing.StrokeDashOffset);
        Assert.False(flowing.HasAnimatedProperties);
    }

    // ---------------------------------------------------------------- building nabzı (DS ds-node-pulse)

    [StaFact]
    public void A_building_node_breathes_1_to_half_and_back_over_1_6s_at_30fps()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());

        var building = view.NodeVisuals["OSYS.Data.Core"];
        Assert.True(building.IsPulsing);
        // Cell (stagger) ve Body (seçim sönmesi) DOLU olduğundan nabzın ÜÇÜNCÜ bir opaklık taşıyıcısı olmalı.
        Assert.True(building.PulseHost.HasAnimatedProperties);
        Assert.NotSame(building.PulseHost, building.Cell);
        // Rozet nabız kabının DIŞINDADIR (DS'te kardeş eleman) — building düğümde de solmaz.
        Assert.DoesNotContain(building.Badge, building.PulseHost.Children.Cast<UIElement>());

        // Nabız dışındaki düğümler animasyonsuz ve tam opak.
        var idle = view.NodeVisuals["OSYS.Base"];
        Assert.False(idle.IsPulsing);
        Assert.False(idle.PulseHost.HasAnimatedProperties);
        Assert.Equal(1.0, idle.PulseHost.Opacity);
    }

    [StaFact]
    public void The_pulse_stops_when_the_node_leaves_building()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.True(view.NodeVisuals["OSYS.Data.Core"].IsPulsing);

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Succeeded));

        var settled = view.NodeVisuals["OSYS.Data.Core"];
        Assert.False(settled.IsPulsing);
        Assert.False(settled.PulseHost.HasAnimatedProperties);
        Assert.Equal(1.0, settled.PulseHost.Opacity);
    }

    [StaFact]
    public void Re_SetGraph_stops_the_pulse_on_the_discarded_old_visuals()
    {
        // [M-d] M-3'ün dash-clock sızıntısıyla AYNI sınıf: SetGraph eski görselleri _nodes'tan ATAR ama
        // GC'ye bırakılmadan önce sonsuz nabız animasyonu durdurulmazsa timing engine 30fps'te uyanık kalırdı.
        var view = NewView(true);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        var stale = view.NodeVisuals["OSYS.Data.Core"];
        Assert.True(stale.PulseHost.HasAnimatedProperties); // nabız gerçekten dönüyor

        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges()); // yeni topoloji → eski görsel ATILIR

        Assert.False(stale.PulseHost.HasAnimatedProperties, "atılan eski görselin nabzı hâlâ dönüyor — sızıntı");
        Assert.Equal(1.0, stale.PulseHost.Opacity);
        // Yeni görsel kendi nabzını normal şekilde kurar — StopPulse yalnız ESKİYİ durdurur, yeniyi etkilemez.
        var fresh = view.NodeVisuals["OSYS.Data.Core"];
        Assert.NotSame(stale, fresh);
        Assert.True(fresh.PulseHost.HasAnimatedProperties);
    }

    [StaFact]
    public void Reduced_motion_never_starts_the_building_pulse()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());

        var building = view.NodeVisuals["OSYS.Data.Core"];
        Assert.False(building.IsPulsing);
        Assert.False(building.PulseHost.HasAnimatedProperties);
        Assert.Equal(1.0, building.PulseHost.Opacity);
    }

    // ---------------------------------------------------------------- canlı reduced-motion (M-2)

    [StaFact]
    public void Flipping_the_motion_signal_at_runtime_stops_the_flow_and_the_pulse_immediately()
    {
        var motion = new FakeMotionSettings { AnimationsEnabled = true };
        var view = NewView(true, motion: motion);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); // aboneliği kur
        view.SetGraph(Nodes(dataStatus: GraphStatus.Building), Edges());
        Assert.NotNull(view.SharedDashClock);
        Assert.True(view.NodeVisuals["OSYS.Data.Core"].IsPulsing);

        motion.Flip(false); // OS reduced-motion'a geçti — bir sonraki UpdateStatuses BEKLENMEZ

        Assert.Null(view.SharedDashClock);
        Assert.False(view.NodeVisuals["OSYS.Data.Core"].IsPulsing);
        Assert.All(view.FlowingEdgePaths, p => Assert.False(p.HasAnimatedProperties));

        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        motion.Flip(true); // abonelik bırakıldı → view artık tepki vermez
        Assert.Null(view.SharedDashClock);
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

    [StaFact]
    public void Selection_dims_every_non_neighbour_node_to_25_percent_and_untouched_edges_to_16_percent()
    {
        var view = NewView(false); // reduced-motion: sönme ANINDA uygulanır (deterministik)
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Server.Api";

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Server.Api"].Body.Opacity); // seçili
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data.Core"].Body.Opacity);  // komşu
        Assert.Equal(0.25, view.NodeVisuals["OSYS.Base"].Body.Opacity);      // uzak
        Assert.Equal(0.25, view.NodeVisuals["OSYS.Web.Portal"].Body.Opacity);

        var untouched = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(0.16, untouched.Path.Opacity);
        var touching = view.EdgeVisuals.Single(e => e.Model.To == "OSYS.Server.Api");
        Assert.Equal(1.0, touching.Path.Opacity);
        Assert.Equal(1.6, touching.Path.StrokeThickness);
    }

    [StaFact]
    public void Clearing_the_selection_restores_every_node_and_edge()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Server.Api";

        view.SelectedNode = null;

        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Body.Opacity));
        Assert.All(view.EdgeVisuals, e => Assert.Equal(0.8, e.Path.Opacity));
    }

    // ---------------------------------------------------------------- panel başlığı + boş durum

    [StaFact]
    public void The_panel_header_counts_projects_and_dependencies_from_the_data()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        Assert.Equal("4 projects · 3 dependencies", view.HeaderCountsText);
        // [I-2] design-v1 §1.2: sayaç MAKİNE ÇIKTISIDIR → gömülü Geist Mono (sistem Consolas'ı tasarımın parçası
        // değildir). Kardeş chrome (ConsoleHeader "N lines", StickyLayerList satır sayısı, LatestPill) da aynı
        // TEK aileyi (AppFonts.Mono) kullanır — pack URI hiçbir yerde kopyalanmaz.
        Assert.Same(BuildOrchestrator.App.Controls.AppFonts.Mono, view.HeaderCountsFontFamily);
        Assert.Equal("./#Geist Mono", BuildOrchestrator.App.Controls.AppFonts.Mono.Source);
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

    [StaFact]
    public void The_camera_uses_a_scale_plus_translate_transform_group_and_targets_the_selected_node()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var group = Assert.IsType<TransformGroup>(view.World.RenderTransform);
        Assert.IsType<ScaleTransform>(group.Children[0]);   // CSS `translate(...) scale(...)` = önce ölçek
        Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal(new Point(0, 0), view.World.RenderTransformOrigin);

        view.SelectedNode = "OSYS.Web.Portal";

        var expected = GraphCamera.Compute(view.ViewportSize, view.GraphSize, view.NodeCenter("OSYS.Web.Portal"));
        Assert.Equal(expected, view.CurrentCamera);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_camera_with_no_animation()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.False(view.LastCameraAnimated);
        Assert.Equal(view.CurrentCamera.Scale, ((ScaleTransform)((TransformGroup)view.World.RenderTransform).Children[0]).ScaleX);
        Assert.Equal(view.CurrentCamera.Tx, ((TranslateTransform)((TransformGroup)view.World.RenderTransform).Children[1]).X);
    }

    [StaFact]
    public void With_motion_enabled_the_camera_animates_over_460ms()
    {
        // Dar panel: graf yatayda sığmaz ⇒ seçim GERÇEKTEN yeni bir tx üretir (sığdığında kamera zaten sabittir).
        var view = NewView(true, width: 400, height: 300);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.True(view.LastCameraAnimated);
        Assert.Equal(460.0, GraphCamera.TransitionMs);
    }

    [StaFact]
    public void An_unchanged_camera_target_does_not_restart_the_460ms_transition()
    {
        var view = NewView(true, width: 400, height: 300);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Web.Portal";
        Assert.True(view.LastCameraAnimated);
        var applied = view.CurrentCamera;

        // Statü güncellemesi kamerayı DEĞİŞTİRMEZ (seçim aynı düğümde) — uçuştaki geçiş yeniden doğmamalı.
        view.UpdateStatuses(Nodes(apiStatus: GraphStatus.Building));

        Assert.Equal(applied, view.CurrentCamera);
    }

    [StaFact]
    public void Only_a_FRONTIER_focus_is_remembered_so_it_cannot_suppress_the_next_frontier_retarget()
    {
        // [M-5] 8px "yeniden hedefleme" eşiği YALNIZ frontier dalında uygulanır (GraphCamera.ResolveFocus).
        // Seçimden / settled-merkezden / varsayılan merkezden gelen odak hatırlanırsa, bir sonraki frontier
        // hedefini eşiğin altında kalarak BASTIRABİLİR — bu yüzden yalnız frontier odağı saklanır.
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        Assert.Null(view.PreviousFocus); // seçim yok + frontier yok → varsayılan merkez, HATIRLANMAZ

        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Building));
        Assert.Equal(view.NodeCenter("OSYS.Data.Core"), view.PreviousFocus); // frontier → hatırlanır

        view.SelectedNode = "OSYS.Web.Portal";
        Assert.Null(view.PreviousFocus); // seçim dalı → HATIRLANMAZ

        view.SelectedNode = null;
        Assert.Equal(view.NodeCenter("OSYS.Data.Core"), view.PreviousFocus); // frontier yeniden hedeflenir

        view.IsSettled = true;
        view.UpdateStatuses(Nodes()); // frontier boşaldı → settled merkezi
        Assert.Null(view.PreviousFocus);
    }
}
