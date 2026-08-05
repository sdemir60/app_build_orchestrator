# Graf "Canlı Kamera" Tasarımı (Plan A)

Tarih: 2026-08-05 · Karar sahibi: kullanıcı (Plan A — tam paket onayı + "kısa bekleme" takip dönüşü)
İlgili kod: `src/BuildOrchestrator.App/Graph/` (GraphCamera, GraphView, EdgeStyleResolver, GraphLayout, GraphCulling)

## 1. Problem

177 proje / 1214 bağımlılıkta graf paneli okunmaz bir yumağa dönüşüyor. Kök nedenler kodda doğrulandı:

1. **Kamera hiç yakınlaşmıyor.** `GraphCamera.FitScale` ölçeği her zaman TÜM grafı panele sığdırır ve
   0.68–1.08'e kıstırır; odak (`ResolveFocus`) yalnız pan'i değiştirir. 177 projede tuval ~2.000px →
   ölçek tabana yapışır, 26px kare ekranda ~18px kalır.
2. **Etiketler statik LOD ile düşer.** `ShowsLabel` kararı `SetGraph` anında katman aralığına göre BİR KEZ
   verilir (`GraphView.SetGraph` → `GraphLayout.LabelsFit`); zoom'dan bağımsızdır. Kalabalık katmanda aralık
   34.4px < etiket ~70–100px → etiket hiç kurulmaz.
3. **1214 kenarın tamamı 0.8 opaklıkta.** `EdgeStyleResolver.Resolve` idle kenara `Brush.Border`/0.8 verir;
   yalnız seçim varken 0.16'ya iner. Sakin görünümün görsel dili tasarımda var ama yalnız seçimde devrede.

Altyapı hazır: viewport culling kamera dikdörtgenine göre çalışır (yakınlaşınca materyalize küme küçülür),
statü tick'i "değişmediyse dokunma" fast-path'li, akan dash'ler tek paylaşımlı clock'ta (ARCHITECTURE §13.6,
§20 ölçümleri). Eksik olan teknoloji değil; kamera politikası + kenar yoğunluk politikası + etkileşim.

## 2. Verilen kararlar

- **Plan A — tam paket** seçildi: kenar sisi + follow-zoom + zoom-aware etiket + drag/wheel etkileşimi.
- **Takip dönüşü: kısa bekleme (4 sn).** Ayrıntılı kural §3.5'te.
- **Küre/3D reddedildi.** ARCHITECTURE §14.7 "a rotating decorative globe"u isimle yasaklar; katman = build
  sırası sözleşmesi kürede kaybolur; sürekli dönüş "boşta hiçbir animasyon çalışmaz" ilkesine aykırıdır.
- **Fisheye/lens reddedildi.** Layout anime eder — motion kuralı (yalnız transform/opacity) ihlali.
- Katmanlı DAG yerleşimi, odak zinciri (seçili → frontier → merkez), Esc zinciri (§13.7) ve 460ms/ease-in-out
  kamera geçiş dili **değişmez**.

## 3. Tasarım

### 3.0 Tek kapı: sinema modu

Yeni davranışların TAMAMI tek bir kapıya bağlıdır: **sinema modu**, graf panele okunur ölçekte sığmıyorken —
`FitScale(viewport, graph) < 0.9` (sabit: `CinemaEngageFitScale`, tek yerde) — etkindir. Sığan graflarda
(bugünkü ~36 düğümlük tasarım hedefi dahil) kamera, kenarlar, etiketler ve jestler **birebir bugünkü gibi**
kalır; bu, testlerle pinlenen yapısal bir garantidir. Pencere yeniden boyutlanınca kapı yeniden değerlendirilir.

`FullDetailMaxNodes` (150) AYRI bir eşiktir ve anlamı değişmez: o, nesne KURULUM maliyetinin kapısıdır
(cull + kurulum LOD'u). İki kapının alanları farklıdır: biri geometri/okunurluk, öteki inşa maliyeti.

### 3.1 Kamera politikası — `GraphCamera` (saf aritmetik)

Sinema modunda ölçek, odağın yanında hedefin parçası olur:

| Kip | Odak | Ölçek hedefi |
|---|---|---|
| Takip (frontier) | Bugünkü COG + 8px eşiği | Frontier bbox'ını viewport'a çerçeveleyen değer, **[0.85, 1.4]** bandına kıstırılmış |
| Seçim | Seçili düğüm (bugünkü) | Sabit **1.1** |
| Settled/idle | Bugünkü merkez kuralları | Bugünkü `FitScale` (0.68 tabanı — büyük graf sığmaz, merkeze pan'lenir; bugünkü davranış) |
| Manuel | Kullanıcı | Kullanıcı; bant **[0.45, 2.0]** |

- Frontier bbox'ı building düğüm merkezlerinden kurulur; her yana hücre payı (`NodeCellWidth/2` yatay,
  `NodeSize/2 + LabelGap + LabelHeight` dikey) + `FitPadding` eklenir. Çerçeve COG odaklıdır; çok geniş bir
  cephe 0.85 tabanında tamamen sığmayabilir — kabul edilir (ağırlık merkezi görünür kalır).
- **Zeno koruması genişler:** mevcut 8px odak eşiğinin yanına ölçek için küçük-sapma eşiği (hedef ölçek
  değişimi < **0.05** ise yeniden hedefleme yok) eklenir.
- Fonksiyonlar saf kalır (WPF'e dokunmaz); tüm bantlar/eşikler `GraphCamera` sabitlerinde tek yerde, test pinli.
- Sinema modu dışında ölçek bugünkü gibi yalnız `FitScale`'dir.

### 3.2 Kenar sisi — `EdgeStyleResolver` (saf kural)

Sinema modunda ve **seçim yokken** devreye girer (kapı bilgisi `Resolve`'a parametre olarak iner):

| Kenar | Bugün | Sisli |
|---|---|---|
| Akan (hedef building) | amber 0.85, akan dash | değişmez |
| Hata dalı (kaynak failed/dep-issue) | kırmızı 0.95, statik dash | değişmez |
| Succeeded/failed'e varan renkli | 0.8 | **0.35** |
| Idle (`Brush.Border`) | 0.8 | **0.16** |

- 0.16, seçim-dim değeriyle AYNI sabittir ve **tek kaynaktan** tanımlanır (kopya yasak — bugün `Resolve`
  içinde iki kez inline 0.16 var; sabite çıkarılır, her dal onu okur).
- Seçim varken bugünkü dim kuralları aynen kazanır; sinema modu dışında hiçbir kenar stili değişmez.
- `EdgeStyle` record eşitliği ve fast-path aynen çalışır; akan-dash clock kablajı değişmez.

### 3.3 Zoom'a duyarlı etiketler — `GraphLayout` + `GraphView`

Sinema modunda etiket kararı ölçeğe bağlanır (sinema dışı: bugünkü statik karar aynen):

- Oran `r = (katman aralığı × kameranın HEDEF ölçeği) / etiketin ölçülen genişliği` üzerinden: etiket
  `r ≥ 1.0` olunca belirir; görünür bir etiket `r < 0.85` olmadıkça kalır (histerezis — titreme yok;
  iki katsayı sabit, tek yerde).
- **Değerlendirme anı:** her karede DEĞİL — kamera yeniden hedeflendiğinde ve düğüm materyalize olurken.
  Yalnız materyalize düğümler gezilir; katman başına ölçülen genişlik (`MeasureLayerLabelWidths`) önbellekte.
- Etiket TextBlock'u tembel kurulur; gizlenirken `Collapsed` yapılır (nesne atılmaz — churn yok). Etiketi
  görünmeyen düğümün tam-ad tooltip'i korunur (mevcut davranış).
- `LabelsFit` ölçek parametresi alan saf fonksiyona genişler.
- Etiket belirmesi motion sözleşmesine tabidir: mevcut opacity dilinde, reduced-motion'da ani.

### 3.4 Jestler — drag / wheel (`GraphView`, yalnız sinema modunda)

- **Sürükleme:** boş zeminde (`Ground`) sol tuş + platform drag eşiği
  (`SystemParameters.MinimumHorizontalDragDistance`) aşılınca pan başlar; sürüklerken imleç **el**
  (`Cursors.Hand`), bırakınca normale döner; mouse capture kullanılır. Eşik aşılmadan bırakılırsa bugünkü
  "boş alana tıkla → seçim kalkar" davranışı çalışır (bugün `MouseLeftButtonDown`'da olan seçim kaldırma
  click-vs-drag ayrımı için release'e taşınır; sinema dışı graf da dahil davranış sonucu aynı kalır).
- **Wheel:** imleç merkezli zoom (imlecin altındaki dünya noktası sabit kalır); çarpansal adım **1.1/kademe**
  (sabit); manuel bant [0.45, 2.0].
- Manuel pan/zoom SIRASINDA culling çalışmaya devam eder — `UpdateMaterialization` canlı kamera
  dikdörtgeniyle beslenir (taranan-bölge optimizasyonu aynen geçerli).
- Düğüm tıklama/seçim davranışı aynen kalır.

### 3.5 Takip dönüşü + gösterge

Tek kural: **takip, hedef varken (koşu sürüyor VEYA seçim var) ve son manuel girdiden bu yana ≥ 4 sn
geçmişse kamerayı hedefler; aksi halde manuel kamera korunur.**

Sonuçları:

- Koşu sırasında drag/wheel takibi askıya alır; bırakıp 4 sn dokunmayınca takip kaldığı yerden animasyonla
  döner (reduced-motion'da ani).
- Settled + seçimsiz gezinme KALICIDIR — hedef yokken kamera kullanıcıyla kavga etmez. Yeni koşu başladığında
  (4 sn çoktan geçmiş olacağından) takip hemen devreye girer.
- Süre tek sabit (`FollowResumeDelayMs = 4000`), tek atımlık ve her manuel girdide yeniden kurulan
  `DispatcherTimer`; zaman test-enjekte edilebilir (mevcut `TickCount64`/seam desenleri).
- **Gösterge:** takip askıdayken (hedef var + süre dolmadı ya da sürükleme sürüyor) panel başlığında sönük bir
  **FOLLOW PAUSED** pili görünür; tıklanırsa takip hemen döner. UI metni İngilizce; UIA adı merkezi ad
  tablosundan; yeni XAML kökü → realize testi zorunlu.
- **Esc zinciri değişmez**; takip Esc'e bağlanmaz.

### 3.6 Performans sözleşmesi

- Sürekli çalışan YENİ hiçbir şey eklenmez: dönüş tek atımlık timer'dır; sis ve etiket kararları mevcut
  fast-path'lerin (record eşitliği, "değişmediyse dokunma") içinde kalır.
- Yakınlaşma culling kümesini KÜÇÜLTÜR — takip modunda materyalize edilen düğüm/kenar sayısı bugünden azdır.
- Etiket kurulumu görünür kümeyle sınırlıdır; ölçüm katman başına önbellekten okunur.
- Motion kuralları: yalnız transform/opacity; süre/eğri token'lardan; reduced-motion her animasyon başında
  taze okunur; dekoratif sonsuz animasyonlara yenisi eklenmez.

## 4. Test stratejisi (kırmızı-önce)

- **Saf birim:** sinema kapısı (fit ölçeği eşiği, resize ile giriş/çıkış); frontier bbox → ölçek çerçeveleme
  + bant kelepçeleri; ölçek Zeno eşiği; seçim ölçeği; settled'ın bugünkü `FitScale`'e denkliği; sinema dışı
  kipte bugünkü kameranın birebir korunumu; sis matrisi (sinema × statü × seçim); `LabelsFit(scale)` +
  histerezis.
- **WPF/STA:** wheel → imleç altı dünya noktasının sabitliği; drag → pan + el imleci + seçim korunumu;
  eşik altı tıklama → seçim kalkar; manuel modda `ApplyCamera`'nın hedeflememesi; zaman enjeksiyonuyla 4 sn
  dönüşü; settled+seçimsiz manuel kalıcılığı; manuel pan sırasında materyalizasyon; zoom-in'de etiket
  belirmesi/histerezisi; pil görünürlüğü + tıklamayla anında dönüş; sinema dışı grafta jestlerin kapalılığı.
- **Realize:** FOLLOW PAUSED pili (yeni XAML kökü kuralı).
- **Guard'lar:** token/motion/D8/İngilizce-metin guard'ları mevcut süitte — yeni kod onlara tabi.
- Davranışı bilerek değişen eski testler (örn. kamera ölçeğini her durumda fit varsayanlar) YENİ kuralı
  pinleyecek şekilde yeniden yazılır; doc'una eski iddia + değişme gerekçesi işlenir (CLAUDE.md kuralı).

## 5. Uygulama sırası (her adım bağımsız yeşil + commit)

1. **Sinema kapısı + kenar sisi** — kapı sabiti + resolver kuralı + testler (en büyük görsel kazanç).
2. **Follow-zoom kamerası** — GraphCamera ölçek politikası + GraphView entegrasyonu.
3. **Zoom-aware etiketler** — `LabelsFit(scale)` + tembel etiket + histerezis.
4. **Jestler + takip dönüşü** — drag/wheel + manuel mod + 4 sn kural + FOLLOW PAUSED pili.
5. **Doküman:** ARCHITECTURE §13.6 (kamera/sis/etiket/jest anlatısı yerinde yeniden yazılır), README (yeni
   jestler) — changelog dili olmadan.

## 6. Kapsam dışı

- Küre/3D görünüm, fisheye/lens, PiP şeridi (reddedildi — §2).
- `DrawingVisual` katman göçü (ölçümle reddedilmiş yön — §13.6; bu iş onu gerektirmez).
- Kenar stillerinin CANLI zoom değerine bağlanması (fast-path'i bozar; sis, durum + kapı kuralıdır).
- Düğüm boyutunun (26px) değiştirilmesi — büyüklük hissi zoom'la sağlanır, yerleşim sözleşmesi bozulmaz.
- Klavyeyle graf gezinmesi (bilinen sınır §20, ayrı iş).
