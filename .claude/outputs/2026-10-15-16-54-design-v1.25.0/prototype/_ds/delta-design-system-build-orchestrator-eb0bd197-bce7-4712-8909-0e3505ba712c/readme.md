# Delta Build Orchestrator — Design System

Delta Yazılım'ın dahili geliştirici aracı **Build Orchestrator** için tasarım sistemi.

## Bağlam

**Delta Yazılım** (https://www.delta-yazilim.com/) otomotiv sektörü için bayi yönetim yazılımları (DMS) üretir: yeni/ikinci el araç satışı, servis, yedek parça ve mobil çözümler (OSYS ürün ailesi). 2001'den beri Türkiye otomotiv distribütör ve bayilerine hizmet verir; kurumsal, güven-odaklı bir B2B markasıdır.

**Build Orchestrator**, Delta'nın kendi çok-projeli .NET çözümünü (OSYS) bağımlılık sırasına göre, paralel ve yalnızca değişen projeleri derleyip canlı gösteren **dark, modern, veri-yoğun bir Windows masaüstü geliştirici aracıdır**. Marka tonu: yenilikçi, veri-odaklı, profesyonel, sakin ve hassas — gösteriş değil, güven.

### Kaynaklar
- `uploads/delta-logo.svg` — resmi DELTA wordmark (koyu harfler + amber yay)
- `uploads/delta-tokens.css` — onaylı token seti (bu sistemin tek renk/ölçü kaynağı; `tokens/` altına bölünerek birebir taşındı)
- https://www.delta-yazilim.com/ — kurumsal site (ton/bağlam referansı)

## Kimlik özeti

- **Near-black dark tema.** TEK marka accent'i **Delta amber `#eda10f`** (logodaki sıcak turuncu-sarı). Amber dışında hiçbir dekoratif renk yok.
- **Renk yalnızca STATÜ taşır.** Hiyerarşi renkle değil **ağırlıkla** kurulur: boyut, kontrast, konum.
- **Statü = renk + glyph + metin** — üçü birden, her zaman (colorblind-safe). Emoji asla.
- Yapıyı **hairline border** taşır, gölge değil. Elevation kıt: tek yumuşak gölge yalnız floating overlay'de.
- Motion sakin: bounce/overshoot yok, yalnız transform+opacity, aynı anda en fazla 1 hero motion.

---

## CONTENT FUNDAMENTALS

**Dil:** Türkçe UI. Teknik terimler yerleşik İngilizce haliyle kalır ve çevrilmez: *build, branch, worktree, sync, solution, restore, commit, Debug/Release*. Dosya adları, komutlar, SHA'lar olduğu gibi.

**Ton:** sakin, kesin, mühendisçe. Pazarlama sesi yok, ünlem yok, espri yok. Uygulama kullanıcıya güven verir çünkü **net rakam ve net durum** söyler: "14/38 derlendi · 21 atlandı · 3 hata".

**Hitap:** kısa emir kipi eylemlerde ("Derle", "Sync", "Durdur", "Konsolu Aç", "Vazgeç"). Sistem mesajları edilgen/nesnel ("Restore tamamlandı (1.2s)", "3 proje derlenemedi"). "Siz" veya "sen" kullanılacak kadar uzun cümle nadiren kurulur; gerekirse resmî olmayan ama mesafeli 2. tekil kaçınılır, cümle nesne-odaklı yazılır.

**Casing:** Cümle düzeni (sentence case). Başlıklarda Türkçe dilbilgisi; SCREAMING CAPS yalnız 11px bölüm etiketlerinde (`ÇIKTI`, `PROJELER` gibi, `--tracking-caps` ile).

**Sayılar ve süreler:** daima Geist Mono + tabular. Süre biçimi `4.2s`, `1m 12s`; sayaç `14/38`; SHA 7 hane `a3f81c2`. Ondalık ayracı nokta (developer bağlamı).

**Emoji:** ASLA. Statü daima glyph + renk + metinle.

**Örnek kopya:**
- Buton: "Derle" · "Yalnızca Değişenler" · "Grafı Göster"
- Boş durum: "Henüz sync yapılmadı. Proje durumları Sync sonrası görünür."
- Hata: "CS0246: 'OsysDbContext' türü bulunamadı (Osys.Parca.Api)"
- Toast: "Derleme başarısız — 3 hata. Konsolu Aç"

---

## VISUAL FOUNDATIONS

**Renk:** near-black, hafif sıcak nötr rampa (`#0a0a0c → #ededee`). Zeminler 5 kademe: sunken/base/surface/raised/overlay + en koyu `--console-bg #060608`. Amber tek accent; statü paleti 6 anlam (success yeşil, fail kırmızı, building amber, skipped gri, cycle turuncu, queued gri). Her statünün 4 tonu: çekirdek, `-text` (≥4.5:1), `-soft` (%10-12 zemin), `-border` (%24-32). Dekoratif gradient, mor/indigo, blob YASAK.

**Tipografi:** UI = **Geist** (300–700), makine çıktısı = **Geist Mono** (tabular rakam). Temel UI 13px; ölçek 11→34px. Başlık 600, vurgu 500, gövde 400. Caps etiketler 11px + `--tracking-caps 0.07em`. Mono asla dekoratif kullanılmaz: yalnız console, süre, SHA, sayaç, yol.

**Boşluk:** 4px grid (`--space-1…16`). Yoğunluk yüksek: satır 36px (compact 30), padding'ler 8-16px bandında.

**Arka planlar:** düz koyu yüzeyler; görsel/foto/pattern/texture/gradient yok. Derinlik yalnız yüzey kademeleriyle.

**Radius:** kontroller 4 · kart/panel 6 · overlay (menü/dialog/toast) 8 · chip/tag/kbd 3 · **console daima 0 (keskin)**. Şişik radius yok.

**Border:** hairline 1px her yapının taşıyıcısı — `subtle` iç bölücü, `border` panel, `strong` etkileşimli kontrol. Kart = surface zemin + 1px border + radius-md, **gölgesiz**.

**Gölge:** yalnız floating overlay: `--elevation-overlay` (dialog/menü/toast), `--elevation-popover` (tooltip). Başka hiçbir yerde yok.

**Motion:** 80/120/180/280ms; `ease-out` giriş, `ease-standard` durum değişimi, `ease-in-out` yer değiştirme. Bounce/overshoot yok. Yalnız transform+opacity (layout animasyonu yasak). Aynı anda en fazla 1 hero motion (örn. building pulse). OS reduced-motion → tüm süreler 0 (tokens/effects.css bunu otomatik yapar); uygulama-içi toggle yok.

**Hover:** zemin bir yüzey adımı açılır (`transparent → raised`, `raised → overlay`); renk tonu değişmez, boyut değişmez. **Press:** bir adım koyulaşır (amber'da `--amber-dim`). **Focus:** 2px `--focus-ring` amber halka, offset 1px.

**Seçim:** satırda zemin `--surface-raised`; graf node'unda amber halka. Metin seçimi `--amber-soft-hover`.

**Transparanlık/blur:** yok. Scrim `rgba(4,4,6,.6)` düz. Backdrop-blur kullanılmaz.

**Disabled:** %45 opaklık, layout sabit.

**Statü gösterimi:** renk + ince-halka daire-içi glyph + Türkçe metin. Will-build noktası (8px) statüden AYRI ortogonal kanal: amber dolu=dirty, gri=clean, hollow=unknown.

---

## ICONOGRAPHY

- **İkon sistemi:** [Lucide](https://lucide.dev) geometrisi — 1.5–2px stroke, `currentColor`, 12–16px kullanım boyu. Bileşenler ihtiyaç duydukları glyph'leri **inline stroke SVG** olarak gömer (box, folder, code, chevron, search, gear, min/max/close, branch). Ayrı ikon fontu ya da sprite yok; yeni ikon gerekirse Lucide'den path kopyala, stroke kalınlığını koru.
- **Statü glyph'leri:** `StatusGlyph` bileşeni — ince-halkalı daire içinde tik/çarpı/tire/saat; building dönen arc; cycle uyarı üçgeni. Gerçek çizim, emoji değil.
- **Yasak:** dekoratif dolu renkli daire-rozet ikonlar (SaaS avatar kalabalığı), emoji, çok renkli ikonlar. İkon daima tek renk (currentColor) ve işlevseldir.
- **Unicode:** yalnız console `▸` (cmd öneki) ve tipografik `—` / `·` ayraçları.
- **Logolar:** `assets/delta-logo.svg` (açık zemin), `assets/delta-logo-dark.svg` (dark UI; wordmark `#ededee`, amber yay korunur — title bar'da 15px yükseklik). **App icon = logodaki D harfi** (wordmark'tan birebir çıkarılmış glyph), kurumsal ton varyantlarıyla: `delta-app-icon.svg` (koyu zemin + amber D — ana; taskbar/pencere), `delta-app-icon-amber.svg` (amber zemin + koyu D), `delta-app-icon-mono.svg` (koyu zemin + `#ededee` D — tray/mono bağlamlar).

---

## Dosya dizini

```
styles.css                  ← tek global giriş (@import listesi)
tokens/                     ← colors · typography · spacing · effects · fonts · base
assets/                     ← delta-logo.svg · delta-logo-dark.svg · delta-app-icon.svg (+ Brand kartları)
guidelines/                 ← foundation specimen kartları (Colors/Type/Spacing/Effects)
components/
  status/                   StatusGlyph · StatusBadge · WillBuildDot · Spinner
  controls/                 Button · IconButton · Chip · Segment · Kbd
  forms/                    Field · Input · Select · Checkbox · Switch
  data/                     ProjectRow · Console · ConsoleLine · ProgressBar · Metric · Tag
  graph/                    DependencyGraphNode
  shell/                    TitleBar · Toolbar (ToolbarSep · ToolbarSpacer) · Tabs · StatusBar (StatusBarItem) · Tooltip · Dialog · Toast
ui_kits/build-orchestrator/ ← ana pencere UI kiti (index.html + MainWindow.jsx)
templates/build-orchestrator/ ← tüketici projeler için başlangıç şablonu
SKILL.md                    ← ajan kullanım kılavuzu
```

### Components

Status: `StatusGlyph`, `StatusBadge`, `WillBuildDot`, `Spinner`
Controls: `Button`, `IconButton`, `Chip`, `Segment`, `Kbd`
Forms: `Field`, `Input`, `Select`, `Checkbox`, `Switch`
Data: `ProjectRow`, `Console`, `ConsoleLine`, `ProgressBar`, `Metric`, `Tag`
Graph: `DependencyGraphNode`
Shell: `TitleBar`, `Toolbar`, `ToolbarSep`, `ToolbarSpacer`, `Tabs`, `StatusBar`, `StatusBarItem`, `Tooltip`, `Dialog`, `Toast`

Her bileşenin yanında `.d.ts` (props sözleşmesi) ve `.prompt.md` (kullanım örneği) var. Runtime: `_ds_bundle.js` yüklendikten sonra `window.DeltaBuildOrchestratorDS_eb0bd1`.

### Statü tablosu

| Statü | Token | Glyph | Metin |
|---|---|---|---|
| Discovered | `--text-faint` | kesikli daire | Keşfedildi |
| Queued | `--status-queued-text` | saat | Sırada |
| Building | `--amber-text` | dönen arc (pulse) | Derleniyor |
| Succeeded | `--status-success-text` | ✓ | Başarılı |
| Failed | `--status-fail-text` | ✗ | Başarısız |
| Skipped | `--status-skipped-text` | — | Atlandı / güncel |
| CycleDetected | `--status-cycle-text` | uyarı üçgeni | Döngü |

## Fontlar — ÖNEMLİ NOT

Geist ve Geist Mono şu an **Google Fonts CDN**'den yükleniyor (`tokens/fonts.css`). Air-gapped Windows build için **woff2 dosyaları sağlanmalı**; sağlanınca `assets/fonts/` altına konup `tokens/fonts.css` @font-face kurallarıyla değiştirilecek.
