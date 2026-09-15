# Design v1.17.0 + v1.18.0 uygulama planı (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-10-10-23-design-v1.18.0/README.md` → §9 `## v1.18.0` ve
`## v1.17.0` bölümleri (satır ~559-651). Prototip: aynı klasörde `prototype/app/BuildApp.jsx`,
`prototype/app/build-data.js`, DS: `prototype/_ds/.../_ds_bundle.js`, token'lar `.../tokens/*.css`.

**Kapsam:** önceki paket v1.15.0 idi. v1.16.0 zaten `main`'de (`a0b0ae5`). Uygulanacaklar v1.17.0 + v1.18.0.
v1.17.0 madde 4 (harici kartta `Source` seçimi kalktı) **zaten uygulanmış** (TFVC kullanıcı kararıyla kaldırıldı,
`9661e7f`); iş yok — açıklama metni git-only kalır, TFVC geri getirilmez.

## Global Constraints

- Proje kuralları: `CLAUDE.md` (kökte) — ÖZELLİKLE: kırmızı test kuralı (fix/özellik kodundan ÖNCE testin KIRMIZI
  verdiği gösterilir), davranış değişince eski testi yeni kuralı pinleyecek şekilde yeniden yaz + doc'una eski iddia
  + değişme gerekçesi, eşik/bütçe gevşetmek YASAK, yeni XAML kökü/şablonu = realize testi, kopya YASAK / tek
  doğruluk kaynağı, hardcoded hex/ms/px yerine token (`Resources/Tokens.xaml`, `Motion.xaml`), kod/UI metinleri
  İngilizce, kod yorumları Türkçe.
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Açık uygulama Debug bin'ini kilitleyebilir → kilitlenirse `-c Release` ile derle/test et.
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` KULLANMA (UTF-8/CRLF bozar) — Edit/Write kullan.
- Commit mesajı dosyaya yazılıp `git commit -F` ile atılır; Türkçe, ASCII (mevcut log üslubu: `feat(graph): ...`).
  Sonuna: `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.
- ARCHITECTURE.md §22 kod haritasıdır; davranışı değişen bölüm (§13.x, §14.5 vb.) AYNI task'ta yerinde yeniden
  yazılır (changelog değil, anlatı; rakam gömme yok). Doküman kodla uyuşmuyorsa raporda belirt.
- Tasarımdaki süre "80ms" yazıyor ama prototip `var(--duration-fast)` = **120ms** kullanıyor; uygulamanın
  `Duration.Fast`/`DsTransition` 120ms'dir → mevcut geçiş altyapısı kullanılır, yeni süre sabiti açılmaz.
- Renk eşlemesi: `neutral-700` = `Brush.Neutral700` (#2a2a30), `neutral-500` = `Brush.Neutral500` (#54545c),
  `amber-soft-hover` = `Brush.AmberSoftHover`, `amber` = `Brush.Amber`, `surface` = `Brush.Surface` (#141417),
  `surface-hover` = `Brush.SurfaceHover` (#1a1a1e), `surface-raised` = `Brush.SurfaceRaised`, `border` =
  `Brush.Border`, `border-subtle` = `Brush.BorderSubtle`, `text-secondary`/`text-faint`/`text-primary` =
  `Brush.TextSecondary`/`Brush.TextFaint`/`Brush.TextPrimary`, `amber-text` = `Brush.AmberText`.
- Sıra (bloklayıcı → önemli → kozmetik): bloklayıcı yok. Önemli: T1-T5. Kozmetik: T6-T7. Kapanış: T8.

---

### Task 1: Sıralı teslim dalgası (v1.18.0 — işaretleme→koşu geçişi)

Spec: README §9 v1.18.0 ilk üç madde + "Uygulama sayıları — sıralı teslim dalgası" tablosu. Prototip:
`build-data.js` `_mark` (satır ~511-543), `BuildApp.jsx` satır ~581-588.

Mevcut kod: `src/BuildOrchestrator.App/Controls/MarkingChoreography.cs` (adımlar `Neutral, Wave, Hold, DimEnv,
Settle, Wait, Wait2`; `TotalMs = 2520 + W`; kapsam Settle'da `MarkedOpacity` 0.45'e iner),
`Services/OperationChoreographer.cs` (Play/PlayAsync, doğal bitişte son adımda TUTAR, `runStarted` gelince Cancel),
`Graph/GraphView.xaml.cs` (`SetMarking`, `ApplyNodeOpacity` marking dalı, `ApplyOpacityTarget`), `MainWindow.xaml.cs`
(`ApplyMarkingToGraph`). Mevcut testler: `tests/.../App/ChoreographyTests.cs` (ve `MarkingChoreography`'ye atıf
yapan diğerleri — grep ile bul).

Yeni kural:
1. `Wait` ve `Wait2` adımları KALKAR. Adımlar: `Neutral 0 · Wave 440 · Hold 440+W · DimEnv 740+W · Settle 1300+W`;
   koşu (koreografinin toplam süresi, `TotalMs`) = **2060+W**. W formülü değişmez.
2. `SettleStaggerMs(n) = n > 1 ? min(40, round(700/(n−1))) : 0` (JS `Math.round` paritesi → AwayFromZero).
3. `Settle` adımında İŞARETLİ node hedefi **0.13** (= `GraphNodeOpacity.RunDim`, tek kaynak — ikinci bir 0.13
   sabiti açma), geçiş **400ms ease-in-out**, node başına gecikme = o node'un dalga sırası (`Order`) ×
   `SettleStaggerMs`. Dalga sırası AMBER'a yanma sırasıyla aynıdır (aynı `Order` sonucu).
   İşaretli node `Hold/DimEnv`'de 1.0 kalır. İŞARETSİZ node `DimEnv` ve `Settle`'da 0.18, 1120ms ease-in-out (değişmez).
   `MarkedOpacity` (0.45) ve `MarkedGlideMs` (440) artık kullanılmıyorsa silinir.
4. Doğal bitişte ekran `Settle`'da TUTULUR (mevcut "son kareyi tut" kuralı korunur); `runStarted` sonrası koşu
   fazında sırada bekleyen node 0.13'tür → `marking → running` geçişinde opaklık zıplaması YOK. Test bunu pinler:
   işaretli ve henüz derlenmeyen bir node için settle hedefi == running hedefi.
5. Gecikmeyi grafa taşımak için grafın node başına dalga sırasını bilmesi gerekir. Uygulama seçimi implementer'ın:
   ör. `PushToGraph`'a sıra haritası eklemek ya da saf çekirdekte `SettleDelayMs(order, n)` + grafın gecikmeli
   `BeginTime`/keyframe'li animasyonu. Karar saf sınıfta, WPF yalnız uygular. Reduced-motion: koreografi zaten
   oynamaz (değişmez).
6. Uygulamanın kendi sürüm notunda (`Services/ReleaseNotes.cs`) "The opening choreography holds its last frame until
   the run actually starts — no flash back to full brightness in between." maddesi yeni davranışı anlatacak şekilde
   yerinde yeniden yazılır (Changed; ör. marked projects dim in the order they lit, the run starts while the last
   ones are still dimming).

Testler (önce KIRMIZI): adım zamanları ve `TotalMs` (n=1, n=36 örneği: W=1465 → koşu 3525ms), `SettleStaggerMs`
(n=1→0, n=2→40, n=36→20, n=300→2), `Steps` listesinde Wait/Wait2 yok, graf marking dalında işaretli node Settle
hedefi 0.13 ve gecikmesi sıra×stagger, işaretsiz node 0.18. Eski 0.45/Wait pinleyen testler DEĞİŞEN KURAL doc'uyla
yeniden yazılır.

Doküman: ARCHITECTURE.md §14.5 (Motion) ve koreografiyi anlatan diğer yerler (grep: `Wait2`, `0.45`, `2520`).

### Task 2: Beads bir tık kalın + hücreye kelepçeli yörünge (v1.18.0)

Spec: README §9 v1.18.0 madde 3-4. Prototip `BuildApp.jsx` satır ~517-525 ve ~622-631:
```
const bsw = 1.6;
const bgap = Math.max(0.8, Math.min(2.8, (pitch - size - bsw - 2) / 2));
const ri = 3.2, rw = size + bgap * 2, svgS = rw + ri * 2, rx = Math.min(rw / 2, 6.8);
const per = 4 * rw - 8 * rx + 2 * Math.PI * rx;
const step = per / Math.max(8, Math.round(per / 3.2));
strokeDasharray: '0.01px ' + (step - 0.01) + 'px'; strokeLinecap round; animation 2400ms linear infinite
```
(`rw` = yörünge YOLUNUN kenarı; kalem yola ortalanır.)

Mevcut kod: `src/BuildOrchestrator.App/Graph/GraphBeads.cs` (`For(nodeSize)`, `OrbitGapPx 2.8`, `BeadSpacingPx 3.4`,
`StrokeThickness 1.0` — "1'den farklı olamaz, dash birimi kalınlık çarpanı" sözleşmesi, `CycleMs 4200`),
`Graph/GraphView.xaml.cs` (`CellOverhang`, `ApplySizes`, `EnsureBeads`, `ApplyBeadsGeometry`, `EnsureBeadsClock`),
`Graph/QuietGraphLayout.cs` (`QuietLayoutResult` zaten `Pitch` taşır). Test: `tests/.../App/GraphBeadsTests.cs`.

Yeni kural:
- Kalınlık **1.6**, hedef aralık **3.2**, tur **2400ms**; `bgap = max(0.8, min(2.8, (pitch − size − 1.6 − 2)/2))`,
  yol kenarı `size + 2·bgap`, köşe `min(side/2, 6.8)`, çevre/adım formülü aynı (min 8 nokta).
- `GraphBeads.For` pitch'i de alır (ör. `For(nodeSize, pitch)`); `GraphView.ApplySizes` `_layout.Pitch`'i geçer.
- **WPF dash birimi:** `StrokeDashArray` ve `StrokeDashOffset` kalınlık ÇARPANIDIR → kalınlık 1.6 olunca desen
  `[0.01/1.6, (step−0.01)/1.6]` ve saat `To = −perimeter/1.6` olmalı (ya da eşdeğeri); eski "kalınlık 1 olmak
  zorunda" sözleşmesi DEĞİŞEN KURAL olarak yeniden yazılır. Ayrıca WPF `Rectangle` kalemi sınırın İÇİNE çizer
  (yol = sınır − kalınlık/2): yol kenarının `size+2·bgap` olması için Rectangle genişliği buna göre ayarlanmalı —
  mevcut kodun bu konuda ne yaptığını doğrula, ölçüyle pinle.
- `CellOverhang` yörüngenin en dış mürekkebini (2.8 + 0.8 = 3.6) kırpmamalı — hâlâ ≥ ise değişmez; formül yeni
  sabitlerden türemeli.
- Node yapısı, küp glyph, hover ölçeği, seçim border'ı, giriş/çıkış opaklığı 420/640ms, reduced-motion DEĞİŞMEZ.

Testler (önce KIRMIZI): 36 projede tipik pitch (ör. pitch 20, size 12 → bgap 2.2; pitch 44, size 24 → bgap 2.8
kelepçe kapanmaz) ve yoğun graf (pitch 8, size 8 → bgap 0.8); **komşu mürekkepler arası boşluk ≥ 2px** kelepçe
tabanına inmeden (pitch − size − 2·(bgap+0.8) ≥ 2 when bgap > 0.8); desen çevreyi tam böler; kalınlık/aralık/tur
sabitleri; dash dizisinin kalınlığa bölünmüş olduğu.

Doküman: ARCHITECTURE.md §13.6 (graph renderer) beads anlatısı.

### Task 3: Konsol başlığı ve `Back` satırı tasarıma döner (v1.18.0 §9 "Konsol başlığı")

Spec: README §9 v1.18.0 "Konsol başlığı ve `Back` satırı" alt bölümü (kabuk, iki hâl, davranış sözleşmesi,
"WPF tarafında sık sapan noktalar" kontrol listesi) — BİREBİR. Prototip `BuildApp.jsx` satır ~2599-2631, DS
`Button` (ghost/sm), `IconButton` (sm), `StatusGlyph` `_ds_bundle.js`'te; `I.back` ikonu `BuildApp.jsx` `const I`.

Mevcut kod: `src/BuildOrchestrator.App/Console/ConsoleHeader.xaml(.cs)` — sapmalar: Back `Style="{x:Null}"`, ikonsuz
`← Back` unicode metni, padding 2 / margin −2 (spec: DS Ghost Sm 24px + 12px lucide arrow-left, `margin-left −6`);
statü glyph'i unicode `TextBlock` (spec: 13px `StatusGlyph`, building'de dönen amber `BuildingSpinner`);
dependency issue KIRMIZI + `Icon.DepWarn` (spec: amber-text, `Icon.AlertTri` üçgen, 11px metin, tooltip
`Dependency issue: <projeler> — last successful output referenced`); `⚠ dependency cycle` (tooltip
`In a dependency cycle`) YOK; Copy log özel kabuk (spec: DS IconButton sm; başarıda 1.4s yeşil ✓ + `Copied`
— mevcut davranış korunur); sol grup `StackPanel` olduğu için proje adı hiç kırpılmıyor (spec: panel daralınca
kısalan TEK öğe proje adı; Back ve sağ blok asla kırpılmaz); öğe aralıkları 8px değil (5/10). Başlığı besleyen yer
(`MainWindow.xaml.cs` `ShowProjectLog` çağrıları) cycle üyeliğini ve dep-issue projelerini geçmiyorsa eklenir
(veri VM'de zaten var: `RowWarning`, cycle bilgisi — grep). Mevcut `ConsoleStatus` (Glyph/Name/BrushKey) statü
metni ve rengi için korunur; glyph artık çizilir.
- Başlık 28px, padding 10, `CONSOLE` etiketi proje logunda gizli, `N lines` mono 11px text-faint — mevcut doğru
  olanlara dokunma.
- Back tıklaması yalnız seçimi bırakır (mevcut `BackRequested` yolu); Esc aynı — değişmez, test ile doğrula.
- İkon için `Resources/Icons.xaml`'de `Icon.ArrowLeft` yoksa lucide arrow-left eklenir (+ StrokeThickness kardeşi,
  mevcut sözlük deseni).

Testler (önce KIRMIZI): realize testleri (`window.Content` üzerinde) — Back'in stili Ghost.Sm, yüksekliği 24, ikon
içerdiği, sol marjı −6; statü glyph'i `StatusGlyph`/building'de `BuildingSpinner`; dep issue metni amber +
tooltip; cycle uyarısı; dar genişlikte proje adının kırpıldığı ama Back ve `N lines`'ın tam kaldığı; öğe
aralıkları 8. Mevcut `ConsoleModesTests`/`ConsoleViewTests` ilgili testleri DEĞİŞEN KURAL olarak güncellenir.

Doküman: ARCHITECTURE.md §13.5 (console host) başlık anlatısı.

### Task 4: Alt barda tek hover dili (v1.17.0 madde 1)

Spec: README §9 v1.17.0 "### 1 — Alt barda tek hover dili" + durum tablosu. Prototip CSS `BuildApp.jsx` satır 51-60,
bar kontrolleri satır ~2645-2742 (`data-bo-hov="off"|"on"`).

Kural (YALNIZ action bar içindeki kontrollere):
- Nötr hover (Sync · bakım kutusunun 3 ikon butonu · Σ/building/✓/✗/—/⚠ sayaç çipleri · `N behind` · branch ·
  worktree · perf): zemin `Brush.Neutral700`, kenar `Brush.Neutral500`, metin VE ikon `Brush.TextPrimary`. Statü
  glyph'leri (✓ ✗ —), building spinner/nokta ve amber ⚠ üçgeni KENDİ rengini korur.
- Açık/aktif kontrol (IsChecked filtre çipi, açık branch/worktree popover chip'i, koşan bakım görevi/Resolve
  düğmesi — `MaintenanceBox.SetBusy` amber-soft hâli) hover'da: zemin `Brush.AmberSoftHover`, kenar `Brush.Amber`,
  metin `amber-text` sabit.
- Segment (`Debug | Release`): yalnız seçili OLMAYAN seçenek hover alır: zemin `Brush.SurfaceRaised`, metin
  `Brush.TextSecondary`.
- Bakım kutusu ikon butonlarının kendi kenarı yok (kutu çerçeveyi taşır) → hover yalnız zemin + ikon; ölçü ve
  ayraçlar sabit.
- Boyut/radius sabit, geçiş mevcut `DsTransition` (120ms), disabled kontrol hover almaz (Base zaten opaklık .45 —
  hover setter'ı IsEnabled=False'ta etkisiz olmalı: `MultiTrigger IsMouseOver+IsEnabled`).
- Build split-button ve Stop DEĞİŞMEZ.
- Paylaşımlı `Ds.Chip`/`Ds.Button.Secondary`/`Ds.IconButton` stilleri başka yerlerde de kullanılıyor (ShellRoot filtre
  chip'i, satır ikonları, dialog'lar) → onları DEĞİŞTİRME; bar'a özgü BasedOn stilleri (ör. `Ds.Bar.Chip`,
  `Ds.Bar.Chip.Action`, `Ds.Bar.Button.Secondary.Sm`, `Ds.Bar.IconButton`, `Ds.Bar.Segment.Item`) aç ve
  ActionBar/MaintenanceBox'ta kullan. Tasarımın "WPF karşılığı" notu: tek style dili, iki trigger (nötr hover ·
  aktif+hover). Tekrarı en aza indir (ortak setter'lar tek yerde).
- İkonun metinle birlikte beyazlaması: şu an bazı ikonlar sabit fırçayla çiziliyor (`MaintenanceBox.Compose` →
  `IconVisual.Make(..., "Brush.TextSecondary")`, `ActionBar` Σ ikonu `Brush.TextDim`, Sync ikonu `Brush.TextPrimary`)
  → bunlar kontrolün `Foreground`'una bağlanır (`IconVisual.BoundToForeground` deseni). Σ ikonunun dinlenme rengi
  tasarımda ne ise korunur (prototip `DS.Chip` icon wrapper rengi — `_ds_bundle.js` Chip'e bak).

Testler (önce KIRMIZI): realize edilmiş ActionBar'da her kontrol grubunun stil trigger'larının hover hedeflerini
çözdüğü (stil üzerinden `IsMouseOver` trigger setter değerlerini okumak ya da `DsTransition` hedef fırça anahtarını
doğrulamak — mevcut `ActionBarTests` desenine bak), aktif çipte hover'ın amber-soft-hover + amber kenar olduğu,
segmentte seçili olanın hover almadığı, bakım ikonunun Foreground'u izlediği, Build/Stop stillerinin değişmediği.

Doküman: ARCHITECTURE.md §13.2 action bar / §14 bileşen davranışları (hover kuralı).

### Task 5: Katman başlıkları tıklanabilir (v1.17.0 madde 2)

Spec: README §9 v1.17.0 "### 2 — Katman başlıkları tıklanabilir". Prototip `BuildApp.jsx` `GroupHead` (satır ~841-860)
ve `jumpGroup` (~914-918).

Mevcut kod: `src/BuildOrchestrator.App/Controls/StickyLayerList.xaml(.cs)` (in-flow başlık + yapışık overlay AYNI
`HeaderTemplate`; overlay `IsHitTestVisible=False`), `Controls/LayoutMetrics.cs` (`OffsetOfRow`, `OffsetOfHeader`,
`ScrollTargetForRow`), `Controls/ScrollAnimator.cs` (smooth scroll), reduced-motion `MotionSettings`.

Kural:
- Hover: zemin `Brush.SurfaceRaised`, alt çizgi `Brush.Border`, metin (caps ad + mono sayı) `Brush.TextSecondary`;
  dinlenme hâli mevcut (surface / border-subtle / text-faint). Geçiş mevcut 120ms altyapısı. İmleç `Hand`.
  Tooltip `Jump to <layer>` (native tooltip; metin tek kaynak — `AccessibilityNames`/`InteractionText` deseni).
- Tıklama: o grubun İLK (görünür/filtrelenmiş) satırını yığılmış başlıkların hemen altına getirir:
  `scrollTop = offsetOfFirstRow − (headerSlotIndex + 1) × headerHeight`, 0'a kelepçeli; smooth (mevcut
  `ScrollAnimator`), reduced-motion'da anında. Aritmetik saf `LayoutMetrics`'e (ör. `JumpTargetForHeader(slot)`).
- Seçim, filtre, konsol, graf DEĞİŞMEZ — başlık yalnız scroll eder. Katlama yok.
- Yapışık overlay başlıkları da tıklanabilir ve hover alır olmalı (kullanıcı çoğunlukla onlara tıklar); overlay
  hit-test'e açılınca mouse wheel liste ScrollViewer'ına ulaşmaya devam etmeli (wheel'i ileten bir kablo gerekirse
  ekle) ve overlay başlığı altındaki in-flow başlıkla aynı görünmeli.
- Klavye: tasarım Enter/Space de diyor. Mevcut liste klavye modeli (başlıklar odaklanamaz, ok tuşları satırlar
  arasında gezinir — `KeyboardNavigation.DirectionalNavigation="Contained"`) KIRILMAMALI. Enter/Space bu model
  bozulmadan eklenebiliyorsa ekle; eklenemiyorsa ekleme ve raporda gerekçesini yaz.

Testler (önce KIRMIZI): `LayoutMetrics` hedef aritmetiği (ilk grup → 0'a kelepçe, orta grup, filtreli liste);
realize testi: başlık hover fırçaları, tooltip metni, tıklamanın ScrollViewer offset hedefini doğru istemesi (reduced
motion ile anında), seçim/filtrenin değişmediği; overlay başlığının tıklanabildiği; wheel'in hâlâ scroll ettiği.

Doküman: ARCHITECTURE.md §13.2 projects list / §13.4 scroll.

### Task 6: Konsol — ok imleç + satır hover bandı (v1.17.0 madde 3, konsol kısmı)

Spec: README §9 v1.17.0 "### 3" ilk iki madde + "iki panelde hover adımı eşittir" maddesi. Prototip `.bo-cline`
(`BuildApp.jsx` satır 62-63), konsol gövdesi `cursor: default` (satır ~1080).

Mevcut kod: `src/BuildOrchestrator.App/Console/ConsoleView.xaml(.cs)` — AvalonEdit `TextEditor` (`Padding 12,8,12,14`,
seçim açık, IBeam imleç varsayılanı).
- Konsol gövdesinin imleci standart ok (`Cursors.Arrow`) — hem `TextEditor` hem `TextArea`/`TextView` seviyesinde
  gerçekten Arrow görünmeli (AvalonEdit TextArea kendi IBeam'ini dayatır; doğrula).
- İmlecin altındaki satır: tam genişlik bant (gövde padding'ini aşar: sol/sağ 12px dahil panel kenarından kenara),
  zemin `Brush.Surface`, geçiş 120ms altyapısı (ya da anlık — AvalonEdit background renderer'da animasyon zorsa
  anlık bant kabul, raporda yaz). Renk kuralı, hiza, satır yüksekliği, metin seçilebilirliği değişmez. Fare panelden
  çıkınca bant kalkar. Aktif prompt satırı overlay'i etkilenmez.
- Uygulama önerisi: AvalonEdit `IBackgroundRenderer` (KnownLayer.Background) + `TextView.MouseMove/MouseLeave` —
  ya da editörü saran Grid'de konumlanan tek bir `Rectangle`. Performans: boşta ek saat/timer AÇMA (bkz. son perf
  commit'leri — boşta uyuyan pompa/saat kuralı, ARCHITECTURE §14.5).

Testler (önce KIRMIZI): imlecin Arrow olduğu; bir satır üzerinde mouse konumu simüle edilince bandın o satırın
görsel Y aralığında ve tam genişlikte `Brush.Surface` ile çizildiği (renderer'ın hedef hesabı saf bir yardımcıya
çıkarılıp test edilebilir); mouse leave'de bandın kalktığı; seçim işlevinin durduğu.

Doküman: ARCHITECTURE.md §13.5.

### Task 7: Event stream — her satırda hover (v1.17.0 madde 3, stream kısmı)

Spec: README §9 v1.17.0 "### 3" üçüncü madde. Prototip `StreamRow` (`BuildApp.jsx` satır ~1110-1117):
`background: isSel ? surface-raised : hover ? (clickable ? surface-hover : surface) : transparent`;
`cursor: clickable ? pointer : default`.

Mevcut kod: `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` `EventStreamRow.SetHover` — tıklanamaz satırda
hover yok ("parıltıyı ezmesin" gerekçesi), `ApplyBackground`.
- Tıklanamaz satır hover'da `Brush.Surface`, imleç Arrow; tıklanabilir `Brush.SurfaceHover`, imleç Hand (mevcut);
  seçili satır (2px amber şerit + `SurfaceRaised`) değişmez.
- Glow-once (başarılı done/taskdone satırının 1.1s parıltısı) hover tarafından bozulmamalı: parıltı sürerken
  hover o satırın zemin animasyonunu kesmemeli ya da parıltı bitince hover doğru zemine oturmalı — hangi davranışı
  seçtiysen testle pinle, eski "hover zemini yok" kuralını DEĞİŞEN KURAL olarak yeniden yaz.

Testler (önce KIRMIZI): tıklanamaz satırda hover hedefi `Brush.Surface`, tıklanabilirde `Brush.SurfaceHover`,
seçilide `SurfaceRaised`; imleçler; glow-once ile etkileşim.

Doküman: ARCHITECTURE.md §13.2 event stream anlatısı.

### Task 8: Sürüm notları + doküman taraması + tam süit

- `src/BuildOrchestrator.App/Services/ReleaseNotes.cs`: bu işin kullanıcıya görünen değişiklikleri için İngilizce
  maddeler (tasarımın `release-notes.js` 1.17.0/1.18.0 maddelerinden uyarlanır, ama YALNIZ uygulamada gerçekten
  yapılanlar; TFVC/Source maddesi EKLENMEZ): tek hover dili (Changed), katman başlığına tıklayıp atlama (Added),
  konsol satır hover + ok imleç ve her stream satırında hover (Changed), beads kalın/hızlı + komşuya değmeyen
  yörünge (Changed), konsol başlığı düzeni (Changed/Fixed), sıralı teslim (Task 1 zaten güncelledi — tekrar ekleme).
  Mevcut notlar testlerini (`ReleaseNotes` testleri) kır/düzelt.
- ARCHITECTURE.md / README.md'de T1-T7 sonrası bayat kalan her ifadeyi tara (grep: `4200`, `3.4`, `Wait2`,
  `0.45`, `I-beam`, `IBeam`, `← Back`, `hover` bar kuralı) ve yerinde düzelt; README kısayol/kullanım etkileniyorsa
  (katman başlığına tıklama) kısa ekle.
- Tam süit: `dotnet test ... --filter "Category!=Acceptance"` YEŞİL (token/motion/D8/source guard'ları dahil).
