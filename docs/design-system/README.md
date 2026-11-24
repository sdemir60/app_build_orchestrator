# Build Orchestrator design system

Build Orchestrator'ın görsel dili: token'lar, marka kitabı, bileşenler ve logolar. Bu klasör tasarımın kaynağıdır: prototip ([docs/design](../design/README.md)) bunu yükler; uygulamanın WPF kaynakları (`src/BuildOrchestrator.App/Resources/Tokens.xaml`, `Motion.xaml`, `Icons.xaml`) buradaki değerleri taşır. Claude artifact'i *Build Orchestrator - Design System* bunun gezilebilir kopyasıdır ve bu klasörle aynı tutulur. Her değişiklik [VERSIONS.md](VERSIONS.md)'de bir sürümdür; **2.0.0 milattır**: sistem uygulamanın kodundan (uygulama 1.15.0) yeniden kuruldu.

| Dosya | Ne tutar |
|---|---|
| [tokens.json](tokens.json) | Her token, değeri ve kullanımı: renk, tipografi, boşluk, radius, gölge, yerleşim yükseklikleri, süre, easing. Kaynak budur. |
| [tokens.css](tokens.css) | Aynı token'lar CSS custom property olarak, tip stilleri sınıf olarak; JSON'dan üretilir. |
| [components/bundle.css](components/bundle.css) | Font yüklemesi, temel gövde stili, odak halkası ve reduced-motion kuralı. |
| [components/bundle.js](components/bundle.js) | Bileşenlerin çalışan hâli, `window.DeltaBuildOrchestratorDS_eb0bd1`; prototip ve artifact önizlemeleri bunu yükler. Uygulama yüklemez. |
| [components/index.d.ts](components/index.d.ts) | Bileşenlerin props sözleşmeleri (belge; tip denetimi yapılmaz). |
| `components/<Ad>/` | Her bileşen için `README.md` (kurallar) ve `preview.html` (canlı örnek). `Cover/` artifact'in kapak görselidir. |
| [assets/Logos](assets/Logos/README.md), [assets/App Icons](assets/App%20Icons/README.md) | Ürün markası, firma logosu ve uygulama ikonları (SVG). |

Örneklerdeki adlar temsilidir (`ACME.*`, `Acme.sln`, `D:\src\ACME`): repo herkese açıktır.

## Sürüm yapmak ve koda geçirmek

1. Klasörü değiştir (`tokens.json` ve ondan `tokens.css`, bileşenin README'si ve önizlemesi, gerekirse `bundle.js`) ve [VERSIONS.md](VERSIONS.md)'nin en üstüne sürümün kaydını yaz: numara, tarih, başlık; neden; ne değişti; **uygulamaya ne düşüyor** (hangi XAML kaynağı, hangi değer). Bir bileşen, token, varlık ya da marka kitabı kuralı eklenir ya da değişirse minor; düzeltme patch; major yalnız proje sahibinin kararıyla (milat).
2. Artifact'i bu klasörden yeniden yayınla; prototipteki kopyasını (`docs/design` paketinin `prototype/ds/` klasörü ve tasarım artifact'i) aynı işte güncelle.
3. Kod tarafı sürümü uygular: `Resources/*.xaml` değerleri burayla eşitlenir, commit ve CHANGELOG işi `design-system X.Y.Z` adıyla anılır; VERSIONS kaydının altına `Uygulandı: app X.Y.Z` satırı eklenir.

---

Delta Yazılım'ın dahili geliştirici aracı **Build Orchestrator** için tasarım sistemi — sürüm **2.0.0**.

## Bağlam

**Build Orchestrator**, büyük, çok-projeli bir .NET çözümünü bağımlılık sırasına göre, paralel ve yalnızca değişen projeleri derleyip canlı gösteren dahili bir geliştirici aracıdır: **dark, modern, veri-yoğun bir Windows masaüstü uygulaması** (WPF). Marka tonu: veri-odaklı, profesyonel, sakin ve hassas — gösteriş değil, güven.

## Kimlik özeti

- **Near-black dark tema.** TEK marka accent'i **Delta amber `#eda10f`**. Amber dışında hiçbir dekoratif renk yok.
- **Renk yalnızca DURUM taşır** — tek bir hikâye: projenin çıktısının durumu. Hiyerarşi renkle değil **ağırlıkla** kurulur: boyut, kontrast, konum.
- **Statü = renk + glyph + metin** — üçü birden, her zaman (colorblind-safe). Emoji asla.
- Yapıyı **hairline border** taşır, gölge değil. Elevation kıt: gölge yalnız floating overlay'de.
- Motion sakin: bounce/overshoot yok, aynı anda en fazla 1 hero motion, OS reduced-motion'a uyar.

---

## CONTENT FUNDAMENTALS

**Dil:** Tüm UI metni, proje adları ve loglar **İngilizce**. Uygulamada bir guard testi, kullanıcıya görünen bir metne Türkçe girerse build'i kırar; tek istisna telif satırındaki tescilli firma adıdır (`Delta Yazılım`) ve yalnız adı kapsar. Dosya adları, komutlar, SHA'lar olduğu gibi yazılır.

**Ton:** sakin, kesin, mühendisçe. Pazarlama sesi yok, ünlem yok, espri yok. Uygulama güven verir çünkü **net rakam ve net durum** söyler: `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`.

**Hitap:** eylemlerde kısa emir kipi (`Build`, `Sync`, `Stop`, `Rebuild`, `Resolve cycles`, `Restart to update`). Sistem mesajları nesnel ve kısa (`Engine ready — v1.15.0`, `Ready — 6 to build`, `Cancelled — build not started`).

**Casing:** cümle düzeni (sentence case). SCREAMING CAPS yalnız 11px panel etiketlerinde (`DEPENDENCY GRAPH`, `PROJECTS`, `CONSOLE`, `EVENT STREAM`), `--tracking-caps` ile.

**Sayılar ve süreler:** daima Geist Mono + tabular. Süre `4.2s`, `1m 12s`; sayaç `14/38`; SHA 7 hane `a3f81c2`. Ondalık ayracı nokta.

**Emoji:** ASLA. Statü daima glyph + renk + metinle.

**Örnek kopya (uygulamadan):**
- Buton: `Build` · `Sync` · `Stop` · `Rebuild` · `Clean`
- Özet: `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s` · `Stopped — 4 remaining projects queued`
- Boş durum: `No projects` · `No projects match this filter.`
- Uyarı tooltip'i: `In a dependency cycle` · `Dependency issue: ACME.Sales.Core +2`
- Örnek veri: `ACME.*` projeleri, `Acme.sln`, kök `D:\src\ACME`.

---

## VISUAL FOUNDATIONS

**Renk:** near-black, hafif sıcak nötr rampa (`#0a0a0c → #ededee`). Zeminler: sunken/base/surface/raised/overlay + en koyu `--console-bg #060608`. Amber tek accent. Statü tonları: success yeşil, fail kırmızı, building/marked/queued amber ailesi, gri (derlenecek / başlangıç modu). Her statünün 4 tonu: çekirdek, `-text` (≥4.5:1), `-soft` (%10-12 zemin), `-border` (%24-32). Dekoratif gradient, mor/indigo, blob YASAK — tek istisna ürün işaretinin chevron gradyanıdır (logo çizimi, arayüz yüzeyi değil).

**Tipografi:** UI = **Geist**, makine çıktısı (console, süre, SHA, sayaç, yol) = **Geist Mono**, daima tabular rakam. Ölçek 11 / 12 / **13 (base)** / 14 / 16 / 20 / 26 / 34. Başlık 600, vurgu 500, gövde 400; konsol gövdesi Geist Mono **300 (Light)**, 12px / 1.55. Caps etiketler 11px / 500 / `--tracking-caps 0.07em`, `text-faint`. Uzun okuma metni (About, What's new) 1.62 satır yüksekliği. Mono asla dekoratif kullanılmaz.

**Boşluk:** 4px grid (`--space-1…16`). Yoğunluk yüksek: satır 36px (compact 30). Pencere iskeleti: title bar 40 · ribbon 32 · progress 2 · panel başlığı 28 · katman başlığı 24 · action bar 42; pencere minimumu 1240 × 620.

**Arka planlar:** düz koyu yüzeyler; görsel/foto/pattern/texture yok. Derinlik yalnız yüzey kademeleriyle.

**Radius:** chip/tag/kbd 3 · kontroller 4 · kart/panel 6 · overlay (menü/dialog) ve pencere kökü 8 · **console daima 0 (keskin)**. Şişik radius yok.

**Border:** hairline 1px her yapının taşıyıcısı — `subtle` iç bölücü, `border` panel, `strong` etkileşimli kontrol ve overlay kenarı. Kart = surface zemin + 1px border + radius-md, **gölgesiz**.

**Gölge:** yalnız floating overlay: `--elevation-overlay` (dialog/menü), `--elevation-popover` (tooltip, küçük popover). WPF gölgeyi tek katman ve spread'siz çizer; token'lar CSS karşılığıdır.

**Hover:** zemin bir yüzey adımı açılır; renk tonu ve boyut değişmez. **Press:** bir adım koyulaşır (amber'da `--amber-dim`). **Focus:** 2px `--focus-ring` amber halka (%50), offset 1px.

**Seçim:** satırda zemin `--surface-raised`; graf node'unda amber halka. Metin seçimi `--amber-soft-hover`.

**Transparanlık/blur:** yok. Scrim `rgba(4,4,6,.6)` düz; backdrop-blur kullanılmaz.

**Disabled:** %45 opaklık, layout sabit.

---

## Statü sözlüğü

Statü daima **renk + glyph + metin**; her metin tonu (dim dahil) 4.5:1'i karşılar. **Renk tek kanaldır:** satırın şeridi, adın yanındaki nokta, statü glyph'i, graf node'unun kenarı ve içindeki küp tek bir değerden (görsel durum) boyanır ve tek bir şey söyler: *projenin çıktısının durumu*. Temel katman kararın kendisidir (güncel / derlenecek / kanıtlı hata / henüz karar yok); üstüne koşu biner (işaretli, kuyrukta, derleniyor, sonuç).

| Durum | Glyph | Metin | Renk |
|---|---|---|---|
| Henüz karar yok (başlangıç modu) | kesikli daire | `Not synced` | gri; satırda tam opak düz şerit + dört yaylı halka, grafta kesikli kenar |
| Derlenecek | kesikli daire | `To build` | gri |
| İşaretli (işaretleme dalgası) | kesikli daire | `Marked to build` | amber |
| Güncel | halka içinde ✓ | `Up to date` | yeşil |
| Kuyrukta | saat | `Queued` | amber (kuyruk operasyonun kapsamıdır, sonuç değil) |
| Derleniyor | dönen kesikli halka | `Building` | amber |
| Bu koşuda derlendi | halka içinde ✓ | `Up to date` | yeşil |
| Başarısız (kanıtlı derleyici hatası) | halka içinde ✗ | `Failed` | kırmızı |
| Atlandı — yalnız koşu hikâyesi | halka içinde — | `Skipped` | `text-dim` |

- **Kırmızı yalnız kanıttır:** derleyici hatası. Timeout, Stop ya da çağrı hatası kırmızı yazmaz; proje gri `To build`'e döner.
- **Atlanmak bir renk değildir.** `—` yalnız event stream'in atlama satırında görünür; liste satırı, graf node'u ve sayaç taşımaz — atlanan proje kendi durumunun rengini korur. `N skipped` ribbon özetinde sayılır.
- **Uyarı üçgeni:** satırın sabit 14px slotunda, statüsüz, her zaman amber (`--amber-text`), konturlu Lucide *alert-triangle*. Tooltip tek satır: `In a dependency cycle` ya da `Dependency issue: ACME.Sales.Core +2`. Kırmızı dolu üçgen kullanılmaz; gerekçe proje logundadır. Grafta üçgen yoktur — döngü üyesinin içindeki **küp her durumda amber**dır.
- **Kaldırılan iki kanal:** turuncu döngü rengi ve ayrı will-build noktası uygulamada artık kullanılmaz. `--status-cycle*` ve `--dot-*` token'ları **uyumluluk için** durur; `StatusGlyph`/`DependencyGraphNode`'un `cycle` ve `skipped` değerleri ile `WillBuildDot` bileşeni de aynı sebeple sistemdedir. 8px nokta artık görsel durumun rengini taşır.

---

## ICONOGRAPHY

- **İkon sistemi:** [Lucide](https://lucide.dev) geometrisi — 1.5–2px stroke, tek renk (`currentColor`), 12–16px. Uygulamada XAML geometrisi olarak tek sözlükte (`Icons.xaml`) yaşar; bileşenler burada inline stroke SVG gömer. İkon fontu ya da sprite yok; yeni ikon gerekirse Lucide'den path kopyala, stroke kalınlığını koru.
- **Statü glyph'leri:** ince halkalı daire içinde ✓ / ✗ / saat; başlangıç modunda kesikli daire. **Building spinner ayrı bir çizim değildir:** aynı kesikli halkanın amber, 1.4s lineer dönen hali.
- **Ortak aileler:** Build menüsü ve satır menüsü tek ikon ailesini paylaşır (play, rotate-cw, brush). Title bar küme ikonları (layout seçicileri, dişli, What's new yıldızı, info, *power*) aynı 16px kutuda, 1.7 stroke ile çizilir. Uygulamaya özgü ikonlar: *power* (Exit), *folders* (worktree anahtarı), *arrow-down-to-line* (`N behind`), *rotate-ccw* (motoru yeniden başlat), *circle-arrow-up* (güncelleme hapı).
- **Büyük ikon tek yerde:** boş görünüm ve keşif blokları — Lucide *network* (graf) ve *list* (proje listesi), 28px, 1.4 stroke, `text-faint` %70 — kontrol değil, boş durum resmi.
- **Caret ve chevron'lar çizilir,** yazılmaz; tek istisna `⌄ latest` hapının ⌄'sidir (U+2304, metin glifi).
- **Yasak:** emoji, dekoratif dolu renkli daire-rozet ikonlar, çok renkli ikonlar. İkon daima tek renk ve işlevseldir.
- **Unicode:** yalnız `▸` öneki (konsolun komut satırları ve ribbon'un anlatı satırları), latest hapının `⌄`'si ve tipografik `—` / `·` ayraçları.
- **Ürün logosu (Build Orchestrator)** — `assets/Logos/`. Beş şerit + gradyanlı chevron; üç varyant, üç ayrı iş için, birbirinin yerine kullanılmaz:
  - `app-mark.svg` — uygulama içi: title bar (19px), About (30px). Tile yok (şeffaf); şeritler nötr rampadan ve `brand-strip-dim`'den, chevron amber gradyanı (`amber-bright → brand-chevron-deep`).
  - `app-mark-mono.svg` — tray, tek renk bağlamlar, devre dışı durumlar. Tek mürekkep `#EDEDEE` (`text-primary`); hiyerarşi opaklıkla.
  - **Animasyonlu işaret (tray build göstergesi)** — [components/TrayBuildIndicator](components/TrayBuildIndicator/README.md). Pencere tepsideyken derleme koşarken sağ alt köşede 187.5 × 67'lik 3 saniyelik döngü: şevron şeritleri siler, durur, parçalar sağa süzülüp kaybolur. Geometri ve renkler `app-mark.svg` ile aynı; zaman çizelgesi token değil, teslim edilmiş sanattır. Reduced motion'da döngü başlamaz, duruş karesi görünür.
  - `assets/App Icons/app-icon.svg` — uygulama ikonu (.exe, taskbar, kısayol, yükleyici). Uygulamanın raster ikonları bilinçli olarak tile'sız, şeffaf zeminle üretilir.
- **Title bar'daki chevron accent ağırlığı taşır:** o bölgede başka amber öğe konumlandırılmaz. Tek istisna What's new butonundaki 5px okunmamış-sürüm noktasıdır (süs değil, statü işareti).
- **Firma logosu (opsiyonel):** `assets/Logos/delta-logo-dark.svg` — dark UI wordmark. Title bar'da ürün işaretinin sağında, ayraçtan sonra 10px / %55 opaklık; About'ta 13px / %80. Ürün logosu daima önde: daha büyük, tam renk, solda. Logo yoksa ayraç da gider.
- `assets/App Icons/delta-app-icon.svg` — Delta'nın kendi uygulama ikonu; Build Orchestrator ikonu değil, referans olarak durur.

---

## MOTION

- **Süreler** 80 / 120 / 180 / 280ms (`--duration-instant/fast/base/slow`). **Üç eğri:** `ease-out` giriş, `ease-standard` durum değişimi, `ease-in-out` yer değiştirme; uygulamada aynı eğriler `KeySpline` olarak birebir. Bounce/overshoot yok.
- **Ne anime olur:** layout geçişi gerektirmeyenler — opaklık, renk, transform, stroke dash offset, clip. Bilinçli, sonlu istisnalar: satırın statü şeridi ve ribbon'un ilerleme göstergesi (`Width`), scrollbar hapının hover içe çekilmesi, scroll kaymaları.
- **Aynı anda tek hero.** Graf ve liste ön cephesi tek hero sayılır ve birlikte oynar; başka bir hero reddedilir, sahibi son duruma atlar.
- **Reduced motion bir OS sinyalidir, uygulama ayarı değil:** canlı izlenir, dört süre token'ı yerinde 0'a çekilir ve geri yüklenir; koreografiler hiç oynamaz. Bu pakette `components/bundle.css` aynı kuralı CSS'te uygular.
- **Overlay girişleri tek gövde:** popover ve Build menüsü 4px aşağıdan, .985 ölçekle 140ms'de yükselir; modal'lar 6px, ölçeksiz, `--duration-base`. Çıkış animasyonu yok.
- **İşlemi iki koreografi çerçeveler:** açılış (nötr an → kapsamın rastgele sırayla amber yandığı işaretleme dalgası → graf dışı node'ların sönmesi ve kapsamın sırayla teslimi) ve bitiş (yalnız grafta: bu koşuda derlenenler floresan gibi tutuşur, sonra kalan gri birlikte gelir). Liste her iki koreografide de sakin kalır. Koreografiler yalnız görünür pencerede oynar.
- **Kapsam amber'e kayar, sıçramaz:** dalganın dokunduğu her yüzey 200ms'de standart eğriyle geçer.
- **Renk geçişleri premultiplied yolda yürür** (WPF düz interpolasyon yapar; farklı alfa uçlarında orta nokta parlar). Sonsuz dekoratif animasyonlar 30 fps'tir, görünmez olunca durur ve saatleri ağaçtan kaldırılır; iki caret tek saati paylaşır ve yalnız pencere aktifken yanıp söner.

---

## YASAKLAR

Genel SaaS kart ızgaraları, üç sütunlu özellik ızgaraları, mor/indigo gradyanlar, şişik radius, dekoratif blob'lar, pazarlama hero'ları, her şeyi ortalamak, dekoratif gölgeler, tasarım öğesi olarak emoji, dolu çok renkli rozet ikonlar ve dönen dekoratif küre. Bilinçli, ince halkalı, statü renkli daire-içi glyph'ler bunun tersidir ve teşvik edilir. **Uygulamada toast ve uygulama içi popup yoktur;** `Toast` bileşeni sistemde durur ama uygulama kullanmaz.

---

## Components

Status: `StatusGlyph`, `StatusBadge`, `WillBuildDot` (uyumluluk), `Spinner`
Controls: `Button`, `IconButton`, `Chip`, `Segment`, `Kbd`
Forms: `Field`, `Input`, `Select`, `Checkbox`, `Switch`
Data: `ProjectRow`, `Console`, `ConsoleLine`, `ProgressBar`, `Metric`, `Tag`
Graph: `DependencyGraphNode`
Shell: `TitleBar`, `Toolbar`, `ToolbarSep`, `ToolbarSpacer`, `Tabs`, `StatusBar`, `StatusBarItem`, `Tooltip`, `Dialog`, `Toast`

Her bileşenin `components/<Ad>/README.md` kılavuzu ve canlı önizlemesi var; props sözleşmeleri `components/index.d.ts`. Runtime: `components/bundle.js` yüklendikten sonra `window.DeltaBuildOrchestratorDS_eb0bd1` (ayrıca `STATUS_META`). UI kiti: `MainWindow`. Bundle'ın varsayılan adları (`STATUS_META`, pencere ve kapat düğmeleri, `Spinner`) uygulamadaki İngilizce adlardır; yalnız `MainWindow` kitinin ve `WillBuildDot`'un metinleri 1.x paketinden geldiği gibi Türkçedir. Önizlemeler uygulamanın İngilizce kelimelerini açıkça verir — sen de öyle yap.

## Fontlar

Uygulama Geist ve Geist Mono'yu **gömülü statik OTF** olarak taşır (resmî GitHub sürümünden; konsol için Geist Mono Light dahil) ve air-gapped çalışır. Değişken font kullanılmaz (WPF eksen desteklemez); CDN sürümü OpenType tabloları alt kümelenebildiği için reddedildi. 0.07em tracking WPF'te açık advance genişlikli bir `GlyphRun` ile çizilir; hair space eklemek yasaktır. Bu paket ise yalnız önizleme için fontları **Google Fonts CDN**'den yükler (`components/bundle.css`).

---

## Kaynak

Design system **2.0.0** bir milattır: yayındaki uygulamanın (**app 1.15.0**) kodundan yeniden kuruldu — renk, ölçü ve tipografi `src/BuildOrchestrator.App/Resources/Tokens.xaml`'den, motion `Motion.xaml`'den, ikonlar `Icons.xaml`'den okundu. Token adları ve değerleri `tokens.json` / `tokens.css`'tedir; uygulamada olup eski sistemde karşılığı olmayan değerler (`status-queued-border`, `brand-strip-dim`, `brand-chevron-deep`, pencere iskeleti ölçüleri, pencere minimumu, reading line height, console weight) token olarak girdi. `components/bundle.js` 1.x tasarım paketinin bundle'ından gelir, örnek verisindeki adlar nötrleştirildi (`ACME.*`, `Acme.sln`); `Spinner` ve `StatusGlyph`'in building çizimi uygulamanın `BuildingSpinner`'ına göredir. Sistemdeki her değişiklik repodaki `docs/design-system/VERSIONS.md`'de bir sürümdür.
