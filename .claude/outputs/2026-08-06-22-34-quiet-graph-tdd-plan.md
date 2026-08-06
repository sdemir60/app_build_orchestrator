# Quiet Graph — TDD uygulama planı (design v1.3.0 §2.3)

> **Agent'lar için:** ZORUNLU ALT-SKILL: bu planı task-task uygulamak için `superpowers:subagent-driven-development`
> (önerilen) ya da `superpowers:executing-plans` kullan. Adımlar checkbox (`- [ ]`) ile izlenir.

**Hedef:** Graf panelinin İÇİNİ design v1.3.0 §2.3'e göre sıfırdan yazmak — isimsiz mini node'lar, panele tam
sığan otomatik pitch, soluk/parlak koşu sistemi, beads, hover tooltip'i, seçimde odakla-sığdır.

**Mimari:** Panel içi tamamen yeniden yazılır; kamera transform altyapısı (`ScaleTransform`+`TranslateTransform`,
`Pan`/`ZoomAt`/`RoundPixels`) ve liste↔graf seçim kablosu
(`MainWindow.xaml.cs` `OnGraphSelectionChanged` / `PushGraphSelection`) KORUNUR. Yerleşim artık panel
boyutunun fonksiyonudur (`SizeChanged` → yeniden pitch → yeniden konum + yeniden boyut). Kenarlar kalıcı
değildir — yalnız seçim varken, seçili düğüme değen kenarlar kurulur. Ad yolu tek: hover tooltip'i +
seçim ad etiketi, ikisi de kamera transform'unun DIŞINDA, ekran koordinatlı bir overlay katmanında.

**Tech stack:** .NET 10 · WPF (net10.0-windows) · xUnit + `[StaFact]` · CommunityToolkit.Mvvm.

---

## TEK OTORİTE

| # | Dosya | Ne için |
|---|---|---|
| 1 | `.claude/outputs/2026-08-05-01-26-design-v1.3.0/README.md` | §2.3 (panelin tamamı), §3.3 (seçim modeli), §8 (yapılmayacaklar) |
| 2 | `.claude/outputs/2026-08-05-01-26-design-v1.3.0/prototype/app/BuildApp.jsx` | **Algoritmanın kendisi** (satır 241–491): `graphLayout`, `GraphPanel` |
| 3 | `.claude/outputs/2026-08-05-01-26-design-v1.3.0/Build Orchestrator (standalone).html` | Çalışan hâli — gözle doğrulama |

**GEÇERSİZ (okuma, referans verme):** `2026-08-05-12-02-graph-live-camera-design.md`,
`2026-08-05-12-27-graph-live-camera-implementation-plan.md`, `2026-08-06-10-06-graph-close-focus-design.md`,
`2026-08-06-13-33-graph-still-panel-plan.md`. Bunlar tarihsel kayıttır; v1.3.0 hepsini ezer.

**TEK İSTİSNA:** `2026-08-06-22-17-quiet-graph-implementation-plan.md` — YALNIZ §1 (söküm envanteri) ve §4
(riskler). Tasarım kararı için kullanılmaz.

---

## Global Constraints

Her task'ın gereksinimleri bu bölümü de kapsar.

- **Kırmızı test kuralı.** Hiçbir değişiklik, kuralı/kusuru yakalayan test KIRMIZI verdiği gösterilmeden
  yapılmaz. Yeni bir tip gerekiyorsa önce **derlenen ama `NotSupportedException` atan / boş dönen bir stub**
  yazılır ki kırmızı, derleme hatası değil GERÇEK bir assert hatası olsun.
- **Bilerek değişen kuralı pinleyen test SİLİNMEZ, yeniden yazılır.** Yeni kuralı pinleyecek biçimde
  yazılır ve XML doc'una **eski iddia + değişme gerekçesi (ölçüm ya da v1.3.0 §2.3 atfı)** yazılır.
  Eşik/bütçe gevşetmek YASAKTIR.
- **Kopya YASAK.** Aynı değer/metin/primitif iki yerde tanımlanmaz — ne kodda ne testlerde. Ortak
  fixture'lar `GraphTestView` / `SyntheticGraph` içindedir.
- **Kod, UI metinleri ve loglar İngilizce**; kod yorumları ve `.claude/` kayıtları Türkçe.
- **Renk/ms/hex literal YOK.** Renkler `Brush.*` anahtarlarından `SetResourceReference` ile; süreler ya
  `Duration.*`/`KeySpline.*` token'larından ya da **sahibi olan sınıfta adlandırılmış bir sabitten** gelir
  (`GraphCamera.TransitionMs` deseni). Guard'lar: `NoHardcodedColorTests`, `NoHardcodedMotionTests`.
- **Yeni XAML kökü/şablonu → realize testi ZORUNLU.** `Window.Measure/Arrange` HWND'siz içeriğe inmez —
  realize `window.Content` üzerinde yapılır.
- **Reduced-motion.** Her animasyon başlangıcında `AnimationsEnabledProvider()` TAZE okunur. Kapalıyken:
  beads yok, akan kesikler yok, kamera geçişi yok, açılış dalgası yok — konumlar ANINDA yerleşir.
- **Uygulama açıkken build alma** (çalışan Supervisor kendi binary'lerini kilitler).
- **Komutlar.** Build: `dotnet build BuildOrchestrator.slnx` · Test:
  `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
- **Taban:** 1904 passed / 2 skipped / 0 failed. Bitişte tam süit yeşil olacak; sayı bu plan boyunca
  DEĞİŞECEK (testler siliniyor/ekleniyor) — ölçüt "yeşil", "1904" değil.
- **Git.** İş branch'i: `feat/quiet-graph`. Task başına bir commit. Bitince `main`'e merge + push, merge
  doğrulandıktan sonra branch local+remote silinir, oturum `main` üzerinde biter.

### v1.3.0'ın taşıdığı sayılar (TEK kaynak — sabit adları Task'larda verilir)

| Değer | Nerede | Kaynak |
|---|---|---|
| pitch tarama 44 → 5, adım 0.5 | `QuietGraphLayout` | JSX:268 |
| bant boşluğu 0.7 × pitch | `QuietGraphLayout` | JSX:271, 282 |
| node = pitch × 0.6, kelepçe 8–24 | `QuietGraphLayout` | JSX:288 |
| kenar payı 12px, ipucu rezervi 18px | `QuietGraphLayout` | JSX:339, 341 |
| glyph = node × 0.52, stroke 1.8 | `GraphView` | JSX:447-448 |
| soluk 0.13 · biten 0.2 · odak dışı 0.1 | `GraphNodeOpacity` | JSX:422-426 |
| hold 2400ms / fade 700ms | `GraphNodeOpacity` | JSX:254, 426 |
| opaklık geçişi 280ms · renk geçişi 380ms | `GraphNodeOpacity` | JSX:421, 445 |
| beads yörünge 2.8px dışta, pad 6 | `GraphBeads` | JSX:380 |
| beads nokta adımı ≈3.4, min 8 nokta | `GraphBeads` | JSX:383 |
| beads turu 4200ms linear | `GraphBeads` | JSX:28 |
| beads giriş 420ms / çıkış 640ms ease-out | `GraphBeads` | JSX:458 |
| hover scale 1.7, 120ms ease-out | `GraphView` | JSX:442, 445 |
| tooltip 8px üstte, 6px panel kelepçesi | `GraphOverlay` | JSX:470-471 |
| seçim zoom kelepçesi 0.7–2.6, pad 3×node+48 | `GraphCamera` | JSX:352-353 |
| kamera geçişi 460ms ease-in-out | `GraphCamera` | JSX:399 |
| wheel 0.7–5.0, çarpan 1.14, 160ms ease-out | `GraphCamera` | JSX:330, 399 |
| seçim kenarı: dash 4 8 → offset −24, 640ms, 1.2px, op 0.75 | `SelectionEdgeStyle` | JSX:34, 413 |
| açılış dalgası: index × 9ms, tavan 520ms, 300ms + 5px | `GraphView` | JSX:29, 437 |

---

## Verilen karar: `ClampPan` SİLİNİR

İlk taslakta bu bir soru olarak duruyordu ("kalacak" listesindeki `ClampPan` korunsun mu"). Aritmetiği
yazınca kararı veri verdi: **korunamaz.**

Yeni dünyada tuval PANELİN KENDİSİDİR (graf her boyutta panele tam sığar, §2.3). `ClampPan`'in ilk klozu
"sığan eksen ORTALANIR"dır:

```
tx = scaledW <= viewport.Width ? (viewport.Width - scaledW) / 2 : clamp(...)
```

Seçim sığdırması ölçeği **0.7–2.6** bandına kıstırır. Ölçek 1'in ALTINDA kaldığı her seçimde (geniş bir
odak kümesi — çok deps/dependents'lı bir proje) `scaledW < viewport.Width` olur ve kelepçe ötelemeyi
**grafın tam merkezine zorlar** — yani odakla-sığdır hesabını tamamen ezer. Kamera seçili düğüme değil
grafın ortasına gider. Bu bir ayar meselesi değil, iki mekanizmanın birbirini yok etmesidir.

Tasarımın kendi kurtarma yolu zaten var ve kelepçeye gerek bırakmıyor: **boş alana tıkla → görünüm
varsayılana döner (zoom 1, pan 0, 460ms)** — `JSX:404`, §2.3 "Seçim" son maddesi. Sağ alttaki kalıcı ipucu
satırı da bu jesti duyurur. Yani kullanıcı grafı nereye sürüklerse sürüklesin tek tıkla geri gelir.

**Karar:**
- `GraphCamera.ClampPan` ve `PanMarginPx` **SİLİNİR** (prototipte karşılığı zaten yok).
- `Pan` ve `ZoomAt` **KALIR** — jest aritmetiği olarak hâlâ doğru ve test edilebilir; yalnız artık
  `ClampPan`'den geçmezler. `ZoomAt` **ölçek** kelepçesini (0.7–5.0) korur; öteleme kelepçesi yoktur.
- `FocusAndFit` prototiple BİREBİR: kelepçesiz, merkez ortalanır (§2.3 "merkez ortalanır" harfiyen).
- `RoundPixels` **KALIR** ama yalnız ANİMASYONLU uçlarda (`FocusAndFit`, varsayılan görünüm, `ZoomAt`);
  sürükleme kareleri (`Pan`) yuvarlanmaz — ara karede yuvarlamak titretir (mevcut `GraphCamera` doc'undaki
  A13.2 gerekçesi aynen geçerli).

> Bunun bedeli: kullanıcı grafı panelin dışına sürükleyebilir. Tasarım bunu bilerek kabul ediyor ve
> kurtarma jestini veriyor. Kelepçeyi tutmanın bedeli ise ÇALIŞMAYAN bir odakla-sığdır olurdu.

---

## Dosya yapısı

### Yeni (src)

| Dosya | Sorumluluk |
|---|---|
| `src/BuildOrchestrator.App/Graph/QuietGraphLayout.cs` | SAF: pitch taraması, katman bantları, satır-içi ortalama, blok ortalaması, node boyutu |
| `src/BuildOrchestrator.App/Graph/GraphNodeOpacity.cs` | SAF: koşu yaşam döngüsü opaklık kararı + hold/fade/glide süreleri |
| `src/BuildOrchestrator.App/Graph/GraphBeads.cs` | SAF: beads yörünge geometrisi (çevre, dash adımı) + zamanlamalar |
| `src/BuildOrchestrator.App/Graph/SelectionEdgeStyle.cs` | SAF: seçim kenarının bezier'i + akan kesik sabitleri |
| `src/BuildOrchestrator.App/Graph/GraphOverlay.cs` | SAF: ekran-koordinatlı tooltip / ad etiketi konum aritmetiği (kelepçe dâhil) |

### Değişen (src)

| Dosya | Ne olur |
|---|---|
| `Graph/GraphView.xaml` | `FollowPill` SÖKÜLÜR; `OverlayLayer` (transform'suz Canvas) + `HintText` EKLENİR |
| `Graph/GraphView.xaml.cs` | Panel içi yeniden yazılır (~1714 → hedef ≲900 satır) |
| `Graph/GraphCamera.cs` | Sinema/follow yüzeyi + `ClampPan`/`PanMarginPx` SÖKÜLÜR; `FocusAndFit` + yeni manuel band EKLENİR; `Pan`/`ZoomAt`/`RoundPixels` KALIR |
| `Graph/GraphNodeVisual.cs` | `Label`/`Badge*`/`PulseHost`/`IsPulsing`/`GraphEdgeSlot` SÖKÜLÜR; `Beads` EKLENİR |
| `Graph/GraphModels.cs` | `GraphNode.Prefix` + `ShortName` + `HasDepIssue` SÖKÜLÜR (`ShortLabel`/`CommonDotPrefix` KALIR — ProjectRow/StickyRibbon/RunViewModel okuyor) |
| `MainWindow.xaml.cs` | `IsSettled` itişleri SÖKÜLÜR; seçim kablosu (`:214`, `:584` civarı) DOKUNULMAZ |
| `AccessibilityNames.cs` | `GraphFollowPill` SÖKÜLÜR |
| `ViewModels/InteractionText.cs` | `GraphFollowPaused` SÖKÜLÜR; `GraphHintNavigate` + `GraphHintRelease` EKLENİR |
| `ARCHITECTURE.md` | §13.6 yeniden yazılır · §20 iki madde · §22 kod haritası |

### Silinen (src)

| Dosya | Gerekçe |
|---|---|
| `Graph/GraphLayout.cs` | Sabit 880px tuval / 96px satır / 26px node yerleşimi tamamen gitti. `EdgeCurve`/`BuildEdgeGeometry` `SelectionEdgeStyle`'a taşınır |
| `Graph/GraphLabelMetrics.cs` | Node üstü etiket yok → etiket genişliği ölçümü yok |
| `Graph/EdgeStyleResolver.cs` | Kalıcı kenar ağı yok; seçim kenarının TEK stili var (`SelectionEdgeStyle`) |
| `Graph/GraphCulling.cs` | **Cull artık hiçbir şeyi eleyemez** — graf her boyutta panele tam sığar, yani varsayılan görünümde her düğüm görünürdedir ve materyalizasyon tek yönlü olduğu için sonradan yakınlaşmak da bir şey kazandırmaz. Ayrıntı: test envanteri §1 "M8 neden ölü". `FullDetailMaxNodes` kapısı da onunla gider |

---

### Task 1: TEST ENVANTERİ

**Bu task bitmeden hiçbir kod değişmez.** Sökülecek davranışı ~4700 satır test pinliyor; hangisinin
yaşayacağı/yeniden yazılacağı/silineceği kararı tek yerde, YAZILI olarak verilir. Sonraki task'lar bu
envantere atıf yapar.

**Files:**
- Create: `.claude/outputs/2026-08-06-22-34-quiet-graph-test-inventory.md`
- Read (değiştirme): `tests/BuildOrchestrator.Tests/App/Graph*.cs`,
  `tests/BuildOrchestrator.Tests/App/EdgeStyleResolverTests.cs`,
  `tests/BuildOrchestrator.Tests/App/SyntheticGraph.cs`, `tests/BuildOrchestrator.Tests/App/GraphTestView.cs`,
  `tests/BuildOrchestrator.Tests/Graph/GraphBuilderTests.cs`,
  ve grafa DEĞEN komşular: `AccessibilityTests.cs`, `CopyTextTests.cs`, `IconGeometryTests.cs`,
  `MainWindowInputTests.cs`, `MotionOwnerHygieneTests.cs`, `ReducedMotionCoverageTests.cs`,
  `ShellLayoutTests.cs`, `StickyRevealTests.cs`, `SuccessFlourishTests.cs`, `UiResponsivenessBudgetTests.cs`

**Interfaces:**
- Consumes: (yok — ilk task)
- Produces: `quiet-graph-test-inventory.md` — Task 2–11 "hangi testi siliyorum/yeniden yazıyorum" sorusunu
  bu dosyadan cevaplar. Her satırda: dosya · test adı · **YAŞAR / YENİDEN YAZILIR / SİLİNİR** · gerekçe ·
  hangi task'ta ele alınacağı.

**Envanterin şablonu (her dosya için bir tablo):**

```markdown
### tests/BuildOrchestrator.Tests/App/GraphCameraTests.cs (368 satır)

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Scale_fits_the_graph_into_the_panel_with_the_30px_padding | SİLİNİR | FitScale kalktı: graf ARTIK panele pitch ile sığar, kamera sığdırmaz (v1.3.0 §2.3 "Pitch otomatik") | 8 |
| Panning_keeps_a_12px_margin_at_the_leading_edge | SİLİNİR | ClampPan silindi — tuval=panel olunca kelepçe, ölçek<1 seçimlerde odakla-sığdırı eziyordu (bkz. "Verilen karar") | 8 |
| Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed | YENİDEN YAZILIR | ZoomAt kalıyor ama band 0.45–2.0 → 0.7–5.0, adım 1.1 → 1.14 (v1.3.0 §2.3 "Serbest gezinme") | 9 |
```

**Kararın kuralı (envanterde her satır bu üçlüden birine düşer):**
1. **YAŞAR** — pinlediği davranış v1.3.0'da AYNEN geçerli (ClampPan aritmetiği, `GraphBinder` katman/kenar
   üretimi, `GraphCulling.VisibleWorldRect`, token/motion/erişilebilirlik guard'ları, `MotionGate` latch-first
   sözleşmesi, reveal hero mutex'i).
2. **YENİDEN YAZILIR** — kural bilerek değişti; test YENİ kuralı pinleyecek biçimde yazılır ve doc'una
   **eski iddia + değişme gerekçesi** eklenir (CLAUDE.md). Örn. wheel bandı, node boyutu, reveal gecikmesi,
   düğüm başına nesne tavanı.
3. **SİLİNİR** — pinlediği MEKANİZMA tasarımdan kalktı, yerine geçen bir kural YOK. Silme gerekçesi
   §2.3'e/§8'e atıfla yazılır. Örn. etiket LOD'u, kenar sisi, frontier follow, FOLLOW PAUSED pili.

**Envanterin sonunda ZORUNLU özet bölümü:**
- Dosya bazında toplam (kaç test yaşıyor / yeniden yazılıyor / siliniyor)
- **"Yerine hiçbir test gelmeyen davranışlar"** listesi — silinen her mekanizma için "bu davranış artık YOK,
  onu geri getirecek bir regresyon testi de yok" beyanı. Bu liste review'da tek tek okunur.
- **Ortak fixture kararı:** `GraphTestView` (89 satır) ve `SyntheticGraph` (131 satır) hangi hâliyle yaşıyor.
  (`GraphTestView.LabelFontFamily` seam'i etiket ölçümü için vardı → düşer; `Sized`/`Resize`/`Realized`
  üçlüsü YAŞAR ve Task 4'ün resize testlerinin ana yolu olur.)

- [ ] **Adım 1: Her graf test dosyasının test adlarını çıkar**

```powershell
Get-ChildItem tests/BuildOrchestrator.Tests/App/Graph*.cs, tests/BuildOrchestrator.Tests/App/EdgeStyleResolverTests.cs |
  ForEach-Object { $_.Name; Select-String -Path $_.FullName -Pattern '^\s+public (void|async Task) ' }
```

- [ ] **Adım 2: Grafa DEĞEN komşu dosyaları da tara** (yukarıdaki listedeki 10 dosya) — her birinde grafa
      bakan testleri ayrı bir "komşular" tablosuna yaz. Bunlar çoğunlukla YAŞAR ama üçü kesinlikle
      YENİDEN YAZILIR: `ReducedMotionCoverageTests` (nabız → beads), `SuccessFlourishTests`
      (`Graph_nodes_release_their_clocks_when_they_succeed` — nabız clock'u yerine beads clock'u),
      `UiResponsivenessBudgetTests` (statü tick bütçesi, yeni panel içi).

- [ ] **Adım 3: Envanter dosyasını yaz** (yukarıdaki şablon + zorunlu özet bölümü).

- [ ] **Adım 4: Kendi kendini denetle** — envanterdeki "SİLİNİR" satırlarının toplamı, `1. Ne sökülüyor`
      envanteriyle (`2026-08-06-22-17-quiet-graph-implementation-plan.md` §1) çelişmiyor mu? Çelişen her
      satır ya envanterde gerekçelendirilir ya düzeltilir.

- [ ] **Adım 5: Commit**

```bash
git checkout -b feat/quiet-graph
git add .claude/outputs/2026-08-06-22-34-quiet-graph-test-inventory.md
git commit -m "docs(graph): quiet graph test envanteri"
```

**Bu task'ın çıktısı koddur değil karardır — kod değişmediği için süit çalıştırılmaz.**

---

### Task 2: Görünmez panel — gizli grafta iş yapılmaz

**Kusur:** `MainWindow.xaml.cs:552` (`RebuildGraph`) ve `PushGraphStatuses` grafı view mode'a BAKMADAN besler;
`ShellRoot.xaml.cs:191` paneli yalnız `Collapsed` yapar. `list`/`focus` modunda panel görünmezken de her
200ms'de her düğümün stili + 1214 kenarın stili hesaplanır. **Bağımsız fix** — v1.3.0 rewrite'ından önce
gider ve sonrasında da geçerli kalır.

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphVisibilityTests.cs` (Create)

**Interfaces:**
- Consumes: `GraphView.SetGraph(IReadOnlyList<GraphNode>, IReadOnlyList<GraphEdge>)`,
  `GraphView.UpdateStatuses(IReadOnlyList<GraphNode>)`, `GraphView.NodeStatusApplyCount` (mevcut internal sayaç)
- Produces: `GraphView` artık **`Visibility != Visible` iken hiçbir O(N) görsel iş yapmaz**; en son besleme
  saklanır ve panel görünür olduğunda AYNI yoldan uygulanır. Task 4–9 bu kapıyı korur: `SetGraph` /
  `UpdateStatuses` gövdeleri `Apply*` metotlarına taşındığı için sonraki task'lar `Apply*`'ı değiştirir,
  kapıyı değil.

- [ ] **Adım 1: Kırmızı testi yaz**

```csharp
using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Gizli graf paneli (layout modu `list`/`focus`) statü akışını GÖRSELE çevirmez. Kusur: besleme yolu
/// (MainWindow.PushGraphStatuses) görünürlüğe bakmıyordu, ShellRoot ise paneli yalnız Collapsed yapıyordu —
/// yani panel ekranda yokken de her 200ms'de düğüm ve kenar stilleri hesaplanıyordu.
/// </summary>
public class GraphVisibilityTests
{
    private static IReadOnlyList<GraphNode> Nodes(GraphStatus status) =>
        [new("OSYS.Base", 0, status), new("OSYS.Data", 1, status)];

    private static IReadOnlyList<GraphEdge> Edges() => [new("OSYS.Base", "OSYS.Data")];

    [StaFact]
    public void A_hidden_panel_does_no_visual_work_when_statuses_are_pushed()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.SetGraph(Nodes(GraphStatus.Queued), Edges());
        view.Visibility = Visibility.Collapsed;
        int before = view.NodeStatusApplyCount;

        // Her tick GERÇEKTEN değişen bir statü iter — "değişmediyse dokunma" hızlı yolu testi maskeleyemesin.
        for (int tick = 0; tick < 10; tick++)
            view.UpdateStatuses(Nodes(tick % 2 == 0 ? GraphStatus.Building : GraphStatus.Succeeded));

        Assert.Equal(before, view.NodeStatusApplyCount);
    }

    [StaFact]
    public void A_panel_that_becomes_visible_again_shows_the_LATEST_status_not_the_one_it_was_hidden_with()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.SetGraph(Nodes(GraphStatus.Queued), Edges());
        view.Visibility = Visibility.Collapsed;

        view.UpdateStatuses(Nodes(GraphStatus.Building));
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded));
        view.Visibility = Visibility.Visible;

        Assert.Equal(GraphStatus.Succeeded, view.NodeVisuals["OSYS.Base"].Model.Status);
        Assert.Equal(GraphStatus.Succeeded, view.NodeVisuals["OSYS.Data"].Model.Status);
    }

    [StaFact]
    public void A_topology_that_arrives_while_hidden_is_built_when_the_panel_comes_back()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.Visibility = Visibility.Collapsed;

        view.SetGraph(Nodes(GraphStatus.Discovered), Edges());
        Assert.Equal(0, view.NodeCount);

        view.Visibility = Visibility.Visible;
        Assert.Equal(2, view.NodeCount);
    }
}
```

- [ ] **Adım 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~GraphVisibilityTests"
```
Beklenen: `A_hidden_panel_does_no_visual_work…` FAIL (`Assert.Equal() Failure: Expected: 2, Actual: 12` —
sayaç her tick'te düğüm başına artıyor) ve `A_topology_that_arrives_while_hidden…` FAIL
(`Expected: 0, Actual: 2` — gizliyken de kuruluyor).

- [ ] **Adım 3: Minimal implementasyon**

`GraphView.xaml.cs` — mevcut `SetGraph`/`UpdateStatuses` gövdeleri `ApplyGraph`/`ApplyStatuses` özel
metotlarına AYNEN taşınır, public metotlar kapıya dönüşür:

```csharp
    /// <summary>[quiet] En son gelen besleme, panel GİZLİYKEN saklanır. Gizli bir panelde düğüm/kenar stili
    /// hesaplamak saf israftır (statü akışı 200ms'de bir gelir); besleme kaybolmaz, panel görünür olunca
    /// AYNI yoldan uygulanır.</summary>
    private (IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)? _pendingTopology;
    private IReadOnlyList<GraphNode>? _pendingStatuses;

    private bool IsPanelVisible => Visibility == Visibility.Visible;

    public void SetGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        if (!IsPanelVisible) { _pendingTopology = (nodes, edges); _pendingStatuses = null; return; }
        ApplyGraph(nodes, edges);
    }

    public void UpdateStatuses(IReadOnlyList<GraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        // Bekleyen bir topoloji varsa statüler ONUN üstüne yazılır — sıra korunur (önce topoloji, sonra statü).
        if (!IsPanelVisible) { _pendingStatuses = nodes; return; }
        ApplyStatuses(nodes);
    }

    /// <summary>Görünürlük DEĞİŞİMİNİ yakalamanın headless'ta da çalışan tek yolu. <c>IsVisible</c> ve
    /// <c>IsVisibleChanged</c> KULLANILAMAZ: bağlı olmayan bir ağaçta <c>IsVisible</c> her zaman false'tur ve
    /// olay hiç ateşlenmez — testler ile üretim ayrışırdı. <c>Visibility</c> ise öğenin KENDİ özelliğidir
    /// (ShellRoot'un sürdüğü sinyalin ta kendisi) ve her iki ortamda da aynı davranır.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != VisibilityProperty || (Visibility)e.NewValue != Visibility.Visible) return;

        if (_pendingTopology is { } topology) { _pendingTopology = null; ApplyGraph(topology.Nodes, topology.Edges); }
        if (_pendingStatuses is { } statuses) { _pendingStatuses = null; ApplyStatuses(statuses); }
    }
```

- [ ] **Adım 4: Yeşili gör + tam süit**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: 3 yeni test PASS, geri kalan süit yeşil. `ShellLayoutTests` / `MainWindowInputTests`'in
`Visibility.Collapsed` iddiaları etkilenmez (kapı davranışı ekler, görünürlüğü değiştirmez).

- [ ] **Adım 5: Commit**

```bash
git add src/BuildOrchestrator.App/Graph/GraphView.xaml.cs tests/BuildOrchestrator.Tests/App/GraphVisibilityTests.cs
git commit -m "fix(graph): gizli panelde statü akisi gorsele cevrilmez"
```

---

### Task 3: `QuietGraphLayout` — otomatik pitch + katman bantları (SAF)

**En yüksek regresyon riski buradadır** (risk #1): yerleşim artık panel boyutunun fonksiyonu. Bu task
YALNIZ saf aritmetiği kurar; panele hiç dokunmaz. Task 4 onu view'a bağlar.

**Files:**
- Create: `src/BuildOrchestrator.App/Graph/QuietGraphLayout.cs`
- Test: `tests/BuildOrchestrator.Tests/App/QuietGraphLayoutTests.cs` (Create)

**Interfaces:**
- Consumes: `GraphNode(string Name, int Layer, GraphStatus Status, bool HasDepIssue)` — `GraphModels.cs`
- Produces:
  ```csharp
  public readonly record struct QuietLayoutResult(
      IReadOnlyDictionary<string, Point> Positions,  // İÇERİK koordinatında düğüm MERKEZLERİ
      double Pitch,
      double NodeSize,
      int Columns);

  public static class QuietGraphLayout
  {
      public const double MaxPitch = 44.0;
      public const double MinPitch = 5.0;
      public const double PitchStep = 0.5;
      public const double BandGapPitches = 0.7;
      public const double NodeSizeFactor = 0.6;
      public const double MinNodeSize = 8.0;
      public const double MaxNodeSize = 24.0;
      public const double ContentInset = 12.0;
      public const double HintReservePx = 18.0;
      public const double MinPanelWidth = 240.0;
      public const double MinPanelHeight = 160.0;

      public static Size ContentSize(Size panel);
      public static (double Pitch, int Columns) ResolvePitch(Size content, IReadOnlyList<int> bandCounts);
      public static QuietLayoutResult Compute(IReadOnlyList<GraphNode> nodes, Size panel);
  }
  ```
  Task 4 `Compute`'u, Task 8 `ContentInset`'i (dünya ofseti), Task 6 `NodeSize`'ı okur.

- [ ] **Adım 1: Derlenen stub'ı yaz** (kırmızı GERÇEK assert olsun diye)

```csharp
using System.Windows;

namespace BuildOrchestrator.App.Graph;

public readonly record struct QuietLayoutResult(
    IReadOnlyDictionary<string, Point> Positions, double Pitch, double NodeSize, int Columns);

public static class QuietGraphLayout
{
    public const double MaxPitch = 44.0;
    public const double MinPitch = 5.0;
    public const double PitchStep = 0.5;
    public const double BandGapPitches = 0.7;
    public const double NodeSizeFactor = 0.6;
    public const double MinNodeSize = 8.0;
    public const double MaxNodeSize = 24.0;
    public const double ContentInset = 12.0;
    public const double HintReservePx = 18.0;
    public const double MinPanelWidth = 240.0;
    public const double MinPanelHeight = 160.0;

    public static Size ContentSize(Size panel) => Size.Empty;
    public static (double Pitch, int Columns) ResolvePitch(Size content, IReadOnlyList<int> bandCounts) => (0, 0);
    public static QuietLayoutResult Compute(IReadOnlyList<GraphNode> nodes, Size panel) =>
        new(new Dictionary<string, Point>(), 0, 0, 0);
}
```

- [ ] **Adım 2: Kırmızı testleri yaz**

```csharp
using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Yerleşim — katman bantları" — prototype/app/BuildApp.jsx <c>graphLayout</c> (satır
/// 259-289) SAF portu. Graf HER panel boyutunda tam sığar; scrollbar yoktur.
///
/// <para><b>Eski iddia (GraphLayoutTests, artık geçersiz):</b> tuval 880px tabanlıydı, satır aralığı 96px
/// sabitti, node 26px'ti ve yerleşim panel boyutundan BAĞIMSIZDI. v1.3.0 §2.3 bunu ezdi: "Pitch (node adımı)
/// otomatik: 44px'ten 5'e 0.5 adımla taranır; tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine
/// sığan İLK değer seçilir."</para>
/// </summary>
public class QuietGraphLayoutTests
{
    private static IReadOnlyList<GraphNode> Bands(params int[] counts)
    {
        var nodes = new List<GraphNode>();
        for (int layer = 0; layer < counts.Length; layer++)
            for (int i = 0; i < counts[layer]; i++)
                nodes.Add(new GraphNode($"L{layer}.P{i:D3}", layer, GraphStatus.Discovered));
        return nodes;
    }

    /// <summary>Hesap alanı = panel − 2×12 kenar payı; yükseklikte ayrıca 18px mono ipucu rezervi
    /// (JSX:339-341: <c>graphLayout(W - 24, H - 24 - 18)</c>).</summary>
    [Fact]
    public void The_content_box_is_the_panel_minus_the_12px_inset_and_the_18px_hint_reserve()
    {
        var content = QuietGraphLayout.ContentSize(new Size(640, 360));
        Assert.Equal(616, content.Width, 3);
        Assert.Equal(318, content.Height, 3);
    }

    /// <summary>Panel çok küçükse hesap 240×160 tabanına oturur (JSX:339).</summary>
    [Fact]
    public void A_tiny_panel_floors_at_the_240_by_160_minimum()
    {
        var content = QuietGraphLayout.ContentSize(new Size(100, 80));
        Assert.Equal(240 - 24, content.Width, 3);
        Assert.Equal(160 - 24 - 18, content.Height, 3);
    }

    /// <summary>Bol yer varsa tarama İLK adımda (44) durur; sütun sayısı en kalabalık bandı ASLA aşmaz
    /// (JSX:269) — aşsaydı satır-içi ortalama ile blok ortalaması çakışırdı.</summary>
    [Fact]
    public void A_roomy_panel_keeps_the_44px_ceiling_and_never_exceeds_the_widest_band()
    {
        var result = QuietGraphLayout.Compute(Bands(10, 10, 10), new Size(640, 360));

        Assert.Equal(44, result.Pitch, 3);
        Assert.Equal(10, result.Columns);
    }

    /// <summary>Tarama 0.5 adımlıdır ve SIĞAN İLK değeri alır — tam sayıya yuvarlamaz. 6×40 düğüm,
    /// 640×360 panel: 21 ve üstü sığmaz, 20.5 sığar (15.5 satır-birimi × 20.5 = 317.75 ≤ 318).</summary>
    [Fact]
    public void The_pitch_scan_walks_down_in_half_pixel_steps_and_takes_the_FIRST_value_that_fits()
    {
        var result = QuietGraphLayout.Compute(Bands(40, 40, 40, 40, 40, 40), new Size(640, 360));

        Assert.Equal(20.5, result.Pitch, 3);
        Assert.Equal(30, result.Columns);
    }

    /// <summary>Node kenarı = pitch × 0.6, 8–24 kelepçesiyle (JSX:288).</summary>
    [Theory]
    [InlineData(44.0, 24.0)]   // 26.4 → tavan
    [InlineData(20.5, 12.3)]   // banttan
    [InlineData(5.0, 8.0)]     // 3.0 → taban
    public void The_node_edge_is_60_percent_of_the_pitch_clamped_to_8_and_24(double pitch, double expected)
        => Assert.Equal(expected, Math.Clamp(pitch * QuietGraphLayout.NodeSizeFactor,
            QuietGraphLayout.MinNodeSize, QuietGraphLayout.MaxNodeSize), 3);

    /// <summary>Hiçbir pitch sığmazsa taban (5) ve tek sütun kullanılır — graf yine de üretilir (JSX:267).</summary>
    [Fact]
    public void A_graph_that_fits_at_no_pitch_falls_back_to_the_5px_floor()
    {
        var result = QuietGraphLayout.Compute(Bands(Enumerable.Repeat(200, 40).ToArray()), new Size(300, 200));

        Assert.Equal(QuietGraphLayout.MinPitch, result.Pitch, 3);
        Assert.Equal(QuietGraphLayout.MinNodeSize, result.NodeSize, 3);
    }

    /// <summary>Bantlar derlenme sırasına göre: layer 0 en ÜSTTE, bant içi build-order SOLDAN SAĞA (§2.3).</summary>
    [Fact]
    public void Bands_run_top_down_by_layer_and_left_to_right_in_build_order()
    {
        var result = QuietGraphLayout.Compute(Bands(4, 4), new Size(640, 360));
        var p = result.Positions;

        Assert.True(p["L0.P000"].X < p["L0.P001"].X);
        Assert.True(p["L0.P001"].X < p["L0.P002"].X);
        Assert.True(p["L0.P000"].Y < p["L1.P000"].Y);
    }

    /// <summary>Bant boşluğu 0.7 × pitch: son satır ile sonraki bandın ilk satırı arasında 1.7 pitch var
    /// (JSX:282 — <c>rowCursor += rows + 0.7</c>, merkezler 0.5 offsetli).</summary>
    [Fact]
    public void The_gap_between_two_bands_is_zero_point_seven_pitches()
    {
        var result = QuietGraphLayout.Compute(Bands(4, 4), new Size(640, 360));

        double delta = result.Positions["L1.P000"].Y - result.Positions["L0.P000"].Y;
        Assert.Equal(1.7 * result.Pitch, delta, 3);
    }

    /// <summary>Bandın EKSİK kalan son satırı yatayda ORTALANIR (JSX:279) — kısa satırın orta noktası dolu
    /// satırınkiyle aynıdır. (Blok ortalaması ikisini de aynı miktarda kaydırdığı için karşılaştırma geçerli.)</summary>
    [Fact]
    public void An_incomplete_last_row_is_centred_against_the_full_row_above_it()
    {
        // 640px panelde 44 pitch → 14 sütun sığar ama bant 17 düğümlü: satır 1 = 14, satır 2 = 3.
        var result = QuietGraphLayout.Compute(Bands(17), new Size(640, 360));
        var p = result.Positions;

        double fullRowMid = (p["L0.P000"].X + p["L0.P013"].X) / 2;
        double shortRowMid = (p["L0.P014"].X + p["L0.P016"].X) / 2;
        Assert.Equal(fullRowMid, shortRowMid, 3);
    }

    /// <summary>Tüm blok hesap alanında ortalanır (JSX:284-287): merkezlerin sınır kutusunun ortası =
    /// hesap alanının ortası.</summary>
    [Fact]
    public void The_whole_block_is_centred_inside_the_content_box()
    {
        var panel = new Size(640, 360);
        var content = QuietGraphLayout.ContentSize(panel);
        var p = QuietGraphLayout.Compute(Bands(7, 5, 9), panel).Positions;

        double x0 = p.Values.Min(v => v.X), x1 = p.Values.Max(v => v.X);
        double y0 = p.Values.Min(v => v.Y), y1 = p.Values.Max(v => v.Y);
        Assert.Equal(content.Width / 2, (x0 + x1) / 2, 3);
        Assert.Equal(content.Height / 2, (y0 + y1) / 2, 3);
    }

    /// <summary>
    /// <b>Yerleşim ARTIK panel boyutunun fonksiyonudur</b> — bu planın en yüksek regresyon riski
    /// (bkz. §4.1). Aynı düğüm kümesi iki farklı panelde FARKLI pitch ve FARKLI konum üretir.
    ///
    /// <para><b>Eski iddia:</b> <c>GraphLayoutTests.Canvas_size_is_880_wide…</c> yerleşimin panelden bağımsız
    /// olduğunu pinliyordu. v1.3.0 §2.3 ("graf HER panel boyutunda tam sığar, scrollbar yok") onu ezdi.</para>
    /// </summary>
    [Fact]
    public void The_same_graph_lays_out_differently_in_a_different_panel_because_layout_is_a_function_of_size()
    {
        var nodes = Bands(40, 40, 40, 40, 40, 40);
        var wide = QuietGraphLayout.Compute(nodes, new Size(1200, 700));
        var narrow = QuietGraphLayout.Compute(nodes, new Size(640, 360));

        Assert.True(wide.Pitch > narrow.Pitch);
        Assert.NotEqual(wide.Positions["L0.P000"], narrow.Positions["L0.P000"]);
        Assert.True(wide.NodeSize > narrow.NodeSize);
    }

    /// <summary>Boş graf çökmez; sonuç boştur (SetGraph'ın 0 düğümlü yolu buradan geçer).</summary>
    [Fact]
    public void An_empty_graph_yields_an_empty_layout_instead_of_throwing()
    {
        var result = QuietGraphLayout.Compute([], new Size(640, 360));
        Assert.Empty(result.Positions);
    }

    /// <summary>Boş katman İNDEKSLERİ atlanır — bant listesi yalnız DOLU katmanlardan oluşur (JSX:264
    /// <c>groups = byLayer.filter(Boolean)</c>); aksi halde boş bir katman graf ortasında hayalet bir
    /// boşluk açardı.</summary>
    [Fact]
    public void An_empty_layer_index_produces_no_band_at_all()
    {
        IReadOnlyList<GraphNode> nodes =
        [
            new("A", 0, GraphStatus.Discovered),
            new("B", 5, GraphStatus.Discovered), // katman 1-4 YOK
        ];
        var result = QuietGraphLayout.Compute(nodes, new Size(640, 360));

        Assert.Equal(1.7 * result.Pitch, result.Positions["B"].Y - result.Positions["A"].Y, 3);
    }
}
```

- [ ] **Adım 3: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~QuietGraphLayoutTests"
```
Beklenen: 13 test FAIL, hepsi assert hatasıyla (`Expected: 616, Actual: 0` gibi) — derleme hatasıyla değil.

- [ ] **Adım 4: Minimal implementasyon**

```csharp
    public static Size ContentSize(Size panel) => new(
        Math.Max(MinPanelWidth, panel.Width) - 2 * ContentInset,
        Math.Max(MinPanelHeight, panel.Height) - 2 * ContentInset - HintReservePx);

    /// <summary>44'ten 5'e 0.5 adımla tarar, TÜM bantların + bant boşluklarının hesap yüksekliğine sığdığı
    /// İLK adımı döner. Sütun sayısı en kalabalık bandı aşmaz (JSX:269).</summary>
    public static (double Pitch, int Columns) ResolvePitch(Size content, IReadOnlyList<int> bandCounts)
    {
        ArgumentNullException.ThrowIfNull(bandCounts);
        if (bandCounts.Count == 0) return (MinPitch, 1);

        int widest = 1;
        foreach (int count in bandCounts) widest = Math.Max(widest, count);

        for (double pitch = MaxPitch; pitch >= MinPitch; pitch -= PitchStep)
        {
            int columns = Math.Max(1, Math.Min(widest, (int)Math.Floor(content.Width / pitch)));
            double rows = 0;
            foreach (int count in bandCounts) rows += Math.Ceiling(count / (double)columns);
            if ((rows + (bandCounts.Count - 1) * BandGapPitches) * pitch <= content.Height)
                return (pitch, columns);
        }
        return (MinPitch, 1);
    }

    public static QuietLayoutResult Compute(IReadOnlyList<GraphNode> nodes, Size panel)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var positions = new Dictionary<string, Point>(nodes.Count, StringComparer.Ordinal);
        if (nodes.Count == 0) return new QuietLayoutResult(positions, MinPitch, MinNodeSize, 1);

        // Bantlar: katman indeksine göre artan, bant İÇİNDE giriş (build) sırası korunur.
        var byLayer = new SortedDictionary<int, List<string>>();
        foreach (var node in nodes)
        {
            if (!byLayer.TryGetValue(node.Layer, out var band)) byLayer[node.Layer] = band = [];
            band.Add(node.Name);
        }
        var bands = byLayer.Values.ToList();

        var content = ContentSize(panel);
        var (pitch, columns) = ResolvePitch(content, [.. bands.Select(b => b.Count)]);

        double rowCursor = 0;
        for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            var band = bands[bandIndex];
            int rows = (int)Math.Ceiling(band.Count / (double)columns);
            for (int row = 0; row < rows; row++)
            {
                int start = row * columns;
                int count = Math.Min(columns, band.Count - start);
                // Eksik son satır yatayda ORTALANIR.
                double offsetX = (columns - count) / 2.0 * pitch;
                for (int column = 0; column < count; column++)
                    positions[band[start + column]] = new Point(
                        offsetX + (column + 0.5) * pitch,
                        (rowCursor + row + 0.5) * pitch);
            }
            rowCursor += rows + (bandIndex < bands.Count - 1 ? BandGapPitches : 0);
        }

        // Blok, hesap alanında ortalanır.
        double x0 = positions.Values.Min(p => p.X), x1 = positions.Values.Max(p => p.X);
        double y0 = positions.Values.Min(p => p.Y), y1 = positions.Values.Max(p => p.Y);
        double shiftX = content.Width / 2 - (x0 + x1) / 2;
        double shiftY = content.Height / 2 - (y0 + y1) / 2;
        foreach (string name in positions.Keys.ToList())
            positions[name] = new Point(positions[name].X + shiftX, positions[name].Y + shiftY);

        return new QuietLayoutResult(
            positions, pitch, Math.Clamp(pitch * NodeSizeFactor, MinNodeSize, MaxNodeSize), columns);
    }
```

- [ ] **Adım 5: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~QuietGraphLayoutTests"
```
Beklenen: 13 PASS.

- [ ] **Adım 6: Commit**

```bash
git add src/BuildOrchestrator.App/Graph/QuietGraphLayout.cs tests/BuildOrchestrator.Tests/App/QuietGraphLayoutTests.cs
git commit -m "feat(graph): otomatik pitch + katman bantlari yerlesimi (saf)"
```

---

### Task 4: View yerleşimi devralır — isimsiz mini node + resize'da yeniden hesap

Task 3'ün saf sonucu panele bağlanır. **Panelde başka hiçbir şeye dokunulmaz** (opaklık, beads, hover,
seçim kamerası sonraki task'lardadır). Bu task, kullanıcı emrindeki "Task 3 = otomatik pitch + yerleşim"in
ikinci yarısıdır ve ikisi yeşil olmadan panele başka hiçbir şey girmez.

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphNodeVisual.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphModels.cs`
- Modify: `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs`
- Delete: `src/BuildOrchestrator.App/Graph/GraphLabelMetrics.cs`
- Delete: `src/BuildOrchestrator.App/Graph/GraphCulling.cs`
- Test: `tests/BuildOrchestrator.Tests/App/QuietGraphNodeTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/App/GraphRealizationPerfTests.cs` (Modify — nesne tavanı)
- Test: `tests/BuildOrchestrator.Tests/App/GraphRenderTests.cs`,
  `GraphCullTests.cs`, `GraphCinemaTests.cs`, `GraphLayoutTests.cs` (envantere göre sil/yeniden yaz)
- Test: `tests/BuildOrchestrator.Tests/App/GraphTestView.cs` (Modify — `labelFontFamily` seam'i düşer)

**Interfaces:**
- Consumes: `QuietGraphLayout.Compute(nodes, panel)` → `QuietLayoutResult`; `QuietGraphLayout.ContentInset`
- Produces:
  ```csharp
  // GraphNodeVisual — Label/Badge*/PulseHost/IsPulsing SÖKÜLDÜ
  internal sealed class GraphNodeVisual
  {
      public required GraphNode Model { get; set; }
      public required Grid Cell { get; init; }          // açılış dalgasının hedefi (Task 9)
      public required GraphNodeBody Body { get; init; } // tıklama + opaklık + hover ölçeği hedefi
      public required Rectangle Square { get; init; }
      public required Rectangle SelectionRing { get; init; }
      public required Path Icon { get; init; }
      public Path? Beads { get; set; }                   // Task 6'da talep üzerine
      public Point Center { get; set; }                  // resize'da GÜNCELLENİR (init değil)
  }

  // GraphNodeSlot — Center artık `init` DEĞİL `set` (resize onu yeniden yazar); Bounds SÖKÜLDÜ (cull yok)
  internal sealed class GraphNodeSlot
  {
      public required GraphNode Model { get; set; }
      public required Point Center { get; set; }
      public required GraphNodeVisual Visual { get; init; }  // artık HER ZAMAN kurulur (tembel değil)
      // ShowsLabel SÖKÜLDÜ (etiket yok) · GraphEdgeSlot tipi Task 8'de tamamen SİLİNİR
  }

  // GraphView (internal test yüzeyi + yeni özel alanlar)
  private const double IconFactor = 0.52;      // §2.3: glyph node'un %52'si
  private const double IconStroke = 1.8;       // §2.3
  private const double RingInsetPx = 3.0;      // 2px outline + 2px offset − yarım kalem
  private readonly ScaleTransform _iconScale = new(1, 1);  // TÜM düğümlerde ORTAK: boyut hepsinde aynı,
                                                           // resize'da tek nesne mutasyonu yeter (donmaz)
  internal double NodeSize { get; }        // canlı pitch×0.6 kelepçeli
  internal double Pitch { get; }
  internal Point NodeCenter(string name);  // İÇERİK koordinatında
  internal int LayoutComputeCount { get; } // resize'ın kaç kez yerleşim hesapladığı (perf pini)
  ```
  Task 5–9 `NodeSize`/`NodeCenter`/`Cell`/`Body`/`Square`'i okur.

- [ ] **Adım 1: Kırmızı testleri yaz**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: node = İSİMSİZ mini kare (pitch×0.6, 8–24), radius-sm, 1.5px border, içinde
/// node'un %52'si kadar Lucide <c>box</c> glyph'i. Node üstü ad etiketleri ve graf içi dep-issue rozeti
/// KALDIRILDI (§2.3 "Kaldırılanlar"); ad artık hover tooltip'i ve seçim etiketiyle verilir.
/// </summary>
public class QuietGraphNodeTests
{
    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Succeeded),
        new("OSYS.Data", 1, GraphStatus.Failed, HasDepIssue: true),
        new("OSYS.Api", 2, GraphStatus.Queued),
    ];

    private static GraphView Built(Size size)
    {
        var view = GraphTestView.Realized(size);
        view.SetGraph(Nodes(), [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")]);
        return view;
    }

    /// <summary>Kare kenarı yerleşimin verdiği node boyutudur — 26px SABİT DEĞİL.
    /// <b>Eski iddia:</b> <c>GraphRenderTests.A_node_is_a_26px_square…</c>. v1.3.0 §2.3 ("Node = kare kutu,
    /// boyut = pitch×0.6 (8–24px kelepçe)") onu ezdi.</summary>
    [StaFact]
    public void The_node_square_is_the_layouts_node_size_not_a_fixed_26()
    {
        var view = Built(new Size(640, 400));
        var square = view.NodeVisuals["OSYS.Base"].Square;

        Assert.Equal(view.NodeSize, square.Width, 3);
        Assert.Equal(view.NodeSize, square.Height, 3);
        Assert.Equal(QuietGraphLayout.NodeSizeFactor * view.Pitch, view.NodeSize, 3);
    }

    /// <summary>Panel yeniden boyutlanınca yerleşim YENİDEN hesaplanır: node'lar hem YER hem BOYUT değiştirir.
    /// <b>Eski iddia:</b> yerleşim <c>SetGraph</c>'ta bir kez hesaplanır ve panel boyutundan bağımsızdır.</summary>
    [StaFact]
    public void Resizing_the_panel_recomputes_the_layout_so_nodes_move_AND_change_size()
    {
        var view = Built(new Size(1200, 700));
        double bigSize = view.NodeSize;
        var bigCenter = view.NodeCenter("OSYS.Api");

        GraphTestView.Resize(view, new Size(320, 200));
        view.UpdateLayout();

        Assert.True(view.NodeSize < bigSize);
        Assert.NotEqual(bigCenter, view.NodeCenter("OSYS.Api"));
        Assert.Equal(view.NodeSize, view.NodeVisuals["OSYS.Api"].Square.Width, 3);
    }

    /// <summary>Yeniden boyutlanma görselleri YENİDEN KURMAZ, yerinde günceller — splitter sürüklenirken
    /// saniyede onlarca SizeChanged gelir; her birinde 177 düğümü baştan inşa etmek panelin kendisini
    /// dondururdu.</summary>
    [StaFact]
    public void Resizing_updates_the_visuals_in_place_instead_of_rebuilding_them()
    {
        var view = Built(new Size(1200, 700));
        var square = view.NodeVisuals["OSYS.Base"].Square;

        GraphTestView.Resize(view, new Size(900, 500));
        view.UpdateLayout();

        Assert.Same(square, view.NodeVisuals["OSYS.Base"].Square);
    }

    /// <summary>Hiçbir düğümde ad etiketi kurulmaz (§2.3 "Kaldırılanlar").
    /// <b>Eski iddia:</b> <c>GraphRenderTests.The_node_label_is_the_short_name_in_10px_mono…</c> ve
    /// <c>GraphCullTests</c>'in etiket LOD testleri.</summary>
    [StaFact]
    public void No_node_carries_a_name_label_any_more()
    {
        var view = Built(new Size(640, 400));

        foreach (var visual in view.NodeVisuals.Values)
            Assert.Empty(LogicalTreeHelper.GetChildren(visual.Body).OfType<TextBlock>());
    }

    /// <summary>Graf içi dep-issue rozeti kaldırıldı — dep bilgisi kartlarda yaşıyor (§2.3 "Kaldırılanlar").
    /// <b>Eski iddia:</b> <c>GraphRenderTests.A_dep_issue_node_gets_a_13px_circle_badge…</c>.</summary>
    [StaFact]
    public void A_dep_issue_node_builds_no_badge_because_that_information_lives_on_the_cards()
    {
        var view = Built(new Size(640, 400));
        var visual = view.NodeVisuals["OSYS.Data"]; // HasDepIssue: true

        Assert.Empty(LogicalTreeHelper.GetChildren(visual.Body).OfType<Ellipse>());
        Assert.Single(LogicalTreeHelper.GetChildren(visual.Body).OfType<Path>()); // yalnız box glyph'i
    }

    /// <summary>Glyph node'un %52'si, 1.8px stroke, geometrisi Icons.xaml'den (kopya değil) — §2.3.</summary>
    [StaFact]
    public void The_box_glyph_is_52_percent_of_the_node_and_comes_from_the_icon_dictionary()
    {
        var view = Built(new Size(640, 400));
        var icon = view.NodeVisuals["OSYS.Base"].Icon;
        var expected = (Geometry)DsResources.Load("Icons.xaml")[GraphView.PackageIconKey];

        Assert.Same(expected, icon.Data);
        var box = (FrameworkElement)VisualTreeHelper.GetParent(icon);
        var scale = (ScaleTransform)box.RenderTransform;
        Assert.Equal(view.NodeSize * 0.52 / 24.0, scale.ScaleX, 4);
    }

    /// <summary>Graf artık HER düğümü kurar — tembel materyalizasyon yok. <b>Eski iddia:</b>
    /// <c>GraphCullTests.A_large_graph_only_builds_the_visual_tree_of_the_nodes_the_camera_can_see</c>;
    /// cull, graf panele tam sığdığı için hiçbir şeyi eleyemez hâle geldi (envanter §1/M8).</summary>
    [StaFact]
    public void A_thousand_node_graph_builds_every_node_because_the_whole_graph_is_on_screen()
    {
        var (nodes, edges) = SyntheticGraph.Build(1000, 8, 2.0);
        var view = GraphTestView.Realized(new Size(900, 520));
        view.SetGraph(nodes, edges);

        Assert.Equal(1000, view.NodeVisuals.Count);
    }
}
```

- [ ] **Adım 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~QuietGraphNodeTests"
```
Beklenen: 7 test FAIL. Örnek: `The_node_square_is_the_layouts_node_size…` → `Expected: 24, Actual: 26`;
`No_node_carries_a_name_label_any_more` → `Assert.Empty() Failure: Collection: [TextBlock]`;
`Resizing_the_panel_recomputes_the_layout…` → `Assert.NotEqual() Failure` (konum sabit kaldı).

- [ ] **Adım 3: Minimal implementasyon**

`GraphCulling.cs` **silinir** ve onunla birlikte `UpdateMaterialization`, `MaterializeNode`,
`MaterializeSelection`, `MaterializeByName`, `_scannedRegion`, `LiveCamera`, `_cullEnabled` ve
`FullDetailMaxNodes` gider. `ApplyGraph` artık her düğümün görselini doğrudan kurar.

`GraphView.xaml.cs` — `ApplyGraph` (Task 2'nin taşıdığı gövde) yerleşimi artık `QuietGraphLayout`'tan alır ve
`Ground.SizeChanged` yerleşimi YENİDEN hesaplar:

```csharp
    private QuietLayoutResult _layout = QuietGraphLayout.Compute([], new Size(0, 0));

    internal double NodeSize => _layout.NodeSize;
    internal double Pitch => _layout.Pitch;
    internal Point NodeCenter(string name) => _layout.Positions[name];
    internal int LayoutComputeCount { get; private set; }

    /// <summary>Dünya (çizim) koordinatı = içerik koordinatı + 12px kenar payı.</summary>
    private static Point ToWorld(Point content) =>
        new(content.X + QuietGraphLayout.ContentInset, content.Y + QuietGraphLayout.ContentInset);

    /// <summary>[quiet] Yerleşimi panel ölçüsünden YENİDEN hesaplar ve görselleri YERİNDE günceller
    /// (yeniden kurmaz). v1.3.0 §2.3: "graf HER panel boyutunda tam sığar" — dolayısıyla yerleşim artık
    /// SetGraph'ın değil PANEL ÖLÇÜSÜNÜN fonksiyonudur.</summary>
    private void Relayout()
    {
        if (_slotOrder.Count == 0) return;
        LayoutComputeCount++;

        var panel = ViewportSize;
        _layout = QuietGraphLayout.Compute([.. _slotOrder.Select(s => s.Model)], panel);
        // Dünya tuvali PANELİN kendisidir: zoom 1'de graf tam oturur, kamera ötelemesi 0'dır.
        World.Width = panel.Width;
        World.Height = panel.Height;
        _iconScale.ScaleX = _iconScale.ScaleY = _layout.NodeSize * IconFactor / IconViewBox;

        foreach (var slot in _slotOrder)
        {
            if (!_layout.Positions.TryGetValue(slot.Model.Name, out var center)) continue;
            slot.Center = center;
            PlaceNode(slot, slot.Visual);
        }
    }

    /// <summary>Tek düğümün konumunu ve boyutunu canlı yerleşimden uygular — kurulum ile resize AYNI yolu
    /// kullanır (kopya YASAK).</summary>
    private void PlaceNode(GraphNodeSlot slot, GraphNodeVisual visual)
    {
        double size = _layout.NodeSize;
        visual.Center = slot.Center;
        visual.Square.Width = visual.Square.Height = size;
        visual.SelectionRing.Width = visual.SelectionRing.Height = size + RingInsetPx * 2;
        visual.Body.Width = visual.Body.Height = size;
        var world = ToWorld(slot.Center);
        Canvas.SetLeft(visual.Cell, world.X - size / 2);
        Canvas.SetTop(visual.Cell, world.Y - size / 2);
    }
```
Kurucudaki kablo değişir:
```csharp
        Ground.SizeChanged += (_, _) => { Relayout(); ApplyCamera(animate: false); };
```
`BuildNodeVisual` sadeleşir: `ring + square + iconBox` → `Body`; etiket/rozet/nabız kabı YOK; `Cell` genişliği
`NodeCellWidth` değil canlı `NodeSize`. `EnsureLabel` / `ApplyLabelVisibility` / `UpdateLabelVisibility` /
`MeasureLayerLabelWidths` / `LayerSpacing` / `LayerLabelWidth` / `ShowsLabelFor` / `IsFocusExempt` /
`EnsureBadge` / `LabelFontFamily` / `_labelWidths` SİLİNİR. `GraphNode.Prefix` ve `ShortName` SİLİNİR;
`GraphBinder.Nodes` prefix hesabını bırakır (`CommonDotPrefix` diğer yüzeyler için `GraphModels`'ta KALIR).

- [ ] **Adım 4: Envanterin bu task'a düşen testlerini uygula**
      (`GraphLayoutTests` → silinir, `GraphRenderTests`'in node/etiket/rozet testleri → silinir veya
      `QuietGraphNodeTests`'e taşınır, `GraphCullTests`'in LOD testleri → silinir, `GraphCinemaTests`'in
      etiket muafiyeti testleri → silinir, `GraphCullingTests.Node_bounds…` ve `Edge_bounds…` → biri
      yeniden yazılır biri silinir). Her SİLME için envanterde gerekçe zaten yazılı.

- [ ] **Adım 5: Nesne tavanını yeniden ölç ve pinle**

`GraphRealizationPerfTests.A_graph_node_builds_no_more_than_the_per_node_object_ceiling` YENİDEN YAZILIR:
etiket + rozet + nabız kabı + Viewbox düştüğü için tavan düşer. Ölç, ölçülen değeri yaz, doc'una eski
tavanı ve düşme gerekçesini ekle. **Tavanı yukarı çekmek YASAK** — düşmesi gerekiyor.

- [ ] **Adım 6: Realize testi**

`GraphRealizationPerfTests.A_500_node_graph_realizes_in_a_real_window_through_the_app_resource_chain`
YAŞAR (yeni kök yok, ama yeni node ağacı var) — ek olarak **resize altında** realize edildiğini pinleyen bir
vaka eklenir: gerçek `Window`'un `Content`'i üzerinde 1200×700 → 640×360 yeniden boyutlandırılır ve
`UpdateLayout` sonrası hiçbir düğüm ölçüsüz kalmaz.

- [ ] **Adım 7: Tam süit**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

- [ ] **Adım 8: Commit**

```bash
git add src tests
git commit -m "feat(graph): isimsiz mini node + panel olcusune bagli yerlesim"
```

---

### Task 5: Koşu yaşam döngüsü — soluk/parlak opaklık sistemi

**Files:**
- Create: `src/BuildOrchestrator.App/Graph/GraphNodeOpacity.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphNodeOpacityTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/App/GraphRunLifecycleTests.cs` (Create)

**Interfaces:**
- Consumes: `GraphNodeVisual.Body`, `GraphView.AnimationsEnabledProvider`, `MotionTokens.ResolveKeySpline`
- Produces:
  ```csharp
  public enum GraphRunPhase { Idle, Running }

  public static class GraphNodeOpacity
  {
      public const double RunDim = 0.13;        // queued/discovered, koşarken
      public const double Finished = 0.2;       // biten, sönme sonrası
      public const double Unfocused = 0.1;      // seçim varken odak dışı
      public const double Full = 1.0;
      public const double HoldMs = 2400.0;
      public const double FadeMs = 700.0;
      public const double GlideMs = 280.0;      // normal opaklık geçişi
      public const double TintMs = 380.0;       // zemin/kenar/glyph renk geçişi

      public static double Resolve(
          GraphStatus status, GraphRunPhase phase, bool hasSelection, bool inFocus, bool hovered);
  }

  // GraphView
  public GraphRunPhase RunPhase { get; set; }              // setter tüm düğüm opaklıklarını yeniden uygular
  internal AnimationClock? OpacityClockOf(string nodeName); // test yüzeyi (SharedDashClock deseni)
  ```
  Task 7 `hovered`, Task 8 `hasSelection`/`inFocus` kolunu sürer.

- [ ] **Adım 1: Kırmızı saf testleri yaz**

```csharp
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Koşu yaşam döngüsü — soluk/parlak sistemi" — prototype BuildApp.jsx satır 421-429'un
/// SAF portu. Sıra bağlayıcıdır: seçim > koşu > hover (hover en sonda, her şeyi ezer).
/// </summary>
public class GraphNodeOpacityTests
{
    private static double Op(GraphStatus s, GraphRunPhase p, bool sel = false, bool focus = false, bool hov = false)
        => GraphNodeOpacity.Resolve(s, p, sel, focus, hov);

    /// <summary>idle/boot/sync: TÜMÜ tam opak (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Discovered)]
    [InlineData(GraphStatus.Queued)]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Skipped)]
    public void Everything_is_fully_opaque_while_the_graph_is_idle(GraphStatus status)
        => Assert.Equal(1.0, Op(status, GraphRunPhase.Idle), 3);

    /// <summary>Koşarken: queued/discovered 0.13, yalnız derlenenler tam opak (§2.3).</summary>
    [Fact]
    public void A_running_graph_fades_the_untouched_nodes_to_thirteen_percent_and_keeps_the_building_ones_bright()
    {
        Assert.Equal(0.13, Op(GraphStatus.Queued, GraphRunPhase.Running), 3);
        Assert.Equal(0.13, Op(GraphStatus.Discovered, GraphRunPhase.Running), 3);
        Assert.Equal(1.0, Op(GraphStatus.Building, GraphRunPhase.Running), 3);
    }

    /// <summary>Biten proje sonuç rengine döner ve nihayetinde 0.2'ye söner (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Skipped)]
    public void A_finished_node_settles_at_twenty_percent_while_the_run_continues(GraphStatus status)
        => Assert.Equal(0.2, Op(status, GraphRunPhase.Running), 3);

    /// <summary>Seçim koşu kararını EZER: odak kümesi tam opak, geri kalan HER ŞEY 0.1 (§2.3 "Seçim").</summary>
    [Fact]
    public void A_selection_overrides_the_run_system_entirely()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, sel: true, focus: true), 3);
        Assert.Equal(0.1, Op(GraphStatus.Building, GraphRunPhase.Running, sel: true, focus: false), 3);
        Assert.Equal(0.1, Op(GraphStatus.Succeeded, GraphRunPhase.Idle, sel: true, focus: false), 3);
    }

    /// <summary>Hover her şeyi ezer — soluk moddayken bile opaklık 1 (§2.3 "Hover").</summary>
    [Fact]
    public void Hover_wins_over_everything_including_the_selection_dim()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, hov: true), 3);
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, sel: true, focus: false, hov: true), 3);
    }
}
```

- [ ] **Adım 2: Kırmızıyı gör** — stub `Resolve` `0` döndüğü için tüm assert'ler patlar.

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~GraphNodeOpacityTests"
```

- [ ] **Adım 3: Saf implementasyon**

```csharp
    public static double Resolve(
        GraphStatus status, GraphRunPhase phase, bool hasSelection, bool inFocus, bool hovered)
    {
        // Sıra prototiple BİREBİR (BuildApp.jsx:421-429): seçim dalı koşu dalını EZER, hover en sonda.
        double opacity = Full;
        if (hasSelection) opacity = inFocus ? Full : Unfocused;
        else if (phase == GraphRunPhase.Running)
            opacity = status switch
            {
                GraphStatus.Building => Full,
                GraphStatus.Queued or GraphStatus.Discovered => RunDim,
                _ => Finished,
            };
        return hovered ? Full : opacity;
    }
```

- [ ] **Adım 4: Hold-fade'in view testlerini yaz (kırmızı)**

```csharp
/// <summary>
/// §2.3: "Proje bitince sonuç rengine döner ve 2400ms tam opak KALIR, sonra 700ms'de 0.2'ye söner."
/// CSS'te bu <c>transition: opacity 700ms ease-standard 2400ms</c>; WPF karşılığı BeginTime'lı TEK ATIMLIK
/// bir animasyondur (timer yok, ek render turu yok).
/// </summary>
public class GraphRunLifecycleTests
{
    [StaFact]
    public void A_node_that_leaves_building_holds_bright_for_2400ms_and_then_fades_to_0_2_over_700ms()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Building)], []);
        view.RunPhase = GraphRunPhase.Running;

        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded)]);

        var clock = view.OpacityClockOf("OSYS.Base");
        var animation = (DoubleAnimation)clock!.Timeline;
        Assert.Equal(GraphNodeOpacity.Full, animation.From);
        Assert.Equal(GraphNodeOpacity.Finished, animation.To);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.HoldMs), animation.BeginTime);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.FadeMs), animation.Duration.TimeSpan);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_finished_node_to_0_2_with_no_hold_and_no_animation()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => false);
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Building)], []);
        view.RunPhase = GraphRunPhase.Running;

        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded)]);

        Assert.Null(view.OpacityClockOf("OSYS.Base"));
        Assert.Equal(GraphNodeOpacity.Finished, view.NodeVisuals["OSYS.Base"].Body.Opacity, 3);
    }

    [StaFact]
    public void A_status_tick_that_changes_nothing_never_restarts_the_hold_fade()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Building)], []);
        view.RunPhase = GraphRunPhase.Running;
        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded)]);
        var first = view.OpacityClockOf("OSYS.Base");

        for (int i = 0; i < 5; i++) view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded)]);

        Assert.Same(first, view.OpacityClockOf("OSYS.Base"));
    }

    /// <summary>Koşu bitince (done/stopped) tümü sonuç renginde TAM OPAK olur — hold-fade iptal edilir (§2.3).</summary>
    [StaFact]
    public void Ending_the_run_brings_every_node_back_to_full_opacity_in_its_result_colour()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, GraphStatus.Queued)], []);
        view.RunPhase = GraphRunPhase.Running;
        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Succeeded), new("OSYS.Data", 1, GraphStatus.Skipped)]);

        view.RunPhase = GraphRunPhase.Idle;

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 3);
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data"].Body.Opacity, 3);
    }

    /// <summary>RİSK #4 ölçümü: 177 proje aynı tick'te biterse 177 tek-atımlık animasyon doğar. Tick'in
    /// kendisi UI bütçesinde kalmalı; ölçüm çıktıya yazılır (bayat sayı gömülmez, ölçüt bütçedir).</summary>
    [StaFact]
    [Trait("Category", "Perf")]
    public void A_whole_workspace_finishing_in_one_tick_stays_inside_the_ui_budget(ITestOutputHelper output)
    {
        var (nodes, edges) = SyntheticGraph.Build(177, 6, 2.2);
        var view = GraphTestView.Realized(new Size(900, 520), () => true);
        view.SetGraph([.. nodes.Select(n => n with { Status = GraphStatus.Building })], edges);
        view.RunPhase = GraphRunPhase.Running;

        var sw = Stopwatch.StartNew();
        view.UpdateStatuses([.. nodes.Select(n => n with { Status = GraphStatus.Succeeded })]);
        sw.Stop();

        output.WriteLine($"177 hold-fade animasyonu: {sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < StatusTickBudgetMs,
            $"statü tick'i bütçeyi aştı: {sw.Elapsed.TotalMilliseconds:F1} ms");
    }
}
```
> **İki ön koşul, task başında doğrulanır:**
> 1. `OpacityClockOf(string)` yeni bir test yüzeyidir — `GraphView`'ın mevcut `SharedDashClock` /
>    `ThinDashClock` internal'larıyla AYNI desende açılır (`UIElement.GetAnimationBaseValue` bir
>    `AnimationClock` vermez, bu yüzden clock view tarafında saklanır).
> 2. `StatusTickBudgetMs` **yeni bir sayı DEĞİLDİR**: `UiResponsivenessBudgetTests`'in statü tick'i için
>    zaten kullandığı bütçe sabitidir; adı ve konumu task başında o dosyadan okunur ve BURAYA kopyalanmaz
>    (kopya YASAK). Orada adlandırılmış bir sabit yoksa, önce oraya çıkarılır ve iki test onu paylaşır.

- [ ] **Adım 5: Kırmızıyı gör, sonra implementasyon**

`GraphView`'a `public GraphRunPhase RunPhase { get; set; }` eklenir (setter tüm düğümlerin opaklığını
yeniden uygular). `ApplyNodeStatus` içindeki `ApplyBuildingPulse` çağrısı **kaldırılır** (nabız → beads,
Task 6) ve yerine `ApplyNodeOpacity(visual, leavingBuilding)` gelir:

```csharp
    /// <summary>[quiet] §2.3 opaklık sistemi. Hold-fade YALNIZ building'den ÇIKIŞ anında doğar: CSS'in
    /// gecikmeli transition'ı da yalnız değer 1→0.2 değiştiğinde koşar. Sonraki tick'ler değeri zaten 0.2
    /// bulur ve hiçbir animasyon yeniden başlamaz (Zeno koruması).</summary>
    private void ApplyNodeOpacity(GraphNodeVisual visual, bool leftBuilding)
    {
        double target = GraphNodeOpacity.Resolve(
            visual.Model.Status, RunPhase, _selectedNode is not null,
            _focusSet.Contains(visual.Model.Name), string.Equals(_hoveredNode, visual.Model.Name, StringComparison.Ordinal));

        bool animate = AnimationsEnabledProvider();
        if (!animate)
        {
            visual.Body.BeginAnimation(OpacityProperty, null);
            visual.Body.Opacity = target;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        if (leftBuilding && RunPhase == GraphRunPhase.Running && _selectedNode is null)
        {
            var hold = new DoubleAnimation
            {
                From = GraphNodeOpacity.Full,
                To = target,
                BeginTime = TimeSpan.FromMilliseconds(GraphNodeOpacity.HoldMs),
                Duration = TimeSpan.FromMilliseconds(GraphNodeOpacity.FadeMs),
                EasingFunction = new KeySplineEase(spline),
                FillBehavior = FillBehavior.HoldEnd,
            };
            visual.Body.BeginAnimation(OpacityProperty, hold, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        visual.Body.BeginAnimation(OpacityProperty,
            MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(GraphNodeOpacity.GlideMs), spline),
            HandoffBehavior.SnapshotAndReplace);
    }
```
Renk geçişi (`TintMs` = 380ms ease-standard) `ApplyNodeStatus`'ta `MotionTokens.TransitionColor` deseniyle
uygulanır — fırça anahtarı yine `SetResourceReference`'tan gelir, hex yazılmaz.

`MainWindow.xaml.cs`: `Shell.GraphHost.IsSettled = …` satırları
`Shell.GraphHost.RunPhase = _vm.IsMidRunLocked ? GraphRunPhase.Running : GraphRunPhase.Idle;` olur.

- [ ] **Adım 6: Envanterin bu task'a düşen testleri** — `GraphRenderTests`'in nabız testleri
      (`A_building_node_breathes_1_to_half…`, `The_pulse_stops_when_the_node_leaves_building`,
      `Re_SetGraph_stops_the_pulse…`, `Reduced_motion_never_starts_the_building_pulse`) Task 6'ya devredilir
      (beads karşılıkları); `Selection_dims_every_non_neighbour_node_to_25_percent…` YENİDEN YAZILIR (0.25 →
      0.1) ve doc'una eski iddia + §2.3 atfı eklenir.

- [ ] **Adım 7: Tam süit + commit**

```bash
git add src tests
git commit -m "feat(graph): kosu yasam dongusu opaklik sistemi (hold-fade dahil)"
```

---

### Task 6: Beads — building animasyonu

**Files:**
- Create: `src/BuildOrchestrator.App/Graph/GraphBeads.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs`, `Graph/GraphNodeVisual.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphBeadsTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/App/ReducedMotionCoverageTests.cs` (Modify)
- Test: `tests/BuildOrchestrator.Tests/App/SuccessFlourishTests.cs` (Modify)

**Interfaces:**
- Consumes: `GraphView.NodeSize`, `GraphView.AnimationsEnabledProvider`, `GraphView.DecorativeFrameRate`
- Produces:
  ```csharp
  public readonly record struct BeadsGeometry(
      double Extent,      // SVG kutusunun kenarı = node + 2×Pad
      double Inset,       // yörünge rect'inin kutuya olan payı = Pad − OrbitGapPx
      double Side,        // yörünge rect'inin kenarı
      double CornerRadius,
      double Perimeter,
      double DashStep);

  public static class GraphBeads
  {
      public const double PadPx = 6.0;
      public const double OrbitGapPx = 2.8;    // node'un DIŞINDA
      public const double MaxCornerRadius = 6.8;
      public const double BeadSpacingPx = 3.4;
      public const int MinBeadCount = 8;
      public const double StrokeThickness = 1.0;
      public const double CycleMs = 4200.0;
      public const double FadeInMs = 420.0;
      public const double FadeOutMs = 640.0;
      public const double SpinAfterStopMs = 700.0;  // noktalar DÖNERKEN söner

      public static BeadsGeometry For(double nodeSize);
      public static DoubleCollection DashArrayFor(BeadsGeometry geometry);
  }

  // GraphNodeVisual
  public Path? Beads { get; set; }             // TALEP ÜZERİNE (ilk building'de) kurulur, sökülmez

  // GraphView (test yüzeyi)
  internal AnimationClock? BeadsClock { get; } // TÜM beads'in paylaştığı TEK clock (null = hiç dönmüyor)
  internal BeadsGeometry BeadsGeometry { get; }
  ```

- [ ] **Adım 1: Kırmızı saf testleri yaz**

```csharp
/// <summary>
/// design v1.3.0 §2.3 "Building animasyonu — beads" — prototype BuildApp.jsx satır 379-384'ün SAF portu.
/// Nokta deseni ÇEVREYE TAM BÖLÜNÜR; ek yerinde bindirme olmaz.
/// </summary>
public class GraphBeadsTests
{
    /// <summary>Yörünge node'un 2.8px DIŞINDADIR: kutu = node + 12, iç pay = 6 − 2.8 = 3.2 (JSX:380-381).</summary>
    [Fact]
    public void The_orbit_sits_2_8px_outside_the_node()
    {
        var g = GraphBeads.For(24);

        Assert.Equal(36, g.Extent, 3);       // 24 + 2×6
        Assert.Equal(3.2, g.Inset, 3);       // 6 − 2.8
        Assert.Equal(29.6, g.Side, 3);       // 36 − 2×3.2
    }

    /// <summary>Köşe yarıçapı 6.8'de tavanlanır, dar yörüngede yarım kenara iner (JSX:381).</summary>
    [Theory]
    [InlineData(24, 6.8)]
    [InlineData(8, 6.8)]     // side = 13.6 → yarısı 6.8
    [InlineData(3, 4.3)]     // side = 8.6 → yarısı 4.3, tavanın altında
    public void The_corner_radius_is_half_the_side_capped_at_6_8(double nodeSize, double expected)
        => Assert.Equal(expected, GraphBeads.For(nodeSize).CornerRadius, 3);

    /// <summary>Çevre = yuvarlatılmış karenin gerçek çevresi: 4·side − 8·r + 2πr (JSX:382).</summary>
    [Fact]
    public void The_perimeter_is_the_rounded_square_perimeter()
    {
        var g = GraphBeads.For(24);
        Assert.Equal(4 * g.Side - 8 * g.CornerRadius + 2 * Math.PI * g.CornerRadius, g.Perimeter, 6);
    }

    /// <summary>Adım = çevre / round(çevre/3.4), en az 8 nokta ⇒ desen çevreye TAM bölünür (JSX:383).</summary>
    [Fact]
    public void The_dash_step_divides_the_perimeter_a_whole_number_of_times()
    {
        var g = GraphBeads.For(24);

        double count = g.Perimeter / g.DashStep;
        Assert.Equal(Math.Round(count), count, 6);
        Assert.True(count >= GraphBeads.MinBeadCount);
    }

    /// <summary>Çok küçük node'da bile en az 8 nokta vardır — aksi halde desen "noktalar" değil "çizgiler"
    /// olurdu (JSX:383 <c>Math.max(8, …)</c>).</summary>
    [Fact]
    public void A_tiny_node_still_carries_at_least_eight_beads()
        => Assert.True(GraphBeads.For(8).Perimeter / GraphBeads.For(8).DashStep >= GraphBeads.MinBeadCount);

    /// <summary>Dash deseni = <c>0.01 (adım − 0.01)</c> — 1px kalınlıkta WPF'in çarpan birimi mutlak px'e
    /// eşittir, dolayısıyla SVG değerleri BİREBİR taşınır (JSX:384).</summary>
    [Fact]
    public void The_dash_pattern_is_a_hairline_dot_followed_by_the_rest_of_the_step()
    {
        var g = GraphBeads.For(24);
        var dash = GraphBeads.DashArrayFor(g);

        Assert.Equal(2, dash.Count);
        Assert.Equal(0.01, dash[0], 6);
        Assert.Equal(g.DashStep - 0.01, dash[1], 6);
        Assert.True(dash.IsFrozen);
    }
}
```

- [ ] **Adım 2: Kırmızıyı gör** → stub `For` sıfırlarla dönerken hepsi patlar.

- [ ] **Adım 3: Saf implementasyon**

```csharp
    public static BeadsGeometry For(double nodeSize)
    {
        double extent = nodeSize + PadPx * 2;
        double inset = PadPx - OrbitGapPx;
        double side = extent - inset * 2;
        double radius = Math.Min(side / 2, MaxCornerRadius);
        double perimeter = 4 * side - 8 * radius + 2 * Math.PI * radius;
        double step = perimeter / Math.Max(MinBeadCount, Math.Round(perimeter / BeadSpacingPx));
        return new BeadsGeometry(extent, inset, side, radius, perimeter, step);
    }

    public static DoubleCollection DashArrayFor(BeadsGeometry geometry)
    {
        var dash = new DoubleCollection([0.01, geometry.DashStep - 0.01]);
        dash.Freeze();
        return dash;
    }
```
> `Math.Round` burada JS `Math.round` ile ayrışabilir (banker's rounding). `GraphCamera.RoundPixels`'ın
> gerekçesi aynıdır — `Math.Round(x, MidpointRounding.AwayFromZero)` KULLANILIR ve doc'una not düşülür.

- [ ] **Adım 4: View testlerini yaz (kırmızı)**

```csharp
/// <summary>
/// §2.3 "Building animasyonu — beads" — WPF kablajı. <b>Eski iddia (GraphRenderTests, artık geçersiz):</b>
/// building düğüm DS <c>ds-node-pulse</c> ile 1.6s'de 1→0.5→1 nefes alırdı. v1.3.0 §2.3 nabzı KALDIRDI ve
/// yerine node'un 2.8px dışında dolanan amber noktaları koydu.
/// </summary>
public class GraphBeadsWiringTests
{
    private static GraphView Running(Func<bool> motion, params GraphStatus[] statuses)
    {
        var view = GraphTestView.Realized(new Size(640, 400), motion);
        view.SetGraph([.. statuses.Select((s, i) => new GraphNode($"P{i}", i % 3, s))], []);
        view.RunPhase = GraphRunPhase.Running;
        return view;
    }

    /// <summary>Beads TALEP ÜZERİNE kurulur — building olmayan düğüm hiç <c>Path</c> taşımaz.</summary>
    [StaFact]
    public void Only_a_building_node_builds_its_beads_orbit()
    {
        var view = Running(() => true, GraphStatus.Building, GraphStatus.Queued);

        Assert.NotNull(view.NodeVisuals["P0"].Beads);
        Assert.Null(view.NodeVisuals["P1"].Beads);
    }

    /// <summary>RİSK #3: N paralel derlemede N ayrı sonsuz animasyon DEĞİL, TEK paylaşımlı clock. Tüm
    /// node'lar aynı boyutta olduğu için çevre de aynıdır ⇒ tek clock faz-kilitli çalışır.</summary>
    [StaFact]
    public void Every_beads_orbit_hangs_off_ONE_shared_clock_no_matter_how_many_nodes_build()
    {
        var view = Running(() => true, GraphStatus.Building, GraphStatus.Building, GraphStatus.Building);

        var clock = view.BeadsClock;
        Assert.NotNull(clock);
        var animation = (DoubleAnimation)clock.Timeline;
        Assert.Equal(0.0, animation.From);
        Assert.Equal(-view.BeadsGeometry.Perimeter, animation.To!.Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphBeads.CycleMs), animation.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, animation.RepeatBehavior);
        Assert.Equal(GraphView.DecorativeFrameRate, Timeline.GetDesiredFrameRate(animation));
    }

    /// <summary>Giriş 420ms, çıkış 640ms ease-out (§2.3).</summary>
    [StaFact]
    public void The_orbit_fades_in_over_420ms_and_out_over_640ms()
    {
        var view = Running(() => true, GraphStatus.Building);
        Assert.Equal(GraphBeads.FadeInMs, view.BeadsFadeMsOf("P0"), 3);

        view.UpdateStatuses([new("P0", 0, GraphStatus.Succeeded)]);
        Assert.Equal(GraphBeads.FadeOutMs, view.BeadsFadeMsOf("P0"), 3);
    }

    /// <summary>§2.3: "Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup
    /// kaybolmaz." Son building düğüm bittiğinde clock ANINDA bırakılmaz.</summary>
    [StaFact]
    public void The_dash_clock_keeps_spinning_for_700ms_after_the_last_node_stops_building()
    {
        var view = Running(() => true, GraphStatus.Building);
        view.UpdateStatuses([new("P0", 0, GraphStatus.Succeeded)]);

        Assert.NotNull(view.BeadsClock);                       // hâlâ dönüyor
        view.HandleBeadsSpindownTick(GraphBeads.SpinAfterStopMs);
        Assert.Null(view.BeadsClock);                          // ...ve 700ms sonra bırakılıyor
    }

    /// <summary>Panel yeniden boyutlanınca node boyutu → çevre değişir ⇒ desen ve clock YENİDEN kurulur
    /// (Task 4 ile kesişen regresyon: sabit çevre varsayımı burada kırılır).</summary>
    [StaFact]
    public void Resizing_the_panel_rebuilds_the_pattern_because_the_orbit_perimeter_changed()
    {
        var view = Running(() => true, GraphStatus.Building);
        double before = view.BeadsGeometry.Perimeter;

        GraphTestView.Resize(view, new Size(300, 200));
        view.UpdateLayout();

        Assert.NotEqual(before, view.BeadsGeometry.Perimeter, 3);
        Assert.Equal(GraphBeads.DashArrayFor(view.BeadsGeometry), view.NodeVisuals["P0"].Beads!.StrokeDashArray);
    }

    /// <summary>Reduced-motion: beads HİÇ kurulmaz ve clock doğmaz (§2.3 son madde).</summary>
    [StaFact]
    public void Reduced_motion_builds_no_beads_at_all()
    {
        var view = Running(() => false, GraphStatus.Building);

        Assert.Null(view.NodeVisuals["P0"].Beads);
        Assert.Null(view.BeadsClock);
    }

    /// <summary>Yeni topoloji ve unload paylaşımlı clock'u bırakır (mevcut dash-clock hijyeninin eşi) —
    /// aksi halde timing engine, view ağaçta olmasa bile 30fps'te uyanık kalırdı.</summary>
    [StaFact]
    public void A_new_topology_and_an_unload_both_release_the_shared_clock() { /* … */ }

    /// <summary>RİSK #3 ölçümü: 32 paralel building düğümde statü tick'i bütçede kalıyor mu.</summary>
    [StaFact]
    [Trait("Category", "Perf")]
    public void Thirty_two_parallel_builds_stay_inside_the_ui_budget(ITestOutputHelper output) { /* … */ }
}
```
> `BeadsFadeMsOf(string)` ve `HandleBeadsSpindownTick(double)` yeni internal test seam'leridir; mevcut
> `HandleFollowResumeTick(long)` deseninin eşidir (zaman geçişi testte SÜRÜLÜR, gerçek timer beklenmez).

- [ ] **Adım 5: Implementasyon** — `_beadsClock` alanı + `EnsureBeads(visual)` (bir kez kurar, sökülmez) +
      `ReleaseBeadsClock()`; mevcut `_dashClockRoot`/`DashClockFor` deseninin BİREBİR eşi, kopya değil
      taşınmış hâli (`ApplyBuildingPulse`/`StopPulse` bu task'ta silinir).

- [ ] **Adım 6: Tam süit + commit**

```bash
git add src tests
git commit -m "feat(graph): beads building animasyonu (tek paylasimli clock)"
```

---

### Task 7: Hover + ekran-koordinatlı tooltip

**Files:**
- Create: `src/BuildOrchestrator.App/Graph/GraphOverlay.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml` (OverlayLayer), `GraphView.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphOverlayTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/App/GraphHoverTests.cs` (Create)

**Interfaces:**
- Consumes: `CameraTransform`, `QuietGraphLayout.ContentInset`, `GraphView.NodeSize`
- Produces:
  ```csharp
  public static class GraphOverlay
  {
      public const double TooltipGapPx = 8.0;
      public const double EdgeClampPx = 6.0;
      public const double TooltipRisePerNode = 0.9;   // node üstü: size × 0.9 × zoom
      public const double LabelDropPerNode = 0.95;    // node altı
      public const double LabelGapPx = 6.0;
      public const double LabelBottomReservePx = 26.0;

      /// <summary>Tooltip'in ANKRAJI (ekran koordinatı): X = ok ucu, Y = kutunun ALT kenarı.</summary>
      public static Point TooltipAnchor(Point contentCenter, CameraTransform camera, double nodeSize, Size panel);

      /// <summary>Seçim ad etiketinin SOL-ÜST köşesi; yatayda ölçülen genişlikle panele kelepçelenir.</summary>
      public static Point NameLabelTopLeft(
          Point contentCenter, CameraTransform camera, double nodeSize, Size panel, Size labelSize);
  }

  // GraphView (yeni sabit + test yüzeyi)
  public const double HoverScale = 1.7;
  public const double HoverBorderThickness = 2.0;
  public const double HoverScaleMs = 120.0;
  internal void SetHoverForTest(string? nodeName);
  internal Visibility TooltipVisibility { get; }
  internal string TooltipText { get; }
  internal Point TooltipTopLeft { get; }
  internal Size TooltipRenderSize { get; }
  internal Border TooltipElement { get; }
  internal Transform? OverlayLayerTransform { get; }   // HER ZAMAN null — RİSK #5'in yapısal kanıtı
  ```
  Task 8 `NameLabelTopLeft`'i ve `SetHoverForTest`'i kullanır.

- [ ] **Adım 1: Kırmızı saf testleri yaz** — ekran dönüşümü, 6px yatay kelepçe, 8px boşluk,
      zoom'un ankrajı ölçeklendirmesi (`(inset + x)·z + tx`), dikey kelepçe (`6 … H − 26`).

```csharp
/// <summary>
/// §2.3: "Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her zoom'da net)" ve
/// "yatayda panel kenarına kelepçeli (6px) — node kenardayken bile tamamen okunur" (JSX:468-475).
/// </summary>
public class GraphOverlayTests
{
    [Fact]
    public void The_tooltip_anchor_is_the_node_projected_through_the_camera_not_a_world_coordinate()
    {
        var camera = new CameraTransform(2.0, 40, -30);
        var anchor = GraphOverlay.TooltipAnchor(new Point(100, 50), camera, nodeSize: 12, panel: new Size(600, 400));

        // (12 + 100)·2 + 40 = 264 ; (12 + 50)·2 − 30 − 12·0.9·2 − 8 = 64.4
        Assert.Equal(264, anchor.X, 3);
        Assert.Equal(64.4, anchor.Y, 3);
    }

    [Fact]
    public void A_node_at_the_panel_edge_still_gets_a_fully_readable_tooltip_because_x_is_clamped_to_6px()
    {
        var camera = new CameraTransform(1.0, -500, 0);
        var anchor = GraphOverlay.TooltipAnchor(new Point(100, 50), camera, 12, new Size(600, 400));

        Assert.Equal(GraphOverlay.EdgeClampPx, anchor.X, 3);
    }

    [Fact]
    public void The_selection_name_label_is_clamped_by_its_MEASURED_width_so_it_never_overflows_the_panel()
    {
        var topLeft = GraphOverlay.NameLabelTopLeft(
            new Point(600, 50), new CameraTransform(1, 0, 0), 12, new Size(600, 400), new Size(180, 16));

        Assert.Equal(600 - 180 - GraphOverlay.EdgeClampPx, topLeft.X, 3);
    }
}
```
> Prototipteki `half = ad.Length × 3.1 + 8` bir JS mono-genişlik TAHMİNİDİR; WPF'te gerçek ölçüm vardır ve
> onu kullanmak DAHA sadık sonuçtur. Bu sapma testin doc'una yazılır.

- [ ] **Adım 2: Kırmızıyı gör** → stub `Point` `(0,0)` dönerken hepsi patlar.

- [ ] **Adım 3: Saf implementasyon + XAML overlay katmanı**

`GraphView.xaml`, `Ground` içine `Viewport`'un KARDEŞİ olarak (RenderTransform YOK):
```xml
      <!-- [quiet] §2.3: tooltip ve seçim ad etiketi EKRAN koordinatındadır — kamera transform'unun
           ALTINDA yaşasalardı zoom'da ölçeklenip bulanıklaşırlardı. Bu Canvas World'ün kardeşidir ve
           hiçbir RenderTransform taşımaz. -->
      <Canvas x:Name="OverlayLayer" IsHitTestVisible="False" />
```
Tooltip ve ad etiketi bu Canvas'ta **TEK, yeniden kullanılan** birer `Border`'dır (düğüm başına nesne YOK).

- [ ] **Adım 4: Hover davranış testleri (kırmızı)**

```csharp
/// <summary>
/// §2.3 "Hover": node scale(1.7) 120ms ease-out, border 2px, opacity 1 (soluk moddayken bile), z-index öne;
/// tooltip GECİKMESİZ, TAM proje adıyla, ekran koordinatında.
///
/// <para><b>Eski iddia (GraphCullTests, artık geçersiz):</b> ad yolu "etiketi düşen düğümde native WPF
/// ToolTip"ti — etiket kalktığı için tooltip artık İSTİSNA değil ANA isim yoludur ve konumu native
/// popup yerleşimine değil §2.3'ün 8px/6px kuralına uyar.</para>
/// </summary>
public class GraphHoverTests
{
    private const string LongName = "OSYS.Orchestration.Service.WorkOrder";

    private static GraphView Hovered(string name, Func<bool>? motion = null)
    {
        var view = GraphTestView.Realized(new Size(640, 400), motion ?? (() => true));
        view.SetGraph([new(LongName, 0, GraphStatus.Queued), new("OSYS.Base", 1, GraphStatus.Queued)], []);
        view.SetHoverForTest(name);
        return view;
    }

    [StaFact]
    public void Hovering_a_node_scales_it_to_1_7_and_thickens_its_border_to_2px()
    {
        var view = Hovered(LongName);
        var visual = view.NodeVisuals[LongName];
        var scale = (ScaleTransform)visual.Body.RenderTransform;

        Assert.Equal(1.7, scale.ScaleX, 3);
        Assert.Equal(new Point(0.5, 0.5), visual.Body.RenderTransformOrigin);
        Assert.Equal(GraphView.HoverBorderThickness, visual.Square.StrokeThickness, 3);
    }

    /// <summary>Soluk moddayken bile hover opaklığı 1'e çeker (§2.3) ve düğümü z-order'da öne alır.</summary>
    [StaFact]
    public void A_hovered_node_is_fully_opaque_even_while_the_run_has_faded_everything_else()
    {
        var view = Hovered(LongName);
        view.RunPhase = GraphRunPhase.Running;
        view.SetHoverForTest(LongName);

        Assert.Equal(1.0, view.NodeVisuals[LongName].Body.Opacity, 3);
        Assert.Equal(0.13, view.NodeVisuals["OSYS.Base"].Body.Opacity, 3);
        Assert.Equal(view.NodeVisuals.Count - 1, Panel.GetZIndex(view.NodeVisuals[LongName].Cell) > 0 ? view.NodeVisuals.Count - 1 : -1);
    }

    /// <summary>Tooltip GECİKMESİZ görünür ve TAM adı taşır — kısaltma yok (§2.3).</summary>
    [StaFact]
    public void The_tooltip_appears_with_no_delay_and_carries_the_FULL_project_name()
    {
        var view = Hovered(LongName);

        Assert.Equal(Visibility.Visible, view.TooltipVisibility);
        Assert.Equal(LongName, view.TooltipText);
    }

    /// <summary>Tooltip kamera transform'unun DIŞINDADIR: zoom değişince ANKRAJI kayar ama ÖLÇEĞİ değişmez
    /// (RİSK #5'in kanıtı).</summary>
    [StaFact]
    public void Zooming_moves_the_tooltip_but_never_scales_it()
    {
        var view = Hovered(LongName);
        double before = view.TooltipRenderSize.Width;
        var anchorBefore = view.TooltipTopLeft;

        view.HandleWheel(new Point(300, 200), 120);

        Assert.Equal(before, view.TooltipRenderSize.Width, 3);
        Assert.NotEqual(anchorBefore, view.TooltipTopLeft);
        Assert.Null(view.OverlayLayerTransform);        // katmanda HİÇ RenderTransform yok
    }

    [StaFact]
    public void Leaving_the_node_hides_the_tooltip_and_returns_the_opacity_to_the_run_decision()
    {
        var view = Hovered(LongName);
        view.RunPhase = GraphRunPhase.Running;
        view.SetHoverForTest(null);

        Assert.Equal(Visibility.Collapsed, view.TooltipVisibility);
        Assert.Equal(0.13, view.NodeVisuals[LongName].Body.Opacity, 3);
    }

    /// <summary>Tooltip'in tek bir örneği vardır — düğüm başına nesne kurulmaz (177 projede 177 Border olmaz).</summary>
    [StaFact]
    public void The_overlay_reuses_one_tooltip_element_instead_of_building_one_per_node()
    {
        var view = Hovered(LongName);
        var first = view.TooltipElement;
        view.SetHoverForTest("OSYS.Base");

        Assert.Same(first, view.TooltipElement);
    }

    /// <summary>Realize testi: overlay katmanı gerçek bir pencerede, uygulamanın token zinciriyle realize olur
    /// (headless süit XAML runtime çözümlemesini görmez — CLAUDE.md).</summary>
    [StaFact]
    public void The_overlay_realizes_in_a_real_window_with_its_tokens_resolved() { /* … */ }
}
```
> `SetHoverForTest(string?)`, `TooltipVisibility`, `TooltipText`, `TooltipTopLeft`, `TooltipRenderSize`,
> `TooltipElement`, `OverlayLayerTransform` yeni internal test yüzeyleridir. Hover üretimde gerçek
> `MouseEnter`/`MouseLeave` ile sürülür; headless'ta gerçek fare olayı üretilemediği için seam açılır
> (mevcut `HandlePan*`/`HandleWheel` deseni) ve seam'in ÜSTÜNDEKİ iki karar (`MouseEnter` bağlama +
> `Ground.MouseLeave` temizliği) gerçek routed event'le `MouseInput` üzerinden ayrıca pinlenir.

Ayrıca bu adımda **eski native `visual.Body.ToolTip` yolu SÖKÜLÜR** ve
`GraphCullTests.A_node_that_lost_its_label_carries_the_full_project_name_as_a_tooltip…` YENİDEN YAZILIR
(yeni overlay yolunu pinler; doc'una eski iddia + §2.3 atfı).

- [ ] **Adım 5: Implementasyon + tam süit + commit**

```bash
git add src tests
git commit -m "feat(graph): hover buyutme + ekran koordinatli tooltip overlay"
```

---

### Task 8: Seçim — odakla & sığdır + bağımlılık çizgileri + ad etiketi

Bu task aynı zamanda **v1 "sinema modu"nun söküm noktasıdır**: frontier kamerası, takip dönüşü,
`FOLLOW PAUSED` pili, kenar sisi ve kalıcı kenar ağı burada gider.

**Files:**
- Create: `src/BuildOrchestrator.App/Graph/SelectionEdgeStyle.cs`
- Modify: `src/BuildOrchestrator.App/Graph/GraphCamera.cs`, `GraphView.xaml`, `GraphView.xaml.cs`,
  `GraphNodeVisual.cs`, `src/BuildOrchestrator.App/MainWindow.xaml.cs`,
  `src/BuildOrchestrator.App/AccessibilityNames.cs`, `src/BuildOrchestrator.App/ViewModels/InteractionText.cs`
- Delete: `src/BuildOrchestrator.App/Graph/EdgeStyleResolver.cs`, `Graph/GraphLayout.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphSelectionFocusTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/App/EdgeStyleResolverTests.cs` (Delete — 313 satır),
  `GraphCinemaTests.cs` (Delete — 517 satır), `GraphCameraTests.cs` (Modify),
  `GraphPanZoomTests.cs`'in follow/pil bölümü (Delete)

**Interfaces:**
- Consumes: `QuietGraphLayout` konumları, `GraphCamera.ClampPan/RoundPixels`, `GraphOverlay.NameLabelTopLeft`
- Produces:
  ```csharp
  public static class GraphCamera   // sinema yüzeyi SÖKÜLDÜ
  {
      public const double DefaultScale = 1.0;
      public const double SelectionMinScale = 0.7;
      public const double SelectionMaxScale = 2.6;
      public const double SelectionPaddingPx = 48.0;
      public const double SelectionPaddingNodeFactor = 3.0;
      public const double ManualMinScale = 0.7;
      public const double ManualMaxScale = 5.0;
      public const double WheelZoomStep = 1.14;
      public const double TransitionMs = 460.0;
      public const double WheelTransitionMs = 160.0;
      // PanMarginPx / ClampPan SİLİNDİ — gerekçe: "Verilen karar: ClampPan SİLİNİR" bölümü.

      public static CameraTransform Default { get; }
      public static CameraTransform FocusAndFit(Size panel, Rect centreBounds, double nodeSize, Vector worldOffset);
      public static CameraTransform Pan(CameraTransform camera, Vector delta);              // kelepçesiz, yuvarlanmaz
      public static CameraTransform ZoomAt(CameraTransform camera, Point cursor, double factor); // ölçek kelepçeli
      public static double RoundPixels(double value);
  }

  public static class SelectionEdgeStyle
  {
      public const double Thickness = 1.2;
      public const double Opacity = 0.75;
      public const double FlowTravelPx = 24.0;
      public const double FlowDurationMs = 640.0;
      public static readonly IReadOnlyList<double> Dash;   // {4, 8} ABSOLUT px
      public const string BrushKey = "Brush.Amber";

      public static DoubleCollection DashArray { get; }          // kalınlığa BÖLÜNMÜŞ, donmuş
      public static double DashOffsetTarget { get; }             // −24 / Thickness
      public static Geometry Curve(Point from, Point to);        // dikey kübik bezier, my = (y1+y2)/2
  }

  // GraphView (test yüzeyi)
  internal IReadOnlyList<Path> SelectionEdgePaths { get; }   // seçim yokken BOŞ
  internal AnimationClock? EdgeFlowClock { get; }            // seçim kenarlarının TEK paylaşımlı clock'u
  internal string SelectionLabelText { get; }
  internal Visibility SelectionLabelVisibility { get; }
  internal CameraTransform CurrentCamera { get; }            // YAŞAR
  ```

- [ ] **Adım 1: Kırmızı testleri yaz**

```csharp
/// <summary>
/// design v1.3.0 §2.3 "Seçim — odakla & sığdır" + §3.3. Prototip: BuildApp.jsx satır 344-356 (sığdırma),
/// 386-396 (kenarlar), 476-485 (ad etiketi).
///
/// <para><b>Eski iddia (GraphCinemaTests / GraphCameraTests, artık geçersiz):</b> kamera building
/// frontier'ini takip ederdi (0.85–1.4 bandı), seçim sabit 1.1 ölçeğe giderdi, kenar ağı kalıcıydı ve
/// koşuya karışmayan kenarlar sise düşerdi. v1.3.0 §2.3 hepsini kaldırdı: kamera YALNIZ seçimle hareket
/// eder, kenarlar YALNIZ seçimde vardır.</para>
/// </summary>
public class GraphSelectionFocusTests
{
    /// <summary>Base → Data → Api zinciri + bağlantısız Other. Odak kümesi testlerinin ORTAK zemini.</summary>
    private static GraphView Wired(Func<bool>? motion = null)
    {
        var view = GraphTestView.Realized(new Size(600, 400), motion ?? (() => false));
        view.SetGraph(
            [new("OSYS.Base", 0, GraphStatus.Succeeded),
             new("OSYS.Other", 0, GraphStatus.Queued),
             new("OSYS.Data", 1, GraphStatus.Building),
             new("OSYS.Api", 2, GraphStatus.Queued)],
            [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")]);
        return view;
    }

    /// <summary>Odak kümesi = seçili + doğrudan deps + doğrudan dependents; sınır kutusu panele sığdırılır,
    /// zoom 0.7–2.6'ya kelepçelenir (pad = 3×node + 48).</summary>
    [Fact]
    public void The_focus_set_is_fitted_into_the_panel_with_a_3_node_plus_48px_padding()
    {
        var camera = GraphCamera.FocusAndFit(
            new Size(600, 400), new Rect(100, 100, 200, 100), nodeSize: 12, worldOffset: new Vector(12, 12));

        double pad = 12 * 3 + 48;                       // 84
        double expected = Math.Min(600 / (200 + pad), 400 / (100 + pad)); // = 600/284 ≈ 2.113
        Assert.Equal(Math.Clamp(expected, 0.7, 2.6), camera.Scale, 4);
    }

    [Theory]
    [InlineData(4000, 0.7)]   // çok geniş küme → taban
    [InlineData(1, 2.6)]      // tek node → tavan
    public void The_selection_zoom_is_clamped_to_the_0_7_to_2_6_band(double span, double expected)
        => Assert.Equal(expected, GraphCamera.FocusAndFit(
            new Size(600, 400), new Rect(0, 0, span, span), 12, new Vector(12, 12)).Scale, 3);

    [StaFact]
    public void Selecting_a_node_draws_edges_to_its_deps_and_dependents_and_NOTHING_else()
    {
        var view = Wired();                             // Base → Data → Api, ayrıca Other (bağlantısız)
        view.SelectedNode = "OSYS.Data";

        Assert.Equal(2, view.SelectionEdgePaths.Count);
        Assert.All(view.SelectionEdgePaths, p => Assert.Equal(SelectionEdgeStyle.Thickness, p.StrokeThickness, 3));
    }

    /// <summary>Seçim yokken graf ÇİZGİSİZDİR — kalıcı bağımlılık ağı v1.3.0'da kaldırıldı (§2.3
    /// "Kaldırılanlar"). Bu, gizli panel bug'ının (Task 2) ikinci yarısını da yapısal olarak kapatır:
    /// artık her tick'te stillenecek 1214 kenar YOKTUR.</summary>
    [StaFact]
    public void With_no_selection_the_graph_carries_no_edges_at_all()
    {
        var view = Wired();
        Assert.Empty(view.SelectionEdgePaths);
    }

    [StaFact]
    public void Clearing_the_selection_tears_the_edges_down_again()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";
        view.SelectedNode = null;

        Assert.Empty(view.SelectionEdgePaths);
    }

    /// <summary>Aynı node'a tekrar tıkla VEYA boş alana tıkla → varsayılan görünüm (zoom 1, pan 0) — §2.3.</summary>
    [StaFact]
    public void Releasing_the_selection_returns_the_camera_to_the_default_view()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";
        view.SelectedNode = null;

        Assert.Equal(GraphCamera.Default, view.CurrentCamera);
    }

    /// <summary>Seçili node'da 2px focus-ring outline + altında 6px boşlukla ad etiketi (§2.3).</summary>
    [StaFact]
    public void The_selected_node_shows_its_focus_ring_and_a_clamped_name_label_below_it()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";

        Assert.Equal(Visibility.Visible, view.NodeVisuals["OSYS.Data"].SelectionRing.Visibility);
        Assert.Equal("OSYS.Data", view.SelectionLabelText);
        Assert.Equal(Visibility.Visible, view.SelectionLabelVisibility);
    }

    /// <summary>Akan kesikler: dasharray 4 8 → offset −24, 640ms linear sonsuz; WPF'te dash birimi kalınlık
    /// çarpanı olduğu için değerler 1.2'ye BÖLÜNÜR ve MUTLAK desen yine 4px/8px olur.</summary>
    [Fact]
    public void The_selection_edges_flow_with_an_absolute_4_by_8_dash_regardless_of_the_1_2px_thickness()
    {
        Assert.Equal(4.0 / SelectionEdgeStyle.Thickness, SelectionEdgeStyle.DashArray[0], 6);
        Assert.Equal(8.0 / SelectionEdgeStyle.Thickness, SelectionEdgeStyle.DashArray[1], 6);
        Assert.Equal(-SelectionEdgeStyle.FlowTravelPx / SelectionEdgeStyle.Thickness,
            SelectionEdgeStyle.DashOffsetTarget, 6);
    }

    /// <summary>Odak dışı HER ŞEY 0.1'e söner (§2.3) — seçim kenarları hariç.</summary>
    [StaFact]
    public void Everything_outside_the_focus_set_dims_to_ten_percent()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 3);   // dep → odakta
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals["OSYS.Other"].Body.Opacity, 3);
    }

    /// <summary>Seçim değişince hover TEMİZLENİR (§2.3) — Task 7'nin overlay'i bayat kalmaz.</summary>
    [StaFact]
    public void Changing_the_selection_clears_a_stale_hover()
    {
        var view = Wired();
        view.SetHoverForTest("OSYS.Base");
        view.SelectedNode = "OSYS.Data";

        Assert.Equal(Visibility.Collapsed, view.TooltipVisibility);
    }

    /// <summary>Kamera geçişi 460ms ease-in-out; reduced-motion'da ANINDA (§2.3 + Global Constraints).</summary>
    [StaFact]
    public void The_camera_glides_over_460ms_and_snaps_under_reduced_motion() { /* … */ }
}
```

- [ ] **Adım 2: Kırmızıyı gör** — `SelectionEdgeStyle`/`FocusAndFit` stub'ları, `SelectionEdgePaths` boş.

- [ ] **Adım 3: Implementasyon**

`GraphCamera.FocusAndFit`:
```csharp
    /// <summary>[quiet] §2.3 "odakla & sığdır": odak kümesinin MERKEZLERİNİN sınır kutusu panele sığdırılır
    /// ve merkez ORTALANIR. Pay (3×node + 48) kutunun kare uzantısını ve nefes payını birlikte karşılar.
    /// KELEPÇE YOKTUR ve olamaz: tuval = panel olduğu için bir öteleme kelepçesi, ölçek 1'in altındaki her
    /// seçimde ötelemeyi grafın merkezine zorlar ve bu hesabı tamamen ezerdi.</summary>
    public static CameraTransform FocusAndFit(Size panel, Rect centreBounds, double nodeSize, Vector worldOffset)
    {
        double pad = nodeSize * SelectionPaddingNodeFactor + SelectionPaddingPx;
        double scale = Math.Clamp(
            Math.Min(panel.Width / (centreBounds.Width + pad), panel.Height / (centreBounds.Height + pad)),
            SelectionMinScale, SelectionMaxScale);
        double cx = centreBounds.X + centreBounds.Width / 2 + worldOffset.X;
        double cy = centreBounds.Y + centreBounds.Height / 2 + worldOffset.Y;
        return new CameraTransform(
            scale,
            RoundPixels(panel.Width / 2 - cx * scale),
            RoundPixels(panel.Height / 2 - cy * scale));
    }
```

`GraphView`: `_edgeSlots`/`_edges`/`_flowingEdges`/`ApplyEdgeStyles`/`ApplyEdgeStyle`/`MaterializeEdge`/
`DashClockFor`/`EdgeDashes` SÖKÜLÜR; yerine `_deps`/`_dependents` sözlükleri + `RebuildSelectionEdges()`
(seçim değişince kurar/söker, en fazla komşu sayısı kadar `Path`) + tek `_edgeFlowClock` gelir.
Sökülenler: `IsSettled`, `_previousFocus`, `_previousScale`, `ResolveFocus`, `ResolveScale`, `FitScale`,
`FrontierScale`, `ShouldRetarget`, `ShouldRescale`, `FollowResume*`, `_followResumeTimer`,
`TryResumeFollow`, `ResumeFollowNow`, `HasFollowTarget`, `UpdateFollowPill`, `FollowPillMouseDown`,
`FollowPill` XAML'i, `InteractionText.GraphFollowPaused`, `AccessibilityNames.GraphFollowPill`.

- [ ] **Adım 4: Envanterin bu task'a düşen silmeleri uygula** ve her birini envanterdeki gerekçeyle eşleştir.
      `ARCHITECTURE.md §20`'nin `FOLLOW PAUSED` maddesi Task 11'de silinecek (not düş).

- [ ] **Adım 5: Tam süit + commit**

```bash
git add src tests
git commit -m "feat(graph): secimde odakla-sigdir + talep uzerine baglanti cizgileri"
```

---

### Task 9: Serbest gezinme + açılış dalgası + ipucu satırı

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml` (HintText), `GraphView.xaml.cs`,
  `src/BuildOrchestrator.App/ViewModels/InteractionText.cs`
- Test: `tests/BuildOrchestrator.Tests/App/GraphPanZoomTests.cs` (Modify — band/adım/geçiş yeniden yazılır)
- Test: `tests/BuildOrchestrator.Tests/App/GraphRevealTests.cs` (Create)

**Interfaces:**
- Consumes: `GraphCamera.ZoomAt/Pan/ClampPan`, `RevealStagger`, `GraphView.RevealHeroKey`
- Produces:
  ```csharp
  public const double RevealStepMs = 9.0;      // build-order index BAŞINA
  public const double RevealDelayCapMs = 520.0;
  public const double DragThresholdPx = 3.0;   // §2.3: "≤3px hareket tıklama sayılır"
  internal static double RevealDelayMs(int buildOrderIndex);
  internal string HintText { get; }            // seçime göre iki metinden biri
  ```
  ```csharp
  // InteractionText
  public const string GraphHintNavigate = "scroll = zoom · drag = pan";
  public const string GraphHintRelease = "click again to release";
  ```

- [ ] **Adım 1: Kırmızı testleri yaz**

```csharp
/// <summary>
/// §2.3 "Serbest gezinme" + "İlk açılış". <b>Eski iddialar (artık geçersiz):</b>
/// (a) <c>GraphPanZoomTests</c> wheel'i manuel bandı 0.45–2.0 ve adımı ×1.1 olarak pinliyordu —
///     v1.3.0 §2.3 bandı 0.7–5.0'e, adımı ×1.14'e taşıdı;
/// (b) jestler yalnız sinema kipinde (>150 düğüm) çalışıyordu — sinema kipi kalktı, jestler HER grafta var;
/// (c) <c>GraphRenderTests.The_layer_stagger_is_55ms_per_layer_capped_at_330ms</c> gecikmeyi KATMAN başına
///     veriyordu — §2.3 onu DÜĞÜM başına yaptı ("build-order index × 9ms, max 520ms").
/// </summary>
public class GraphRevealTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 9)]
    [InlineData(57, 513)]
    [InlineData(58, 520)]     // 522 → tavan
    [InlineData(1000, 520)]
    public void The_reveal_delay_is_nine_ms_per_build_order_index_capped_at_520(int index, double expected)
        => Assert.Equal(expected, GraphView.RevealDelayMs(index), 3);

    /// <summary>Dalga BUILD-ORDER'ı izler: gecikme sırası, <c>SetGraph</c>'a gelen sıradır — katman değil.</summary>
    [StaFact]
    public void The_wave_follows_build_order_top_down_and_left_to_right()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(
            [new("A", 0, GraphStatus.Discovered), new("B", 0, GraphStatus.Discovered),
             new("C", 1, GraphStatus.Discovered)], []);

        Assert.Equal(0, view.RevealDelayOf("A"), 3);
        Assert.Equal(GraphView.RevealStepMs, view.RevealDelayOf("B"), 3);
        Assert.Equal(2 * GraphView.RevealStepMs, view.RevealDelayOf("C"), 3);
    }

    /// <summary>Beliriş: 300ms ease-out, 5px yukarıdan — sabitler liste satırıyla ORTAK (RevealStagger).</summary>
    [StaFact]
    public void A_node_rises_five_pixels_over_300ms_exactly_like_a_list_row()
    {
        Assert.Equal(RevealStagger.RevealMs, GraphView.RevealMs, 3);
        Assert.Equal(RevealStagger.RevealRisePx, GraphView.RevealRisePx, 3);
    }

    [StaFact]
    public void Reduced_motion_places_every_node_instantly_with_no_wave() { /* … */ }
}

/// <summary>§2.3 "Serbest gezinme" — wheel bandı, sürükleme eşiği ve ipucu satırı.</summary>
public class GraphNavigationTests
{
    [StaFact]
    public void The_wheel_zooms_by_1_14_per_notch_inside_the_0_7_to_5_0_band()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", 0, GraphStatus.Discovered)], []);

        view.HandleWheel(new Point(320, 200), 120);
        Assert.Equal(GraphCamera.WheelZoomStep, view.CurrentCamera.Scale, 3);

        for (int i = 0; i < 40; i++) view.HandleWheel(new Point(320, 200), 120);
        Assert.Equal(GraphCamera.ManualMaxScale, view.CurrentCamera.Scale, 3);

        for (int i = 0; i < 80; i++) view.HandleWheel(new Point(320, 200), -120);
        Assert.Equal(GraphCamera.ManualMinScale, view.CurrentCamera.Scale, 3);
    }

    /// <summary>İmlecin altındaki dünya noktası sabit kalır — bu iddia YAŞAR (yalnız band/adım değişti).</summary>
    [Fact]
    public void The_world_point_under_the_cursor_stays_put_while_zooming()
    {
        var start = new CameraTransform(1, 0, 0);
        var cursor = new Point(240, 130);
        var zoomed = GraphCamera.ZoomAt(start, cursor, GraphCamera.WheelZoomStep);

        Assert.Equal((cursor.X - start.Tx) / start.Scale, (cursor.X - zoomed.Tx) / zoomed.Scale, 2);
    }

    /// <summary>≤3px hareket TIKLAMADIR, üstü pan; sürükleme sonrası bırakma boş-alan tıklaması TETİKLEMEZ
    /// (§2.3). <b>Eski iddia:</b> eşik platformdan (<c>SystemParameters.MinimumHorizontalDragDistance</c>)
    /// geliyordu; §2.3 sayıyı açıkça 3px verdiği için tasarım sabiti kazanır.</summary>
    [StaFact]
    public void A_three_pixel_wiggle_is_a_click_but_a_fourth_pixel_makes_it_a_pan()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", 0, GraphStatus.Discovered)], []);
        view.SelectedNode = "A";

        view.HandlePanStart(new Point(100, 100));
        view.HandlePanMove(new Point(102, 101));     // toplam 3px
        view.HandlePanEnd();
        Assert.Null(view.SelectedNode);              // tıklama sayıldı → seçim bırakıldı

        view.SelectedNode = "A";
        view.HandlePanStart(new Point(100, 100));
        view.HandlePanMove(new Point(140, 100));     // 40px
        view.HandlePanEnd();
        Assert.Equal("A", view.SelectedNode);        // pan'dı → seçime dokunulmadı
    }

    /// <summary>Jestler artık HER graf boyutunda canlıdır — sinema kapısı yok.</summary>
    [StaFact]
    public void Gestures_work_on_a_three_node_graph_because_there_is_no_cinema_gate_any_more()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", 0, GraphStatus.Discovered)], []);

        view.HandleWheel(new Point(320, 200), 120);

        Assert.NotEqual(GraphCamera.DefaultScale, view.CurrentCamera.Scale);
    }

    /// <summary>Sağ alttaki mono ipucu seçime göre değişir (§2.3).</summary>
    [StaFact]
    public void The_hint_line_switches_between_the_navigate_and_the_release_copy()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", 0, GraphStatus.Discovered)], []);

        Assert.Equal(InteractionText.GraphHintNavigate, view.HintText);
        view.SelectedNode = "A";
        Assert.Equal(InteractionText.GraphHintRelease, view.HintText);
    }

    /// <summary>Sürükleme sırasında kamera geçişi KAPALIDIR (birebir takip) — mevcut
    /// <c>Grabbing_the_graph_mid_flight_freezes_the_current_frame</c> testinin sadeleşmiş hâli YAŞAR.</summary>
    [StaFact]
    public void A_drag_follows_the_hand_frame_by_frame_with_no_transition() { /* … */ }
}
```
> `RevealDelayOf(string)` yeni bir internal test seam'idir (mevcut `RevealGeneration` deseni).
> İpucu satırı yeni bir XAML öğesidir → **realize testi ZORUNLU**: gerçek `Window`'un `Content`'i üzerinde
> mono ailesi ve `Brush.TextFaint` çözülerek realize olur (`CopyTextTests`'in başlık pini deseni).

- [ ] **Adım 2: Kırmızıyı gör → implementasyon → yeşil**

- [ ] **Adım 3: Tam süit + commit**

```bash
git add src tests
git commit -m "feat(graph): serbest gezinme bandi + acilis dalgasi + ipucu satiri"
```

---

### Task 10: Ölü kod süpürmesi + guard'lar + tam süit

**Files:**
- Delete: `src/BuildOrchestrator.App/Graph/GraphLayout.cs`, `Graph/GraphLabelMetrics.cs`,
  `Graph/EdgeStyleResolver.cs` (Task 4/8'de sökülmediyse burada)
- Modify: `GraphView.xaml.cs` (kalan ölü üye/sabit/test yüzeyi), `GraphTestView.cs`, `SyntheticGraph.cs`
- Test: tüm graf süiti

**Interfaces:**
- Consumes: Task 1 envanterinin "SİLİNİR" listesi
- Produces: envanterle kod arasında **sıfır sapma** — silinmesi kararlaştırılan her mekanizmanın kodda da
  karşılığı kalmamış olur.

- [ ] **Adım 1: Ölü üye taraması**

```powershell
dotnet build BuildOrchestrator.slnx -warnaserror:CS0169,CS0414,CS0649
Select-String -Path src/BuildOrchestrator.App/Graph/*.cs -Pattern 'sinema|follow|Fog|LabelsFit|frontier|Pulse|LayerStagger|Cull|Materiali|FullDetail'
```
Kalan her eşleşme ya gerçek bir tüketiciye bağlanır ya silinir. Eski `[sinema]`/`[G2]`/`[T63]` etiketli
yorumlar, ANLATTIKLARI mekanizma gittiyse birlikte gider.

- [ ] **Adım 2: Guard'ları koştur**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~NoHardcoded|FullyQualifiedName~AntiSlop|FullyQualifiedName~Contrast|FullyQualifiedName~MotionOwnerHygiene|FullyQualifiedName~NoTurkishUserText|FullyQualifiedName~DesignTokenScale"
```
Beklenen: hepsi yeşil. `SuccessFlourishTests`'in `SourceGuard.ScanText` kaynak listeleri graf dosyalarını
adıyla anıyor — silinen dosyalar oradan da çıkarılır (aksi halde guard "taradığını iddia ettiği dosyayı
bulamadı" diye kırmızı verir; bu KASITLI bir kontroldür).

- [ ] **Adım 3: Erişilebilirlik yüzeyi**

`AccessibilityTests`: `GraphNodeBody` peer'ı ve `AccessibilityNames.GraphNode(ad, statü)` YAŞAR — düğüm
üstünde ad olmasa da ekran okuyucu adı statüyle birlikte alır. `GraphFollowPill` ile ilgili her şey gider.

- [ ] **Adım 4: Tam süit + perf süiti**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: 0 failed. `GraphRealizationPerfTests` çıktısındaki 500/1000 düğüm ölçümleri konsola yazılır ve
Task 11'in doküman metnine GERÇEK ölçüm olarak girer.

- [ ] **Adım 5: Commit**

```bash
git add -A src tests
git commit -m "chore(graph): v1 sinema modu kalintilarinin sokumu"
```

---

### Task 11: Doküman + gözle doğrulama

**Files:**
- Modify: `ARCHITECTURE.md` (§13.6 tamamen · §20 iki madde · §22 kod haritası)
- Modify: `README.md` (yalnız graf kısayolu/kullanım cümlesi yanlışsa)
- Create: `.claude/outputs/2026-08-06-22-34-quiet-graph-visual-checklist.md`

**Interfaces:**
- Consumes: Task 4–10'un ölçümleri ve nihai kod
- Produces: doküman ile kod arasında sıfır sapma + gözle doğrulama listesi

- [ ] **Adım 1: `ARCHITECTURE.md` §13.6 "Graph renderer" TAMAMEN yeniden yazılır**

Anlatı üslubu korunur ("şu oturumda ekledik" YAZILMAZ). Kapsayacakları:
otomatik pitch ve panele tam sığma · katman bantları ve build-order okuma yönü · isimsiz mini node +
box glyph · koşu opaklık sistemi ve hold-fade'in `BeginTime` karşılığı · beads ve tek paylaşımlı clock ·
ekran-koordinatlı overlay (tooltip + ad etiketi) ve NEDEN kamera transform'unun dışında olduğu ·
seçimde odakla-sığdır + talep üzerine kenarlar · serbest gezinme bandı ve **pan'ın neden kelepçesiz
olduğu** (kelepçe, ölçek 1'in altındaki seçimlerde odakla-sığdırı ezerdi; kurtarma jesti boş-alan
tıklamasıdır) · açılış dalgası · **gizli panelde iş yapılmaması** (Task 2).
`FullDetailMaxNodes` "tek kapı" anlatısı, etiket LOD'u, kenar sisi, frontier follow ve pil bölümleri
KALDIRILIR. Rakam gömme kuralı: bayatlayacak ölçüm (37ms/75ms gibi) YAZILMAZ ya da dayanıklı dille yazılır.

- [ ] **Adım 2: `ARCHITECTURE.md` §20 "Known limits"**

**(a) Klavye erişimi — kullanıcı isteği, sonradan keşfedilmesin diye AÇIKÇA yazılır.** Mevcut madde
(`Graph nodes are not keyboard-accessible (§15)`) yerine, v1.3.0'ın sonucunu da taşıyan hâli:

```markdown
- **Graph nodes are not keyboard-accessible, and the quiet graph does not change that.** A node is a
  mouse target: pointer hover names it, a click selects it. Each node does reach the automation tree as
  an invokable element carrying its project name and status, so a screen reader can find and activate
  one — but there is no tab stop, no arrow-key traversal and no focus visual driven by the keyboard, and
  the design deliberately does not add one. The keyboard path to any project is the projects list, which
  is fully traversable and drives the same selection everywhere (§13.7); the graph reflects that
  selection rather than being a second way to reach it.
```

**(b)** `FOLLOW PAUSED` pili maddesi **SİLİNİR** (pil yok).
**(c)** "The graph is full-detail up to 150 nodes" maddesi YENİDEN YAZILIR: artık karakter değiştiren bir
eşik yok. Graf her boyutta aynı görünür — pitch küçülür ve node 8px tabanına iner — ve **her düğüm
kurulur**; cull kaldırıldı, çünkü graf panele tam sığdığı için eleyecek bir şey bulamıyordu. Bunun
bedeli, çok büyük bir workspace'te açılışın (Sync anında, bir kez) tüm düğümleri kurmasıdır; ölçüm
`GraphRealizationPerfTests` çıktısından GERÇEK sayılarla yazılır.

- [ ] **Adım 3: `ARCHITECTURE.md` §22 kod haritası** — silinen üç dosya çıkarılır, beş yeni dosya
      (`QuietGraphLayout`, `GraphNodeOpacity`, `GraphBeads`, `GraphOverlay`, `SelectionEdgeStyle`) eklenir.

- [ ] **Adım 4: Gözle doğrulama listesi yazılır** (`quiet-graph-visual-checklist.md`)

Tarayıcıda `Build Orchestrator (standalone).html` açılır, uygulama gerçek OSYS reposuyla çalıştırılır ve
yan yana bakılır. **Ölçülemeyen, yalnız gözle kapanan maddeler:**

1. **RİSK #2 — 8px node'da üç opaklık kademesi.** 177 projelik koşuda 0.13 (soluk) / 1.0 (derlenen) /
   0.2 (biten) ayırt edilebiliyor mu? Edilemiyorsa bu bir TASARIM sorunudur — eşik kodda gizlice
   değiştirilmez, kullanıcıya bildirilir.
2. **RİSK #2b — 8px karede %52 glyph** (≈4px) görünür mü, yoksa gürültü mü?
3. **Kelepçesiz pan** — grafı panelin dışına sürükleyip boş alana tıklayınca gerçekten geri geliyor mu;
   kayboldum hissi veriyor mu?
4. **Beads** 8px node'un 2.8px dışında gerçekten "sık noktalar" gibi mi görünüyor, yoksa kesintisiz
   halka mı? Ek yerinde bindirme var mı?
5. **Hold-fade ritmi:** biten proje 2.4sn parlak kalıp 0.7sn'de sönüyor mu; paralel 6 projede göz
   yoruluyor mu?
6. **Splitter sürüklerken** yerleşim yeniden hesabı akıcı mı (takılma/zıplama var mı)?
7. **Tooltip** her zoom'da net mi (ölçeklenmiyor mu), panel kenarında tamamen okunuyor mu?
8. **Açılış dalgası** üstten alta / soldan sağa akıyor mu; 177 projede 520ms tavanı doğru hissettiriyor mu?
9. **Reduced-motion açıkken** (Windows → Ayarlar → Erişilebilirlik → Görsel efektler → Animasyon efektleri
   KAPALI) beads/dalga/kamera geçişi tamamen kapalı mı?
10. **`list`/`focus` moduna geçince** koşu sürerken CPU düşüyor mu (Task 2'nin gözle karşılığı).

- [ ] **Adım 5: Tam süit son kez + merge**

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

```bash
git add ARCHITECTURE.md README.md .claude/outputs
git commit -m "docs(graph): quiet graph mimarisi ve bilinen sinirlar"
git checkout main && git merge --no-ff feat/quiet-graph
git push origin main
# merge'ün geçtiği DOĞRULANDIKTAN sonra:
git branch -d feat/quiet-graph && git push origin --delete feat/quiet-graph
```

---

## Risk → task eşlemesi

| # | Risk | Nerede ele alınıyor |
|---|---|---|
| 1 | Yerleşim panel boyutuna bağlı (en yüksek regresyon riski) | **Task 3** (saf, 13 test) + **Task 4** (`Resizing_the_panel_recomputes…`, `Resizing_updates_the_visuals_in_place…`) |
| 2 | 8px node'da üç opaklık kademesinin ayırt edilebilirliği | **Task 11** gözle doğrulama #1/#2 — kodla kapanmaz, kullanıcı kararı |
| 3 | Paralel derlemede N sonsuz beads animasyonu | **Task 6** — tek paylaşımlı `ClockGroup` + 32 düğümlük ölçüm testi |
| 4 | Hold-fade'in WPF karşılığı, 177 animasyon | **Task 5** — `BeginTime`'lı tek atımlık animasyon + 177 düğümlük bütçe testi |
| 5 | Ekran-koordinatlı tooltip/etiket transform dışında olmalı | **Task 7** — `OverlayLayer` (RenderTransform'suz Canvas) + `GraphOverlay` saf aritmetiği |

## Kapsam dışı (bilinçli)

- Graf düğümlerine klavye erişimi (bkz. Task 11 §20 maddesi — bilinen sınır olarak YAZILIYOR).
- Prototipin `revealKey` ile tüm katmanı remount etme numarası; WPF'te `RevealStagger` zaten var.
- Sim/örnek veri; graf gerçek `Topology` beslemesinden çalışır.
- `GraphBinder`/`GraphBuilder` (Core) semantiği — katman, kenar ve statü üretimi DEĞİŞMEZ.
