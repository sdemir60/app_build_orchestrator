# TrayBuildIndicator

Ürün işaretinin animasyonlu hâli: pencere tepsideyken bir derleme koşarken ekranın sağ alt köşesinde 3 saniyelik bir döngüyle şevron şeritleri siler, durur ve sağa süzülerek kaybolur.

Uygulamada `Controls/TrayBuildIndicator.xaml(.cs)` (işaret ve döngü), `Views/TrayBuildOverlayWindow` (pencere, konum) ve `Services/TrayBuildIndicatorController` (ne zaman görünür, nasıl biter) — ARCHITECTURE §12.3, §14.5. Bu klasör bir bundle bileşeni değildir (`bundle.js`'te yok); `preview.html` uygulamanın storyboard'unu SVG + CSS keyframe olarak birebir taşır ve kaynak olarak kopyalanır.

## Ne zaman görünür

- Yalnız **pencere gizliyken** (tepside) **ve** bir derleme koşusu aktifken: `Starting` · `Running` · `Stopping`. Sync kapsam dışıdır.
- Koşu sürerken pencere tepsiye inerse hemen belirir; giriş animasyonu yoktur — döngünün ilk karesi zaten boştur. Pencere tepsideyken başlayan derleme (`Ctrl+Shift+Space`) de göstergeyi alır.
- Pencere geri gelirse **anında** gizlenir: çıkış animasyonu oynamaz, balon üretilmez.
- *Show notifications* göstergeyi etkilemez; yalnız balonu susturur.

## Ölçü ve konum

| | |
|---|---|
| Bant (çizim alanı) | 375 × 134 birim — iç koordinatta x −30..345, y 76..210 (`viewBox="-30 76 375 134"`) |
| Ekrandaki boyut | bandın 0.5 katı: **187.5 × 67 DIP** (tek ölçek, `Scale = 0.5`) |
| Konum | **birincil** ekranın çalışma alanının sağ alt köşesi; sağ pay **4**, alt pay **12** DIP (görev çubuğunun üstünde) |
| Zemin | yok — şeffaf, çerçevesiz, odak almayan, Alt-Tab'da görünmeyen pencere |
| Tıklama | çizili piksele sol tık pencereyi tepsi ikonuyla aynı yoldan geri getirir (el imleci); şeffaf alan tıklamayı alttaki pencereye geçirir |

Duruş karesinde işaret bandın solundadır; sağdaki boşluk şevronun çıkış yoludur, bu yüzden sağ pay alttakinden dardır. Köşe her gösterimde ekranın o anki çalışma alanından ve ölçeğinden yeniden hesaplanır.

## Parçalar

| Parça | Geometri (iç koordinat) | Renk |
|---|---|---|
| Üst koyu şerit | `73.5,83` 35 × 21, r 10.5 | `brand-strip-dim` |
| Amber şerit | `119.5,83` 51 × 21, r 10.5 | `amber` |
| Orta koyu şerit | `51.5,131` 39 × 21, r 10.5 | `neutral-700` |
| Beyaz şerit | `102.5,131` 60 × 22, r 11 | `text-primary` |
| Gümüş şerit | `65.5,178` 57 × 21, r 10.5 | `text-secondary` |
| Şevron | `app-mark.svg`'nin path'i | gradyan `amber-bright → amber (.5) → brand-chevron-deep`, (161.5,82) → (182.5,204), şevronla birlikte kayar |

Şekiller `app-mark.svg` ile aynıdır (uygulamada tek kaynak `BrandGeometry.xaml`). Gölgeler: şeritler grubu aşağı 2, blur 4.8, siyah %30; şevron aşağı 2.6, blur 5.6, siyah %32 — zemin olmadığı için şeritler her masaüstünde seçilir. Şerit gölgesi kırpmadan **önce** hesaplanır (ayrı katman); aynı öğeye konursa süpürme kenarında gölge çizgisi kalır.

## Döngü — 3.000 s

**Giriş (0–1.34 s):** şevron en solda (X −170) soluk belirir ve sağa süzülür. Şevronun siluetini izleyen bir kaplama (maske) onunla **birebir aynı** keyframe'lerle kayar ve şeritleri silerek açar; şeritler x 30.5'te üst üste başlar ve kendi gecikmeleriyle yerine oturur. **Duruş (1.34–2.10 s):** logo karesi. **Çıkış (2.10–3.00 s):** şevron önce hareket eder ve hızlanarak uzaklaşır; şeritler farklı mesafelerle peşinden gider, hepsinin sağ ucu x 250.5'te yok olur. Her parçanın solması 340 ms'dir ve kendi hareketinin bittiği anda 0'a iner. Son şerit 3.000 s'de kaybolur ve döngü o anda başa döner — boş kare yoktur.

| Parça | Giriş X | Bekler → | Yerine oturur (eğri) | Çıkış başlar | Çıkış biter, X (eğri) | Solma |
|---|---|---|---|---|---|---|
| Şevron | −170 | 0.100 | 1.150 `(.14,.62,.2,1)` | 2.100 | 2.800, +110 `(.34,0,.68,.6)` | 2.460–2.800 |
| Kaplama (maske) | −170 | 0.100 | 1.150 `(.14,.62,.2,1)` | 2.100 | 2.800, +260 `(.34,0,.68,.6)` | — |
| Amber | −89 | 0.200 | 1.180 `(.16,.7,.22,1)` | 2.130 | 2.780, +80 `(.42,0,.75,.5)` | 2.440–2.780 |
| Beyaz | −72 | 0.260 | 1.220 `(.12,.66,.18,1)` | 2.175 | 2.840, +88 `(.42,0,.75,.5)` | 2.500–2.840 |
| Üst koyu | −43 | 0.300 | 1.260 `(.16,.7,.22,1)` | 2.225 | 2.890, +142 `(.45,0,.78,.52)` | 2.550–2.890 |
| Gümüş | −35 | 0.360 | 1.300 `(.12,.66,.18,1)` | 2.275 | 2.940, +128 `(.45,0,.78,.52)` | 2.600–2.940 |
| Orta koyu | −21 | 0.400 | 1.340 `(.16,.7,.22,1)` | 2.325 | 3.000, +160 `(.42,0,.75,.5)` | 2.660–3.000 |

- Eğriler WPF `KeySpline`'dır ve CSS'e aynı dört sayıyla `cubic-bezier` olarak geçer; WPF'te eğri **bittiği** keyframe'in segmentine, CSS'te **başladığı** keyframe'e yazılır. Bekleme ve duruş doğrusaldır (sabit değer).
- Şevron opaklığı 0 → 1, 0.000–0.300 `(.32,.5,.4,1)`; tüm solmalar `(.45,.15,.6,1)`.
- **Maske yalnız girişe aittir:** turun başında konur ve kaplama yerine oturduğu an (1.150) düşer; turun geri kalanında şeritlerde kırpma yoktur (aksi halde çıkışta amber şeridin şevron altında kalan ucu dik kesilmiş görünürdü). Çıkışta kaplama şevrondan uzağa (+260) gider ki bir şerit kenarına takılmasın.
- Zaman çizelgesi bir tasarım token'ı **değildir** — teslim edilmiş bir sanat eseridir, `duration-*` rampasına bağlanmaz; değerler yeniden yorumlanmaz.
- Uygulama döngüyü 30 kare/sn ile sürer (dekoratif animasyon); önizleme tarayıcının kare hızındadır.

## Statik kare (reduced motion)

Animasyonlar kapalıyken döngü kısaltılmaz, **hiç başlamaz**: gösterge duruş karesini (her parça yerinde, tam opak, maskesiz — `app-mark.svg` ile aynı kare) gösterir. Ayrı bir statik çizim yoktur; parçaların taban değerleri zaten duruş karesidir. `preview.html` bunu `prefers-reduced-motion: reduce` ile yapar.

## Bitiş koreografisi

1. Koşu biter (aktif kümeden çıkılır): gösterge **yarıda kesilmez** — yeni tur başlatmaz, içinde bulunduğu turu sonuna (3.000 s) kadar oynatır; çıkış evresi tamamlanır.
2. Son karede pencere gizlenir.
3. Kısa bir nefes: `duration-slow` (280 ms) — gizlenme ile balon üst üste binmesin diye. Reduced motion'da 0'dır; orada çıkış evresi de yoktur, sıra hemen akar.
4. *Show notifications* açıksa OS balonu: şeridin terminal satırı (başlık = satırın başı, gövde = gerisi), uygulama ikonunun köşesinde sonuç rozeti. Koşu başına tek balon.

Koşu giriş evresindeyken biterse bitiş en çok bir tur (3 s) bekler. Çıkış sürerken pencere geri gelirse gösterge anında gizlenir; koşu tepsideyken bittiği için balon yine bir kez gelir. Koşu pencere açıkken biterse balon yoktur — şerit zaten ekrandadır.

## Sayaç

Yok. Tasarım teslimatının sayaçlı bir varyantı vardı (beyaz şeritte `139/248`); bu ölçekte okunmadığı için uygulamadan kaldırıldı. Beyaz şerit markanın kendi pill'idir (60 birim).

## Tüketici sağlar

- **Görünürlük:** pencere gizli mi + koşu fazı (aktif küme). Gösterge kendisi karar vermez.
- **Reduced motion sinyali:** gösterge ekrandayken değişirse mod yerinde takas edilir (döngü ↔ statik kare).
- **Bitiş isteği ve geri çağrı:** "yeni tur başlatma, bu turu bitir, sonra haber ver" — sonra gizleme, nefes ve balon tüketicinin sırasıdır.
- **Geri getirme eylemi:** çizili piksele tık — tepsi ikonuyla aynı yol.
- **Balon metni:** göstergeden üretilmez; şeridin terminal satırı aynen taşınır.
- Renkler token'dan (`brand-strip-dim`, `neutral-700`, `text-primary`, `text-secondary`, `amber`, `amber-bright`, `brand-chevron-deep`); ölçüler bileşenin kendi ölçüleridir, token değildir.
