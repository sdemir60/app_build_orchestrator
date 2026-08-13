# Handoff: Delta Build Orchestrator — UI Tasarım Spesifikasyonu

> **Paket sürümü: v1.7.0** · tarih: 2026-08-13 — değişenler için → [Sürüm geçmişi](#sürüm-geçmişi)
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
- cycle (uyarı turuncusu — warn satırlarında da kullanılır) `#df6f2b` / `#f0853f`
- queued `#7c7c84` / `#9a9aa2`

**Will-build noktası (statüden AYRI ortogonal kanal):** 8px daire; dolu amber = dirty (derlenecek), dolu gri `#3a3a42` = clean (atlanacak), transparent + 1px `#1c1c20` halka = unknown (Sync öncesi).

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

- Sol — **logo kilidi**: `app-mark.svg` (ürün markası, 19px, tam renk) + `Build Orchestrator` (12px/500, `text-secondary`) + 1px × 13px dikey ayraç + **firma logosu** (`delta-logo-dark.svg`, 10px, %55 opaklık, `title` ile firma adı) + mono 11px `text-dim` bağlam: `OSYS · main` — worktree aktifse `· main-2` eklenir (`text-faint`). Repo yokken: `no repository`. Hiyerarşi şart: ürün logosu daha büyük ve tam renk, firma logosu daha küçük ve soluk. Firma logosu opsiyoneldir — yoksa ayraç da düşer.
- Sağ: 3 layout ikonu (quad/list/focus, tooltip'li) · 1px dikey ayraç · dişli (Settings, tooltip: "Settings — layer definitions") · **ⓘ (About, tooltip: "About — version, shortcuts and diagnostics (F1)")** · pencere kontrolleri (min/max/close).

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
- Sağda **döngü kümesi** (yalnız Sync bir SCC bulduysa; hata kümesinin solunda): turuncu üçgen glyph + `3 in a dependency cycle` chip'i (cycle rengi, 20px). Tıkla → listede `cycle` filtresi. Hover tooltip iki satır: `In a dependency cycle — won't be built` + mono döngü yolu `Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts`. Küme Sync'ten koşu sonuna kadar kalıcıdır — döngü bir koşu sonucu değil, bir yapılandırma hatasıdır.
- Altında **2px global ProgressBar**: değer = tamamlanan/derlenecek; renk building=amber, failed=kırmızı, done=yeşil; sync sırasında indeterminate. Radius 0.

## 2.3 Dependency graph (sol üst) — v1.3.0 "quiet graph"

Panel başlığı aynı (caps `DEPENDENCY GRAPH` + sağda mono sayaç). Panelin İÇİ yeniden tasarlandı: node üzeri ad etiketleri ve kalıcı bağımlılık çizgi ağı kaldırıldı — amaç 100+ projede bile sakin, tek bakışta okunan bir yüzey. Adlar hover tooltip'i ve seçim etiketiyle verilir.

### Yerleşim — katman bantları
- Bantlar **derlenme sırasına** göre: layer 0 (ilk derlenenler) en üstte, bağımlı katmanlar alta doğru. Bant içindeki dizilim de **build-order**dır (ilk derlenecek soldan başlar), yani göz üstten alta / soldan sağa okuduğunda derleme sırasını görür.
- Pitch (node adımı) otomatik: 44px'ten 5'e 0.5 adımla taranır; tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine sığan İLK değer seçilir → graf HER panel boyutunda tam sığar, scrollbar yok.
- Bantların eksik kalan son satırı yatay ORTALANIR; tüm blok panelde ortalanır (12px kenar payı; hesap alanı W−24 × H−24).
- Node = kare kutu, boyut = pitch×0.6 (8–24px kelepçe), radius-sm, 1.5px border, içinde Lucide `box` glyph'i (node'un %52'si, 1.8px stroke).
- Statü görünümü DS `DependencyGraphNode` tablosuyla birebir: zemin `--status-*-soft`, border `--status-*`, glyph `--status-*-text`; discovered = kesikli `--border-strong` + `--surface-raised` zemin.

### Koşu yaşam döngüsü — soluk/parlak sistemi
- idle/boot/sync: tümü tam opak.
- Koşu başlayınca (phase=running): graf soluklaşır — queued/discovered opacity **0.13**; yalnız o an derlenenler tam opak.
- Proje bitince: sonuç rengine döner (succeeded yeşil / failed kırmızı / skipped gri) ve **2400ms tam opak KALIR**, sonra **700ms'de 0.2'ye** söner. Uygulama hilesi: opacity değeri anında 0.2 yazılır, beklemeyi CSS taşır → `transition: opacity 700ms var(--ease-standard) 2400ms` (timer/ek render yok).
- Koşu bitince (done/stopped): tümü sonuç renginde tam opak.
- Zemin/kenar/glyph renk geçişleri 380ms ease-standard; opaklık geçişi 280ms (hold-fade hariç).

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
  1. **Statü şeridi** 2px dikey (satırın tam sol kenarı; statü çekirdek rengi; discovered=transparent). Seçiliyken 3px + amber (discovered ise).
  2. **Nokta** 8px — B/C kanalı: amber=derlenecek · gri=güncel · **turuncu=döngü üyesi (KALICI — statü ne olursa olsun)**. Tooltip'li: `Will build — source changed since last build` / `Up to date` / cycle açıklaması + döngü yolu.
  3. **Ad** 13px/500 — tek kural (v1.7.0): bu koşuda işi olan satır (dirty · queued · building · failed) `text-primary`, güncel/atlanacak satır `text-secondary` + yanında **sln adı** 12px `text-faint` (`Osys.Sales.sln`). Taşmada ellipsis.
  4. Sağ blok (min 118px, sağa yaslı): hover'da **2 ikon buton** — "Reveal in Explorer" (klasör) ve "Open in Visual Studio" (kod ikonları, tooltip'li; tıklayınca konsola dim not düşer); hover yokken mono 10.5px SHA — dirty: `a3f81c2 → b7e91d4` (`text-secondary`), clean: tek `b7e91d4` (`text-faint`). Succeeded olan projenin `curSha`'sı hedefe eşitlenir — SHA her satırda görünür, satırlar arası sıçrama yok (v1.7.0).
  5. **Statü glyph'i** 14px, tooltip: durum adı (+ building ise geçen süre). Glyph her zaman GERÇEK statüyü gösterir; uyarılar yandaki slottadır — glyph'in yerine asla geçmez (v1.7.0).
  6. **Sabit 14px uyarı slotu**: cycle üyeliği ve/veya depIssue varsa TEK üçgen (12px) — **renk en ağır nedeni söyler: cycle üyesi satırda turuncu (`--status-cycle`), yalnız depIssue'da amber (`--amber-text`)**; tooltip nedenleri alt alta listeler — cycle açıklaması + `Dependency issue: X — last successful output referenced`. Satırın kendisi building iken gizlenir (spinner'la yarışmaz). Slot her satırda var — **hiza asla bozulmaz**.
  7. **Süre** mono 12px sağa yaslı 46px: building=canlı sayaç, bitti=`4.2s`, yoksa `—`. Failed'da kırmızı.
- **Building satırı efekti:** kart zemininde hareketsiz amber "nefes" — `amber-soft` katmanı opacity 0→0.32→0, 3.8s ease-in-out sonsuz (tepe ~%3 görünür etki). Süpürme/parlama/kayma YOK (denendi, istenmedi).
- **Failed anı:** satır 360ms yatay shake (±3px), bir kez.
- **Sol şerit (v1.7.0):** her satırda görünür — workspace açıldığı andan itibaren **gri** (`--status-skipped-border`; discovered ve skipped AYNI ton — iki gri kafa karıştırıyordu), koşuda amber, bitişte sonuç rengi. Sync şeridi getirmez, zaten oradadır; Sync yalnız plan kanalını (nokta) tazeler. Şerit 2px (seçilide 3px) ve **1px dikey iç boşluklu** — boşluk satır ayracı kadar: bitişik satırlarda tek kesintisiz çizgiye kaynamaz, ama araları da açılmaz.
- **Döngüdeki satırlar (v1.7.0):** nokta **kalıcı turuncu** (`--status-cycle` #df6f2b — amber'dan ayrışan koyu ton); atlanan üye satırı: gri şerit + `—` glyph + turuncu nokta + turuncu üçgen + **beyaz isim** (işi bitmedi, Resolve bekliyor) + `cur→hedef` SHA; sol şerit ve statü glyph'i normal statüyü izler (sync'te discovered, Resolve'da amber→yeşil, standart Build atlarsa `—` skipped), uyarı slotunda amber üçgen. Standart Build üyeleri `skipped — in a dependency cycle, not rebuilt` olarak düşürür; derleyen tek akış **Resolve cycles** (§3.7). Derlenip yeşil de olsa nokta turuncu kalır — kod hâlâ döngülü.
- **Katman başlıkları:** 24px, caps 11px + mono sayı (satır adedi); **birikerek yapışır** — i'inci görünür başlık `top = i×24px`'e yapışır, alttakiler kaydıkça üsttekiler asılı kalır.
- **Gruplama:** Settings'teki regex tanımlarıyla, ilk eşleşen kazanır; eşleşmeyen → `Other`. **Varsayılan: katman YOK → başlıksız tek liste, build sırasında.**
- **Follow-mode:** koşarken ve seçim yokken liste frontier'i yumuşak takip eder (ilk building satırı görünür tutulur; scroll animasyonu 550ms'de bir, hedef sapması <54px ise dokunulmaz). Karta tıklayınca takip durur; seçim kalkınca sürer.
- Boş durum (repo seçilmemiş): ortada `Pick a repository to get started` (14px/600) + açıklama `Point to the OSYS solution root — projects and the dependency graph are discovered automatically.` + primary buton `Choose Folder` (klasör ikonu).
- Filtre eşleşmezse: `No projects match this filter.`

## 2.5 Console (sağ üst)

- Zemin **`#060608`, radius 0**, padding 8×12. **Mono 12px, ağırlık 300 (Light)**, satır 1.55. Alta yapışık scroll (kullanıcı 48px'ten fazla yukarı kaydırırsa serbest bırakılır, dibe inince yeniden yapışır). **`⌄ latest` pill:** kullanıcı dipten uzaktayken (≥48px) panel alt-ortasında küçük mono pill (surface-overlay, border-strong, radius-md, popover gölgesi); tıkla → yumuşak en alta iner. Koşu bitmiş olsa da yukarı kayınca çıkar — klasik dip afordansı; dibe dönünce/tıklayınca kaybolur, konsol↔proje-log geçişinde dibe sabitlenir.
- **İki mod:**
  - **Anlatı (seçim yok):** satır = **yalnız metin** — saat sütunu ve `▸` ikon kolonu YOK (v1.6.0); tüm satırlar imleçle aynı sol hizada başlar. Satır türü yalnız **renkle** ayrılır: cmd=`text-primary`, info=`text-secondary`, dim=`text-faint`, success/warn/error=ilgili `-text` tonu. Canlı gelen satırlar **anında** basılır — daktilo yok. En altta tek **prompt satırı**: yanıp sönen blok imleç (7×13px, 1.1s blink), idle/boot'ta yanında `ready` (dim).
  - **Seçili proje logu:** panel başlığı değişir → `← Back` ghost buton + proje adı (mono) + statü glyph + statü adı + (varsa) `▲ dependency issue` rozeti. Building ise sonda amber `build in progress ▮`.
- **Görünüm değişimi = kaskat (v1.6.0).** Proje logu açılırken ve `← Back` ile ana loglara dönerken konsol içeriği **aşağı serilerek** açılır (tek parça "tilt in": perspective 900px, rotateX 7° + 14px, 340ms) — pat diye değişmez; proje listesi filtresindeki kaskatla aynı dil. **İki yön de aynı sürede** biter: serilme 14 adım × 26ms ≈ 360ms, adım başına satır sayısı içeriğe göre ölçeklenir (3 satırlık log ile 200 satırlık anlatı aynı hissedilir). Log yoksa gösterilen tek açıklama satırı da aynı pop-in ile gelir. Kaskat yalnız **açılış anlık görüntüsüne** uygulanır; sonrasında akışa katılan canlı satırlar anında basılır. Log yoksa: skipped → `Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (a3f81c2)`; queued → `Queued — waiting for dependencies: Sales.Core, Security`; diğer → `No log yet — output streams here once the build starts.`
- Akış yönü **klasik: en yeni altta** (her iki panelde; "en yeni üstte" değerlendirildi, İSTENMEDİ).
- Panel başlığı sağında mono `N lines`.

## 2.6 Event stream (sağ alt)

- Panel başlığı: caps `EVENT STREAM`; sağda mono `N events`.
- Satır (min 24px, mono 12px): saat + glyph (ok=✓, fail=✗, skip=—, sync/info/task=amber `▸`, done/taskdone=✓/✗) + metin. Renkler: fail=kırmızı, skip=`text-faint`, done=yeşil/kırmızı, sync/info/task=`text-dim`, taskdone=yeşil, ok=`text-secondary`.
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
4. **Sayaç chip'leri** (tıkla=filtre toggle, tooltip'li). **Temel beşli her zaman durur:** `Σ 36` (tümü/filtre temizle) · spinner+`4` (building; boşken gri nokta) · `✓ 14` · `✗ 5` · `— 17`. **İki istisnai chip yalnız listede karşılığı varken çıkar:** `⚠ 3` (döngü; turuncu cycle glyph'i, filtre `cycle`, tooltip `In a dependency cycle — filter`) ve `▲ 4` (dependency-affected; kırmızı üçgen, filtre `dep`). İkisi de nadir durumları anlatır — boş/gri chip taşımazlar. Aktif filtre chip'i vurgulu.
5. Esnek boşluk.
6. `branch: main ▾` chip (branch ikonu) → **Branch popover**.
7. `worktree: off ▾` chip (ağaç ikonu) → **Worktree popover**.
8. `Debug | Release` segment (sm).
9. `perf: Balanced` chip — tıkla döngü: Full(6) → Balanced(4) → Light(2) paralellik. Tooltip YOK (istenmedi).
10. 1px ayraç.
11. **Build split-button** (primary md, play ikonu): sol `Build` (F5, **stale set** — değişen + hatalı + hiç derlenmemiş + hatalıların bağımlıları) + sağ `▴` ok → yukarı menü: `Build — Only stale projects — F5` ve `Rebuild — All 36 projects — cache ignored — Ctrl+F5` (ikon+başlık+açıklama+Kbd). Koşarken yerine **`Stop` danger butonu** (kare ikon; F5 de durdurur). **Continue ve Retry failed KALDIRILDI (1.7.0)** — Stop sonrası Build kaldığı yerden sürdürür, hata sonrası Build hatalıları + bağımlılarını yeniden alır.

Toast/popup YOK (karar). Kaynak bilgisi yalnız worktree popover'ındaki `source` satırında.

## 2.8 Popover'lar

Ortak: chip'in üstünde 8px boşlukla açılır, `surface-overlay` zemin, 1px `border-strong`, radius 8, overlay gölgesi, 140ms pop-in (4px yukarı + scale .985→1). Dışarı tıkla / Esc → kapanır.

**Branch (272px):** caps başlık `SWITCH BRANCH` · arama inputu (büyüteç, `Search branches…`) · satırlar (28px): seçilide ✓ amber, değilse branch ikonu; ad mono; aktif branch'te amber `active` rozeti, diğerlerinde mono SHA. Eşleşme yoksa `No branches match "q".` · Alt not: `Picking a non-active branch requires a worktree; the active branch stays untouched.`
- **Aktif olmayan branch seçilince:** worktree zorunlu ON (switch disabled), tüm proje durumları sıfırlanır (discovered/unknown), faz boot'a döner; konsola `git switch --detach f3a02c8  # release/2026.06 (worktree required)` + `Branch changed: release/2026.06 — Sync required` yazılır.

**Worktree (300px):** caps `WORKTREE` · Switch `Build in worktree` (zorunluysa disabled) · açıklama metni: zorunlu → `Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched.` / açık → `The committed HEAD builds in a separate worktree; local changes excluded.` / kapalı → `Off: in-place build — local changes included.` · Açıkken `TARGET WORKTREE` listesi: `main-2 (new)` (auto) + mevcutlar (`main-1 — 2 days ago · clean`), hover'da çöp kutusu (sil) · En altta `source` satırı: mono, `working directory — local changes included` veya `committed HEAD (main) → main-2`.

## 2.9 Settings dialog (620px)

Dişliden açılır. DS Dialog: `surface-overlay`, radius 8, overlay gölge, scrim.

- Caps başlık `LAYERS` + açıklama: `Projects are grouped by the first matching pattern (regex on the project name), top to bottom; card order is the layer order in the list. Non-matching projects fall under Other.`
- **Katman kartları** (36px + 6px boşluk): grip tutamacı (sürükle-bırak sıralama — kart yarım satır eşiğiyle yer değiştirir, sürüklenen kart raised zemin + strong border) · ad inputu (170px) · regex inputu (mono, esnek; geçersiz regex kırmızı invalid durumu) · çöp ikonu (sil).
- `+ Add layer` ghost buton.
- **Varsayılan BOŞ** — boş durumda kesikli kutu: `No layers yet — projects show as a single list in build order.`
- Footer: solda ghost `Load sample layers` (örnek 6 OSYS katmanını doldurur) · sağda `Cancel` + `Save` (primary; herhangi bir ad boş/regex geçersizse disabled).
- Kaydet → listede gruplar güncellenir, konsola dim not: `Layer definitions updated — 6 layers` / `Layers removed — single project list`. Kalıcı (prototipte localStorage `delta-bo-layers-v2`).
- Eşleşme sayacı gösterilmez (istenmedi).

---

## 2.10 About dialog (620px, F1)

Title bar'daki **ⓘ** ikonundan veya **F1** ile açılır (F1 toggle). DS Dialog ile aynı kabuk (`surface-raised`, 1px `border-strong`, radius-lg, tek overlay gölge, scrim, 180ms fade+6px yukarı giriş) ama **başlık satırı yok** — yerine kimlik bloğu:

- **Başlık bloğu** (18px padding): solda `app-mark.svg` (30px) + `Build Orchestrator` (17px/600) · alt satır `Ordered, incremental builds for a multi-project .NET solution.` (12px `text-dim`) · mono 11px `text-faint`: `1.2.0 · © 2026 Delta`. Sağda **firma kilidi**: 1px × 30px `border-subtle` ayraç + sağa yaslı caps 11px `text-faint` `LICENSED TO` etiketi + firma logosu (13px, %80 opaklık). Firma logosu yoksa blok tamamen düşer.
- **Segment** (DS.Segment, sm): `Shortcuts | Environment | Third-party`. Gövde min-yükseklik 236px — tab değişince dialog zıplamaz.
- **Shortcuts:** satır 26px; solda açıklama (13px `text-secondary`), sağda Kbd chip'leri (birden fazlaysa 4px arayla). İçerik: F5 / Ctrl+F5 + Shift+F5 / Ctrl+F / F1 / Esc / Alt+B.
- **Environment:** etiket kolonu 130px (12px `text-dim`) + değer mono 12px `text-secondary`; uzun yollar ellipsis + `title` ile tam değer. Satırlar: App version · Engine version · Engine PID · .NET runtime · OS · MSBuild · Repository root · State file · Logs · Worktree pool.
- **Third-party:** ad + mono sürüm (70px) + sağa yaslı mono 11px lisans (92px). **Prototipteki liste PLACEHOLDER** — gerçek paketlenen bağımlılıklardan üretilecek.
- **Footer** (üstte `border-subtle`): solda ghost `Copy diagnostics` (kopya sonrası 1.4s `Copied` + yeşil ✓; panoya sürüm + tüm Environment satırları düz metin gider), sağda secondary `Close`.
- Esc önceliği: About açıkken Esc **önce About'u** kapatır (Settings'ten önce). Scrim tıklaması da kapatır.

---

# 3. Davranış ve Akışlar

## 3.1 Fazlar

`empty → boot → syncing → idle → running → done | stopped` · dikey eksen: `task` (bakım görevi — §3.4)

- **empty:** repo yok. Liste panelinde davet + `Choose Folder`; graf/konsol/stream bekleme metinleri; şerit `Not ready — no repository selected`; Sync/Build/chip'ler disabled.
- **boot:** repo var, Sync yapılmadı. Konsolda açılış satırı: `Build Orchestrator 2.4.1 — Osys.sln loaded (36 projects) · main`. Tüm dot'lar unknown (hollow).
- **Sync:** konsola `▸ git fetch origin main` → `HEAD b7e91d4 — computing osys-state diff` → `Sync complete — 7 changed projects, 14 to build` + `22 projects up to date (will skip)`. Dot'lar dirty/clean olur; şerit `Ready — …`.
- **Build (F5):** boot'ta ve Rebuild'de Sync otomatik koşar (~1.2s sonra derleme); diğer her durumda **mevcut durumdan** koşar — stale set = değişen + hatalı + hiç derlenmemiş + hatalıların bağımlıları (`startRunFromState`; sync/reset yok, konsol/stream sıfırlanmaz). Stale set boşsa hızlı kontrol koşusu (`Everything up to date`). Önceki koşuda hata varsa düzeltme uygulandı varsayılır (baseFails kapanır). Konsola `▸ msbuild Osys.sln /m:4 /p:Configuration=Debug — 14 projects, 22 skipped`.
- **Rebuild (Ctrl+F5):** tümü dirty kabul edilir; konsola warn `Rebuild — cache ignored, all 36 projects queued`.
- **Stop:** building olanlar queued'a döner; konsola warn `Build stopped — 7/14 completed`, stream'e `Stopped — 7 remaining projects queued`. Sonrası **Build** kaldığı yerden sürdürür (ayrı Continue yok — 1.7.0); elapsed yeni koşuda sıfırlanır. Koşu sürerken F5 = Stop.

## 3.2 Scheduler kuralları (gerçek uygulamada Core'un davranışı — UI bunu yansıtır)

- Derleme sırası **liste sırasıdır** (katman → tanım sırası). Sıradaki projenin bağımlılığı bitmemişse **İLERİ ATLANMAZ** (`if (!ok) break`) — paralellik katman içinde doğal oluşur. Paralellik üst sınırı perf ayarından (2/4/6).
- **Hatalı bağımlılık ALT PROJELERİ BLOKLAMAZ:** bağımlılar son başarılı çıktıyla yine derlenir; kök hata adları `depIssue` olarak zincir boyunca aşağı taşınır. Bu projeler:
  - log başında warn satır(lar)ı alır: `warning: OSYS.Sales.Core failed in this run — last successful output referenced (yesterday 18:42)` (dolaylıysa: `warning: failure in dependency chain (Sales.Core) — referenced outputs may be stale`),
  - kartta/grafta üçgen rozet taşır, `dependency-affected` sayacına girer,
  - stream'de `built — dependency issue (2.4s)` olarak görünür; konsolda warn tonunda.
- **Döngü üyeleri standart koşuya hiç girmez (v1.7.0):** `skipped — in a dependency cycle, not rebuilt` olarak düşer (çözülmüşlerse normal `up to date`); bitiş özetine `· N cycle projects skipped` eklenir. Derlenmemiş döngü üyesine bağımlı projeler depIssue taşır (`warning: X is in a dependency cycle and was not rebuilt — last known output referenced`). Döngüyü yalnız **Resolve cycles** derler (§3.7).
- Temiz projeler bağımlılıkları çözülünce **dalga dalga skip** edilir (tek seferde hepsi değil — tik başına ~3, all-clean'de ~12).
- Succeeded olan projenin will-dot'u griye (clean) döner — artık güncel.
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
- **Solution temizlendiği anda** o solution'ın projeleri `discovered` + will-build `dirty` (amber nokta) hâline döner — liste ve graf canlı olarak boşalır, sayaç chip'leri düşer.
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

Bakım kutusunun üçüncü ikonu (**unlink**, cycle turuncusu; yalnız Sync bir döngü bulduysa enabled). Gerçek bir proje döngüsü tek MSBuild geçişiyle derlenemez; Resolve **iki geçişle** çözer:

- **Kapsam:** döngü üyeleri + bayat (dirty/failed) bağımlılıklarının kapanışı. **Ardışık** koşar (paralellik 1) — çözüm sıraya duyarlıdır.
- **Pass 1:** önce bağımlılıklar, sonra üyeler **son bilinen (bayat) referanslarla** derlenir; üye loglarında warn `warning: circular reference (Parts.Inventory, Parts.Api) — building against last known outputs`. Üye pass 1 sonunda yeşil görünür ama noktası amber kalmaz → hayır: üye pass 1'de `will=dirty` KALIR (çıktı bayat), yalnız pass 2 temizler.
- **Pass 2:** yalnız üyeler taze referanslarla yeniden derlenir (~0.45× süre) → yakınsar; `will=clean`, `curSha` hedefe eşitlenir. Nokta/çekirdek **turuncu kalır** — kod hâlâ döngülü; konsola dim not düşer.
- **Konsol:** `osys-resolve-cycles — 3 projects in cycle + 1 stale dependencies · 2 passes` + mono döngü yolu + pass ayracı satırları (`pass 1/2 — building with last known references`, `pass 2/2 — rebuilding cycle projects to converge`); proje bitişleri normal success satırı (pass 1 üyelerinde `— stale references` eki).
- **Şerit:** amber spinner + `▸ Resolving cycles · pass 1/2 · 2/7 · 4s`; 2px amber progress = biten/toplam. **Stream:** `task`/`taskdone` olayları + `built — pass 1/2` satırları + canlı `… building…` daktilosu.
- **Bitiş:** faz `idle`; şeritte kalıcı yeşil satır `Cycles resolved — 3 projects converged in 2 passes · outputs now current` (sonraki eylem temizler). Sonuç **normal succeeded** — sayaçlara girer; ayrı "resolved" rengi YOK.
- **Kilitleme:** koşu `running` fazındadır — Sync/Build/Clean/Optimize disabled; **Stop çalışır**: scope discovered'a döner, faz `idle`, üyeler çözülmemiş kalır (konsola warn).
- Resolve sonrası Build üyeleri doğal atlar (`up to date`); yeni Sync değişiklik bulursa üyeler yine dirty işaretlenir.

---

# 4. State (WPF/MVVM karşılığı)

- **Engine/VM durumu:** faz; proje başına `{status: discovered|queued|building|succeeded|failed|skipped, will: dirty|clean|unknown, startAt, doneDur, depIssue: string[]|null, log[]}` + **kalıcı cycle üyelik kümesi** (statü DEĞİL — v1.7.0); willBuild kümesi (döngü üyeleri hariç); `resolveRun {list, pass, fin, total, startT}`; sayaçlar (building/succeeded/failed/skipped/queued, depIssueCount, cycle=üyelik sayısı); elapsed + ETA; anlatı satırları `{type, time, text}`; stream olayları `{kind, time, project?, text}`; aktif satır.
- **UI durumu:** `selected` (proje adı|null), `filter`, layout `{mode, col, left, right}` (kalıcı), `layerCfg` (kalıcı), branch seçimi, worktree `{on|forced, chosen|auto}`, cfg (Debug/Release), perf.
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
| cycle üyeliği (ortogonal, KALICI) | 8px turuncu nokta (kart) / turuncu çekirdek (graf) + uyarı slotunda ▲ | #f0853f | tooltip: `In a dependency cycle — standard builds skip it; Resolve cycles builds it in two passes` + döngü yolu |
| dep-affected (ortogonal) | uyarı slotunda ▲ (cycle ile birleşik, tek üçgen) | amber `--amber-text` | tooltip: `Dependency issue: X — last successful output referenced` |
| will-build (ortogonal dot) | 8px nokta (kart) / node çekirdeği (graf) | amber=derlenecek · gri=güncel · hollow=unknown | tooltip: `Will build — source changed` / `Up to date` |

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

- Toast/popup yok · "View failures" butonu yok · perf/Build tooltip'i yok · katman eşleşme sayacı yok.
- Clean/Optimize/Resolve: etiketli buton yok (bar taşıyor), onay dialogu yok; Clean/Optimize durdurulamaz, Resolve durdurulabilir (`running` fazı); ayrı loading overlay'i yok.
- **Continue ve Retry failed YOK (1.7.0)** — Build her zaman stale set'i derler (Stop sonrası devam + hata sonrası retry bunun doğal sonucu). Yeniden önerme.
- Building efekti yalnız sabit "nefes"; süpürme/parlama denendi, İSTENMEDİ.
- Konsol/stream akışı klasik (en yeni altta); "en yeni üstte" İSTENMEDİ.
- Cull/agrega (çok-solution rollup) görünümü GEREK YOK.
- Emoji, gradient, amber dışı dekoratif renk, panel gölgesi, backdrop-blur YASAK.
- Varsayılan katman konfigürasyonu BOŞ (tek liste); örnek katmanlar yalnız "Load sample layers" ile.
- Cycle işareti TURUNCU ve KALICI (nokta + graf çekirdeği); kırmızı badge eklenmez (kırmızı = derlendi ve patladı). ARCHITECTURE.md §14.3 buna göre düzeltilecek.
- Cycle ile dep-affected uyarıları TEK amber üçgende birleşti (v1.7.0) — ayrım tooltip satırlarında; ayrı sütun/ayrı renk taşınmıyor.
- Dep-affected tooltip metni yeterli (`Dependency issue: X — last successful output referenced`); tıklanabilir bağımlılık adı, satır geneli hover ve ek "output may be stale" satırı İSTENMEDİ.
- `⚠` cycle ve `▲` dep-affected chip'leri yalnız listede karşılığı varken görünür; kalan beş sayaç chip'i her zaman durur.

---

# 9. Sürüm geçmişi

Her yeni ekleme/çıktıda sürüm artar ve bu bölüme ne değiştiği ayrıntılı yazılır. Sürüm numarası aynı zamanda uygulamanın About penceresinde ve `Copy diagnostics` çıktısında görünür (`prototype/app/BuildApp.jsx` → `ABOUT_VERSION`).

Sürüm başlıkları `v` önekiyle yazılır (`## v1.7.0`) — spec bölüm numaralarından (`## 1.1 Renkler`) böyle ayrılır.

| Sürüm | Tarih | Konu |
|---|---|---|
| **v1.7.0** | 2026-08-13 | Cycle akışı + üç-kanal statü modeli · Resolve cycles · Build sadeleşmesi |
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

## v1.7.0 — 2026-08-13

**Cycle akışı ve statü kanalları yeniden kurgulandı (üç-kanal modeli); Resolve cycles eklendi; Build semantiği sadeleşti.** Kullanıcı akışı: Sync → (istenirse) Build (döngü atlanır) → Resolve cycles → Build. Sıra dayatması yok; Scene 8 zinciri interaktif oynatır.

### Statü kanalları (§2.3, §2.4, §5)

Önceki sürümde cycle bilgisi bazen sol şeridi, bazen statü ikonunu işgal ediyordu; aynı renk iki anlama gelebiliyordu. Artık her kanal tek soruya cevap verir ve başka kanalın yerine geçmez:

| Kanal | Soru | Nerede | Değerler |
|---|---|---|---|
| A — Sonuç | Bu koşuda ne oldu? | kart sol şeridi · statü glyph'i · graf node border'ı | discovered · queued · building · succeeded · failed · skipped |
| B — Plan | Sıradaki Build buna dokunacak mı? | kart noktası · graf node çekirdeği | amber = derlenecek · gri = güncel |
| C — Yapısal | Kodda döngü var mı? | kart noktası · graf çekirdeği · uyarı üçgeni | turuncu = döngü üyesi (kalıcı) |

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
