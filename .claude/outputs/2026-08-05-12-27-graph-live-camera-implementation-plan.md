# Graf Canlı Kamera (Sinema Modu) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 177 proje / 1214 bağımlılıkta okunmaz yumağa dönen graf panelini, büyük graflarda devreye giren
"sinema modu" ile izlenebilir kılmak: kenar sisi + follow-zoom kamera + zoom'a duyarlı etiketler +
drag/wheel gezinme + 4 sn'de kendiliğinden dönen takip.

**Architecture:** Tüm yeni davranış TEK kapıya bağlı — sinema modu = `nodes.Count > GraphView.FullDetailMaxNodes`
(cull/LOD ile AYNI mevcut eşik; `GraphView._cullEnabled` biti). Saf politika katmanları (`GraphCamera`,
`EdgeStyleResolver`, `GraphLayout`) WPF'siz birim-testlenir; `GraphView` yalnız kablaj yapar. Küçük grafta
(≤150 düğüm) HER ŞEY birebir bugünkü gibi kalır — bu iki taraftan pinlenir.

**Tech Stack:** .NET 10 / WPF (net10.0-windows), xUnit (`[Fact]` saf, `[StaFact]` WPF), mevcut fixture'lar:
`GraphTestView`, `SyntheticGraph`, `DsResources`.

**Onaylı spec:** `.claude/outputs/2026-08-05-12-02-graph-live-camera-design.md` (bu planla aynı branch'te).

## Global Constraints

- **Kırmızı test kuralı:** hiçbir fix/özellik, testin KIRMIZI verdiği gösterilmeden yazılmaz. Her task önce
  testi koşturup FAIL çıktısını görür.
- **Kopya YASAK:** aynı değer/mantık iki yerde tanımlanmaz. 0.16 seçim-dim değeri TEK sabitten; pan kelepçesi
  TEK metottan (`ClampPan`); etiket kurulumu TEK metottan (`EnsureLabel`); test fixture'ları `GraphTestView`'da.
- **UI metinleri İngilizce** (`InteractionText`/`AccessibilityNames` üzerinden); kod yorumları Türkçe.
- **Motion sözleşmesi:** animasyon kodunda hex/ms literal YOK (token'lar `Duration.*`/`KeySpline.*`);
  reduced-motion animasyon başında TAZE okunur (`AnimationsEnabledProvider`); yalnız transform/opacity anime edilir.
- **Sayı biçimleme `InvariantCulture`.**
- **Süit filtrelidir:** `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıkken build alma (Supervisor binary kilitler).
- **Davranışı bilerek değişen eski test** silinmez/gevşetilmez: YENİ kuralı pinleyecek şekilde yeniden yazılır
  ve doc yorumuna eski iddia + değişme gerekçesi yazılır.
- **WPF STA testleri** `[Collection("Console UI (serial)")]` + `[StaFact]` kullanır (mevcut desen).
- Çalışma branch'i: `graph-live-camera` (açık). Task başına bir commit; mesaj sonuna
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` eklenir.

## Dosya haritası

| Dosya | Değişiklik |
|---|---|
| `src/BuildOrchestrator.App/Graph/EdgeStyleResolver.cs` | `DimmedOpacity`/`FogFinishedOpacity` sabitleri + `Resolve`'a `bool fogged` |
| `src/BuildOrchestrator.App/Graph/GraphCamera.cs` | Follow-zoom sabitleri + `ResolveScale`/`FrontierScale`/`ShouldRescale` + `Compute` 4-arg + `ClampPan` + `Pan`/`ZoomAt` + `FollowResumeDelayMs` |
| `src/BuildOrchestrator.App/Graph/GraphLayout.cs` | `LabelShowRatio`/`LabelHideRatio` + `LabelVisibleAtScale`; `LabelsFit` ona delege eder |
| `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` | sis/scale kablajı, `EnsureLabel` + `ApplyLabelVisibility`, jest handler'ları, manuel mod + takip dönüşü + pil |
| `src/BuildOrchestrator.App/Graph/GraphView.xaml` | başlığa `FollowPill` (Border + TrackedTextBlock) |
| `src/BuildOrchestrator.App/Graph/GraphNodeVisual.cs` | `GraphNodeVisual.Label` ve `GraphNodeSlot.ShowsLabel` init→set |
| `src/BuildOrchestrator.App/ViewModels/InteractionText.cs` | `GraphFollowPaused` sabiti |
| `src/BuildOrchestrator.App/AccessibilityNames.cs` | `GraphFollowPill` sabiti |
| `tests/.../App/EdgeStyleResolverTests.cs` | sis matrisi testleri |
| `tests/.../App/GraphCameraTests.cs` | follow-zoom + jest aritmetiği testleri |
| `tests/.../App/GraphLayoutTests.cs` | `LabelVisibleAtScale` testleri |
| `tests/.../App/GraphTestView.cs` | `Realized(...)` yardımcıcısı (Sized + UpdateLayout) |
| `tests/.../App/GraphCinemaTests.cs` (YENİ) | sis kablajı + follow-zoom + etiket LOD STA testleri |
| `tests/.../App/GraphPanZoomTests.cs` (YENİ) | jest + manuel mod + takip dönüşü + pil STA testleri |
| `ARCHITECTURE.md` §13.6, §20 · `README.md` | davranış anlatısı yerinde yeniden yazılır |

Test komutu kısaltması (aşağıda `TEST(X)` diye anılır):
```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance&FullyQualifiedName~X"
```

---

### Task 1: Kenar sisi kuralı (saf — EdgeStyleResolver)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/EdgeStyleResolver.cs`
- Test: `tests/BuildOrchestrator.Tests/App/EdgeStyleResolverTests.cs`

**Interfaces:**
- Produces: `EdgeStyleResolver.DimmedOpacity = 0.16` (const double), `EdgeStyleResolver.FogFinishedOpacity = 0.35`
  (const double), `Resolve(GraphStatus source, bool sourceHasDepIssue, GraphStatus target, bool touchesSelection,
  bool hasSelection, bool fogged = false)` — Task 2 `fogged` argümanını `GraphView._cullEnabled`'dan besler.
- Consumes: yok (saf).

- [ ] **Step 1: Kırmızı testleri yaz**

`EdgeStyleResolverTests.cs`'e ekle. Dosyadaki mevcut `Resolve` yardımcısına `bool fogged = false` parametresi
ekle ve production çağrısına geçir:

```csharp
    private static EdgeStyle Resolve(
        GraphStatus source, GraphStatus target,
        bool sourceHasDepIssue = false, bool touchesSelection = false, bool hasSelection = false,
        bool fogged = false)
        => EdgeStyleResolver.Resolve(source, sourceHasDepIssue, target, touchesSelection, hasSelection, fogged);
```

Sınıfın sonuna yeni bölüm:

```csharp
    // ---------------------------------------------------------------- [sinema] kenar sisi

    [Fact]
    public void Fog_dims_an_idle_edge_to_the_shared_selection_dim_level()
    {
        var style = Resolve(GraphStatus.Discovered, GraphStatus.Discovered, fogged: true);

        // Sis, seçim-dim ile AYNI sabiti okur (kopya yasak — tek doğruluk kaynağı).
        Assert.Equal(EdgeStyleResolver.DimmedOpacity, style.Opacity);
        Assert.Equal(0.16, EdgeStyleResolver.DimmedOpacity);
        Assert.Equal("Brush.Border", style.BrushKey);
        Assert.Equal(1.0, style.Thickness);
        Assert.False(style.IsFlowing);
    }

    [Fact]
    public void Fog_keeps_finished_branches_visible_at_35_percent()
    {
        Assert.Equal(EdgeStyleResolver.FogFinishedOpacity,
            Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, fogged: true).Opacity);
        Assert.Equal(EdgeStyleResolver.FogFinishedOpacity,
            Resolve(GraphStatus.Succeeded, GraphStatus.Failed, fogged: true).Opacity);
        Assert.Equal(0.35, EdgeStyleResolver.FogFinishedOpacity);
        // Renk anahtarları sisten etkilenmez.
        Assert.Equal("Brush.StatusSuccessBorder",
            Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, fogged: true).BrushKey);
    }

    [Fact]
    public void Fog_does_not_touch_the_run_story_flowing_and_error_branches()
    {
        // Akan amber kenar: sisli ve sissiz BİREBİR aynı.
        Assert.Equal(Resolve(GraphStatus.Succeeded, GraphStatus.Building),
                     Resolve(GraphStatus.Succeeded, GraphStatus.Building, fogged: true));
        // Statik kırmızı hata dalı: aynı.
        Assert.Equal(Resolve(GraphStatus.Failed, GraphStatus.Queued),
                     Resolve(GraphStatus.Failed, GraphStatus.Queued, fogged: true));
    }

    [Fact]
    public void Fog_is_inert_while_a_selection_exists_selection_dim_rules_win()
    {
        // Seçim varken sisli çıktı, sissiz seçimli çıktıyla BİREBİR aynıdır (idle/flow/bad/hot dördü de).
        Assert.Equal(Resolve(GraphStatus.Discovered, GraphStatus.Discovered, hasSelection: true),
                     Resolve(GraphStatus.Discovered, GraphStatus.Discovered, hasSelection: true, fogged: true));
        Assert.Equal(Resolve(GraphStatus.Succeeded, GraphStatus.Building, hasSelection: true),
                     Resolve(GraphStatus.Succeeded, GraphStatus.Building, hasSelection: true, fogged: true));
        Assert.Equal(Resolve(GraphStatus.Failed, GraphStatus.Queued, hasSelection: true),
                     Resolve(GraphStatus.Failed, GraphStatus.Queued, hasSelection: true, fogged: true));
        Assert.Equal(
            Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, touchesSelection: true, hasSelection: true),
            Resolve(GraphStatus.Succeeded, GraphStatus.Succeeded, touchesSelection: true, hasSelection: true, fogged: true));
    }

    [Fact]
    public void Fog_defaults_off_so_small_graphs_keep_todays_styles()
        => Assert.Equal(0.8, Resolve(GraphStatus.Discovered, GraphStatus.Discovered).Opacity);
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `TEST(EdgeStyleResolverTests)`
Expected: yeni testler FAIL (CS1501 — `Resolve` 6. parametreyi tanımıyor; derleme hatası da kırmızı sayılır,
önce derlenen kısmı düzeltmek için Step 3'e geç).

- [ ] **Step 3: Implementasyon**

`EdgeStyleResolver.cs`:

1. Sabitleri ekle (sınıfın sabitler bölgesine):

```csharp
    /// <summary>Seçim-dim VE sinema sisi ortak opaklığı (design-v1 §2.3 `op .16`; kopya yasak — iki kural
    /// da BU sabiti okur).</summary>
    public const double DimmedOpacity = 0.16;
    /// <summary>[sinema] Succeeded/failed'e varan renkli kenarın sis opaklığı — biten bölge sakinleşir ama
    /// hikâye silinmez (spec §3.2).</summary>
    public const double FogFinishedOpacity = 0.35;
```

2. `Resolve` imzasına `bool fogged = false` ekle ve gövdeyi güncelle (yorumlar dahil mevcut yapı korunur;
   yalnız gösterilen satırlar değişir):

```csharp
    public static EdgeStyle Resolve(
        GraphStatus source, bool sourceHasDepIssue, GraphStatus target,
        bool touchesSelection, bool hasSelection, bool fogged = false)
    {
        bool flow = target == GraphStatus.Building;
        bool bad = source == GraphStatus.Failed || sourceHasDepIssue;
        bool hot = hasSelection && touchesSelection;

        string brushKey = "Brush.Border";
        double thickness = DefaultThickness;
        // [sinema] Sis, seçim-dim ile AYNI seviyeye iner — koşuya karışmamış kenar büyük grafta fısıltıdır.
        double opacity = hasSelection || fogged ? DimmedOpacity : 0.8;
        bool hasErrorDash = false;
        bool isFlowing = false;

        if (flow)
        {
            brushKey = "Brush.Amber";
            opacity = hasSelection ? 0.2 : 0.85;
            isFlowing = true;
        }
        else if (target == GraphStatus.Succeeded)
        {
            brushKey = "Brush.StatusSuccessBorder";
            if (!hasSelection && fogged) opacity = FogFinishedOpacity;
        }
        else if (target == GraphStatus.Failed)
        {
            brushKey = "Brush.StatusFailBorder";
            if (!hasSelection && fogged) opacity = FogFinishedOpacity;
        }
        // ... bad ve hot blokları AYNEN kalır (opacity'lerini kendileri ezer) ...
```

`bad`/`hot` blokları ve dash seçimi DEĞİŞMEZ. Var olan `hasSelection ? 0.16 : 0.8` literalindeki 0.16 artık
`DimmedOpacity`'den gelir.

- [ ] **Step 4: Yeşili gör**

Run: `TEST(EdgeStyleResolverTests)`
Expected: TÜM testler PASS (eskiler dahil — `fogged` default `false` eski davranışı birebir korur).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Graph/EdgeStyleResolver.cs tests/BuildOrchestrator.Tests/App/EdgeStyleResolverTests.cs
git commit -m "feat(graph): kenar sisi kurali (EdgeStyleResolver fogged)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Sis kablajı — büyük grafta devrede (GraphView)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (yalnız `ApplyEdgeStyle` içindeki `Resolve` çağrısı)
- Modify: `tests/BuildOrchestrator.Tests/App/GraphTestView.cs` (`Realized` yardımcısı)
- Create: `tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs`

**Interfaces:**
- Consumes: Task 1'in `Resolve(..., bool fogged)` imzası; mevcut `GraphView._cullEnabled` / `IsCullEnabled`.
- Produces: `GraphTestView.Realized(Size, Func<bool>?, IMotionSettings?, FontFamily?)` — Task 4/5/6/7 testleri
  bunu kullanır. `GraphCinemaTests` dosyası ve içindeki `BigNodes(...)` yardımcıları (Task 4/5 aynı dosyaya ekler).

- [ ] **Step 1: `GraphTestView.Realized` ekle**

`GraphTestView.cs` sonuna (fixture TEK yerde kalsın — kopya yasak):

```csharp
    /// <summary>Sized + <c>UpdateLayout</c>: cull/etiket/kamera kablajını gerçek yerleşimle test eden
    /// STA testlerinin kurulumu (GraphCullTests'in yerel Layout deseni; artık ortak).</summary>
    public static GraphView Realized(
        Size size,
        Func<bool>? animationsEnabled = null,
        IMotionSettings? motion = null,
        FontFamily? labelFontFamily = null)
    {
        var view = Sized(size, animationsEnabled, motion, labelFontFamily);
        view.UpdateLayout();
        return view;
    }
```

- [ ] **Step 2: Kırmızı testleri yaz**

Yeni dosya `tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs`:

```csharp
using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sinema] Büyük grafta (düğüm sayısı > FullDetailMaxNodes — cull/LOD ile AYNI kapı) devreye giren
/// sinema modunun WPF kablajı: kenar sisi, follow-zoom kamera ve zoom'a duyarlı etiketler.
/// Küçük grafta HER ŞEYİN birebir bugünkü gibi kaldığı da burada pinlenir (spec §3.0).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphCinemaTests
{
    private static readonly Size Panel = new(600, 400);

    /// <summary>Sinema bandında deterministik graf: 4 katman, katman başına eşit dağıtım, hepsi Discovered.
    /// Adlar kısa tutulur (etiket senaryoları Task 5'te kendi adlarını üretir).</summary>
    internal static IReadOnlyList<GraphNode> BigNodes(int count = GraphView.FullDetailMaxNodes + 6) =>
        [.. Enumerable.Range(0, count).Select(i => new GraphNode($"N{i}", i % 4, GraphStatus.Discovered))];

    /// <summary>Her düğümü bir üst katmandaki komşusuna bağlayan basit kenar kümesi.</summary>
    internal static IReadOnlyList<GraphEdge> ChainEdges(IReadOnlyList<GraphNode> nodes) =>
        [.. nodes.Where(n => n.Layer > 0)
            .Select(n => new GraphEdge(
                nodes.First(m => m.Layer == n.Layer - 1).Name, n.Name))];

    private static GraphView NewView() => GraphTestView.Realized(Panel, labelFontFamily: DsResources.MonoFontFamily);

    // ---------------------------------------------------------------- kenar sisi kablajı

    [StaFact]
    public void A_large_graph_fogs_its_idle_edges_to_the_dim_level()
    {
        var nodes = BigNodes();
        var view = NewView();

        view.SetGraph(nodes, ChainEdges(nodes));

        Assert.True(view.IsCullEnabled); // sinema kapısı = cull kapısı
        var idle = view.EdgeVisuals.First();
        Assert.Equal(EdgeStyleResolver.DimmedOpacity, idle.Path.Opacity);
    }

    [StaFact]
    public void A_small_graph_keeps_todays_full_opacity_edges()
    {
        var nodes = BigNodes(GraphView.FullDetailMaxNodes); // tam sınırda: sinema KAPALI
        var view = NewView();

        view.SetGraph(nodes, ChainEdges(nodes));

        Assert.False(view.IsCullEnabled);
        Assert.Equal(0.8, view.EdgeVisuals.First().Path.Opacity);
    }
}
```

- [ ] **Step 3: Kırmızıyı gör**

Run: `TEST(GraphCinemaTests)`
Expected: `A_large_graph_fogs...` FAIL — beklenen 0.16, gerçek 0.8 (sis kablajı henüz yok).
`A_small_graph_keeps...` PASS (bugünkü davranış).

- [ ] **Step 4: Implementasyon**

`GraphView.xaml.cs` → `ApplyEdgeStyle` içindeki çağrıya kapıyı geçir:

```csharp
        var style = EdgeStyleResolver.Resolve(
            source?.Model.Status ?? GraphStatus.Discovered,
            source?.Model.HasDepIssue ?? false,
            target?.Model.Status ?? GraphStatus.Discovered,
            touchesSelection,
            _selectedNode is not null,
            fogged: _cullEnabled); // [sinema] kapı = cull kapısı (FullDetailMaxNodes) — spec §3.0
```

- [ ] **Step 5: Yeşili gör + commit**

Run: `TEST(GraphCinemaTests)` → PASS; ardından `TEST(GraphCullTests)` ve `TEST(GraphRenderTests)` → PASS
(küçük graf yolunda stil değişmedi).

```powershell
git add src/BuildOrchestrator.App/Graph/GraphView.xaml.cs tests/BuildOrchestrator.Tests/App/GraphTestView.cs tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs
git commit -m "feat(graph): kenar sisi buyuk grafta devrede" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Follow-zoom ölçek politikası (saf — GraphCamera)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphCamera.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphCameraTests.cs`

**Interfaces:**
- Produces (Task 4/6/7 tüketir):
  - `const double FollowMinScale = 0.85`, `FollowMaxScale = 1.4`, `SelectionScale = 1.1`,
    `ScaleRetargetThreshold = 0.05`, `ManualMinScale = 0.45`, `ManualMaxScale = 2.0`,
    `WheelZoomStep = 1.1`, `FollowResumeDelayMs = 4000.0`,
    `FrontierMarginX`, `FrontierMarginY` (aşağıda)
  - `static double ResolveScale(Size viewport, Size graph, bool cinema, Point? selected, IReadOnlyList<Point> building, bool settled, double? previousScale)`
  - `static double FrontierScale(Size viewport, IReadOnlyList<Point> building)`
  - `static bool ShouldRescale(double previous, double next)`
  - `static CameraTransform Compute(Size viewport, Size graph, Point focus, double scale)` (mevcut 3-arg buna delege eder)
- Consumes: `GraphLayout.NodeCellWidth/NodeSize/LabelGap/LabelHeight` (mevcut).

- [ ] **Step 1: Kırmızı testleri yaz**

`GraphCameraTests.cs` sonuna:

```csharp
    // ---------------------------------------------------------------- [sinema] follow-zoom ölçeği

    [Fact]
    public void Outside_cinema_the_scale_is_always_the_fit_scale()
    {
        var viewport = new Size(600, 400);
        double scale = GraphCamera.ResolveScale(
            viewport, Graph, cinema: false,
            selected: new Point(100, 100), building: [new Point(1, 1)], settled: false, previousScale: null);

        // Sinema dışında seçim de frontier de ölçeği DEĞİŞTİRMEZ — bugünkü davranış birebir (spec §3.0).
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), scale);
    }

    [Fact]
    public void A_selection_zooms_to_the_fixed_readable_scale_in_cinema()
        => Assert.Equal(GraphCamera.SelectionScale, GraphCamera.ResolveScale(
            new Size(600, 400), Graph, cinema: true,
            selected: new Point(100, 100), building: [], settled: false, previousScale: null));

    [Fact]
    public void A_single_building_node_frames_at_the_follow_ceiling()
    {
        // Tek düğümlük frontier: bbox = 2×kenar payı ⇒ ölçek tavana kelepçelenir (26px kare ~36px görünür).
        double scale = GraphCamera.FrontierScale(new Size(600, 400), [new Point(440, 200)]);

        Assert.Equal(GraphCamera.FollowMaxScale, scale);
        Assert.Equal(1.4, GraphCamera.FollowMaxScale);
    }

    [Fact]
    public void A_wide_frontier_clamps_at_the_follow_floor()
    {
        // 1600px'e yayılmış cephe 600'lük panele 0.85'in altında sığardı — tabana kelepçelenir.
        double scale = GraphCamera.FrontierScale(new Size(600, 400), [new Point(100, 200), new Point(1700, 200)]);

        Assert.Equal(GraphCamera.FollowMinScale, scale);
        Assert.Equal(0.85, GraphCamera.FollowMinScale);
    }

    [Fact]
    public void The_frontier_frame_includes_the_cell_margins_and_fit_padding()
    {
        // Dikey eksen kısıt olsun: iki düğüm alt alta, panel alçak.
        var viewport = new Size(2000, 300);
        var building = new List<Point> { new(400, 100), new(400, 300) };
        double h = 200 + 2 * GraphCamera.FrontierMarginY;

        Assert.Equal(Math.Clamp(300 / h, GraphCamera.FollowMinScale, GraphCamera.FollowMaxScale),
            GraphCamera.FrontierScale(viewport, building), 10);
    }

    [Fact]
    public void Settled_or_idle_cinema_returns_to_the_overview_fit_scale()
    {
        var viewport = new Size(600, 400);
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: [], settled: true, previousScale: null));
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: [], settled: false, previousScale: null));
    }

    [Fact]
    public void A_scale_change_below_the_threshold_keeps_the_previous_scale_zeno_guard()
    {
        var viewport = new Size(600, 400);
        var building = new List<Point> { new(100, 200), new(1700, 200) }; // taban: 0.85
        // Önceki ölçek hedefe 0.05'ten yakın → ESKİ ölçek korunur (kamera mikro-zoom'la titremez).
        Assert.Equal(0.86, GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: building, settled: false, previousScale: 0.86));
        // Eşik ve üstü → yeni hedef kazanır.
        Assert.Equal(0.85, GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: building, settled: false, previousScale: 0.90));
        Assert.False(GraphCamera.ShouldRescale(1.00, 1.04));
        Assert.True(GraphCamera.ShouldRescale(1.00, 1.05));
    }

    [Fact]
    public void Compute_with_an_explicit_scale_centers_the_focus_and_the_3_arg_overload_stays_fit()
    {
        var viewport = new Size(500, 300);
        var t = GraphCamera.Compute(viewport, Graph, new Point(440, 292), 1.4);

        Assert.Equal(1.4, t.Scale);
        // Odak panel merkezinde: tx = vw/2 − fx·s (pan payı sınırları içinde).
        Assert.Equal(Math.Floor(250 - 440 * 1.4 + 0.5), t.Tx);
        // 3-arg overload = FitScale (mevcut testlerin tamamı bunun üstünden yeşil kalır).
        Assert.Equal(GraphCamera.Compute(viewport, Graph, new Point(440, 292),
            GraphCamera.FitScale(viewport, Graph)), GraphCamera.Compute(viewport, Graph, new Point(440, 292)));
    }
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `TEST(GraphCameraTests)`
Expected: yeni testler derleme hatasıyla FAIL (`ResolveScale`/`FrontierScale` yok).

- [ ] **Step 3: Implementasyon**

`GraphCamera.cs`'e ekle (mevcut üyeler değişmez; `Compute` gövdesi 4-arg'a taşınır):

```csharp
    // ---------------------------------------------------------------- [sinema] follow-zoom (spec §3.1)

    /// <summary>Takip bandı: frontier çerçevesi bu aralığa kıstırılır (26px kare ekranda ~22–36px).</summary>
    public const double FollowMinScale = 0.85;
    public const double FollowMaxScale = 1.4;
    /// <summary>Seçimde hedef ölçek — listeden tıklanan proje okunur yakınlıkta gelir.</summary>
    public const double SelectionScale = 1.1;
    /// <summary>Ölçek Zeno eşiği: hedef bundan az değiştiyse yeniden hedefleme yok (odak 8px eşiğinin ölçek eşi).</summary>
    public const double ScaleRetargetThreshold = 0.05;
    /// <summary>Manuel (wheel) bandı — otomatik banttan geniştir; istenirse tüm siluet görülebilir.</summary>
    public const double ManualMinScale = 0.45;
    public const double ManualMaxScale = 2.0;
    /// <summary>Wheel kademesi (çarpansal).</summary>
    public const double WheelZoomStep = 1.1;
    /// <summary>Son manuel girdiden takibin kendiliğinden dönüşüne kadar geçen süre (spec §3.5).</summary>
    public const double FollowResumeDelayMs = 4000.0;
    /// <summary>Frontier bbox'ına her yana eklenen yatay pay: yarım hücre + sığdırma payı.</summary>
    public const double FrontierMarginX = GraphLayout.NodeCellWidth / 2 + FitPadding;
    /// <summary>Dikey pay: yarım kare + etiket bandı + sığdırma payı.</summary>
    public const double FrontierMarginY =
        GraphLayout.NodeSize / 2 + GraphLayout.LabelGap + GraphLayout.LabelHeight + FitPadding;

    /// <summary>Ölçek hedefi (spec §3.1 tablosu). <paramref name="previousScale"/> YALNIZ frontier dalında
    /// Zeno eşiği için kullanılır — <see cref="ResolveFocus"/>'un previousFocus sözleşmesinin ölçek eşi.</summary>
    public static double ResolveScale(
        Size viewport, Size graph, bool cinema,
        Point? selected, IReadOnlyList<Point> building, bool settled, double? previousScale)
    {
        ArgumentNullException.ThrowIfNull(building);

        if (!cinema) return FitScale(viewport, graph);
        if (selected is not null) return SelectionScale;
        if (building.Count > 0)
        {
            double next = FrontierScale(viewport, building);
            return previousScale is { } prev && !ShouldRescale(prev, next) ? prev : next;
        }
        return FitScale(viewport, graph); // settled/idle: bugünkü kuşbakışı
    }

    /// <summary>Building merkezlerinin bbox'ını (+ hücre payları) panele çerçeveleyen ölçek, takip bandına
    /// kıstırılmış. Çok geniş cephe tabana kelepçelenir — ağırlık merkezi görünür kalır (spec §3.1).</summary>
    public static double FrontierScale(Size viewport, IReadOnlyList<Point> building)
    {
        ArgumentNullException.ThrowIfNull(building);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var p in building)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        double w = maxX - minX + 2 * FrontierMarginX;
        double h = maxY - minY + 2 * FrontierMarginY;
        return Math.Clamp(Math.Min(viewport.Width / w, viewport.Height / h), FollowMinScale, FollowMaxScale);
    }

    /// <summary>Ölçek yeterince değişti mi (frontier dalının Zeno koruması).</summary>
    public static bool ShouldRescale(double previous, double next) =>
        Math.Abs(next - previous) >= ScaleRetargetThreshold;
```

`Compute`'u 4-arg yap; 3-arg delege kalsın (tek doğruluk kaynağı):

```csharp
    /// <summary>Odağı panelin ortasına getiren transform; graf sığıyorsa eksende ortalanır, sığmıyorsa
    /// 12px kenar payıyla sınırlanır. 3-arg biçim = bugünkü fit davranışı (ölçek <see cref="FitScale"/>).</summary>
    public static CameraTransform Compute(Size viewport, Size graph, Point focus) =>
        Compute(viewport, graph, focus, FitScale(viewport, graph));

    public static CameraTransform Compute(Size viewport, Size graph, Point focus, double scale) =>
        ClampPan(viewport, graph, scale,
            viewport.Width / 2 - focus.X * scale,
            viewport.Height / 2 - focus.Y * scale);

    /// <summary>Pan kelepçesi TEK yerde: sığan eksen ortalanır, sığmayan 12px payla sınırlanır, uçlar piksele
    /// yuvarlanır. <see cref="Compute"/> ve (Task 6) manuel Pan/ZoomAt AYNI metodu kullanır — kopya yasak.</summary>
    public static CameraTransform ClampPan(Size viewport, Size graph, double scale, double tx, double ty)
    {
        double scaledW = graph.Width * scale;
        double scaledH = graph.Height * scale;

        tx = scaledW <= viewport.Width
            ? (viewport.Width - scaledW) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Width - scaledW - PanMarginPx, tx));
        ty = scaledH <= viewport.Height
            ? (viewport.Height - scaledH) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Height - scaledH - PanMarginPx, ty));

        return new CameraTransform(scale, RoundPixels(tx), RoundPixels(ty));
    }
```

(Eski `Compute` gövdesindeki kelepçe satırları `ClampPan`'a TAŞINIR — kopyalanmaz.)

- [ ] **Step 4: Yeşili gör**

Run: `TEST(GraphCameraTests)`
Expected: TÜM testler PASS (eski Compute testleri 3-arg delegeyle birebir aynı sonucu verir).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Graph/GraphCamera.cs tests/BuildOrchestrator.Tests/App/GraphCameraTests.cs
git commit -m "feat(graph): follow-zoom olcek politikasi (saf)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Kamera follow-zoom kablajı (GraphView)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (`ApplyCamera` + `_previousScale` alanı)
- Test: `tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs`

**Interfaces:**
- Consumes: Task 3 `ResolveScale(...)`; Task 2'nin `BigNodes`/`ChainEdges`/`NewView` yardımcıları.
- Produces: `GraphView.CurrentCamera.Scale` artık sinemada frontier/seçime göre değişir; `_previousScale`
  yalnız frontier dalında hatırlanır (Task 7'nin manuel modu bu alanı sıfırlamaz — bağımsız).

- [ ] **Step 1: Kırmızı testleri yaz**

`GraphCinemaTests.cs`'e ekle:

```csharp
    // ---------------------------------------------------------------- follow-zoom kablajı

    /// <summary>Tek düğümün statüsünü değiştirir — GraphPanZoomTests de kullanır (fixture tek yerde).</summary>
    internal static IReadOnlyList<GraphNode> WithStatus(
        IReadOnlyList<GraphNode> nodes, string name, GraphStatus status) =>
        [.. nodes.Select(n => n.Name == name ? n with { Status = status } : n)];

    [StaFact]
    public void A_building_frontier_zooms_the_camera_into_the_follow_band()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        // Tek düğümlük frontier tavana çerçevelenir (saf tarafı Task 3 pinledi; burada KABLAJ pinlenir).
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void Settled_returns_the_camera_to_the_overview_fit()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        view.UpdateStatuses(nodes); // frontier bitti
        view.IsSettled = true;

        Assert.Equal(GraphCamera.FitScale(view.ViewportSize, view.GraphSize), view.CurrentCamera.Scale);
    }

    [StaFact]
    public void A_small_graph_never_changes_scale_when_building_todays_behavior_pinned()
    {
        var nodes = BigNodes(GraphView.FullDetailMaxNodes);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        double before = view.CurrentCamera.Scale;

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.Equal(before, view.CurrentCamera.Scale); // sinema dışı: ölçek fit'te sabit
    }

    [StaFact]
    public void A_selection_zooms_to_the_selection_scale_in_cinema()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.SelectedNode = "N3";

        Assert.Equal(GraphCamera.SelectionScale, view.CurrentCamera.Scale);
    }
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `TEST(GraphCinemaTests)`
Expected: yeni 4 testten `A_small_graph_never_changes...` PASS, diğer üçü FAIL (ölçek hep fit — 0.68 civarı).

- [ ] **Step 3: Implementasyon**

`GraphView.xaml.cs`:

1. Alan ekle (`_previousFocus`'un yanına):

```csharp
    /// <summary>[sinema] YALNIZ frontier dalından gelen ölçek hedefi hatırlanır (Zeno eşiği yalnız orada
    /// geçerlidir) — <c>_previousFocus</c> sözleşmesinin ölçek eşi.</summary>
    private double? _previousScale;
```

2. `SetGraph` reset bloğuna (`_previousFocus = null;` satırının yanına): `_previousScale = null;`

3. `ApplyCamera` içinde odak çözümünün ardından ölçek çözümü (mevcut satırlar korunur, `Compute` çağrısı
   4-arg olur):

```csharp
        var focus = GraphCamera.ResolveFocus(selected, building, _isSettled, GraphSize, _previousFocus);
        bool focusCameFromFrontier = selected is null && building.Count > 0;
        _previousFocus = focusCameFromFrontier ? focus : null;

        // [sinema] Ölçek de hedefin parçasıdır (spec §3.1). Sinema dışında ResolveScale = FitScale ⇒ birebir
        // bugünkü davranış (yapısal garanti, GraphCinemaTests pinler).
        double scale = GraphCamera.ResolveScale(
            viewport, GraphSize, _cullEnabled, selected, building, _isSettled, _previousScale);
        _previousScale = _cullEnabled && focusCameFromFrontier ? scale : null;

        var camera = GraphCamera.Compute(viewport, GraphSize, focus, scale);
```

(`ResolveFocus`'a giden `selected` zaten `Point?` olarak hesaplanıyor — aynı değişken `ResolveScale`'e de gider.)

- [ ] **Step 4: Yeşili gör**

Run: `TEST(GraphCinemaTests)` → PASS. Ardından kamera davranışına dokunan mevcut süit:
`TEST(GraphRenderTests)`, `TEST(GraphCullTests)`, `TEST(GraphClickTests)` → PASS (küçük graf fit'te sabit).

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Graph/GraphView.xaml.cs tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs
git commit -m "feat(graph): kamera follow-zoom kablaji" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Zoom'a duyarlı etiketler (GraphLayout + GraphView)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphLayout.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphNodeVisual.cs` (`Label`/`ShowsLabel` init→set)
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (`EnsureLabel`, `ApplyLabelVisibility`, `_labelWidths` alanı)
- Test: `tests/BuildOrchestrator.Tests/App/GraphLayoutTests.cs` (saf) + `tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs` (kablaj)

**Interfaces:**
- Produces:
  - `GraphLayout.LabelShowRatio = 1.0`, `GraphLayout.LabelHideRatio = 0.85` (const double)
  - `static bool LabelVisibleAtScale(double spacing, double widestLabelWidth, double scale, bool currentlyVisible)`
  - `GraphView.EnsureLabel(GraphNodeVisual)` (private) — etiket kurulumunun TEK yolu
  - `GraphNodeVisual.Label { get; set; }`, `GraphNodeSlot.ShowsLabel { get; set; }` (artık mutable)
- Consumes: Task 4'ün kamera hedef ölçeği (`CurrentCamera.Scale`), mevcut `MeasureLayerLabelWidths`.

- [ ] **Step 1: Saf kırmızı testler**

`GraphLayoutTests.cs`'e ekle:

```csharp
    // ---------------------------------------------------------------- [sinema] zoom'a duyarlı etiket

    [Fact]
    public void A_label_appears_at_ratio_1_and_survives_down_to_0_85_hysteresis()
    {
        // r = spacing×scale / widest. Gizliyken ancak r ≥ 1.0'da belirir; görünürken r ≥ 0.85 oldukça kalır.
        Assert.False(GraphLayout.LabelVisibleAtScale(34.4, 48.0, 1.35, currentlyVisible: false)); // r≈0.967 < 1
        Assert.True(GraphLayout.LabelVisibleAtScale(34.4, 48.0, 1.40, currentlyVisible: false));  // r≈1.003 ≥ 1
        Assert.True(GraphLayout.LabelVisibleAtScale(34.4, 48.0, 1.25, currentlyVisible: true));   // r≈0.895 ≥ .85
        Assert.False(GraphLayout.LabelVisibleAtScale(34.4, 48.0, 1.15, currentlyVisible: true));  // r≈0.824 < .85
        Assert.Equal(1.0, GraphLayout.LabelShowRatio);
        Assert.Equal(0.85, GraphLayout.LabelHideRatio);
    }

    [Fact]
    public void The_static_labels_fit_rule_is_the_scale_1_case_of_the_same_predicate()
    {
        // Tek doğruluk kaynağı: LabelsFit(s,w) ≡ LabelVisibleAtScale(s,w,1,false).
        Assert.Equal(GraphLayout.LabelsFit(96, 90), GraphLayout.LabelVisibleAtScale(96, 90, 1.0, false));
        Assert.Equal(GraphLayout.LabelsFit(34.4, 90), GraphLayout.LabelVisibleAtScale(34.4, 90, 1.0, false));
    }
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `TEST(GraphLayoutTests)` → yeni testler derleme hatasıyla FAIL.

- [ ] **Step 3: Saf implementasyon**

`GraphLayout.cs`:

```csharp
    /// <summary>[sinema] Gizli bir etiketin belirme eşiği (oran = aralık×ölçek / en geniş etiket).</summary>
    public const double LabelShowRatio = 1.0;
    /// <summary>[sinema] Görünür bir etiketin tutunma eşiği — histerezis bandı titremeyi önler (spec §3.3).</summary>
    public const double LabelHideRatio = 0.85;

    /// <summary>[sinema] Etiket verilen kamera hedef ölçeğinde görünür mü. Histerezisli: gizliyken
    /// <see cref="LabelShowRatio"/>, görünürken <see cref="LabelHideRatio"/> eşiği geçerlidir.</summary>
    public static bool LabelVisibleAtScale(
        double spacing, double widestLabelWidth, double scale, bool currentlyVisible) =>
        spacing * scale >= widestLabelWidth * (currentlyVisible ? LabelHideRatio : LabelShowRatio);

    public static bool LabelsFit(double spacing, double widestLabelWidth) =>
        LabelVisibleAtScale(spacing, widestLabelWidth, 1.0, currentlyVisible: false);
```

(Mevcut `LabelsFit` gövdesi delegeyle DEĞİŞTİRİLİR — davranış birebir: `s ≥ w` ≡ `s×1 ≥ w×1`.)

Run: `TEST(GraphLayoutTests)` → PASS.

- [ ] **Step 4: Kablaj kırmızı testleri**

`GraphCinemaTests.cs`'e ekle:

```csharp
    // ---------------------------------------------------------------- zoom'a duyarlı etiketler

    /// <summary>Etiketleri STATİK kararla düşen ama 1.4×'te sığan graf: 40 düğümlük katman (aralık ≈34.4px),
    /// ~6 karakterlik mono adlar (10px'te ~36–42px çizilir; 34.4 < w ≤ 34.4×1.4).</summary>
    private static IReadOnlyList<GraphNode> CrowdedNodes() =>
        [.. Enumerable.Range(0, GraphView.FullDetailMaxNodes + 10)
            .Select(i => new GraphNode($"Node{i:D2}", i % 4, GraphStatus.Discovered))];

    [StaFact]
    public void Zooming_into_the_frontier_materialises_the_labels_that_fit_at_that_scale()
    {
        var nodes = CrowdedNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        var target = view.NodeVisuals["Node00"];
        Assert.Null(target.Label); // statik LOD düşürdü (kalabalık katman, ölçek 1 varsayımı)

        view.UpdateStatuses(WithStatus(nodes, "Node00", GraphStatus.Building)); // kamera 1.4'e çerçeveler

        Assert.NotNull(target.Label);
        Assert.Equal(Visibility.Visible, target.Label!.Visibility);
        Assert.Equal("Node00", target.Label.Text);
        Assert.Null(target.Body.ToolTip); // etiket görünürken tam-ad tooltip'i kalkar
    }

    [StaFact]
    public void Zooming_back_out_hides_the_labels_and_restores_the_tooltip()
    {
        var nodes = CrowdedNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        view.UpdateStatuses(WithStatus(nodes, "Node00", GraphStatus.Building));
        var target = view.NodeVisuals["Node00"];
        Assert.NotNull(target.Label);

        view.UpdateStatuses(nodes);
        view.IsSettled = true; // kuşbakışına dönüş (~0.68): r histerezis tabanının çok altında

        Assert.Equal(Visibility.Collapsed, target.Label!.Visibility);
        Assert.Equal("Node00", target.Body.ToolTip);
    }

    [StaFact]
    public void Small_graph_labels_are_untouched_by_the_scale_machinery()
    {
        var nodes = BigNodes(36);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.All(view.NodeVisuals.Values, v => Assert.NotNull(v.Label)); // tam-detay garantisi
    }
```

Run: `TEST(GraphCinemaTests)` → yeni testler FAIL (`Label` hâlâ statik/null; `ToolTip` beklenen gibi değil).
Not: `CrowdedNodes` ad genişliği font metriğine bağlıdır — `Node00` 1.4×'te sığmıyorsa adı bir karakter
kısalt/uzat (ör. `Nd{i:D2}` / `Node{i:D3}`); eşik testin ÖZÜ değil, malzemesidir.

- [ ] **Step 5: Kablaj implementasyonu**

1. `GraphNodeVisual.cs`: `public TextBlock? Label { get; init; }` → `{ get; set; }`;
   `GraphNodeSlot`: `public required bool ShowsLabel { get; init; }` → `{ get; set; }`
   (yorumlarına "sinema modunda kamera hedef ölçeğiyle güncellenir — spec §3.3" cümlesi eklenir).

2. `GraphView.xaml.cs` alan: `private Dictionary<int, double>? _labelWidths;` — `SetGraph`'ta
   `var labelWidths = ...` satırı `_labelWidths = fullDetail ? null : MeasureLayerLabelWidths(nodes);` olur
   (yerel kullanım alanı okur).

3. `BuildNodeVisual` içindeki etiket bloğu `EnsureLabel`'e TAŞINIR (kopyalanmaz):

```csharp
    /// <summary>[sinema] Etiket kurulumunun TEK yolu — statik yol (BuildNodeVisual) ve zoom yolu
    /// (ApplyLabelVisibility) aynı metodu kullanır (kopya YASAK).</summary>
    private void EnsureLabel(GraphNodeVisual visual)
    {
        if (visual.Label is not null) return;

        var label = new TextBlock
        {
            FontFamily = AppFonts.Mono,
            FontSize = 10,
            MaxWidth = GraphLayout.NodeCellWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, GraphLayout.LabelGap, 0, 0),
            Text = visual.Model.ShortName,
        };
        // feasibility §3.4/§4.4: Display modu scale altında bozulur — LOKAL Ideal override (T65).
        TextOptions.SetTextFormattingMode(label, TextFormattingMode.Ideal);
        label.SetResourceReference(TextBlock.ForegroundProperty,
            string.Equals(visual.Model.Name, _selectedNode, StringComparison.Ordinal)
                ? "Brush.TextPrimary" : "Brush.TextDim");
        visual.Body.Children.Add(label);
        visual.Label = label;
    }
```

`BuildNodeVisual`'da eski `if (slot.ShowsLabel) { label = new TextBlock... } else { body.ToolTip = ... }`
bloğu şuna iner (visual nesnesi kurulduktan sonra, `ApplyNodeStatus`'tan önce):

```csharp
        if (slot.ShowsLabel) EnsureLabel(visual);
        else visual.Body.ToolTip = node.Name; // [G2 · A3] etiketsiz düğüm anonim kalmaz
```

(`TextBlock? label` yerel değişkeni ve `Label = label` init ataması kalkar; `GraphNodeVisual` nesnesi
`Label = null` ile kurulur.)

4. Görünürlük değerlendirmesi:

```csharp
    /// <summary>[sinema] Tek düğümün etiket görünürlüğünü kamera HEDEF ölçeğine göre uygular (spec §3.3).
    /// Tam-detay bandında (_labelWidths null) HİÇ çalışmaz — statik garanti.</summary>
    private void ApplyLabelVisibility(GraphNodeSlot slot, GraphNodeVisual visual, double targetScale)
    {
        if (_labelWidths is null) return;

        double spacing = _layout.LayerSpacing.TryGetValue(visual.Model.Layer, out double s)
            ? s : GraphLayout.MaxNodeSpacing;
        double widest = _labelWidths.GetValueOrDefault(visual.Model.Layer, GraphLayout.NodeCellWidth);
        bool show = GraphLayout.LabelVisibleAtScale(spacing, widest, targetScale, slot.ShowsLabel);
        if (show == slot.ShowsLabel && (visual.Label is not null) == show) return; // değişmediyse dokunma

        slot.ShowsLabel = show;
        if (show)
        {
            EnsureLabel(visual);
            visual.Label!.Visibility = Visibility.Visible;
            visual.Body.ToolTip = null;
        }
        else
        {
            if (visual.Label is { } label) label.Visibility = Visibility.Collapsed;
            visual.Body.ToolTip = visual.Model.Name;
        }
    }

    private void UpdateLabelVisibility(double targetScale)
    {
        if (_labelWidths is null) return;
        foreach (var visual in _nodes.Values)
            ApplyLabelVisibility(_slots[visual.Model.Name], visual, targetScale);
    }
```

5. Çağrı noktaları:
   - `ApplyCamera`: `CurrentCamera = camera; _hasCamera = true;` satırlarından hemen sonra
     `UpdateLabelVisibility(camera.Scale);`
   - `MaterializeNode` sonuna: `ApplyLabelVisibility(slot, visual, CurrentCamera.Scale);`
     (pencere içinde sonradan görünen düğüm de doğru karara oturur).

- [ ] **Step 6: Yeşili gör + commit**

Run: `TEST(GraphCinemaTests)`, `TEST(GraphLayoutTests)`, `TEST(GraphCullTests)`, `TEST(GraphRenderTests)` → PASS.

```powershell
git add src/BuildOrchestrator.App/Graph/GraphLayout.cs src/BuildOrchestrator.App/Graph/GraphNodeVisual.cs src/BuildOrchestrator.App/Graph/GraphView.xaml.cs tests/BuildOrchestrator.Tests/App/GraphLayoutTests.cs tests/BuildOrchestrator.Tests/App/GraphCinemaTests.cs
git commit -m "feat(graph): zoom-duyarli etiket LOD" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Drag/wheel jestleri + manuel kamera (GraphCamera + GraphView)

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphCamera.cs` (`Pan`, `ZoomAt`)
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (jest handler'ları, manuel mod, `SnapCameraTo`)
- Test: `tests/BuildOrchestrator.Tests/App/GraphCameraTests.cs` (saf) + Create: `tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs`

**Interfaces:**
- Produces (Task 7 tüketir):
  - `GraphCamera.Pan(CameraTransform, Vector, Size viewport, Size graph)` → `CameraTransform`
  - `GraphCamera.ZoomAt(CameraTransform, Point cursor, double factor, Size viewport, Size graph)` → `CameraTransform`
  - `GraphView` internal seam'leri: `HandlePanStart(Point)`, `HandlePanMove(Point)`, `HandlePanEnd()`,
    `HandleWheel(Point, int delta)`, `internal bool IsManualCamera`, `internal long LastManualInputTicks`
  - private `EnterManualCamera()`, `SnapCameraTo(CameraTransform)`, `NoteManualInput(long nowTicks)`
    (Task 7 `NoteManualInput`'a timer kurulumunu ekler; bu task'ta gövdesi yalnız tick kaydeder)
- Consumes: Task 3 sabitleri (`ManualMinScale/ManualMaxScale/WheelZoomStep`), `ClampPan`.

- [ ] **Step 1: Saf kırmızı testler**

`GraphCameraTests.cs`'e ekle:

```csharp
    // ---------------------------------------------------------------- [sinema] manuel jest aritmetiği

    [Fact]
    public void Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed()
    {
        var big = new Size(2000, 1000);
        var camera = new CameraTransform(1.0, -500, -300);
        var cursor = new Point(300, 200); // dünya: (800, 500)

        var zoomed = GraphCamera.ZoomAt(camera, cursor, GraphCamera.WheelZoomStep, new Size(600, 400), big);

        Assert.Equal(1.1, zoomed.Scale, 10);
        // İmleç altındaki dünya noktası sabit kalır; tx/ty piksele yuvarlandığı için ±0.5px band verilir.
        Assert.InRange((cursor.X - zoomed.Tx) / zoomed.Scale, 800 - 0.5, 800 + 0.5);
        Assert.InRange((cursor.Y - zoomed.Ty) / zoomed.Scale, 500 - 0.5, 500 + 0.5);
    }

    [Fact]
    public void Manual_zoom_is_clamped_to_the_manual_band()
    {
        var big = new Size(2000, 1000);
        var camera = new CameraTransform(GraphCamera.ManualMaxScale, -500, -300);

        Assert.Equal(GraphCamera.ManualMaxScale,
            GraphCamera.ZoomAt(camera, new Point(300, 200), GraphCamera.WheelZoomStep, new Size(600, 400), big).Scale);
        Assert.Equal(GraphCamera.ManualMinScale, GraphCamera.ZoomAt(
            new CameraTransform(GraphCamera.ManualMinScale, -10, -10),
            new Point(300, 200), 1 / GraphCamera.WheelZoomStep, new Size(600, 400), big).Scale);
        Assert.Equal(0.45, GraphCamera.ManualMinScale);
        Assert.Equal(2.0, GraphCamera.ManualMaxScale);
    }

    [Fact]
    public void Panning_moves_the_camera_and_stays_inside_the_12px_margins()
    {
        var big = new Size(2000, 1000);
        var camera = new CameraTransform(1.0, -500, -300);

        var panned = GraphCamera.Pan(camera, new Vector(40, -25), new Size(600, 400), big);
        Assert.Equal(-460, panned.Tx);
        Assert.Equal(-325, panned.Ty);

        // Kelepçe: dev bir delta 12px kenar payında durur (ClampPan tek kaynak — Compute ile aynı sınır).
        var clamped = GraphCamera.Pan(camera, new Vector(100000, 100000), new Size(600, 400), big);
        Assert.Equal(GraphCamera.PanMarginPx, clamped.Tx);
        Assert.Equal(GraphCamera.PanMarginPx, clamped.Ty);
    }
```

Run: `TEST(GraphCameraTests)` → derleme hatasıyla FAIL (`ZoomAt`/`Pan` yok).

- [ ] **Step 2: Saf implementasyon**

`GraphCamera.cs`:

```csharp
    /// <summary>[sinema] Manuel pan: delta ekran pikselidir; sonuç <see cref="ClampPan"/> sınırlarına oturur.</summary>
    public static CameraTransform Pan(CameraTransform camera, Vector delta, Size viewport, Size graph) =>
        ClampPan(viewport, graph, camera.Scale, camera.Tx + delta.X, camera.Ty + delta.Y);

    /// <summary>[sinema] İmleç merkezli zoom: imlecin altındaki DÜNYA noktası sabit kalır
    /// (w = (cursor − t)/s; t' = cursor − w·s'). Ölçek manuel banda kıstırılır.</summary>
    public static CameraTransform ZoomAt(
        CameraTransform camera, Point cursor, double factor, Size viewport, Size graph)
    {
        double scale = Math.Clamp(camera.Scale * factor, ManualMinScale, ManualMaxScale);
        double wx = (cursor.X - camera.Tx) / camera.Scale;
        double wy = (cursor.Y - camera.Ty) / camera.Scale;
        return ClampPan(viewport, graph, scale, cursor.X - wx * scale, cursor.Y - wy * scale);
    }
```

Run: `TEST(GraphCameraTests)` → PASS.

- [ ] **Step 3: Kablaj kırmızı testleri**

Yeni dosya `tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sinema] Manuel kamera jestleri: boş zeminde drag = pan (el imleci), wheel = imleç merkezli zoom,
/// eşik altı tıklama = seçim kaldırma (release'te). Jestler YALNIZ sinema modunda çalışır; küçük grafta
/// bugünkü down-anında seçim kaldırma birebir korunur (GraphClickTests onu pinlemeye devam eder).
/// Testler internal seam'leri (HandlePanStart/Move/End, HandleWheel) doğrudan sürer — headless'ta gerçek
/// mouse capture güvenilir değildir; event handler'lar bu seam'lerin ince kabuğudur.
/// </summary>
[Collection("Console UI (serial)")]
public class GraphPanZoomTests
{
    private static readonly Size Panel = new(600, 400);

    private static GraphView CinemaView(out IReadOnlyList<GraphNode> nodes)
    {
        nodes = GraphCinemaTests.BigNodes();
        var view = GraphTestView.Realized(Panel, labelFontFamily: DsResources.MonoFontFamily);
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        return view;
    }

    [StaFact]
    public void A_drag_beyond_the_threshold_pans_the_camera_and_enters_manual_mode()
    {
        var view = CinemaView(out _);
        var before = view.CurrentCamera;

        view.HandlePanStart(new Point(300, 200));
        view.HandlePanMove(new Point(300 + 40, 200 - 25)); // eşik (4px) aşıldı
        view.HandlePanEnd();

        Assert.True(view.IsManualCamera);
        Assert.Equal(GraphCamera.Pan(before, new Vector(40, -25), view.ViewportSize, view.GraphSize),
            view.CurrentCamera);
    }

    [StaFact]
    public void A_subthreshold_press_release_on_the_ground_clears_the_selection_without_entering_manual_mode()
    {
        var view = CinemaView(out _);
        view.SelectedNode = "N5";

        view.HandlePanStart(new Point(300, 200));
        view.HandlePanEnd(); // hareket yok → tıklama

        // Seçim kalkar (release'te — spec §3.4) ve manuel moda GİRİLMEZ. Kameranın kendisi seçim
        // kalktığı için normal otomatik hedefine döner (fit) — o davranış Task 4'te pinli.
        Assert.Null(view.SelectedNode);
        Assert.False(view.IsManualCamera);
    }

    [StaFact]
    public void The_wheel_zooms_at_the_cursor_and_enters_manual_mode()
    {
        var view = CinemaView(out _);
        var before = view.CurrentCamera;
        var cursor = new Point(300, 200);

        view.HandleWheel(cursor, 120);

        Assert.True(view.IsManualCamera);
        Assert.Equal(GraphCamera.ZoomAt(before, cursor, GraphCamera.WheelZoomStep, view.ViewportSize, view.GraphSize),
            view.CurrentCamera);
    }

    [StaFact]
    public void Manual_mode_suppresses_automatic_retargeting()
    {
        var view = CinemaView(out var nodes);
        view.HandleWheel(new Point(300, 200), 120);
        var manual = view.CurrentCamera;

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.Equal(manual, view.CurrentCamera); // frontier kamerayı ÇEKEMEZ — manuel mod askıda tutar
    }

    [StaFact]
    public void Gestures_are_inert_outside_cinema()
    {
        var nodes = GraphCinemaTests.BigNodes(36);
        var view = GraphTestView.Realized(Panel, labelFontFamily: DsResources.MonoFontFamily);
        view.SetGraph(nodes, GraphCinemaTests.ChainEdges(nodes));
        var before = view.CurrentCamera;

        view.HandleWheel(new Point(300, 200), 120);
        view.HandlePanStart(new Point(300, 200));
        view.HandlePanMove(new Point(340, 175));
        view.HandlePanEnd();

        Assert.False(view.IsManualCamera);
        Assert.Equal(before, view.CurrentCamera); // küçük graf: bugünkü davranış birebir
    }

    [StaFact]
    public void During_a_drag_the_ground_shows_the_hand_cursor_and_releases_it_after()
    {
        var view = CinemaView(out _);

        view.HandlePanStart(new Point(300, 200));
        view.HandlePanMove(new Point(340, 175));
        Assert.Equal(Cursors.Hand, view.GroundCursor);

        view.HandlePanEnd();
        Assert.Null(view.GroundCursor);
    }
}
```

Run: `TEST(GraphPanZoomTests)` → derleme hatasıyla FAIL (seam'ler yok).

- [ ] **Step 4: Kablaj implementasyonu**

`GraphView.xaml.cs`:

1. Alanlar:

```csharp
    // ---------------------------------------------------------------- [sinema] manuel kamera (jestler)
    private bool _manualCamera;
    private bool _panPressed;   // zeminde sol tuş basılı (henüz drag olmayabilir)
    private bool _dragging;     // eşik aşıldı — pan sürüyor
    private Point _panLast;     // ekran (Ground) koordinatı
    private long _lastManualInputTicks;
```

2. Ctor'daki `Ground.MouseLeftButtonDown += (_, _) => SelectedNode = null;` satırı kablo bloğuyla DEĞİŞİR
   (davranış: sinema dışı birebir eski — down'da seçim kalkar):

```csharp
        // [sinema] Jest kablosu. Sinema DIŞINDA down-anında seçim kaldırma birebir korunur (spec §3.4);
        // sinemada click-vs-drag ayrımı için kaldırma release'e taşınır. Handler'lar ince kabuktur —
        // mantık internal seam'lerde (HandlePan*/HandleWheel), STA testleri onları doğrudan sürer.
        Ground.MouseLeftButtonDown += (_, e) =>
        {
            if (!_cullEnabled) { SelectedNode = null; return; }
            HandlePanStart(e.GetPosition(Ground));
            Ground.CaptureMouse();
        };
        Ground.MouseMove += (_, e) => { if (_panPressed) HandlePanMove(e.GetPosition(Ground)); };
        Ground.MouseLeftButtonUp += (_, _) => { Ground.ReleaseMouseCapture(); HandlePanEnd(); };
        Ground.LostMouseCapture += (_, _) => HandlePanEnd();
        Ground.MouseWheel += (_, e) =>
        {
            if (!_cullEnabled) return;
            e.Handled = true;
            HandleWheel(e.GetPosition(Ground), e.Delta);
        };
```

3. Seam'ler + yardımcılar:

```csharp
    internal bool IsManualCamera => _manualCamera;
    internal long LastManualInputTicks => _lastManualInputTicks;
    internal Cursor? GroundCursor => Ground.Cursor;

    internal void HandlePanStart(Point position)
    {
        if (!_cullEnabled || !_hasCamera) return; // kamera henüz kurulmadıysa (boş/ölçülmemiş) jest yok
        _panPressed = true;
        _dragging = false;
        _panLast = position;
    }

    internal void HandlePanMove(Point position)
    {
        if (!_panPressed) return;

        if (!_dragging)
        {
            var delta = position - _panLast;
            // Platform drag eşiği: tıklama ile sürükleme ayrımı (spec §3.4).
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _dragging = true;
            EnterManualCamera();
            Ground.Cursor = Cursors.Hand; // el imleci — yalnız sürüklerken (spec §3.4)
        }

        var move = position - _panLast;
        _panLast = position;
        SnapCameraTo(GraphCamera.Pan(CurrentCamera, move, ViewportSize, GraphSize));
        UpdateMaterialization(); // manuel gezinme sırasında cull çalışmaya devam eder (spec §3.4)
    }

    internal void HandlePanEnd()
    {
        if (!_panPressed) return;
        _panPressed = false;
        Ground.ClearValue(CursorProperty);

        if (!_dragging)
        {
            SelectedNode = null; // eşik altı: bugünkü "boş alana tıkla → seçim kalkar" (release'te)
            return;
        }
        _dragging = false;
        NoteManualInput(Environment.TickCount64);
    }

    internal void HandleWheel(Point cursor, int delta)
    {
        if (!_cullEnabled || !_hasCamera) return; // ZoomAt mevcut kameradan türetir — kamera şart
        EnterManualCamera();
        double factor = delta > 0 ? GraphCamera.WheelZoomStep : 1 / GraphCamera.WheelZoomStep;
        SnapCameraTo(GraphCamera.ZoomAt(CurrentCamera, cursor, factor, ViewportSize, GraphSize));
        UpdateMaterialization();
        UpdateLabelVisibility(CurrentCamera.Scale); // zoom etiket kararını değiştirir
        NoteManualInput(Environment.TickCount64);
    }

    /// <summary>Manuel moda giriş: uçuştaki kamera animasyonu O ANKİ karede dondurulur, sonrası kullanıcıya
    /// aittir. Zaten manuelse hiçbir şey yapmaz.</summary>
    private void EnterManualCamera()
    {
        if (_manualCamera) return;
        _manualCamera = true;
        var live = LiveCamera;
        SnapCameraTo(live);
        UpdateFollowPill(); // Task 7'de gövde kazanır; bu task'ta boş bırakılır (aşağı bak)
    }

    /// <summary>Kamerayı ANİMASYONSUZ uygular — ApplyCamera'nın reduced-motion dalıyla AYNI yol (kopya yasak:
    /// o dal bu metodu çağıracak şekilde yeniden düzenlenir).</summary>
    private void SnapCameraTo(CameraTransform camera)
    {
        _cameraScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _cameraScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _cameraTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _cameraTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _cameraScale.ScaleX = camera.Scale;
        _cameraScale.ScaleY = camera.Scale;
        _cameraTranslate.X = camera.Tx;
        _cameraTranslate.Y = camera.Ty;
        CurrentCamera = camera;
        _hasCamera = true;
    }

    private void NoteManualInput(long nowTicks) => _lastManualInputTicks = nowTicks; // Task 7 timer ekler

    private void UpdateFollowPill() { } // Task 7 gövdeyi verir (pil XAML'i orada eklenir)
```

4. `ApplyCamera`'ya manuel koruma (viewport kontrolünden hemen sonra):

```csharp
        if (_manualCamera)
        {
            UpdateMaterialization(); // viewport büyümüş olabilir (SizeChanged) — cull yine de tarar
            return;                  // hedefleme YOK: kamera kullanıcıda (spec §3.5)
        }
```

5. `ApplyCamera`'nın animasyonsuz dalı `SnapCameraTo(camera)` çağrısına indirgenir (mevcut 8 satırlık
   BeginAnimation(null)+atama bloğu oraya TAŞINIR — kopya kalmaz). `ApplyCamera` zaten her iki daldan önce
   `CurrentCamera = camera; _hasCamera = true;` atadığından SnapCameraTo'nun aynı atamayı tekrarlaması
   idempotenttir ve zararsızdır.

6. `SetGraph` reset bloğuna: `_manualCamera = false; _panPressed = false; _dragging = false;`

- [ ] **Step 5: Yeşili gör + eski süit**

Run: `TEST(GraphPanZoomTests)` → PASS. `TEST(GraphClickTests)` → PASS (küçük graf down-yolu birebir korunur).
`TEST(GraphCinemaTests)`, `TEST(GraphRenderTests)`, `TEST(GraphCullTests)` → PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/BuildOrchestrator.App/Graph/GraphCamera.cs src/BuildOrchestrator.App/Graph/GraphView.xaml.cs tests/BuildOrchestrator.Tests/App/GraphCameraTests.cs tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs
git commit -m "feat(graph): drag/wheel manuel kamera" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Takip dönüşü (4 sn) + FOLLOW PAUSED pili

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (`TryResumeFollow`, timer, pil kablosu)
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml` (pil elemanı)
- Modify: `src/BuildOrchestrator.App/ViewModels/InteractionText.cs`, `src/BuildOrchestrator.App/AccessibilityNames.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs`

**Interfaces:**
- Consumes: Task 6 seam'leri (`HandleWheel`, `NoteManualInput`, `IsManualCamera`, `UpdateFollowPill` iskeleti),
  Task 3 `GraphCamera.FollowResumeDelayMs`.
- Produces: `internal bool TryResumeFollow(long nowTicks)`, `internal void ResumeFollowNow()`,
  `InteractionText.GraphFollowPaused = "FOLLOW PAUSED"`, `AccessibilityNames.GraphFollowPill`.

- [ ] **Step 1: Kırmızı testleri yaz**

`GraphPanZoomTests.cs`'e ekle:

```csharp
    // ---------------------------------------------------------------- takip dönüşü + pil (spec §3.5)
    // Statü değiştirme yardımcısı GraphCinemaTests.WithStatus'tur (fixture tek yerde — kopya yasak).

    [StaFact]
    public void Follow_resumes_only_after_the_delay_and_only_with_a_target()
    {
        var view = CinemaView(out var nodes);
        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building)); // hedef VAR (koşu)
        view.HandleWheel(new Point(300, 200), 120);      // manuel mod
        long t0 = view.LastManualInputTicks;

        Assert.False(view.TryResumeFollow(t0 + 3999));   // süre dolmadı
        Assert.True(view.IsManualCamera);

        Assert.True(view.TryResumeFollow(t0 + 4000));    // doldu → takip döner
        Assert.False(view.IsManualCamera);
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale); // frontier'e geri çerçeveledi
    }

    [StaFact]
    public void Manual_camera_persists_while_there_is_nothing_to_follow()
    {
        var view = CinemaView(out _);
        view.IsSettled = true;                            // hedef YOK (koşu bitti, seçim yok)
        view.HandleWheel(new Point(300, 200), 120);
        long t0 = view.LastManualInputTicks;

        Assert.False(view.TryResumeFollow(t0 + 100_000)); // süre GEÇSE de dönüş yok — kavga etmez (spec §3.5)
        Assert.True(view.IsManualCamera);
    }

    [StaFact]
    public void A_selection_counts_as_a_follow_target()
    {
        var view = CinemaView(out _);
        view.SelectedNode = "N5";
        view.HandleWheel(new Point(300, 200), 120);
        long t0 = view.LastManualInputTicks;

        Assert.True(view.TryResumeFollow(t0 + 4000));
        Assert.Equal(GraphCamera.SelectionScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void The_pill_shows_while_follow_is_suspended_and_click_resumes_immediately()
    {
        var view = CinemaView(out var nodes);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility); // başlangıç: gizli

        view.UpdateStatuses(GraphCinemaTests.WithStatus(nodes, "N0", GraphStatus.Building));
        view.HandleWheel(new Point(300, 200), 120);
        Assert.Equal(Visibility.Visible, view.FollowPillVisibility);   // hedef var + manuel → görünür

        view.ResumeFollowNow();                                        // pil tıklaması bunu çağırır
        Assert.False(view.IsManualCamera);
        Assert.Equal(Visibility.Collapsed, view.FollowPillVisibility);
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void The_pill_carries_the_shared_copy_and_the_uia_name()
    {
        var view = CinemaView(out _);

        Assert.Equal(BuildOrchestrator.App.ViewModels.InteractionText.GraphFollowPaused, view.FollowPillText);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.GraphFollowPill,
            System.Windows.Automation.AutomationProperties.GetName(view.FollowPillElement));
    }
```

Run: `TEST(GraphPanZoomTests)` → derleme hatasıyla FAIL.

- [ ] **Step 2: Implementasyon**

1. `InteractionText.cs` (graf bölümüne):

```csharp
    /// <summary>[sinema] Takip askıdayken graf başlığında görünen pil (GraphView) — tıklanınca takip döner.</summary>
    public const string GraphFollowPaused = "FOLLOW PAUSED";
```

2. `AccessibilityNames.cs` (graf bölümüne):

```csharp
    /// <summary>[sinema] FOLLOW PAUSED pili — tıklama takibi hemen döndürür.</summary>
    public const string GraphFollowPill = "Follow paused — resume automatic follow";
```

3. `GraphView.xaml` — kök elemana `xmlns:app="clr-namespace:BuildOrchestrator.App"` ekle; başlık
   `DockPanel`'inde `CountsText`'ten ÖNCE:

```xml
        <!-- [sinema] Takip askıdayken görünen pil; tıklama takibi hemen döndürür (spec §3.5).
             Pil dekoratif değil yedek bir kısayoldur: dönüş 4sn'de kendiliğinden de olur — klavye erişimi
             olmaması bilinen graf sınırının (§20) içinde kalır. -->
        <Border x:Name="FollowPill" DockPanel.Dock="Right" Visibility="Collapsed"
                VerticalAlignment="Center" Margin="0,0,10,0" Padding="7,2" CornerRadius="4"
                BorderThickness="1"
                Background="{DynamicResource Brush.SurfaceRaised}"
                BorderBrush="{DynamicResource Brush.Border}"
                Cursor="Hand"
                MouseLeftButtonDown="FollowPillMouseDown"
                AutomationProperties.Name="{x:Static app:AccessibilityNames.GraphFollowPill}">
          <controls:TrackedTextBlock x:Name="FollowPillLabel"
                                     Text="{x:Static vm:InteractionText.GraphFollowPaused}" />
        </Border>
```

4. `GraphView.xaml.cs`:

```csharp
    /// <summary>[sinema] Tek atımlık dönüş zamanlayıcısı — her manuel girdide yeniden kurulur (spec §3.5).</summary>
    private DispatcherTimer? _followResumeTimer;

    /// <summary>Takip edilecek bir hedef var mı: koşu sürüyor (building düğüm) VEYA seçim var.</summary>
    private bool HasFollowTarget
    {
        get
        {
            if (_selectedNode is not null) return true;
            foreach (var slot in _slotOrder)
                if (slot.Model.Status == GraphStatus.Building) return true;
            return false;
        }
    }

    /// <summary>[sinema] TEK dönüş kuralı (spec §3.5): hedef varken ve son manuel girdiden bu yana
    /// ≥ FollowResumeDelayMs geçmişse takip kamerayı geri alır. Timer tick'i, UpdateStatuses ve testler
    /// AYNI metodu çağırır (kopya yasak).</summary>
    internal bool TryResumeFollow(long nowTicks)
    {
        if (!_manualCamera) return false;
        if (!HasFollowTarget) return false;
        if (nowTicks - _lastManualInputTicks < (long)GraphCamera.FollowResumeDelayMs) return false;

        ResumeFollowNow();
        return true;
    }

    /// <summary>Pil tıklaması + dönüş kuralının ortak sonucu: manuel mod biter, kamera hedefine animasyonla
    /// (reduced-motion'da ani) döner.</summary>
    internal void ResumeFollowNow()
    {
        _followResumeTimer?.Stop();
        if (!_manualCamera) return;
        _manualCamera = false;
        UpdateFollowPill();
        ApplyCamera(animate: true);
    }

    private void FollowPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ResumeFollowNow();
    }
```

`NoteManualInput` timer kurar (Task 6'daki gövde genişler):

```csharp
    private void NoteManualInput(long nowTicks)
    {
        _lastManualInputTicks = nowTicks;
        UpdateFollowPill();

        // Tek atımlık dönüş tetiği. Koşu sürerken UpdateStatuses de aynı kuralı dener; timer, statü tick'i
        // OLMAYAN hedefleri (yalnız seçim varken) ve koşunun son anlarını kapatır.
        _followResumeTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GraphCamera.FollowResumeDelayMs),
        };
        _followResumeTimer.Stop();
        _followResumeTimer.Tick -= OnFollowResumeTick; // çifte abonelik olmaz
        _followResumeTimer.Tick += OnFollowResumeTick;
        _followResumeTimer.Start();
    }

    private void OnFollowResumeTick(object? sender, EventArgs e)
    {
        _followResumeTimer?.Stop();
        TryResumeFollow(Environment.TickCount64);
    }
```

`UpdateFollowPill` gövdesi (Task 6'daki boş iskelet dolar):

```csharp
    private void UpdateFollowPill() =>
        FollowPill.Visibility = _manualCamera && HasFollowTarget ? Visibility.Visible : Visibility.Collapsed;
```

`EnterManualCamera` zaten `UpdateFollowPill()` çağırıyor (Task 6). Şunlar eklenir:
- `UpdateStatuses` sonundaki `ApplyCamera(animate: true);` satırından ÖNCE:
  `TryResumeFollow(Environment.TickCount64); UpdateFollowPill();`
  (yeni koşu başladığında — 4 sn çoktan geçmişse — takip hemen devreye girer; girmediyse pil hedefe göre tazelenir).
- `SetGraph` reset bloğuna: `_followResumeTimer?.Stop(); UpdateFollowPill();` (manuel bayrak zaten sıfırlanıyor).
- `OnUnloadedReleaseClocks` içine: `_followResumeTimer?.Stop();` (M-d deseni — view ağaçtan düşünce timer uyanık kalmaz).
- Test görünürlük seam'leri (test/görünürlük bölümüne):

```csharp
    internal Visibility FollowPillVisibility => FollowPill.Visibility;
    internal string FollowPillText => FollowPillLabel.Text;
    internal UIElement FollowPillElement => FollowPill;
```

- [ ] **Step 3: Yeşili gör**

Run: `TEST(GraphPanZoomTests)` → PASS; `TEST(GraphCinemaTests)` → PASS.
Realize notu: pil, `GraphView.xaml`'in içindedir ve süitteki HER GraphView testi `InitializeComponent` ile bu
XAML'i gerçekten çözer; `The_pill_carries_the_shared_copy...` testi pil elemanının kurulduğunu, metnini ve UIA
adını ayrıca pinler — yeni ayrı bir pencere kökü açılmadığı için ayrı bir realize dosyası gerekmez.

- [ ] **Step 4: Tüm graf süiti**

Run: `TEST(Graph)` (tüm Graph* sınıfları) → PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/BuildOrchestrator.App/Graph/GraphView.xaml src/BuildOrchestrator.App/Graph/GraphView.xaml.cs src/BuildOrchestrator.App/ViewModels/InteractionText.cs src/BuildOrchestrator.App/AccessibilityNames.cs tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs
git commit -m "feat(graph): takip donusu + follow paused pili" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Doküman güncellemesi + tam süit

**Files:**
- Modify: `ARCHITECTURE.md` (§13.6 Graph renderer, §20 Known limits)
- Modify: `README.md` (graf jestleri — kullanım/kısayol bölümü)

**Interfaces:** yok (anlatı).

- [ ] **Step 1: ARCHITECTURE.md §13.6'yı yerinde yeniden yaz**

Kamera paragrafı yeni davranışı ANLATIR (changelog dili YOK, "eskiden şöyleydi" YOK). İçermesi gerekenler:

- Sinema modu tek kapı: cull/LOD ile aynı `FullDetailMaxNodes` eşiği; küçük grafta tüm davranış tam-detay
  bandındaki gibidir (yapısal garanti).
- Kamera: odak zinciri (selected → frontier COG → merkez) + ölçek politikası — frontier bbox çerçevesi
  0.85–1.4, seçimde 1.1, settled'da fit; ölçek için 0.05 Zeno eşiği; 460 ms geçiş.
- Kenar sisi: sinemada idle kenar 0.16 (seçim-dim ile aynı sabit), biten dallar 0.35, akan/hata/seçim tam.
- Etiketler: sinemada karar `aralık × hedef ölçek ≥ ölçülen genişlik`, histerezis 1.0/0.85, tembel kurulum,
  tooltip fallback.
- Jestler: boş zeminde drag = pan (el imleci, platform drag eşiği, click release'te seçim kaldırır),
  wheel = imleç merkezli zoom (0.45–2.0); manuel mod otomatik hedeflemeyi askıya alır; takip, hedef varken
  son girdiden 4 sn sonra (veya FOLLOW PAUSED piliyle hemen) döner; hedef yokken manuel kamera kalıcıdır.

- [ ] **Step 2: §20 Known limits**

"Graph nodes are not keyboard-accessible" maddesini genişlet: the FOLLOW PAUSED pill is likewise
pointer-only; automatic resume (4 s) is the non-pointer path. (İngilizce, tek cümle ekleme.)

- [ ] **Step 3: README**

Kullanım bölümüne graf jestleri (İngilizce, kısa): drag empty ground to pan (hand cursor), mouse wheel to
zoom at the cursor, follow resumes 4 s after the last manual input or immediately via the FOLLOW PAUSED pill;
only on large graphs (above the full-detail band).

- [ ] **Step 4: Tam süit + commit**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Expected: build clean, tüm süit PASS (token/motion/D8/İngilizce guard'ları dahil).

```powershell
git add ARCHITECTURE.md README.md
git commit -m "docs(graph): sinema modu anlatisi (kamera/sis/etiket/jest)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Görsel doğrulama (kullanıcıyla)**

Uygulamayı `dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` ile aç, gerçek OSYS
reposunda (177 proje) şunlara bak: sis silueti, build sırasında yakınlaşma, etiketlerin belirmesi, drag/wheel,
4 sn dönüş, pil. Kullanıcı onayı SONRASI merge (CLAUDE.md: merge + push + branch temizliği + `main`'de bitir).

---

## Bilinçli sınırlar (plan kapsamı)

- Kenar stilleri CANLI zoom değerine bağlanmaz (fast-path korunur) — sis durum+kapı kuralıdır.
- Pil klavye-erişilebilir değildir; §20 sınırının içinde belgelenir (dönüşün klavyesiz yolu 4 sn timer'dır).
- `FrontierScale` çok geniş cephede 0.85 tabanına kelepçelenir; cephenin tamamı o anda görünmeyebilir (COG
  merkezde kalır) — spec §3.1'de kabul edilmiş davranış.
- Manuel mod `_previousScale`/`_previousFocus`'u sıfırlamaz; dönüşte Zeno eşikleri normal işler.
