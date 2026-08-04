# Handoff: Delta Build Orchestrator — UI Tasarım Spesifikasyonu

> **Paket sürümü: 1.2.1** · tarih: 2026-08-05 — değişenler için → [Sürüm geçmişi](#sürüm-geçmişi)
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
- **Font notu (açık iş):** Geist şu an Google CDN'den; air-gapped paket için woff2/ttf dosyaları temin edilip gömülecek (kullanıcı kararı — geliştirme sırasında yapılacak).

## 1.3 Boşluk / radius / elevation / motion

- 4px grid (`4/8/12/16/20/24/32…`). Satır 36px (compact 30). Titlebar 40 · statusbar 28.
- Radius: kontrol 4 · kart/panel 6 · overlay 8 · chip/kbd 3 · **console 0 (keskin)**. Pencere kökü 8.
- Gölge YALNIZ floating overlay'de: `0 10px 28px -10px rgba(0,0,0,.66), 0 2px 6px -2px rgba(0,0,0,.5)`. Panel/kart gölgesiz — yapıyı 1px border taşır.
- Motion: 80/120/180/280ms; ease-out `cubic-bezier(.22,1,.36,1)` giriş, ease-standard `cubic-bezier(.4,0,.2,1)` durum değişimi, ease-in-out `cubic-bezier(.65,0,.35,1)` yer değiştirme. Bounce/overshoot yok; yalnız transform+opacity. Aynı anda en fazla 1 hero motion. **OS reduced-motion → tüm süreler 0** (uygulama içi toggle yok).

## 1.4 İkonografi

- Lucide geometrisi, 1.5–2px stroke, tek renk (currentColor), 12–16px. Emoji ASLA.
- Statü glyph'leri = ince halkalı daire içinde çizim: ✓ tik (success), ✗ çarpı (fail), — tire (skipped), saat (queued), kesikli daire (discovered), uyarı üçgeni (cycle/dep).
- **Building spinner = discovered'ın kesikli halkasının amber, dönen hali**: `stroke-dasharray 2.3 2.5` dairesel halka, 1.4s lineer sonsuz dönüş. (Ayrı bir "spinner" çizimi değil — aynı halka döner.)
- Dep-hata rozeti: küçük DOLU üçgen (▲), 12-13px, `status-fail-text` renkli.
- Logolar: `assets/delta-logo-dark.svg` (title bar, 15px yükseklik), `assets/delta-app-icon.svg` (pencere/taskbar ikonu).

## 1.5 Dil ve ton

- **Tüm UI, proje adları ve loglar İNGİLİZCE** (OSYS.Sales.Core, "Build", "Sync", "up to date"…). Kod yorumları Türkçe kalabilir.
- Ton: sakin, kesin, mühendisçe. Ünlem yok, espri yok. Net rakam + net durum: `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`.
- Statü daima **renk + glyph + metin** üçlüsü (colorblind-safe).

---

# 2. Pencere Yerleşimi

Tek pencere, min ~1240×620. Kök: `surface-base` zemin, 1px `border`, radius 8, overflow hidden. Dikey sıra:

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
- Altında **2px global ProgressBar**: değer = tamamlanan/derlenecek; renk building=amber, failed=kırmızı, done=yeşil; sync sırasında indeterminate. Radius 0.

## 2.3 Dependency graph (sol üst)

- Panel başlığı (28px, `surface`, alt `border-subtle`): caps `DEPENDENCY GRAPH`; sağda mono 11px `36 projects · 58 dependencies`.
- Zemin `surface-base`. Katmanlı DAG: her katman bir yatay sıra (satır aralığı 96px, düğüm aralığı ≤96px), kenarlar yukarıdan aşağı kübik bezier.
- **Düğüm:** 26px daire (DS `DependencyGraphNode`), altında kısa ad etiketi (`OSYS.` öneki atılır). Statü rengi düğümde; seçiliyken amber halka.
- **Kenar renkleri:** varsayılan `border` 1px · hedef building → **amber, kesikli akış animasyonu** (dash 4 7, 0.9s kayar; reduced-motion'da statik kesikli) · hedef succeeded → yeşil border tonu · failed → kırmızı · **hatanın taşındığı dal** (kaynak failed veya depIssue taşıyor) → kırmızı, statik kesikli `3 4` · seçili düğüme değen kenar → amber (veya hata dalıysa kırmızı), 1.6px, tam opak.
- **Seçim:** seçili + komşuları normal; geri kalan düğümler %25 opaklığa söner, kenarlar %16'ya. Boş alana tıkla → seçim kalkar.
- **Kamera:** otomatik — seçili düğüme, yoksa building frontier'in ağırlık merkezine, done/stopped'ta merkeze. Yumuşak geçiş: transform 460ms ease-in-out. Ölçek panele sığdırılır, 0.68–1.08 aralığına kıstırılır.
- **Dep-hata rozeti:** depIssue taşıyan düğümün sağ üstünde 13px daire (zemin `surface-base`, 1px kırmızı border) içinde dolu kırmızı üçgen.
- Sync öncesi boş durum: ortada kesikli çerçeveli kutu `Graph appears after Sync`.
- İlk açılışta düğümler katman katman belirir (fade+yukarıdan 5px, katman başına 55ms gecikme).

## 2.4 Projects listesi (sol alt)

- Panel başlığı: caps `PROJECTS` + mono `build-order` etiketi; aktif filtre varsa kaldırılabilir chip (ör. `Failed ✕`).
- **Satır (36px, alt çizgi `border-subtle`):** soldan sağa:
  1. **Statü şeridi** 2px dikey (satırın tam sol kenarı; statü çekirdek rengi; discovered=transparent). Seçiliyken 3px + amber (discovered ise).
  2. **WillBuildDot** 8px (bkz. 1.1).
  3. **Ad** 13px/500 `text-primary` (skipped/discovered ise `text-dim`) + yanında **sln adı** 12px `text-faint` (`Osys.Sales.sln`). Taşmada ellipsis.
  4. Sağ blok (min 118px, sağa yaslı): hover'da **2 ikon buton** — "Reveal in Explorer" (klasör) ve "Open in Visual Studio" (kod ikonları, tooltip'li; tıklayınca konsola dim not düşer); hover yokken dirty projelerde mono 10.5px `a3f81c2 → b7e91d4` (curSha → hedef SHA).
  5. **Statü glyph'i** 14px, tooltip: durum adı (+ building ise geçen süre, depIssue ise `— dependency issue`).
  6. **Sabit 14px slot**: depIssue varsa küçük kırmızı üçgen-ünlem (12px, tooltip: `Failed dependency: Sales.Core — last successful output referenced`). Slot her satırda var — **hiza asla bozulmaz**.
  7. **Süre** mono 12px sağa yaslı 46px: building=canlı sayaç, bitti=`4.2s`, yoksa `—`. Failed'da kırmızı.
- **Building satırı efekti:** kart zemininde hareketsiz amber "nefes" — `amber-soft` katmanı opacity 0→0.32→0, 3.8s ease-in-out sonsuz (tepe ~%3 görünür etki). Süpürme/parlama/kayma YOK (denendi, istenmedi).
- **Failed anı:** satır 360ms yatay shake (±3px), bir kez.
- **Katman başlıkları:** 24px, caps 11px + mono sayı (satır adedi); **birikerek yapışır** — i'inci görünür başlık `top = i×24px`'e yapışır, alttakiler kaydıkça üsttekiler asılı kalır.
- **Gruplama:** Settings'teki regex tanımlarıyla, ilk eşleşen kazanır; eşleşmeyen → `Other`. **Varsayılan: katman YOK → başlıksız tek liste, build sırasında.**
- **Follow-mode:** koşarken ve seçim yokken liste frontier'i yumuşak takip eder (ilk building satırı görünür tutulur; scroll animasyonu 550ms'de bir, hedef sapması <54px ise dokunulmaz). Karta tıklayınca takip durur; seçim kalkınca sürer.
- Boş durum (repo seçilmemiş): ortada `Pick a repository to get started` (14px/600) + açıklama `Point to the OSYS solution root — projects and the dependency graph are discovered automatically.` + primary buton `Choose Folder` (klasör ikonu).
- Filtre eşleşmezse: `No projects match this filter.`

## 2.5 Console (sağ üst)

- Zemin **`#060608`, radius 0**, padding 8×12. Mono 12px, satır 1.55. Alta yapışık scroll (kullanıcı 48px'ten fazla yukarı kaydırırsa serbest bırakılır, dibe inince yeniden yapışır). **`⌄ latest` pill:** kullanıcı dipten uzaktayken (≥48px) panel alt-ortasında küçük mono pill (surface-overlay, border-strong, radius-md, popover gölgesi); tıkla → yumuşak en alta iner. Koşu bitmiş olsa da yukarı kayınca çıkar — klasik dip afordansı; dibe dönünce/tıklayınca kaybolur, konsol↔proje-log geçişinde dibe sabitlenir.
- **İki mod:**
  - **Anlatı (seçim yok):** her satır `HH:MM:SS` (sahte duvar saati, `text-faint`) + **10px ikon kolonu** + metin. İkon kolonu: cmd satırında amber `▸`; **en yeni satırda yanıp sönen blok imleç** (7×13px, 1.1s blink). En yeni satır **daktilo gibi yazılır** (satır başına ≤ ~250ms; yazım bitince imleç ~420ms sonra söner). Boşta (idle/boot) tek satır: `12:04:07 ▮ ready` (dim). Satır renkleri: cmd=`text-primary`, info=`text-secondary`, dim=`text-faint`, success/warn/error=ilgili `-text` tonu.
  - **Seçili proje logu:** panel başlığı değişir → `← Back` ghost buton + proje adı (mono) + statü glyph + statü adı + (varsa) `▲ dependency issue` rozeti. Log satırları **remount ile sıfırdan kaskatla açılır** (26ms'de 3 satır, her satır 140ms pop-in; flash yok). Building ise sonda amber `build in progress ▮`. Log yoksa: skipped → `Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (a3f81c2)`; queued → `Queued — waiting for dependencies: Sales.Core, Security`; diğer → `No log yet — output streams here once the build starts.`
- Akış yönü **klasik: en yeni altta** (her iki panelde; "en yeni üstte" değerlendirildi, İSTENMEDİ).
- Panel başlığı sağında mono `N lines`.

## 2.6 Event stream (sağ alt)

- Panel başlığı: caps `EVENT STREAM`; sağda mono `N events`.
- Satır (min 24px, mono 12px): saat + glyph (ok=✓, fail=✗, skip=—, sync/info=amber `▸`, done=✓/✗) + metin. Renkler: fail=kırmızı, skip=`text-faint`, done=yeşil/kırmızı, sync/info=`text-dim`, ok=`text-secondary`.
- Örnek metinler: `OSYS.Domain.Service built (2.9s)` · `OSYS.Sales.Core failed — 2 errors (3.1s)` · `OSYS.Base skipped — up to date` · `Sync — 14 to build, 22 up to date` · `Build started — 14 projects, parallelism 4` · `Completed — 5 failed · 12 succeeded · 17 skipped · 1m 30s · 4 dependency-affected`.
- **En yeni satır daktiloyla yazılır**; ama sık ardışık olaylarda (<340ms) ve hata olaylarında ANINDA basılır. Aktif satır: `OSYS.Server.Api building…` — saat + **imleç** + amber daktilo metni (konsolla aynı dil).
- Projeli satırlar tıklanabilir → seçim; seçili satırda sol 2px amber şerit + `surface-raised` zemin.
- Tümü başarılı biten koşuda done satırı bir kez yeşil parlar (background `success-soft` → transparent, 1.1s).
- Alta yapışık scroll + `⌄ latest` pill (konsolla aynı kural).

## 2.7 Action bar (42px, altta)

`surface` zemin, üstte 1px `border`. Soldan sağa:

1. `Sync` — secondary sm buton (döngü ikonu). Koşarken/repo yokken disabled.
2. 1px dikey ayraç.
3. **Sayaç chip'leri** (tıkla=filtre toggle, tooltip'li): `Σ 36` (tümü/filtre temizle) · spinner+`4` (building; boşken gri nokta) · `✓ 14` · `✗ 5` · `— 17` · `▲ 4` (dependency-affected, filtre `dep`; sayı >0 ise üçgen kırmızı). Aktif filtre chip'i vurgulu.
4. Esnek boşluk.
5. `branch: main ▾` chip (branch ikonu) → **Branch popover**.
6. `worktree: off ▾` chip (ağaç ikonu) → **Worktree popover**.
7. `Debug | Release` segment (sm).
8. `perf: Balanced` chip — tıkla döngü: Full(6) → Balanced(4) → Light(2) paralellik. Tooltip YOK (istenmedi).
9. 1px ayraç.
10. **Build split-button** (primary md, play ikonu): sol `Build` (F5, yalnız değişenler) + sağ `▴` ok → yukarı menü: `Build — Only changed projects — F5` ve `Rebuild — All 36 projects — cache ignored — Ctrl+F5` (ikon+başlık+açıklama+Kbd). Koşarken yerine **`Stop` danger butonu** (kare ikon).

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

`empty → boot → syncing → idle → running → done | stopped`

- **empty:** repo yok. Liste panelinde davet + `Choose Folder`; graf/konsol/stream bekleme metinleri; şerit `Not ready — no repository selected`; Sync/Build/chip'ler disabled.
- **boot:** repo var, Sync yapılmadı. Konsolda açılış satırı: `Build Orchestrator 2.4.1 — Osys.sln loaded (36 projects) · main`. Tüm dot'lar unknown (hollow).
- **Sync:** konsola `▸ git fetch origin main` → `HEAD b7e91d4 — computing osys-state diff` → `Sync complete — 7 changed projects, 14 to build` + `22 projects up to date (will skip)`. Dot'lar dirty/clean olur; şerit `Ready — …`.
- **Build (F5):** Sync otomatik koşar, ~1.2s sonra derleme başlar. Konsola `▸ msbuild Osys.sln /m:4 /p:Configuration=Debug — 14 projects, 22 skipped`.
- **Rebuild (Ctrl+F5):** tümü dirty kabul edilir; konsola warn `Rebuild — cache ignored, all 36 projects queued`.
- **Stop:** building olanlar queued'a döner; konsola warn `Build stopped — 7/14 completed`, stream'e `Stopped — 7 remaining projects queued`.

## 3.2 Scheduler kuralları (gerçek uygulamada Core'un davranışı — UI bunu yansıtır)

- Derleme sırası **liste sırasıdır** (katman → tanım sırası). Sıradaki projenin bağımlılığı bitmemişse **İLERİ ATLANMAZ** (`if (!ok) break`) — paralellik katman içinde doğal oluşur. Paralellik üst sınırı perf ayarından (2/4/6).
- **Hatalı bağımlılık ALT PROJELERİ BLOKLAMAZ:** bağımlılar son başarılı çıktıyla yine derlenir; kök hata adları `depIssue` olarak zincir boyunca aşağı taşınır. Bu projeler:
  - log başında warn satır(lar)ı alır: `warning: OSYS.Sales.Core failed in this run — last successful output referenced (yesterday 18:42)` (dolaylıysa: `warning: failure in dependency chain (Sales.Core) — referenced outputs may be stale`),
  - kartta/grafta üçgen rozet taşır, `dependency-affected` sayacına girer,
  - stream'de `built — dependency issue (2.4s)` olarak görünür; konsolda warn tonunda.
- Temiz projeler bağımlılıkları çözülünce **dalga dalga skip** edilir (tek seferde hepsi değil — tik başına ~3, all-clean'de ~12).
- Succeeded olan projenin will-dot'u griye (clean) döner — artık güncel.
- ETA: kalan iş / paralellik, üstel yumuşatma (0.75 eski + 0.25 yeni).
- **Log statüyle tutarlı:** succeeded projenin logu `Build succeeded — 0 errors, N warnings (4.2s)` ile biter; `error CS…` / `Build FAILED` satırları YALNIZ gerçekten başarısız olan projede görünür. Hero/temiz koşuda hiçbir yeşil projenin logunda hata satırı olmaz — statü ve log her zaman aynı şeyi söyler.

## 3.3 Seçim modeli

- Kart, graf düğümü veya stream satırına tıkla → **her yerde senkron seçim**: graf o düğüme kayar (460ms), liste karta kaydırır, konsol tam loga geçer (kaskat açılım), panel başlığı `← Back` moduna geçer.
- Aynı öğeye tekrar tıkla veya `Back` veya Esc → seçim kalkar; koşuyorsa follow-mode kaldığı yerden sürer. Sim/koşu seçimden etkilenmez.
- Esc önceliği: açık dialog → popover'lar/menü → seçim.
- Kısayollar: **F5 = Build**, **Ctrl+F5 (veya Shift+F5) = Rebuild**, **F1 = About**, Esc yukarıdaki gibi. (Gerçek uygulamada global hotkey RegisterHotKey — plan v6.)

## 3.4 Filtreler

Alt bardaki chip'ler ve şeritteki `+N more` liste filtresini kurar: `building` (queued dahil) · `succeeded` · `failed` · `skipped` · `dep` (depIssue olanlar). Aynı chip'e tekrar tıkla → temizle; `Σ` chip'i de temizler. Aktif filtre Projects başlığında kaldırılabilir chip.

## 3.5 Config / perf

- `Debug ↔ Release` (koşarken kilitli): boot değilse tümü dirty işaretlenir, konsola warn `Configuration → Release — all projects will rebuild`.
- `perf` chip döngüsü paralelliği anında değiştirir; koşarken konsola dim `parallelism: 6 (Full)`.

---

# 4. State (WPF/MVVM karşılığı)

- **Engine/VM durumu:** faz; proje başına `{status: discovered|queued|building|succeeded|failed|skipped, will: dirty|clean|unknown, startAt, doneDur, depIssue: string[]|null, log[]}`; willBuild kümesi; sayaçlar (building/succeeded/failed/skipped/queued, depIssueCount); elapsed + ETA; anlatı satırları `{type, time, text}`; stream olayları `{kind, time, project?, text}`; aktif satır.
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
| dep-affected (ortogonal) | ▲ üçgen | #ff706a | dependency issue / dependency-affected |
| will-build (ortogonal dot) | 8px nokta | amber/gri/hollow | — |

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
- Building efekti yalnız sabit "nefes"; süpürme/parlama denendi, İSTENMEDİ.
- Konsol/stream akışı klasik (en yeni altta); "en yeni üstte" İSTENMEDİ.
- Cull/agrega (çok-solution rollup) görünümü GEREK YOK.
- Emoji, gradient, amber dışı dekoratif renk, panel gölgesi, backdrop-blur YASAK.
- Varsayılan katman konfigürasyonu BOŞ (tek liste); örnek katmanlar yalnız "Load sample layers" ile.

---

# 9. Sürüm geçmişi

Her yeni ekleme/çıktıda sürüm artar ve bu bölüme ne değiştiği ayrıntılı yazılır. Sürüm numarası aynı zamanda uygulamanın About penceresinde ve `Copy diagnostics` çıktısında görünür (`prototype/app/BuildApp.jsx` → `ABOUT_VERSION`).

## 1.2.1 — 2026-08-05

**Logo kurumsal palete çevrildi (§6).** Mavi sürüm bırakıldı; logo artık DS'in kendi renklerinde: tile near-black (`#141417→#0A0A0C`, border `#2A2A30`), şeritler nötr rampadan (`#54545C / #3A3A42 / #EDEDEE / #A9A9B0`), chevron **amber** (`#FFB52E→#8B5907`). `app-icon.svg` kullanıcının verdiği haliyle birebir; `app-mark.svg` (uygulama içi) aynı palette düz şeritler + kısaltılmış amber gradient (`#FFB52E→#C9860C`) ile türetildi. `app-mark-mono.svg` değişmedi (zaten tek renk `#EDEDEE`).

**Sonuç:** logo arayüzle tek palette konuşuyor — title bar ve About'ta yabancı durmuyor. Karşılığında 1.2.0'daki "mavi yalnız logoya ait" kuralı düştü; yerine: **title bar'daki chevron accent ağırlığı taşıdığı için o bölgeye başka amber öğe konmaz.**

## 1.2.0 — 2026-08-05

**Yeni: ürün logosu (§6).** Build Orchestrator artık kendi markasını taşıyor — pill şeritler + yuvarlatılmış gradient chevron. Üç varyant üretildi: `app-icon.svg` (tile'lı, .exe/taskbar/bildirim), `app-mark.svg` (şeffaf, uygulama içi), `app-mark-mono.svg` (tek renk, tray). Kullanım matrisi ve renk kararları §6'da.

**Renk ayarı.** Chevron gradienti küçük boyda okunurluk için parlatıldı (tile: `#2E9BFF→#0B39C4`, uygulama içi: `#3D8BFF→#0B4FDF`); uygulama içi şeritler soğuk slate'ten DS'in nötr rampasına çevrildi. Karar: **mavi yalnız ürün markasına ait, amber tek UI accent'i olarak kalıyor** — logo mavisi arayüzde accent olarak kullanılmaz.

**Title bar logo kilidi (§2.1).** Sol üstte artık ürün markası (19px, tam renk) + ürün adı + ayraç + firma logosu (10px, %55) + repo bağlamı sıralaması var. Eskiden yalnız Delta wordmark'ı vardı. Firma logosu opsiyonel; yoksa ayraçla birlikte düşer.

**About başlığı (§2.10).** Kimlik bloğu ürün markasına (30px) geçti; sağa `LICENSED TO` + firma logosu bloklu bir kilit eklendi. Sonuç: iki logo tek kompozisyonda, ürün önde.

**Not:** `delta-app-icon.svg` artık uygulama ikonu değil — Delta'nın kendi ikonu olarak referansta kalıyor. Prototip kabuğunun başlık şeridindeki ikon da yeni `app-icon.svg`'ye çevrildi (18×18 kare slot → tile varyantı).

## 1.1.0 — 2026-08-04

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

## 1.0.0 — ilk handoff paketi

Ana pencerenin tamamı: title bar + görünüm modları (quad/list/focus) · sticky durum şeridi + 2px progress · dependency graph · projects listesi (tip-to-filter, Ctrl+F) · console (proje log seçimi, Copy log) · event stream · action bar (Build split-button: Build/Rebuild/Continue/Retry failed, statü chip'leri, Σ sayaç) · Settings dialog (katman tanımları, sürükle-bırak) · branch/worktree popover'ları · imleç yaşam döngüsü (canlı saatli boş prompt) · first-run boş durumu · warnings sayacı. Tasarım sistemi token'ları, statü tablosu ve bilinçli "yapılmayacaklar" listesi.
