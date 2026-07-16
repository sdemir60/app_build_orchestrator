# Tasarım (design-v1) ↔ Plan v6 (WPF/.NET 10) — Uygulanabilirlik Analizi ve Karar Raporu

> **Girdi:** [Tasarım paketi README](2026-07-15-19-00-design-v1/README.md) + prototip kaynakları (`BuildApp.jsx` 1623 satır, `build-data.js` 554 satır, DS token/stil dosyaları) ↔ [Plan v6](2026-07-02-01-38-build-orchestrator-plan-v6-implementation.md).
> **Yöntem:** 21 agent'lık çok aşamalı analiz — davranış envanteri, çelişki taraması, 10 boyutta fizibilite analizi, ardından her riskli iddianın şüpheci doğrulayıcılarla (WebSearch destekli) adversarial doğrulaması. ~100 tasarım öğesi tek tek değerlendirildi; ~60 iddia doğrulandı, 6'sı düzeltildi.

---

# 1. YÖNETİCİ ÖZETİ

## Sonuç: UYGULANABİLİR — teknoloji değişikliği GEREKMİYOR

**Plan v6'nın WPF (.NET 10) kararı bu tasarımı taşır.** ~100 öğelik envanterde "uygulanamaz" sınıfına giren tek kalem, Chromium ile **bit-düzeyi font rasterization eşitliği** — o da WebView2 dışındaki hiçbir native framework'te çözülmez ve algısal eşdeğerlik (%95-98) sağlanabilir. Tasarım dilinin kendisi WPF lehine: düz renk yüzeyler, 1px hairline border, gölgesiz panel, yalnız transform+opacity motion, gradient/blur/emoji yasak — bunların hepsi WPF'in tam güçlü olduğu alan. Tasarımın "backdrop blur yok, toast yok, panel gölgesi yok" kararları WPF'in zayıf noktalarını zaten bertaraf etmiş durumda.

## Üç kova

| Kova | İçerik |
|---|---|
| **Native / kolay-custom (büyük çoğunluk)** | Layout iskeleti (Grid+GridSplitter), tüm renk/spacing/radius token'ları (CSS px = WPF DIP, 1:1), üç cubic-bezier eğrisi (KeySpline birebir aynı matematik), typewriter + imleç, dash-flow, nefes, shake, stagger, kaskat log, kamera pan+zoom, spinner, scrollbar restyle (birebir çıkar), popover/dialog/scrim, focus ring (`:focus-visible` semantiğiyle birebir), reduced-motion (Chromium'un okuduğu **aynı OS sinyali**: `SPI_GETCLIENTAREAANIMATION`), Win11 köşe/border (DWM API'leriyle), fontların gömülmesi (Geist SIL OFL, statik OTF) |
| **Hedefli custom parçalar (5 kalem)** | ① `TrackedTextBlock` (letter-spacing 0.07em — WPF'te özellik yok, 1-2 gün) · ② birikimli sticky katman başlıkları (overlay mimarisi, 2-4 gün) · ③ smooth-scroll altyapısı (native yok, attached DP, ~2 gün) · ④ konsol = AvalonEdit + colorizer (metin seçimi + renk + hacim üçlüsü için tek yol, 3-5 gün + chunk loader ~1 hafta) · ⑤ DS kontrol kütüphanesi (Button/Chip/Switch/Segment/Input/Kbd ControlTemplate seti — hiçbiri hazır gelmez, 4-6 gün) |
| **Kabul edilecek yapısal farklar (bkz. §5)** | Font rasterization ~%95-98 · DropShadow'da spread parametresi yok · AvalonEdit'te satır bazlı translateY+scale pop-in yok (tempo + fade eşdeğeri) · animasyonlar UI thread'te tick'lenir (compositor yok) · OS yüzeyleri (klasör seçici, Explorer) uygulama temasına boyanamaz |

## Ama iki ödev var (görsel değil, davranışsal)

1. **Tasarım ↔ Plan çelişki taraması 22 bulgu çıkardı; 3'ü YÜKSEK önem** ve görsel değil **semantik** karar gerektiriyor: Sync'in anlamı (`git fetch` var mı?), branch seçiminde `git switch --detach` konsol satırı (planın "aktif branch'e asla dokunulmaz" kuralıyla çelişir), scheduler dispatch semantiği (tasarımın `break`'i vs planın ready-set'i — throughput farkı büyük). → §7
2. **Prototipte olup README'de yazmayan 25 davranış** tespit edildi (Continue, Retry failed, Copy log, Ctrl+F araması, ETA +400ms sabiti, engine tick kadansı…). Bunlar spec'e bağlanmazsa implementasyonda kaçar. → §9 + Ek A

## Tavsiye

**WPF'te kal; Plan v6'ya bu rapordaki delta'ları işleyerek ilerle.** WebView2 hibrit meşru bir "fidelity sigortası" ama bu ölçekte aşırı mühendislik (bkz. §6). Backend (Contracts/Core/Supervisor) UI teknolojisinden bağımsız olduğu için bu kapı zaten maliyetsiz açık kalıyor: font rasterization farkını gerçek ekranda yan yana görüp kabul edilemez bulursan tek değişen katman App olur.

---

# 2. KARAR MATRİSİ (boyut bazında)

| Boyut | Sonuç | En kritik iş / risk | Custom efor |
|---|---|---|---|
| Tipografi & font | ✅ Uygulanabilir | letter-spacing → `TrackedTextBlock`; variable font desteklenmez → statik OTF'ler | 2-3 gün |
| Pencere kabuğu & shell | ✅ Uygulanabilir | Snap Layouts (WM_NCHITTEST/HTMAXBUTTON) + **maximize taşması düzeltmesi (zorunlu)** | 3-4 gün |
| Layout & scroll | ✅ Uygulanabilir | Birikimli sticky başlıklar + virtualization etkileşimi (bkz. §4.1) | 4-6 gün |
| Motion & animasyon | ✅ Uygulanabilir | UI-thread disiplini; tüm easing/süre birebir çevrilir | 5-8 gün (hacim işi) |
| Dependency graf | ✅ Uygulanabilir | 36 düğümde trivial; T51 (500-1000) için eşikli hibrit mimari şart | 3-5 gün (+T51 3+ gün) |
| Console & event stream | ✅ Uygulanabilir | AvalonEdit zorunlu tercih; satır pop-in transform'u yaklaşıkla | 1-2 hafta |
| DS kontrol kütüphanesi | ✅ Uygulanabilir | Hepsi ControlTemplate işi; 120ms renk geçişleri en pahalı ortak kalem | 4-6 gün |
| Tooltip sistemi | ✅ Uygulanabilir | Ortalanmış placement için `CustomPopupPlacementCallback`; delay=0 override görev-kritik | ~1 gün |
| Settings drag-drop | ✅ Uygulanabilir | `DragDrop.DoDragDrop` KULLANILMAMALI — Mouse.Capture ile elle port | ~1 gün |
| OS entegrasyonu | ✅ Uygulanabilir | VS'de açma semantiği karar ister; Clipboard retry sarmalayıcı | ~1 gün |

---

# 3. BOYUT DETAYLARI

## 3.1 Tipografi & Font

- **Gömme:** `vercel/geist-font` GitHub reposundan **statik OTF** dosyaları (Regular/Medium/SemiBold × Sans/Mono), csproj'da `Resource`, `FontFamily="pack://application:,,,/Assets/Fonts/#Geist"`. woff2 dönüşümü gerekmez (repo OTF dağıtıyor). Lisans SIL OFL 1.1 — gömme/dağıtım serbest, `LICENSE.txt` paketle taşınır. **Kritik:** Google Fonts CDN build'leri OpenType tablolarını (tnum dahil) kırpabiliyor — mutlaka GitHub sürümü.
- **Variable font tuzağı:** WPF variable font eksenlerini desteklemez (dotnet/wpf#7758). Variable TTF gömülürse **tüm ağırlıklar 400 görünür.** Statik instance'lar şart; aynı aile adıyla `FontWeight="Medium|SemiBold"` doğru dosyayı eşler.
- **Tabular rakam:** tasarımdaki tüm tabular bağlamlar Geist Mono'da — monospace'te doğal, ek ayar gerekmez. Sans'ta gerekirse `Typography.NumeralAlignment="Tabular"` native.
- **Letter-spacing 0.07em (caps etiketler):** WPF'te letter-spacing YOK (dotnet/wpf#293). Tasarımın en yaygın tipografik detayı (7+ panel/popover başlığı + katman başlıkları). Çözüm: GlyphRun tabanlı `TrackedTextBlock` — `AdvanceWidths`'e karakter başına `FontSize×0.07` ekler; uppercase dönüşümü de aynı kontrole gömülür. 1-2 gün, izole iş.
- **Küçük punto (11-13px):** pencere köküne `TextOptions.TextFormattingMode="Display"` (Ideal 14px altında bulanık). Koyu zeminde ClearType renk saçağı rahatsız ederse `Grayscale` — Chromium'un koyu zemin görünümüne çoğu ekranda daha yakın. **Hedef monitörde A/B testi şart.**
- **px→DIP:** CSS px = WPF DIP (1/96"), tüm değerler 1:1 taşınır. `PerMonitorV2` + kökte `UseLayoutRounding=True` (hairline'lar için şart).
- **line-height:** `LineHeight` mutlak DIP ister (çarpan değil): 12×1.55=18.6 gibi hesaplanmış resource'lar + `LineStackingStrategy="BlockLineHeight"`. Tek satırlık etiketlerde LineHeight KULLANMA — kap yüksekliği + `VerticalAlignment=Center` (yoksa metin CSS'e göre yukarı kaçar).
- **Ondalık nokta:** tüm formatlama `CultureInfo.InvariantCulture` ile VM'de (Türkçe Windows'ta `4,2s` basma tuzağı); prototipteki `fmtDur` (9950ms eşiği dahil) birebir C# helper'a port edilir.
- **Özel glifler (▸ → · — …):** gömülü fontta varlığı build sırasında doğrulanmalı (`GlyphTypeface.CharacterToGlyphMap`); imleç ▮ zaten karakter değil Rectangle olarak çizilir (prototip de div ile çiziyor) — fallback riski sıfırlanır.

## 3.2 Pencere kabuğu & Shell

- **Custom title bar:** `WindowChrome` (CaptionHeight=40, UseAeroCaptionButtons=False) + `SingleBorderWindow`. Drag/çift-tık/sistem menüsü bedava; etkileşimli öğelere `IsHitTestVisibleInChrome`.
- ⚠️ **Doğrulayıcı düzeltmesi — maximize taşması:** içeriğin maximize'da ekran dışına ~7-8px taşması `WindowStyle=None`'a özgü DEĞİL; WindowChrome yolunda da yaşanır (dotnet/wpf#3887, #2242). **Zorunlu düzeltme:** `WindowState=Maximized` trigger'ında kök içeriğe `SystemParameters.WindowResizeBorderThickness` kadar Padding (veya WM_GETMINMAXINFO hook).
- **Win11 köşe (radius 8):** `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)`. `AllowsTransparency` yolu YANLIŞ (gölge/perf/snap bozulur). Radius OS kontrolünde (~8px @96dpi, fiilen tasarımla örtüşür); iç Border'a ayrıca CornerRadius verme (çift kırpma). Maximize'da OS köşeleri keskinleştirir — doğru davranış.
- **1px pencere border'ı:** `DWMWA_BORDER_COLOR` ile OS çerçevesi birebir `#2a2a30` boyanır — köşe yuvarlamayı takip eder.
- **Snap Layouts:** WindowChrome bunu kendiliğinden YAPMAZ (dotnet/wpf#4825). WM_NCHITTEST'te maximize butonuna `HTMAXBUTTON` döndürme + WM_NCLBUTTONDOWN/UP işleme + hover görselini elle sürme. DPI-hassas rect hesabı; 1-2 gün.
- **Scrim:** düz `rgba(4,4,6,0.60)` pencere-içi Grid katmanı — blur yasak olduğu için WPF'in zayıf noktası hiç devreye girmiyor.
- **Popover gölgesi:** DropShadowEffect'te **spread parametresi yok** — çift gölge tek effect'le yakınsanır (BlurRadius≈22, Opacity≈0.6); gözle ayırt edilmez ama piksel-eş değil. Effect altında ClearType kapanır → popover metni grayscale AA olur; prototip zaten `antialiased` olduğundan bu fiilen tasarıma YAKLAŞTIRIR.
- ⚠️ **Doğrulayıcı düzeltmesi — single-instance:** "kendi process'i restore ediyor, foreground kısıtına takılmaz" varsayımı YANLIŞ — tray'de bekleyen ilk instance background'dur, `Activate()` çoğu durumda sadece taskbar'ı yakıp söndürür. İkinci instance sinyalden önce `AllowSetForegroundWindow(pid)` çağırmalı.
- **Tray:** WPF'te NotifyIcon yok → `H.NotifyIcon.Wpf` veya Shell_NotifyIcon P/Invoke. 16px tray ikonu için elle ayarlanmış raster varyant üret (SVG oto-küçültme amber "D"yi bozar).
- **Maximize glyph'i:** prototipte tek kare — `WindowState=Maximized` iken "restore" (iki kare) glyph'i gerekir; **tasarımda tanımsız, karar iste.**

## 3.3 Layout & Scroll

- **İskelet:** kök Grid `RowDefinitions="40,34,*,42"`; gövde star-oranlı kolonlar + `GridSplitter` (ResizeBehavior=PreviousAndNext). Yüzde sınırları (%28-72 / %18-82) `SizeChanged`'de `ColumnDefinition.Min/MaxWidth` güncellemesiyle. Splitter template: 7px transparan hit alanı + ortada 1px Rectangle; `IsDragging` trigger'ında amber (GridSplitter Thumb'dan türer — doğrulandı). Negatif margin taşması scrollbar'a biner — DPI'larda test.
- **Görünüm modları (quad/list/focus):** `RowDefinition.Height` preset ataması + Visibility. Persist: DragCompleted'da JSON'a (%LOCALAPPDATA%).
- **Birikimli sticky katman başlıkları — tasarımın en zor parçası:** WPF'te `position:sticky` yok. Seçilen mimari: liste ScrollViewer'ının üstüne **overlay ItemsControl** — yapışık küme salt aritmetikle hesaplanır (36px satır + 24px başlık kümülatif tablosu), overlay item'ları listedeki gerçek başlıklarla aynı DataTemplate + opak zemin → geçiş görünmez. VSP-türevi ve Adorner adayları gerekçeli elendi. **Doğrulayıcı düzeltmesi için bkz. §4.1 (virtualization drift).**
- **Alta yapışık scroll:** `ScrollChangedEventArgs.ExtentHeightChange` içerik/kullanıcı ayrımını native verir; 48px eşik + `jumping` bayrağı birebir port edilir.
- **Smooth scroll:** WPF'te native YOK (`VerticalOffset` read-only — doğrulandı). Attached DP `ScrollAnimator.VerticalOffset` + DoubleAnimation; `VirtualizingPanel.ScrollUnit=Pixel` önkoşul. Kullanıcı animasyon sırasında wheel çevirirse animasyon iptal edilmeli (`BeginAnimation(prop, null)`).
- **Follow-mode:** 550ms throttle + 54px dead-band + offset tablosu. Sticky başlıklarla **aynı LayoutMetrics servisini** paylaşmalı; drift düzeltmesi §4.1.
- **"⌄ latest" pill, hover-reveal ikonlar, sabit 14px depIssue slotu, şerit chip'leri (4+N / 3+N):** hepsi standart template/trigger işi, düşük risk.

## 3.4 Motion & Animasyon

- **Easing birebirliği:** üç CSS eğrisi de (`.22,1,.36,1` / `.4,0,.2,1` / `.65,0,.35,1`) `KeySpline` ile **matematiksel olarak aynı** (doğrulandı — MS Learn). Süre/eğri token'ları ResourceDictionary'de; reduced-motion'da topluca 0'a çekilir.
- **Yapısal fark:** WPF'te compositor-independent animasyon YOK — tüm Storyboard'lar UI thread'te tick'lenir; UI bloklanırsa animasyonlar donar (web'de donmazdı). Telafi: (a) derleme zaten ayrı Supervisor process'te — UI thread hafif; (b) dekoratif sonsuz animasyonlara `Timeline.DesiredFrameRate=30`; (c) UI thread'te iş yasağı mimari kural.
- **Typewriter:** DispatcherTimer ~15.6ms çözünürlüklü — 11ms tick birebir tutturulamaz; **Stopwatch-bazlı elapsed hesabıyla** "satır ≤250ms" temposu birebir tutar. Yalnız son `Run.Text` güncellenir; koleksiyon reset'i YASAK.
- **Dash-flow:** `StrokeDashArray={4,7}` + `StrokeDashOffset` To=-22 (11'lik desenin tam 2 periyodu — dikişsiz loop). **Kritik birim farkı (doğrulandı):** WPF dash birimleri px değil **StrokeThickness çarpanı** — 1px'te birebir, 1.6px seçili kenarda değerler bölünür. Tüm akan kenarlar TEK paylaşımlı clock'a bind edilir.
- **Kamera:** `TransformGroup(Scale+Translate)` + From'suz To-animasyonu + `SnapshotAndReplace` = CSS transition retarget paritesi (ikisi de hız korumaz — doğrulandı). Frontier hedefi için küçük sapma eşiği (<~8px retarget etme) eklenmeli. ⚠️ **Düzeltme:** graf etiketlerinde `TextFormattingMode=Ideal` kullan — Display, scale transform altında DAHA KÖTÜ görünür (ancak final scale 1.0'a snap ediliyorsa Display mantıklı).
- **Nefes/shake/glow/stagger/kaskat/pop-in:** hepsi keyframe'li Storyboard, birebir değerlerle. Stagger'da CSS `both` fill'in "öncesi" karşılığı: başlangıç Opacity=0 set et (veya t=0 DiscreteKeyFrame hold) — yoksa gecikme sırasında flash.
- **Motion budget (1 hero):** teknik engel yok; `MotionCoordinator` servisi (tek kapı) + Completed ile slot yönetimi — kod disiplini meselesi.
- **60fps bütçesi:** 36 proje / 58 kenar / ~40 görünür satır WPF için küçük ölçek (VS shell'i çok daha yoğun WPF UI taşır). Risk ölçek değil savrukluk: her tick'te full binding refresh yapmamak.

## 3.5 Dependency Graf

- **36 düğüm/64 kenar ölçeğinde tamamen rahat:** frozen `StreamGeometry` bezier kenarlar, `EdgeStyleResolver` saf fonksiyonu (prototipteki if-zincirinin unit-testlenebilir portu), RenderTransform kamera (layout tetiklemez — 1000 düğümde de ucuz), rozet/dot/tooltip standart.
- ⚠️ **Tasarım çelişkisi — düğüm şekli:** README "26px **daire**" der; DS kodu (canlı prototipte görünen) **4px radius'lu kare** çizer. **Karar iste.** Ayrıca discovered'ın kesikli çerçevesi: WPF Border dashed desteklemez → `Rectangle.StrokeDashArray`.
- **T51 ölçek mimarisi (500-1000 düğüm):** UIElement-per-node taşımaz. Eşikli hibrit: **≤~150 düğüm Shapes** (tooltip/hit-test native) — tasarımın 36'sı bu bantta; üstünde **3 katman**: EdgeLayer (tek OnRender, tüm statik kenarlar tek pass), NodeLayer (DrawingVisual koleksiyonu; dim %25 için `Visual.Opacity` re-render'sız), FlowOverlay (akan dash kenarları HER ZAMAN UIElement Path — **DrawingContext içinde Pen.DashStyle.Offset animasyonu güvenilir çalışmaz**, doğrulanmış). En pahalı kalem 1000×10px mono etiket → GlyphRun cache; zoom<0.8'de etiket gizleme (LOD) meşru kaçış ama tasarım kararı ister.
- ⚠️ **Düzeltme (DrawingVisual modu):** `ContainerVisual.Opacity` DP değil, düz CLR property — Storyboard hedefleyemez. Katman stagger'ı için katmanları ince UIElement host yapmak en temiz çözüm.
- **AA dürüstlüğü:** 1px eğri bezier'ler kesirli scale'de WPF ve tarayıcıda farklı yumuşar — düzeltilecek bug değil, doğal sapma. `EdgeMode=Aliased` eğrilerde KULLANILMAZ (tırtık). Prototipteki `Math.round(tx,ty)` pikselleme animasyon SONUNDA uygulanır (sırasında titretir).

## 3.6 Console & Event Stream

- **Kırılım noktası:** "renkli satır + **metin seçimi** (Plan DD8: 'kutsal') + binlerce satırda akıcılık" üçlüsünü WPF'te aynı anda yalnız **AvalonEdit** verir. TextBlock/ItemsControl seçim desteklemez; RichTextBox/FlowDocument virtualization'sız — MSBuild-verbose hacminde çöker (dotnet/wpf#9202).
- **Mimari:** read-only AvalonEdit + satır-offset bazlı `DocumentColorizingTransformer` (saat=faint, ▸=amber, metin=tip rengi — düz metin yazıldığı için kopyalanan metin de anlamlı) + **hibrit aktif satır**: en yeni satır editör dokümanına girmeden altındaki TextBlock'ta daktilolanır, bitince `Document.Insert` ile commit. Bilinçli küçük sapma: aktif satır ~250ms boyunca seçilebilir değil — seçim+typewriter çatışmasının en temiz çözümü.
- ⚠️ **Satır aralığı 1.55:** AvalonEdit'te line-spacing API'si YOK (issue #233/#315). Çözüm: Geist Mono'yu `LineSpacing=1.55` tanımlı **CompositeFont** ile sarmak — **It-0'da spike ile doğrulanmalı**; tutmazsa satırlar ~%10-15 sıkışık kalır.
- ⚠️ **Birebir taşınamayan tek görsel:** proje logu kaskatındaki satır başına 140ms translateY+scale pop-in — AvalonEdit satır transform'u desteklemez. **Tempo (26ms'de 3 satır) birebir korunur** + satır bazlı opacity-fade en yakın eşdeğer. ItemsControl'e dönmek pop-in'i verir ama metin seçimini feda eder — önerilmez.
- **Event stream:** ListBox + VirtualizingStackPanel (seçim gerekmez, satır=click-to-select) — tamamı native/kolay. Glow-once'ta recycle tekrar-oynatma bayrağı (VM `GlowPlayed`).
- **Hacim:** IPC background thread → `Channel<LogLine>` → ~50ms batch flush → `Document.BeginUpdate/tek Insert/EndUpdate`. Chunk yükleme: son 128-256KB diskten, tepeye yaklaşınca önceki chunk + scroll telafisi; dikiş sequence-id ile.

## 3.7 DS Kontrol Kütüphanesi (kritik eksikten tamamlandı)

Hiçbiri "uygulanamaz" değil ama **neredeyse hiçbiri hazır gelmez** — 9 kontrolün 8'i custom ControlTemplate: Button 4 varyant × 3 boy, split-button (per-corner `CornerRadius` ile iki butonu tek gövdeye dikme — web'deki çözümün birebir aynısı), Chip (icon/label/value/chevron/✕/active), **Switch (WPF'te ToggleSwitch YOK** → CheckBox template), Segment (RadioButton restyle), **Input (placeholder + prefix ikon native YOK** → Grid overlay; invalid kırmızı durum), IconButton, Kbd (tek Border, template bile gerekmez). En pahalı ortak iş: **120ms renk geçişleri** — web'de tek satır `transition`, WPF'te VSM/Storyboard + frozen-brush tuzağı (paylaşılan resource brush anime edilemez → template-lokal brush). Toplam 4-6 günlük düzenli tema kütüphanesi.

## 3.8 Tooltip Sistemi (kritik eksikten tamamlandı)

- Global implicit `ToolTip` Style birebir çıkar; **`InitialShowDelay=0` global override görev-kritik** (WPF varsayılanı ~400-700ms — web'deki anlık his kaybolur).
- ⚠️ WPF `PlacementMode.Top/Bottom` tooltip'i hedefe **ortalamaz**, sola hizalar → `CustomPopupPlacementCallback` şart.
- Canlı içerik ("Building — 24s" sayacı, Copy→Copied) binding ile doğal çözülür (modern WPF'te ShowDuration zaten sonsuz). Klavye focus'ta tooltip .NET Core 3.0+'da sıfır kodla gelir — prototipten fazlası.
- Ayrı HWND farkları: `ClearTypeHint=Enabled` unutulmamalı; gölge spread'i yok.

## 3.9 Settings Drag-Drop (kritik eksikten tamamlandı)

- **`DragDrop.DoDragDrop` KULLANILMAMALI** — OS ghost/adorner semantiği "kart yerinde kayar + canlı swap" hissini VEREMEZ. Doğru karşılık: JSX'teki pointer-capture algoritmasının `Mouse.Capture` + MouseMove ile ~40-60 satırlık elle portu; swap = `ObservableCollection.Move`; sürüklenen karta TranslateTransform + ZIndex + raised trigger. Komşular animasyonsuz anında snap'ler (prototipte de öyle — animasyon EKLENMEMELİ).
- Tek kozmetik fark: Windows'ta grab/grabbing el cursor'ı yok — custom .cur yapılmazsa standart cursor görünür.

## 3.10 OS Entegrasyonu (kritik eksikten tamamlandı)

- **Choose Folder:** `Microsoft.Win32.OpenFolderDialog` (.NET 8+ native, WinForms referansı gerekmez). Dialog OS temasını izler — uygulamanın amber temasına boyanamaz (README'nin "en yakın native" istisnası).
- **Reveal in Explorer:** `explorer.exe /select,"path"` (boşluklu yolda tırnak ŞART); garanti istenirse `SHOpenFolderAndSelectItems`.
- **Open in Visual Studio — karar noktası:** v1 önerisi yeni instance (`vswhere -latest -property productPath` → devenv). Çalışan VS'ye bağlanma (ROT/EnvDTE) zor-custom + COM kırılgan (`Marshal.GetActiveObject` .NET Core'da kaldırıldı) — v1'de yapılmasın.
- **Copy log:** `Clipboard.SetText` ÇIPLAK ÇAĞRILMAZ — pano kilitliyken `CLIPBRD_E_CANT_OPEN` fırlatır (dotnet/wpf#9901); retry sarmalayıcı şart.

---

# 4. DOĞRULAMA DÜZELTMELERİ (şüpheci turdan çıkan 6 kritik düzeltme)

## 4.1 Sticky başlıklar + follow-mode: virtualization offset drift'i (EN ÖNEMLİSİ)

Overlay mimarisinin "virtualization ile sıfır çatışma" iddiası **çürütüldü**: `ScrollUnit=Pixel` modunda VirtualizingStackPanel realize edilmemiş item yüksekliklerini **ortalama tahminle** hesaplar; karışık 36/24px yüksekliklerde model tablosu ile gerçek offset kayar — sticky hesap ve follow-mode hedefi yanlış yere iner. **Çözüm seçenekleri:**
1. **En basit (önerilen başlangıç):** listede virtualization'ı KAPAT — birkaç yüz basit satır WPF için sorunsuz; OSYS 191 proje bu banda giriyor. Aritmetik tablo kesinleşir, her şey birebir çalışır.
2. Virtualization şartsa: viewport'taki ilk realized container'dan **drift kalibrasyonu** (`TransformToAncestor` ile gerçek pozisyon ölç, farkı düzelt); follow-mode'da iki aşama (`ScrollIntoView` → kesin offset'e animasyon).
3. Gerçek "zor-custom": exact-extent raporlayan custom VirtualizingPanel.

Hafifletici: **varsayılan katman konfigürasyonu BOŞ** (başlıksız tek liste) — sticky başlıklar yalnız kullanıcı katman tanımlayınca devreye girer; üstelik katmansız listede yükseklikler uniform (36px) olduğundan tahmin de kaymaz.

## 4.2 WindowChrome maximize taşması
`SingleBorderWindow` yolu bu sorundan MUAF DEĞİL (dotnet/wpf#3887/#2242) — maximize'da kök içeriğe resize-border kadar Padding zorunlu. (§3.2)

## 4.3 Single-instance foreground
`AllowSetForegroundWindow` olmadan pencere öne gelmez, taskbar flash'lar. (§3.2)

## 4.4 Graf etiketlerinde render modu
Zoom transform'u altında `Display` DEĞİL `Ideal`. (§3.4)

## 4.5 DrawingVisual katman opacity
`ContainerVisual.Opacity` animate edilemez (DP değil) — katmanlar UIElement host olmalı. (§3.5)

## 4.6 WinUI 3 eleme gerekçesi
"WASDK 2.0 preview" gerekçesi bayat — 2.0.1 stable Nisan 2026'da çıktı, 2.2.0 yayında. **Eleme kararı DEĞİŞMEZ** ama doğru gerekçe: CharacterSpacing/Composition kazanımları bu tasarımda kritik değilken (letter-spacing 1-2 günlük custom, animasyonlar Storyboard'la karşılanıyor) ekip WPF deneyimi + farklı stil sistemi + aynı P/Invoke ihtiyacı geçiş maliyetini kazanımın üstüne çıkarır.

---

# 5. KABUL EDİLMESİ GEREKEN YAPISAL FARKLAR (dürüst sınırlar)

Bunlar iş gücüyle kapanmaz; "birebir" hedefi bu maddelerde "algısal eşdeğer" olarak çerçevelenmeli:

1. **Font rasterization:** DirectWrite ≠ Chromium/Skia — 11-13px metin hiçbir ayarla bit-düzeyinde aynı olmaz; ~%95-98 eşleşme, `Display` + ClearType/Grayscale A/B ile "yan yana bakılmadan ayırt edilemez" seviye hedeflenir. **WebView2 dışında hiçbir seçenek bunu sıfırlamaz** (Avalonia/WinUI da farklı rasterize eder).
2. **Gölge spread'i:** DropShadowEffect'te spread yok — popover/pill gölgesi tek katmanla yakınsanır.
3. **AvalonEdit satır pop-in'i:** kaskat temposu birebir, satır başına translateY+scale yerine opacity-fade (4px+%1.5 scale 140ms'de algı eşiğinde — fark minimal).
4. **Animasyon threading'i:** "UI donsa da animasyon akar" garantisi WPF'te verilemez — Supervisor ayrımı sayesinde pratik etki beklenmez, ama mimari kural olarak yazılmalı.
5. **CSS `dashed` köşe hizalama:** WPF dash desenini köşeye hizalamaz — pratikte fark edilmez.
6. **OS yüzeyleri:** OpenFolderDialog/Explorer/VS pencereleri uygulama temasına boyanamaz.
7. **Tooltip HWND davranışları:** pencere dışına taşabilir, ekran kenarında flip eder (web'de kırpılırdı) — çoğunlukla iyileştirmedir.

---

# 6. TEKNOLOJİ ALTERNATİFLERİ — KARŞILAŞTIRMA VE TAVSİYE

| Seçenek | Fidelity | Plan v6 gereksinimleri | Bedel / risk | Karar |
|---|---|---|---|---|
| **WPF saf XAML (mevcut plan)** | Ölçü/renk/yerleşim/motion %100; metin ~%95-98 | Tümü birinci sınıf (Job Object, tray, hotkey, WindowChrome, MSBuild shell-out) | 5 hedefli custom parça + tema kütüphanesi hacmi | ✅ **KAL** |
| WPF shell + WebView2 (React UI) | %100 garanti (prototip neredeyse aynen kullanılır) | Backend aynen kalır; titlebar/tray/hotkey yine native yazılır | Idle birkaç yüz MB RAM, 5-6 ek process, Evergreen runtime bağımlılığı, iki stack bakımı, "native değil" hissi | 🟡 Fidelity sigortası olarak **kapıda beklet** |
| Avalonia 11 | Skia custom-draw kolay; LetterSpacing native | Karşılanır (TrayIcon native var) | Windows'ta belgeli text-rendering regresyon geçmişi (#12162/#13265/#15015); ekip deneyimi sıfır; Windows-only projede cross-platform kazancı sıfır | ❌ Ele |
| WinUI 3 / WASDK 2.x | CharacterSpacing + Composition native | Karşılanır (Job Object vs. yine P/Invoke; tray native yok) | Kazanım 1-2 günlük WPF custom işe denk; farklı stil sistemi + öğrenme maliyeti + packaged/unpackaged sürtünmesi | ❌ Ele |

**Kritik stratejik gerçek:** Plan v6'nın asıl seçim sürücüleri (nested Job Object cascade-kill, RegisterHotKey, single-instance, MSBuild.exe shell-out, stdio NDJSON IPC) **dört seçenekte de aynı Win32/P-Invoke koduyla** çözülür — seçimi platform tarafı değil fidelity tarafı belirler; fidelity tarafında da WPF yeterli. WebView2'nin satın alacağı tek gerçek şey font rasterization birebirliği; karşılığı bu ölçekte (36 proje görünümü, ~250 satırlık tamponlar) ağır. Karar kapısı: **It-4 başında** Geist 12-13px gerçek ekran karşılaştırması yap; kabul edilemezse yalnız App katmanı değişir.

---

# 7. TASARIM ↔ PLAN v6 ÇELİŞKİLERİ (22 bulgu)

## 7.1 YÜKSEK önem — davranış kararı gerektirir (görsel değil semantik)

| # | Konu | Tasarım diyor ki | Plan diyor ki | Öneri |
|---|---|---|---|---|
| Y1 | **Sync'in anlamı** | `git fetch origin main` → remote hedef SHA → `curSha → targetSha` gösterimi buna dayanır (README §2.2/3.1, build-data.js 265-285) | Sync = scan/graph/cache; **fetch planda hiç yok**; incremental karar local HEAD'e göre (A5) | **Karar şart.** Fetch isteniyorsa plana "Sync başında fetch (ref güncelleme; checkout/pull YOK — Δ1 uyumlu)" eklenir; istenmiyorsa konsol satırı gerçek adımlara çevrilir. N1 granular tarama satırları çelişki değil — tasarımın dim/info diliyle araya eklenir |
| Y2 | **`git switch --detach` konsol satırı** | Branch seçildiği ANDA cmd satırı basılır (README §2.8, jsx 1336-1353) | "Branch seçimi = niyet; Build'e kadar git'te işlem yok; aktif branch ASLA checkout edilmez" (Δ1) | **Plan kazansın** — komut hem yanlış (ana working-tree HEAD'ini değiştirir) hem yanlış zamanlı. Satır niyet bildirimine çevrilir: `branch target: release/2026.06 (f3a02c8) — worktree will be used at Build`; gerçek `git worktree add` Build anında loglanır |
| Y3 | **Scheduler dispatch semantiği** | Liste sırasında bağımlılığı çözülmemiş projede `break` — arkadaki hazırlar da bekler (head-of-line blocking; README §3.2 "Core'da birebir bu semantik") | Ready-set'ten build-order'da en önde gelen seçilir — hazır olmayanın ÜZERİNDEN ATLAR (A6 §6) | **Plan kazansın** — 191 projelik gerçek grafda head-of-line blocking ciddi süre kaybettirir; UI görünümü değişmez (liste yine build-order, frontier yine akar). Tasarımın kuralı bilinçli isteniyorsa throughput maliyetiyle onaylat |

## 7.2 ORTA önem — biri seçilmeli / plana eklenmeli

| # | Konu | Özet | Öneri |
|---|---|---|---|
| O1 | Toast: DD12 "X-to-tray ilk toast" ↔ README §8 "Toast/popup YOK (karar)" | Tasarım kazansın; DD12'nin amacı (X kapatmıyor bilgisi) tray balloon/Windows bildirimi ile karşılansın (uygulama-içi toast değil) — karar netleşmeli |
| O2 | Kısayollar: F5/Ctrl+F5/Esc/Ctrl+F (tasarım) ↔ Ctrl+B/R + çift-Shift + Ctrl+P (plan N6) | Tasarım kazansın (VS alışkanlığı); çift-Shift/Ctrl+P düşsün (branch popover'da zaten arama var, Ctrl+F proje filtresi var); Alt+B global hotkey korunur. Koşarken F5 davranışı tanımsız — planın "çalışıyorsa Stop" kuralı önerilir |
| O3 | Failure UI: kapatılabilir banner + [Failed'a git] (Δ4) ↔ sticky şerit hata kümesi + "+N more" (README §2.2) | Aynı işlevin iki sunumu — tasarım kazansın; T39 "banner" → "sticky şerit hata kümesi" olarak revize |
| O4 | Konsol cmd satırı `msbuild Osys.sln /m:4` tek-çağrı izlenimi ↔ proje-başına MSBuild.exe shell-out (D10) | Motor değişmez; cmd satırı orchestrator-özet satırına çevrilsin (`build — 14 projects, parallelism 4, Debug`); gerçek MSBuild komutları proje logunda |
| O5 | Perf modları: sabit 6/4/2 paralellik ↔ derece + priority + Job CPU cap (%40/%70/∞) | Birleştir: UI tasarımdan, motor semantiği plandan; derece çekirdek sayısından türetilebilir; konsol notu cap'i de yazabilir |
| O6 | Tray/tray menüsü/X→tray tasarımda hiç yok | Çelişki değil tasarım EKSİĞİ — plan davranışı korunur; tray ikonu = delta-app-icon, menü tasarım dilinde eklenir |
| O7 | T37 interaction state'leri: git-fail retry, engine-died, 0-proje tasarımda yok | Plan kazansın — üretimde kaçınılmaz; tasarım diline çevrilerek eklenir (engine-died → sticky şeridin kalıcı hata modu + "Restart engine"; toast'sız) |
| O8 | depIssue / dependency-affected sistemi **planda hiç yok** (Contracts'ta karşılığı yok) | Tasarım kazansın — plana eklenecek: scheduler'da resolved={succeeded,failed,skipped}, depIssue propagation, `ProjectResult.depIssues[]`, runCompleted'a depIssueCount, ▲ chip + `dep` filtresi |
| O9 | Continue (Stop sonrası) + Retry failed **planda yok** (A9 komut setinde karşılıksız) | Tasarım kazansın — A9'a `continueRun` + `mode='retryFailed'` (failed + transitif bağımlılar) eklenir |
| O10 | Mid-run kilit: plan T12 branch/config/worktree kilitler; prototip branch'i koşarken serbest bırakıyor | Plan kazansın — prototipteki serbestlik gözden kaçma; perf chip canlı kalır (iki taraf hemfikir) |
| O11 | Settings kapsamı: yalnız Layers (tasarım) ↔ A10 geniş konfigürasyon | v1'de tasarımın minimal dialogu; kalan A10 alanları config JSON'da yaşar. "Repo değiştir" akışı tanımsız — küçük giriş kararlaştırılmalı |

## 7.3 DÜŞÜK önem / bilgi

- Worktree kaynak etiketi yalnız popover'da (DD13 "Build yanında glanceable" düşürülür — chip value + title bar eki yeterli).
- "statusbar 28" token'ı = aslında panel başlığı yüksekliği; `PanelHeaderHeight` olarak adlandır, statusbar bölgesi EKLEME.
- Success flourish yalnız stream done satırında (T44 buna daraltılır; liste/graf yeşil dalga EKLENMEZ — "denendi, istenmedi" kararıyla tutarlı).
- Seçili satır `surface-raised` zemini "kutu yok" kuralını bozmaz (border çizilmiyor); plana not düşülsün.
- Will-build hollow: "Sync öncesi VEYA imza hesaplanamayan (null)" olarak birleştirilir; succeeded→clean canlı geçiş plana işlenir.
- Sim metinleri (net8.0 yolları, "Osys.sln 36 projects", eksik flag'ler) **placeholder** — "kopya birebir" kuralının istisnası olarak işaretlenmeli; format birebir, sayılar/yollar/TFM gerçek veriden.
- Split oranları 50/50/50 + 3 görünüm modu (tasarım) plandaki ~%46/%54'ü ezer; ETA formülü (EMA 0.75/0.25 + 5s yuvarlama + almost done) plana implementasyon detayı olarak girer — **BuildState'e `lastDurationMs` alanı eklenmeli** (gerçek ETA için süre tahmini şart, planda yok).
- Skip glyph'i — (tire); plandaki ↷/●/○ ASCII taslaktı, bağlayıcı değil.
- Sticky şerit chip spinner'ı DD7'yi ihlal etmez (yasak olan kayan metindi); reduced-motion'da spinner da durur.
- Build = implicit Sync + Run (tasarım kazanır; D5 cache ile ucuz); elapsed sayacı Sync'i içermez — aynı davranış hedeflenir.

---

# 8. PLANA EKLENECEK YENİ İŞ KALEMLERİ (öneri task listesi)

UI tarafı (çoğu It-4'ün detaylandırması; T34-T51'in altına):

| Kalem | Kapsam | Efor |
|---|---|---|
| U1 `TrackedTextBlock` | GlyphRun + advance genişletme; uppercase converter gömülü | 1-2 gün |
| U2 Sticky header overlay + `LayoutMetrics` servisi | §4.1 kararıyla birlikte (virtualization kapalı başla) | 2-4 gün |
| U3 Scroll altyapısı | `ScrollAnimator` + `BottomAnchorBehavior` + `FollowScrollController` + latest pill | 2-3 gün |
| U4 AvalonEdit konsol | colorizer + hibrit aktif satır + CompositeFont line-height **spike (It-0)** + chunk loader + interleave | 1-2 hafta |
| U5 DS kontrol kütüphanesi | 9 kontrol ControlTemplate seti + 120ms geçiş altyapısı + focus ring | 4-6 gün |
| U6 Tooltip altyapısı | implicit Style + CustomPopupPlacementCallback + delay=0 + canlı içerik deseni | ~1 gün |
| U7 Motion altyapısı | KeySpline/Duration token dictionary + `MotionCoordinator` + reduced-motion servisi (canlı dinleme) | 1-2 gün |
| U8 Pencere kabuğu | WindowChrome + maximize padding düzeltmesi + DWM köşe/border + **Snap Layouts hook** + restore glyph kararı | 3-4 gün |
| U9 Graf paneli | Shapes yolu (36-150 düğüm) + `EdgeStyleResolver` + kamera + FlowOverlay; T51 hibrit katmanlar ayrı | 3-5 gün (+T51) |
| U10 Asset hattı | vercel/geist-font statik OTF gömme + glif kapsam testi + SVG→XAML ikonlar + çoklu-boyut ICO (16/24px elle) | 1-2 gün |
| U11 Settings drag-drop | Mouse.Capture portu (DoDragDrop YASAK) | ~1 gün |
| U12 OS eylemleri | explorer /select + vswhere→devenv + OpenFolderDialog + Clipboard retry | ~1 gün |
| U13 Klavye/focus mimarisi | satır tabIndex+Enter, in-window dialog focus-trap, popover focus yönetimi, AutomationProperties (analiz edilmedi — tek açık kalem) | 2-3 gün |

Motor/sözleşme tarafı (Iteration 1-3'e dokunur): depIssue sistemi (O8), continueRun/retryFailed (O9), `BuildState.lastDurationMs` + ETA formülü, buildPreview/willBuild canlı geçişleri, Sync-fetch kararı (Y1), scheduler semantiği onayı (Y3).

**Kaba toplam ek UI eforu:** ~4-6 hafta — Plan v6'nın It-4/It-5'te zaten ayırdığı UX-polish kapsamının detaylandırılmış hali; plana yeni bir faz eklemiyor, mevcut fazı netleştiriyor.

---

# 9. KARAR NOKTALARI (senin yönlendirmen gereken maddeler)

1. **Graf düğüm şekli:** daire (README) mi, 4px-radius kare (DS kodu — canlı prototipte görünen bu) mi?
2. **Sync semantiği (Y1):** `git fetch origin` Sync'in parçası mı?
3. **Scheduler (Y3):** ready-set (plan, hızlı) mi, liste-sıralı break (tasarım, sim sadeleştirmesi olabilir) mi?
4. **Branch seçim konsol satırı (Y2):** öneri = niyet bildirimi (plan kazanır) — onay.
5. **X→tray ilk bilgilendirme (O1):** tray balloon mı, hiç mi?
6. **Kısayol şeması (O2):** F5/Ctrl+F5 canonical; çift-Shift/Ctrl+P düşsün mü?
7. **VS'de Aç semantiği:** yeni devenv instance (v1 önerisi) mi, çalışan VS'ye bağlanma mı?
8. **Maximize'da başlık butonu:** restore (iki kare) glyph'i eklensin mi (tasarımda tanımsız)?
9. **WebView2 karar kapısı:** It-4 başında font karşılaştırma testi yapılsın mı (önerilir), yoksa şimdiden saf WPF'e kilitlenilsin mi?

---

# Ek A — Prototipte olup README'de YAZMAYAN 25 davranış (implementasyonda kaçmasın)

1. **Continue akışı:** stopped fazında ana buton "Continue" olur; menüye "Continue — {N} queued projects resume — F5" eklenir; F5 stopped'ta Continue tetikler.
2. **Retry failed:** failed>0 iken menüde "Retry failed — {N} failed + dependents"; failed + transitif bağımlılar yeni willBuild; konsol/stream SIFIRLANMAZ.
3. **Copy log butonu** (konsol başlığı): satırlar `\n` ile panoya; ikon 1400ms ✓ + "Copied" tooltip.
4. **Ctrl+F/Cmd+F** proje arama inputuna focus; arama inputundaki Esc yalnız temizler+blur (global Esc zincirine sızmaz).
5. **Proje arama inputu** ("Filter…", 150×20px, büyüteç) — README §2.4'te hiç yok; metin filtresi + statü filtresi AND kesişir; yalnız proje ADINDA arar (sln adı aranmaz).
6. Choose Folder → Sync OTOMATİK başlar; öncesinde cmd `workspace: {path} — {N} projects discovered`.
7. Ek faz metinleri: idle+allClean `▸ Ready — everything looks up to date`; running+allClean `▸ Checking — scanning for changes…`.
8. **Sync filtreyi KORUR, seçimi temizler; Build/Retry ikisini de temizler** (asimetri).
9. Liste satır stagger'ı: satır başına 10ms, tavan 380ms (grafta 55ms/katman, tavan 330ms).
10. Varsayılan kamera merkezi y=H×0.3; pan 12px kenar payı; tx/ty Math.round.
11. Seçili karta scroll 90ms gecikme + %35 offset (follow-mode %30'dan farklı).
12. Fail shake yalnız hata anından itibaren 700ms penceresinde tetiklenir.
13. Stream aktif satırı, izlenen proje bitince EN SON başlayan building projeye atlar.
14. ETA'da building varken +400ms sabit ek; gösterim 5s'e yuvarlanır.
15. latest pill 560ms "jumping" penceresi; pill hover'da yüzey/metin bir adım açılır.
16. Render dilimleri: konsol son 200 satır, stream son 150 olay (tamponlar 240/260).
17. Settings'te BOŞ regex geçerli (hiçbir şeyle eşleşmez); Save yalnız derlenemeyen regex/boş adla kilitlenir.
18. Worktree auto-ad: `branch/`→`-` slug + aynı prefix'li mevcut sayısı+1; silinince konsola dim not; silinen seçiliyse auto'ya döner.
19. Liste satırı tabIndex=0 + Enter=seçim toggle.
20. **Engine kadansı:** 120ms UI timer; gerçek-zaman deltası 20sn'ye clamp + ≤150ms substep'ler (arka plan/throttle dayanıklılığı) — WPF DispatcherTimer eşleniğinde birebir gerekli.
21. Branch popover arama metni kapanınca sıfırlanır.
22. all-clean koşusunda progress = skipped/36 oranı (done'da 100).
23. "N lines/N events" sayaçları render dilimini değil TAM tampon uzunluğunu gösterir.
24. Hover aksiyon konsol notları birebir metinli: `{name}.csproj revealed in Explorer` / `{name} opened in Visual Studio`.
25. Build menüsünde stopped'ta "Build" maddesinin açıklaması "Start over — only changed projects" olur (kbd rozetsiz).

---

# Ek B — Analiz Metodolojisi ve Güven Notu

- 21 agent: 1 davranış envanteri (BuildApp.jsx satır satır) + 1 çelişki tarayıcı + 7 fizibilite analisti + 4 tamamlayıcı analist (kritik eksik kapama) + 7 şüpheci doğrulayıcı + 1 eksik-kapsam kritiği. Doğrulayıcılar iddiaları ÇÜRÜTMEK üzere prompt'landı ve WebSearch ile API gerçeklerini teyit etti (MS Learn, dotnet/wpf issue'ları, AvalonEdit issue'ları, WASDK release notes).
- ~60 doğrulama kararı: 54 doğrulandı, 6 düzeltildi (§4) — düzeltmelerin hiçbiri "uygulanabilir" sonucunu değiştirmiyor; implementasyon hatası önlüyor.
- Kritiğin işaret ettiği 6 boşluktan 4'ü ayrıca analiz edildi (§3.7-3.10); kalan 1'i iş kalemi olarak listelendi (U13 klavye/focus mimarisi), 1'i (arama inputu) Ek A-5'te spec'e bağlandı.
