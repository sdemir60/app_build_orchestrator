# Handoff: Delta Build Orchestrator — UI Tasarım Spesifikasyonu

> **Paket sürümü: v1.16.0** · tarih: 2026-09-10 — değişenler için → [Sürüm geçmişi](#sürüm-geçmişi)
>
> **Hedef:** Bu paketteki tasarımı, Plan v6'daki WPF (.NET 10) uygulamasında birebir hayata geçirmek.
> Claude Code'a bu README'yi + `prototype/` klasörünü referans olarak ver.

## Bu dosyalar nedir / ne değildir

`prototype/` altındaki dosyalar **HTML ile yapılmış tasarım referanslarıdır** — amaçlanan görünümü ve davranışı gösteren çalışan bir prototiptir, üretim kodu DEĞİLDİR. Görev: bu tasarımı hedef codebase'in kendi ortamında (**WPF/XAML**, CommunityToolkit.Mvvm) **yeniden yaratmak**. HTML/JS/React kodu kopyalanmaz; görsel değerler (renk, ölçü, tipografi), yerleşim, kopya metinleri ve davranış birebir taşınır.

`prototype/Build Orchestrator.dc.html` herhangi bir tarayıcıda açılır ve canlı çalışır (simüle build). Üstteki sahne şeridi (Hero/Detail/Failure/…) **prototip iskelesidir, gerçek uygulamada yoktur** — gerçek uygulama penceresi, şeridin altındaki çerçeveli alandır.

## Fidelity: HIGH-FIDELITY

Piksel hassasiyetli mockup: renkler, tipografi, boşluklar, ikonlar ve etkileşimler **final**dir. Birebir uygulanmalı. Tek istisna: WPF'e çevrilemeyen web ayrıntıları (scrollbar stili gibi) en yakın native karşılıkla çözülür.

---

# 1. Tasarım Sistemi Özeti

Tam token seti: `prototype/_ds/…/tokens/*.css` (colors/typography/spacing/effects). WPF'te bunlar ResourceDictionary'ye çevrilir. Kritik değerler:

## 1.1 Renkler

**Yüzeyler (near-black, hafif sıcak):**
- `surface-sunken #0a0a0c` (pencere dışı zemin) · `surface-base #0e0e10` (panel içleri) · `surface #141417` (panel başlıkları, action bar) · `surface-raised #1a1a1e` (seçili satır, hover'lı popover satırı) · `surface-overlay #202024` (popover/menü/dialog zemini) · `console-bg #060608` (yalnız konsol — en koyu)
- Hover = bir üst yüzey adımı: `surface-hover #1a1a1e`, `surface-active #202024`. Scrim `rgba(4,4,6,0.60)` düz (blur YOK).

**Border (hairline, yapının taşıyıcısı):** `border-subtle #1c1c20` (iç bölücüler, satır altı) · `border #2a2a30` (panel/kart) · `border-strong #3a3a42` (etkileşimli kontrol, overlay kenarı).

**Metin:** `text-primary #ededee` · `text-secondary #a9a9b0` · `text-dim #76767e` · `text-faint #54545c` · `text-on-accent #1c1304` (amber buton üzerindeki koyu yazı).

**Marka accent — TEK renk, amber:** `amber #eda10f` · `amber-bright #ffb52e` · `amber-dim #b87a0b` (press) · `amber-text #f1ab2e` · `amber-soft rgba(237,161,15,.12)` · `amber-soft-hover rgba(…,.18)` · `amber-border rgba(…,.32)`. Amber dışında dekoratif renk YASAK; gradient/mor/indigo yasak.

**Statü paleti (4'lü ton: çekirdek / -text / -soft %10-12 / -border %24-32):**
- success `#43b16b` / `#58cb80` / `rgba(67,177,107,.12)` / `rgba(67,177,107,.30)`
- fail `#ee5a52` / `#ff706a` / `rgba(238,90,82,.12)` / `rgba(238,90,82,.32)`
- building = amber tonları
- skipped `#6a6a73` / `#888890` / `rgba(120,120,128,.10)` / `rgba(120,120,128,.24)`
- ~~cycle turuncusu~~ **v1.11.0'da UI'dan kaldırıldı** (`--status-cycle*` token'ları dosyada durur ama hiçbir yerde kullanılmaz); warn satırları artık amber
- queued `#7c7c84` / `#9a9aa2`

**Statü noktası (v1.11.0 — tek kanal):** 8px daire; şerit/glyph/node ile **aynı statü rengini** taşır (amber=işaretli/derleniyor · yeşil · kırmızı · gri). Başlangıç modunda (v1.12.0) dolgu yok, **4 eşit yaylı** `--status-skipped-border` halka (tek 8px SVG: `r=3.2`, stroke 1.1, `stroke-dasharray 2.93 2.1`, opaklık 0.85); işlem başlayınca aynı SVG içindeki dolu daireye 380ms çapraz-sönümle geçer — eleman ve boyut değişmez, kayma yok. Ayrı bir will-build kanalı YOK — ortogonal amber/gri/hollow nokta kaldırıldı.

**Focus:** 2px `rgba(237,161,15,.50)` halka, offset 1px.

## 1.2 Tipografi

- UI = **Geist** (`'Geist','Segoe UI',system-ui`); makine çıktısı (console, süre, SHA, sayaç, yol) = **Geist Mono**, DAİMA tabular rakam. Mono asla dekoratif kullanılmaz.
- Ölçek: 2xs 11 · xs 12 · **sm 13 (BASE)** · md 14 · lg 16 · xl 20… Ağırlıklar: başlık 600, vurgu 500, gövde 400.
- Caps etiketler (panel başlıkları `DEPENDENCY GRAPH` vb.): 11px, 500, letter-spacing 0.07em, uppercase, `text-faint`.
- Satır yükseklikleri: tight 1.2 · snug 1.35 · normal 1.5 · mono 1.55.
- Sayı biçimleri: süre `4.2s`, `1m 12s`; sayaç `14/38`; SHA 7 hane `a3f81c2`; ondalık ayracı nokta.
- **Konsol ağırlığı:** konsol gövdesi (anlatı + proje logu) **Geist Mono 300 (Light)**, 12px/1.55 — tek seferde yüzlerce satır bastığı için ince ağırlıkta daha rahat taranır. Diğer mono alanlar (event stream, sayaçlar, süreler, SHA) 400'de kalır.
- **Font notu (açık iş):** Geist şu an Google CDN'den; air-gapped paket için woff2/ttf dosyaları temin edilip gömülecek (kullanıcı kararı — geliştirme sırasında yapılacak). **Ayrıca:** geliştirmedeki konsol şu an sistem monosuyla (Consolas vb.) çiziliyor — bağlayıcı olan prototiptir: konsol da Geist Mono, ağırlık 300, 12px/1.55, tabular.

## 1.3 Boşluk / radius / elevation / motion

- 4px grid (`4/8/12/16/20/24/32…`). Satır 36px (compact 30). Titlebar 40 · statusbar 28.
- Radius: kontrol 4 · kart/panel 6 · overlay 8 · chip/kbd 3 · **console 0 (keskin)**. Pencere kökü 8.
- Gölge YALNIZ floating overlay'de: `0 10px 28px -10px rgba(0,0,0,.66), 0 2px 6px -2px rgba(0,0,0,.5)`. Panel/kart gölgesiz — yapıyı 1px border taşır.
- Motion: 80/120/180/280ms; ease-out `cubic-bezier(.22,1,.36,1)` giriş, ease-standard `cubic-bezier(.4,0,.2,1)` durum değişimi, ease-in-out `cubic-bezier(.65,0,.35,1)` yer değiştirme. Bounce/overshoot yok; yalnız transform+opacity. Aynı anda en fazla 1 hero motion. **OS reduced-motion → tüm süreler 0** (uygulama içi toggle yok).

## 1.4 İkonografi

- Lucide geometrisi, 1.5–2px stroke, tek renk (currentColor), 12–16px. Emoji ASLA.
- Statü glyph'leri = ince halkalı daire içinde çizim: ✓ tik (success), ✗ çarpı (fail), — tire (skipped), saat (queued), kesikli daire (discovered). Uyarı üçgeni ayrı sabit slottadır — amber, cycle + dep birleşik (v1.7.0).
- **Building spinner = discovered'ın kesikli halkasının amber, dönen hali**: `stroke-dasharray 2.3 2.5` dairesel halka, 1.4s lineer sonsuz dönüş. (Ayrı bir "spinner" çizimi değil — aynı halka döner.)
- Dep-hata rozeti: küçük DOLU üçgen (▲), 12-13px, `status-fail-text` renkli.
- Logolar: `assets/delta-logo-dark.svg` (title bar, 15px yükseklik), `assets/delta-app-icon.svg` (pencere/taskbar ikonu).

## 1.5 Dil ve ton

- **Tüm UI, proje adları ve loglar İNGİLİZCE** (OSYS.Sales.Core, "Build", "Sync", "up to date"…). Kod yorumları Türkçe kalabilir.
- Ton: sakin, kesin, mühendisçe. Ünlem yok, espri yok. Net rakam + net durum: `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`.
- Statü daima **renk + glyph + metin** üçlüsü (colorblind-safe).

---

# 2. Pencere Yerleşimi

Tek pencere, min ~1240×620 (action bar 1240'ta tam sığar; Clean/Optimize bu yüzden ikon butondur — §2.7). Kök: `surface-base` zemin, 1px `border`, radius 8, overflow hidden. Dikey sıra:

```
┌────────────────────────────────────────────────────────┐
│ 1 TITLE BAR (40px)                                     │
├────────────────────────────────────────────────────────┤
│ 2 STICKY ŞERİT (32px) + global progress (2px)          │
├───────────────────────────┬────────────────────────────┤
│ 3a Dependency graph       │ 3c Console                 │
│    (sol kolon üst)        │    (sağ kolon üst)         │
│ ──── yatay splitter ────  │ ──── yatay splitter ────   │
│ 3b Projects listesi       │ 3d Event stream            │
│    (sol kolon alt)        │    (sağ kolon alt)         │
├──────── dikey splitter (kolonlar arası) ───────────────┤
│ 4 ACTION BAR (42px)                                    │
└────────────────────────────────────────────────────────┘
```

**Splitter'lar:** 7px tutma alanı, görünür kısım 1px çizgi (`border`; sürüklerken `amber-border`). Sınırlar: kolon %28–72, satırlar %18–82. Konumlar kalıcı (prototipte localStorage `delta-bo-layout-v1`; WPF'te user settings).

**Görünüm modları** — title bar sağındaki 3 ikon (aktif olan vurgulu):
- **quad** (varsayılan): 4 panel, preset'e dönünce split'ler 50/50/50'ye sıfırlanır.
- **list**: graf gizli — sol kolon tamamen proje listesi; sağ split %50.
- **focus**: graf gizli + sağda konsol %76 (stream küçülür).

## 2.1 Title bar (40px)

- Sol — **logo kilidi**: `app-mark.svg` (ürün markası, 19px, tam renk) + `Build Orchestrator` (12px/500, `text-secondary`) + 1px × 13px dikey ayraç + **firma logosu** (`delta-logo-dark.svg`, 10px, %55 opaklık). **v1.11.0: `OSYS · main · worktree` metni KALDIRILDI** — title bar yalnız markayı taşır; workspace adı alt bara geçti (§2.7-5a), branch/worktree bilgisi zaten alt bardaki chip'lerde.
- Sağ: 3 layout ikonu (quad/list/focus, tooltip'li) · 1px dikey ayraç · dişli (Settings, tooltip: "Settings — repository root and layer definitions") · **📣 (What's new, v1.13.0 — Lucide *megaphone* 13px, tooltip: "What's new — release notes (Ctrl+F1)"; görülmemiş sürümde "What's new in <sürüm> (Ctrl+F1)" + 5px amber nokta)** · **ⓘ (About, tooltip: "About — version, shortcuts and diagnostics (F1)")** · pencere kontrolleri (min/max/close).

## 2.2 Sticky şerit (32px + 2px progress)

Kalıcı durum satırı; `surface-base`, altta `border-subtle`.

- Solda faz metni — mono 12px, çoğu `▸ ` önekiyle:
  - boot: `▸ Waiting for Sync — project states appear after Sync` (dim)
  - syncing: `▸ Sync — git fetch origin…`
  - idle: `▸ Ready — 14 to build · 22 up to date`
  - running: `▸ Building 7/14 · 24s · ~35s left` (ETA <4s kala `· almost done`; ETA 5s'e yuvarlanır)
  - stopped: `▸ Stopped — 7/14 · rest queued` (dim)
  - done (başarılı): `Completed — 14 succeeded · 22 skipped · 1m 12s` (yeşil + ✓ glyph)
  - done (hatalı): `Completed — 5 failed · 12 succeeded (4 dependency-affected) · 17 skipped · 1m 30s` (kırmızı + ✗ glyph)
  - all-clean done: `Everything up to date — 36 projects checked in 8.4s, nothing to build` (yeşil + ✓)
- Faz metninin yanında: **o an derlenen projelerin chip'leri** (spinner ikonu + kısa ad; en çok 4, fazlası `+N`); tıkla → proje seçilir.
- Sağda **hata kümesi** (yalnız hata varken): ✗ glyph + `5 failed` (kırmızı, 500) + `· 4 dependency-affected` (dim 11px) + ilk 3 hatalı chip + `+2 more` chip (kırmızı metin; tıkla → listede `failed` filtresi). ("View failures" butonu YOK — kaldırıldı.)
- ~~Döngü kümesi chip'i~~ **v1.11.0'da kaldırıldı** (turuncu UI'dan çıktı); döngü bilgisi satırdaki tek amber üçgende ve ⚠ filtresinde. Hata kümesindeki `N failed · N dependency-affected` sayaç metinleri de kalktı — ilk 3 hatalı chip + `+N more`. Hover tooltip iki satır: `In a dependency cycle — won't be built` + mono döngü yolu `Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts`. Küme Sync'ten koşu sonuna kadar kalıcıdır — döngü bir koşu sonucu değil, bir yapılandırma hatasıdır.
- Altında **2px global ProgressBar**: değer = tamamlanan/derlenecek; renk building=amber, failed=kırmızı, done=yeşil; sync sırasında indeterminate. Radius 0.

## 2.3 Dependency graph (sol üst) — v1.3.0 "quiet graph"

Panel başlığı aynı (caps `DEPENDENCY GRAPH` + sağda mono sayaç). Panelin İÇİ yeniden tasarlandı: node üzeri ad etiketleri ve kalıcı bağımlılık çizgi ağı kaldırıldı — amaç 100+ projede bile sakin, tek bakışta okunan bir yüzey. Adlar hover tooltip'i ve seçim etiketiyle verilir.

### Yerleşim — katman bantları
- Bantlar **derlenme sırasına** göre: layer 0 (ilk derlenenler) en üstte, bağımlı katmanlar alta doğru. Bant içindeki dizilim de **build-order**dır (ilk derlenecek soldan başlar), yani göz üstten alta / soldan sağa okuduğunda derleme sırasını görür.
- Pitch (node adımı) otomatik: 44px'ten 5'e 0.5 adımla taranır; tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine sığan İLK değer seçilir → graf HER panel boyutunda tam sığar, scrollbar yok.
- Bantların eksik kalan son satırı yatay ORTALANIR; tüm blok panelde ortalanır (12px kenar payı; hesap alanı W−24 × H−24).
- Node = kare kutu, boyut = pitch×0.6 (8–24px kelepçe), radius-sm, 1.5px border, içinde Lucide `box` glyph'i (node'un %52'si, 1.8px stroke).
- Statü görünümü DS `DependencyGraphNode` tablosuyla birebir: zemin `--status-*-soft`, border `--status-*`, glyph `--status-*-text`; discovered = düz `--border-strong` + `--surface-raised` zemin; **başlangıç modu** = kesikli `--border-strong` (v1.12.0'da da korundu — kullanıcı kararı).

### Koşu yaşam döngüsü — soluk/parlak sistemi
- idle/boot/sync: tümü tam opak.
- Koşu başlayınca (phase=running): graf soluklaşır — queued/discovered opacity **0.13**; yalnız o an derlenenler tam opak.
- Proje bitince: sonuç rengine döner (succeeded yeşil / failed kırmızı / skipped gri) ve **2400ms tam opak KALIR**, sonra **700ms'de 0.2'ye** söner. Uygulama hilesi: opacity değeri anında 0.2 yazılır, beklemeyi CSS taşır → `transition: opacity 700ms var(--ease-standard) 2400ms` (timer/ek render yok).
- Koşu bitince (done/stopped): tümü sonuç renginde tam opak.
- Zemin/kenar/glyph renk geçişleri 380ms ease-standard; opaklık geçişi 280ms (hold-fade hariç).

### Renk kuralı (v1.11.0 — tek statü kanalı)
Node border'ı ve içindeki küp **aynı statü rengini** taşır; ayrı "plan" ya da "cycle" çekirdeği YOK. Görsel durumlar: **başlangıç** (kesikli border, gri küp — Sync sonrası/açılış) · **nötr** (düz gri) · **işaretli** (amber) · **derleniyor** (amber + beads) · **sonuç** (yeşil/kırmızı/gri). **Tek istisna (v1.12.0):** bu işlemde derlenmeyen **cycle üyesinde küp AMBER** (`vstate` = `cyc` gri border · `cycskip` skipped border) — satırdaki uyarı üçgeninin grafik vekili; işlemin nötr anında yanar, koşu ve finalde kalır, bir sonraki Sync/işlemle düşer. Resolve'da üye derlendiği için normal sonuç rengini alır (amber küp yok). Grafta uyarı üçgeni yoktur.

### İşlem koreografisi (v1.11.0 — açılış)
Build/Rebuild/Clean/satır aksiyonu/Resolve — hangisi tıklanırsa aynı sıra oynar (§3.1, §9 v1.11.0/4): nötr an 440ms → **random dalga** (kapsam tek tek amber'a yanar; `markStagger` 110ms/node, toplam ≤1.1s; border + zemin + küp aynı `transition-delay`) → sarı-gri an 300ms → **örtüşen veda** (griler 1120ms sönüşe başlar, 560ms sonra sarılar 440ms'de katılır — ikisi aynı anda biter) → nefes 420+240ms → koşu. Satır listesi bu fazda node'larla senkron söner.

### Bitiş koreografisi (v1.11.0 — "neon tutuşma")
Koşu bitince, **yalnız grafta**: `hold` 900ms hepsi soluk (0.16) → `neon` yalnız derlenenler (succeeded ∪ failed) **random sırayla** düzensiz titreyerek tutuşur (`bo-neon` 1150ms, `endStagger` ≤150ms/node, zincir ≤1.5s) → `bwait` 700ms nefes → `grey` kalan tüm griler (atlanan + dokunulmamış) **hep birlikte** 980ms ease-in-out belirginleşir → final. Hızlı kontrol koşusunda ve derlenen yoksa çalışmaz; yeni işlem koreografiyi anında keser. `prefers-reduced-motion`: flicker kapalı, opaklık kademeleri kalır.

### Building animasyonu — "beads"
- Derlenen node'un **2.8px dışında**, node ile eş-merkezli yuvarlatılmış-kare yörüngede dolanan sık amber noktalar.
- Çizim: SVG `rect` (fill none, stroke `--amber-text`, stroke-width 1, linecap round). Nokta deseni `stroke-dasharray: 0.01 (adım−0.01)`; adım = çevre / round(çevre/3.4) → desen çevreye TAM bölünür, ek yerinde bindirme olmaz.
- Hareket: `stroke-dashoffset` 0 → −çevre, **4200ms linear infinite** (yavaş, sabit hız).
- Yumuşak giriş/çıkış: yörünge SVG'si DOM'da sürekli durur, yalnız opaklığı değişir — building'e girişte **420ms ease-out** ile 1, bitişte **640ms ease-out** ile 0. Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup kaybolmaz.
- `prefers-reduced-motion`: beads ve akan çizgiler tamamen kapalı.

### Hover
- Node scale(1.7) (120ms ease-out), border 2px, opacity 1 (soluk moddayken bile), z-index öne.
- Tooltip: node'un üstünde 8px, GECİKMESİZ; `--surface-overlay` + 1px `--border-strong` + radius-md + popover gölgesi; Geist Mono 11px `--text-primary`; içerik = TAM proje adı (örn. `OSYS.Orchestration.Service.WorkOrder`). Yatayda panel kenarına kelepçeli (6px) — node kenardayken bile tamamen okunur. Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her zoom'da net).

### Seçim — odakla & sığdır
- Node tıklaması genel seçim modeline bağlanır (§3.3: liste kartı seçilir, konsol o projenin loguna kaskatla geçer). Listeden/stream'den seçim de grafı AYNI şekilde odaklar.
- Odak: seçili node + doğrudan deps + doğrudan dependents'ın sınır kutusu panele sığdırılır → zoom = min(W/bw, H/bh), **0.7–2.6** kelepçe (padding = 3×node + 48px), merkez ortalanır, kamera **460ms ease-in-out** kayar. Yalnız pan değil zoom da ayarlanır (kullanıcı wheel ile uzaklaşmışsa bile).
- Görsel: odak kümesi tam opak, geri kalan HER ŞEY opacity **0.1**. Seçili node'da 2px `--focus-ring` outline (offset 2).
- Bağımlılık çizgileri YALNIZ seçimde: deps→node ve node→dependents, dikey kübik bezier, amber akan kesikler (`bo-edge-flow`: dasharray 4 8 → offset −24, 640ms linear infinite; 1.2px, opacity 0.75).
- Seçili node'un altında 6px boşlukla ad etiketi: mono 10px `--amber-text`, `--surface-overlay` zemin, 1px `--amber-border`, radius-sm; panel sınırlarına kelepçeli (asla taşmaz); ekran koordinatında.
- Aynı node'a tekrar tıkla VEYA boş alana tıkla → seçim bırakılır, görünüm varsayılana döner (zoom 1, pan 0, 460ms). Seçim değişince hover temizlenir (odak kayması sonrası imleç altında bayat hover kalmaz).
- Sağ altta mono ipucu: `scroll = zoom · drag = pan`, seçiliyken `click again to release`.

### Serbest gezinme
- Wheel = zoom **0.7–5.0**, çarpan 1.14/adım, imlecin altındaki nokta sabit kalır; 160ms ease-out. (Native listener, `passive:false`.)
- Boş alanda sürükle = pan; imleç grab/grabbing; ≤3px hareket tıklama sayılır, üstü pan (drag sonrası bırakma boş-alan tıklaması TETİKLEMEZ). Sürükleme sırasında kamera transition'ı kapalı (birebir takip).
- İlk açılış (Sync sonrası): node'lar **derleme sırasıyla** belirir — `bo-reveal` (fade + 5px yukarıdan), gecikme = build-order index × 9ms (max 520ms); dalga üstten alta, soldan sağa akar.

### Kaldırılanlar
- Node üzeri kısa ad etiketleri, kalıcı bağımlılık çizgi ağı, graf içi dep-issue rozeti (dep bilgisi kartlarda yaşıyor). Eski 26px `DependencyGraphNode` bileşeni graf panelinde artık kullanılmıyor (başka yüzeyler etkilenmez).

## 2.4 Projects listesi (sol alt)

- Panel başlığı: caps `PROJECTS` + mono `build-order` etiketi; aktif filtre varsa kaldırılabilir chip (ör. `Failed ✕`).
- **Satır (36px, alt çizgi `border-subtle`):** soldan sağa:
  1. **Statü şeridi** 2px dikey (satırın tam sol kenarı; statü rengi). Seçiliyken 3px. **Başlangıç modunda düz ve soluk** — aynı `--status-skipped-border`, opaklık 0.5; işlem başlayınca 380ms'de 1'e çıkar (v1.12.0 — kesikli gradient tırtık yapıyordu, kaldırıldı).
  2. **Statü noktası** 8px — şeritle **aynı rengi** taşır (v1.11.0; ayrı plan/cycle kanalı yok), başlangıç modunda **4 yaylı halka** (v1.12.0, §1.1), işlem başlayınca dolu noktaya çapraz-söner. Tooltip taşımaz. İşaretleme dalgasında şerit ve node ile aynı gecikmeyle yanar.
  3. **Ad** 13px/500 — tek kural (v1.7.0): bu koşuda işi olan satır (dirty · queued · building · failed) `text-primary`, güncel/atlanacak satır `text-secondary` + yanında **sln adı** 12px `text-faint` (`Osys.Sales.sln`). Taşmada ellipsis.
  4. Sağ blok (min 134px, sağa yaslı — v1.16.0: en uzun karar etiketi `up to date · just now` mono 10.5px'te 132px sürüyor, 118px'lik eski yuva onu kırpıyordu): hover'da **4 ikon buton (v1.11.0)** — **play** (o projeyi derler — §3.8), **⋯** (satır menüsü: Build · Rebuild · Clean; satıra sağ tık da açar), "Reveal in Explorer" (klasör) ve "Open in Visual Studio" (kod ikonu). İkon butonları **tooltip taşımaz** (yalnız native `title` + `aria-label`); tıklayınca konsola dim not düşer; hover yokken **karar etiketi (v1.16.0)**: mono 10.5px, sağa yaslı, projenin NEDEN derleneceğini (ya da neden atlanacağını) söyleyen sabit sözcükler — `modified` (kendi dosyaları değişti) · `affected` (kendi dosyaları aynı, bağımlılığı değişti) · `never built` (diskte çıktı yok) · `failed · retry` (son derleme hata verdi) · `up to date · 2h` (güncel, atlanacak; kuyruk = son başarılı derlemenin yaşı — `just now` / `14m` / `2h` / `3d`). Derlenecek satırlar `text-secondary`, güncel satırlar `text-faint`; `·` sonrası kuyruk her zaman `text-faint`. **Statü rengi taşımaz** (kırmızı/yeşil yalnız glyph + şeritte — v1.11.0 tek kanal), ikon taşımaz, DS Tooltip taşımaz: gerekçenin uzun hâli ikon butonlarıyla aynı dilde **native `title`** ile verilir (`Its own files changed since the last build`, `Up to date — last built 2h ago`). Etiket ana proje ve harici proje için AYNI (harici projeler zaten `EXTERNAL PROJECTS` grubunda — v1.14.0; rozet yok). Yuva değişmedi: min 134px, hover'da 4 ikon butonla yer değiştirir, **satırlar arası sıçrama yok** (v1.7.0 kuralı). Karar bilinmiyorsa (Sync yapılmadı ya da branch değişti → `will: unknown`) yuva boş kalır. Commit çifti (`a3f81c2 → b7e91d4`) KALDIRILDI — gerekçesi §9 v1.16.0'da.
  5. **Statü glyph'i** 14px (halka içinde ✓/✗/—/saat, başlangıçta kesikli daire). **Tooltip YOK** (v1.11.0 — renk + glyph + süre kolonu aynı şeyi söylüyordu). Glyph her zaman GERÇEK statüyü gösterir; uyarılar yandaki slottadır.
  6. **Sabit 14px uyarı slotu**: cycle üyeliği ve/veya depIssue varsa TEK üçgen (12px), **her zaman amber** (v1.11.0 — turuncu/amber ayrımı kalktı). Tooltip **tek satır**: `In a dependency cycle` ya da `Dependency issue: Sales.Core +2`; gerekçe ve üye listesi **proje logunda**. Listede tooltip taşıyan tek öğe budur. Satırın kendisi building iken gizlenir. Slot her satırda var — **hiza asla bozulmaz**.
  7. **Süre** mono 12px sağa yaslı 46px: building=canlı sayaç, bitti=`4.2s`, yoksa `—`. Failed'da kırmızı.
- **İşlem koreografisinde satırlar (v1.13.2'de sadeleşti):** satırlar dalganın RENGİNİ taşır, **opaklığı oynamaz** — sönme/geri gelme (veda + neon finali) yalnız graf node'larında. Satırda dalga: şerit, nokta ve **proje adı aynı anda** amber/parlak olur (ad rengine de `markOrder × markStagger` gecikmesi verilir; eskiden tüm adlar dalga başında birden beyazlıyordu). Eski davranış (kapsam dışı 0.3 / kapsam 0.45 iniş): koşu başlayınca tam opaklığa dönerler. **Bitiş koreografisi (neon) satırlara UYGULANMAZ** — koşu bitiminde liste sabit kalır (kullanıcı kararı).
- **Building satırı efekti:** kart zemininde hareketsiz amber "nefes" — `amber-soft` katmanı opacity 0→0.32→0, 3.8s ease-in-out sonsuz (tepe ~%3 görünür etki). Süpürme/parlama/kayma YOK (denendi, istenmedi).
- **Failed anı:** satır 360ms yatay shake (±3px), bir kez.
- **Sol şerit (v1.11.0, v1.12.0, v1.13.2):** her satırda görünür — **başlangıç modunda da tam opak, standart gri** (v1.13.2: 0.5 opaklık kalktı; Sync sonrası satır artık silik değil), işlem başlayınca renk DEĞİŞMEZ, işaretlenince amber, bitişte sonuç rengi. Sync şeridi soluk hâle döndürür (plan göstermez). Şerit 2px (seçilide 3px) ve **1px dikey iç boşluklu** — boşluk satır ayracı kadar: bitişik satırlarda tek kesintisiz çizgiye kaynamaz, ama araları da açılmaz.
- **Döngüdeki satırlar (v1.11.0):** ayrı renk YOK — şerit, nokta ve glyph normal statüyü izler; döngü üyeliği yalnız **amber uyarı üçgeni** + proje logundaki gerekçeyle ifade edilir. Standart Build üyeleri `skipped — in a dependency cycle, not rebuilt` olarak düşürür; derleyen tek akış **Resolve cycles** (§3.7).
- **Katman başlıkları:** 24px, caps 11px + mono sayı (satır adedi); **birikerek yapışır** — i'inci görünür başlık `top = i×24px`'e yapışır, alttakiler kaydıkça üsttekiler asılı kalır.
- **Gruplama:** Settings'teki regex tanımlarıyla, ilk eşleşen kazanır; eşleşmeyen → `Other`. **Varsayılan: katman YOK → başlıksız tek liste, build sırasında.**
- **Follow-mode:** koşarken ve seçim yokken liste frontier'i yumuşak takip eder (ilk building satırı görünür tutulur; scroll animasyonu 550ms'de bir, hedef sapması <54px ise dokunulmaz). Karta tıklayınca takip durur; seçim kalkınca sürer.
- **Boş durum — kurulum daveti (v1.8.0):** ortada `Configure the workspace` (14px/600) + açıklama `Set the repository root — and, if projects should be grouped, the layers. Discovery starts right after.` + **kurulum listesi** (292px kart, `surface` zemin + 1px border + radius-md; 30px satırlar, aralarında `border-subtle`): `Repository root` → mono `Not set` (`text-secondary`) · `Layers` → mono `N defined` ya da `Optional` (`text-faint`); altında **iki buton yan yana (v1.10.0)**: primary `Open settings` (dişli) + secondary `Import settings…` (upload) — ikincisi Settings'i açıp dosya seçiciyi hemen tetikler. Butonların altında 11px `text-faint` not: `Import fills the form from a settings file — nothing is applied until you save.` **Dosya seçtiren `Choose Folder` butonu KALDIRILDI** — başlamak için birden çok ayar gerektiğinden repository root artık Settings içinde (§2.9).
- Filtre eşleşmezse: `No projects match this filter.`

## 2.5 Console (sağ üst)

- Zemin **`#060608`, radius 0**, padding 8×12. **Mono 12px, ağırlık 300 (Light)**, satır 1.55. Alta yapışık scroll (kullanıcı 48px'ten fazla yukarı kaydırırsa serbest bırakılır, dibe inince yeniden yapışır). **`⌄ latest` pill:** kullanıcı dipten uzaktayken (≥48px) panel alt-ortasında küçük mono pill (surface-overlay, border-strong, radius-md, popover gölgesi); tıkla → yumuşak en alta iner. Koşu bitmiş olsa da yukarı kayınca çıkar — klasik dip afordansı; dibe dönünce/tıklayınca kaybolur, konsol↔proje-log geçişinde dibe sabitlenir.
- **İki mod:**
  - **Anlatı (seçim yok):** satır = **yalnız metin** — saat sütunu ve `▸` ikon kolonu YOK (v1.6.0); tüm satırlar imleçle aynı sol hizada başlar. Satır türü yalnız **renkle** ayrılır: cmd=`text-primary`, info=`text-secondary`, dim=`text-faint`, success/warn/error=ilgili `-text` tonu. Canlı gelen satırlar **anında** basılır — daktilo yok. En altta tek **prompt satırı**: yanıp sönen blok imleç (7×13px, 1.1s blink), idle/boot'ta yanında `ready` (dim).
  - **Seçili proje logu:** panel başlığı değişir → `← Back` ghost buton + proje adı (mono) + statü glyph + statü adı + (varsa) `▲ dependency issue` rozeti. Building ise sonda amber `build in progress ▮`.
- **Görünüm değişimi = kaskat (v1.6.0).** Proje logu açılırken ve `← Back` ile ana loglara dönerken konsol içeriği **aşağı serilerek** açılır (tek parça "tilt in": perspective 900px, rotateX 7° + 14px, 340ms) — pat diye değişmez; proje listesi filtresindeki kaskatla aynı dil. **İki yön de aynı sürede** biter: serilme 14 adım × 26ms ≈ 360ms, adım başına satır sayısı içeriğe göre ölçeklenir (3 satırlık log ile 200 satırlık anlatı aynı hissedilir). Log yoksa gösterilen tek açıklama satırı da aynı pop-in ile gelir. Kaskat yalnız **açılış anlık görüntüsüne** uygulanır; sonrasında akışa katılan canlı satırlar anında basılır. Log yoksa: skipped → `Skipped — up to date; not built in this run. Last successful build: 2h ago (a3f81c2)`; queued → `Queued — waiting for dependencies: Sales.Core, Security`; diğer → `No log yet — output streams here once the build starts.`
- Akış yönü **klasik: en yeni altta** (her iki panelde; "en yeni üstte" değerlendirildi, İSTENMEDİ).
- Panel başlığı sağında mono `N lines`.

## 2.6 Event stream (sağ alt)

- Panel başlığı: caps `EVENT STREAM`; sağda mono `N events`.
- Satır (min 24px, mono 12px): saat + glyph (ok=✓, fail=✗, skip=—, sync/info/task=amber `▸`, done/taskdone=✓/✗) + metin. Renkler: fail=`status-fail-text`, skip=`text-dim`, done=yeşil/kırmızı (`status-success-text` / `status-fail-text`), sync/info/task=`text-dim`, taskdone=`status-success-text`, ok=`text-secondary`.
- **Renk kuralı:** renk kıt bir kaynaktır, yalnız iki tür satıra verilir — (a) dikkat gerektiren **hata**, (b) bölüm kapatan **özet**. Tekrar eden başarı satırları nötr (`text-secondary`) kalır; statüyü satırın kendi **glyph'i** taşır, metin taşımaz. Glyph rengiyle metin renginin eşleşmesi gerekmez (yeşil ✓ + nötr metin normaldir). Skip satırları geri plandadır ama **okunur** kalır (`text-dim` #76767e, `text-faint` değil): "neden derlenmedi" en sık sorulan sorudur. Saat sütunu her satırda `text-faint`, olay türünden bağımsız. Değerlendirilip alınmayan iki alternatif — her satırın kendi statü renginde olması (özet satırı üstündeki yeşillerin arasında kaybolur, kırmızı tekliğini yitirir) ve proje adı `text-primary` + kuyruk `text-dim` (satırda üç gri tonu, konsolla yan yana kalabalık); seçenek tablosu `Stream Color Options.dc.html`.
- Örnek metinler: `OSYS.Domain.Service built (2.9s)` · `OSYS.Sales.Core failed — 2 errors (3.1s)` · `OSYS.Base skipped — up to date` · `Sync — 14 to build, 22 up to date` · `Build started — 14 projects, parallelism 4` · `Completed — 5 failed · 12 succeeded · 17 skipped · 1m 30s · 4 dependency-affected`.
- **En yeni satır daktiloyla yazılır**; ama sık ardışık olaylarda (<340ms) ve hata olaylarında ANINDA basılır. Aktif satır: `OSYS.Server.Api building…` — saat + **imleç** + amber daktilo metni (konsolla aynı dil).
- Projeli satırlar tıklanabilir → seçim; seçili satırda sol 2px amber şerit + `surface-raised` zemin.
- Tümü başarılı biten koşuda done satırı bir kez yeşil parlar (background `success-soft` → transparent, 1.1s).
- Bakım görevi sürerken en altta **canlı görev satırı**: saat + imleç + amber daktilo metni (`cleaning Osys.Parts.sln…`, `restoring Osys.Web.sln…`) — build'in `… building…` satırıyla aynı dil, ama tıklanamaz. Görev bitince `Clean complete — …` satırı bir kez yeşil parlar.
- Alta yapışık scroll + `⌄ latest` pill (konsolla aynı kural).

## 2.7 Action bar (42px, altta)

`surface` zemin, üstte 1px `border`. Soldan sağa:

1. `Sync` — secondary sm buton (döngü ikonu). Koşarken, bakım görevi sürerken veya repo yokken disabled.
2. **Bakım grubu (Clean / Optimize / Resolve cycles)** — chip ağırlığında tek kutu: 24px yükseklik, `surface-raised` zemin, 1px `border`, radius-xs, `overflow:hidden`; içinde üç 28×22 ikon buton, aralarında 1px×14 `border` ayraç. **Etiket yok** (bar 1240'ta ancak böyle sığar) — anlam tooltip'te: `Clean — /t:Clean on every solution, then remove bin/, obj/, artifacts/`, `Optimize — restore packages, prune the cache, rebuild the dependency index`, `Resolve cycles — build the 3 cycle projects in two passes: stale references first, then rebuild until they converge` (döngü yokken `— no dependency cycles detected` + disabled). İkonlar Lucide: **eraser** (Clean), **gauge** (Optimize), **unlink** (Resolve — ikon `--status-cycle-text` turuncusu), 12px, currentColor. Yürüyen görevin/Resolve'un butonu `active` olur ve ikon yerine **amber spinner** döner; diğerleri + Sync + Build + branch/worktree o sırada disabled. Davranış → §3.4, §3.7.
3. 1px dikey ayraç.
4. **Sayaç chip'leri — v1.11.0: ÇOKLU filtre** (`filter` bir Set; çipler bağımsız açılır-kapanır, seçili küme **VEYA** ile birleşir — ✓ + ✗ = "bu koşuda derlenenler"; arama ile VE). Aktif çip **kendi statü renginde** yanar. Set: `Σ 36` (tümü/filtre temizle) · spinner+`4` (building; boşken gri nokta) · `✓ 14` · `✗ 5` · `— 17`. **İki istisnai chip yalnız listede karşılığı varken çıkar:** `⚠ 3` (döngü; turuncu cycle glyph'i, filtre `cycle`, tooltip `In a dependency cycle — filter`) ve `▲ 4` (dependency-affected; kırmızı üçgen, filtre `dep`). İkisi de nadir durumları anlatır — boş/gri chip taşımazlar. Aktif filtre chip'i vurgulu.
5. Esnek boşluk.
5a. **Workspace adı (v1.11.0):** mono 11px `OSYS`, `text-dim`, branch chip'inin solunda; tooltip = repository root (`D:\src\osys`). Title bar'dan buraya taşındı.
6. `branch: main ▾` chip (branch ikonu) → **Branch popover**.
6a. **`3 behind` chip (v1.16.0)** — branch chip'inin hemen sağında, **yalnız geride kalınca** görünür: Lucide *arrow-down-to-line* (12px) + mono sayı + `behind`. Nötr chip ağırlığı (amber yok — statü rengi değil). Görünme koşulları: aktif branch seçili **ve** sayı biliniyor (fetch başarılı) **ve** > 0. Çevrimdışı (fetch degraded) → sayı bilinmez → chip yok; güncel → chip yok; başka branch seçiliyken (worktree modu) chip yok — derleme worktree'den yapılıyor, ana ağacı güncellemenin anlamı yok. Koşu/görev sırasında diğer bar kontrolleriyle aynı mid-run kilidi (disabled). **Tıklama = ff-only pull** (→ §3.9). Tooltip: `Fetch found 3 new commits on origin/main. Click to fast-forward; nothing is merged or rewritten.`
7. `worktree: off ▾` chip (ağaç ikonu) → **Worktree popover**.
8. `Debug | Release` segment (sm).
9. `perf: Balanced` chip — tıkla döngü: Full(6) → Balanced(4) → Light(2) paralellik. Tooltip YOK (istenmedi).
10. 1px ayraç.
11. **Build split-button** (primary md, play ikonu): sol `Build` (F5, **stale set**) + sağ `▴` ok → yukarı menü **üç madde (v1.11.0)**: `Build` (play · F5) · `Rebuild` (rotate-cw · Ctrl+F5, tümü) · `Clean` (brush — VS *Clean Solution*: yalnız `/t:Clean`, cache'lere dokunmaz, herkes başlangıç moduna döner). Menü ikonları satır menüsüyle aynı ailedendir (play · rotate-cw · brush, tek grid/stroke). Koşarken buton `Stop`'a döner (her koşuda: Build, Rebuild, satırdan build, Resolve). Tooltip yok.

Toast/popup YOK (karar). Kaynak bilgisi yalnız worktree popover'ındaki `source` satırında.

## 2.8 Popover'lar

Ortak: chip'in üstünde 8px boşlukla açılır, `surface-overlay` zemin, 1px `border-strong`, radius 8, overlay gölgesi, 140ms pop-in (4px yukarı + scale .985→1). Dışarı tıkla / Esc → kapanır.

**Branch (272px):** caps başlık `SWITCH BRANCH` · arama inputu (büyüteç, `Search branches…`) · satırlar (28px): seçilide ✓ amber, değilse branch ikonu; ad mono; aktif branch'te amber `active` rozeti, diğerlerinde mono SHA. Eşleşme yoksa `No branches match "q".` · Alt not: `Picking a non-active branch requires a worktree; the active branch stays untouched.`
- **Aktif olmayan branch seçilince:** worktree zorunlu ON (switch disabled), tüm proje durumları sıfırlanır (discovered/unknown), faz boot'a döner; konsola `git switch --detach f3a02c8  # release/2026.06 (worktree required)` + `Branch changed: release/2026.06 — Sync required` yazılır.

**Worktree (300px):** caps `WORKTREE` · Switch `Build in worktree` (zorunluysa disabled) · açıklama metni: zorunlu → `Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched.` / açık → `The committed HEAD builds in a separate worktree; local changes excluded.` / kapalı → `Off: in-place build — local changes included.` · Açıkken `TARGET WORKTREE` listesi: `main-2 (new)` (auto) + mevcutlar (`main-1 — 2 days ago · clean`), hover'da çöp kutusu (sil) · En altta `source` satırı: mono, `working directory — local changes included` veya `committed HEAD (main) → main-2`.

## 2.9 Settings dialog (760px)

> **v1.16.0:** External projects bölümünün açıklama metni düzeltildi — kart sırası **derleme sırası değil**, yalnız çalışma kopyalarının güncellenme sırasıdır; hariciler arasındaki derleme sırası bağımlılıktan gelir. "before" vurgusu ve üç parçalı yapı kaldı. Metin: `Projects outside the repository root — a folder, a solution or a project file, and whether it comes from Git or TFVC. The working copy root is found from the path upwards. They are built before everything else; the rest follows the layers below. Card order only sets the order the working copies are updated — among themselves they build in dependency order.` ("before" sözcüğü uygulamada 500 ağırlık + `text-secondary` ile vurgulu kalır.) Bölümün geri kalanı (kartlar, `Source` seçimi, `Pull before build` switch'i, boş durum, Save kuralı) DEĞİŞMEDİ.

> **v1.15.0:** External projects **bölüm başlığı satırı** iki uçlu: solda caps `EXTERNAL PROJECTS`, arada 1px `border-subtle` çizgi, sağda caps `PULL BEFORE BUILD` + DS `Switch` (açıklama tooltip'te). Gövde metnine ve kart listesine dokunulmaz. Save'e kadar uygulanmaz; kalıcılık `delta-bo-pull-external-v1` (varsayılan açık), JSON alanı `pullExternalBeforeBuild`. Davranış → §9 v1.15.0.

> **v1.14.0:** gövde `bo-scroll` + `maxHeight: min(56vh, 460px)` ile kendi içinde kaydırılır ve bölüm sırası **Workspace → External projects → Layers**'tır (harici projeler derleme sırasının başında olduğu için katmanlardan önce durur). Harici proje kartı: grip + mono sıra numarası + tam genişlik path input'u + sil; ekleme `Add external project`; kalıcılık `delta-bo-external-v1`; export/import alanı `externalProjects`. Harici projeler motora da bağlıdır: listede/grafta en üstte kendi katmanı, derleme sırasında ilk. Ayrıntı: §9 v1.14.0.

Dişliden — ve first run'da liste panelindeki `Open settings` butonundan — açılır. DS Dialog: `surface-overlay`, radius 8, overlay gölge, scrim. İki bölüm: **Workspace** ve **Layers**.

**Workspace (v1.8.0)**

- Caps başlık `WORKSPACE` + açıklama: `Repository root — the folder that holds the OSYS solutions. Projects and the dependency graph are discovered from it on Sync.`
- Tek satır: mono input (esnek genişlik, placeholder `D:\src\osys`) + secondary `Browse…` butonu (klasör ikonu; gerçek uygulamada klasör seçici açar).
- Root boşken `Save` disabled — uygulamanın çalışması için zorunlu tek ayar budur.
- 18px boşluk + 1px `border-subtle` ayraç, sonra Layers bölümü.

**Layers**

- Caps başlık `LAYERS` + açıklama: `Projects are grouped by the first matching pattern (regex on the project name), top to bottom; card order is the layer order in the list. Non-matching projects fall under Other.`
- **Katman kartları** (36px + 6px boşluk): grip tutamacı (sürükle-bırak sıralama — kart yarım satır eşiğiyle yer değiştirir, sürüklenen kart raised zemin + strong border) · ad inputu (170px) · regex inputu (mono, esnek; geçersiz regex kırmızı invalid durumu) · çöp ikonu (sil).
- `+ Add layer` ghost buton.
- **Varsayılan BOŞ** — boş durumda kesikli kutu: `No layers yet — projects show as a single list in build order.`
- Footer: solda ghost `Load sample layers` (örnek 6 OSYS katmanını doldurur) · 1px ayraç · **Export / Import / Clear ikon butonları (v1.10.0)**: Export → `build-orchestrator-settings.json` indirir (`app`, `version`, `repositoryRoot`, `layers[{name, pattern}]`); Import → dosya seçiciden gelen değerleri **forma** yükler; Clear (çöp kutusu) → **iki aşamalı**: ilk tık ikonu kırmızıya çevirip `Click again to clear root and all layers` uyarısını basar (2.4s sonra kendini iptal eder), ikinci tık root'u ve tüm katmanları boşaltır. Üçü de yalnız FORMU değiştirir — Save'e kadar hiçbir şey uygulanmaz (onay dialogu yok). Geri bildirim aynı satırda 2.4s: yeşil `Exported …` / `Imported — N layers · root set` / `Cleared — nothing is applied until you save`, kırmızı `Invalid settings file`. · sağda `Cancel` + primary kaydet butonu. **First run'da (workspace yok) etiket `Save and sync`**, sonrasında `Save`. Disabled koşulu: root boş **veya** herhangi bir katman adı boş / regex geçersiz.
- Kaydet → listede gruplar güncellenir, konsola dim not: `Layer definitions updated — 6 layers` / `Layers removed — single project list`; root değiştiyse ayrıca `Repository root → D:\src\osys — Sync required`. Kalıcı (prototipte localStorage `delta-bo-layers-v2`).
- **First run'da kaydet → workspace açılır ve Sync kendiliğinden başlar** (konsolda `workspace: D:\src\osys — 36 projects discovered`, ardından sync akışı). Ayrı bir onay adımı yok.
- Eşleşme sayacı gösterilmez (istenmedi).

---

## 2.10 About dialog (660px, F1)

Title bar'daki **ⓘ** ikonundan veya **F1** ile açılır (F1 toggle). DS Dialog ile aynı kabuk (`surface-raised`, 1px `border-strong`, radius-lg, tek overlay gölge, scrim, 180ms fade+6px yukarı giriş) ama **başlık satırı yok** — yerine kimlik bloğu:

- **Başlık bloğu** (18px padding): solda `app-mark.svg` (30px) + `Build Orchestrator` (17px/600) · alt satır `Ordered, incremental builds for a multi-project .NET solution.` (12px `text-dim`) · mono 11px `text-faint`: `1.2.0 · © 2026 Delta`. Sağda **firma kilidi**: 1px × 30px `border-subtle` ayraç + sağa yaslı caps 11px `text-faint` `LICENSED TO` etiketi + firma logosu (13px, %80 opaklık). Firma logosu yoksa blok tamamen düşer.
- **Segment** (DS.Segment, sm): `Shortcuts | Environment | Third-party` (v1.13.0'da What's new sekmesi ÇIKARILDI → §2.11). Segment satırının altında **1px `border-subtle` hairline** dialog genişliğinde uzanır — sekme şeridi ile içeriği ayırır. İçeriği çerçeveleyen kutu ya da 3D/oyuk (inset) kanal YOK: DS yapıyı hairline ile taşır, bevel/gölge kullanmaz; 620px dialogda kutu-içinde-kutu ağırlık yapıyordu. Gövde min-yükseklik 236px — tab değişince dialog zıplamaz.
- **Shortcuts:** satır 26px; solda açıklama (13px `text-secondary`), sağda Kbd chip'leri (birden fazlaysa 4px arayla). İçerik: F5 / Ctrl+F5 + Shift+F5 / Ctrl+F / F1 / Ctrl+F1 / Esc / Alt+B.
- **Environment:** tek liste, 2 kolon — etiket 130px (12px `text-dim`) + değer mono 12px `text-secondary`, satır 26px. Satırlar: App version · Engine version · Engine PID · .NET runtime · OS · MSBuild · Repository root · State file · Logs · Worktree pool. **Uzun yollar kesilmez, kaydırılır (v1.13.1):** değer hücresi `.bo-xscroll` — `white-space: nowrap` + `overflow-x: auto`, **scrollbar görünmez** (`scrollbar-width: none`, webkit yüksekliği 0) ve hücrenin üzerinde fare tekerleği çevrilince yol sağa kayar (`onWheel` → `scrollLeft += deltaY`, yalnız taşan hücrede; `overscroll-behavior-x: contain`). Ellipsis + `title` ipucu ve tek-kolon/kırmalı `PATHS` bloğu denendi, İSTENMEDİ. Tam metni kopyalamak için footer'daki `Copy diagnostics`.
- **Third-party:** ad + mono sürüm (70px) + sağa yaslı mono 11px lisans (92px). **Prototipteki liste PLACEHOLDER** — gerçek paketlenen bağımlılıklardan üretilecek.
- **Footer** (üstte `border-subtle`): solda ghost `Copy diagnostics` (kopya sonrası 1.4s `Copied` + yeşil ✓; panoya sürüm + tüm Environment satırları düz metin gider), sağda secondary `Close`.
- Esc önceliği: What's new → About → Settings → popover/menü → seçim. Scrim tıklaması da kapatır.

## 2.11 What's new dialog (620px, Ctrl+F1) — v1.13.0

Sürüm notları **kendi dialogu**; About'un sekmesi değil. Title bar'daki **sparkle** ikonundan (dişli ile ⓘ arasında) veya **Ctrl+F1** ile açılır (toggle). About'la aynı kabuk (`surface-raised`, 1px `border-strong`, radius-lg, tek overlay gölge, scrim, 180ms fade+6px) ama 620px. Gövde yüksekliği **sabit 400px** (min değil) + `bo-scroll` ile dikey scroll: `Earlier versions` açıldığında dialog uzayıp ekranın altına/üstüne yapışmaz, liste kendi içinde kayar. Sürüm listesi zamanla uzayacağı için bu sabit kutu kalıcı çözüm.

- **Başlık satırı** (`16px 28px 12px 18px`, altında 1px `border-subtle`; sağ padding 18+10 = **28px** çünkü gövdenin 10px `bo-scroll` scrollbar'ı içeriği içe alıyor — sürüm bloğu böylece listedeki tarih sütunuyla tam sağa hizalanır): solda `What's new` (15px/600) + alt satır `Release notes for Build Orchestrator.` (12px `text-dim`); sağda **iki satırlı sürüm bloğu** (v1.13.1): 10px caps `INSTALLED VERSION` (`text-faint`) üstte, altında mono **15px/500 `text-primary`** sürüm numarası, sağa yaslı. Blok `align-items: flex-end` + `margin-bottom: 3px` ile kaldırılır: sürüm numarasının taban çizgisi soldaki açıklama satırının taban çizgisiyle hizalanır (mono satırın `line-height: 1` olması yüzünden aksi hâlde 3px aşağıda kalıyordu). Logo bloğu ve Segment YOK — dialogun tek işi var.
- **Gövde:** `WhatsNew` listesi, min-yükseklik 236px, dikey scroll. Liste kuralları (aşağıdaki maddeler) v1.9.0'dan DEĞİŞMEDİ.
- Sürüm başlığı: mono 13px/500 numara + (kurulu sürümde) **`INSTALLED` çipi** (v1.13.1: 17px yüksek, `0 6px`, `surface-raised` zemin + 1px `border-strong`, radius-xs, 10px caps `text-dim`) + sağa yaslı mono 11px tarih. Amber rozet ve çerçevesiz caps metin denendi; nötr çip seçildi. Sürümler arasında 14px boşluk + 1px `border-subtle`.
- **Kategori satır başına değil BLOĞA yazılır** (Keep a Changelog mantığı): 6px renkli kare (radius 1) + caps 11px `text-dim` başlık, altında 13px maddeler 13px içeriden (4px aralık, `leading-snug`). Satır başında ikon/sigil/tooltip YOK — ikonlu ve diff-sigilli varyantlar denendi, blok başlığı seçildi (`Whats New Icon Options.dc.html` deney dosyası).
- Kategoriler, sabit sırayla: **Added** (amber) · **Changed** (`text-dim`) · **Fixed** (yeşil) · **Performance** (turuncu) · **Removed** (kırmızı). Boş kategori başlığı hiç çizilmez.
- **Son 3 sürüm açık**, gerisi ghost buton `Earlier versions (N)` altında katlı — buton sarmalayıcısı `margin-left: -10px` ile Button'ın kendi yatay padding'ini iptal eder, böylece etiket **içerik kolonuyla tam sola hizalanır** — tıkla, hepsi açılır (geri katlama yok; dialog yeniden açılınca 3'e döner). Tam geçmiş erişilir, ekran sade kalır.
- Veri kaynağı: `app/release-notes.js` (`window.DELTA_BO_NOTES` — sürüm, tarih, `[kind, text]` maddeleri; kind = feature|change|fix|perf|removed). İçerik §9'un kısa İngilizce özeti; her yeni sürümde buraya da madde eklenir.
- **Footer:** yalnız sağda secondary `Close`. `Copy diagnostics` About'ta kalır.
- **Görülmemiş sürüm işareti:** localStorage `delta-bo-seen-version-v1` ≠ `ABOUT_VERSION` ise megaphone butonunda 5px amber nokta (top 2 / right 2) + tooltip `What's new in <sürüm> (Ctrl+F1)`. Dialog **açıldığı anda** görüldü işaretlenir (`onSeen`), nokta söner. Açılış pop-up'ı / karşılama toast'ı YOK.
- Esc/scrim kapatır; Esc'te en üst katman olduğu için About açıkken de önce bu kapanır (zIndex 101 / About 100).

---

# 3. Davranış ve Akışlar

## 3.1 Fazlar

`empty → boot → syncing → idle → marking → running → done | stopped` · dikey eksen: `task` (bakım görevi — §3.4)

**v1.11.0 — her işlem aynı borudan geçer:** `_beginOp` (konsol + stream temizlenir, kalıcı işlem etiketi) → `_neutralize` (herkes düz nötr gri) → `_mark` (faz `marking`: nötr an → random dalga → sarı-gri an → örtüşen veda → nefes) → koşu. Girişler: `beginBuild` · `beginRebuild` · `beginProject(name, mode)` · `beginProjectClean` · `startTask` · `startResolve`. Koşu bitince `_runEndFinale` (hold → neon → nefes → grilerin açılışı). Ayrıntı: §9 v1.11.0 / 4-5.

- **empty:** repo yok. Liste panelinde kurulum daveti + `Open settings` (§2.4); title bar'da mono `not configured`; graf/konsol/stream bekleme metinleri; şerit `Not configured — repository root not set`; Sync/Build/chip'ler disabled. Çıkış: Settings → root gir → `Save and sync`.
- **boot:** repo var, Sync yapılmadı. Konsolda açılış satırı: `Build Orchestrator 2.4.1 — Osys.sln loaded (36 projects) · main`. Tüm satır/node'lar **başlangıç modunda** (satırda soluk düz şerit + 4 yaylı nokta, grafta kesikli node border; v1.12.0).
- **Sync:** konsola `▸ git fetch origin main` → **`HEAD a3f81c2 · 3 commits behind origin/main`** (güncelse `HEAD b7e91d4 · up to date with origin/main`; çevrimdışıysa bu satır hiç yazılmaz, yerine degrade uyarısı — v1.16.0) → `Sync complete — 7 changed projects, 14 to build` + `22 projects up to date (will skip)`. **Hiçbir şey renklenmez** (v1.11.0): herkes başlangıç modunda kalır; **planı renk yerine karar etiketi okutur** (v1.16.0 — §2.4): derlenecek satırlar `modified` / `affected` / `never built` / `failed · retry`, kalanlar `up to date · 2h`. Şerit `Ready — 14 changed · 22 up to date`, işlem pill'i `SYNC`. Açılış = otomatik sync, kapat-aç hep bu moda döner.
- **Build (F5):** boot'ta Sync otomatik koşar; diğer her durumda **mevcut durumdan** koşar — stale set = değişen + hatalı + hatalıların bağımlıları (`beginBuild`). **Konsol ve stream her Build'de temizlenir** (v1.11.0) ve koreografi baştan oynar. Bir önceki koşu her şeyi güncellediyse `_reseedChanges()` yeni bir HEAD üretip 2-4 proje bayatlar (kapsam ≤%45) — tekrar Build de tam süreçle koşar. Stale set gerçekten boşsa (Sync sonrası) hızlı kontrol koşusu (`Everything up to date`, koreografi yok). Önceki koşuda hata varsa düzeltme uygulandı varsayılır (baseFails kapanır). Konsola `▸ msbuild Osys.sln /m:4 /p:Configuration=Debug — 14 projects, 22 skipped`.
- **Rebuild (Ctrl+F5):** tümü dirty kabul edilir (`beginRebuild`); aynı koreografi, kapsam = döngü dışı tüm projeler.
- **Clean (Build menüsü, v1.11.0):** VS *Clean Solution* — 7 solution için yalnız `msbuild /t:Clean`; cache'lere dokunulmaz. Bitişte herkes **başlangıç moduna** döner, `allDirty=true` → sonraki Build tam derleme. Derin Clean (bin/obj + NuGet cache) bakım kutusunda kalır (§3.4).
- **Stop:** building olanlar queued'a döner; konsola warn `Build stopped — 7/14 completed`, stream'e `Stopped — 7 remaining projects queued`. Sonrası **Build** kaldığı yerden sürdürür; elapsed yeni koşuda sıfırlanır. Koşu sürerken F5 = Stop. **Marking fazında da çalışır** (v1.11.0): koşu hiç başlamaz, konsola `Cancelled — build not started`.

## 3.2 Scheduler kuralları (gerçek uygulamada Core'un davranışı — UI bunu yansıtır)

- Derleme sırası **liste sırasıdır** (katman → tanım sırası). Sıradaki projenin bağımlılığı bitmemişse **İLERİ ATLANMAZ** (`if (!ok) break`) — paralellik katman içinde doğal oluşur. Paralellik üst sınırı perf ayarından (2/4/6).
- **Hatalı bağımlılık ALT PROJELERİ BLOKLAMAZ:** bağımlılar son başarılı çıktıyla yine derlenir; kök hata adları `depIssue` olarak zincir boyunca aşağı taşınır. Bu projeler:
  - log başında warn satır(lar)ı alır: `warning: OSYS.Sales.Core failed in this run — last successful output referenced (yesterday 18:42)` (dolaylıysa: `warning: failure in dependency chain (Sales.Core) — referenced outputs may be stale`),
  - kartta/grafta üçgen rozet taşır, `dependency-affected` sayacına girer,
  - stream'de `built — dependency issue (2.4s)` olarak görünür; konsolda warn tonunda.
- **Döngü üyeleri standart koşuya hiç girmez (v1.7.0):** `skipped — in a dependency cycle, not rebuilt` olarak düşer (çözülmüşlerse normal `up to date`); bitiş özetine `· N cycle projects skipped` eklenir. Derlenmemiş döngü üyesine bağımlı projeler depIssue taşır (`warning: X is in a dependency cycle and was not rebuilt — last known output referenced`). Döngüyü yalnız **Resolve cycles** derler (§3.7).
- Temiz projeler bağımlılıkları çözülünce **dalga dalga skip** edilir (tek seferde hepsi değil — tik başına ~3, all-clean'de ~12).
- Succeeded olan projenin `will` alanı `clean` olur, `reason` düşer ve `lastBuiltAt` şimdiye çekilir → satır `up to date · just now` yazar (v1.16.0; ayrı bir görsel kanal yok — v1.11.0). Failed olan projede `lastFailed` işaretlenir → satır `failed · retry`.
- ETA: kalan iş / paralellik, üstel yumuşatma (0.75 eski + 0.25 yeni).
- **Log statüyle tutarlı:** succeeded projenin logu `Build succeeded — 0 errors, N warnings (4.2s)` ile biter; `error CS…` / `Build FAILED` satırları YALNIZ gerçekten başarısız olan projede görünür. Hero/temiz koşuda hiçbir yeşil projenin logunda hata satırı olmaz — statü ve log her zaman aynı şeyi söyler.

## 3.3 Seçim modeli

- Kart, graf düğümü veya stream satırına tıkla → **her yerde senkron seçim**: graf düğümü komşularıyla panele sığdırıp ortalar (460ms), liste karta kaydırır, konsol tam loga geçer (kaskat açılım), panel başlığı `← Back` moduna geçer.
- Aynı öğeye tekrar tıkla veya `Back` veya Esc → seçim kalkar; koşuyorsa follow-mode kaldığı yerden sürer. Sim/koşu seçimden etkilenmez.
- Esc önceliği: açık dialog → popover'lar/menü → seçim.
- Kısayollar: **F5 = Build**, **Ctrl+F5 (veya Shift+F5) = Rebuild**, **F1 = About**, Esc yukarıdaki gibi. (Gerçek uygulamada global hotkey RegisterHotKey — plan v6.)

## 3.4 Bakım görevleri: Clean ve Optimize

Her ikisi de action bar'daki ikon grubundan tetiklenir (§2.7-2) ve aynı iskeleti kullanır: sıralı **adım listesi**; her adım kendi süresi kadar sürer, bitince konsola bir satır + stream'e bir olay basar. Faz `task`; `engine.task = {kind, title, i, total, label, stream, startT}` UI'ın tek ilerleme kaynağıdır.

**Ortak UI davranışı**
- Sticky şerit: amber spinner + `▸ Cleaning 4/9 · Osys.Parts.sln · 3s` (başlık · adım/toplam · yürüyen adımın etiketi · geçen süre); altındaki 2px progress = tamamlanan adım / toplam adım, amber.
- Konsol: her adım bir satır (anında basılır — §2.5).
- Event stream: her adım bir olay (`task` kind, amber `▸`, `text-dim`) + en altta canlı daktilo satırı; bitişte `taskdone` olayı (✓ yeşil, bir kez parlar).
- Kilitleme: görev sürerken Sync, Build (+ menü oku), branch ve worktree chip'leri, Debug/Release ve diğer bakım butonu disabled; F5/Ctrl+F5 no-op. Görev durdurulamaz (Stop yok) — 4-5 saniyelik işler.
- Görev bitince şeritte **sonuç satırı** kalır (yeşil ✓): `Clean complete — 4.4 GB reclaimed · all 36 projects will rebuild` / `Optimize complete — 431 packages restored · 1.2 GB reclaimed`. Sonraki Sync/Build/görev bu satırı temizler. Toast YOK.

**Clean** — VS'teki *Clean Solution* + `bin`/`obj` silme birlikte:
- Adımlar: her solution için `msbuild <sln> /t:Clean /m — N projects · D directories · X MB` (7 solution) → `removing artifacts/ · TestResults/ · .vs/ — 14 directories · 486 MB` → warn `obj/project.assets.json removed — NuGet restore required on next build`.
- **Solution temizlendiği anda** o solution'ın projeleri **başlangıç moduna** (kesikli, `fresh`) döner — liste ve graf canlı olarak boşalır, sayaç chip'leri düşer.
- Bitişte: `Clean complete — 36 projects · 301 directories removed · 4.4 GB reclaimed (5.1s)` + warn `All 36 projects will rebuild — outputs removed`. Faz `idle`, `allDirty=true` → **sonraki Build tam derleme** (stopped durumu düşer). Döngü üyelerinin çıktıları da silinir (dirty olurlar) ama standart plana yine girmezler (v1.7.0).

**Optimize** — restore/cache/index bakımı; derleme durumunu DEĞİŞTİRMEZ:
- Adımlar: her solution için `nuget restore <sln> — N packages, K downloaded | all cached` → `pruning global package cache — 38 orphaned packages · 1.2 GB reclaimed` → `dependency index rebuilt — 36 projects · 64 references · 0 cycles` → `compiler server warmed — 4 msbuild nodes · incremental cache primed` (sayı = perf paralelliği).
- Bitişte: `Optimize complete — 7 solutions restored · N packages · 1.2 GB reclaimed (5.4s)` + dim `Build state unchanged — no projects marked dirty`. Faz görevden önceki fazına döner (biten koşunun sonucu korunur).

**Gerçek uygulama notu:** adım süreleri prototipte sabittir; gerçekte her adım kendi işini bitirince ilerler. Sözleşme aynı: adım başına tek konsol satırı + tek stream olayı, şeritte adım/toplam + yürüyen adımın adı.

## 3.5 Filtreler

Alt bardaki chip'ler ve şeritteki `+N more` liste filtresini kurar: `building` (queued dahil) · `succeeded` · `failed` · `skipped` · `dep` (depIssue olanlar). Aynı chip'e tekrar tıkla → temizle; `Σ` chip'i de temizler. Aktif filtre Projects başlığında kaldırılabilir chip.

## 3.6 Config / perf

- `Debug ↔ Release` (koşarken ve bakım görevi sürerken kilitli): boot değilse tümü dirty işaretlenir, konsola warn `Configuration → Release — all projects will rebuild`.
- `perf` chip döngüsü paralelliği anında değiştirir; koşarken konsola dim `parallelism: 6 (Full)`.

## 3.7 Resolve cycles (döngü çözme)

Bakım kutusunun üçüncü ikonu (**unlink**, nötr renk — v1.11.0; yalnız Sync bir döngü bulduysa enabled). Gerçek bir proje döngüsü tek MSBuild geçişiyle derlenemez; Resolve **iki geçişle** çözer:

- **Kapsam:** döngü üyeleri + bayat (dirty/failed) bağımlılıklarının kapanışı. **Ardışık** koşar (paralellik 1) — çözüm sıraya duyarlıdır.
- **Pass 1:** önce bağımlılıklar, sonra üyeler **son bilinen (bayat) referanslarla** derlenir; üye loglarında warn `warning: circular reference (Parts.Inventory, Parts.Api) — building against last known outputs`. Üye pass 1 sonunda yeşil görünür ama noktası amber kalmaz → hayır: üye pass 1'de `will=dirty` KALIR (çıktı bayat), yalnız pass 2 temizler.
- **Pass 2:** yalnız üyeler taze referanslarla yeniden derlenir (~0.45× süre) → yakınsar; `will=clean`, `lastBuiltAt` şimdiye çekilir (satır `up to date · just now`). Sonuç normal succeeded; **döngü işareti yalnız amber uyarı üçgeni** olarak kalır (v1.11.0 — turuncu nokta/çekirdek yok); konsola dim not düşer.
- **Konsol:** `osys-resolve-cycles — 3 projects in cycle + 1 stale dependencies · 2 passes` + mono döngü yolu + pass ayracı satırları (`pass 1/2 — building with last known references`, `pass 2/2 — rebuilding cycle projects to converge`); proje bitişleri normal success satırı (pass 1 üyelerinde `— stale references` eki).
- **Şerit:** amber spinner + `▸ Resolving cycles · pass 1/2 · 2/7 · 4s`; 2px amber progress = biten/toplam. **Stream:** `task`/`taskdone` olayları + `built — pass 1/2` satırları + canlı `… building…` daktilosu.
- **Bitiş:** faz `idle`; şeritte kalıcı yeşil satır `Cycles resolved — 3 projects converged in 2 passes · outputs now current` (sonraki eylem temizler). Sonuç **normal succeeded** — sayaçlara girer; ayrı "resolved" rengi YOK.
- **Kilitleme:** koşu `running` fazındadır — Sync/Build/Clean/Optimize disabled; **Stop çalışır**: scope discovered'a döner, faz `idle`, üyeler çözülmemiş kalır (konsola warn).
- Resolve sonrası Build üyeleri doğal atlar (`up to date`); yeni Sync değişiklik bulursa üyeler yine dirty işaretlenir.

---

## 3.8 Tek proje derleme (v1.10.0)

Proje satırındaki **play** ikonu o projeyi tek başına derler; yanındaki **⋯** menüsü (ve satıra sağ tık) Build · Rebuild · Clean sunar — developer, tam koşuyu beklemeden üzerinde çalıştığı projeyi denemek ister.

**v1.11.0 kapsam kuralı: YALNIZ o proje.** Bağımlılıklar derlenmez; bayat kalan bir bağımlılık uyarı üçgeni doğurur ve logda `last known output referenced` satırı görünür. "Build with dependencies" istenmedi. Satırdan tetiklemek **satıra tıklamak değildir**: seçim + filtre sıfırlanır, graf odağı açılmaz, konsol ana loga döner; konsol ve stream temizlenir ve koreografi Build ile birebir aynı oynar. Satırdan Clean o projenin çıktılarını siler (satır başlangıç moduna döner) ve diğer projeleri varsayılan görünüme çeker.

- **Kapsam (scope):** hedef proje + yalnız onun **bayat (`will: dirty`) ya da hatalı** bağımlılıkları, geriye doğru özüyle (transitif). **Koşul yok:** atlanmış (güncel), hatalı ya da **döngü üyesi** bir proje de satırından derlenebilir. Döngü üyesi *bağımlılıklar* kapsama girmez (onları Resolve cycles derler) — karşılığında standart bayat-referans uyarısı ve dep-üçgen kuralı aynen işler.
- **Scheduler:** kapsamlı koşu kendi sırasını kullanır — yalnız kapsam üyeleri gezilir, kapsam dışı bir satır sırayı **bloklamaz** (`continue`, tam koşudaki `break` değil) ve kapsam dışı bağımlılık "çözülmüş" sayılır. Derlemeye alma işi tek giriş noktasından (`_startBuild`) geçtiği için statü, depIssue, log planı ve aktif satır kuralları tam koşuyla birebir aynıdır.
- **Kurallar değişmez:** koşu tam derlemeyle **aynı motor yolundan** geçer — statü renkleri, building nefesi, spinner, graf beads'i, uyarı üçgeni ve log kuralları birebir aynıdır. Tek fark kapsamın büyüklüğü.
- **Liste yerinden oynamaz:** play'e basınca filtre sıfırlanmaz ve liste remount edilmez (reveal kaskadı tetiklenmez) — imleç altındaki satır kaydığı için yanlış projenin derlenmesi/seçilmesi sorunu böyle çözüldü (v1.10.0).
- **Seçim hedefe GEÇMEZ (v1.11.0):** satırdan tetiklemek satıra tıklamak değildir — seçim ve filtre sıfırlanır, konsol ana loga döner, graf odağı açılmaz.
- **Kapsam dışına dokunulmaz:** atlama dalgası (skip) ve "bitti mi" kontrolü `scope` kümesiyle sınırlı — diğer projeler `discovered` kalır, sayaçları şişirmez. Kapsam dışındaki bağımlılıklar "çözülmüş" sayılır (son çıktıları kullanılır).
- **UI:** Şerit ve alt bar normal koşu gibi davranır (progress = kapsam kadar), ana buton `Stop` olur. Play/Stop ikonları satırdaki diğer ikonlardan bir tık büyük (14px, diğerleri 12px) — ana eylem olduğu için. Kapsam hedefi satırında play ikonu kırmızı **Stop**'a döner ve hover olmadan da görünür; diğer satırların ikonu disabled (`Build in progress — wait or stop it first`).
- **Konsol/stream:** başlangıç `msbuild <Proje>.csproj /p:Configuration=Debug — single project + 2 stale dependencies`; bitiş `Build complete — <Proje> + 2 dependencies (3.4s)` (hata varsa `Build failed — 1 error in <Proje> …`). Stop → `Build stopped — <Proje> not rebuilt`, faz `idle`.

---

## 3.9 Uzak uç: geride kalma ve ff-only pull (v1.16.0)

Sync'in `git fetch`'i uzak ucu çözüyor; motor bundan **yerel HEAD'in origin'den kaç commit geride olduğunu** da veriyor (`behind`). Bilgi iki yerde görünür: konsoldaki Sync satırı (§3.1) ve alt bardaki `3 behind` chip'i (§2.7-6a).

**Kural (bilinçli güncelleme):** "araç ana repoda pull yapmaz" kuralı duruyor — araç **kendiliğinden asla** pull etmez. Kullanıcı chip'e basarsa **yalnız ff-only**, **yalnız aktif branch'te**. Gerekçe: fetch zaten "3 commit gerideyim" diyor; bunu gördükten sonra terminale geçip `git pull --ff-only` yazmak aracın işini yarıda bırakmak olur. Fast-forward geri alınabilir, tarihi yeniden yazmaz, birleştirme kararı vermez — aracın üstlenebileceği tek git yazma işlemi budur.

**Akış (tıklama):** `git fetch origin <branch>` → "yalnız geride miyim" kontrolü → `git merge --ff-only origin/<branch>`. Asla merge commit'i, asla rebase, kirli ağaca dokunmaz. Sonuçlar konsola yazılır:

| Sonuç | Konsol | Sonrası |
|---|---|---|
| başarı | `Pulled origin/main — fast-forward a3f81c2..b7e91d4` (info) | chip düşer → **otomatik Sync** (yeni HEAD'in planı hesaplanır); pull satırları konsolda kalır |
| kirli ağaç | `Pull refused — uncommitted changes in the working tree; commit or stash them first` (warn) | chip kalır, başka hiçbir şey değişmez |
| ayrışmış | `Pull refused — local branch has diverged from origin/main; reconcile it manually` (warn) | chip kalır |
| ağ | `Pull failed — <sebep>` (error) | chip kalır |

- **Otomatik Sync konsolu silmez** (tek istisna): normal Sync motoru resetleyip konsolu temizler, pull sonrası Sync `keepLog` ile geçmişi korur — yoksa kullanıcı kendi tetiklediği pull'un sonucunu hiç görmezdi.
- **Prototip sınırı:** başarı yolu simüle edilir (`behind: 3` → tıkla → 0). Üç ret dalı motorda kodlu (`eng.pullResult = 'dirty' | 'diverged' | 'error'`) ama UI'da tetikleyicisi yok; çevrimdışı durum da (`behind = null`) simüle edilmiyor — kural kodda: sayı bilinmiyorsa chip çizilmez ve Sync mesafe satırı yerine `Fetch degraded — origin/main unreachable; distance from the remote is unknown` yazar.

---

# 4. State (WPF/MVVM karşılığı)

- **Engine/VM durumu:** faz; proje başına `{status: discovered|queued|building|succeeded|failed|skipped, fresh: bool (başlangıç modu), will: dirty|clean|unknown (plan kanalı — görsel kanal DEĞİL), reason: changed|dependency|neverBuilt|lastFailed|null (satırdaki karar etiketi — v1.16.0), lastBuiltAt: zaman|null (null = hiç derlenmemiş), lastFailed: bool, builtSha (yalnız proje logu başlığı), startAt, doneDur, depIssue: string[]|null, log[]}` + koreografi alanları (`marked`, `markStep`, `markOrder`, `markStagger`, `endStep`, `endOrder`, `endStagger`, `op`) + **kalıcı cycle üyelik kümesi** (statü DEĞİL — v1.7.0); willBuild kümesi (döngü üyeleri hariç); uzak uç durumu `{targetSha (yerel HEAD), remoteSha (origin ucu), behind: sayı|null}` (v1.16.0); `resolveRun {list, pass, fin, total, startT}`; sayaçlar (building/succeeded/failed/skipped/queued, depIssueCount, cycle=üyelik sayısı); elapsed + ETA; anlatı satırları `{type, time, text}`; stream olayları `{kind, time, project?, text}`; aktif satır.
- **Karar (`reason`) önceliği:** hiç derlenmemiş > son derleme hata verdi > kendi dosyası değişti > bağımlılığı değişti. `will=clean` → `reason=null` (satır `up to date · <yaş>`). Zorlamalı kapsam (Rebuild, konfigürasyon değişimi) içerik gerekçesi **uydurmaz** — ayrıntı §9 v1.16.0/1.
- **UI durumu:** `selected` (proje adı|null), `filter` (**Set — çoklu, VEYA**; v1.11.0), layout `{mode, col, left, right}` (kalıcı), `layerCfg` (kalıcı), branch seçimi, worktree `{on|forced, chosen|auto}`, cfg (Debug/Release), perf.
- Konsol/stream tamponları sınırlı tutulur (prototipte ~240-260 satır).

# 5. Statü tablosu (UI metinleri İngilizce)

| Statü | Glyph | Renk | UI metni |
|---|---|---|---|
| discovered | kesikli daire | text-faint | Discovered |
| queued | saat | #9a9aa2 | Queued |
| building | dönen kesikli halka + nefes | amber | Building |
| succeeded | ✓ daire | #58cb80 | Succeeded |
| failed | ✗ daire | #ff706a | Failed |
| skipped | — daire | #888890 | Skipped |
| başlangıç modu (`fresh`) | tam opak düz şerit + 4 yaylı nokta (v1.13.2: ikisi de standart tonda) + kesikli daire glyph (DS, değişmedi) / kesikli node border (değişmedi) | `--status-skipped-border` | tooltip yok — Sync sonrası ve açılışta herkes bu moddadır (v1.12.0) |
| cycle üyesi, bu işlemde derlenmedi (`cyc` / `cycskip`) | gri node border + **amber küp** (grafta); satırda normal gri + uyarı üçgeni | `--amber-text` | işlemin nötr anında yanar, finalde kalır; Resolve ile derlenince sonuç rengine döner (v1.12.0) |
| işaretli (`marked`, koreografi kapsamı) | amber şerit + nokta + node | `--amber` | işlem tıklandıktan sonra random dalgayla yanar |
| ~~cycle üyeliği~~ (v1.11.0'da görsel kanal kaldırıldı) | yalnız uyarı slotunda ▲ | amber `--amber-text` | tooltip: `In a dependency cycle` (gerekçe proje logunda) — eski turuncu nokta/çekirdek yoks builds it in two passes` + döngü yolu |
| dep-affected (ortogonal) | uyarı slotunda ▲ (cycle ile birleşik, tek üçgen) | amber `--amber-text` | tooltip: `Dependency issue: X — last successful output referenced` |
| ~~will-build (ortogonal dot)~~ | **v1.11.0'da KALDIRILDI** — nokta ve node çekirdeği artık statü rengini taşır | — | plan bilgisi satırın **karar etiketinde** (v1.16.0): `modified` · `affected` · `never built` · `failed · retry` · `up to date · 2h` (eski çift SHA kalktı) |

# 6. Assets

**Ürün logosu (Build Orchestrator).** Uygulamanın kendi markası; Delta'ya değil ürüne ait. Üç varyant, üç ayrı iş için — birbirinin yerine kullanılmaz:

| Dosya | Nerede | Notlar |
|---|---|---|
| `prototype/assets/app-icon.svg` | Uygulama ikonu: .exe, taskbar, kısayol, bildirim, yükleyici | Near-black tile (`#141417→#0A0A0C`, border `#2A2A30`, radius %11 ≈ 31/286) + amber gradient chevron. **Yalnız zemini olan yerlerde**; 16/32/48/256 PNG/ICO buradan üretilir. |
| `prototype/assets/app-mark.svg` | Uygulama içi: title bar (19px), About (30px), splash | Tile YOK (şeffaf). Şeritler DS nötr rampası (`#44444B / #2A2A30 / #A9A9B0 / #EDEDEE`), chevron amber. |
| `prototype/assets/app-mark-mono.svg` | Tray, mono/tek renk bağlamlar, devre dışı durumlar | Tek renk `#EDEDEE`; hiyerarşi opaklık kademeleriyle (1 / .6 / .5 / .32). Tray'de sistem temasına göre rengi değiştirilebilir. |

**Renk (karar).** Logo DS'in kurumsal paletinde: tile near-black (`#141417→#0A0A0C`), şeritler nötr rampadan, chevron **amber** (`#FFB52E→#8B5907`). Uygulama içi markada chevron gradienti kısaltıldı (`#FFB52E→#C9860C`) — 19px'te dibin near-black zeminde kaybolmaması için. Logo artık UI accent'iyle aynı amber'ı kullanıyor; bu bilinçli — marka arayüzle tek palette konuşuyor. Karşılığında **title bar'daki chevron görsel olarak accent ağırlığı taşır**: o bölgede başka amber öğe (chip, buton, vurgu) konumlandırılmaz.

**Firma logosu (opsiyonel, müşteriye özel).** Uygulama Delta'ya özel değil; kurulumu yapan firma kendi logosunu ekleyebilir. Yalnız iki yerde görünür: **title bar** (ürün markasının sağında, ayraçtan sonra, 10px, %55 opaklık) ve **About başlığı** (`LICENSED TO` bloklu, 13px). Her ikisinde de ürün logosu önde: daha büyük, tam renk, solda. Firma logosu SVG olarak beklenir; dark UI için açık varyantı (ör. `delta-logo-dark.svg`) kullanılır. Yükseklik sınırlıdır, genişlik serbest — wordmark'lar bozulmadan sığar.

- `prototype/assets/delta-logo-dark.svg` — örnek firma logosu (dark UI wordmark, amber yay korunur).
- `prototype/assets/delta-app-icon.svg` — Delta'nın kendi uygulama ikonu; **artık Build Orchestrator ikonu değil**, yalnız referans olarak duruyor.
- Fontlar: Geist + Geist Mono (şimdilik Google Fonts; pakete gömülecek — bkz. 1.2 not).
- Tokens: `prototype/_ds/…/tokens/*.css` → WPF ResourceDictionary'ye çevrilecek tek doğruluk kaynağı.

# 7. Dosyalar (referans)

- **`Build Orchestrator (standalone).html` — çift tıkla, tarayıcıda çalışır.** Tek dosyaya paketlenmiş prototip; internet/sunucu gerekmez. Tasarımı CANLI incelemek için bunu kullan.
- `prototype/Build Orchestrator.dc.html` — prototipin kaynak hali. Not: tarayıcılar `file://` altında `.jsx` yüklemeyi engeller; bu kaynağı doğrudan açmak yerine standalone dosyayı aç (ya da klasörü basit bir web sunucusuyla servis et).
- `prototype/app/BuildApp.jsx` — tüm UI davranışının referans kodu (panel yerleşimi, animasyon süreleri, kopya metinleri buradan doğrulanabilir).
- `prototype/app/build-data.js` — simülasyon motoru + 36 projelik örnek OSYS grafı; **scheduler kuralları (3.2) burada kodlanmıştır** — Core implementasyonunda birebir bu semantik hedeflenir.
- `prototype/_ds/…` — design token'ları ve DS stilleri.

# 8. Bilinçli KARARLAR / YAPILMAYACAKLAR (tekrar önerme)

- Toast/popup yok · "View failures" butonu yok · perf/Build tooltip'i yok · katman eşleşme sayacı yok · sürüm notları için açılış pop-up'ı yok (About → What's new + ⓘ noktası).
- Clean/Optimize/Resolve: etiketli buton yok (bar taşıyor), onay dialogu yok; Clean/Optimize durdurulamaz, Resolve durdurulabilir (`running` fazı); ayrı loading overlay'i yok.
- **Continue ve Retry failed YOK (1.7.0)** — Build her zaman stale set'i derler (Stop sonrası devam + hata sonrası retry bunun doğal sonucu). Yeniden önerme.
- Building efekti yalnız sabit "nefes"; süpürme/parlama denendi, İSTENMEDİ.
- Konsol/stream akışı klasik (en yeni altta); "en yeni üstte" İSTENMEDİ.
- Cull/agrega (çok-solution rollup) görünümü GEREK YOK.
- Emoji, gradient, amber dışı dekoratif renk, panel gölgesi, backdrop-blur YASAK.
- Varsayılan katman konfigürasyonu BOŞ (tek liste); örnek katmanlar yalnız "Load sample layers" ile.
- **v1.11.0:** renk YALNIZ son işlemin hikâyesini anlatır — tek statü kanalı (şerit + nokta + glyph + node border + küp aynı renk). Ayrı will-build kanalı ve turuncu cycle işareti kaldırıldı; döngü yalnız amber üçgen + log ile ifade edilir. Kırmızı badge eklenmez (kırmızı = derlendi ve patladı).
- **v1.11.0:** proje listesi KART yapısına geçmez (36 projede yoğun liste doğru form) · satırdan "Build with dependencies" eklenmez · bitiş koreografisi (neon) satırlara uygulanmaz, yalnız grafta yaşar · satır tooltip'i yalnız uyarı üçgeninde (gecikmeli/satır üstü tooltip denendi, istenmedi) · işaretleme dalgası derleme sırasıyla değil RANDOM akar.
- Cycle ile dep-affected uyarıları TEK amber üçgende birleşti (v1.7.0) — ayrım tooltip satırlarında; ayrı sütun/ayrı renk taşınmıyor.
- Dep-affected tooltip metni yeterli (`Dependency issue: X — last successful output referenced`); tıklanabilir bağımlılık adı, satır geneli hover ve ek "output may be stale" satırı İSTENMEDİ.
- `⚠` cycle ve `▲` dep-affected chip'leri yalnız listede karşılığı varken görünür; kalan beş sayaç chip'i her zaman durur.
- **v1.16.0:** satırda commit çifti (`a3f81c2 → b7e91d4`) geri gelmez — kararı commit değil diskteki içerik verir; yuvada karar etiketi durur. Etikette statü rengi, ikon ve DS Tooltip yok; satırda tooltip taşıyan tek öğe hâlâ uyarı üçgeni.
- **v1.16.0:** araç ana repoda **kendiliğinden** pull etmez; kullanıcı `N behind` chip'ine basarsa yalnız ff-only, yalnız aktif branch'te. Merge commit'i, rebase, kirli ağaçta zorlama ve otomatik pull YOK. Chip yerine pasif uyarı, şeritte metin ve chip menüsü denendi — İSTENMEDİ (§9 v1.16.0).

---

# 9. Sürüm geçmişi

Her yeni ekleme/çıktıda sürüm artar ve bu bölüme ne değiştiği ayrıntılı yazılır. Sürüm numarası aynı zamanda uygulamanın About penceresinde ve `Copy diagnostics` çıktısında görünür (`prototype/app/BuildApp.jsx` → `ABOUT_VERSION`).

Sürüm başlıkları `v` önekiyle yazılır (`## v1.7.0`) — spec bölüm numaralarından (`## 1.1 Renkler`) böyle ayrılır.

Numaralama: yeni özellik / davranış değişikliği / yeni ekran → **minor** (1.7.0 → 1.8.0); küçük düzeltme, metin-renk-hiza rötuşu, tek bileşen ince ayarı → **patch** (1.8.0 → 1.8.1). Patch sürümler de bu bölüme girer, kısa bir madde yeter.

| Sürüm | Tarih | Konu |
|---|---|---|
| **v1.16.0** | 2026-09-10 | **Karar diskten okunur**: satırdaki commit çifti yerine karar etiketi (`modified` · `affected` · `never built` · `failed · retry` · `up to date · 2h`) · alt barda `N behind` chip'i + ff-only pull · Sync satırı uzak uç mesafesini yazıyor |
| **v1.15.0** | 2026-09-09 | **Build öncesi harici pull**: Settings → External projects içinde `Pull before build` switch'i; açıkken her build harici working copy'leri önce günceller |
| v1.14.0 | 2026-09-08 | **Harici projeler**: ayarlarda yeni bölüm (Layers'tan önce) + yol taraması, listede/grafta en üstte kendi katmanı, derleme sırasında ilk · Settings gövdesi kaydırılır |
| v1.13.2 | 2026-09-08 | Başlangıç modu satırda tam opak · dalgada ad + şerit + nokta aynı anda · listede sönme/geri gelme kalktı · Sync'te liste başa döner · bitiş koreografisi tam görünümde · ⋯ menüsü toggle |
| v1.13.1 | 2026-09-08 | Dialog genişlikleri içeriğe ve büyüme yönüne göre ayrıldı (Settings 760 · About 600 · What's new 560) · Clear onay metni kısaldı |
| v1.13.0 | 2026-09-08 | What's new kendi dialogu oldu (About sekmesinden çıktı) · title bar'da ayrı megaphone butonu + Ctrl+F1 |
| v1.12.1 | 2026-09-08 | Konsol imleci: her kırpmanın dibinde satır paletinden sıradaki renge sıçrar (cmd · info · success · warn · error · dim) |
| v1.12.0 | 2026-09-08 | Cycle üyesinde amber küp (gri node içinde, işlem başından finale) · başlangıç modu satırda kesiksiz: soluk düz şerit + 4 yaylı nokta (node kesikli kaldı) · Resolve dalgası yalnız üyeler + neon bitiş |
| v1.11.0 | 2026-08-27 | Renk sadeleştirme: tek statü kanalı · başlangıç modu · işlem koreografisi (açılış + bitiş) · satır aksiyonları · Build menüsünde Clean · çoklu filtre |
| v1.10.0 | 2026-08-13 | Satırdan tek proje derleme · ayarları dışa/içe aktarma + temizleme |
| v1.9.0 | 2026-08-13 | What's new sekmesi: kategorili sürüm notları + görülmemiş sürüm işareti |
| v1.8.0 | 2026-08-13 | First run kurulum akışı: Settings'e yönlendirme · repository root Settings'e taşındı |
| v1.7.0 | 2026-08-13 | Cycle akışı + üç-kanal statü modeli · Resolve cycles · Build sadeleşmesi |
| v1.6.0 | 2026-08-09 | Konsol sadeleşti (saat/ikon kolonu yok, daktilo yok) · panel geçişinde tilt-in · "⌄ latest" pill |
| v1.5.2 | 2026-08-09 | Action bar chip kuralı: dependency-affected chip'i koşullu |
| v1.5.1 | 2026-08-09 | Dependency-affected sayacı listeyle hizalandı |
| v1.5.0 | 2026-08-09 | Döngü ve bağımlılık uyarıları anlaşılır hâle geldi (tooltip'ler) |
| v1.4.0 | 2026-08-09 | Bakım görevleri: Clean ve Optimize |
| v1.3.0 | 2026-08-06 | Dependency graph yeniden tasarlandı ("quiet graph") |
| v1.2.1 | 2026-08-05 | Logo kurumsal palete çevrildi |
| v1.2.0 | 2026-08-05 | Ürün logosu ve marka varyantları |
| v1.1.0 | 2026-08-04 | About penceresi (F1) |
| v1.0.0 | — | İlk handoff paketi |

## v1.16.0 — 2026-09-10

**Kararı diskteki içerik verir; satır o kararı söyler (§2.4, §2.5, §2.7, §2.9, §3.1, §3.2, §3.9).**

Motor değişti: bir projenin derlenip derlenmeyeceğine artık commit değil, **projenin kendi dosyalarının diskteki içeriği** karar veriyor — ana repo ve harici kökler için tek kural. Bunun sonucu olarak satırdaki `a3f81c2 → b7e91d4` çifti kararı anlatmıyordu ve yanıltıyordu: sağ taraf pull yapılmamış bir uzak commit'ti, sol taraf projeye değil repoya aitti. Bu sürüm üç şey yapar — satır kararı söyler, "geridesin" bilgisi tıklanabilir bir chip olur, bir metin düzeltilir.

### 1 — Satır sha yuvası → karar etiketi (§2.4)

Commit çifti kalktı; yerine kararın gerekçesi geldi. Sözcükler sabit (git + MSBuild'in ortak dili):

| Durum | Etiket | Motorun verdiği olgu |
|---|---|---|
| Kendi dosyaları değişmiş, derlenecek | `modified` | `will=dirty`, `reason=changed` |
| Kendi dosyaları aynı, bağımlılığı değişmiş | `affected` | `will=dirty`, `reason=dependency` |
| Hiç derlenmemiş (diskte çıktı yok) | `never built` | `lastBuiltAt=null` → `reason=neverBuilt` |
| Son derlemesi hata vermişti | `failed · retry` | `lastFailed=true` → `reason=lastFailed` |
| Güncel, atlanacak | `up to date · 2h` | `will=clean`, `lastBuiltAt` (göreli yaş) |

- **Yuva aynı, genişliği bir kez büyüdü:** sağ blok mono 10.5px, sağa yaslı; hover'da 4 ikon butonla yer değiştirir, **satırlar arası sıçrama yok** (v1.7.0 kuralı). Ölçüldü (Geist Mono 10.5px, tabular): `up to date · just now` 132px · `up to date · 51m` 101px · `failed · retry` 88px · `never built` 69px · `modified` 50px. Eski 118px yuva en sık görülen etiketi (koşu biter bitmez tüm succeeded satırlar `up to date · just now` yazar) taşımıyor, kolonu esnetip ad kolonundan yer çalıyordu → **min genişlik 134px**; en uzun etiket sığar, sağ kenar hizası ve ad kolonu her satırda sabit kalır.
- **Yeni token/renk açılmadı:** derlenecek satırlarda `text-secondary`, güncelde `text-faint` ayrımı korundu; `·` sonrası kuyruk (`retry`, `2h`) her zaman `text-faint` — asıl sözcük önde okunur. Etiket **statü rengi taşımaz**: yeşil/kırmızı yalnız glyph ve sol şeritte (v1.11.0 tek statü kanalı). `failed · retry` de nötr yazılır; kırmızı "bu koşuda patladı" demek, etiket ise "bir sonraki koşuda ne olacak" diyor.
- **İkon yok.** Satırda zaten şerit + nokta + glyph + uyarı slotu var; beşinci bir grafik işaret 36 satırda gürültü. Denendi (kalem/ok/uyarı ikonlu etiket), İSTENMEDİ.
- **Tooltip:** DS Tooltip yok — listede tooltip taşıyan tek öğe hâlâ uyarı üçgeni (v1.11.0 kararı, RowTip geri getirilmedi). Uzun gerekçe ikon butonlarıyla aynı dilde **native `title`**: `Its own files changed since the last build` · `Its own files are unchanged — a dependency changed` · `No build output on disk` · `The last build of this project failed` · `Up to date — last built 2h ago`.
- **Başlangıç modu (§3.1) artık renk olmadan da okunuyor:** Sync hiçbir şeyi renklendirmez, ama her satır kararını yazar — eski kural ("değişen projelerde çift SHA korunur") yerine bu geçti. Çift SHA "iki farklı commit" diyordu, etiket doğrudan "derlenecek / güncel" diyor.
- **Karar bilinmiyorsa yuva boş:** `will=unknown` (Sync yapılmadı ya da branch değişti) → hiçbir şey yazılmaz. Boş yuva "henüz bilmiyorum"un doğru karşılığı.
- **Zorlamalı kapsam olguyu ezmez:** Rebuild (ve konfigürasyon değişimi) kapsamı tüm projeler olur, ama içeriği güncel bir satır `up to date · 2h` yazmaya devam eder — kapsamı şerit/nokta/işaretleme dalgası anlatır. Sebep: etiket bir **disk olgusudur**, plan kanalı değil (plan kanalı v1.11.0'da kaldırıldı). Alternatifler denendi: hepsine `modified` yazmak (yalan), altıncı bir sözcük (`forced` — sabit sözcük listesi bozulur), yuvayı boşaltmak (`unknown` ile karışır). Konfigürasyon değişiminde ise olgu gerçekten değişir (o konfigürasyonun çıktısı bayat) → herkes `modified`.
- **Model:** `curSha` kalktı. Gelen alanlar: `reason`, `lastBuiltAt` (null = hiç derlenmemiş), `lastFailed`. `builtSha` yalnız proje logu başlığındaki `Last successful build: 2h ago (a3f81c2)` satırını besler — o satır duruyor, ama mutlak saat (`yesterday 18:42`) yerine satırdaki **aynı göreli yaşı** yazıyor; iki yerde iki farklı zaman görünmesin. `targetSha` motorda kaldı (HEAD — konsol satırları ve pull için).
- **Hiç derlenmemiş ve hatalı projeler artık plana kök olarak girer** (`computeWillBuild(allDirty, forced)`): "diskte çıktı yok" da bir bayatlık nedenidir. Clean (her iki türü de) çıktıları sildiği için tüm satırları `never built` yapar — sonraki tam derlemeyi renk olmadan da açıklar.

### 2 — `N behind` chip'i ve ff-only pull (§2.7-6a, §3.9)

Sync'in fetch'i uzak ucu çözüyor; motor artık mesafeyi de veriyor. Alt barda branch chip'inin hemen sağında, **yalnız geride kalınca** görünen nötr bir chip: `3 behind` (Lucide *arrow-down-to-line* + mono sayı). Tıklama = ff-only pull; sonrası otomatik Sync.

- **Neden bir kural güncellemesi:** "araç ana repoda pull yapmaz" duruyor — araç **kendiliğinden asla** pull etmez. Ama fetch zaten "3 commit gerideyim" diyorsa, kullanıcıyı terminale göndermek aracın işini yarıda bırakmaktır. Fast-forward, aracın üstlenebileceği tek git yazma işlemidir: tarih yeniden yazılmaz, birleştirme kararı verilmez, kirli ağaca dokunulmaz, geri alınabilir. Bu yüzden yalnız ff-only, yalnız aktif branch, yalnız kullanıcı tıklamasıyla.
- **Görünme kuralları:** geride **ve** çevrimiçi (sayı biliniyor) **ve** aktif branch. Çevrimdışı → sayı bilinmez → chip yok (uydurma sayı yazılmaz). Güncel → chip yok. Başka branch seçiliyken (worktree modu) chip yok: derleme worktree'den yapılıyor, ana ağacı güncellemenin anlamı yok. Koşu/görev sırasında diğer bar kontrolleriyle aynı mid-run kilidi.
- **Konsol:** Sync satırı da açık yazılıyor — `HEAD a3f81c2 · 3 commits behind origin/main` / `HEAD b7e91d4 · up to date with origin/main` (eski `HEAD b7e91d4 — computing osys-state diff` kalktı; "osys-state diff" kullanıcıya bir şey söylemiyordu). Çevrimdışıysa mesafe satırı hiç yazılmaz: `Fetch degraded — origin/main unreachable; distance from the remote is unknown`.
- **Pull sonuçları** (dördü de motorda; prototip başarı yolunu simüle eder): başarı `Pulled origin/main — fast-forward a3f81c2..b7e91d4` → chip düşer → otomatik Sync · kirli ağaç `Pull refused — uncommitted changes in the working tree; commit or stash them first` · ayrışmış `Pull refused — local branch has diverged from origin/main; reconcile it manually` · ağ `Pull failed — <sebep>` (chip kalır). Komut satırları da yazılır: `git fetch origin main` + `git merge --ff-only origin/main`.
- **Otomatik Sync konsolu silmez** (tek istisna, `keepLog`): normal Sync motoru resetleyip konsolu temizler; burada temizlerse kullanıcı kendi tetiklediği pull'un sonucunu göremezdi.
- **Tooltip:** `Fetch found 3 new commits on origin/main. Click to fast-forward; nothing is merged or rewritten.` — ikinci cümle, tek tıkla ne OLMADIĞINI söylüyor; korkulan şey merge/rebase.
- **Denenen ve bırakılan:** (a) pasif uyarı (şeritte ya da branch chip'inde nokta) — bilgi var, eylem yok; kullanıcı yine terminale gidiyor. (b) Şeritte metin (`3 commits behind origin/main`) — şerit koşunun durumunu anlatıyor, git durumunu değil; kalıcı bir satır orada yer kaplıyor. (c) Chip menüsü (`Pull` / `Fetch only` / `Show log`) — üç maddelik menü tek eylem için ağır; ff-only zaten tek güvenli seçenek. (d) Branch popover'ının içine `Pull` satırı — geride kalındığı görünmüyor, chip'in tüm değeri görünürlükte. (e) Sayısız chip (`behind ▾`) — sayı asıl bilgi.

### 3 — External projects açıklama metni (§2.9)

v1.14.0'dan kalan "They are built before everything else, **in this order**" cümlesindeki *in this order* düştü: kart sırası yalnız çalışma kopyalarının güncellenme (pull) sırasıdır; hariciler arasındaki derleme sırası bağımlılıktan gelir. "before" doğru olduğu için kaldı (hariciler ayrı `External` katmanında, en üstte) ve cümlenin üç parçalı yapısı korundu; sona kartların ne anlattığını söyleyen bir cümle eklendi. Metnin tamamı §2.9'un başındaki notta.

### Gerçek uygulama notu (UI öğesi → motor olgusu)

Bağlama bu eşlemeye birebir yapılacak; UI hiçbir yerde olgu üretmez, yalnız okur.

| UI öğesi | Motor olgusu |
|---|---|
| Satır etiketi `modified` | proje derlenecek **ve** gerekçe kendi dosyalarının içeriği (`reason=changed`) |
| Satır etiketi `affected` | proje derlenecek, kendi dosyaları aynı, bağımlılık kapanışından geldi (`reason=dependency`) |
| Satır etiketi `never built` | diskte çıktı yok (`lastBuiltAt=null`) — çıktı silindiğinde (Clean) de bu olgu doğar |
| Satır etiketi `failed · retry` | son derleme denemesi hata verdi (`lastFailed=true`), proje hâlâ derlenecek |
| Satır etiketi `up to date · 2h` | derlenmeyecek (`will=clean`) + son başarılı derlemenin zamanı (`lastBuiltAt`) → göreli yaş |
| Yuvanın boş kalması | karar henüz yok (`will=unknown`: Sync yapılmadı / branch değişti) |
| Proje logu `Last successful build: 2h ago (a3f81c2)` | `lastBuiltAt` + `builtSha` (o çıktıyı üreten commit) |
| `3 behind` chip'inin görünmesi | `behind > 0` **ve** `behind != null` (fetch başarılı) **ve** aktif branch seçili |
| Chip'in kaybolması | `behind = 0` (başarılı ff) — pull sonrası HEAD uzak uca eşitlenir |
| Chip'in hiç çizilmemesi | `behind = null` (fetch degraded) ya da worktree modu (aktif olmayan branch) |
| Chip'in disabled olması | koşu/bakım görevi sürüyor (diğer bar kontrolleriyle aynı kilit) |
| Konsol `HEAD … · N commits behind origin/<branch>` | `targetSha` + `behind` |
| Konsol `Pulled … fast-forward a..b` | `merge --ff-only` başarılı; `a` = eski HEAD, `b` = fetch'in çözdüğü uzak uç (`remoteSha`) |
| Konsol `Pull refused …` / `Pull failed …` | ff-only ön koşulu sağlanmadı (kirli ağaç / ayrışma) ya da ağ hatası; derleme durumu değişmez |

**Erişilebilirlik ve motion:** chip normal `button` (DS Chip) — `aria-label` metni chip'in kendi metniyle aynı (`3 behind`), tooltip `DS.Tooltip` ile bağlanır; disabled durumda %45 opaklık, layout sabit. Karar etiketi salt metin (`cursor: default`, native `title`) — ekran okuyucu satırın adından sonra doğal olarak okur. Yeni animasyon yok: etiket değişimleri renk geçişi bile taşımaz, `prefers-reduced-motion` için ek kural gerekmez.

**DS sapması yok:** yeni token/renk/bileşen eklenmedi; chip DS `Chip`, etiket mono 10.5px `text-secondary`/`text-faint` (mevcut SHA metninin ölçüleri), ikon Lucide 12px currentColor.

- Dosyalar:
  - `app/build-data.js` — `fmtAge`; `_blankState` (`reason`/`lastBuiltAt`/`lastFailed`/`builtSha`, `curSha` kalktı); `computeWillBuild(allDirty, forced)`; `_reasonFor`/`_setReasons`/`_fillReasons`/`_factReasons`; `_applySync` (kök kümesi + karar etiketleri); `startSync` (uzak uç mesafesi satırı); `pullRemote` (ff-only, dört sonuç); `reset` (`targetSha`=yerel HEAD, `remoteSha`, `behind`, `pullResult`, `keepLog`); `_startBuild` bitişi, `_processResolve`, `_cleanSolution` ve clean planlarının bitişleri (`lastBuiltAt`/`reason`); `BRANCHES` main SHA `a3f81c2` + `REMOTE_SHA`.
  - `app/BuildApp.jsx` — `decisionLabel` + `DECISION_WORDS`/`DECISION_TITLES`, `Row` sağ bloğu; `I.pullDown`; action bar `N behind` chip'i + `doPull`; `gitRef` (pull sonrası HEAD/behind reset'ten sağ çıkar), `resetEngine`/`doSync` (`keepLog`), `pickBranch`, `applyScene`; proje logu başlığındaki son derleme satırı; Settings external açıklama metni; `ABOUT_VERSION 1.16.0`.
  - `app/release-notes.js` — 1.16.0 maddeleri.

## v1.15.0 — 2026-09-09

**Build öncesi harici pull (§2.9).** Harici projeler (v1.14.0) ayrı çalışma kopyalarından gelir; derlemeden önce o kopyaların güncellenip güncellenmeyeceği artık ayarlarda, **harici projeler bölümünün kendi içinde** bir switch.

- **Yer (seçenek 1c):** Settings → **External projects** bölümünün **başlık satırı**. Tek satır, iki uç: solda caps `EXTERNAL PROJECTS`, arada esneyen 1px `border-subtle` çizgi, sağda caps `PULL BEFORE BUILD` (aynı 11px + `tracking-caps` + `text-faint`) + DS `Switch`, 9px aralık. Bölüm başlığına ait bir anahtar olarak okunur; gövde metni, kart listesi ve `Add external project` satırı hiç değişmez. Açıklama satırı yerine tooltip: `Every build starts by updating each external working copy — git pull --ff-only or tf get, one command per copy. Off: the copies are built as they are on disk.`
- **Denenen ve bırakılan yerleşimler:** `Pull Setting Options.dc.html` (1a alt satır sağa yaslı · 1b gövde metninin altında checkbox + açıklama · **1c başlık satırı — seçilen** · 1d kart içinde proje başına `Pull` kolonu · 1e listenin üstünde çerçeveli kural satırı). Ayrıca alt bardaki `pull` chip'i denenip kaldırıldı (aşağıdaki karar notu).
- **Neden action bar değil (karar):** alt bar per-run seçimleri taşır (Debug/Release, perf, branch, worktree) — bu ise harici projelerin tanımına bağlı, seyrek değişen bir kural. Bar'da chip olarak denendi ve bırakıldı: harici proje tanımlı olmayan workspace'te chip disabled kalıyor (aç/kapa afordansı yok), ayrıca bar 1240px'te zaten sıkışık. Switch hem on/off'u doğrudan gösteriyor hem de ait olduğu yerde duruyor.
- **Uygulama anı:** dialog kuralı korunur — **Save'e kadar hiçbir şey uygulanmaz** (`pullDraft`). Save'de localStorage `delta-bo-pull-external-v1` (`'1'`/`'0'`, varsayılan **açık**) yazılır ve motora geçer (`eng.pullExternal`); değer değiştiyse ve tanımlı harici proje varsa konsola dim satır: `Pull before build on — external working copies update first` / `Pull before build off — external working copies are used as they are`. Sahne değişimi ve engine reset'i ayarı bozmaz.
- **Export/Import/Clear:** JSON'a `pullExternalBeforeBuild: true|false` alanı eklendi (import boolean'sa forma yükler); Clear switch'i **açık** varsayılanına döndürür.
- **Koşuda ne oluyor:** her build (Build · Rebuild · satırdan Build/Rebuild) msbuild satırından ÖNCE pull adımını yazar. **Yol başına tek kopya** — aynı path'ten doğan 2–4 proje tek working copy'den gelir, komut bir kez koşar. Git: `git -C "<path>" pull --ff-only` → `Delta.Common — Fast-forward 78d5e3f..bd2f08f, 5 files` ya da `Delta.Common — already up to date`. TFVC: `tf get "<path>" /recursive` → `Delta.Common — 4 files updated, changeset 48263` ya da `Delta.Common — all files up to date`. Stream'e tek satır: `Pulled 2 external working copies · 1 updated`.
- **Sonuç uydurulmaz:** bir kopya, projelerinden biri o koşuda derlenecekse "güncelleme indi" (Fast-forward) sayılır, değilse "zaten güncel" yazar — konsol satırı listedeki statüyle çelişmez.
- **Kapalıyken:** tek dim satır (`Pull off — 2 external working copies used as they are`), sonra normal build. Harici proje yoksa hiçbir satır yazılmaz.
- **Tek proje derlemede** yalnız o projenin kendi working copy'si pull edilir — kapsam kuralı §3.8 ile aynı, kapsam dışına dokunulmaz.
- **Gerçek uygulama notu:** pull hatası (ff-only reddi, kilitli dosya, ağ) prototipte simüle edilmiyor. Gerçekte hata konsola `fail` satırı olarak yazılmalı ve build'in başlayıp başlamayacağına karar verilmeli — öneri: pull hatası build'i başlatmaz, şeritte hata olarak görünür.
- Dosyalar: `app/build-data.js` (`pullExternal` bayrağı, `_externalRoots`, `_pullExternals`, `startRun` ve `_startProjectNow` çağrıları), `app/BuildApp.jsx` (Settings'te `pullDraft` + Switch, export/import/clear alanı, `saveSettings(root, layers, ext, pull)`, `ABOUT_VERSION 1.15.0`), `app/release-notes.js`.

## v1.14.0 — 2026-09-08

**Ayarlarda harici projeler (§2.9).** Repository root dışında yaşayan projeler artık ayarlarda tanımlanıyor ve **derleme sırasının başına** geçiyor.

- **Yeni bölüm:** Workspace → **External projects** → Layers. Sıra kasıtlı: harici projeler önce derlenir, kalanlar katman sırasına göre gelir; bölümün konumu bu akışı anlatıyor.
- **Kart yapısı katmanlarla birebir aynı:** 36px kart, 1px `border`, `radius-md`, 6px aralık; solda grip (sürükle-bırak sıralama), hemen ardından mono path input'u — **katman kartındaki ad input'uyla aynı x'te başlar** (sıra numarası denendi, kaldırıldı: sıra zaten kart sırası) —, sağında 96px **`Source`** seçimi (`Git` · `TFVC` — yalnız bu ikisi, varsayılan Git), en sağda sil ikonu. Başlık satırı iki kolon: `PROJECT PATH` + sağa yaslı `SOURCE`. Placeholder `C:\src\shared\Delta.Common\Delta.Common.csproj`.
- **Sürükle-bırak tek mekanizmadan:** `mkDrag(ref, setIdx, setOff, setList, getLen)` fabrikası iki listeyi de sürüyor (aynı `ROWH = 42`, aynı yarı-satır eşiği, sürüklenen kart `surface-raised` + `border-strong`); katman listesinin eski `startDrag/moveDrag/endDragRow` üçlüsü buna taşındı.
- **Boş durum:** kesikli çerçeveli tek satır — `No external projects — only what is discovered under the repository root is built.`
- **Ekleme:** altta ghost `Add external project` (+ ikonu), boş path'li kart ekler. **Save**, boş path kalan bir kart varken disabled (katman adı kuralıyla aynı sertlik).
- **Gövde kendi içinde kaydırılır:** Settings içeriği `bo-scroll` + `maxHeight: min(56vh, 460px)` (min 300px korunur), `padding-right: 10px / margin-right: -10px` ile scrollbar içeriği kaydırmaz. İki liste birden uzayabildiği için dialog artık ekran kenarlarına yapışmıyor — dialog genişliği 760px'te kaldı.
- **Sürüm kontrolü alanı:** her harici proje kendi kaynağını taşır (`vcs`: `git` | `tfvc`) — projeler farklı sistemlerde durabilir. **Gerçek uygulama notu:** verilen yol ya doğrudan çalışma kopyası kökü olur ya da kök yukarı doğru aranır (`.git` / `$tf` klasörü bulunana kadar); prototipte bu arama simüle edilmez. Değer motora da geçer (`scanExternal(path, seen, vcs)` → üretilen her proje `vcs` alanı taşır); proje listesinde ekstra rozet YOK, sadelik korunur.
- **Kalıcılık:** localStorage `delta-bo-external-v1` (dizi, `[{path, vcs}]`). Save'de yazılır; katman kaydıyla aynı çağrıda (`saveSettings(root, layers, ext)`). Sayı değiştiyse şeritte bilgi satırı: `External projects → N — built before the repository projects` / `External projects cleared`.
- **Export/Import/Clear:** JSON'a `externalProjects: [{path, vcs}]` alanı eklendi (import düz string dizisini de kabul eder; eksik `vcs` → `git`). Import geri bildirimi `Imported — N layers · M external · root set`. Clear her iki listeyi de boşaltır; üçü de Save'e kadar uygulanmaz.
**Motor bağlantısı (aynı sürümde).** Harici projeler artık gerçekten derleniyor; süreç repo projeleriyle **birebir aynı**, tek fark sıradaki yerleri.

- **Yol taraması (simülasyon):** kullanıcı klasör, `.sln` ya da `.csproj` yolu verir; gerçek uygulama o yolu tarayıp içindeki tüm `.csproj`'leri listeye katar. Simülasyonda tarama deterministik taklit edilir (`scanExternal`): yolun son parçası taban ad olur (uzantı atılır), path hash'i **2–4 proje** üretir, adlandırma standardı korunur — `<Taban>.Core / .Data / .Api / .Client`, solution `<Taban>.sln`, iç bağımlılık zinciri Core ← Data ← Api ← Client, süre 1200–2640ms. Aynı taban ada sahip ikinci yol `.2` ile ayrılır. İlk proje `dirty` — bağımlılık kapanışı zinciri derler.
- **Katman:** harici projeler `layer: -1` taşır ve `ext: true` işaretlidir. `ORDER` katmana göre sıralandığı için **derleme sırasının başındadırlar**; scheduler, paralellik, statü, işaretleme dalgası, bitiş koreografisi, cycle kuralları, filtreler — hepsi aynı yoldan geçer.
- **Proje listesi:** `compileGroups` harici projeleri katman regex'lerine hiç sokmaz; en üstte kendi grubu olur — başlık **`EXTERNAL PROJECTS`** + sayaç. Katman tanımı yokken bile başlık çıkar, kalan projeler `REPOSITORY` başlığı altında toplanır.
- **Graf:** `graphLayout` bantları dizi indeksi yerine sıralı bant listesinden kurar (negatif katman için), harici projeler en üst bantta durur.
- **Çalışma anında küme değişimi güvenli:** `SimEngine.syncProjects()` (yeni) state haritasını P ile hizalar — eksik projeler başlangıç modunda doğar, kalkanlar düşer; koşu sürüyorsa `eng.stop()` ile durdurulur (proje kümesi değişti, koşu geçersiz). Graf yerleşimi memo'su artık proje imzasını da dinler (`BO.ORDER.length + ':' + BO.ORDER[0]`) ve bilinmeyen ad gelirse node çizilmez (`pos[name]` yoksa `null`). Bu üçü olmadan Settings'ten yol eklemek "Cannot read properties of undefined" ile uygulamayı düşürüyordu.
- **Yeniden keşif:** Save'de liste değiştiyse `BO.setExternal(paths)` + `syncGraphMaps()` çalışır ve **Sync yeniden koşar** (proje kümesi değişti); şeritte `External projects → N paths, M projects — built first`. First run'da workspace açılışı bu kümeyle başlar.
- **Yeniden kurulabilir türetilmiş yapılar:** `byName` · `ORDER` · `SOLUTIONS` · `EDGE_N` · `GRAPH` artık `rebuild*()` fonksiyonlarıyla yenilenir ve `window.DELTA_BO` üzerinden **getter** ile okunur (eski `const` referansları bayat kalıyordu). UI'daki `G_ORDER_IX`/`G_DEPS`/`G_DEPENDENTS` aynı şekilde `syncGraphMaps()` ile yenilenir.
- **Sabit `36` sayıları kalktı:** konsol/stream/şerit/menü metinleri `P.length` (motor) ve `BO.PROJECTS.length` (UI) okur; graf paneli sayacı `BO.EDGE_N`. Harici projelerle sayı büyüdüğünde metinler doğru kalır.
**Yapılmayanlar (tekrar önerme):** harici projeler için kart/rozet gösterimi (proje listesinde `vcs` rozeti YOK — sadelik) · sıra numarası (kart sırası yeterli) · harici projeler için ayrı statü/renk kanalı · repo projelerinin harici projelere bağımlı sayılması (bağımsız; sıra yeterli).

- Dosyalar:
  - `app/build-data.js` — `EXT_SUFFIX`/`extBase`/`hash32`/`scanExternal`/`setExternal`, `rebuildIndex`/`rebuildOrder`/`rebuildSolutions`/`rebuildGraph`, `buildGraph` bant kaydırması, sabit 36'ların kaldırılması, `window.DELTA_BO` getter'ları.
  - `app/BuildApp.jsx` — `SettingsDialog` (`extCfg` prop'u, `ext` state'i, `mkDrag`, harici projeler bölümü, kaydırılabilir gövde, export/import/clear/valid), `compileGroups` harici grubu, `graphLayout` bant listesi, `syncGraphMaps`, `BuildApp` (`extCfg` state'i + localStorage + motor kurulumundan önce uygulama, `saveSettings(root, layers, ext)` ve yeniden keşif), sabit 36'ların kaldırılması.
  - `app/release-notes.js` — 1.14.0 maddeleri. `ABOUT_VERSION → 1.14.0`.

**Gerçek uygulamada dikkat (aktarım notu):** (1) yol taraması gerçek dosya sistemine bağlanacak — klasör verildiğinde alt klasörlerdeki tüm `.csproj`'ler, `.sln` verildiğinde solution'ın projeleri; (2) `vcs` seçimi çalışma kopyası kökünü bulmak için kullanılır (`.git` / `$tf` yukarı doğru aranır) ve değişiklik tespiti buna göre yapılır (Git SHA · TFVC changeset); (3) harici projeler kendi solution'larıyla derlenir, bu yüzden bakım görevleri (Clean/Optimize) solution listesine otomatik dahil olur — `SOLUTIONS` yeniden kurulduğu için ek iş gerekmez.

## v1.13.2 — 2026-09-08

**Proje listesi ile graf arasındaki senkron sadeleşti (§2.4, §3.2).** Üç düzeltme; graf tarafına dokunulmadı.

- **Başlangıç modu artık silik değil (§2.4):** Sync sonrası sol şerit opaklığı 0.5 → **1** ve 4 yaylı nokta 0.85 → **1**; ikisi de `--status-skipped-border` standart tonunda. İşlem başlayınca **renk geçişi olmaz** — yalnız kesikli halka dolu noktaya çapraz-söner (kesikli glyph başlangıç moduyla uyumlu kalır).
- **Dalga tek harekette (§3.2):** proje adının `text-secondary → text-primary` geçişine de `markOrder × markStagger` gecikmesi verildi (`color 200ms + waveDelay`). Önce bütün adlar beyazlayıp sonra şerit/nokta sırayla amber'a dönüyordu; artık ad, şerit ve nokta aynı anda yanıyor.
- **Listede sönme/geri gelme kalktı (§3.2):** işaretleme fazındaki opaklık düşüşü (kapsam dışı 0.3 / kapsam 0.45) satırlardan çıkarıldı, `rowOp` sabit 1. Veda ve neon finali **yalnız graf node'larında** — koşu zaten başlamış olduğu için listede ikinci bir sönme okunmuyordu.
- **Sync listeyi başa alır (§2.4):** `revealKey` değiştiğinde (Sync · workspace kaydı · senaryo geçişi) ve seçim yokken liste scroll'u smooth 0'a döner — graf zaten reveal'ini yeniden oynuyor, ikisi birlikte "sıfırdan listelendi" okunur. **Build/Rebuild/Clean/Resolve ve satırdan tetiklenenler scroll'a dokunmaz** (kullanıcı kararı: imlecin altındaki satır kaçmasın; o işlemlerde zaten işaretleme koreografisi anlatıyor). Konsol + event stream ise **her işlemde** temizlenir (`_beginOp`), ardından yalnız o işlemin satırları yazılır — Sync'te motor tümden resetlendiği için aynı sonuç.
- **Graf panel ölçüsü düzeltildi (§2.5):** ölçüm ve wheel-zoom bağlantısı artık **callback ref** (`setBox`) içinde kurulur — daima monte olan node ölçülür; `!workspace` early-return dalı artık `boxRef` taşımaz. **Ölçülmeden çizim yok:** `dim` artık `{w:0,h:0}` başlar ve node katmanı `dim.w > 0` olana dek render edilmez — eskiden uydurma `640×360` ilk kare 6 bandı 194px'lik panele yerleştiriyor, node'ların yarısı panelin altında kalıyordu. **Ölçüm tek kaynağa bağlı değil:** her karede ucuz `getBoundingClientRect` karşılaştırması (rAF nöbeti — bu ortamda `ResizeObserver` hiç ateşlemiyor, `setInterval` de kısılıyor), ayrıca sync + 120ms, `window resize`, 300ms yedek nöbeti ve varsa `ResizeObserver`. 0×0 ölçüm yok sayılır, değişmeyen ölçü `setDim` çağırmaz (render churn yok). Eskiden iki farklı DOM node'u tek ref'i paylaşıyor, workspace ayarlanınca `ResizeObserver` kopmuş node'da kalıp bir daha ateşlemiyordu: `dim` ilk ölçüde (240×160) donuyor, graf panelin soluna sıkışıyor, splitter ve layout değişimlerinde reflow etmiyor, wheel-zoom ölü kalıyordu. "Panele daima tam sığar" kuralı yeniden geçerli.
- **Bitiş koreografisi tam görünümde oynar (§2.5):** koşu biterken (`eng.endStep` doğar) graf seçim odağını bırakır ve fit-all pozisyonuna 460ms glide ile döner; `hold` fazı (900ms) bu geçişi karşılar, neon zinciri hep tam görünümde başlar. Finale **seçim ve filtre dimlemesini ezer** (opaklık zincirinde `es` dalı en başta) — yoksa seçili koşuda tüm node'lar 0.1'de kalıp neon hiç görünmüyordu. Finale boyunca akan çizgiler, seçim halkası ve kelepçeli ad etiketi de kalkar; **seçim silinmez** ama **odak geri de gelmez** — fit görünüm final hâldir (`focusOff` bırakılan seçimi hatırlar). Odak yalnız kullanıcı yeni bir proje seçtiğinde (ya da aynısını yeniden seçtiğinde) açılır.
- **Odak modunda derlenen node tam opak (§2.5):** seçim dimlemesinde `live` istisnası — o an derlenen node odak kümesinde olmasa da 1.0'da kalır. Beads halkası amber dönerken gövdenin 0.1'de kalması "derlenmiyor" gibi okunuyordu.
- **Şerit pill'i satırdan tetiklemede de sade (§2.2):** `opLabel()` artık hedef adını eklemiyor (`BUILD — Sales.Core` → `BUILD`) — tek proje derlemesi şeritte tam koşudan ayırt edilmiyor, süreç de birebir aynı.
- **⋯ menüsü toggle (§2.4):** aynı butona ikinci tıklama menüyü kapatır. Dışarı-tıklama dinleyicisi `[aria-label="More build actions"]`'ı atlıyor; eskiden mousedown menüyü kapatıp click'i yeniden açtığı için toggle çalışmıyordu.
**Yapılmayanlar (tekrar önerme):** Build/Rebuild/Clean/Resolve ve satırdan tetiklemede liste scroll'u sıfırlanmadı (imleç altındaki satır kaçmasın — kullanıcı kararı) · bitiş koreografisi satırlara uygulanmadı · finale sonrası seçim silinmedi (yalnız odak bırakılıyor) · satır tooltip'i (RowTip) geri getirilmedi.

**Dosyalar:**
- `app/BuildApp.jsx` — `Row`: `rowOp` sabit 1, `waveDelay` (ad rengi gecikmesi), şerit/nokta opaklıkları, `openMenu` toggle'ı; `ListPanel`: `revealKey` scroll efekti, `RowMenu` dışarı-tıklama filtresi; `GraphPanel`: `setBox` callback ref (ölçüm + wheel), `focusOff` state'i, `finale` bayrağı, opaklık zincirinde `es` dalının öne alınması ve `live` istisnası.
- `app/build-data.js` — `opLabel()` hedef adını yazmıyor.
- `app/release-notes.js` — 1.13.2 maddeleri. `ABOUT_VERSION → 1.13.2`.

## v1.13.1 — 2026-09-08

**Dialog ölçüleri ve Clear onayı (§2.9, §2.10, §2.11).** Üç dialog aynı 620px kalıbını paylaşıyordu. Artık her biri **bugünkü içeriğine değil, büyüme yönüne** göre ölçülüyor:

| Dialog | Genişlik | Gövde min | Gerekçe |
|---|---|---|---|
| Settings | **760px** | 300px | En çok büyüyecek olan: bugün root + katman kartları (ad **ve** regex yan yana), yarın MSBuild yolu, paralellik, worktree havuzu, bildirim tercihleri. Form + iki kolonlu kart → en geniş. Gövde min-yüksekliği katman listesi boşken dialogun çökmesini engeller. Büyüme sürerse bölüm listesi (sol nav) eklenir, genişlik yine 760'ta kalır. |
| About | **660px** | 236px | Statik referans: sürüm, kısayollar, environment, third-party. 600 → 660px: en uzun yol (85 karakterlik MSBuild yolu, 12px mono'da ~610px) tek satıra hâlâ sığmaz, o yüzden genişliğin yanına **görünmez yatay scroll** kondu (bkz. §2.10) — kırpma da, satır kırma da yok. Zamanla yalnız third-party listesi uzar → dikeyde büyür. Üçünün en darı olması doğru: en az iş yapan dialog. |
| What's new | **620px** | sabit 400px | Sürekli büyüyen tek içerik. Genişlik 620'de madde satırları rahat okunuyor; kritik olan dikey: gövde **sabit** 400px ve kendi içinde scroll eder, yoksa `Earlier versions` açıldığında dialog ekran boyunca uzayıp kenarlara yapışıyordu. |

- **Environment'ta ellipsis kalktı, tasarım aynı kaldı (§2.10):** iki kolonlu satır düzeni korunuyor; değer hücresi görünmez yatay scroll kutusuna dönüştü (`.bo-xscroll`, `white-space: nowrap`, scrollbar gizli) ve hücre üzerinde fare tekerleği yolu sağa kaydırıyor. About 600 → 660px. (Yolları `PATHS` bloğunda tek kolona alıp kırmak denendi, İSTENMEDİ.)
- **What's new gövdesi sabitlendi (§2.11):** genişlik 560 → 620px, gövde sabit 400px + `bo-scroll`; `Earlier versions` açılınca dialog artık uzamıyor, liste kendi içinde kayıyor.
- **What's new tipografisi (§2.11):** güncel sürüm etiketi `CURRENT` metninden **`INSTALLED` nötr çipine** döndü; başlıktaki sürüm satırı iki satırlı bloğa çıktı (10px caps `INSTALLED VERSION` + mono 15px/500 numara) — 11px tek satır okunmuyordu. `Earlier versions` butonu içerik koluna hizalandı (`margin-left: -10px`).
- **Prototipte okunmadı noktası her açılışta görünür:** `notesUnseen()` prototipte sabit `true` (mekanizma görülebilsin diye), tıklayınca düşer. **Gerçek uygulamada kural sürüm karşılaştırmasıdır** (`notesUnseenReal`: kayıtlı sürüm ≠ `ABOUT_VERSION`) — davranış sürüm notlarında (1.13.0) tanımlı ve uygulamada bu şekilde kodlanacak.
- **Clear onayı sadeleşti:** footer geri bildirimi `Click again to clear root and all layers` → **`Click again to clear`**; armed tooltip'i de aynı metin. Ayrıca `Exported — build-orchestrator-settings.json` → `Exported — settings JSON`, `Cleared — nothing is applied until you save` → `Cleared — save to apply`. Onay dialogu hâlâ YOK (iki aşamalı ikon onayı korunuyor).
- **What's new ikonu kesinleşti:** **sparkle** (tek 4 kollu yıldız, 1.7px stroke, 13px) — dişli ve ⓘ ile aynı optik ağırlık. Adaylar `Notes Icon Options.dc.html`'de duruyor (a sparkle · b daire-içi yıldız · c bayrak · d kitap · e liste+ · f paket · g çan · h doküman · i mono sürüm pill'i); kullanıcı **a**'yı seçti.
- **Okunmadı noktası — kesinleşen senaryo:** `ABOUT_VERSION` her sürümde artar; localStorage `delta-bo-seen-version-v1` bu değerden farklıysa sparkle butonunda 5px amber nokta durur. Nokta **dialog açıldığı anda** (`onSeen`) düşer ve o sürüm için bir daha gelmez; sonraki sürümde yeniden çıkar. Uygulama ilk kurulduğunda da nokta vardır (kayıt yok). Başka hiçbir yerde işaret yok: açılış pop-up'ı, toast, menü badge'i yok.

## v1.13.0 — 2026-09-08

**What's new kendi dialogu ve kendi butonu (§2.1, §2.10, §2.11).** Sürüm notları About'un 4. sekmesi olmaktan çıktı: ayrı bir dialog (`NotesDialog`, 620px) ve title bar'da kendi girişi var — sağ blokta **dişli ile ⓘ arasında** kendi ikonu (13px, IconButton sm). *(İkon bu sürümde megaphone'du; v1.13.1'de **sparkle**'a çevrildi — güncel hâli §2.1'de.)*

- **About sadeleşti:** Segment artık `Shortcuts | Environment | Third-party`; `initialTab`/`onNotesSeen` propları ve sekme yönlendirmesi kaldırıldı. ⓘ ve **F1** her zaman Shortcuts'ta açar.
- **Yeni dialog:** About kabuğuyla aynı (surface-raised, border-strong, radius-lg, overlay gölge, scrim) ama logo bloğu ve Segment yok; başlıkta `What's new` + `Release notes for Build Orchestrator.` + sağda mono `Installed <sürüm>`, footer'da yalnız `Close`. Liste (`WhatsNew` bileşeni, kategori blokları, son 3 sürüm + `Earlier versions (N)`) v1.9.0'dan aynen taşındı.
- **Kısayol:** **Ctrl+F1** = What's new (toggle); Shortcuts listesine satır eklendi. Esc sırası: What's new → About → Settings → popover → seçim (zIndex 101 / 100).
- **Okunmadı noktası** ⓘ'dan bu butona taşındı; dialog açıldığı anda görüldü işaretlenir (`delta-bo-seen-version-v1`). Kesinleşen senaryo v1.13.1 maddesinde.
- İkon turu: çok-yıldızlı *sparkles* → *megaphone* → **sparkle** (v1.13.1). Adaylar `Notes Icon Options.dc.html`'de, dişli · aday · ⓘ üçlüsüyle gerçek 13px boyutta.
- Dosyalar: `app/BuildApp.jsx` (`I.megaphone`, `NotesDialog`, `notesOpen` state'i, title bar sağ blok, F1/Ctrl+F1 ve Esc handler'ları, `AboutDialog` sadeleşmesi, `ABOUT_SHORTCUTS`), `app/release-notes.js`. `ABOUT_VERSION → 1.13.0`.

## v1.12.1 — 2026-09-08

**Konsol imleci renk sıçraması (§2.5, §2.6).** Seçenek dosyası: `Console Cursor Options.dc.html` (1a mevcut · 1b statü paleti turu · 1c amber sıcaklık · 1d nötr↔amber · 1e tam spektrum · 1f kırpma dibinde sıçrama · 1g son satırın rengi). Kullanıcı **1f**'yi seçti; palet isteği üzerine dört statü rengi yerine **konsol satır paleti** kullanıldı.

- **Bileşen:** `Cursor` (7×13 blok, `verticalAlign -2`) — konsol prompt satırı (`ready`), event stream canlı satırı (building… / görev adımı / idle saat satırı), "Waiting for a workspace" ve seçili proje logundaki "build in progress" hepsi aynı bileşeni kullanır → değişim hepsinde birden.
- **Kırpma DEĞİŞMEDİ:** `bo-blink` 1.1s ease-in-out sonsuz (opacity 1 → 0.1 → 1).
- **Renk:** ikinci animasyon `bo-hop` 6.6s **linear**, **6 kesikli adım** (her adım 1.1s = bir kırpma): `--text-primary` (cmd) → `--text-secondary` (info) → `--status-success-text` → `--amber-text` (warn) → `--status-fail-text` (error) → `--text-faint` (dim). Sıra ve tonlar `NARR_COLORS` ile aynı — imleç, bir konsol satırının taşıyamayacağı hiçbir rengi almaz.
- **Faz:** `animation-delay: 0s, -0.55s` → renk değişimi tam kırpmanın dibine (opacity 0.1) düşer; geçiş görünmez, imleç her yanışta yeni renkle gelir. Geçişli (ease) renk kayması İSTENMEDİ — kesikli ritim tercih edildi.
- **Reduced motion:** `.bo-cursor` animasyonları yalnız `prefers-reduced-motion: no-preference` altında → aksi hâlde sabit, `currentColor` (satır rengi).
- **WPF:** tek `Rectangle`; `DoubleAnimation` (Opacity, 1.1s, AutoReverse) + `ColorAnimationUsingKeyFrames` (`DiscreteColorKeyFrame` ×6, 6.6s, BeginTime −0.55s), ikisi de `RepeatBehavior.Forever`.
- Dosyalar: `app/BuildApp.jsx` (`.bo-cursor` kuralı, `@keyframes bo-hop`, `ABOUT_VERSION 1.12.1`), `app/release-notes.js`.

## v1.12.0 — 2026-09-08

**Cycle işareti grafta + başlangıç modu kesiksiz.** Kullanıcıyla mutabık deneme dosyası: `Cycle & Fresh Options.dc.html` (son hâl: 2a Build · 2b Resolve · 2c yakın plan). Ana koreografinin süre, sıra ve renkleri DEĞİŞMEDİ.

**1 — Cycle üyesinde amber küp (§2.3, §5).** 1.11.0 cycle'ı yalnız satırdaki uyarı üçgeniyle anlatıyordu; grafta hiçbir iz yoktu, bitmiş bir koşu incelenirken "bu neden derlenmedi" okunmuyordu. Yeni kural: **bu işlemde derlenmeyen cycle üyesinde node border'ı gri, içindeki küp AMBER** — satırdaki amber üçgenin grafik vekili. Tek istisna olarak border ≠ küp; başka hiçbir durumda ayrışmazlar.
- `vstate()`: `discovered` + cycle + `!fresh` → `cyc` (border `--border-strong`, zemin `--surface-raised`, küp `--amber-text`); `skipped` + cycle → `cycskip` (skipped border/zemin, küp amber). `GTONE`/`GCORE` bu iki anahtarı taşır.
- **Zamanlama:** işlemin **nötr anında** yanar — `_neutralize` fresh'i düşürür, küp 380ms `ease-standard` ile griden amber'a geçer (şeritlerin düze dönmesiyle aynı anda). Dalgaya KATILMAZ (dalga = derlenecekler). Vedada grilerle söner, koşuda soluk kalır, **finalde gri node içinde amber küp olarak durur** — sonraki Sync (fresh) ya da kendisini derleyen bir işleme dek.
- **Resolve cycles:** üyeler kapsamdır → nötr anda küp amber, dalgada tam amber, derlenince **yalnız sonuç renginde** (amber küp yok). Motor: `startResolve` artık `_mark(members)` — bayat bağımlılık kapanışı dalgada YANMAZ, `queued` da yapılmaz; gri kalır, sırası gelince `building` → sonuç. Koşu bitişinde `_runEndFinale()` çağrılır — Resolve de neon bitişle kapanır (eksikti).
- Satırdan tek üye build: üye derlenir → tam amber → sonuç; diğer üyeler `cyc`. Clean/VS Clean: herkes fresh → amber küp yok (başlangıç renksiz kuralı korunur).
- Liste tarafında DEĞİŞİKLİK YOK: şerit/nokta gri, üçgen zaten amber.

**2 — Başlangıç modu satırda kesiksiz (§1.1, §2.4, §5).** Kesikli şerit (`repeating-linear-gradient`) 1px hairline'da piksel ızgarasına oturmuyor, 8px kesikli CSS border tırtıklı çiziliyordu. Seçilen çözüm "orta yol — 4 yaylı halka":
- **Şerit:** düz `--status-skipped-border`, **opaklık 0.5**; işlem başlayınca 380ms'de 1. Genişlik 2px (seçili 3px) ve 1px dikey boşluk aynen.
- **Nokta:** tek 8×8 SVG, iki daire: `r=3.2` **4 eşit yaylı** halka (stroke 1.1, `stroke-dasharray 2.93 2.1`, opaklık 0.85) ↔ `r=4` dolu daire (statü rengi). Fresh'te halka görünür, işlem başlayınca **çapraz-sönüm 380ms** — eleman ve boyut sabit, hiza kaymaz, titreme yok. Dolu dairenin `fill` geçişi dalgada satırın `markOrder` gecikmesini paylaşır (eski `background` geçişiyle aynı).
- **Statü glyph'i (DS `StatusGlyph` discovered = kesikli daire) DEĞİŞMEDİ** — SVG stroke olduğu için tırtık yapmıyor ve building spinner onun dönen hâli.
- **Node:** kesikli border **KORUNDU** (`GTONE.fresh.dash`). İlk uygulamada düz+soluk (0.55) denendi; kullanıcı Sync sonrası grafta kesikli çizimi istedi, geri alındı — tırtık şikâyeti yalnız CSS ile çizilen şerit ve noktaya aitti.
- WPF: tek statü noktası kontrolü — `Path` (4 yaylı) + `Ellipse` üst üste, `Opacity` animasyonları; şerit `Opacity` 0.5→1.

**3 — Dosyalar.** `app/BuildApp.jsx` (GTONE/GCORE/vstate, graf node opaklığı, Row şerit+nokta, `ABOUT_VERSION 1.12.0`), `app/build-data.js` (startResolve kapsamı, Resolve finali), `app/release-notes.js` (1.12.0 maddeleri). Deneme dosyası: `Cycle & Fresh Options.dc.html`.

## v1.11.0 — 2026-08-27

**Renk sadeleştirme: tek statü kanalı + her işlem için tek koreografi.** İlke: **renk yalnız son işlemin hikâyesini anlatır.** Kullanıcıyla mutabık plan dosyası: `Simplification Plan.dc.html`.

**1 — Tek statü kanalı (§2.4, §2.3, §5).** Ortogonal *will-build* kanalı kaldırıldı: satırdaki 8px will-build noktası ve graf node'unun plan renkli çekirdeği artık ayrı bilgi taşımıyor. Bunun yerine satırın sol şeridi, adın solundaki nokta, statü glyph'i, node border'ı ve node içindeki küp **aynı statü rengini** taşır (`GTONE` / `GCORE` / `vstate()`). Turuncu (`--status-cycle*`) UI'dan tamamen çıktı — nokta, çekirdek, çip, filtre, tooltip ve Resolve ikonu nötr; konsoldaki `warn` satırları amber. Döngü üyeliği hâlâ modelde kalıcı bir küme (`eng.cycle`) ama görsel olarak yalnız uyarı üçgeniyle ifade edilir.

**2 — Görsel durum = `vstate()`.** Beş görsel durum: `fresh` (başlangıç modu — kesikli) · `discovered` (nötr gri) · `marked` (bu işlemin kapsamı — amber) · `queued`/`building` (amber) · `succeeded`/`failed`/`skipped` (sonuç). WPF karşılığı: satır ve node aynı enum'dan beslenen tek bir görsel durum makinesi.

**3 — Başlangıç modu (§3.1).** Sync ve uygulama açılışı **hiçbir şeyi renklendirmez**: hangi işlemin geleceği belli olmadığı için plan gösterilmez. Satırda kesikli sol şerit (`repeating-linear-gradient`, 3px dolu / 4px boşluk) + kesikli daire glyph + kesikli nokta; grafta kesikli node border. Alan: `st.fresh`. Değişen projelerde çift SHA (`a3f81c2 → b7e91d4`) korunur — renk olmadan da neyin bayat olduğu okunur. Uyarı üçgeni bu modda da görünür. Uygulama kapatılıp açılınca hep bu temiz moda döner.

**4 — Açılış koreografisi (her işlem aynı — §3.1, §3.2).** Motor tek boruya indi: `_beginOp` → `_neutralize` → `_mark` → koşu.
- `_beginOp(label, target)`: **konsol + event stream temizlenir**, kalıcı işlem etiketi yazılır (§2.2).
- `_neutralize()`: önceki koşunun tüm izleri silinir — herkes düz nötr gri (`fresh` düşer, statü/süre/log sıfırlanır).
- `_mark(scope)` = **"Dalga + örtüşen veda"**, adımlar `eng.markStep`, süresi kapsamla değişir (`2520 + W`, `W = markStagger*(n-1)+380`):
  1. **nötr an** 440ms — kapsam da düz gri (amber henüz yok),
  2. **dalga** — kapsam **RANDOM sırayla** amber'a yanar (`markOrder`, mulberry32 ile karıştırılır; derleme sırası DEĞİL — kullanıcı kararı). Tempo `markStagger` = 110ms/node, toplam ≤1.1s'e sıkışır (36 projede de kısa kalır). CSS `transition-delay` ile; node border + zemin + küp aynı gecikmeyi paylaşır (küpün ayrı geçişi vardı, düzeltildi),
  3. **sarı-gri an** 300ms — plan bir an ekranda durur,
  4. **örtüşen veda** — griler 1120ms sönüşe başlar (0.13), 560ms sonra sarılar katılır (440ms, 0.55) → **ikisi aynı anda biter**, ease-in-out. (Sarı süresi algı için gri süresinden kısa: gri büyük opaklık düşüşü yaptığı için yolun ortasında "gitti" okunuyor.)
  5. **nefes** 420 + 240ms → koşu başlar.
- Satırlar dalganın rengini node'larla senkron taşır; **opaklık koreografisi satırlarda YOK (v1.13.2)** — `rowOp` sabit 1, sönme/yerleşme yalnız grafta. `noReveal` ref'i reveal animasyonunun fill kilidini bırakır. Ad rengi de aynı `markOrder × markStagger` gecikmesiyle geçer, böylece ad + şerit + nokta tek harekette yanar.
- Koreografi **gerçek zamanda** (setTimeout) koşar — sim saati yalnız koşu/sync'te ilerlediği ve arka plan sekmede rAF durduğu için sim saatine bağlanınca asılıyordu. `stop()` ve `reset()` bekleyen zamanlayıcıları temizler.
- Motor girişleri: `beginBuild` · `beginRebuild` · `beginProject(name, 'build'|'rebuild')` · `beginProjectClean` · `startTask('cleansln'|'clean'|'optimize')` · `startResolve`. `startRunFromState` ve `startProjectRun` kaldırıldı.
- Faz `marking` eklendi: `busy()` true, F5/Stop bu fazda da çalışır (koşu hiç başlamaz, konsola `Cancelled — build not started`).
- Denenip **istenmeyenler**: göz kırpma ×2, tek nefes (2e), sıralı (derleme sırası) dalga. Varyant dosyası: `Marking Animation Options.dc.html` (Tur 1-4).

**5 — Bitiş koreografisi ("Neon tutuşma" — §3.2).** Koşu bitince (`_runEndFinale`, adımlar `eng.endStep`, gerçek zaman):
1. `hold` 900ms — sarılar gitti, **hepsi soluk** (0.16) bekler,
2. `neon` — **yalnız bu koşuda derlenenler** (succeeded ∪ failed) **RANDOM sırayla** floresan lamba gibi düzensiz titreyerek tutuşur (`bo-neon` 1150ms; `endOrder` karıştırılmış, `endStagger` ≤150ms/node, zincir ≤1.5s),
3. `bwait` 700ms nefes,
4. `grey` — kalan **tüm griler** (atlanan + dokunulmamış) **hep birlikte** 980ms ease-in-out belirginleşir,
5. `null` — final görünüm.
Koreografi **yalnız graf node'larında** yaşar; proje listesi bitişte sabit kalır (kullanıcı kararı). Hızlı kontrol koşusunda ve derlenen proje yoksa koreografi çalışmaz. Yeni bir işlem (`_beginOp`) koreografiyi anında keser. Denenip istenmeyenler: grilerin tek tek kırpması, kıvılcım/pop, çift/üç kırpma — `Marking Animation Options.dc.html` Tur 5-6.

**6 — Satır aksiyonları (§2.4, §3.8).** Hover'da **▶ play = Build** (tek tık) + **⋯ menü**: Build · Rebuild · Clean. Satıra **sağ tık** aynı menüyü açar (VS Solution Explorer alışkanlığı → WPF ContextMenu). Menü liste scroll kabının içinde absolute konumlanır (`data-list-scroll` / `data-row-wrap`) ve görünür alana clamp'lenir.
- **Kapsam YALNIZ o proje** — bağımlılıklar derlenmez. Bayat kalan bağımlılık uyarı üçgeni doğurur ve logda `last known output referenced` satırı görünür. "Build with dependencies" istenmedi.
- Satırdan tetiklemek **satıra tıklamak değildir**: seçim ve filtre sıfırlanır, graf odağı açılmaz, konsol ana loga döner — süreç Build/Rebuild/Resolve ile birebir aynı.
- Satırdan Clean: yalnız o projenin çıktıları silinir, satır başlangıç moduna döner; diğer projeler de varsayılan görünüme döner.

**7 — Build split menüsüne Clean (§2.7).** Menü: Build (F5) · Rebuild (Ctrl+F5) · **Clean**. Bu Clean = Visual Studio'nun *Clean Solution*'ı: yalnız `msbuild /t:Clean` (7 solution), cache'lere dokunmaz; bitişte herkes başlangıç moduna döner ve sonraki Build tam derleme olur. Bakım kutusundaki **derin Clean** (bin/obj + artifacts + NuGet cache) yerinde kalır — ikisi birbirinin yerine geçmez. İkon ailesi tek grid/stroke'ta: **play · rotate-cw · brush**; satır menüsü aynı seti kullanır (eski Rebuild ikonu değişti).

**8 — Tek uyarı üçgeni (§2.4, §5).** Statüden bağımsız, sabit 14px slot, **her zaman amber**. Tooltip **tek satır**: `In a dependency cycle` veya `Dependency issue: Sales.Core +2`. Döngü yolu, üye listesi ve gerekçe **proje logunda** (`skipped — in a dependency cycle`, `skipped — up to date`, `built with last good output of X`). Grafta üçgen yok. Renkle neden ayrımı (turuncu/amber) kaldırıldı.

**9 — Sticky şerit (§2.2).** Solda **kalıcı işlem pill'i**: `eng.opLabel()` → `SYNC` · `BUILD` · `REBUILD` · `CLEAN` · `RESOLVE` · `OPTIMIZE` (v1.13.2: satırdan tetiklenen işlemde hedef adı pill'e yazılmaz — tek proje derlemesi şeritte tam koşuyla birebir aynı görünür; hedef konsolda ve grafta belli). Mono, caps, 19px, 1px çerçeve; **koşarken amber** (amber-soft zemin), bitince nötrleşir ama **bir sonraki işleme kadar kalır** — "ne yapmıştım?" sorusu tek bakışta biter. Spinner / sonuç glyph'i **pill'in içinde**, metnin hemen sağında (6px). Kalkanlar: hata kümesindeki `N failed · N dependency-affected` sayaçları ve turuncu cycle çipi. Kalan: ilk 3 hatalı çip + `+N more` (tık → failed filtresi) + koşarken building çipleri.

**10 — Çoklu filtre (§2.7).** `filter` artık bir Set; çipler bağımsız açılıp kapanır ve seçili küme **VEYA** ile birleşir (✓ + ✗ = "bu koşuda derlenenler"), arama ile VE. Aktif çip **kendi statü renginde** yanar. Set: Σ (temizler) · building · ✓ · ✗ · — · **⚠** (`warn` = cycle ∪ dependency issue, tek birleşik uyarı filtresi). `dep` ve `cycle` filtreleri kalktı. Filtre grafı da sürer (eşleşmeyen node 0.1'e söner). Panel başlığındaki aktif filtre çipi seçili kümeyi ` + ` ile listeler.

**11 — Title bar ve workspace adı (§2.1, §2.7).** Title bar solunda yalnız marka kalır: ürün logosu + `Build Orchestrator` + ayraç + firma logosu. `OSYS · main · wt` metni kaldırıldı; workspace adı (`OSYS`, tooltip = repo kökü) alt barda **branch çipinin soluna** mono etiket olarak geçti.

**12 — Ardışık Build.** Bir önceki koşu her şeyi güncellediyse stale set boş kalıyordu ve tekrar Build hiç koreografi göstermeden bitiyordu. Artık simülasyon "geliştirici çalışmaya devam etti" varsayar: `_reseedChanges()` yeni bir HEAD üretir ve **çekirdek katman dışından** (bağımlılığı olan projelerden) 2-4 proje bayatlar; bağımlı kapanışı hesaplanarak kapsam workspace'in **%45'inin altında** tutulur — böylece gri kalan hep olur ve Build, Rebuild'den ayrışır. Konsol satırı kapanışla uyumlu: `HEAD 3f8ee8c — 4 projects changed · 10 affected since last build`. Gerçekten hiçbir şeyin değişmediği durum yalnız Sync sonrası mümkündür; orada hızlı kontrol + `Everything up to date` görünür.

**13 — Satır tooltip'leri (§2.4).** Proje listesinde tooltip **yalnız uyarı üçgeninde** kaldı (DS Tooltip, `side="left"`, gecikmesiz). Statü glyph'i ve hover ikon butonları (play/Stop · ⋯ · Explorer · VS) tooltip taşımaz — anlamları belli; erişilebilirlik için native `title` + `aria-label` durur. 500ms gecikmeli, satır üstünde açılan varyant denendi ve **istenmedi**.

**Yapılmayanlar (tekrar önerme):** proje listesi kart yapısına geçmedi (36 projede yoğun liste doğru form) · "Build with dependencies" eklenmedi · bitiş koreografisi satırlara uygulanmadı · turuncu hiçbir yerde geri gelmedi.

**Dosyalar:** `app/build-data.js` (`MARK`/`_beginOp`/`_neutralize`/`_mark`/`_clearMarkTimers`, `_runEndFinale`/`_clearEndTimers`, `beginBuild`/`beginRebuild`/`beginProject`/`beginProjectClean`, `_cleanSlnPlan`, `_reseedChanges`, `st.fresh`, `opLabel`, `isMarked`, `stop()` marking dalı), `app/BuildApp.jsx` (`vstate`/`GTONE`/`GCORE`, `warnText`, `RowMenu`, `Row` şerit+nokta+glyph, `StickyStrip` işlem pill'i, çoklu `filter` Set'i, `doRowAction`/`doCleanSln`, `I.rebuild`/`I.brush`/`I.more`, `bo-neon` keyframe'i, title bar/alt bar), `app/release-notes.js`. `ABOUT_VERSION` 1.11.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.10.0 — 2026-08-13

**Satırdan tek proje derleme + ayarların dışa/içe aktarılması.**

- **Proje satırında "Build this project" (§2.4, §3.8):** hover ikon grubunun başına play ikonu eklendi (klasör ve VS ikonlarının solunda). Tıkla → **kapsamlı (scoped) koşu**: hedef proje + yalnız onun bayat/hatalı bağımlılıkları derlenir; kapsam dışındaki 30+ projeye hiç dokunulmaz (atlama dalgası ve bitiş kontrolü `scope` kümesiyle sınırlı). Proje otomatik seçilir → konsol o projenin loguna geçer.
- **Koşarken satır Stop olur:** kapsam hedefi olan satırda play ikonu kırmızı **Stop**'a döner ve hover gerekmeden görünür kalır; diğer satırların build ikonu disabled (tooltip: `Build in progress — wait or stop it first`). Play/Stop ikonları satırdaki diğer ikonlardan bir tık büyük (14px). **Koşul yok:** atlanmış, hatalı, döngü üyesi — her proje satırından derlenebilir; renk/animasyon/statü kuralları tam koşuyla birebir aynı kalır. Alt bardaki ana buton da her koşuda (Build, Rebuild, tek proje, **Resolve cycles**) `Stop`'a döner; F5 de durdurur.
- **Seçim + liste sabitliği:** play'e basınca hedef proje seçilir (konsol onun loguna, graf o node'a odaklanır). Filtre sıfırlama ve liste remount'u kaldırıldı — liste yerinden oynamadığı için imleç altındaki satır kaymaz, yanlış proje derlenmez.
- **Stop davranışı:** tek proje koşusu durdurulunca kapsam `discovered`'a döner ve faz `idle` olur (yarım queued kuyruk kalmaz) — Resolve iptaliyle aynı mantık. Konsol: `Build stopped — <proje> not rebuilt`.
- **Settings: Export / Import / Clear (§2.9):** footer'da `Load sample layers`'ın sağında ayraç + üç ikon buton (download / upload / çöp kutusu). Export → `build-orchestrator-settings.json` (app, version, `repositoryRoot`, `layers[{name, pattern}]`). Import → dosya seçici; değerler **forma** yüklenir. Clear → iki aşamalı (ilk tık kırmızı ikon + `Click again to clear root and all layers`, 2.4s sonra kendini iptal eder; ikinci tık formu boşaltır). Üçünde de `Save`'e kadar hiçbir şey uygulanmaz; onay dialogu yok. Geri bildirim footer'da 2.4s yazı (yeşil/kırmızı).
- **First run'da Import kısayolu (§2.4):** kurulum listesinin altında `Open settings` (primary) yanında `Import settings…` (secondary) — Settings'i açıp dosya seçiciyi hemen tetikler; altında 11px not `Import fills the form from a settings file — nothing is applied until you save.` Hazır ayar dosyası olan developer tek adımda başlar.
- **Motor:** kapsamlı koşu kendi sırasını kullanır (`_processRun` içinde ayrı dal): yalnız kapsam üyeleri gezilir, kapsam dışı satır sırayı bloklamaz (`continue`), kapsam dışı bağımlılık çözülmüş sayılır. Derlemeye alma tek giriş noktasına toplandı (`_startBuild`) — statü/depIssue/log/aktif satır kuralları iki yolda birebir aynı.
- Dosyalar: `app/build-data.js` (`startProjectRun`, `scope`/`scopeName`, `stop()` kapsam dalı, `_processRun`'da kapsamlı scheduler dalı + paylaşılan `_startBuild`, tek proje bitiş özeti), `app/BuildApp.jsx` (`Row` build/stop ikonu, `doBuildProject`, Settings export/import/clear, first-run `Import settings…`, `download`/`upload`/`trash`/`playRow`/`stopRow` ikonları), `app/release-notes.js`. `ABOUT_VERSION` 1.10.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.9.0 — 2026-08-13

**Sürüm notları artık uygulamanın içinde: About → What's new (§2.10).** Ayrı bir pencere ya da açılış pop-up'ı açılmadı — sürüm numarasının zaten göründüğü About penceresine 4. sekme eklendi.

- **Liste:** en yeni sürüm üstte; mono sürüm no + `CURRENT` amber etiketi + sağa yaslı tarih; maddeler sabit ikon slotu + 13px metin.
- **Kategori = blok başlığı:** 6px renkli kare + caps etiket — **Added** (amber) · **Changed** (nötr) · **Fixed** (yeşil) · **Performance** (turuncu) · **Removed** (kırmızı); maddeler başlığın altında işaretsiz durur. Satır başına ikon (Lucide) ve diff sigili (`+ ~ ✓ » −`) varyantları üretildi, kullanıcı blok başlığını seçti — satırlar üzerinde tek bir işaret bile kalmadı, en sakin okuma.
- **Okunurluk:** son 3 sürüm açık, gerisi `Earlier versions (N)` altında katlı — tüm geçmiş erişilir ama sekme bir ekran boyunda açılıyor. Veri statik ve küçük (`app/release-notes.js`), koda gömülü değil.
- **Görülmemiş sürüm işareti:** ⓘ üzerinde 5px amber nokta + değişen tooltip; About doğrudan What's new'da açılır, sekme görülünce işaret söner (localStorage `delta-bo-seen-version-v1`). Gerçek uygulamada "açılışta yenilikleri göster" akışı da bu sekmeye bağlanır.
- Dosyalar: yeni `app/release-notes.js`; `app/BuildApp.jsx` (`NOTE_KINDS`, `WhatsNew`, About segment/initialTab/onNotesSeen, ⓘ noktası); `Build Orchestrator.dc.html` (helmet'e release-notes.js). `ABOUT_VERSION` 1.9.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.8.0 — 2026-08-13

**First run artık dosya seçtirmiyor, ayarlara yönlendiriyor.** Başlamak için gereken ayar sayısı arttığından (repository root + katman tanımları) boş durum tek bir klasör seçiciye değil Settings'e açılıyor; repository root da Settings'in bir parçası oldu.

- **Liste panelinde kurulum daveti (§2.4):** `Configure the workspace` başlığı + açıklama + **kurulum listesi** (Repository root → `Not set`, Layers → `N defined` / `Optional`) + primary `Open settings` (dişli). Eski `Pick a repository to get started` + `Choose Folder` butonu kaldırıldı.
- **Settings'e Workspace bölümü (§2.9):** caps `WORKSPACE` + açıklama + mono root inputu (placeholder `D:\src\osys`) + secondary `Browse…`; altında 1px ayraç, sonra mevcut Layers bölümü. Root boşken Save disabled.
- **First run'da kaydet = kurulum:** primary buton etiketi `Save and sync`, kaydedince workspace açılır, konsola `workspace: <root> — 36 projects discovered` düşer ve Sync kendiliğinden başlar (eski `Choose Folder` davranışının aynısı, artık ayarların içinden).
- Root'un sonradan değişmesi: konsola dim not `Repository root → <root> — Sync required` (durum sıfırlanmaz, kullanıcı Sync'ler).
- **Metin uyumu:** şerit `Not ready — no repository selected` → `Not configured — repository root not set`; title bar `no repository` → `not configured`; dişli tooltip'i `Settings — repository root and layer definitions`; Scene 5 açıklaması güncellendi.
- Dosyalar: `app/BuildApp.jsx` (`ListPanel` boş durumu, `SettingsDialog` workspace bölümü, `repoRoot` state'i, `openWorkspace`/`saveSettings`), `Build Orchestrator.dc.html` (Scene 5 metni). `ABOUT_VERSION` 1.8.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.7.0 — 2026-08-13

**Cycle akışı ve statü kanalları yeniden kurgulandı (üç-kanal modeli); Resolve cycles eklendi; Build semantiği sadeleşti.** Kullanıcı akışı: Sync → (istenirse) Build (döngü atlanır) → Resolve cycles → Build. Sıra dayatması yok; Scene 8 zinciri interaktif oynatır.

### Statü kanalları (§2.3, §2.4, §5)

Önceki sürümde cycle bilgisi bazen sol şeridi, bazen statü ikonunu işgal ediyordu; aynı renk iki anlama gelebiliyordu. Artık her kanal tek soruya cevap verir ve başka kanalın yerine geçmez:

| Kanal | Soru | Nerede | Değerler |
|---|---|---|---|
| A — Sonuç | Bu koşuda ne oldu? | kart sol şeridi · statü glyph'i · graf node border'ı | discovered · queued · building · succeeded · failed · skipped |
| B — Plan | Sıradaki Build buna dokunacak mı? | kart noktası · graf node çekirdeği | amber = derlenecek · gri = güncel |
| C — Yapısal | Kodda döngü var mı? | kart noktası · graf çekirdeği · uyarı üçgeni | turuncu = döngü üyesi (kalıcı) |

> **v1.11.0 NOTU — bu üç kanallı model KALDIRILDI.** Yalnız A kanalı kaldı (tek statü rengi: şerit + nokta + glyph + node border + küp); B kanalı çift SHA metnine, C kanalı amber uyarı üçgenine indi. Aşağıdaki v1.7.0 anlatımı tarihsel kayıttır — güncel kural §9 v1.11.0/1-3'tedir.

- **C kanalı B'yi ezer:** döngü üyesinin noktası/çekirdeği sonuç ne olursa olsun turuncu kalır (yeşil bitse de) — kod hâlâ döngülüdür. Turuncu ancak kaynak düzelip Sync bunu görmediğinde kalkar.
- **`cycle` statüsü kaldırıldı (§4):** üyelik artık engine'de kalıcı küme (`eng.cycle` / `_isCycle`); statü normal akar. Sayaç chip'i üyelik sayısını gösterir, Resolve sonrası da durur.
- **Renk ayrımı:** cycle dolguları `-text` (#f0853f) yerine **çekirdek ton `--status-cycle` #df6f2b** — 8px'te amber #eda10f ile karışmıyor. Turuncu ile kırmızı aynı slotta hiç buluşmaz: kırmızı yalnız sonuç kanallarında, turuncu yalnız yapısal kanallarda.

### Kart satırı (§2.4)

- **Sol şerit her satırda var** — workspace açıldığı andan itibaren gri (`--status-skipped-border`); Sync şeridi getirmez, zaten oradadır (Sync yalnız plan kanalını tazeler). `discovered` ve `skipped` **aynı gri** (iki ayrı gri "bazıları koyu bazıları açık" karmaşası yaratıyordu). Zincir: sönük gri → açık gri (queued) → amber (building) → yeşil/kırmızı. 2px (seçilide 3px), **1px dikey iç boşluk** — boşluk satır ayracı kadar: bitişik satırlarda tek çizgiye kaynamaz, arası da açılmaz.
- **Nokta** plan kanalında kalır; derlenince amber → gri söner, sonuç rengine DÖNMEZ (sonuç kartta zaten şerit + glyph ile iki kez var; üçüncüsü kırmızı satırda gürültü). Tooltip: `Will build — source changed since last build` / `Up to date` / döngü açıklaması + yolu.
- **İsim tek kural:** işi olan satır (dirty · queued · building · failed) primary beyaz, güncel/atlanacak secondary gri. **Kalınlık hep 500** — bold yok, satır ritmini bozuyor.
- **SHA her satırda:** dirty `a3f81c2 → b7e91d4` (secondary), clean tek SHA (faint); succeeded'da `curSha` hedefe eşitlenir. "Yalnız derleneceklerde göster" denendi, satırlar arası layout sıçraması yarattığı için bırakıldı.
- **Glyph daima gerçek statü** — uyarı onun yerine geçmez (cycle satırında loading yerine ünlem çıkması kalktı). Sağında **sabit 14px uyarı slotu**, tek üçgen: **cycle üyesinde turuncu** (dep'i de olsa turuncu kazanır — yapısal neden kalıcıdır), **yalnız dep-issue'da amber** (geçici, sonraki koşu temizler); satır **building iken gizli**. Tooltip nedenleri alt alta listeler. Kırmızı dep üçgeni ve hollow cycle noktası kalktı.
- **Atlanan döngü üyesi satırı:** gri şerit + `—` glyph + turuncu nokta + turuncu üçgen + beyaz isim (işi bitmedi, Resolve bekliyor) + `cur → hedef` SHA.

### Graf (§2.3)

- **Çekirdek:** cycle → her zaman turuncu · bu koşuda **bitmişse sonuç rengi** (yeşil/kırmızı/gri) · aksi hâlde plan (amber/gri). Böylece koşu boyunca "ne olacak", kapanışta "ne oldu" okunur; graf sonunda klasik sonuç haritasına döner ("yeşil çerçeve içinde gri küp" hâli kalktı).
- Node border'ı değişmedi: kesikli gri (hiç derlenmedi) → amber + beads → sonuç rengi; ilk sonuçtan sonra kesikli border bir daha kullanılmaz.
- **Node üstü uyarı üçgeni YOK** — grafta döngüyü yalnız turuncu çekirdek anlatır.
- Kart noktası ile graf çekirdeğinin ayrışması bilinçli: **dolgu, iş bitene kadar planı söyler; bitince grafta sonuca döner, kartta griye düşer.**

### Resolve cycles (§2.7-2, §3.7)

- **Yerleşim:** bakım kutusunun üçüncü ikonu (Lucide **unlink**, 28×22, `--status-cycle`); etiket yok (bar 1240px'te taşımıyor), anlam tooltip'te; döngü yokken **disabled** + `no dependency cycles detected`.
- **Kapsam:** döngü üyeleri + bayat (dirty/failed) bağımlılıklarının kapanışı; **ardışık** koşar (paralellik 1) — çözüm sıraya duyarlı.
- **Pass 1/2:** önce bağımlılıklar, sonra üyeler son bilinen (bayat) referanslarla derlenir (üye logunda `circular reference … building against last known outputs` warn'u); üye bu geçişte `will=dirty` kalır. **Pass 2/2:** yalnız üyeler taze referanslarla yeniden derlenir (~0.45×) → yakınsar, `will=clean`, `curSha` hedefe eşitlenir.
- **Geri bildirim:** şeritte amber `Resolving cycles · pass 1/2 · 2/7 · 4s` + progress · konsolda komut satırı + döngü yolu + pass ayraçları + `— stale references` ekleri · stream'de `task`/`taskdone` + `built — pass 1/2` + canlı daktilo.
- **Bitiş:** faz `idle`; şeritte kalıcı yeşil `Cycles resolved — 3 projects converged in 2 passes · outputs now current`; konsola dim not (kaynaklardaki döngü duruyor). Sonuç **normal succeeded**, sayaçlara girer; ayrı "resolved" rengi yok.
- **Tıklandığında Build ile aynı sıfırlama:** seçim düşer, filtre temizlenir, graf varsayılan görünüme döner (zoom/pan sıfır). **Ayrı loading overlay'i, onay dialogu, toast YOK.**
- **Kilitleme/iptal:** koşu `running` — Sync/Build/Clean/Optimize/branch/worktree/Debug-Release disabled, F5 no-op; **Stop çalışır** (kapsam discovered'a döner, üyeler çözülmemiş kalır).

### Standart Build ve döngü (§3.1, §3.2, §3.4)

- **Build (F5) durumdan koşar** (`startRunFromState`): stale set = değişen + hatalı + hiç derlenmemiş + hatalıların bağımlıları; boot/Rebuild dışında sync/reset yok, konsol/stream sıfırlanmaz. Stale set boşsa hızlı `Everything up to date` kontrolü.
- **Continue ve Retry failed kaldırıldı** — Stop sonrası Build kaldığı yerden sürdürür (elapsed sıfırlanır); hata sonrası Build hatalıları + bağımlılarını alır (`baseFails` kapanır, fix varsayılır). Menü iki madde: Build (F5) · Rebuild (Ctrl+F5). Koşarken **F5 = Stop**.
- **Döngü üyeleri standart plana girmez:** `skipped — in a dependency cycle, not rebuilt`; bitiş özetine `· N cycle projects skipped`. Derlenmemiş üyeye bağımlı projeler depIssue alır (`last known output referenced` warn'u) → dependency-affected sayacına girer.
- **Clean:** döngü üyelerinin çıktıları da silinir (dirty olurlar) ama standart plana yine girmezler.

### Filtreleme

Alt bardaki statü chip'ine basınca **seçim düşer, graf varsayılan görünüme döner**, listede yalnız eşleşen satırlar kalır ve **grafta yalnız eşleşen node'lar canlı** kalır (diğerleri 0.1'e söner). Liste ve graf aynı kuralı paylaşır (`filterMatch`): `building` chip'i queued'ı da kapsar, `dep` depIssue taşıyanları, `cycle` üyeleri. Başlıktaki aramayla VE ile birleşir; aynı chip'e tekrar basmak filtreyi kaldırır.

### Scene 8

Sync sonrası **idle** açılır — Build/Resolve sırası kullanıcıda. Sahne **tek kasıtlı hata** taşır (`oneFail` → OSYS.Sales.Core; engine'de `failOnly`): kırmızı sonuç, kırmızı çekirdek ve bağımlılardaki amber dep-üçgeni de aynı sahnede görülür. Yoğun 5-hatalı senaryo Failure sahnesinde kalır; hata sonrası Build fix varsayıp temiz geçer.

### Motor ve doğruluk düzeltmeleri

- **`allClean` kirlenmesi giderildi:** koşu-başı "yapacak iş yok" durumu ayrı `checkOnly` bayrağında (`_fastCheck()` ikisini birlikte okur); her yeni Sync temizler. Önceden boş bir Build workspace'i kalıcı "her şey temiz" işaretliyor, sonraki Sync'ler bayat işi bulamıyordu.
- **Sim saati rAF + 250ms yedek interval:** telafi adımı 20s yerine **2s'ye kırpılır**, `visibilitychange`'de döngü kaldığı yerden sürer. Not: tarayıcı gizli sekmede zamanlayıcıları tümüyle dondurabilir; pencere öne gelince akış devam eder.
- **İlerleme çubuğu tutarlılığı:** biten görev özeti şeritte dururken çubuk o görevin sonucunu izler (`taskResult` → succeeded). Önceden yeşil "Cycles resolved" satırının altında önceki Build'den kalma kırmızı bar kalıyordu.
- **Sayı/çoğul uyumu:** `1 error` / `3 errors`, `1 warning` / `21 warnings`, `1 cycle project skipped` / `3 cycle projects skipped`.

### Kaldırılanlar

| Ne | Neden |
|---|---|
| `cycle` statüsü | Üyelik statü değil, kalıcı yapısal özellik |
| Continue butonu / `continueRun` | Build zaten kaldığı yerden sürdürüyor |
| Retry failed / `startRetry` | Build zaten hatalıları + bağımlılarını kapsıyor |
| Node üstü uyarı üçgeni | Grafta döngüyü turuncu çekirdek anlatıyor |
| Kırmızı dependency üçgeni | Kırmızı sonuç kanalına ait; dep uyarısı amber |
| Hollow cycle noktası | Yerini kalıcı turuncu dolu nokta aldı |
| Discovered'da şeffaf sol şerit | Şerit artık her satırda var |

### Değişen dosyalar

- `app/build-data.js` — kalıcı cycle kümesi, `startResolve`/`_processResolve`/`resolveRun`, `startRunFromState`, `checkOnly`/`_fastCheck`, `failOnly`, cycle skip + özet metinleri, çoğul yardımcısı.
- `app/BuildApp.jsx` — kanal renkleri (nokta/çekirdek/şerit/üçgen), `WarnTip`, isim ve SHA kuralları, `filterMatch` (liste + graf ortak), Resolve butonu ve akışı, Build menüsü, sim saati, `pStatus`.
- `Build Orchestrator.dc.html` — Scene 8 tanımı ve açıklaması.
- `ABOUT_VERSION` 1.7.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.6.0 — 2026-08-09

**Konsol sadeleşti (§2.5).** Gerçek MSBuild çıktısıyla yan yana konunca konsol fazla "sahnelenmiş" duruyordu: her satırda duvar saati vardı, en yeni satır daktiloyla yazılıyordu ve satır başında amber `▸` işaretçisi duruyordu. Gerçek koşuda saniyede yüzlerce satır akıyor — hiçbiri bilgi taşımıyordu.

- **Saat sütunu kaldırıldı.** Konsol satırı artık yalnız metin. Zaman bilgisi tek yerde: event stream (düşük hacimli özet) + sticky şeritteki geçen süre.
- **`▸` ikon kolonu kaldırıldı.** Tüm satırlar **imleçle aynı sol hizada** başlar; satır türü yalnız renkle ayrılır (cmd=`text-primary`, info=`text-secondary`, dim=`text-faint`, success/warn/error=ilgili `-text`). Bu yüzden konsolda DS `ConsoleLine` yerine aynı renk sözleşmesini taşıyan glyph'siz satır kullanılıyor (bilinçli sapma).
- **Daktilo kaldırıldı.** Canlı gelen satırlar anında basılır. Konsoldaki tek canlı öğe: en alttaki prompt satırının yanıp sönen imleci (idle/boot'ta `ready`).
- **Panel geçişinde kaskat KORUNDU (ve simetrik hâle getirildi).** Kaldırılan şey canlı akıştaki animasyondu, panel geçişindeki değil: proje logu açılırken ve `← Back` ile ana loglara dönerken açılış içeriği aşağı serilerek gelir. Hareket tek parça olduğu için satır sayısından bağımsız: 3 satırlık log ile 200 satırlık anlatı aynı ritimde açılır. Açılış **tek parça** gelir ("tilt in"): alt kenardan menteşeli — `perspective(900px) rotateX(7deg) translateY(14px)` + opacity 0 → düz ve tam opak, **340ms ease-out**. Gözün baktığı dip sabit kalır, uzak taraf yatarak oturur. Satır bazlı kaskat, pop-in ve solgundan-belirme denendi; bu daha temiz durduğu için bırakıldı.
- **Konsol metni Geist Mono 300'e (Light) indi** (§1.2). Boyut 12px ve satır aralığı 1.55 değişmedi; yoğun çıktı ince ağırlıkta daha rahat taranıyor. Diğer mono alanlar 400'de.
- **Gerçek uygulama için font notu (§1.2):** geliştirmedeki konsol şu an sistem monosuyla (Consolas vb.) çiziliyor; bağlayıcı olan prototiptir — konsol da Geist Mono, ağırlık 300, 12px/1.55, tabular. Air-gapped woff2 paketlemesi hâlâ açık iş.
- **Event stream değişmedi (§2.6):** daktilo ve saat orada duruyor. Stream özet kanalıdır (koşu başına ~40 satır); ritmi koşunun canlı olduğunu gösteren tek sinyal, saat de orada tekrar değil bilgi.
- `ABOUT_VERSION` 1.6.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.5.2 — 2026-08-09

**Action bar chip kuralı netleşti (§2.7-4).** Temel beş sayaç (`Σ` · building · `✓` · `✗` · `—`) her zaman durur; **`▲` dependency-affected chip'i artık `⚠` cycle chip'i gibi yalnız listede o kayıt varken görünür.** Gri/0 hâli kalktı — göründüğünde daima kırmızı ve dolu. Gerekçe: ikisi de rutin değil istisnai durum bildirir; barı sürekli boş chip'le doldurmak sinyali zayıflatıyordu.

## v1.5.1 — 2026-08-09

**Dependency-affected sayacı listeyle hizalandı (§2.7-4, §2.2).** `▲` chip'i yalnız *succeeded* satırları sayıyordu; oysa listedeki üçgen ve `dep` filtresi, kendi derlemesi de patlamış satırlardaki dep-hatasını da gösteriyor. Sonuç: listede kırmızı üçgenler dururken chip 0 ve gri kalabiliyordu, filtre ise dolu geliyordu.

- Sayaç artık `depIssue` taşıyan HER projeyi sayar (statüsü ne olursa olsun) → chip sayısı = filtre sonucu = listedeki üçgen adedi; sayı >0 olduğu an ikon da kırmızıya döner.
- Şerittteki `· N dependency-affected` ve bitiş özeti aynı kaynaktan beslendiği için onlar da düzeldi. Anlam değişmedi: "bağımlılığı patladı, son başarılı çıktıya karşı derlendi" — bu, projenin kendi derlemesi de başarısızsa aynı şekilde doğrudur.

## v1.5.0 — 2026-08-09

**Döngü (cycle) ve bağımlılık uyarıları anlaşılır hâle getirildi.** Geliştirme tarafından gelen not: statü sütunundaki turuncu üçgeni ve derlenmiş satırdaki kırmızı üçgeni kullanıcı okuyamıyordu.

- **Cycle artık tooltip'li (§2.4-5).** Statü glyph'i hover'da iki satır söyler: `In a dependency cycle — won't be built` + mono döngü yolu. Sebep: üçgen tek başına "bir şey ters" diyor, ne olduğunu söylemiyor.
- **Cycle ambient olarak da duyuruluyor (§2.2).** Sticky şeridin sağında turuncu `3 in a dependency cycle` chip'i — tıkla → listede `cycle` filtresi. Gerekçe: döngü bir koşu sonucu değil bir yapılandırma hatası; Sync biter bitmez "bu N proje hiç derlenmeyecek" demek ve kullanıcının hover etmeyi bilmesi beklenemez. Aksi hâlde `N to build` sayacı ile listedeki proje sayısı tutmuyor ve sebebi görünmüyordu.
- **Konsol (§2.5):** Sync sonunda warn satırı `3 projects in a dependency cycle — excluded from build` + dim `cycle: OSYS.Domain.Parts → OSYS.Parts.Inventory → OSYS.Parts.Api → OSYS.Domain.Parts`. `N up to date (will skip)` sayısı artık döngüdekileri saymıyor; stream'in Sync olayı da `16 to build, 17 up to date, 3 in a dependency cycle` diyor.
- **Action bar (§2.7-4):** sayaç chip'lerine turuncu `⚠ 3` cycle chip'i eklendi — yalnız döngü varken görünür (döngü nadir bir durum; her koşuda boş chip taşımanın anlamı yok).
- **Engine:** yeni `cycle` statüsü (§5) ve `cycleList()`. Döngüdekiler willBuild'den çıkarılır, `queued` olmaz, `skipped` de olmaz; will-build noktası hollow. Bağımlıları normal derlenir (imza akışı korunur). Clean görevi döngüdekileri dirty'ye çevirmez.
- **Yeni sahne 8 — "Cycle"** (`Parts.Api → Domain.Parts` geri kenarı 3 projeyi tek SCC'ye sokar); diğer sahneler döngüsüz kalır.
- **Dep-hata üçgeni değişmedi:** mevcut tooltip (`Failed dependency: X — last successful output referenced`) yeterli bulundu.
- **Karar:** cycle üçgeni **turuncu** kalır, kırmızı badge eklenmez — kırmızı "derlendi ve patladı" demektir, döngüdeki proje hiç denenmemiştir. ARCHITECTURE.md §14.3'teki "warning triangle + red badge" ifadesi bu yönde düzeltilmeli (kod doğru, doküman eski). İki üçgenin (cycle / dep-affected) geometrisi benzer bırakıldı: ayrı sütun + ayrı renk yeterli görüldü.
- `ABOUT_VERSION` 1.5.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.4.0 — 2026-08-09

**Yeni: bakım görevleri — Clean ve Optimize (§3.4).** Action bar'da Sync'in sağına, chip ağırlığında tek kutu içinde iki ikon buton eklendi (§2.7-2): **Clean** (eraser) ve **Optimize** (gauge).

- **Clean** = VS'in *Clean Solution*'ı + `bin`/`obj` silme: 7 solution için sırayla `msbuild /t:Clean`, ardından `artifacts/ · TestResults/ · .vs/` ve `obj/project.assets.json`. Solution bittikçe o solution'ın projeleri anında `discovered`+dirty'ye döner (liste ve graf canlı boşalır); bitişte `allDirty=true` → sonraki Build tam derleme.
- **Optimize** = NuGet restore (solution başına) + global paket cache prune + bağımlılık indeksi + derleyici sunucusu ısıtma. Derleme durumunu değiştirmez; bitişte önceki faza döner.
- **İzlenebilirlik:** her adım konsola bir satır + stream'e bir olay basar (yeni `task` / `taskdone` olay türleri; taskdone yeşil ✓ ve bir kez parlar). Stream'in altında build'inkiyle aynı dilde canlı daktilo satırı (`cleaning Osys.Parts.sln…`).
- **İlerleme göstergesi:** sticky şeritte amber spinner + `Cleaning 4/9 · Osys.Parts.sln · 3s` ve 2px amber progress (adım/toplam). Ayrı loading overlay'i YOK.
- **Kilitleme:** görev sürerken Sync, Build/Continue, branch/worktree, Debug/Release ve diğer bakım butonu disabled; F5 no-op. Yürüyen görevin butonu amber `active` + spinner. Görev durdurulamaz (4-5 sn'lik işler).
- **Bitiş özeti** şeritte yeşil sonuç satırı olarak kalır, sonraki Sync/Build/görevde temizlenir. Toast eklenmedi (§8 kararı korunuyor).
- **Neden ikon buton:** etiketli iki buton action bar'ı 1240px minimumda ~80px taşırıyor ve Build split-button'ı eziyordu. Etiketin işini tooltip + şerit metni görüyor; Clean geri alınabilir bir işlem olduğu için onay dialogu da eklenmedi.
- `ABOUT_VERSION` 1.4.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.3.0 — 2026-08-06

**Dependency graph paneli yeniden tasarlandı (§2.3 — "quiet graph").** Graph Lab denemesinden ana prototipe taşındı; yalnız panel içi değişti, seçim modeli/panel başlığı/diğer paneller aynı.

- İsimsiz mini node'lar (8–24px, statü renkli kare + Lucide box glyph) derlenme sırasına göre katman bantlarında (bant içi de build-order; açılış dalgası aynı sırayı izler); graf her panel boyutuna TAM sığar (otomatik pitch), eksik satırlar ortalanır.
- Koşu sadeliği: build başlayınca tümü soluklaşır (0.13), yalnız derlenenler parlak; biten proje sonuç rengiyle 2.4s parlak kalır, 700ms'de 0.2'ye söner (CSS gecikmeli transition); koşu bitince tümü sonuç renginde canlanır.
- Yeni building animasyonu **beads**: node'un 2.8px dışında dolanan sık amber noktalar (stroke-dash tekniği, çevreye tam bölünür, 4.2s/tur); giriş 420ms / çıkış 640ms opaklık — noktalar dönerken söner.
- Hover: 1.7× büyüme + gecikmesiz mono tooltip (TAM proje adı, panel kenarına kelepçeli).
- Seçim: node + deps + dependents panele sığdırılır (zoom 0.7–2.6 + pan, 460ms); odak dışı 0.1; amber akan bağımlılık çizgileri yalnız bu modda; node altında kelepçeli ad etiketi; boş alana tıkla → varsayılan görünüm; wheel zoom (0.7–5) + drag pan.
- Kaldırıldı: node üstü etiketler, kalıcı çizgi ağı, graf içi dep-issue rozeti.
- `ABOUT_VERSION` 1.3.0; prototip kaynakları ve standalone yeniden üretildi.

## v1.2.1 — 2026-08-05

**Logo kurumsal palete çevrildi (§6).** Mavi sürüm bırakıldı; logo artık DS'in kendi renklerinde: tile near-black (`#141417→#0A0A0C`, border `#2A2A30`), şeritler nötr rampadan (`#54545C / #3A3A42 / #EDEDEE / #A9A9B0`), chevron **amber** (`#FFB52E→#8B5907`). `app-icon.svg` kullanıcının verdiği haliyle birebir; `app-mark.svg` (uygulama içi) aynı palette düz şeritler + kısaltılmış amber gradient (`#FFB52E→#C9860C`) ile türetildi. `app-mark-mono.svg` değişmedi (zaten tek renk `#EDEDEE`).

**Sonuç:** logo arayüzle tek palette konuşuyor — title bar ve About'ta yabancı durmuyor. Karşılığında 1.2.0'daki "mavi yalnız logoya ait" kuralı düştü; yerine: **title bar'daki chevron accent ağırlığı taşıdığı için o bölgeye başka amber öğe konmaz.**

## v1.2.0 — 2026-08-05

**Yeni: ürün logosu (§6).** Build Orchestrator artık kendi markasını taşıyor — pill şeritler + yuvarlatılmış gradient chevron. Üç varyant üretildi: `app-icon.svg` (tile'lı, .exe/taskbar/bildirim), `app-mark.svg` (şeffaf, uygulama içi), `app-mark-mono.svg` (tek renk, tray). Kullanım matrisi ve renk kararları §6'da.

**Renk ayarı.** Chevron gradienti küçük boyda okunurluk için parlatıldı (tile: `#2E9BFF→#0B39C4`, uygulama içi: `#3D8BFF→#0B4FDF`); uygulama içi şeritler soğuk slate'ten DS'in nötr rampasına çevrildi. Karar: **mavi yalnız ürün markasına ait, amber tek UI accent'i olarak kalıyor** — logo mavisi arayüzde accent olarak kullanılmaz.

**Title bar logo kilidi (§2.1).** Sol üstte artık ürün markası (19px, tam renk) + ürün adı + ayraç + firma logosu (10px, %55) + repo bağlamı sıralaması var. Eskiden yalnız Delta wordmark'ı vardı. Firma logosu opsiyonel; yoksa ayraçla birlikte düşer.

**About başlığı (§2.10).** Kimlik bloğu ürün markasına (30px) geçti; sağa `LICENSED TO` + firma logosu bloklu bir kilit eklendi. Sonuç: iki logo tek kompozisyonda, ürün önde.

**Not:** `delta-app-icon.svg` artık uygulama ikonu değil — Delta'nın kendi ikonu olarak referansta kalıyor. Prototip kabuğunun başlık şeridindeki ikon da yeni `app-icon.svg`'ye çevrildi (18×18 kare slot → tile varyantı).

## v1.1.0 — 2026-08-04

**Yeni: About penceresi (§2.10).** Title bar'da dişliden sonra **ⓘ** ikonu; **F1** ile toggle. DS Dialog kabuğu (surface-raised, border-strong, radius-lg, tek overlay gölge, scrim, 180ms fade+6px giriş) ama başlık satırı yerine kimlik bloğu: logo + ürün adı + tek satır açıklama + mono sürüm satırı. DS.Segment ile üç sekme:
- **Shortcuts** — F5 (Build/Stop) · Ctrl+F5 + Shift+F5 (Rebuild) · Ctrl+F (filtre odağı) · F1 (About) · Esc (en üst katmanı kapat) · Alt+B (tray'den geri getir). Satır 26px, sağda Kbd chip'leri.
- **Environment** — App/Engine version, Engine PID, .NET runtime, OS, MSBuild yolu, Repository root, State file, Logs, Worktree pool. Etiket kolonu 130px + mono değer; uzun yollar ellipsis + tam değer `title`'da.
- **Third-party** — ad + sürüm + lisans. **Liste placeholder; gerçek bağımlılıklarla değiştirilecek.**
- Footer: solda ghost `Copy diagnostics` (sürüm + tüm Environment satırlarını düz metin olarak panoya yazar; başarıda 1.4s `Copied` + yeşil ✓), sağda `Close`.
- Gövde min-yükseklik 236px → sekme değişince dialog zıplamaz. Esc önceliği: About açıkken Esc önce About'u kapatır.

**Düzeltme: `⌄ latest` pill artık yumuşak iniyor (§2.5).** Tıklamada `setAway(false)` bir render tetikliyor, o render'da "dibe yapış" etkisi `scrollTop = scrollHeight` (anlık) uygulayıp smooth scroll'u eziyordu → pat diye atlıyordu. Jump sırasında bir `jumping` bayrağı 560ms boyunca hem sticky-pin'i hem `onScroll`'u bastırır; `behavior:'smooth'` animasyonunu tamamlar. Konsol ve event stream'de aynı.

**Düzeltme: pill görünürlük koşulu (§2.5).** Önce "yukarıdayken YENİ satır gelirse" idi; build wall-clock'a yetişip anında bittiği için pratikte hiç görünmüyordu. Artık **dipten ≥48px uzaktaysan** görünür (koşu bitmiş olsa da) — klasik dip afordansı.

**Düzeltme: log ↔ statü tutarlılığı (§3.2).** Statü `baseFails`'e bağlanmıştı ama log üretimi hâlâ projenin statik `fails` bayrağına bakıyordu; Hero sahnesinde `OSYS.Sales.Core` yeşil "Succeeded" görünürken logunda `error CS0246` satırları duruyordu. Log üretimi artık gerçek koşu kararını (`_isFail`) kullanır: succeeded log `Build succeeded — 0 errors` ile biter, hata satırları yalnız gerçekten başarısız projede görünür.

**Sürüm etiketi.** About başlığında sürüm bir kez yazılır (`1.1.0 · © 2026 Delta`); app/engine ayrımı Environment sekmesinde durur. Eski `1.0.0+it5 · engine 1.0.0+it5` tekrarı kaldırıldı.

## v1.0.0 — ilk handoff paketi

Ana pencerenin tamamı: title bar + görünüm modları (quad/list/focus) · sticky durum şeridi + 2px progress · dependency graph · projects listesi (tip-to-filter, Ctrl+F) · console (proje log seçimi, Copy log) · event stream · action bar (Build split-button: Build/Rebuild/Continue/Retry failed, statü chip'leri, Σ sayaç) · Settings dialog (katman tanımları, sürükle-bırak) · branch/worktree popover'ları · imleç yaşam döngüsü (canlı saatli boş prompt) · first-run boş durumu · warnings sayacı. Tasarım sistemi token'ları, statü tablosu ve bilinçli "yapılmayacaklar" listesi.
