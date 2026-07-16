# Delta — Design System & Build Orchestrator Tasarım Promptu (v1)

> **Kullanım (Claude Design):**
> 1. **"Set up your design system"** ekranında → **Company name and blurb** = Bölüm 1.1; **Any other notes** = Bölüm 1.2; **Add fonts, logos and assets** = Delta logosu (dark/light varyant) + istersen Bölüm 1.5 token bloğunu `delta-tokens.css` olarak; **Link code** boş bırakılabilir.
> 2. **Template = Prototype**, **Model = Claude Opus 4.8** seç.
> 3. **Describe** kutusuna → **Bölüm 2 (App Prompt)** + **Bölüm 3 (Canlı Demo Eki)**'ni **birlikte** yapıştır.
>
> **Kaynak otorite:** Uygulama kararları → `2026-07-02-01-38-build-orchestrator-plan-v6-implementation.md` (bu tasarımın türetildiği plan). Token değerleri → önceki Claude Design çıktısının ürettiği stylesheet'ten **birebir** (kullanıcı beğenisiyle donmuş). Bu dosya v4.3 design promptunun (`2026-06-29-12-55-claude-design-prompt-v4.3.md`) türevidir; onaylı yeni özellikler işlendi.

---

## Bölüm 0 — Neyi tasarlıyoruz

Delta Yazılım'ın masaüstü geliştirici aracı **"Build Orchestrator"**: tek git repo altındaki yüzlerce birbirine bağımlı .NET (çoğu legacy .NET Framework) C#/WPF projesini bağımlılık sırasına göre, paralel ve **yalnızca değişenleri** derleyip **canlı** gösteren, dark/modern tek-pencere WPF uygulaması. Pazarlama/landing **değil** — data-dense utility app.

**North-star:** sakin-hassas dark (Linear/Geist ruhu) + heyecanlı build-frontier. Heyecan gürültüden değil, **dependency-order frontier'ın grafta ve listede aşağı akmasından** gelir. **Restraint = kalite = farklılaşma.**

---

# Bölüm 1 — Delta Design System

## 1.1 Company name and blurb

```
Delta Yazılım — Build Orchestrator (dahili geliştirici aracı)

Delta Yazılım, otomotiv sektörünün bayi yönetim yazılımlarını (DMS: yeni/ikinci
el araç satışı, servis, yedek parça, mobil) üreten bir kuruluştur. "Build
Orchestrator", Delta'nın kendi büyük çok-projeli .NET çözümünü (OSYS) bağımlılık
sırasına göre, paralel ve yalnızca değişen projeleri derleyip canlı gösteren dark,
modern, veri-yoğun bir Windows masaüstü geliştirici aracıdır. Marka tonu:
yenilikçi, veri-odaklı, profesyonel, sakin ve hassas — gösteriş değil, güven.
```

## 1.2 Any other notes (brand voice + design DNA)

```
KİMLİK: Near-black dark tema. TEK marka accent'i = Delta amber (#eda10f, logodaki
sıcak turuncu-sarı). Amber dışında hiçbir dekoratif renk yok; renk yalnızca
STATÜ taşır. Hiyerarşi renkle değil AĞIRLIKLA kurulur (boyut/kontrast/konum).

TİPOGRAFİ: UI = Geist (gerçek grotesk; "Inter default" değil). Makinenin ürettiği
her şey (console çıktısı, süreler, commit SHA, sayaçlar) = Geist Mono (gerçek
tasarlanmış monospace; tabular rakamlar). Yoğun geliştirici-aracı ölçeği: temel
UI metni 13px.

ŞEKİL: küçük, tutarlı radius (kontroller 4px; kartlar/paneller 6px; overlay 8px;
CONSOLE keskin = 0). Yapıyı GÖLGE değil HAIRLINE border taşır. Elevation kıt:
tek yumuşak gölge yalnızca floating overlay (menü/dialog/toast) için.

MOTION: sakin; bounce/overshoot yok. Aynı anda EN FAZLA 1 hero motion. Yalnızca
transform+opacity anime edilir (layout animasyonu yok). OS "animasyonları göster"
kapalıysa tüm motion anlık'a düşer (uygulama-içi toggle YOK).

STATÜ = renk + glyph + metin (üçü birden; colorblind-safe; emoji DEĞİL). Dim/skipped
dahil tüm metin ≥4.5:1 kontrast.

KESİN YASAK (AI-slop): generic SaaS kart-grid, 3-kolon feature grid, mor/indigo
gradient, her şeyde şişik radius, dekoratif blob/dalga, marketing hero, ortalanmış
her şey, dekoratif gölge, emoji'yi tasarım öğesi yapmak, DEKORATİF DOLU RENKLİ
DAİRE-ROZET ikonlar (SaaS avatar-icon kalabalığı). — NOT: amaçlı, ince-halkalı,
statü-renkli, tutarlı çizgili DAİRE-İÇİ statü glyph'i (tik/tire/çarpı) SERBEST ve
teşvik edilir; yasak olan yalnızca dekoratif/dolu/çok-renkli rozet kalabalığıdır.
```

## 1.3 Logo & asset

- **Ana logo:** `delta-logo.svg` — koyu "DELTA" wordmark + amber (`#eda10f`) yay/çengel işareti.
- **Dark UI varyantı (title bar için):** wordmark `--text-primary` (`#ededee`) beyaza çevrilir, amber yay korunur (`delta-logo-dark.svg`). Title bar'da sol üstte ~15px yükseklikte.
- **App icon:** amber yay + beyaz üçgen işareti (marka mark'ı); taskbar + tray + pencere.

## 1.4 Fonts

| Rol | Font | Neden |
|---|---|---|
| UI (sans) | **Geist** (`'Segoe UI', system-ui` fallback) | karakterli neo-grotesk, Linear/Geist ruhu |
| Mono (console/süre/SHA/sayaç) | **Geist Mono** (`ui-monospace, 'Cascadia Code'` fallback) | gerçek tasarlanmış monospace, tabular rakam |

Weights: 300/400/500/600/700. Air-gapped Windows build için woff2 dosyaları self-host edilebilir.

## 1.5 Token spec (üretilmiş stylesheet'ten birebir — `delta-tokens.css` olarak eklenebilir)

```css
:root {
  /* ---- Neutrals (near-black, hafif sıcak) ---- */
  --black:#08080a; --neutral-1000:#0a0a0c; --neutral-950:#0e0e10; --neutral-900:#141417;
  --neutral-850:#1a1a1e; --neutral-800:#202024; --neutral-700:#2a2a30; --neutral-600:#3a3a42;
  --neutral-500:#54545c; --neutral-400:#76767e; --neutral-300:#a9a9b0; --neutral-200:#cdcdd2;
  --neutral-100:#ededee; --white:#ffffff;

  /* ---- Surfaces ---- */
  --surface-sunken:var(--neutral-1000); --surface-base:var(--neutral-950); --surface:var(--neutral-900);
  --surface-raised:var(--neutral-850);  --surface-overlay:var(--neutral-800); --console-bg:#060608;

  /* ---- Borders (hairline yapıyı taşır) ---- */
  --border-subtle:#1c1c20; --border:#2a2a30; --border-strong:#3a3a42;

  /* ---- Text ---- */
  --text-primary:#ededee; --text-secondary:#a9a9b0; --text-dim:#76767e; --text-faint:#54545c;
  --text-on-accent:#1c1304;

  /* ---- Brand: amber (TEK marka rengi) ---- */
  --amber:#eda10f; --amber-bright:#ffb52e; --amber-dim:#b87a0b; --amber-text:#f1ab2e;
  --amber-soft:rgba(237,161,15,0.12); --amber-soft-hover:rgba(237,161,15,0.18); --amber-border:rgba(237,161,15,0.32);

  /* ---- Status (renk + glyph + metin, hep birlikte) ---- */
  --status-success:#43b16b; --status-success-text:#58cb80; --status-success-soft:rgba(67,177,107,0.12); --status-success-border:rgba(67,177,107,0.30);
  --status-fail:#ee5a52;    --status-fail-text:#ff706a;    --status-fail-soft:rgba(238,90,82,0.12);     --status-fail-border:rgba(238,90,82,0.32);
  --status-building:var(--amber); --status-building-text:var(--amber-text); --status-building-soft:var(--amber-soft); --status-building-border:var(--amber-border);
  --status-skipped:#6a6a73; --status-skipped-text:#888890; --status-skipped-soft:rgba(120,120,128,0.10); --status-skipped-border:rgba(120,120,128,0.24);
  --status-cycle:#df6f2b;   --status-cycle-text:#f0853f;   --status-cycle-soft:rgba(223,111,43,0.12);   --status-cycle-border:rgba(223,111,43,0.32);
  --status-queued:#7c7c84;  --status-queued-text:#9a9aa2;  --status-queued-soft:rgba(124,124,132,0.10);

  /* ---- Will-build noktası (YENİ — dot semantiği) ---- */
  --dot-dirty:var(--amber);        /* değişmiş → derlenecek */
  --dot-clean:var(--neutral-600);  /* güncel → atlanacak */
  --dot-unknown:transparent;       /* Sync/state öncesi → hollow (yalnız ince halka) */
  --dot-size:8px; --dot-outline-width:1px; --dot-outline-color:var(--border-subtle); /* hollow varyant */

  /* ---- Focus ---- */
  --focus-ring:rgba(237,161,15,0.50); --focus-ring-width:2px;

  /* ---- Type scale ---- */
  --font-sans:'Geist','Segoe UI',system-ui,sans-serif;
  --font-mono:'Geist Mono',ui-monospace,'Cascadia Code',monospace;
  --text-2xs:11px; --text-xs:12px; --text-sm:13px; /* BASE */ --text-md:14px; --text-lg:16px;
  --text-xl:20px; --text-2xl:26px; --text-3xl:34px;
  --leading-tight:1.2; --leading-snug:1.35; --leading-normal:1.5; --leading-mono:1.55;
  --tracking-tight:-0.01em; --tracking-wide:0.02em; --tracking-caps:0.07em;

  /* ---- Space (4px grid) ---- */
  --space-1:4px; --space-2:8px; --space-3:12px; --space-4:16px; --space-5:20px; --space-6:24px;
  --space-8:32px; --space-10:40px; --space-12:48px; --space-16:64px;

  /* ---- Radius (küçük; console keskin) ---- */
  --radius-none:0; --radius-xs:3px; --radius-sm:4px; /* default */ --radius-md:6px; --radius-lg:8px; --radius-full:999px;

  /* ---- Elevation (kıt: yalnız overlay) ---- */
  --elevation-overlay:0 10px 28px -10px rgba(0,0,0,.66), 0 2px 6px -2px rgba(0,0,0,.5);
  --elevation-popover:0 6px 18px -8px rgba(0,0,0,.6);

  /* ---- Motion (sakin; bounce yok) ---- */
  --duration-instant:80ms; --duration-fast:120ms; --duration-base:180ms; --duration-slow:280ms;
  --ease-out:cubic-bezier(.22,1,.36,1); --ease-standard:cubic-bezier(.4,0,.2,1); --ease-in-out:cubic-bezier(.65,0,.35,1);

  /* ---- Layout ---- */
  --titlebar-height:40px; --toolbar-height:44px; --statusbar-height:28px; --row-height:36px; --row-height-compact:30px;
}
```

## 1.6 Component vocabulary (`dx-` prefix — üretilmiş sistemle uyumlu)

- **StatusGlyph / StatusBadge** — statü = ince-halka daire-içi glyph (✓ tik, — tire/skip, ✗ çarpı, ⟳ spinner building, cycle uyarı üçgeni) + renk + metin. Gerçek font glyph, emoji değil.
- **ProjectRow** — dense liste satırı (mosaic/kart-grid değil): sol accent şerit (statü kodlar) · **will-build noktası** (amber dirty / gri clean / hollow unknown) · box glyph · ad (primary) + solution (dim) · sağda glyph + süre (mono) · hover'da "Dosyada Aç / VS'de Aç" ikonları.
- **DependencyGraphNode (YENİ)** — grafta küçük node; renk = canlı statü; building = amber pulse; seçili = amber halka + kalınlaşmış kenarlar.
- **Console / ConsoleLine** — `--console-bg` (#060608), keskin 0-radius, mono, satır tipleri: info/success/warn/error/cmd(`▸`)/dim.
- **Chip'ler** — branch chip (aranabilir), worktree chip (toggle/picker), Debug|Release segment, perf chip (Full/Balanced/Light), Sync + 5 sayaç.
- **ProgressBar / Metric / Tag / Kbd / Spinner / Field·Input·Select·Check·Switch / TitleBar·Toolbar·Tabs·Seg / Tooltip·Dialog** — tokenlara bağlı.

## 1.7 Statü rengi haritası (tek referans)

| Statü | Renk | Glyph (ince-halka daire içinde) | Metin |
|---|---|---|---|
| Discovered | `--text-faint` | kesikli daire | "Keşfedildi" |
| Queued | `--status-queued-text` | saat | "Sırada" |
| Building | `--amber-text` | spinner ⟳ (pulse) | "Derleniyor" |
| Succeeded | `--status-success-text` | ✓ | "Başarılı" |
| Failed | `--status-fail-text` | ✗ | "Başarısız" |
| Skipped | `--status-skipped-text` | — (tire) | "Atlandı / güncel" |
| CycleDetected | `--status-cycle-text` | uyarı üçgeni + **kırmızı rozet** | "Döngü" |

**Will-build noktası (accent'ten AYRI, ortogonal):** amber dolu = dirty/derlenecek · gri = güncel/atlanacak · hollow = Sync öncesi bilinmiyor.

---

# Bölüm 2 — App Prompt (Build Orchestrator, v6 güncel — Describe'a yapıştır)

```
Bağlam: Windows MASAÜSTÜ developer aracı için tek-pencere bir UI tasarla. App UI
(data-dense, utility) — pazarlama/landing DEĞİL. Delta design system'i kullan (dark,
near-black; TEK accent = marka amber #eda10f; gerçek monospace console; title bar'da
Delta logosu açık varyant). Ürün: "Build Orchestrator" — büyük çok-projeli bir .NET
solution'unu bağımlılık sırasına göre, paralel ve yalnızca değişen projeleri derleyip
CANLI gösteren araç. North-star: sakin-hassas dark (Linear/Geist ruhu) + heyecanlı
build-frontier; restraint = kalite. Heyecan gürültüden değil, frontier'ın GRAFTA ve
LİSTEDE aşağı akmasından gelir.

LAYOUT (tek pencere, tek kompozisyon — SOL PANEL ARTIK İKİYE BÖLÜNMÜŞ, sağ panel gibi):
- TITLE BAR (custom, near-black): solda Delta logosu (açık varyant) + "OSYS · main"
  (repo · branch); sağda min/max/close.
- STICKY ŞERİT + GLOBAL PROGRESS (title bar altı, ince): "▸ Building 8/120 · 1m04s ·
  ~40s kaldı" + o anki paralel set çipleri (tıklanabilir). İnce determinate progress.
- GÖVDE = 2 KOLON (dikey GridSplitter, ~%46/%54); her kolon kendi içinde YATAY
  GridSplitter ile ikiye bölünür (4 quadrant + strips). Tüm splitter konumları persist.

  SOL KOLON:
  • SOL-ÜST = DEPENDENCY GRAF: gerçek DAG (node=proje, ince kenar=bağımlılık),
    build-order/katman düzeninde YERLEŞİK (sürekli rotasyon/globe YOK). Node rengi =
    canlı statü (building=amber pulse, ✓yeşil, ✗kırmızı, ↷dim/gri, queued nötr).
    Build-frontier grafın İÇİNDE amber dalga olarak akar (auto-pan ağırlık merkezini
    yumuşak izler). Listeden/graftan bir node seç → o node ortalanır + amber halka +
    komşu kenarlar belirir, gerisi hafif dimlenir. Reduced-motion: statik layout +
    yalnız renk. Çok büyük graf (500+): cull/agrega, katman-bazlı sadeleşme.
  • (yatay splitter)
  • SOL-ALT = build-order'lı PROJE KART LİSTESİ (yüzlerce, virtualized). Kart = DENSE
    LİSTE SATIRI (marketing-card DEĞİL): sol kenarda statü-rengi accent şerit; hemen
    yanında WILL-BUILD NOKTASI (amber dolu=değişmiş/derlenecek, gri=güncel/atlanacak,
    Sync öncesi=hollow) — accent'ten AYRI/ORTOGONAL; proje adı (primary) + solution
    (dim, küçük); sağda ince-halka daire-içi statü glyph'i + süre (mono); cycle
    varsa kırmızı rozet; "şu an <commitA> → hedef <commitB>" mini metni; satır
    HOVER'ında sağda "Dosyada Aç" / "Visual Studio'da Aç" ikonları (estetik, ince
    çizgili — folder-open ve external-window tarzı; hover'da belirir, sadelik korunur).
    Sticky ara başlıklar = KATMAN adları (scroll'da üste yapışır).

  SAĞ KOLON:
  • SAĞ-ÜST = ANA CONSOLE (monospace, keskin 0-radius, #060608): seçim yokken run
    anlatısı / granular adımlar (idle: blink cursor + "ready"); bir kart/satır
    seçiliyken o projenin tam build log'u + sol-üstte [← Back].
  • (yatay splitter)
  • SAĞ-ALT = KALICI ÖZET STREAM: kronolojik tek-satır olaylar, her zaman görünür;
    en alt satır = AKTİF (yazılan) satır. HER SATIR: hover'da arka plan değişir (kart
    gibi); tıkla → o projeyi seç + detay + [← Back] + satır SEÇİLİ görünür; TEKRAR
    tıkla → seçim kalkar + Back kaybolur + ana ekrana döner.

- ACTION BAR (en alt): solda ⟳Sync + 5 sayaç (Σ Toplam · ● Derlenen · ✓ Başarılı ·
  ✗ Başarısız · ↷ Atlanan; tıkla→filtrele); sağda: BRANCH CHIP (main ▾ — açılınca
  ARANABİLİR liste), WORKTREE CHIP (aşağıda), DEBUG|RELEASE SEGMENT TOGGLE (perf çipi
  gibi; seçili taraf amber), PERF CHIP (Full/Balanced/Light), BUILD butonu (çalışırken
  Stop'a morph).

WORKTREE MODELİ (branch-driven — chip davranışı):
- Branch chip'ten seçilen branch her şeyi belirler; AYRI "local dahil/hariç" toggle YOK.
- Seçili branch = aktif branch:
    · Worktree OFF (default) → in-place derleme, YEREL değişiklikler DAHİL. Etiket: "yerel dahil".
    · Worktree ON → committed HEAD bir worktree'de, yerel HARİÇ; isim oto (<branch>-<n>)
      / mevcut worktree'lerden seç / sil. Etiket: "committed temiz · <ad>".
- Seçili branch ≠ aktif branch:
    · Worktree ZORUNLU (in-place seçenek yok; aktif branch HİÇ değişmez). Yerel HARİÇ;
      isim oto / seç / sil. Etiket: "committed <branch> · <ad>".
- Build yanında glanceable etiket ("yerel dahil" / "committed temiz" / "committed <branch>").
  Worktree chip popup'u: toggle (aktif branch'te) + worktree seçici (oto-isim / mevcut liste / Sil).

Statüler (renk + glyph + metin, colorblind-safe): Discovered / Queued / Building(amber
active) / Succeeded(yeşil) / Failed(kırmızı) / Skipped(muted gri) / CycleDetected(warn+rozet).

7 EKRAN ÜRET (ayrı prototype frame; HERO merkez):
1) HERO — paralel build: SOL-ÜST grafta birden çok node "building" (amber, canlı),
   frontier grafın içinden akıyor; SOL-ALT listede aynı frontier üstten aşağı ilerliyor;
   üst şeritte global progress/ETA; SAĞ-ALT özet stream akıyor; en alt satır "▌ Server.Api
   building…" imleç. Grafla liste SENKRON (aynı frontier).
2) DETAY: bir kart seçili (accent şeridi kalınlaşmış + içerik bir tık içe kaymış, KUTU
   YOK); grafta o node ORTALANMIŞ + amber halka + komşu kenarlar belirmiş, gerisi dim;
   ana console o projenin tam log'u; sol-üstte [← Back]; altta özet stream hâlâ duruyor.
3) FAILURE: stream'de kırmızı "✗ Server.Api failed" satırı; nereye kaydırsan görünen
   "✗ 2 hata — Server.Api, Web.Portal [Failed'a git]" affordance'ı; buton/isim →
   Failed FİLTRE çipini uygular; her başarısız satır tıklanınca KENDİ logu açılır
   (birleşik/çoklu-seçim YOK); action bar'da "✗ 2" çipi vurgulu.
4) ALL-SKIPPED (delight): güvenli yeşil tonla "Her şey güncel — 120 proje 0.4sn'de
   kontrol edildi, derlenecek yok." (gri/başarısızlık hissi DEĞİL); listede tüm
   will-build noktaları GRİ (güncel).
5) İLK AÇILIŞ (boş): sol panede ortalanmış sıcak davet "Başlamak için bir repo seç" +
   tek [Klasör Seç] butonu; sağ console "▌ Waiting for a workspace"; graf boş placeholder.
6) IDLE/READY + SELECTOR'LAR: liste dolu, sakin; will-build noktaları görünür (amber
   değişmiş / gri güncel); console "Ready" + blink imleç. Action bar'da BRANCH CHIP
   AÇIK → aranabilir branch listesi (arama kutusu + birkaç dal); yanında WORKTREE
   popup → toggle + worktree seçici (oto-isim / mevcut liste / Sil) + net etiket.
7) DEPENDENCY GRAF ODAK: sol-üst graf büyük, katman düzeninde DAG; frontier amber dalga;
   bir node seçili (ortalanmış, komşuları vurgulu); reduced-motion notu.

ETKİLEŞİMLER (prototype destekliyorsa bağla):
- Özet stream'de HERHANGİ satıra tıkla → proje seç + detay + [← Back]; hover'da satır
  arka planı değişir; tekrar tıkla → seçim kalkar + ana ekrana dön (tek jest; modifier yok).
- Kart tıkla → seçim efekti + detay + grafta node odak; TEKRAR tıkla → seçim kalkar +
  Back kaybolur + ana ekrana dön. Console'da metin seçimi serbest (tıklayınca seçim
  kalkmaz; çıkış = Back veya seçili karta/satıra tekrar tıkla).
- Grafta node tıkla → aynı seçim (liste + console senkron).
- Sticky şerit çipine tıkla → ilgili karta/node'a git. Sayaç tıkla → filtrele. Branch
  chip → aranabilir liste. Worktree chip → toggle + picker popup. Debug|Release toggle →
  seçili config (değiştirince "config değişti, tümü derlenecek" mini-uyarı). Perf chip → döngü.

MOTION / ANİMASYON (prototype destekliyorsa uygula; yoksa o anı çiz). Kural: aynı anda EN
FAZLA 1 hero motion (grafın ve listenin frontier'ı AYNI hero — senkron); yalnız
RenderTransform+Opacity; OS reduced-motion açıksa hepsi anlık'a düşer.
- Frontier: building node/kartlarda hafif pulse + shimmer (yalnız görünür olanlar);
  oturmuş statüler STATİK (sonsuz glow yok). Grafta frontier = akan amber dalga.
- Auto-scroll/pan: aktif grubun ağırlık merkezini yumuşak takip (zıplama/yo-yo yok);
  öncelik frontier > console > stream.
- Typing live-line: en-yeni özet satırı SAKİNDE harf-harf yazılır; imleç hep blink;
  FIRTINADA (çok proje aynı anda biter) typing susar, satırlar anında; hata satırı
  typing'i ATLAR (anlık kırmızı). Ham log ASLA harf-harf yazılmaz. Bir satır asla
  ~250ms'den yavaş değil.
- Seçim efekti: accent şerit kalınlaşır + yazı bir tık içe kayar (anlık+hızlı, kutu yok).
- Sync reveal: kartlar build-order'da yukarıdan aşağı staggered fade-in (≤400ms toplam);
  graf da katman düzeninde belirir.
- Başarı: Done satırında TEK sakin settle/glow + frontier sakin-yeşile oturur (bir kez).
  Hata: kart kısa shake (ikincil ipucu).
- Popup/menüler (branch/worktree): RenderTransform+Opacity ile açılır (layout animasyonu yok).

KESİN YASAK (AI-slop): generic SaaS card grid, 3-kolon feature grid, mor/indigo gradient,
her şeyde şişik radius, dekoratif blob/dalga, marketing hero, ortalanmış her şey, dekoratif
gölge, emoji'yi tasarım öğesi yapmak, DÖNEN DEKORATİF GLOBE, DEKORATİF DOLU RENKLİ DAİRE-ROZET
kalabalığı. NOT: amaçlı, ince-halkalı, statü-renkli DAİRE-İÇİ statü glyph'i (tik/tire/çarpı)
SERBEST/teşvik edilir. Kartlar dense satır; console keskin; glyph'ler gerçek ikon (emoji değil).

ÇIKTI: 7 frame (HERO en gösterişli) + en sonda token özeti (renk HEX, font adları +
boyut/weight, spacing px, radius, icon set adı).
```

---

# Bölüm 3 — Canlı Demo Eki (ana prompt'un sonuna eklenir)

```
CANLI DEMO (otomatik oynayan İNTERAKTİF prototype — statik frame değil):
- Ekran açılır açılmaz simüle bir build run OTOMATİK başlasın: önce Sync reveal (kartlar
  build-order'da staggered iner ≤400ms, graf katman düzeninde belirir), sonra projeler
  sırayla Discovered → Building (amber pulse+shimmer, aynı anda 3-6 tanesi) →
  Succeeded/Failed/Skipped olur. Frontier HEM grafı HEM listeyi üstten aşağı kat etsin
  (senkron); auto-scroll/pan aktif grubu yumuşak takip etsin.
- Will-build noktaları başta göster: değişmiş projeler amber, güncel olanlar gri; run
  ilerledikçe accent şeridi statüye döner.
- Özet stream SÜREKLİ yazsın: her olayda yeni tek-satır; en-yeni satır SAKİN anlarda
  harf-harf (typing imleci + sürekli blink); arada 1-2 "burst" anında typing SUSSUN,
  satırlar anında eklensin. Hata satırı typing'i ATLASIN (anında kırmızı). Stream
  satırlarında hover arka planı ve seçili durum çalışsın.
- Global progress/ETA canlı saysın (X/N · geçen süre · kaba kalan).
- Sim ~20-40 sn sürsün; sonunda Done + success flourish (tek glow, frontier sakin-yeşil)
  ile otursun. İstersen başa sarıp loop'lasın.
- Detay log CANLI: bir karta/node'a/stream satırına tıklayınca o projenin ana console'daki
  tam log'u SATIR SATIR aksın (monospace, hızlı append — harf-harf değil); grafta node
  ortalanıp vurgulansın; hâlâ derleniyorsa "still going" ile canlı stream.
- TÜM ETKİLEŞİM çalışsın (tıklanabilir prototype): kart/node/stream-satırı tıkla → seçim
  + detay + [← Back]; TEKRAR tıkla → seçim kalkar + ana ekrana dön; Failure'da "Failed'a
  git" → filtre; branch chip → aranabilir liste; worktree chip → toggle+picker; Debug|Release
  → config; sticky çip / sayaç / perf chip → kendi davranışları. Detaydayken arka plan sim
  devam edebilir (auto-follow durur).
- REPLAY: Build butonu sim'i baştan başlatsın; Sync reveal'i tetiklesin.
- Görünür animasyonlar: graf+liste senkron frontier pulse/shimmer, typing imleç, seçim
  efekti, failure shake, success glow, sync reveal stagger, popup transform. OS
  reduced-motion açıksa hepsi anlık'a düşsün.
- Demo verisi: gerçekçi .NET adları (OSYS.Base, OSYS.Server.Api, OSYS.Client.Core,
  OSYS.Common.Utils, OSYS.Web.Portal…), ~30-40 kart, 2 fail + birkaç skip, 2-3 katman,
  birkaç değişmiş (amber nokta) + birkaç güncel (gri nokta) içersin.
```
