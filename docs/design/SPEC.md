# Build Orchestrator — UI Tasarım Spesifikasyonu

> **Tasarım sürümü: v2.0.1** · tarih: 2026-11-24 · kaynak: uygulama 1.15.0 kodu — sürümler ve değişenler için → [VERSIONS.md](VERSIONS.md)
>
> 2.0.0 bir **milattır**: tasarım ve design system, yayındaki uygulamanın (**1.15.0**) kodundan baştan kuruldu. Bu dosya ve `prototype/` bugünkü uygulamayı anlatan tabandır — her metin, ölçü ve davranış koddan okundu. Bundan sonra tasarım yeni sürümlerde (2.1.0, 2.2.0 …) elle değişir; kod tarafı o sürümleri sırayla uygular (§9).
>
> **Hedef:** Bu tasarımı WPF (.NET 10) uygulamasında birebir hayata geçirmek. Claude Code'a bu dosyayı + `prototype/` klasörünü referans olarak ver.
>
> **Prototip notu:** `prototype/` yayındaki uygulamayı (**app 1.15.0**) izler. Son denetimde konsolun ve event stream'in metinleri ile tooltip'ler uygulamanın kaynağıyla tek tek karşılaştırıldı ve eşitlendi (konsolda `Engine ready — v1.15.0`, uygulamanın Sync transkripti, `build requested` / `rebuild requested` / `clean requested` / `cycles requested` + planlayıcı satırları, Stop satırları, pull ve harici güncelleme satırları; stream'de tek biçimli `Completed — …` ve `Stopped — N remaining projects queued`). Doğruluk kaynağı uygulamanın kodudur; README ile prototip uyuşmazsa uygulama kazanır. Prototipte hâlâ farklı ya da simüle olanlar (ayrıntı §9 v2.0.0):
> - **Yok (OS yüzeyleri):** tray ikonu ve menüsü, OS balonları, taskbar, başlık butonlarının sistem tooltip'leri, yerel klasör/dosya seçicileri (prototipte `Browse…` bir yol türetir), kurulum programı, global `Shift+Space` / `Ctrl+Shift+Space` · yakınsamayan döngü grubu (`cycle failed` / `cycle round cap reached` / ` (N stuck in a cycle)`).
> - **Basitleştirilmiş:** döngü grubu sabit planla iki turda yakınsar (tavan 3 yazılır), üyeler tek tek derlenir, taşınan üye (`carried`) yok · koşu simülasyonu sayıları ve süreleri sadeleştirir (`Stopped — N remaining projects queued`'daki N = derlenecek − biten; satırdan Clean anında, sabit `0.4s` ile biter; sahne 4'teki hızlı kontrol `checked in 0.0s` okur — uygulama ölçülen süreyi yazar) · anlatının geri yüklemesi yalnız bellekten (anlatı log dosyası yok) · kamera snap/etiket itme kuralları yok · katman başlığı klavyeyle de odaklanır (uygulamada yalnız fare) · Build menüsündeki Clean satırının tooltip'i native `title` (DS tooltip'i değil).
> - **Yalnız demo ile ulaşılan durumlar:** motor ölümü/sessizliği, yarıdaki git işlemi, kirli ağaç, stash ayarı, worktree kurulumu, Exit — `window.boDemo` kancaları (§7); "Demo:" ile başlayan "pencere gitti" yüzeyi (tepsi kipinde sahte görev çubuğu ve tray build göstergesi, §2.14).
> - **Yer tutucu metin (uygulama metni değil):** projelerin MSBuild çıktısı (uyarı kodları, `Build succeeded — …` / `Build FAILED — …` satırları — MSBuild'in kendi metni) · konsoldaki `git checkout <branch>` satırı · art arda ikinci Build'den önce yazılan `HEAD <sha> — N projects changed … since last build` notu · stash mesajının tırnak içi · güncelleme kartındaki 7 madde (`1.16.0` örnek teklifi) · Exit'teki süreç adları · bellek MB değerleri · demo disk klasörleri.

## Bu dosyalar nedir / ne değildir

`prototype/` altındaki dosyalar **HTML ile yapılmış tasarım referanslarıdır** — amaçlanan görünümü ve davranışı gösteren çalışan bir prototiptir, üretim kodu DEĞİLDİR. Görev: bu tasarımı hedef codebase'in kendi ortamında (**WPF/XAML**, CommunityToolkit.Mvvm) **yeniden yaratmak**. HTML/JS/React kodu kopyalanmaz; görsel değerler (renk, ölçü, tipografi), yerleşim, kopya metinleri ve davranış birebir taşınır.

Tek dosyalık prototip (lokalde üretilir, §7) herhangi bir tarayıcıda açılır ve canlı çalışır (simüle build). Üstteki sahne şeridi (Hero/Detail/Failure/…) **prototip iskelesidir, gerçek uygulamada yoktur** — gerçek uygulama penceresi, şeridin altındaki çerçeveli alandır.

## Fidelity: HIGH-FIDELITY

Piksel hassasiyetli mockup: renkler, tipografi, boşluklar, ikonlar ve etkileşimler **final**dir. Birebir uygulanmalı. Tek istisna: WPF'e çevrilemeyen web ayrıntıları (scrollbar stili gibi) en yakın native karşılıkla çözülür.

---

# 1. Tasarım Sistemi Özeti

Tam token seti: `docs/design-system/` (`tokens.css` — colors/typography/spacing/effects). WPF'te bunlar ResourceDictionary'ye çevrilir. Kritik değerler:

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
- queued `#7c7c84` / `#9a9aa2` / — / **`status-queued-border` `rgba(124,124,132,.24)`** (WPF `#3D7C7C84`; DS'te karşılığı yoktu, uygulamadan alındı — kuyruktaki glyph halkası)

**Statü noktası (v1.11.0 — tek kanal):** 8px daire; şerit/glyph/node ile **aynı statü rengini** taşır (amber=işaretli/derleniyor · yeşil · kırmızı · gri). Başlangıç modunda (v1.12.0) dolgu yok, **4 eşit yaylı** `--status-skipped-border` halka (tek 8px SVG: `r=3.2`, stroke 1.1, `stroke-dasharray 2.93 2.1`, opaklık 0.85); işlem başlayınca aynı SVG içindeki dolu daireye 380ms çapraz-sönümle geçer — eleman ve boyut değişmez, kayma yok. Ayrı bir will-build kanalı YOK — ortogonal amber/gri/hollow nokta kaldırıldı.

**Focus:** 2px `rgba(237,161,15,.50)` halka, offset 1px.

**Marka ara tonları (yalnız logo ve tray göstergesi):** `brand-strip-dim #44444b` (`app-mark.svg` şeridinin ara tonu) · `brand-chevron-deep #c9860c` (uygulama içi chevron gradyanının dip durağı, §6). UI accent'i değildir; başka bir yüzeyde kullanılmaz.

## 1.2 Tipografi

- UI = **Geist** (`'Geist','Segoe UI',system-ui`); makine çıktısı (console, süre, SHA, sayaç, yol) = **Geist Mono**, DAİMA tabular rakam. Mono asla dekoratif kullanılmaz.
- Ölçek: 2xs 11 · xs 12 · **sm 13 (BASE)** · md 14 · lg 16 · xl 20… Ağırlıklar: başlık 600, vurgu 500, gövde 400.
- Caps etiketler (panel başlıkları `DEPENDENCY GRAPH` vb.): 11px, 500, letter-spacing 0.07em, uppercase, `text-faint`.
- Satır yükseklikleri: tight 1.2 · snug 1.35 · normal 1.5 · mono 1.55 · **reading 1.62** (About paragrafı ve What's new maddeleri; 13px'te 21.06 — uygulamada `LineHeight.Reading13`, DS'te karşılığı yoktu).
- Ağırlık token'ları: body 400 · emphasis 500 · heading 600 · **console 300 (Light)** — konsolun ağırlığı ayrı bir token'dır (`FontWeight.Console`), mono'nun genel ağırlığı değil.
- Sayı biçimleri: süre `4.2s`, `1m 12s`; sayaç `14/38`; SHA 7 hane `a3f81c2`; ondalık ayracı nokta.
- **Konsol ağırlığı:** konsol gövdesi (anlatı + proje logu) **Geist Mono 300 (Light)**, 12px/1.55 — tek seferde yüzlerce satır bastığı için ince ağırlıkta daha rahat taranır. Diğer mono alanlar (event stream, sayaçlar, süreler, SHA) 400'de kalır.
- **Font notu (açık iş):** Geist şu an Google CDN'den; air-gapped paket için woff2/ttf dosyaları temin edilip gömülecek (kullanıcı kararı — geliştirme sırasında yapılacak). **Ayrıca:** geliştirmedeki konsol şu an sistem monosuyla (Consolas vb.) çiziliyor — bağlayıcı olan prototiptir: konsol da Geist Mono, ağırlık 300, 12px/1.55, tabular.

## 1.3 Boşluk / radius / elevation / motion

- 4px grid (`4/8/12/16/20/24/32…`). Satır 36px (compact 30). Titlebar 40 · statusbar 28.
- **Pencere ölçüleri (token):** ribbon 32 · global progress 2 · panel başlığı 28 · katman başlığı 24 · action bar 42 (DS `toolbar 44` değil — bar 1240'ta bu yükseklikle sığar) · pencere minimumu **1240×620**. Uygulamada `Size.RibbonHeight` · `ProgressHeight` · `PanelHeaderHeight` · `LayerHeaderHeight` · `ActionBarHeight` · `WindowMinWidth/Height`.
- Radius: kontrol 4 · kart/panel 6 · overlay 8 · chip/kbd 3 · **console 0 (keskin)**. Pencere kökü 8.
- Gölge YALNIZ floating overlay'de: `0 10px 28px -10px rgba(0,0,0,.66), 0 2px 6px -2px rgba(0,0,0,.5)`. Panel/kart gölgesiz — yapıyı 1px border taşır. (WPF tek katman ve spread'siz çizer: overlay depth 10 · blur 28 · .66, popover 6 · 18 · .6 — DS ile değer farkı, §9 v2.0.0.)
- Motion: 80/120/180/280ms; ease-out `cubic-bezier(.22,1,.36,1)` giriş, ease-standard `cubic-bezier(.4,0,.2,1)` durum değişimi, ease-in-out `cubic-bezier(.65,0,.35,1)` yer değiştirme. Bounce/overshoot yok; yalnız transform+opacity. Aynı anda en fazla 1 hero motion. **OS reduced-motion → tüm süreler 0** (uygulama içi toggle yok).

## 1.4 İkonografi

- Lucide geometrisi, 1.5–2px stroke, tek renk (currentColor), 12–16px. Emoji ASLA.
- Statü glyph'leri = ince halkalı daire içinde çizim: ✓ tik (success), ✗ çarpı (fail), saat (queued), kesikli daire (discovered). Uyarı üçgeni ayrı sabit slottadır — amber, cycle + dep birleşik (v1.7.0). `—` tire bir liste/graf statü glyph'i DEĞİL — yalnız event stream'deki atlama satırında görünür; satır, node, sayaç chip'i ve konsol başlığı taşımaz (§2.4, §2.5, §2.7, §5). Satır glyph'i satırın görsel durumudur: ✓ güncel/başarılı · ✗ bozuk · saat kuyrukta · spinner derleniyor · kesikli halka diğer her durumda.
- **Building spinner = discovered'ın kesikli halkasının amber, dönen hali**: `stroke-dasharray 2.3 2.5` dairesel halka, 1.4s lineer sonsuz dönüş. (Ayrı bir "spinner" çizimi değil — aynı halka döner.)
- Uyarı üçgeni: konturlu Lucide *alert-triangle* (stroke 1.7), **amber** `--amber-text` — satırın uyarı slotunda 12px, konsol başlığı rozetlerinde 8px, ⚠ chip'inde 12px. Kırmızı dolu üçgen kullanılmaz.
- Title bar küme ikonları (layout ×3, dişli, sparkle, info, power) aynı **16px** kutuda çizilir. Uygulamaya özgü ikonlar: *power* (Exit, stroke 1.7) · *folders* (worktree anahtarı) · *arrow-down-to-line* (`N behind`) · *rotate-ccw* (Restart engine). Paralellik ikonunun çizgileri piksel merkezinde (x = 7 / 13 / 19).
- Logolar: `assets/delta-logo-dark.svg` (title bar, 15px yükseklik), `assets/delta-app-icon.svg` (pencere/taskbar ikonu).

## 1.5 Dil ve ton

- **Tüm UI, proje adları ve loglar İNGİLİZCE** (ACME.Sales.Core, "Build", "Sync", "up to date"…). Kod yorumları Türkçe kalabilir.
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

- Sol — **logo kilidi**: sol kenardan 10px, `app-mark.svg` (ürün markası, 19px, tam renk) · 9px · `Build Orchestrator` (12px/500, `text-secondary`) · 9px · 1px × 13px `border` dikey ayraç · 9px · **firma logosu** (`delta-logo-dark.svg`, 10px, %55 opaklık). Title bar yalnız markayı ve uygulama düzeyi telemetriyi taşır; workspace adı alt barda (§2.7-5a), branch/worktree bilgisi alt bardaki chip'lerde.
- Sağ küme: `[Update <sürüm>] | [quad][list][focus] | [⚙][✦][ⓘ] | [⏻]  [— □ ×]`. Öğeler arası **1px**; her hairline 1×14px `border`, iki yanında **8px** (butondan 9px). Tüm küme ikonları aynı **16px** kutuda.
  - 3 layout ikonu (quad/list/focus, tooltip'li).
  - Dişli — Settings, tooltip `Settings — repository root and layer definitions`.
  - **Sparkle — What's new**, tooltip `What's new — release notes`; görülmemiş sürümde `What's new in <sürüm>` + 5px amber nokta. Kısayolu yok.
  - **ⓘ — About**, tooltip `About — version, shortcuts and diagnostics (F1)`.
  - **⏻ — Exit** (Lucide *power*, stroke 1.7), kendi hairline'ının arkasında kümenin son öğesi; tooltip `Exit — quit and end every process (Ctrl+Q)`. Kart açıkken buton `surface-overlay` + `text-primary` (amber değil) ve tooltip göstermez. Davranış → **§2.13**.
  - Pencere kontrolleri (— □ ×): her biri 46px genişlik, 10×10 glif 1px stroke; hover üçünde de `surface-raised` — **Close'un kırmızı hover'ı yok**; büyütülmüşken □ restore glifine döner.
- **Güncelleme hapı:** yalnız yeni sürüm inip kuruluma hazırken sağ kümenin **SOLUNDA** (arkasında 1px ayraç) `Update <sürüm>` hapı belirir; küme sağa yaslı olduğu için mevcut ikonlar kaymaz. Tıklayınca kart açılır → **§2.12**.
- **v1.25.0 — Kaynak şeridi (title bar'ın TAM ORTASI):** aracın makineye o anki maliyeti — uygulama + motor + derleyici süreçleri birlikte. Pencere genişliğine göre **absolute ortalanır** (sol küme, sağ küme ve Update hapı onu oynatmaz; WPF'te `Grid` ortasında `HorizontalAlignment=Center`, title bar sürükleme bölgesinde `IsHitTestVisibleInChrome=True` yalnız tooltip için). Üç hücre, hücre arası 14px; her hücre = 12px Lucide ikon (`text-faint`, stroke 1.8) + 5px + değer **Geist Mono 11px tabular `text-dim`**. Etiket yok; anlam tooltip'te (aşağı açılır).
  1. **CPU** — Lucide *cpu*. Bu aracın kullandığı CPU payı, tam sayı yüzde: `1%` · `69%`. Tooltip: `CPU used by Build Orchestrator — app, engine and compilers · cap 70%` (cap Balanced 70% · Light 40% · Full'da cap yok → ek kalkar). Cap şeridin metninde YER ALMAZ, yalnız tooltip'te; minik bar yok (hemen altındaki 2px global progress ile yarışmasın).
  2. **RAM** — Lucide *memory-stick*. 1 GB altı tam MB (`310 MB`), 1 GB ve üstü bir ondalık (`4.8 GB`). Tooltip: `Memory used by Build Orchestrator · app 310 MB · engine 85 MB · compilers 4.4 GB`.
  3. **Paralellik + mod** — üç dik çizgi (*tally-3* geometrisi, `|||`). Tek hücrede dört parça: **`koşan/koşunun paralelliği Mod · sınır`**. Pay = **o an gerçekten derlenen proje sayısı** (CANLI — motor durumu); payda = **koşunun oturtulmuş paralelliği** (koşu başında motorun makineye uydurduğu sayı, koşu boyunca sabit; koşu dışında 0); mod sözcüğü ve ` · ` ardından **modun sınırı** (Full 6 · Balanced 4 · Light 2). Boşta `0/0 Balanced · 4`; derlerken `4/4 Balanced · 4`, kuyruk boşalırken `3/4` → `1/4`; motor belleğe göre kısmışsa `2/2 Balanced · 4` — kısılmış koşu (`2/2`), yalnız iki projesi hazır tam koşudan (`2/4`) böylece ayrı okunur. Renk: sayı ve `/N` `text-dim` (mono); mod sözcüğü (Geist 11px), ` · ` ve sınır (mono) `text-faint`. Uyarı rengi YOK — bilgi, uyarı değil. **Koşu sürerken mod sözcüğü koşunun başladığı moddur**; perf çipinden koşu ortasında yapılan değişim yalnız bir sonraki koşuya geçer (hücre ve CPU tooltip'indeki cap değişmez); koşu yokken hücre çipi canlı izler. Tooltip: boşta `Balanced — up to 4 projects build in parallel` · derlerken `3 of 4 projects building in parallel` (payda koşunun sayısı) · kısılmışken `2 of 2 — reduced: 3 GB free memory` (ya da `… — reduced: 4 logical processors`). Mod sözcüğü yalnız okur; mod değiştirme alt bardaki `perf` çipinde kalır (§2.7-8, tooltip'siz).
  - **Sabit çerçeve:** her hücre en geniş değer kadar yer ayırır (`100%` · `12.3 GB` · `12/12 Balanced · 12`), değer ikonun yanında sola hizalı; `1%` → `69%`, `310 MB` → `4.8 GB` olurken hiçbir şey kaymaz. Değer değişiminde animasyon/geçiş YOK. Okunamayan değer `—` (aynı hücre genişliği). CPU ve RAM açılıştan **0.5 sn** sonra ilk örneği alır (o ana dek `—`), sonra 3 sn'de bir (Process sayaçları); pencere gizli ya da küçültülmüşken örnekleme durur. Motor uyurken (§2.9 *Stop the engine when idle*) motor ve derleyiciler 0 MB'tır, RAM yalnız uygulamadır. Paralellik + mod uygulama durumudur, anında güncellenir.
  - **Şerit yalnız okur:** tıklanmaz, hover zemini almaz, amber taşımaz (title bar'da accent ağırlığı logonun chevron'unda kalır — §6). Kuralın güncel hâli: **title bar = marka + uygulama düzeyi telemetri**; koşu bilgisi (§2.2) ve kontroller (§2.7) title bar'a çıkmaz.

## 2.2 Sticky şerit (32px + 2px progress)

Kalıcı durum satırı; `surface-base`, altta `border-subtle`.

- Solda **işlem pill'i** (19px, padding `0 7px`, max 280px, mono 2xs; uçuşta amber + 11px spinner, sonra nötr ve bir sonraki işleme dek durur). Sözcükler: `SYNC · BUILD · REBUILD · CLEAN · DEEP CLEAN · OPTIMIZE · RESOLVE · SWITCHING BRANCH · CREATING WORKTREE`. Pill, durdurma ve workspace işi dahil iş sürdükçe yanar; pull baştan `SYNC` yazar; **kendiliğinden Sync (commit, pencereye dönüş) pill yazmaz ve yakmaz**.
- Ardından faz metni — mono 12px, çoğu `▸ ` önekiyle. **Öncelik sırası** (üstteki kazanır): motor ölümü → motor sessizliği → Exit bekleyişi → motor uyanıyor → workspace yok → uçuştaki workspace işi → (uygulamada: Sync failed → Run failed) → faz satırı.
  - motor ölümü (kırmızı + ✗): `Engine stopped unexpectedly (exit -1073741819)` · `Engine missing — supervisor was not found next to the app · reinstall required` · `Engine could not start — … · reinstall required`
  - motor sessiz (amber, glyph yok): `Engine has stopped responding — no reply for 1m 30s · you can restart it`
  - Exit bekleyişi: `▸ Stopping — …` (§2.13)
  - motor uyanıyor: `▸ Starting the engine…` (ikincil, sweep)
  - workspace yok: `Not configured — repository root not set` (faint)
  - workspace işi (ikincil, glyph yok, sweep): `▸ Switching to <branch>…` · `▸ Pulling origin/<branch>…` · `▸ Creating the worktree…` · `▸ Deep clean — removing bin and obj…` · `▸ Optimize — repairing the workspace…`
  - hata (uygulamada, kırmızı + ✗): `Sync failed — <reason>` · `Run failed — <reason>`
  - boot: `▸ Waiting for Sync — project states appear after Sync` (dim)
  - syncing, adım adım: `▸ Sync…` → `▸ Sync — fetching origin/<branch>…` → `▸ Sync — analyzing 36 projects…` (`… the workspace…` / `1 project`) → kopyanın ilk Sync'inde `▸ Sync — indexing source files · d/t`
  - idle: `▸ Ready — 14 to build · 22 up to date` (uygulamada 0 projede `Ready — nothing to build`)
  - koşu istendi (planlama): `▸ Starting — resolving what to build` (ikincil, sweep)
  - running: `▸ Building 7/14 · 24s · ~35s left` (ETA <4s kala `· almost done`; ETA 5s'e yuvarlanır) · Build menüsü Clean'inde `▸ Cleaning 7/36 · 3s · ~5s left`
  - döngü grubu: bir grubun turu sürerken — düz Build, Rebuild ya da Resolve fark etmez — spinner'la `▸ Resolving cycles · round 1/3 · 2/7 · 4s` (`n/m` proje sayar, tur kalemi değil); Resolve'da üyelerden önce bayat upstream derlenirken `▸ Resolving cycles · preparing dependencies · 2/7 · 4s`. Tur dışında düz koşu `Building` satırına döner
  - stopping: `▸ Stopping — 7/14 · finishing 3 in flight` → `▸ Stopping — wrapping up`
  - stopped: `▸ Stopped — 7/14 · 7 not built` (dim)
  - done (başarılı): `Completed — 14 succeeded · 22 skipped · 1m 12s` (yeşil + ✓ glyph); koşu uyarı ürettiyse süreden önce ` · N warnings` (`… · 22 skipped · 3 warnings · 1m 12s`, hatalı biçimde de)
  - done (hatalı): `Completed — 5 failed · 12 succeeded (4 dependency-affected) · 17 skipped · 1m 30s` (kırmızı + ✗ glyph); uygulamada yakınsamayan bir döngü grubu varsa skipped'in arkasına ` (N stuck in a cycle)`
  - all-clean done: `Everything up to date — 36 projects checked in 8.4s, nothing to build` (yeşil + ✓)
  - Clean / Optimize / Resolve bitince şeritte **ayrı sonuç satırı yoktur** — bakım işinin ardından zincirlenen Sync şeridi devralır, Resolve normal faz satırına döner.
- Faz metninin yanında: **o an derlenen projelerin chip'leri** (spinner ikonu + kısa ad; en çok 4, fazlası `+N`); tıkla → proje seçilir.
- Sağda **hata kümesi** (yalnız hata varken): ✗ glyph + `5 failed` (kırmızı, 500) + `· 4 dependency-affected` (dim 11px) + ilk 3 hatalı chip + `+2 more` chip (kırmızı metin; tıkla → listede `failed` filtresi). ("View failures" butonu YOK — kaldırıldı.)
- ~~Döngü kümesi chip'i~~ **v1.11.0'da kaldırıldı** (turuncu UI'dan çıktı); döngü bilgisi satırdaki tek amber üçgende ve ⚠ filtresinde. Hata kümesindeki `N failed · N dependency-affected` sayaç metinleri de kalktı — ilk 3 hatalı chip + `+N more`. Hover tooltip iki satır: `In a dependency cycle — won't be built` + mono döngü yolu `Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts`. Küme Sync'ten koşu sonuna kadar kalıcıdır — döngü bir koşu sonucu değil, bir yapılandırma hatasıdır.
- Altında **2px global ProgressBar**: değer = tamamlanan/derlenecek; renk building=amber, failed=kırmızı (ilk hatadan itibaren), done=yeşil. **Indeterminate (sweep):** görünür Sync, `Starting`, motor uyanışı ve her workspace işi boyunca; ilk indeksleme sırasında sweep'ten çıkıp amber `d/t` dolar. Resolve'da biten/toplam. Radius 0.

## 2.3 Dependency graph (sol üst) — v1.3.0 "quiet graph"

Panel başlığı aynı (caps `DEPENDENCY GRAPH` + sağda mono sayaç). Panelin İÇİ yeniden tasarlandı: node üzeri ad etiketleri ve kalıcı bağımlılık çizgi ağı kaldırıldı — amaç 100+ projede bile sakin, tek bakışta okunan bir yüzey. Adlar hover tooltip'i ve seçim etiketiyle verilir.

### Yerleşim — katman bantları
- **Bant = listenin grubu** (aynı üyeler, aynı sıra — uygulamada `LayerGrouping`). Katman tanımı yoksa (varsayılan) **tek bant**; harici proje varsa en üstte `External` bandı; Settings'te katman tanımlıysa bantlar listedeki katman gruplarıdır. Bant asla bağımlılık derinliğinden türemez. Bant içindeki dizilim **build-order**dır (ilk derlenecek soldan başlar).
- Pitch (node adımı) otomatik: 44px'ten 5'e 0.5 adımla taranır; tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine sığan İLK değer seçilir → graf HER panel boyutunda tam sığar, scrollbar yok.
- Bantların eksik kalan son satırı yatay ORTALANIR; tüm blok panelde ortalanır — içerik payı her kenarda simetrik **36px** (hesap alanı W−72 × H−72; tooltip ve seçim etiketi de bu paya göre yerleşir).
- Node = kare kutu, boyut = pitch×0.6 (8–24px kelepçe), radius-sm, 1.5px border, içinde Lucide `box` glyph'i (node'un %52'si, **1.6px** stroke). Node'un altında panel renginde **opak taban** vardır — %12'lik dolgunun arkasından seçim kenarı görünmez.
- Statü görünümü: zemin `--status-*-soft`, border `--status-*`, glyph `--status-*-text`; **bilinmiyor** (hiç Sync yok) = kesikli `--border-strong` (desen `2 2`), **derlenecek** = düz gri. Renk ve border geçişleri **anında**dır; yalnız işaretleme dalgasında 200ms.

### Koşu yaşam döngüsü — soluk/parlak sistemi
- idle/boot/sync: tümü tam opak.
- Koşu başlayınca (phase=running): graf soluklaşır — queued/discovered opacity **0.13**; yalnız o an derlenenler tam opak.
- Proje bitince: sonuç rengine döner (succeeded yeşil / failed kırmızı) ve **1400ms tam opak KALIR**, sonra **700ms'de 0.2'ye** söner. Uygulama hilesi: opacity değeri anında 0.2 yazılır, beklemeyi CSS taşır → `transition: opacity 700ms var(--ease-standard) 1400ms` (timer/ek render yok).
- **Atlanan proje sessizdir:** kuyrukla aynı 0.13'te kalır, hiç parlamaz (atlanmak bir renk değildir — node kendi durum rengini taşır).
- Koşu bitince (done/stopped): tümü sonuç renginde tam opak.
- **Koşu grafta filtreyi yok sayar:** tıklamadan finalin sonuna dek opaklık filtresiz hesaplanır, sonra filtreli görünüme döner (uygulamada finalden 200ms sonra, 420ms'de). Liste koşu boyunca filtreli kalır.
- Renk geçişleri anında (yukarıda); opaklık geçişi 280ms (hold-fade hariç); filtre dimlemesi 420ms, iki yönde.

### Renk kuralı (v1.20.0 — tek kanal, çıktının durumu)
Node border'ı ve içindeki küp **aynı statü rengini** taşır; ayrı "plan" ya da "cycle" çekirdeği YOK. Görsel durumlar: **bilinmiyor** (kesikli border, gri küp — yalnız Sync yokken) · **güncel** (yeşil — aracın kendisi derlediyse ve girdi içeriği o derlemeyle hâlâ aynıysa, **ya da** hiç bu araçla derlenmemiş olsa bile diskteki derleme kanıtı kendi girdilerinin tümünden yeniyse) · **derlenecek** (gri) · **bozuk** (kırmızı, kanıtlı) · **işaretli** (amber) · **derleniyor** (amber + beads) · **sonuç** yeşil/kırmızı duruma yazılır ve kalır — bir sonraki koşuya kadar sabittir, "son işlemin hikâyesi" değil çıktının o anki durumudur. Aracın kendi derlediği çıktıda kanıt içeriktendir (girdi aynı kaldıkça yeşil kalır); dışarıda derlenmiş bir çıktıda içerik karşılaştırması yoktur — tek kanıt derleme zamanının kendi girdilerinden yeni olmasıdır. **Atlanmak renk değildir:** atlanan proje kendi durumunu taşır (güncelse yeşil); Rebuild'in zorladığı ama güncel olan proje de yeşil kalır; Build menüsünün Clean'iyle temizlenen proje gri (`never built`) olur. **Döngü üyesinde küp HER durumda amber** (bilinmiyor/derlenecek/derleniyor/sonuç) — satırdaki uyarı üçgeninin grafik vekili; çerçeve kendi durumunu taşır, küp döngü üyeliğini sürekli işaretler. Grafta ayrı bir uyarı üçgeni yoktur.

### İşlem koreografisi (v1.11.0 · kuyruğu v1.18.0'da değişti — "sıralı teslim dalgası")
Build/Rebuild/Clean/satır aksiyonu/Resolve — hangisi tıklanırsa aynı sıra oynar (§3.1, arşiv v1.11.0/4, v1.18.0): nötr an 440ms → **random dalga** (kapsam tek tek amber'a yanar; `markStagger` 110ms/node, toplam ≤1.1s; border + zemin + küp aynı `transition-delay`) → sarı-gri an 300ms → **grilerin vedası** (kapsam dışı 1120ms ease-in-out ile 0.18'e söner) → **teslim dalgası**: kapsam hep birden değil, **yandığı random sırayla** koşu seviyesine (0.13) iner — `settleStagger` = min(40, 700/(n−1)) ms/node gecikme, her node 400ms ease-in-out → **koşu bu kuyrukla örtüşerek başlar**: ilk derlenen node, son node'lar hâlâ sönerken tutuşur.
**Ölü bekleyiş ve koşu başındaki ikinci sönüm YOK (v1.18.0):** eski `wait`/`wait2` fazları (660ms) ve kapsamın 0.45'te bekleyip koşu başında 0.13'e atlaması kaldırıldı — kapsam doğrudan koşu seviyesine iner, faz değişiminde hiçbir opaklık zıplaması olmaz. Adım saatleri (gerçek zaman, W = `markStagger`×(n−1)+380): `wave` 440 · `hold` 440+W · `dimenv` 740+W · `settle` 1300+W · **koşu 2060+W**. Satır listesinde opaklık koreografisi yoktur (v1.13.2); satırlar yalnız dalga rengini taşır.

### Bitiş koreografisi (v1.11.0 — "neon tutuşma")
Koşu bitince, **yalnız grafta**: `hold` 900ms hepsi soluk (0.16) → `neon` yalnız derlenenler (succeeded ∪ failed) **random sırayla** düzensiz titreyerek tutuşur (`bo-neon` 1150ms, `endStagger` ≤150ms/node, zincir ≤1.5s) → `bwait` 700ms nefes → `grey` kalan tüm griler (atlanan + dokunulmamış) **hep birlikte** 980ms ease-in-out belirginleşir → final. Hızlı kontrol koşusunda ve derlenen yoksa çalışmaz; yeni işlem koreografiyi anında keser. `prefers-reduced-motion`: flicker kapalı, opaklık kademeleri kalır.

### Building animasyonu — "beads" (ölçüler v1.18.0)
- Derlenen node'un dışında, node ile eş-merkezli yuvarlatılmış-kare yörüngede dolanan sık amber noktalar.
- Çizim: SVG `rect` (fill none, stroke `--amber-text`, **stroke-width 1.6**, linecap round). Nokta deseni `stroke-dasharray: 0.01 (adım−0.01)`; adım çevreye TAM bölünür (aşağıda), ek yerinde bindirme olmaz.
- Hareket: `stroke-dashoffset` 0 → −çevre, **4000ms linear infinite** (sabit hız). Nokta adımı hedefi **4.4px**: adım = çevre / round(çevre/4.4).
- **Yörünge hücreye kelepçeli:** node'dan dışa boşluk `bgap = max(0.8, min(2.8, (pitch − node − 1.6 − 2) / 2))` (pitch = katman bandının hücre adımı, §2.5 yerleşim). Yörünge kutusu = node + 2×`bgap`; SVG kutusu = yörünge + 2×3.2 (stroke payı). Az projede kelepçe kapanmaz (2.8px dışta, eski yer); 200–300 projede node 8–10px'e inerken **mürekkep dahil komşu şeritle arada ≥2px boşluk** kalır — yan yana iki derlenen node'un şeridi değmez, node/küp/seçim border'ı değişmez.
- Yumuşak giriş/çıkış: yörünge SVG'si DOM'da sürekli durur, yalnız opaklığı değişir — building'e girişte **420ms ease-out** ile 1, bitişte **640ms ease-out** ile 0. Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup kaybolmaz.
- `prefers-reduced-motion`: beads ve akan çizgiler tamamen kapalı.

### Hover
- Node scale(**1.5**) (120ms ease-out), border 2px, opacity 1 (soluk moddayken bile), z-index öne.
- Tooltip: GECİKMESİZ; `--surface-overlay` + 1px `--border-strong` + radius-md (6) + padding `4px 8px` + popover gölgesi; Geist Mono **11px** `--text-primary`; içerik = TAM proje adı (örn. `ACME.Web.DealerPortal`). Yer: node'un boyalı yüksekliğinin yarısı + **6px** üstünde; kutunun tamamı içerik payının içinde tutulur (yatayda kaydırılır), üst bantta aşağı döner (flip). Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her zoom'da net).
- **Liste ↔ graf ortak hover:** listede bir satırın üstüne gelince node'u tam hover'ını gösterir (1.5×, tooltip); node'un üstüne gelince satırı hover zeminini ve ikon butonlarını alır. Kamera ve liste kaymaz; karşılık ekranda değilse hiçbir şey olmaz.

### Seçim — odakla & sığdır
- Node tıklaması genel seçim modeline bağlanır (§3.3: liste kartı seçilir, konsol o projenin loguna kaskatla geçer). Listeden/stream'den seçim de grafı AYNI şekilde odaklar.
- Odak: seçili node + doğrudan deps + doğrudan dependents'ın sınır kutusu panele sığdırılır → zoom = min(W/bw, H/bh), **0.7–2.6** kelepçe (padding = 3×node + 48px), merkez ortalanır, kamera **460ms ease-in-out** kayar. Yalnız pan değil zoom da ayarlanır (kullanıcı wheel ile uzaklaşmışsa bile).
- Görsel: odak kümesi tam opak, geri kalan HER ŞEY opacity **0.1**. Seçili node **hover görünümünü tutar** (1.5×, 2px border, öne alınır) + 2px `--focus-ring` outline (offset 2).
- Bağımlılık çizgileri YALNIZ seçimde: deps→node ve node→dependents, dikey kübik bezier, amber akan kesikler (`bo-edge-flow`: dasharray 4 8 → offset −24, 640ms linear infinite; 1.2px, opacity 0.75).
- Seçili node'un altında ad etiketi: mono 10px `--amber-text`, `--surface-overlay` zemin, 1px `--amber-border`, radius-sm, padding `2px 6px`; **her zaman node'un altında**, odak halkasının DIŞ kenarından 6px — kelepçe ya da flip yok (uygulamada kamera etikete yer açacak en küçük ötelemeyle itilir; prototipte bu itme yok); ekran koordinatında.
- Aynı node'a tekrar tıkla VEYA boş alana tıkla → seçim bırakılır, görünüm varsayılana döner (zoom 1, pan 0, 460ms). Seçim değişince hover temizlenir (odak kayması sonrası imleç altında bayat hover kalmaz).
- Gezinme ipucu (`scroll = zoom · drag = pan` / `click again to release`) YOK — panelde metin satırı ve alt rezervi yoktur.

### Serbest gezinme
- Wheel = zoom **0.7–5.0**, çarpan 1.14/adım, imlecin altındaki nokta sabit kalır; 160ms ease-out. (Native listener, `passive:false`.)
- Boş alanda sürükle = pan; imleç grab/grabbing; ≤3px hareket tıklama sayılır, üstü pan (drag sonrası bırakma boş-alan tıklaması TETİKLEMEZ). Sürükleme sırasında kamera transition'ı kapalı (birebir takip).
- İlk açılış (Sync sonrası): node'lar **derleme sırasıyla** belirir — `bo-reveal` (fade + 5px yukarıdan), gecikme = build-order index × 9ms; toplam ≤520ms — uygulamada n büyükse adım `520/(n−1)`'e küçülür, son node tam 520ms'de başlar (prototipte gecikme node başına 520'ye kırpılır); dalga üstten alta, soldan sağa akar.
- **Filtre:** graf dimlemesi listeyle AYNI eşleşmeyi kullanır (arama VE statü çipleri); eşleşmeyen node 0.1.

### Boş graf
Graf node taşımadığında — workspace yok (first run), Deep clean ya da Optimize sürüyor, Sync proje bulmadı, motor hazır değil — gövde yerine ortalı sabit blok: Lucide *network* 28px (stroke 1.4, `text-faint`, opaklık 0.7) · 10px altında `No graph` (12px `text-faint`). Başlıktaki sayaç gizli. Kesikli `Graph appears after Sync` kutusu YOK.

### Kaldırılanlar
- Node üzeri kısa ad etiketleri, kalıcı bağımlılık çizgi ağı, graf içi dep-issue rozeti (dep bilgisi kartlarda yaşıyor). Eski 26px `DependencyGraphNode` bileşeni graf panelinde artık kullanılmıyor (başka yüzeyler etkilenmez).

### Keşif sürerken — panel boş kalmaz (v1.24.0)
Sync'in keşif adımı sürerken (proje kümesi henüz bilinmiyor) graf gövdesi yerine aynı blok, metni `Graph appears once projects are discovered` (12px, `text-faint`). Başlıktaki `N projects · N dependencies` sayacı gizli. Animasyon yok. Ayrıntı ve tetik koşulu: §2.4, arşiv v1.24.0.

## 2.4 Projects listesi (sol alt)

- Panel başlığı: caps `PROJECTS` + mono `build-order` etiketi; aktif filtre varsa kaldırılabilir chip — etiketler ` + ` ile, tıklama sırasıyla değil barın sabit sırasıyla birleşir (`Building` + `Up to date` + `Failed` + `Warnings`, ör. `Up to date + Failed ✕`); tooltip `Clear the active filter`. Sağda filtre kutusu: `Filter…`, **160px**, temizleme ✕'i, `Ctrl+F` odaklar, Esc temizler ve bırakır.
- **Satır (36px, alt çizgi `border-subtle`):** soldan sağa:
  1. **Statü şeridi** 2px dikey (satırın tam sol kenarı; statü rengi). Seçiliyken 3px. **Bilinmiyor modunda düz** — `--status-skipped-border`, tam opak (v1.13.2; kesikli gradient tırtık yapıyordu, kaldırıldı). Satırın tam yüksekliğinde.
  2. **Statü noktası** 8px, şeridin **12px** sağında başlar — şeritle **aynı rengi** taşır (v1.11.0; ayrı plan/cycle kanalı yok), **bilinmiyor modunda** (yalnız Sync yokken) **4 yaylı halka** (v1.12.0, §1.1), işlem başlayınca dolu noktaya çapraz-söner. Tooltip taşımaz. İşaretleme dalgasında şerit ve node ile aynı gecikmeyle yanar.
  3. **Ad** 13px/500 — tek kural (v1.7.0): bu koşuda işi olan satır (dirty · queued · building · failed) `text-primary`, güncel/atlanacak satır `text-secondary` + yanında **sln adı** 12px `text-faint` (`Acme.Sales.sln`). Taşmada ellipsis.
  4. Sağ blok (min 134px, sağa yaslı — en uzun karar etiketi `modified · local` ve hover'daki 4 ikon buton bu yuvaya sığar): hover'da **4 ikon buton (v1.11.0)** — **play** (o projeyi derler — §3.8), **⋯** (satır menüsü: Build · Rebuild · Clean; satıra sağ tık da açar; koşu sürerken maddeler disabled, tooltip `Build in progress — wait or stop it first`), "Reveal in Explorer" (klasör) ve "Open in Visual Studio" (kod ikonu; proje birden çok solution'da listeleniyorsa seçici açar — aşağıda). İkon butonları **tooltip taşımaz** (yalnız native `title` + `aria-label`); tıklayınca konsola dim not düşer; hover yokken **karar etiketi**: mono 10.5px, sağa yaslı, projenin NEDEN derleneceğini (ya da neden atlanacağını) söyleyen sabit sözcükler — `modified` (kendi dosyaları değişti) · `modified · local` (kendi dosyaları değişti ve değişiklik commit'lenmemiş) · `affected` (kendi dosyaları aynı; bağımlılığı değişti, son başarılı derlemesi öbür configuration'da yapıldı **ya da** beslediği çıktının havuzdaki kopyası kendi derleme kanıtıyla uyuşmuyor) · `never built` (bu aracın bildiği bir derleme çıktısı yok; Clean'le temizlenen proje de) · `failed` (bu kaynakla hata verdi) · `up to date` (güncel, atlanacak — aracın kendi derlemesi de dışarıda yapılmış derleme de aynı sözcük). **Zaman kuyruğu YOK** (`· 2h`, `· just now` yazılmaz) ve `failed`'ın kuyruğu yok. Derlenecek satırlar `text-secondary`, güncel satırlar `text-faint`; `·` sonrası her zaman `text-faint`. **Statü rengi taşımaz** (kırmızı/yeşil yalnız glyph + şeritte — tek kanal), ikon taşımaz, DS Tooltip taşımaz: gerekçenin uzun hâli ikon butonlarıyla aynı dilde **native `title`** ile verilir — `No build output known to this tool` · `Failed at this source — Build will retry it` · `Up to date` · `Up to date — built outside this tool` · `Its own files changed since the last build` (commit'lenmemiş değişiklikte ` — includes uncommitted edits` eki) · `Its own files are newer than its build output` · `Its copy in the shared folder does not match its build output` · `Its own files are unchanged — a dependency changed` · `Its own files are unchanged — last built in Debug`. Canlı: başarı → `up to date`; Clean başarısı ya da kanıt sayılmayan hata → `never built`. **Son derlenen configuration:** araç her projenin son başarılı derlemesinin configuration'ını defterine yazar (Clean kaydı siler); Debug ↔ Release sonrası her şey kirli sayılmaz — kendi dosyası değişmeyen ama defterdeki configuration'ı farklı proje `affected` + `Its own files are unchanged — last built in Debug` yazar (geri dönüşte `… last built in Release`), kendi değişikliği olan `modified` kalır. **Dışarıda derlenmiş proje** (defterde kaydı yok, karar çıktının tarihinden): güncelse `up to date` + `Up to date — built outside this tool`; derlenecek bir bağımlılığın arkasındaysa `affected` + `Its own files are unchanged — a dependency changed`; kendi dosyası çıktıdan yeniyse `modified` + `Its own files are newer than its build output`; o configuration'ın çıktısı yoksa `never built` + `No build output known to this tool`. Araç onu bir kez derleyince defter kipine geçer. Etiket ana proje ve harici proje için AYNI (harici projeler `External` grubunda; rozet yok). Yuva: min 134px, hover'da 4 ikon butonla yer değiştirir, **satırlar arası sıçrama yok** (v1.7.0 kuralı). Karar bilinmiyorsa (Sync yapılmadı ya da branch değişti → `will: unknown`) yuva boş kalır. Commit çifti (`a3f81c2 → b7e91d4`) yok — gerekçesi arşiv v1.16.0'da.
  5. **Statü glyph'i** 14px = satırın **görsel durumu**: ✓ güncel/başarılı · ✗ bozuk · saat kuyrukta · spinner derleniyor · kesikli halka diğer her durumda (bilinmiyor, derlenecek, işaretli). `—` tire satırda YOK. **Tooltip YOK** (v1.11.0 — renk + glyph + süre kolonu aynı şeyi söylüyordu). Uyarılar yandaki slottadır.
  6. **Sabit 14px uyarı slotu**: cycle üyeliği ve/veya depIssue varsa TEK üçgen (12px), **her zaman amber** (v1.11.0 — turuncu/amber ayrımı kalktı). Uyarı **kümülatiftir** (v1.20.0): defterdeki bağımlılık notu durdukça üçgen kalır; bir sonraki koşu onu tek başına silmez, yalnız not düşünce kalkar. Tooltip **tek satır**: `In a dependency cycle` ya da `Dependency issue: Sales.Core +2`; gerekçe ve üye listesi **proje logunda**. Listede tooltip taşıyan tek öğe budur. Satırın kendisi building iken gizlenir. Slot her satırda var — **hiza asla bozulmaz**.
  7. **Süre** mono 12px sağa yaslı 46px: building=canlı sayaç, bitti=`4.2s`, yoksa `—`. Failed'da kırmızı.
- **Open in Visual Studio seçicisi:** proje tek solution'da listeleniyorsa VS butonu doğrudan açar ve konsola `<proje> opened in Visual Studio` notunu düşer. Birden çok solution'daysa (örnek veride `ACME.Security` dört, `ACME.Sales.Core` iki) butonun **4px altında, sol kenarına hizalı** bir seçici açılır: `Ds.Popover` kabuğu (`surface-overlay`, 1px `border-strong`, radius-lg, padding 8, overlay gölgesi), genişlik min 220 / max 360. Caps başlık `OPEN IN VISUAL STUDIO` (11px/500, `text-faint`, margin `4,2,4,8`); satırlar 28px, padding `0 6px`, radius-sm, hover `surface-raised`, mono 12px `text-secondary`, ellipsis — solution adı `.sln`'siz, workspace'in solution sırasıyla. Seçici panel tarafından kırpılmaz (WPF popup'ı gibi pencereye göre konumlanır): sağ kenarda sola kayar, altta yer yoksa butonun üstüne açılır. Seçim seçiciyi kapatır ve aynı notu yazar; dışarı tıklama kapatır. Esc kapatmaz ve klavye gezinmesi yoktur (WPF Popup `StaysOpen=False` yalnız dış tıklamayla kapanır; Esc zinciri onu saymaz). Seçici açıkken satırın eylem bloğu görünür kalır. Seçici yalnız satırdaki VS butonundan açılır — ⋯ menüsünde VS maddesi yoktur.
- **İşlem koreografisinde satırlar (v1.13.2'de sadeleşti):** satırlar dalganın RENGİNİ taşır, **opaklığı oynamaz** — sönme/geri gelme (veda + neon finali) yalnız graf node'larında. Satırda dalga: şerit, nokta ve **proje adı aynı anda** amber/parlak olur (ad rengine de `markOrder × markStagger` gecikmesi verilir; eskiden tüm adlar dalga başında birden beyazlıyordu). Eski davranış (kapsam dışı 0.3 / kapsam 0.45 iniş): koşu başlayınca tam opaklığa dönerler. **Bitiş koreografisi (neon) satırlara UYGULANMAZ** — koşu bitiminde liste sabit kalır (kullanıcı kararı).
- **Building satırı efekti:** kart zemininde hareketsiz amber "nefes" — `amber-soft` katmanı opacity 0→0.32→0, 3.8s ease-in-out sonsuz (tepe ~%3 görünür etki). Süpürme/parlama/kayma YOK (denendi, istenmedi).
- **Failed anı:** satır 360ms yatay shake (±3px), bir kez.
- **Sol şerit (v1.11.0, v1.12.0, v1.13.2):** her satırda görünür — **başlangıç modunda da tam opak, standart gri** (v1.13.2: 0.5 opaklık kalktı; Sync sonrası satır artık silik değil), işlem başlayınca renk DEĞİŞMEZ, işaretlenince amber, bitişte sonuç rengi. Sync şeridi soluk hâle döndürür (plan göstermez). Şerit 2px (seçilide 3px) ve satırın **tam yüksekliğinde** (dikey iç boşluk yok — uygulamanın bilinçli kararı).
- **Döngüdeki satırlar (v1.11.0):** ayrı renk YOK — şerit, nokta ve glyph normal statüyü izler; döngü üyeliği yalnız **amber uyarı üçgeni** + proje logundaki gerekçeyle ifade edilir. Düz Build ve Rebuild bayat bir döngü grubunu turlarla derler (§3.2); üyeler turlar arasında kuyruk glyph'inde bekler ve grup bitince her biri tek sonuç alır. **Resolve cycles** aynı turları yalnız grup ve bayat upstream'i için koşan dar biçimdir (§3.7).
- **Katman başlıkları:** 24px, caps 11px + mono sayı (satır adedi); **birikerek yapışır** — i'inci görünür başlık `top = i×24px`'e yapışır, alttakiler kaydıkça üsttekiler asılı kalır. **v1.17.0: başlık bir gezinme kontrolü** — hover'da zemin `surface-raised`, alt çizgi `border`, metin `text-secondary` (uygulamada 120ms), imleç pointer, native `title` = `Jump to <layer>`; **yalnız fare** (odak almaz, Enter/Space yok — uygulamanın kararı; prototipte başlık hâlâ odaklanır); tıklama o grubun **ilk satırını yığılmış başlıkların hemen altına** getirir: hedef `scrollTop = ilk satırın offsetTop − (başlık indeksi + 1) × 24`, smooth (reduced-motion'da anında). Seçim, filtre, konsol ve graf DEĞİŞMEZ — yalnız scroll. Katman katlama (collapse) yok.
- **Gruplama:** Settings'teki regex tanımlarıyla, ilk eşleşen kazanır; eşleşmeyen → `Other`. Harici projeler en üstte `External` grubunda. **Varsayılan: katman YOK → repository projeleri başlıksız tek grup, build sırasında** (harici varken bile `Repository` başlığı yok). Graf bantları bu gruplardır (§2.3).
- **Follow-mode:** koşarken ve seçim yokken liste frontier'i yumuşak takip eder (ilk building satırı görünür tutulur; scroll animasyonu 550ms'de bir, hedef sapması <54px ise dokunulmaz). Karta tıklayınca takip durur; seçim kalkınca sürer.
- **Boş durum — kurulum daveti (v1.8.0):** ortada `Configure the workspace` (14px/600) + açıklama `Set the repository root — and, if projects should be grouped, the layers. Discovery starts right after.` + **kurulum listesi** (292px kart, `surface` zemin + 1px border + radius-md; 30px satırlar, aralarında `border-subtle`): `Repository root` → mono `Not set` (`text-secondary`) · `Layers` → mono `N defined` ya da `Optional` (`text-faint`); altında **iki buton yan yana (v1.10.0)**: primary `Open settings` (dişli) + secondary `Import settings…` (upload) — ikincisi Settings'i açıp dosya seçiciyi hemen tetikler. Butonların altında 11px `text-faint` not: `Import fills the form from a settings file — nothing is applied until you save.` **Dosya seçtiren `Choose Folder` butonu KALDIRILDI** — başlamak için birden çok ayar gerektiğinden repository root artık Settings içinde (§2.9).
- Filtre eşleşmezse (çip ya da arama): her zaman `No projects match this filter.`
- **Boş liste:** satır yokken ve başka durum geçerli değilken (Deep clean / Optimize sürüyor, proje bulunamadı) ortalı blok — Lucide *list* 28px (graf bloğuyla aynı görünüm) · 10px · `No projects` (12px `text-faint`). Σ ve sayaçlar o sırada 0. Öncelik: kurulum daveti → keşif → boş liste → filtre satırı.
- **Keşif sürerken (v1.24.0):** liste yerine ortalı sabit blok — Lucide *list* 28px · `Discovering projects` (13px/500, `text-secondary`) · mono 11px ilerleme satırı `31 found · 29 repository · 2 external` (toplam `text-dim`, gerisi `text-faint`; harici proje tanımlı değilse yalnız `29 found`). Başlıkta arama kutusu ve `build-order` etiketi gizli. Ayrıntı: arşiv v1.24.0.

## 2.5 Console (sağ üst)

- Zemin **`#060608`, radius 0**, padding `8px 12px 14px 12px` (alt 14px: prompt imleci yatay scrollbar'a yapışmasın). **Mono 12px, ağırlık 300 (Light)**, satır 1.55. **Satırlar SARMAZ:** uzun satır tek satırda kalır, gövde yatay kayar (yatay tekerlek/touchpad, ←/→).
- **Panel başlığı** (`PanelHead`, 28px — arşiv v1.18.0 kabuğu):
  - **Anlatı:** caps `CONSOLE` · sağda mono `N lines` (anlatının TÜM satır sayısı, çizilen dilim değil) · kompakt kip etkinken sayının 8px sağında mono 11px `text-faint` **`compact`** etiketi (workspace yokken gizli).
  - **Proje logu:** `Back` (ghost sm, ok ikonu, −6px optik hiza) · tam proje adı (mono 12px `text-primary`, ellipsis) · (varsa) `dependency issue` ve `dependency cycle` rozetleri (**8px** konturlu üçgen + 5px + 11px `amber-text`; tooltip'ler altta: `Dependency issue: <kısa adlar> — last successful output referenced` · `In a dependency cycle`) … sağda `Copy log` + `N lines` (boş logda `0 lines`). **Statü glyph'i ve statü sözcüğü YOK** — liste ve graf zaten gösteriyor; `compact` etiketi proje sayfasında gizli.
- **İki mod:**
  - **Anlatı (seçim yok):** satır = **yalnız metin** — saat sütunu ve `▸` ikon kolonu YOK (v1.6.0); tüm satırlar imleçle aynı sol hizada başlar. Canlı gelen satırlar **anında** basılır — daktilo yok. Projelerin MSBuild satırları da anlatıya akar (kompakt kurala göre, aşağıda). En altta tek **prompt satırı**: yanıp sönen blok imleç (7×13px, 1.1s blink); yanında `ready` (dim) **yalnız anlatı boşken** — ilk satır geldiği andan itibaren yalnız imleç kalır. Uygulamada imleç yalnız pencere aktifken yanıp söner ve `⌄ latest` görünürken gizlenir.
  - **Seçili proje logu:** projenin tam logu; salt okunur, sonda imleç ya da `build in progress` işareti YOK.
- **Satır rengi metinden gelir** (biçimi bilinen satır renklenir, gerisi tek ton): `[error]` öneki ve MSBuild kanonik `… : error CS0103:` → `status-fail-text` · `warning:` öneki, kanonik `… : warning MSB3277:` ve kompakt özet satırı → `amber-text` · `git ` / `msbuild ` / `nuget ` / `dotnet ` ile başlayan komut satırı → `text-primary` · geri kalan her şey → `text-secondary` (dim notlar `text-faint`). **Yeşil `success` satır türü YOK**; serbest metinde `failed/succeeded` aranmaz.
- **Anlatının satır sözlüğü (uygulamanın metni):**
  - motor hazır: `Engine ready — v1.15.0` — açılışta ve Restart engine sonrası; açılış Sync'i bu satırın altına eklenir. Elle Sync ya da Build konsolu temizler ve bu satırı yazmaz.
  - Sync transkripti: §3.1.
  - koşu isteği: `build requested` · `rebuild requested` · `clean requested` · `cycles requested`; satırdan tek projede `build requested — ACME.Sales.Core (single project)`. Ardından planlayıcı satırları: `Scanning solutions (7)` → `Reading HintPath/Compile items (36 projects)` → `Dependency graph — 1 cycles` → `Build order resolved (36)` → `Computing incremental state (36 projects)`.
  - harici güncelleme (*Pull before build* açıkken, kopya başına): `Updating external '<name>'` → `Updated external '<name>' → <rev>`; git çalışma kopyası bulunamayan kök `warning: external '<name>': no git working copy found above its path — building as-is`; anahtar kapalıyken satır yazılmaz.
  - Stop: basışta `stop requested — no new projects will start; 3 in flight will finish`; Stop now'da `stop now requested — in-flight compiles will be terminated`, ardından `stopped — 3 in-flight compiles terminated`.
  - **Koşunun özeti konsola yazılmaz** — şeritte (§2.2) ve stream'de (`Completed — …` / `Stopped — …`, §2.6) yaşar. `Build complete — …`, `Build failed — …`, `Build stopped — …` gibi bir anlatı satırı yoktur.
- **Kompakt konsol** (Settings → General → CONSOLE, varsayılan açık): anlatı her projeden `building <name>` başlığını (projenin ilk satırından önce), tüm hataları, motorun kendi `warning:` notlarını ve proje başına **ilk 3 farklı uyarıyı** alır; tekrarlar düşer. Gizlenen uyarılar proje bitince tek amber satırda toplanır: `<name>: 25 more warnings — CS1591 ×20, MSB3277 ×5 — see the project log` (en sık 3 kod, eşitlikte alfabetik; kodsuz uyarı `other`; tekilde `warning`). Kapalıyken her satır. Kip her işlemin başında dondurulur; boştayken Save'de yeniden okunur. Proje logu her zaman tamdır.
- **Sabitleme:** **anlatı dibe sabitlenir ve takip eder**; dikey bir okuyucu jesti (tekerlek, dikey bar, PageUp/PageDown, ↑/↓, Home/End) paneli **5 sn** okuyucuya verir — bu sürede gelen satır görünümü çekmez, süre dolunca dibe dönüp takibe geçer; yana jest takibi bozmaz. **Proje logu TEPEDEN açılır ve asla takip etmez** (ilk hata aranır); gelen satırlar görünümü kaydırmaz.
- **`⌄ latest` pill:** okuyucu dipten ≥48px uzaktayken panel alt-ortasında: 22px, padding `0 9px 0 7px`, **radius 8** (`radius-lg`), `surface-overlay` + `border-strong` + popover gölgesi, ⌄ **metin glifi** (U+2304) + 5px + mono 11px; hover `surface-raised` + `text-primary` (120ms); **giriş animasyonu yok**. Tıkla → bir kez dibe iner (uygulamada 280ms ease-in-out kayış; okuyucu jesti kayışı keser) ve proje logunda orada bırakır.
- **Klavye (gövde odaktayken):** PageUp/PageDown bir viewport, ↑/↓ bir satır, ←/→ (ve Ctrl+←/→) yana bir adım; Home tepe + sol kenar, End dip (yatay konum korunur). Görünümü kaydırır, gizli caret'i değil.
- **Görünüm değişimi = düz kayma.** Proje logu açılırken gövde tek parça **14px aşağıdan** kayarak + opaklıkla gelir, `Back` dönüşünde **14px yukarıdan** (ayna) — **340ms ease-out**. Perspektif/`rotateX` tilt YOK (uygulamada 4K'da GPU belleği bağlıyordu). Başlık geçişe katılmaz, içeriği anında değişir. Yeni bölüm ve gizli pencerenin dönüşü kaymasız kurulur.
- **Boş proje sayfası** satırın durumundan türeyen 1–2 satırdır (gerekçe + kanıt; statü, yaş, sha yazılmaz): `Up to date — nothing to compile in this run.` / `Up to date — nothing to compile.` · `Queued — this tool has never built it.` / `Will build — it failed at this source.` / `… — the signature changed since the last successful build.` (defter kipinde configuration değişmeden bayatlayan) / `… — it was last built in Debug.` (yalnız configuration farkı) · dışarıda derlenmiş proje: `Up to date — built outside this tool.` · `Will build — one of its dependencies will be rebuilt first.` · `… — its files are newer than its build output.` · `… — its build output is missing.` · `In a dependency cycle — this run did not compile it.` · `Not analysed yet — run Sync to see what this project will do.` · `Built in this run — its log is no longer on disk.` / `Failed in this run — its log is no longer on disk.`. İkinci satır kanıttır: dışarıda derlenmiş ve güncelse `Built outside this tool`, defterde kaydı yoksa `Never built by this tool`; gerekçe aynı şeyi söylüyorsa yazılmaz (`… never built it.`, `… built outside this tool.`, `… build output is missing.`). Dışarıda derlenmiş proje bu koşuda atlandıysa `Up to date — nothing to compile in this run.` + `Built outside this tool`. Derlenen ama henüz satırı olmayan: `No log yet — output streams here once the build starts.` (uygulamada `ConsoleEmptyState`).
- **Anlatı penceresi:** anlatı 200 satırlık bir penceredir, sınır değildir. Takipteyken son 200 satır çizilir; okuyucu uzaktayken pencerenin ilk satırı kimliğiyle sabitlenir — yeni satırlar alta eklenir, görünüm kaymaz. Görünüm tepeden **48px**'ten uzaklaşınca yükleyici silahlanır; silahlıyken tepeye varış (kaydırma, Home ya da Ctrl+Home) bellekteki geçmişten (son **2 000** satır) önceki 200'lük dilimi ekler ve görünümü aynı satırda tutar. Mod değişiminden sonra yükleyici yalnız görünüm tepeden uzakta kaldıysa silahlıdır. `⌄ latest` takibi açar ve pencere yeniden 200'e iner. `N lines` anlatının tamamını sayar. Proje logu penceresizdir. (Uygulamada geçmiş ayrıca koşu klasöründeki anlatı log dosyasına yazılır; prototipte yalnız bellek.)
- **İmleç ve satır hover (v1.17.0):** gövde imleci **standart ok** (`default`) — metin I-beam'i istenmedi; el işareti yalnız tıklanabilir öğelerde. İmlecin altındaki satır bir yüzey adımı açılır: `surface` (#141417) zeminli **tam genişlik bant**, **120ms** geçiş (bant satırdan satıra anında atlar). Renk, hiza ve seçilebilirlik değişmez.
- Akış yönü **klasik: en yeni altta** (her iki panelde; "en yeni üstte" değerlendirildi, İSTENMEDİ).

## 2.6 Event stream (sağ alt)

- Panel başlığı: caps `EVENT STREAM`; sağda mono `N events`.
- **Satır = üç kolon** (mono 12px): saat · 12px glyph kolonu · metin. Uzun metin **kendi kolonunda sarar** — devam satırı saatin değil metnin başlangıcının altına gelir; saat ve glyph ilk satırın 24px bandında ortalı kalır. Sığan satır 24px; her sarma bir metin satırı ekler (üst/alt 2px iç boşluk). Aktif (derlenen) satır ve prompt aynı düzeni kullanır; prompt sarmaz.
- Glyph ve renk türe göre: ok=✓ + `text-secondary` · fail=✗ + `status-fail-text` · skip=— + `text-dim` · sync/info=amber `▸` + `text-dim` · **warn=amber `▸` + `amber-text`** (konsolun `warning:` rengi; projesi varsa tıklanır) · done=✓/✗ + `status-success-text` / `status-fail-text`.
- **Renk kuralı:** renk kıt bir kaynaktır, yalnız üç tür satıra verilir — (a) dikkat gerektiren **hata**, (b) bölüm kapatan **özet**, (c) **uyarı** (warn — reddedilen git işlemi, değiştirilemeyen dosya). Tekrar eden başarı satırları nötr (`text-secondary`) kalır; statüyü satırın kendi **glyph'i** taşır, metin taşımaz. Glyph rengiyle metin renginin eşleşmesi gerekmez (yeşil ✓ + nötr metin normaldir). Skip satırları geri plandadır ama **okunur** kalır (`text-dim` #76767e, `text-faint` değil): "neden derlenmedi" en sık sorulan sorudur. Saat sütunu her satırda `text-faint`, olay türünden bağımsız. Değerlendirilip alınmayan iki alternatif — her satırın kendi statü renginde olması (özet satırı üstündeki yeşillerin arasında kaybolur, kırmızı tekliğini yitirir) ve proje adı `text-primary` + kuyruk `text-dim` (satırda üç gri tonu, konsolla yan yana kalabalık).
- Örnek metinler: `ACME.Domain.Service built (2.9s)` · `ACME.Sales.Core failed — 2 errors (3.1s)` · `ACME.Base skipped — up to date` · `Sync — 14 to build, 22 up to date` · `Build started — 14 projects, parallelism 4` · `Completed — 5 failed · 12 succeeded · 17 skipped · 1m 30s · 4 dependency-affected` · `Stopped — 7 remaining projects queued` · `Build started — ACME.Sales.Core (single project)` · `Clean started — 36 projects, parallelism 4` · `Sync — 0 to build, 36 up to date` · `ACME.Parts.Api skipped — in dependency cycle` (derlenmeyen kirli döngü üyesi).
- **Tek biçimli kapanış:** her koşu — Build, Rebuild, Build menüsünün Clean'i, satırdan tek proje, Resolve ve hızlı kontrol — `Completed — [f failed · ]s succeeded · k skipped · t[ · N dependency-affected]` ile kapanır; durdurulan koşu her zaman `Stopped — N remaining projects queued` yazar. `Clean complete — …`, `Everything up to date — …` gibi ayrı stream biçimleri yoktur. Harici güncelleme stream'e satır yazmaz.
- **Warn satırları:** `branch switch refused — 3 uncommitted files` · `pull refused — uncommitted changes` / `pull refused — branch diverged` (uygulamada ayrıca `… — not on a branch`) · `ACME.Client.Sales in use by ACME.Client.exe — not replaced; turn on Replace files in use` (kısa in-use notu; *Replace files in use* açıkken `… — replaced`, info). Uygulamadaki diğer satırlar: `waiting for git — <op>` · `interrupted by branch change` · `N run-start warnings — see the console`.
- **Döngü satırları** (düz Build, Rebuild ve Resolve aynı satırları yazar): her tur başında `cycle round 1/3 — 3 members` (tekilde `1 member`; info, liderde tıklanır) · aktif satır `X building… · member 2/3 · round 1/3` · grup bitince her üyeye tek `X built (t)` (turlarının toplamı) ve ardından grubun hükmü `cycle converged — 3 members, 3 compiled, 2 rounds · 6.2s` (ok, liderde tıklanır). Üyelerin tur sonuçları ayrı yazılmaz. Resolve ayrıca `Cycles started — 3 cycle members · 1 prerequisites · up to 3 rounds` (info) ile açılır ve `Completed — …` (done) ile kapanır; ön koşullar normal `X built (t)` yazar. (Uygulamada ayrıca `cycle failed — …` · `cycle round cap reached — …`.)
- **Bakım işleri tek kapanış satırı yazar** (sync tonu; adım satırı ve canlı daktilo satırı yok): `Clean — 36 projects, 301 folders, 4.4 GB freed` · `Optimize — 4 restored, 2 obj cleaned, 41 cache entries pruned, 1.1 GB reclaimed` (uygulamada iş yoksa `Optimize — nothing to fix`).
- **En yeni satır daktiloyla yazılır**; ama sık ardışık olaylarda (<340ms) ve hata/özet olaylarında ANINDA basılır. Aktif satır: `ACME.Server.Api building…` — saat + **imleç** + amber daktilo metni (konsolla aynı dil).
- **Bekleyen prompt** (saat + imleç) her zaman en altta durur — workspace yokken de; `No events yet.` YOK. İmleç dinlenmede amber, renk turu yok (uygulamada yazılan satırın ikon rengini alır).
- Projeli satırlar tıklanabilir → seçim; seçili satırda sol 2px amber şerit + `surface-raised` zemin.
- **Hover her satırda (v1.17.0):** tıklanabilir (projeli) satır `surface-hover`, tıklanamaz satır bir adım daha sessiz `surface`; imleç tıklanabilirde pointer, diğerinde default — satırın tıklanabilirliği hover'dan okunur. Seçili satırın görünümü değişmez.
- Tümü başarılı biten koşuda done satırı bir kez yeşil parlar (background `success-soft` → transparent, 1.1s).
- Alta yapışık scroll + `⌄ latest` pill (≥48px; konsolun pill'iyle aynı).

## 2.7 Action bar (42px, altta)

`surface` zemin, üstte 1px `border`. Soldan sağa:

> **Hover dili — v1.17.0 (bardaki TÜM kontroller için tek kural):**
> - **Nötr kontrol** (Sync · bakım ikonları · sayaç/filtre çipleri · `N behind` · branch/perf): zemin `neutral-700` (#2a2a30), border `neutral-500` (#54545c), **metin VE ikon** `text-primary` — üç kanal birlikte değişir, kontrol türüne göre fark yok. Statü glyph'i (✓ ✗ —) kendi statü rengini korur (renk statü taşır).
> - **Açık/aktif kontrol** (açık filtre çipi, açık popover, yürüyen Sync / bakım görevi / Resolve, açık worktree kopyası): zemin `amber-soft-hover`, border `amber` — aktif okuması hover`da kaybolmaz.
> - **Segment (`Debug | Release`):** yalnız seçili OLMAYAN seçenek hover alır (`surface-raised` + `text-secondary`); seçili seçenek `surface-overlay`da kalır.
> - Boyut, radius ve renk tonu değişmez (DS kuralı: hover = bir yüzey adımı), geçiş 80ms; disabled kontrol hover almaz.
> - **Build / Stop bu kuralın DIŞINDA:** kendi variant hover`ında kalır — bar`ın tek yüksek sesli kontrolü odur.
>
> Prototip notu: DS bileşenleri hover`ı inline yazdığı için kural `[data-bo-actions] [data-bo-hov="off|on"]` seçicileriyle ve `!important` ile kurulur (`bo-app-css`).

1. `Sync` — secondary sm buton (döngü ikonu). **Bir Sync uçuştayken** (zincirlenen dahil) amber `active` zemin + ikon yerine spinner, etiket `Sync` kalır, disabled soluklaşması uygulanmaz (koşan iş, butonu neredeyse amber'dir). Diğer zamanlarda Sync başlayamıyorsa (workspace yok, motor hazır değil, başka iş sürüyor) disabled.
2. **Bakım grubu (Clean / Optimize / Resolve cycles)** — chip ağırlığında tek kutu: 24px yükseklik, `surface-raised` zemin, 1px `border`, radius-xs, `overflow:hidden`; içinde üç 28×22 ikon buton, aralarında 1px×14 `border` ayraç. **Etiket yok** (bar 1240'ta ancak böyle sığar) — anlam tooltip'te: `Clean — remove every project's bin/ and obj/ and reset the build state; the next build compiles everything from scratch` · `Optimize — restore missing NuGet packages, report references that restore cannot fix, clean stale obj leftovers and prune dead cache entries` · `Resolve cycles — build the 3 cycle projects in repeated rounds: stale references first, then rebuild until they converge` (birden çok grup varsa ` (K separate cycles)`; kapsamda bayat upstream varsa ` · U upstream to build first` eki — sayı Resolve'un kendi kapsamından; döngü yokken `Resolve cycles — no dependency cycles detected` + disabled). İkonlar Lucide: **eraser** (Clean), **gauge** (Optimize), **unlink** (Resolve), 12px, currentColor. Yürüyen görevin/Resolve'un butonu `active` olur ve ikon yerine **amber spinner** döner; diğer kontroller o sırada kilitli. Davranış → §3.4, §3.7.
3. 1px dikey ayraç.
4. **Sayaç chip'leri — ÇOKLU filtre** (`filter` bir Set; çipler bağımsız açılır-kapanır, seçili küme **VEYA** ile birleşir — seçili chip'ler **durum filtreleridir**; arama ile VE). Aktif çip **kendi statü renginde** yanar. Set: `Σ 36` · spinner+`4` (building; boşken gri nokta) · `✓ 14` · `✗ 5` · `⚠ 3` (⚠ yalnız > 0 iken). Sayılar **satırın görsel durumundan** gelir, koşu sonucundan değil: ✓ = yeşil görünen satırlar, ✗ = kırmızı görünen satırlar, building = yalnız şu an derlenenler (kuyruk sayılmaz), ⚠ = döngü ∪ bekleyen bağımlılık notu. Σ değeri `text-primary`, ikonu `text-dim`; workspace yokken ve liste boşken (Deep clean / Optimize) Σ ve diğerleri 0. Tooltip'ler (ekran okuyucu adıyla aynı): `All projects — clear filters` · `Building — filter` · `Up to date — filter` · `Failed — filter` · `In a dependency cycle or waiting on a dependency — filter`. **`○` (to build) ve `—` (skipped) chip'i YOK** — derlenecek sayısı ribbon'da (`Ready — N to build`), "N skipped" koşu özetinde (§2.2); `○` chip'i uygulamada kullanıcı kararıyla kalktı (§8).
5. Esnek boşluk.
5a. **Workspace adı:** repository kökünün **klasör adı** (`ACME`; örnek kök `D:\src\ACME`), mono 11px `text-dim`, max 160px + ellipsis; tooltip = **yalnız aktif kök** yolu (worktree açıkken worktree yolu), önek yok.
5b. **Worktree anahtarı** — etiketsiz **24×24 kare chip**, Lucide *folders* (iki klasör), workspace adı ile branch chip'i arasında. Worktree kopyası açıkken **amber** (chip'in açık/aktif hâli). Popover YOK. Tooltip bir sonraki tıklamanın ne yapacağını söyler (disabled iken de görünür): `Set up a worktree — a second working copy for another branch` (klasör ayarlı değil → tıklama Settings › Workspace'i Worktree folder alanına odaklı açar; Save worktree'yi kurar ve içine geçer) · `Switch to the worktree — <folder>` · `Back to the repository — <root>`. Debug/Release ile aynı kapı (geçiş bir Sync başlatır); workspace yokken ve **Restart engine görünürken gizli** (1240px sığma). Geçiş bir kök değişimidir: konsol temizlenir, ilk satır `Switched to the worktree — <path>` / `Switched to the repository — <root>`, liste ve graf baştan (keşif → reveal); o kopyanın ilk Sync'i dosyaları indeksler (§2.2). Ekran okuyucu adı `Worktree`.
6. `branch main ▾` chip (branch ikonu + etiket + mono değer + chevron) → **Branch popover** (§2.8); seçim gerçek `git checkout` — gösterilen branch her zaman aktif kopyada checkout edilmiş branch'tir. Kapı: workspace, motor hazır, başka iş yok, git işlemi yarıda değil. Git işlemi yarıdayken (merge/rebase/cherry-pick/revert yarım kaldıysa ya da `index.lock` var, §3.9) değerin yanında **6px** amber nokta + tooltip `Merge in progress — finish or abort it in git` (işlem adıyla); checkout ve pull o sırada kilitli. Kirli ağaçta branch kartı açılır (§2.8).
6a. **`3 behind` chip** — branch chip'inin hemen sağında, **`behind > 0` olduğu her an** görünür: Lucide *arrow-down-to-line* (12px) + mono sayı (chip'in metin rengini alır) + `behind`. Nötr chip ağırlığı (amber yok). Çevrimdışı (fetch degraded) → sayı bilinmez → chip yok; güncel → chip yok. Branch chip'iyle aynı kapı; git işlemi yarıdayken tooltip onun nedenini yazar. **Tıklama = ff-only pull** (→ §3.9). Tooltip: `Fetch found 3 new commits on origin/main. Click to fast-forward; nothing is merged or rewritten.` Ekran okuyucu adı `3 behind`.
7. `Debug | Release` segment (sm) — değiştirmek bir **Sync** başlatır: konsol ve stream temizlenir, ilk satır `Configuration → Release`, liste ve graf baştan başlar. Yalnız bir Sync başlayabiliyorken açık; değilse soluk ve tıklanmaz.
8. `perf: Balanced` chip — tıkla döngü: Full(6) → Balanced(4) → Light(2) paralellik. Koşu sırasında değişim bir sonraki koşuya geçer (§2.1). Tooltip YOK (istenmedi).
9. 1px ayraç.
9a. **Restart engine** — yalnız motor öldüğünde ya da sustuğunda (ve yeniden başlatılabilirken — supervisor eksikse yok): 24px kare secondary sm buton, Lucide *rotate-ccw*; ikon ribbon satırının rengini alır — **ölümde kırmızı** (`status-fail-text`), **sessizlikte amber** (`amber-text`). Tooltip `Restart engine — start a fresh build engine; the current one stopped or is not answering`. Görünürken worktree anahtarı gizlenir.
10. **Build split-button** (primary md, play ikonu): sol `Build` (F5, **stale set**) + sağ `▴` ok → yukarı menü (276px), her fazda aynı **üç madde**: `Build` — `Only stale projects` — `F5` · `Rebuild` — `All 36 projects — cache ignored` — `F6` · `Clean` — `Remove build outputs — next build is full` — `F7`. Açıklamalar sarmaz. Clean satırının tooltip'i `Clean — msbuild /t:Clean on every project; caches are untouched`. Menü ikonları satır menüsüyle aynı ailedendir (play · rotate-cw · brush). Build menüsünün **Clean'i bir koşudur** (`-t:Clean`, §3.1). Koşu kilidi boyunca (starting · running · stopping) buton **Stop**'a döner ve etiketi aşamayı söyler: `Stop` → (nazik durdurma istendi) `Stop now` → (sert durdurma gönderildi) `Terminating…` (disabled). Tooltip yok.

Toast/popup YOK (karar).

## 2.8 Popover'lar

Ortak: chip'in üstünde 8px boşlukla açılır, `surface-overlay` zemin, 1px `border-strong`, radius 8, overlay gölgesi, 140ms pop-in (4px yukarı + scale .985→1). Dışarı tıkla / Esc → kapanır. (Satırdaki *Open in Visual Studio* seçicisi aynı kabuğu kullanır ama butonun altında açılır ve yalnız dış tıklamayla kapanır — §2.4.)

**Branch (480px):** caps başlık `SWITCH BRANCH` · arama inputu (büyüteç, `Search branches…`, açılınca odak alır, **temizleme ✕'i** var) · satırlar (**28px**, en çok **12 satır** = 336px görünür, sonra kayar): seçilide (= aktif branch) ✓ amber, değilse branch ikonu; ad mono, uzunsa ellipsis; aktif branch'te amber `active` rozeti, diğerlerinde mono SHA. ✓ satırı ile `active` rozeti hep AYNI branch'tir. `origin/HEAD` listelenmez. Eşleşme yoksa `No branches match "q".` · hairline + alt not: `Switching checks the branch out in your working tree.`
- **Klavye:** arama kutusunda `↓` ilk eşleşmeyi işaretler; `↓/↑` işareti taşır (sonda durur, ilk satırda `↑` işareti temizler); `Enter` işaretli satırı seçer (işaret yoksa hiçbir şey); yazmak işareti temizler. İşaretli satır hover zeminini alır ve görünür tutulur.
- **Seçim = gerçek `git checkout <branch>`** (uzak branch için izleyen yerel branch oluşur). Aktif branch'i seçmek hiçbir şey yapmaz. Akış: pill `SWITCHING BRANCH` + `▸ Switching to <branch>…` → checkout → Sync. **Diğer kopyada açık branch:** git aynı branch'i iki kopyada açtırmaz; kontrol kirli ağaç kapısından ve stash'ten ÖNCE yapılır. İş başlar ve reddedilir, pill düşer; konsola tek amber satır `warning: develop is already checked out in D:\src\ACME-wt — a branch can be open in one working copy at a time` (yol, branch'i tutan kopyanın kökü); `git checkout` komut satırı yazılmaz, stream'e satır düşmez (warn yalnız kirli ağaç reddinde). Prototipte: repository'de, demo worktree'si ayarlı ve bağlıyken `develop`; worktree'deyken repository'nin branch'i. Geri uygulama kullanıcının — araç stash'i ne gösterir ne geri uygular.

**Branch kartı — kirli ağaç (360px).** Seçilen branch commit'lenmemiş değişikliğe çarptığında ve *Stash and switch branches* **kapalıyken** branch popover'ının yerinde açılır (chip'in 8px üstünde); `Ds.Popover`, kartın kendi padding'i 0 (üç buton ~315px ister). İki blok, arada 1px `border`:
1. (padding `13px 16px 14px`) caps `UNCOMMITTED CHANGES` (11px `text-faint`) · 8px altında 13px/500 `text-primary`: `3 files have uncommitted changes in the repository.` (`1 file has…`; worktree'deyken `…in the worktree.`)
2. (padding `12px 16px 14px`) 12px `text-dim`: `Stash them and switch here, or open <branch> in the worktree and leave this copy as it is.` (worktree'deyken `…in the repository…`) · 12px altında sağa yaslı, 8px arayla: `Cancel` (secondary sm) · diğer kopya butonu (secondary sm: `Use worktree` / worktree ayarlı değilse `Set up worktree…` / worktree'deyken `Use repository`) · `Stash and switch` (primary sm).
- Akış: checkout işi reddedilir, pill düşer; konsola `warning: 3 files have uncommitted changes — commit or stash them first`, stream'e warn `branch switch refused — 3 uncommitted files`; kart açılır. Esc ya da dışarı tıklama = Cancel.
- `Stash and switch` (ya da ayar açıkken kartsız): konsola `Stashed uncommitted changes: "…" — restore them with git stash pop` → `Switched to <b> (<sha>) — from <a>` → Sync.
- `Use worktree` / `Use repository`: branch'i diğer kopyada checkout edip oraya geçer; o kopya da kirliyse kart orada yeniden açılır. `Set up worktree…`: Settings › Workspace'i Worktree folder alanına odaklı açar; Save worktree'yi kurar ve branch'i orada açar.

## 2.9 Settings dialog (880×576) — v1.19.0 "sol raylı iki panel"

> **v1.19.0 bu bölümü baştan yazar.** Tek uzun kolon, bölüm başına 2–4 satırlık paragraf, bölüm başlığına sıkıştırılmış switch ve `Load sample layers` KALKTI. Gerekçe: ayarlar çoğalacak; tek kolon her eklemede daha da sıkışıyordu. Export / Import / Clear davranışı (v1.10.0) ve harici proje kartının yapısı (v1.17.0: grip + path + sil, tür yoldan okunur) DEĞİŞMEDİ.

**Kabuk (üç dialogun ortağı).** `DialogShell`: scrim + `surface-raised` kutu, 1px `border-strong`, radius-lg, tek overlay gölge, 180ms fade+6px giriş, `overflow: hidden`. Gutter **18px** (başlık ve footer), gövde flex — **yükseklik sabit 576px** (`maxHeight: calc(100% - 48px)`), içerik kendi içinde kayar: bölüm değiştirince dialog zıplamaz.
**Kenar sönümü (üç dialogun kaydırılan gövdesi):** gövdenin üstünde ve altında, içerik payı kadar yüksek birer şerit dialog yüzeyinden (kenarda opak) saydama iner — metin başlık/sekme hairline'ının altına kayarken söner, footer hairline'ının üstünde de; dinlenmede yalnız boş padding'i örter, sonuna kaydırılınca son satır sönmez. Şeritler kaydırıcının içerik hücresinde durur, scrollbar'ı örtmez. Yükseklikler: What's new 22 / 24 · About 14 / 20 · Settings 20 / 24.

- **Başlık satırı** (`12px 12px 12px 18px`, altta `border-subtle`): `Settings` (15px/600 `text-primary`) + sağda kapat (X) IconButton.
- **Bölüm rayı (196px):** `surface` zemin (dialogdan bir kademe koyu — iki panel okuması buradan gelir), sağda 1px `border-subtle`, padding `10px 8px`, satır arası 2px. Satır: 30px yüksek, `radius-sm`, padding `0 8px`, 13px/500. Pasif `text-dim` + şeffaf zemin · hover `surface-raised` · **aktif `surface-overlay` + `text-primary`** (amber yok — accent statüye ayrılmıştır). Sağda mono 11px sayaç yalnız doluysa: `External projects`, `Layers`. Sıra: **General · Workspace · External projects · Layers**.
- **Sayfa (pane):** `bo-scroll`, padding `20px 20px 24px`; scrollbar kolonu **her zaman ayrılmıştır** (liste taşma eşiğini geçince girdiler kaymaz); bölüm değişince sayfa tepeden başlar. Her sayfa `PaneHead` ile açılır: başlık 15px/600 + **TEK satır** açıklama (12px `text-dim`, `leading-snug`, max 520px). Uzun gerekçe metni yok.

**Standart ayar satırı (`ToggleRow`) — genişleme biçimi bu.** Solda ad (13px/500 `text-primary`) + altında tek satır açıklama (12px `text-dim`, max 430px), sağda kontrol; satırlar arası 1px `border-subtle`, dikey padding 13px. Yeni ayar eklemek = `GENERAL_GROUPS`'a bir satır daha; düzen işi yok. Bağımlı satır (`dep`) üst anahtar kapalıyken %45 opaklık + `pointer-events: none` (layout sabit).

**General**

- `PaneHead`: `How the app starts and behaves. Every setting applies when you save.`
- Grup başlıkları caps 11px `text-dim`, gruplar arası 22px. Altı grup, satır = ad · açıklama · varsayılan:
  - **STARTUP:** `Start with Windows` (*Launch when you sign in to Windows.*) · kapalı — `Start minimized to tray` (*No window on start — the tray icon brings it back.*, `dep: startWin`) · kapalı — `Close to tray` (*Closing the window leaves the app running in the tray.*) · açık. Uygulamada Görev Yöneticisi'nde kapatılmış bir *Start with Windows* açıklamanın yerine durum notu gösterir: `Turned off in Task Manager's Startup apps — switch it on to start with Windows again.`
  - **BUILD:** `Pull before build` (*Update every external working copy first — a fast-forward-only git pull, one per copy.*) · açık — `Replace files in use` (*When a post-build copy finds its file held by a running program, move the old file aside and copy again; the program keeps the old build until it restarts. Off: the copy stops there and the console names the program.*) · kapalı.
  - **BRANCHES:** `Stash and switch branches` (*When the working tree has uncommitted changes, stash them (including untracked files) and switch. Off: switching stops and asks you to commit or stash first.*) · kapalı (§2.8).
  - **CONSOLE:** `Compact console` (*Errors in full, the first warnings of each project and a count of the rest; every other MSBuild line stays in the project log. Off: every line.*) · açık (§2.5).
  - **ENGINE:** `Stop the engine when idle` (*After five minutes without work the engine process exits. It starts again in a moment when you build or sync.*) · açık.
  - **NOTIFICATIONS:** `Show notifications` (*Tray notifications: a build's result, the first close to the tray, a Build shortcut that could not start, and a second launch that could not bring the window forward.*) · açık — **tek aç/kapa anahtarı**; "yalnız hatada bildir" kuralı İSTENMEDİ (kullanıcı kararı). Prototipte görsel karşılığı yok — Windows tarafında tray balonları.
- Varsayılanın tek tanımı satırdır (prototipte `GENERAL_GROUPS[].def`; `DEFAULT_GENERAL` satırlardan türer). Kalıcılık: `delta-bo-general-v1` = `{startWin, startTray, closeTray, replace, stash, compact, idleStop, notify}` (eski `notifyFail` değeri okunur ve `notify`ye taşınır). **Pull kendi anahtarında kalır** (`delta-bo-pull-external-v1`, varsayılan açık).
- **Kaydet notları:** yalnız değeri değişen anahtar konsola tek satır yazar, biçim `<Label> on/off — <sonuç>`: ör. `Close to tray off — closing the window quits the app` · `Replace files in use on — a file a running program holds is moved aside and copied again` · `Stash and switch branches on — uncommitted changes are stashed before a branch switch` · `Compact console off — every MSBuild line is shown from the next run` · `Stop the engine when idle on — the engine exits after five idle minutes`.

**Workspace**

- `PaneHead`: `The folder that holds the solutions. Projects and the dependency graph are discovered from it on Sync.`
- caps `REPOSITORY ROOT` + satır: mono input (placeholder **`D:\src\myapp`**) + secondary `Browse…` (uygulamada pencerenin ortasında gerçek klasör seçici). Altında 11px `text-faint`: `Nothing is discovered without it.` **Boş kök kaydedilebilir:** first run'da davet kalır; açık bir workspace üzerinde workspace'i kapatır (boş durum) ve worktree klasörünü boşaltır.
- caps `WORKTREE FOLDER` (üstte 22px, altta 7px) + satır: mono input (placeholder `D:\src\myapp-worktree`, kullanılamazken invalid) + secondary `Browse…` (seçici reponun üst klasöründe başlar). Altında 11px `text-faint`, sarar: `Optional — a second working copy of the repository, next to it, for another branch. Save creates it when the folder does not exist; switch between the two in the bottom bar.`
  - Save, klasör kullanılamazken kapalıdır; footer nedeni (dış proje/katman nedenlerinden önce): `The worktree folder needs a full path` · `The worktree folder must sit next to the repository, not inside it or around it` · `The repository root is not a git repository` · `The worktree folder is not empty and is not a worktree of this repository`. Alan ya da kök boşken denetlenmez.
  - Klasör yoksa ya da boşsa Save onu ana repodan **detached** worktree olarak kurar: pill `CREATING WORKTREE`, `▸ Creating the worktree…`, konsola `Creating the worktree at <folder>…` → `Worktree ready at <folder> (<sha>)` (hata: `warning: worktree not created — <reason>`), ardından Sync. Klasör değişince not: `Worktree folder → <folder>` / `Worktree folder cleared`. Settings worktree anahtarından ya da branch kartından açıldıysa alan odak alır ve başarılı Save worktree'ye geçer.

**External projects**

- `PaneHead`: `Projects outside the repository root — a folder, a solution or a project file; the git working copy is found from the path. They are built before everything else.`
- Kartlar (36px + 6px): grip (sürükle-bırak sıralama) + mono path input (placeholder **`C:\src\shared\MyApp.Common\MyApp.Common.csproj`**) + sil. Kolon başlığı YOK (tek kolon). Boş durum kesikli kutu: `No external projects — only what is discovered under the repository root is built.` Ekleme: ghost `+ Add external project`.
- Altta hairline + tek satır: `Card order sets the order the working copies are updated. Updating them before a build is on/off.` + ghost buton `Pull before build` → rayı **General**'a alır.

**Layers**

- `PaneHead`: `Projects are grouped by the first matching pattern (regex on the project name), top to bottom. Non-matching projects fall under Other.`
- Kolon başlıkları caps `LAYER NAME` (170px) + `PATTERN`. Kart: grip + ad input (170px) + mono regex input (geçersizse invalid) + sil.
- **`+ Add layer` BOŞ satır ekler** (ad ve desen boş) — değer değil **placeholder** gösterilir; satır indeksine göre `LAYER_PLACEHOLDERS` çiftinden: `Core` / `^MyApp\.(Core|Common)\.` · `Infrastructure` / `^MyApp\.(Data|Infrastructure)\.` · `Domain` / `^MyApp\.Domain\.` · `Services` / `^MyApp\.Services\.` · `Api` / `\.Api$` · `Client` / `^MyApp\.(Web|Client|Mobile)\.` (6'dan sonra baştan döner). Ürün adı taşımaz.
- Varsayılan BOŞ; boş durum kesikli kutu: `No layers yet — projects show as a single list in build order.`

**Footer** (`12px 18px`, üstte `border-subtle`)

- Solda **Export / Import / Clear** ikon butonları (davranış v1.10.0): Export → `build-orchestrator-settings.json`, **düz anahtarlar** bu sırayla: `app, version, repositoryRoot, externalProjects[{path}], pullExternalBeforeBuild, replaceFilesInUse, stashOnBranchSwitch, compactConsole, stopEngineWhenIdle, startWithWindows, startMinimizedToTray, closeToTray, showNotifications, layers[{name, pattern}]` (`general` bloğu, `vcs` ve worktree klasörü yok) · Import → düz anahtarları **forma** yükler; dosyada olmayan anahtar o switch'e dokunmaz, bilinmeyen anahtar (`vcs`, eski `general`) yok sayılır · Clear → iki aşamalı ikon onayı (`Click again to clear`), formu boşaltır, her switch'i katalog varsayılanına döndürür ve worktree klasörünü boşaltır. Üçü de Save'e kadar hiçbir şey uygulamaz; geri bildirim aynı satırda 2.4s (`Exported — settings JSON` · `Imported — N layers · M external · root set` · `Invalid settings file` · `Could not write the settings file` · `Cleared — save to apply`).
- Geri bildirim yokken ve **Save kapalıyken tek satır neden** (11px `text-faint`): worktree nedenleri (yukarıda) · `Every external project needs a path` · `Every layer needs a name` · `Check the highlighted pattern`. Hangi sayfada olunduğu fark etmez.
- Sağda `Cancel` + primary `Save` (`Save and sync` yalnız first run'da **ve** bir kök yazılıyken).
- Açılış bölümü: first run'da **Workspace**, sonrasında **General**.

---

## 2.10 About dialog (620px, F1) — v1.19.0 sadeleştirmesi

> **v1.19.0:** `Third-party` sekmesi KALDIRILDI (placeholder lisans listesiydi). Sekme sırası mantıksal: **About → Environment → Shortcuts**, F1 **About**'ta açar. Sekme kontrolü **`DS.Segment` olarak kaldı** (alt çizgili `DS.Tabs` denendi, kullanıcı segmenti seçti): kontrol bir nesne olduğu için **kutusu** 18px gutter'a hizalanır, etiketin kendi 11px iç boşluğu kontrole aittir. Hizasızlık hissi boşlukla çözüldü — kimlik bloğunun altına 16px, segmentin altına 14px + **tam genişlik hairline**; segment artık başlığa yapışık değil, gövdeyle de kıyaslanmıyor.

Ortak `DialogShell` (§2.9). Yükseklik içeriğe göre; **gövde sabit 284px** — sekme değişince dialog zıplamaz.

- **Kimlik bloğu** (`20px 18px 18px`): `app-mark.svg` (28px) + `Build Orchestrator` (17px/600) + yanında **sürüm çipi** — **uygulama sürümü** (`1.15.0`; tasarım sürümü UI'da hiç görünmez) (mono 11px, `surface` zemin, 1px `border-strong`, radius-xs) · alt satır `Ordered, incremental builds for a multi-project .NET solution.` (12px `text-dim`). Sağda firma kilidi: 1px×28px ayraç + caps `LICENSED TO` + firma logosu (13px, %80). Eski `1.18.0 · © 2026 Delta` satırı kalktı — sürüm çipe, copyright About sekmesine taşındı.
- **Sekme bandı:** `DS.Segment` (md, 26px) kendi bandında — band padding `6px 18px 14px`, altta tam genişlik `border-subtle`. **Dikey ritim (kullanıcıyla oturtuldu):** kimlik bloğunun tanım satırından segmente **26px** (kimlik bloğu padding `20px 18px 20px` + band üstü 6px), segmentten hairline'a **14px**, hairline'dan gövde metnine **14px** (gövde padding `14px 18px 20px`) — segmentin iki yanındaki boşluk eşit okunur. Renkler DS varsayılanı: `surface-sunken` kap + 1px `border`, aktif seçenek `surface-overlay` + `text-primary`, pasif `text-dim` — **amber yok** (accent statüde kalır).
- **About sekmesi:** 13px/1.62 (`reading`) paragraf (max 470px): *Build Orchestrator discovers the projects under the repository root, works out the dependency graph and builds in that order — only what changed, in parallel where the graph allows. The plan, the running build and its result stay visible while it works.* · hairline · etiket/değer satırları (etiket 124px 12px `text-dim`, değer mono 12px): `Version` · `Engine` (ikisi de uygulama sürümü; motor raporlamadan önce uygulamada `not started`) · `Copyright © 2026 Delta Yazılım` · altta ghost buton **`What's new in <sürüm>`** (sparkle ikonu) → About kapanır, What's new açılır.
- **Environment sekmesi:** iki caps grup. **RUNTIME**: Engine PID (uygulamada motor uyurken `asleep`, yokken `—`) · .NET runtime · OS. **PATHS**: MSBuild (vswhere sonucu gelene dek `resolving…`) · Repository root (aktif kök; boşsa `no repository`) · State file · Logs. `Worktree pool` satırı YOK. Bellek satırı hiçbir sekmede yok. Satır 27px, etiket 124px, değer mono 12px; **yollar kesilmez** — hücre `.bo-xscroll` (görünmez yatay scroll, fare tekerleği `scrollLeft`e gider, v1.13.1). App/Engine sürümü bu sekmeden çıktı.
- **Shortcuts sekmesi:** üç caps grup, en sık kullanılandan en seyreğe (uygulamada tek katalog — Build menüsünün kbd'leri ve ⓘ tooltip'i de buradan):
  - **GLOBAL:** `Show or hide the window` `Shift+Space` · `Build without bringing the window up` `Ctrl+Shift+Space` (kayıt başarısızsa o satırda `unavailable`)
  - **BUILD:** `Build — only stale projects` `F5` · `Rebuild — all projects, cache ignored` `F6` · `Clean — remove build outputs` `F7`
  - **APPLICATION:** `Focus the project filter` `Ctrl+F` · `About — version, shortcuts and diagnostics` `F1` · `Close the topmost layer: exit card → dialog → popover/menu → selection; otherwise stop the running build, or close the window like ×` `Esc` · `Exit — quit and end every process` `Ctrl+Q`
  - Satır 27px: solda açıklama 13px `text-secondary`, sağda Kbd chip'leri. **Kısayol metinleri birebir korunur.** `Ctrl+F1`, `Alt+B`, `Ctrl+F5`/`Shift+F5` yok.
- **Footer:** solda ghost `Copy diagnostics` (1.4s `Copied` + yeşil ✓), sağda secondary `Close`. Panoya giden düz metin: ilk satır `Build Orchestrator 1.15.0`, sonra Engine, Runtime satırları, **yalnız kopyada** bellek satırları `App private` · `GC committed` · `Managed heap` (MB), sonra Paths — etiketler hizalı, 2 boşluklu ara. Ghost butonun sarmalayıcısı `margin-left: -10px` ile etiketi 18px gutter'a hizalar.
- Esc önceliği: exit kartı → What's new → About → Settings → popover/menü → seçim (tam zincir §3.3). F1 About'u açar/kapar. Scrim tıklaması da kapatır.

## 2.11 What's new dialog (720×600)

> **v1.19.0:** genişlik 620 → **720**, gövde 400 → **flex** (dialog 600px sabit, `maxHeight: calc(100% - 48px)`). Sürüm kimliği başlıktan ve satır sonundan **sol sütuna** taşındı; başlıktaki iki satırlı `INSTALLED VERSION` bloğu yerine tek mono sürüm çipi. Kategori blokları (6px kare + caps) ve veri kaynağı (`app/release-notes.js`) DEĞİŞMEDİ.

Title bar'daki **sparkle** ikonundan ya da About'taki `What's new in <sürüm>` butonundan açılır; kısayolu yok. Ortak `DialogShell`, zIndex 101 (About 100).

**Veri kaynağı = uygulamanın `CHANGELOG.md`'si** (exe'ye gömülü, tek kaynak). Notlar **uygulama sürümleridir** (prototipte CHANGELOG'daki bütün sürümler, en yenisi en üstte); biçim `## [x.y.z] - yyyy-MM-dd` + `### Added|Changed|Fixed|Performance|Removed` + düz metin `- ` maddeler (sarılı satır maddeye devam eder). Prototipte `app/release-notes.js`'i `npm run build` bu dosyadan, uygulamanın ayrıştırıcısıyla aynı kurallarla üretir (Added→feature, Changed→change, Fixed→fix, Performance→perf, Removed→removed; tarih yazıldığı gibi).

- **Başlık satırı** (`20px 18px 16px`, altta `border-subtle`): solda `What's new` (15px/600) + `Release notes for Build Orchestrator.` (12px `text-dim`); sağda mono sürüm çipi (20px yüksek, `surface` zemin + `border-strong`). Sağ kenar sabit — scrollbar'a göre hizalanan blok yok.
- **Gövde:** `bo-scroll`, padding `22px 18px 24px`, flex (dialog yüksekliği sabit), kenar sönümü 22 / 24 (§2.9).
- **Sürüm bloğu = 2 kolonlu grid** (`84px` + `1fr`, 26px kolon aralığı):
  - **Sol sütun (sticky, `top: 0`):** mono 13px/500 sürüm · altında mono 11px `text-faint` tarih · kurulu sürümde 16px `INSTALLED` çipi. Uzun bir sürümün maddeleri kayarken kimliği ekranda kalır; tarihler her sürümde aynı sütunda.
  - **Sağ sütun:** kategori blokları, aralarında 15px. Blok: 6px renkli kare + caps `text-dim` başlık, altında 13px maddeler 13px içeriden, **satır arası 10px, satır yüksekliği 1.62, max 500px ölçü** (`text-wrap: pretty`).
- Sürümler arasında 22px boşluk + 1px `border-subtle`.
- Kategoriler ve sırası: **Added** (amber) · **Changed** (`text-dim`) · **Fixed** (yeşil) · **Performance** (turuncu) · **Removed** (kırmızı). Boş kategori çizilmez.
- **Son 3 sürüm açık**, gerisi `Earlier versions (N)` ghost butonu altında (bugün `Earlier versions (30)`); buton grid'in sağ kolonunda, `margin-left: -10px` ile madde kolonuna hizalı.
- **Footer:** yalnız sağda secondary `Close`.
- **Görülmemiş sürüm işareti** (v1.13.0): `delta-bo-seen-version-v1` ≠ kurulu uygulama sürümü (`ABOUT_VERSION`) iken sparkle butonunda 5px amber nokta; dialog açıldığı anda düşer. Açılış pop-up'ı YOK.

## 2.12 Güncelleme — title bar hapı, kart, restart ekranı (v1.23.0)

**İlke:** güncelleme yalnız gerçekten hazırken görünür. Uygulama açılıştan kısa süre sonra ve periyodik olarak besleme kaynağını sessizce kontrol eder, yeni paket **arka planda iner**; indirme sürerken UI'da hiçbir iz yok. Paket hazır olduğunda title bar'da tek bir hap belirir. Toast, açılış pop-up'ı, her zaman görünen sürüm etiketi YOK.

**Hap (title bar):**
- Yer: sağ kümenin **solu** — `[Update <sürüm>] | [quad][list][focus] | [⚙][✦][ⓘ] | [⏻]  [— □ ×]`. Arkasında mevcut ayraçla aynı 1px × 14px ayraç (`border`, yatay 8px). Küme sağa yaslı olduğu için hap boş alana doğru büyür; **mevcut ikonlar yerinden oynamaz** (kas hafızası).
- Ölçü: 22px yükseklik, padding `0 8px 0 6px`, gap 6, radius 4 (`radius-sm`), `surface-raised` zemin + 1px `border-strong`. İçerik: Lucide *circle-arrow-up* 13px (ⓘ ile aynı r=9, stroke 1.7) + `Update` (12px/500, `text-primary`) + mono sürüm (11px, `text-dim`, tabular).
- Hover / kart açık: `surface-overlay` + `neutral-500` border, sürüm `text-secondary`; 80ms.
- **Amber YOK.** Title bar'da chevron dışında amber öğe olmaması kuralı (§6, v1.2.1) korunur; hap vurgusunu ağırlıkla alır — ikonlu şeritteki tek yazılı öğe ve en parlak metin.
- Giriş: bir kez, 280ms ease-out, 4px yukarıdan + opaklık (ayraç da aynı). Sonrasında hareketsiz — nabız/parlama yok. Reduced-motion: anında.
- `aria-label="Update to <sürüm>"`, `aria-haspopup="dialog"`, `aria-expanded`.

**Kart (344px):** hapın 9px altında, sol kenarı hapla hizalı; `surface-overlay`, 1px `border-strong`, radius 8, overlay gölgesi, 140ms drop-in (4px yukarıdan + scale .985 → 1, origin sol üst). **Settings, About ya da What's new açılınca kart kapanır** (uygulamada Popup kendi HWND'sidir, dialogun altında duramaz). İç bölücüler 1px `border`. Üç blok:
1. **Kimlik** (`13px 16px 15px`): caps `UPDATE READY` (`text-faint`) + sağda mono boyut `18.4 MB` (`text-faint`) · 11px altında **sürüm geçişi**: kurulu sürüm mono 13px `text-dim` → Lucide *arrow-right* 12px `text-faint` → gelen sürüm mono 16px/500 `text-primary`.
2. **Öne çıkanlar** (`13px 16px 14px`): What's new'in blok dili birebir (§2.11) — 6px renkli kare + caps kategori, altında işaretsiz maddeler (12px, `text-secondary`, 1.5 satır). Maddeler **gelen sürümün CHANGELOG bölümünden** gelir; en çok **5** madde, kategori sırasıyla (Added → Changed → Fixed → Performance → Removed); fazlası 8px altında tek düz satır `+N more in What's new after restart` (12px `text-faint`, tıklanmaz). Madde yoksa blok hiç çizilmez.
3. **Karar** (`12px 16px 14px`): tek açıklama satırı (12px `text-dim`) → sağa yaslı `Later` (secondary sm) + `Restart to update` (primary sm, rotate-cw ikonu).
   - Normal metin: `Restart takes a few seconds and reopens the workspace. If you wait, it installs on the next start.`
   - **Kilit:** build / Sync / bakım görevi sürerken `Restart to update` disabled ve açıklama satırı sebebe döner — koşu, Stop'un aşamasını izler: `Available once the build finishes — Esc stops it.` → (durdurma istendi) `Available once the build stops — Esc stops it now.` → (sert durdurma gönderildi) `Available once the build stops.` · `Available once Sync finishes.` · `Available once the running task finishes.` Koşu bitince buton kendiliğinden açılır.
- Kapatma: `Later`, hapa tekrar tıklama (toggle), dışarı tıklama, Esc (popover sırasında — Esc önceliği değişmedi). `Later` hapı gizlemez; hap kurulum yapılana dek kalır, bir sonraki açılışta güncelleme kendiliğinden kurulur.

**Restart ekranı:** `Restart to update` → uygulama kapanana dek gösterilen kısa ekran. En üst katman (title bar'ın da üstünde), `surface-base` tam kaplama, 180ms fade-in; ortada `app-mark` 30px · `Updating Build Orchestrator` (13px/600) · mono `1.15.0 → <sürüm>` (11px) · 232px × 2px amber ProgressBar · adım satırı (11px `text-faint`): **tek adım** `Closing Build Orchestrator…`, çubuk **800ms**'de doğrusal %100'e dolar. Ekran süreç çıkana dek kalır; fade-out ve pencere içi yeniden açılış YOK — kurulum pencere olmadan süreç çıktıktan sonra yapılır, yeni sürüm sıradan bir açılış gibi başlar (What's new noktası geri gelir — yeni sürümün notları görülmedi, §2.11). Başarı toast'u YOK. Ekran açıkken klavye kısayolları yok sayılır.

**Prototip notu:** besleme kaydı `UPDATE_FEED` örnektir — sürüm kurulu uygulama sürümünün bir sonraki minor'ı (`1.16.0`), boyut ve 7 örnek madde (kart `+2 more` gösterir; uygulamanın CHANGELOG'unda 1.16.0 bölümü yoktur). Hap açılıştan 1.4s sonra gelir (sessiz kontrolün bittiği an); Demo → `update pill` kapatıp açmak hapı yeniden getirir. Restart ekranı dolunca "pencere gitti" demo yüzeyi kurulumun pencere olmadan yapıldığını söyler; tıklama yeni sürümü başlatır (hap düşer, uygulama Sync ile açılır). Gerçek uygulamada kontrol kaynağı ve aralığı yapılandırmadan gelir; güncelleme motoru yalnız kurulu kopyada çalışır.

## 2.13 Exit — buton, kart, kapanış ekranı

**İlke:** Exit uygulamayı ve uçuştaki her süreci bitirir. Hiçbir iş yokken uygulama **hemen** kapanır, kart açılmaz. İş varken önce kart sorar. Aynı karar title bar'daki ⏻ butonu, `Ctrl+Q`, tray menüsünün *Exit*'i, *Close to tray* kapalıyken `×` ve kapatacak katmanı kalmamış Esc için geçerlidir (§3.3).

**Kart (320px):** `Ds.Popover`, padding 0; butonun 9px altında, **sağ kenarı** butonla hizalı, sağ üst kökenden drop-in (4px yukarıdan + scale .985 → 1). Dialogların **üstünde** durur — dialog açılması onu kapatmaz. Odak `Cancel`'a düşer. Kapatma: `Cancel`, butona ya da `Ctrl+Q`'ya ikinci basış, dışarı tıklama, Esc (Esc önce onu kapatır). Üç blok, aralarında 1px `border`:
1. **Durum** (`13px 16px 14px`): caps 11px `text-faint` başlık `<WORK> IN PROGRESS` — iş adları `Build` · `Rebuild` · `Resolve` · `Clean` · `Deep clean` · `Optimize` · `Sync` · `Branch switch` · `Pull` · `Worktree setup`; sağda (yalnız koşuda, plan varken) mono 11px `text-faint` ilerleme `14/38`. 8px altında 13px/500 `text-primary` tek cümle: `Exit stops the build, then quits.` · git yazımında (branch switch / pull / worktree setup) `Exit waits for the branch switch, then quits.` (iş adıyla).
2. **`ENDS ON EXIT`** (`12px 16px 13px`): caps başlık, 8px altında dış job'ın süreçleri — satır başına mono 11px ad `text-secondary` solda, `×N` `text-dim` sağda, satırlar 4px arayla; sayıya göre azalan, sonra ada göre. Motor ve `conhost.exe` listelenmez; liste boşsa blok çizilmez. Uygulamada kart açıkken saniyede bir yeniden sayılır.
3. **Karar** (`12px 16px 14px`): 12px `text-dim` sonuç satırı (1.5): Build / Rebuild / Resolve `4 projects in flight stay unbuilt. Finished projects keep their results; the next Build picks up the rest.` (tekilde `1 project … stays`; 0'da yalnız ikinci cümle) · Clean `Projects cleaned so far stay clean; the rest keep their outputs.` · Deep clean `Part of the workspace may already be reset; the next Build compiles what it needs.` · Optimize `Packages restored so far stay; the next Optimize does the rest.` · Sync `Nothing is lost; the next start syncs again.` · Branch switch / Pull / Worktree setup `Cutting git off mid-write could leave the working tree half-switched.` / `…half-updated.` / `…the worktree half-created.` 12px altında sağa yaslı, 8px arayla: `Cancel` (secondary sm) + **danger sm** buton, 12px power ikonu: `Stop and exit` · git yazımında `Wait and exit`.
- Kart açıkken iş biterse boşta hâline döner: başlık `EXIT`, cümle `Nothing is running. Exit quits right away.`, buton `Exit`; ilerleme, süreç bloğu ve sonuç satırı kalkar (butonlar bloğun tepesine çıkar).

**Kapanış ekranı (Exit kipi):** güncelleme restart ekranıyla aynı 232px sütun, pencerenin tamamını örter (dialogların da üstünde): `app-mark` 30px · `Closing Build Orchestrator` (13px/600 `text-primary`) · mono 11px `text-dim` orta satır `Build · 14/38` (iş adı + koşuda ilerleme) · 2px amber çubuk · adım satırı (11px `text-faint`).
1. `Stopping build…` (git yazımında `Finishing branch switch…`, iş adıyla) — en az 500ms, çubuk 0 → %50; motor işin durduğunu bildirene dek bekler (iş kesildiyse hemen, git yazımında iş bitince).
2. `Ending 5 processes…` / `Ending 1 process…` / (0'da) `Quitting…` — 500ms, %50 → %100; sonra pencere kapanır.
- Fade yok; ekran açıkken klavye yok sayılır. Altında ribbon `▸ Stopping — …` yazar ve konsola `exit requested — ending the work in flight, then closing` düşer.

**Prototip notu:** süreç adları (`MSBuild.exe`, `VBCSCompiler.exe`, `git.exe`, `dotnet.exe`) örnek veridir. Kapanıştan sonra ve `×`/Esc pencereyi tepsiye indirdiğinde (*Close to tray* varsayılan açık) "Demo:" ile başlayan "pencere gitti" yüzeyi görünür; tıklama pencereyi geri getirir ya da çıkıştan sonra sahneyi baştan oynatır. Tepsi kipinde yüzeyin altında sahte bir görev çubuğu (yalnız demo) ve koşu sürüyorsa onun üstünde tray build göstergesi durur (§2.14). Tray menüsü (Stop / Exit) ve OS balonları prototipte yok.

## 2.14 Tray build göstergesi — animasyonlu işaret

**İlke:** pencere tepsideyken koşan bir derleme görünmez değildir. Pencere **gizli** VE faz **Starting · Running · Stopping** iken (Sync kapsam dışı) ürün işareti ekranın sağ alt köşesinde döngüyle oynar. Koşu sürerken pencere tepsiye inerse hemen belirir (giriş animasyonu yok — döngünün ilk karesi zaten boş); pencere tepsideyken başlayan derleme (`Ctrl+Shift+Space`) de alır. Pencere geri gelince **anında** gizlenir; çıkış oynamaz. Ayrıntı ve zaman çizelgesi: design system `components/TrayBuildIndicator` (README + `preview.html`).

- **Yüzey:** kendi üst düzey penceresi — çerçevesiz, zeminsiz (şeffaf), odak almaz, Alt-Tab'da ve görev çubuğunda yok, her zaman üstte. Çizili piksele sol tık pencereyi tepsi ikonuyla aynı yoldan geri getirir (el imleci); şeffaf alan tıklamayı alttaki pencereye geçirir.
- **Ölçü ve konum:** bant 375 × 134 birim (`viewBox="-30 76 375 134"`), ekranda 0.5 katı **187.5 × 67 DIP**; **birincil** ekranın çalışma alanının sağ alt köşesi, sağ pay **4**, alt pay **12** DIP. Köşe her gösterimde ekranın o anki çalışma alanı ve ölçeğinden yeniden hesaplanır.
- **İşaret:** `app-mark.svg`'nin beş şeridi ve şevronu (renkler `brand-strip-dim` · `amber` · `neutral-700` · `text-primary` · `text-secondary`, şevron `amber-bright → amber → brand-chevron-deep`); şerit grubu gölgesi aşağı 2 / blur 4.8 / %30, şevron aşağı 2.6 / blur 5.6 / %32.
- **Döngü (3.000 s):** giriş 0–1.34 s — şevron X −170'ten soluk girer, siluetini izleyen bir maske onunla aynı keyframe'lerle kayıp şeritleri silerek açar, şeritler kendi gecikmeleriyle oturur; maske 1.150'de düşer · duruş 1.34–2.10 s — logo karesi · çıkış 2.10–3.00 s — şevron hızlanarak uzaklaşır, şeritler farklı mesafelerle peşinden gider, sağ uçları x 250.5'te yok olur, her parçanın 340 ms solması kendi hareketinin bittiği anda 0'a iner. Son şerit 3.000'de kaybolur ve tur o anda yeniden başlar (boş kare yok). Eğriler WPF `KeySpline` = CSS `cubic-bezier`; zaman çizelgesi token değildir.
- **Reduced motion:** döngü başlamaz — duruş karesi (`app-mark.svg` ile aynı) durur.
- **Bitiş:** koşu bitince gösterge **yarıda kesilmez**: yeni tur başlatmaz, içindeki turu 3.000 s'ye kadar oynatır, son karede gizlenir → `duration-slow` (280 ms) nefes → *Show notifications* açıksa OS balonu (şeridin terminal satırı; koşu başına tek). Reduced motion'da çıkış ve nefes yoktur. Çıkış sürerken pencere geri gelirse gösterge anında söner, balon yine bir kez gelir. Koşu pencere açıkken biterse balon yoktur. *Show notifications* göstergeyi değil yalnız balonu susturur.
- **Sayaç yok:** beyaz şeritte proje sayacı taşıyan varyant bu ölçekte okunmadığı için uygulamadan kaldırıldı.

**Prototip notu:** `×`/Esc pencereyi tepsiye indirdiğinde "Demo:" yüzeyinin altında sahte bir görev çubuğu (40px, OS yüzeyi — yalnız demo) ve koşu sürüyorsa onun 12px üstünde, sağdan 4px içeride gösterge durur; koşu bitince turu bitirip kaybolur. Yüzeye ya da göstergeye tık pencereyi geri getirir. Balon çizilmez; uygulama döngüyü 30 kare/sn ile sürer, prototip tarayıcının kare hızında.

---

# 3. Davranış ve Akışlar

## 3.1 Fazlar

`empty → boot → syncing → idle → marking → running → stopping → done | stopped` · dikey eksenler: `task` (Deep clean / Optimize — §3.4) ve **workspace işi** (`work`: branch switch · pull · worktree setup — pill + şerit satırı, ardından Sync). Motorun kendi durumu ayrıdır: `ok · waking · dead · silent` (§2.2, §2.7-9a).

**v1.21.0 — Sync artık kendiliğinden de çalışır (spec §6.2).** İlke: konsol bir **bölüm** anlatır. Yeni bölümü yalnız iki şey açar — **kullanıcının başlattığı bir işlem** (Sync butonu, Build/Rebuild/satırdan/Resolve, Clean/Optimize, Pull) ya da **branch değişimi** (araçtan ya da dışarıdan); ikisi de konsolu temizler ve kendi transkriptiyle baştan yazar. Aynı dünyada kendiliğinden olan tazeleme — **commit** ya da **pencereye dönüş** (son Sync'ten 5 sn geçmediyse tekrarlanmaz) — bölüm açmaz: konsolu/olay akışını temizlemez, listeyi kaydırmaz, reveal oynatmaz, fetch yapmaz (`N behind` yereldeki son bilinen mesafeye göre hesaplanır); olay akışına yalnız tek satır düşer (`synced after commit`, `synced · N projects changed`).

| Tetik | Konsol | Liste / graf | Fetch |
|---|---|---|---|
| Uygulama açılışı · Restart engine | `Engine ready — v1.15.0`; Sync transkripti altına eklenir | reveal (açılışta) | evet |
| Sync butonu | temizlenir, tam transkript (`Engine ready` yazılmaz) | yapı aynıysa yerinde | evet |
| Branch değişti (araçtan ya da dışarıdan) | temizlenir; ilk satır yeni branch, ardından kısa Sync transkripti | aynı kural | hayır |
| Build / Rebuild / satırdan / Resolve | temizlenir; `… requested` + planlayıcı satırları | renkler kalır | hayır |
| Clean / Optimize (bakım kutusu) | temizlenir; zincirlenen Sync altına eklenir | tıklamada boşalır (`No projects` / `No graph`), Sync'le dolar | evet |
| Clean (Build menüsü, `-t:Clean` koşusu) | temizlenir | renkler kalır; temizlenen satır gri | hayır |
| Debug ↔ Release | temizlenir; ilk satır `Configuration → Release`, ardından Sync | baştan başlar | (Sync) |
| Worktree ↔ repository geçişi | temizlenir; ilk satır `Switched to the worktree — <path>` | keşif → reveal; kopyanın ilk Sync'i indeksler | (Sync) |
| Pull | korunur; sonuç Sync altına eklenir | yerinde | evet |
| Commit (kendiliğinden) | dokunulmaz; tek satır eklenir | yerinde | hayır |
| Pencereye dönüş, 5 sn eşiği (kendiliğinden) | dokunulmaz; değişiklik varsa tek satır eklenir | yerinde | hayır |

**v1.11.0 — kullanıcının başlattığı işlem aynı borudan geçer:** `_beginOp` (konsol + stream temizlenir, kalıcı işlem etiketi) → `_neutralize` (herkes düz nötr gri) → `_mark` (faz `marking`: nötr an → random dalga → sarı-gri an → örtüşen veda → nefes) → koşu. Girişler: `beginBuild` · `beginRebuild` · `beginCleanRun` (Build menüsü Clean) · `beginProject(name, mode)` · `beginProjectClean` · `startResolve`. Bakım işleri (`startTask`) ve workspace işleri (`beginWork`) koreografi oynamaz — pill + ikincil şerit satırı + sweep, sonra Sync. Koşu bitince `_runEndFinale` (hold → neon → nefes → grilerin açılışı). Ayrıntı: arşiv v1.11.0 / 4-5. **Kendiliğinden Sync (commit, pencereye dönüş) bu boruya hiç girmez** (v1.21.0) — koreografi ve reveal yalnız kullanıcı işleminde oynar.

- **empty:** repo yok. Liste panelinde kurulum daveti + `Open settings` (§2.4); graf `No graph`; stream yalnız bekleyen prompt; Σ 0; şerit `Not configured — repository root not set`; Sync/Build/chip'ler disabled. Çıkış: Settings → root gir → `Save and sync`.
- **boot:** repo var, Sync yapılmadı. Konsolun ilk satırı motorun hazır satırıdır: `Engine ready — v1.15.0`; açılış Sync'i onun altına eklenir (uydurma bir `… loaded` açılış satırı yoktur). Tüm satır/node'lar **başlangıç modunda** (satırda soluk düz şerit + 4 yaylı nokta, grafta kesikli node border; v1.12.0). **Açılışta Sync otomatik ve hemen ardından koşar** (v1.21.0) — kullanıcı Sync'e basmaz; boot yalnız bu geçişin anlık görüntüsüdür.
- **Sync:** konsola `git fetch origin main` → **`HEAD a3f81c2 · 3 commits behind origin/main`** (güncelse `HEAD b7e91d4 · up to date with origin/main`; çevrimdışıysa önce `warning: git fetch failed — continuing against the local HEAD; distance from origin/main is unknown (<sebep>)`, sonra mesafesiz `HEAD a3f81c2`) → `Scanning solutions (7)` → `Reading HintPath/Compile items (36 projects)` → `Dependency graph — 1 cycles` (uygulamanın biçimi; sayı tekilde de `cycles`) → `Build order resolved (36)` → kopyanın ilk Sync'inde `Indexing 4955 source files — later runs reuse the index` ve bitince `Indexed 4955 source files in 1s` → `Sync complete — 7 changed projects, 14 to build` + `22 projects up to date (will skip)`. Configuration değişiminden sonra da biçim aynıdır (ayrı bir `config changed` biçimi yok). Stream satırı `Sync — 14 to build, 22 up to date` — döngü eki yok; kirli döngü grubu Build'in planındadır. **Sync sonrası satırlar durum renginde** (v1.20.0 — §2.3): derlenecek satırlar gri, güncel yeşil, kanıtlı bozuk kırmızı; başlangıç (bilinmiyor) modu artık yalnız hiç Sync yapılmadan önce görülür. Plan aynı zamanda **karar etiketinde okunur** (§2.4): derlenecek satırlar `modified` / `modified · local` / `affected` / `never built`, kanıtlı bozuk `failed`, kalanlar `up to date`. Şerit Sync boyunca adım satırlarını (§2.2), sonra `▸ Ready — 14 to build · 22 up to date` yazar; işlem pill'i `SYNC`. Her kopyanın (repo, worktree) ilk Sync'i kaynak dosyaları indeksler (`indexing source files · d/t`). Açılış = otomatik sync, kapat-aç hep bu moda döner. **`.git\logs\HEAD` izlenir** (v1.21.0 — §6.1 spec): checkout, commit, pull, reset görülünce Sync kendiliğinden tetiklenir; HEAD ve branch adı son Sync'tekiyle aynıysa atlanır — aracın kendi checkout'u ve pull'un zincirli Sync'i böylece ikinci bir Sync üretmez.
- **Build (F5):** boot'ta Sync otomatik koşar; diğer her durumda **mevcut durumdan** koşar — stale set = değişen + hatalı + hatalıların bağımlıları (`beginBuild`); döngü grubu tek birimdir — bir üyesi kirliyse hepsi kirlidir ve hepsi `N to build`, `Build started — N projects` ve şeritteki `n/m`'e sayılır (turlar §3.2). **Konsol ve stream her Build'de temizlenir** (v1.11.0) ve koreografi baştan oynar. Bir önceki koşu her şeyi güncellediyse `_reseedChanges()` yeni bir HEAD üretip 2-4 proje bayatlar (kapsam ≤%45) — tekrar Build de tam süreçle koşar. Stale set gerçekten boşsa (Sync sonrası) hızlı kontrol koşusu (`Everything up to date`, koreografi yok). Önceki koşuda hata varsa düzeltme uygulandı varsayılır (baseFails kapanır). Konsola `build requested` ve planlayıcı satırları (§2.5); *Pull before build* açıksa harici kökler `Updating external '<name>'` → `Updated external '<name>' → <rev>` yazar. Koşu sonunda konsola özet satırı yazılmaz. **Git işlemi yarıdayken engellenmez** (v1.21.0 — spec §6.4): merge/rebase/cherry-pick/revert sürerken de Build çalışır; planlama başına işleme göre adlandırılmış tek uyarı satırı eklenir, ör. merge sürerken `a merge is in progress — files with conflict markers will not compile` (rebase/cherry-pick/revert'te işlem adı değişir, dil aynı).
- **Rebuild (F6):** tümü dirty kabul edilir (`beginRebuild`); aynı koreografi, kapsam = döngü üyeleri dahil tüm projeler (grup turlarla derlenir). Zorlanan ama güncel olan proje yeşil kalır.
- **Clean (Build menüsü, F7):** VS *Clean Solution* — grafın **her** projesinde (harici ve döngü üyeleri dahil, kenarsız) `-t:Clean` çalıştıran **bir koşudur** (`beginCleanRun`): pill `CLEAN`, açılış dalgası, `▸ Cleaning n/m`, Stop durdurur. Temizlenen satır gri kalır ve `never built` yazar; Sync zincirlenmez; cache'lere dokunulmaz → sonraki Build tam derleme. Konsol `clean requested` + planlayıcı satırları; stream `Clean started — 36 projects, parallelism 4` … `Completed — …`. Derin Clean (bin/obj silme + durum sıfırlama) bakım kutusunda kalır (§3.4).
- **Stop — üç aşama** (butonun etiketi aşamayı söyler, Esc de aynı adımları atar): ilk basış **nazik** — yeni proje başlamaz, uçuştakiler biter (faz `stopping`, etiket `Stop now`, şerit `▸ Stopping — n/m · finishing k in flight`); ikinci basış **sert** — `Terminating…` (disabled), uçuştakiler kesilir. Konsol basışları yazar: `stop requested — no new projects will start; 3 in flight will finish` · Stop now'da `stop now requested — in-flight compiles will be terminated`, kesilen derlemeler için `stopped — 3 in-flight compiles terminated`; stream `Stopped — 7 remaining projects queued`, şerit `▸ Stopped — 7/14 · 7 not built`. Sonrası **Build** kaldığı yerden sürdürür; elapsed yeni koşuda sıfırlanır. **F5 durdurmaz** — yalnız başlatır. **Marking fazında da çalışır** (v1.11.0): koşu hiç başlamaz, konsola `Cancelled — build not started`. Tek proje ve Resolve koşusu prototipte ilk basışta durur. Durdurulamayan iş (Sync, bakım, workspace işi) Esc'e iş başına bir kez konsol satırıyla cevap verir: `sync can't be stopped — it will finish on its own` (işin adıyla). **Koşu sırasında branch değişirse** (v1.21.0 — checkout/pull/merge/reset/rebase) nazikçe durur: yeni proje başlamaz, derlenenler biter, değişim anında derlenmekte olanların sonucu deftere yazılmaz; koşu bitince yeni bölüm açılır, ilk satırı koşunun özetidir (kaç proje bitti, kaçı yarıda kaldı, run log klasörünün yolu). Commit koşuyu durdurmaz.

## 3.2 Scheduler kuralları (gerçek uygulamada Core'un davranışı — UI bunu yansıtır)

- Derleme sırası **liste sırasıdır** (katman → tanım sırası). Sıradaki projenin bağımlılığı bitmemişse **İLERİ ATLANMAZ** (`if (!ok) break`) — paralellik katman içinde doğal oluşur. Paralellik üst sınırı perf ayarından (2/4/6).
- **Hatalı bağımlılık ALT PROJELERİ BLOKLAMAZ:** bağımlılar son başarılı çıktıyla yine derlenir; kök hata adları `depIssue` olarak zincir boyunca aşağı taşınır. Bu projeler:
  - log başında warn satır(lar)ı alır: `warning: ACME.Sales.Core failed in this run — last successful output referenced (ACME.Sales.Core)`; doğrudan olmayan kökler tam adlarıyla tek satırda, doğrudan satırlara **ek olarak**: `warning: failure in dependency chain (ACME.Sales.Core) — referenced outputs may be stale`,
  - kartta/grafta üçgen rozet taşır, `dependency-affected` sayacına girer,
  - stream'de `built — dependency issue (2.4s)` olarak görünür; konsolda warn tonunda.
- **Döngü grubu düz Build'de turlarla derlenir** (Rebuild'de de): grup tek birimdir (ortak imza). Grup, kendisi dışındaki upstream'i çözülene dek bekler — bir üyenin aşağısındaki projeler upstream sayılmaz, grup bitince koşar. Üyeler bir slotta **teker teker** derlenir, diğer projeler o sırada paralel derlenmeye devam eder. Tur 1 her kirli üyeyi derler; tur 2 okuduğu yüzey kayan üyeyi yeniden derler; sonra grup yakınsar (tavan 3). Turunu bitiren üye kuyrukta bekler (`Status = Queued`), sonucu yazılmaz; grup bitince her üye tek sonuç alır (`X built (t)`, turlarının toplamı), deftere yazılır ve ardından grubun hükmü gelir (`cycle converged — …`, §2.6). Nazik Stop yeni üye ve yeni tur başlatmaz. Bitiş özetinde döngü eki yoktur; uygulamada yakınsamayan grup ` (N stuck in a cycle)` ekler. Tek proje koşusunda grup kurulmaz — kapsam dışı kirli bir döngü üyesine bağımlı proje depIssue taşır (`warning: X is in a dependency cycle and was not rebuilt — last known output referenced`).
- Temiz projeler bağımlılıkları çözülünce **dalga dalga skip** edilir (tek seferde hepsi değil — tik başına ~3, all-clean'de ~12).
- Succeeded olan projenin `will` alanı `clean` olur, `reason` düşer ve `lastBuiltAt` şimdiye çekilir → satır `up to date` yazar (ayrı bir görsel kanal yok — v1.11.0). Failed olan projede `lastFailed` işaretlenir → satır `failed`.
- ETA: kalan iş / paralellik, üstel yumuşatma (0.75 eski + 0.25 yeni).
- **Log statüyle tutarlı:** succeeded projenin logu `Build succeeded — 0 errors, N warnings (4.2s)` ile biter; `error CS…` / `Build FAILED` satırları YALNIZ gerçekten başarısız olan projede görünür. Hero/temiz koşuda hiçbir yeşil projenin logunda hata satırı olmaz — statü ve log her zaman aynı şeyi söyler.

## 3.3 Seçim modeli

- Kart, graf düğümü veya stream satırına tıkla → **her yerde senkron seçim**: graf düğümü komşularıyla panele sığdırıp ortalar (460ms), liste karta kaydırır, konsol tam loga geçer (düz kayma, tepeden), panel başlığı `Back` moduna geçer.
- Aynı öğeye tekrar tıkla, `Back`, Esc ya da boş zemin → seçim kalkar; koşuyorsa follow-mode kaldığı yerden sürer. Sim/koşu seçimden etkilenmez. Satıra sağ tık seçim değildir (menü açar); satır içi eylem bloğundaki tıklama (hover ikonları, ⋯ menüsü) seçim sayılmaz.
- **Koşu başlatmak** (Build, Rebuild, F7 Clean, satır eylemi, Resolve) seçimi bırakır, **filtreyi korur**.
- **Esc zinciri** (ilk eşleşen yapılır): exit kartı → What's new / About / Settings → popover/menü (branch, Build menüsü, update kartı) → seçim → koşan build'i **nazikçe durdur** → (drain sürüyorsa) **sert durdur** → durdurulamayan iş için iş başına TEK konsol satırı (`sync can't be stopped — it will finish on its own`, pill sözcüğüyle) → pencereyi `×` gibi kapat (*Close to tray* açıksa tepsiye; kapalıysa Exit — iş varsa kartla, §2.13).
- **Kısayollar:** **F5 = Build** (yalnız başlatır) · **F6 = Rebuild** · **F7 = Clean** (`-t:Clean` koşusu) · **Ctrl+F** = filtre kutusu · **F1 = About** (aç/kapa, modal açıkken de) · **Esc** yukarıdaki gibi · **Ctrl+Q = Exit** · global **Shift+Space** pencereyi göster/gizle · global **Ctrl+Shift+Space** pencereyi açmadan Build (global jestler RegisterHotKey; prototipte yok). Bağlı OLMAYANLAR: `Ctrl+F5`, `Shift+F5`, `Ctrl+F1`, `Alt+B`, F5 ile durdurma.

## 3.4 Bakım işleri: Clean ve Optimize (bakım kutusu)

Her ikisi de action bar'daki ikon grubundan tetiklenir (§2.7-2). Faz `task`; işin ilerlemesi **yalnız konsolda** akar, şerit ve stream adım göstermez. Durdurulamaz (Stop yok), onay dialogu yok.

**Ortak UI davranışı**
- **Tıklama anında liste ve graf boşalır:** listede `No projects`, grafta `No graph` (§2.3, §2.4); Σ ve sayaçlar 0, başlık sayacı gizli.
- Sticky şerit: pill `DEEP CLEAN` / `OPTIMIZE` (amber + spinner) + ikincil metin, glyph yok, **indeterminate** 2px çubuk: `▸ Deep clean — removing bin and obj…` / `▸ Optimize — repairing the workspace…`. Kutudaki butonu `active` + spinner.
- Konsol: önce `clean requested` / `optimize requested`, sonra servislerin satırları (anında basılır — §2.5), en sonda tek özet satırı.
- Event stream: **tek kapanış satırı**, sync tonunda (§2.6) — adım satırı ve canlı daktilo satırı yok.
- Kilitleme: iş sürerken Sync, Build (+ menü oku), branch chip'i, worktree anahtarı, Debug/Release ve diğer bakım butonu disabled; F5/F6/F7 no-op.
- İş bitince şeritte sonuç satırı **yok**: iş kendiliğinden bir **Sync** zincirler (keşif blokları → reveal) ve şeridi Sync devralır. Toast YOK.

**Clean (Deep clean)** — her projenin `bin/` ve `obj/` klasörünü siler ve derleme durumunu sıfırlar; MSBuild çalıştırmaz.
- Konsol: `clean requested` → `build state reset — 36 entries cleared, the next build compiles from scratch` → `Clean complete — 36 projects · 301 folders · 4.4 GB removed`.
- Stream: `Clean — 36 projects, 301 folders, 4.4 GB freed` (uygulamada dosyası kilitli klasör varsa ` · K in use` eki).
- Sonuç: herkes `never built`, sonraki Build tam derleme. Döngü üyelerinin çıktıları da silinir.

**Optimize** — eksik NuGet paketlerini geri yükler, restore'un düzeltemediği referansları raporlar, eski usul projelerin bayat obj artıklarını temizler, ölü cache girdilerini budar; derleme durumunu DEĞİŞTİRMEZ.
- Konsol: `optimize requested` → `checking NuGet packages — 36 projects, 4 need restore` → `msbuild -t:Restore -p:RestorePackagesConfig=true Acme.sln` → `checking references — all resolved` → `checking obj leftovers — 2 old-style projects with stale NuGet files` → `checking caches — pruned 41 entries` → `Optimize complete — 4 restored · 2 obj cleaned · 41 cache entries pruned · 1.1 GB reclaimed`.
- Stream: `Optimize — 4 restored, 2 obj cleaned, 41 cache entries pruned, 1.1 GB reclaimed` (uygulamada iş yoksa `Optimize — nothing to fix`).

**Gerçek uygulama notu:** prototipte iki işin süresi sabittir (~2.4 s); gerçekte her servis kendi işini bitirince ilerler. Sözleşme: konsolda servis satırları + özet, stream'de tek kapanış satırı, şeritte iş boyunca tek ikincil satır, ardından Sync.

## 3.5 Filtreler

Alt bardaki chip'ler ve şeritteki `+N more` liste filtresini kurar (durum filtreleri, satırın görsel durumundan): `building` (yalnız şu an derlenen) · `current` (up to date — yeşil görünen) · `failed` (kırmızı görünen) · `warn` (döngü ∪ bekleyen bağımlılık notu). Seçili chip'ler VEYA ile birleşir, arama ile VE (uygulamada `ProjectFilter.Matches`; graf dimlemesi de aynı eşleşmeyi kullanır). `+N more` `failed` durum filtresini açar. Aynı chip'e tekrar tıkla → kümeden çıkar; `Σ` chip'i hepsini temizler. Aktif filtre Projects başlığında kaldırılabilir chip. Koşu başlatmak filtreyi silmez (§3.3).

## 3.6 Config / perf

- `Debug ↔ Release` bir **Sync** başlatır (ConfigurationChange): konsol ve stream temizlenir, ilk satır `Configuration → Release`, liste ve graf baştan başlar. Yalnız bir Sync başlayabiliyorken açık; Sync istendiği an kilitlenir. Değişim her şeyi kirli saymaz ve sonraki Build bir Rebuild değil düz Build'dir: defter her projenin son başarılı derlemesinin configuration'ını bilir (kaydı olmayan Debug sayılır; başarılı derleme o anki configuration'ı yazar, `-t:Clean` kaydı siler) — kendi dosyası değişmeyen ama kaydı farklı proje `affected` / `… last built in Debug` olur (§2.4, §2.5). Dışarıda derlenmiş projenin o configuration için çıktısı yoksa `never built` / `Will build — its build output is missing.`
- `perf` chip döngüsü modu değiştirir; **koşu sırasında değişim yalnız bir sonraki koşuya** geçer (koşu, başladığı paralellik ve modla sürer). Konsola satır yazılmaz.
- **Kaynak şeridi (§2.1):** CPU ve RAM, uygulama + motor + derleyici süreçlerinin toplamıdır; ilk örnek 0.5 sn'de, sonra 3 sn'de bir; okunamazsa `—`. CPU cap'i koşunun modu belirler (Balanced 70% · Light 40% · Full cap yok) ve yalnız tooltip'te yazılır. **Makineye uydurma:** bellek ya da işlemci darsa motor koşunun paralelliğini modun sınırının altına oturtur (prototipte sınırın yarısı: Balanced 4 → 2); şerit `2/2 Balanced · 4` gösterir, tooltip `2 of 2 — reduced: 3 GB free memory`. Bu bir hata değildir — uyarı rengi, toast, dialog, konsol satırı yok. Prototipte demo anahtarı: props → Demo → **Reduced parallelism (low memory)**.

## 3.7 Resolve cycles (döngü çözme)

Bakım kutusunun üçüncü ikonu (**unlink**, nötr renk; yalnız Sync bir döngü bulduysa enabled). Gerçek bir proje döngüsü tek MSBuild geçişiyle derlenemez; Düz Build ve Rebuild bayat grubu zaten turlarla derler (§3.2); Resolve aynı işin **dar biçimidir** — yalnız grubu ve bayat upstream'ini **tekrarlanan turlarla** (tavan **3**) yakınsatır. Artımlıdır: grup yakınsamışsa (ör. bir Build'in hemen ardından) `Cycles started — 0 cycle members · 0 prerequisites · up to 3 rounds`, her üye `skipped — up to date`, ardından `Completed — …`. Prototipte döngü iki turda yakınsar.

- **Kapsam:** döngü üyeleri + bayat (dirty/failed) bağımlılıklarının kapanışı. **Ardışık** koşar (paralellik 1) — çözüm sıraya duyarlıdır.
- **Hazırlık:** önce bayat bağımlılıklar derlenir (şerit `preparing dependencies`).
- **Tur 1:** üyeler **son bilinen (bayat) referanslarla** derlenir. Turunu bitiren üye kuyruk glyph'inde bekler; sonucu yazılmaz. Üye logu yalnız MSBuild çıktısıdır — ayrı bir döngü uyarısı yazılmaz.
- **Sonraki tur(lar):** üyeler tazelenmiş referanslarla yeniden derlenir (~0.45× süre) → yakınsar; grup bitince her üye tek sonuç alır (`X built (t)`, turlarının toplamı) — `will=clean`, `lastBuiltAt` şimdiye çekilir, deftere yazılır (satır `up to date`). Sonuç normal succeeded; **döngü işareti amber küp + amber uyarı üçgeni** olarak kalır.
- **Konsol:** `cycles requested` + planlayıcı satırları (§2.5); Resolve harici kökleri güncellemez. Turlar konsola satır yazmaz — uygulamada döngü kararları konsola değil `decision.log`'a gider; koşunun özeti stream'dedir.
- **Şerit:** pill `RESOLVE` + `▸ Resolving cycles · round 1/3 · 2/7 · 4s` (hazırlıkta `· preparing dependencies ·`); 2px amber progress = biten/toplam (`n/m` proje sayar, tur kalemi değil). **Stream:** §2.6'daki döngü satırları (`Cycles started — …` · `cycle round r/3 — …` · `cycle converged — …` · `Completed — …`); üyelerin tur sonuçları yazılmaz; aktif satır `X building… · member i/3 · round r/3`.
- **Bitiş:** faz `idle`, şerit normal faz satırına döner — ayrı yeşil sonuç satırı YOK. Sonuç **normal succeeded** — sayaçlara girer; ayrı "resolved" rengi YOK.
- **Kilitleme:** koşu `running` fazındadır — Sync/Build/Clean/Optimize disabled; **Stop çalışır**: scope discovered'a döner, faz `idle`, üyeler çözülmemiş kalır; stream `Stopped — N remaining projects queued`.
- Resolve sonrası Build üyeleri doğal atlar (`up to date`); yeni Sync değişiklik bulursa üyeler yine dirty işaretlenir.

---

## 3.8 Tek proje derleme (v1.10.0)

Proje satırındaki **play** ikonu o projeyi tek başına derler; yanındaki **⋯** menüsü (ve satıra sağ tık) Build · Rebuild · Clean sunar — developer, tam koşuyu beklemeden üzerinde çalıştığı projeyi denemek ister.

**v1.11.0 kapsam kuralı: YALNIZ o proje.** Bağımlılıklar derlenmez; bayat kalan bir bağımlılık uyarı üçgeni doğurur ve logda `last known output referenced` satırı görünür. "Build with dependencies" istenmedi. Satırdan tetiklemek **satıra tıklamak değildir**: seçim bırakılır, filtre korunur, graf odağı açılmaz, konsol ana loga döner; konsol ve stream temizlenir ve koreografi Build ile birebir aynı oynar. Satırdan Clean o projenin çıktılarını siler (satır başlangıç moduna döner, `never built`) ve diğer projeleri varsayılan görünüme çeker — kapsamı tek proje olan bir Clean koşusudur: konsol `clean requested — <Proje> (single project)` + planlayıcı satırları, stream `Clean started — <Proje> (single project)` → `Completed — 1 succeeded · 0 skipped · t`.

- **Kapsam (scope):** hedef proje + yalnız onun **bayat (`will: dirty`) ya da hatalı** bağımlılıkları, geriye doğru özüyle (transitif). **Koşul yok:** atlanmış (güncel), hatalı ya da **döngü üyesi** bir proje de satırından derlenebilir. Döngü üyesi *bağımlılıklar* kapsama girmez (onları tam Build ya da Resolve cycles turlarla derler) — karşılığında standart bayat-referans uyarısı ve dep-üçgen kuralı aynen işler.
- **Scheduler:** kapsamlı koşu kendi sırasını kullanır — yalnız kapsam üyeleri gezilir, kapsam dışı bir satır sırayı **bloklamaz** (`continue`, tam koşudaki `break` değil) ve kapsam dışı bağımlılık "çözülmüş" sayılır. Derlemeye alma işi tek giriş noktasından (`_startBuild`) geçtiği için statü, depIssue, log planı ve aktif satır kuralları tam koşuyla birebir aynıdır.
- **Kurallar değişmez:** koşu tam derlemeyle **aynı motor yolundan** geçer — statü renkleri, building nefesi, spinner, graf beads'i, uyarı üçgeni ve log kuralları birebir aynıdır. Tek fark kapsamın büyüklüğü.
- **Liste yerinden oynamaz:** play'e basınca filtre sıfırlanmaz ve liste remount edilmez (reveal kaskadı tetiklenmez) — imleç altındaki satır kaydığı için yanlış projenin derlenmesi/seçilmesi sorunu böyle çözüldü (v1.10.0).
- **Seçim hedefe GEÇMEZ (v1.11.0):** satırdan tetiklemek satıra tıklamak değildir — seçim bırakılır (filtre korunur), konsol ana loga döner, graf odağı açılmaz.
- **Kapsam dışına dokunulmaz:** atlama dalgası (skip) ve "bitti mi" kontrolü `scope` kümesiyle sınırlı — diğer projeler `discovered` kalır, sayaçları şişirmez. Kapsam dışındaki bağımlılıklar "çözülmüş" sayılır (son çıktıları kullanılır).
- **UI:** Şerit ve alt bar normal koşu gibi davranır (progress = kapsam kadar), ana buton `Stop` olur. Play/Stop ikonları satırdaki diğer ikonlardan bir tık büyük (14px, diğerleri 12px) — ana eylem olduğu için. Kapsam hedefi satırında play ikonu kırmızı **Stop**'a döner ve hover olmadan da görünür; diğer satırların ikonu disabled (`Build in progress — wait or stop it first`).
- **Konsol/stream:** konsol `build requested — <Proje> (single project)` (Rebuild'de `rebuild requested — …`) + planlayıcı satırları; konsola bitiş satırı yazılmaz. Stream `Build started — <Proje> (single project)` (Rebuild'de `Rebuild started — …`) ve bitişte `Completed — …`. Stop → stream `Stopped — N remaining projects queued`, faz `idle`.

---

## 3.9 Uzak uç: geride kalma ve ff-only pull (v1.16.0)

Sync'in `git fetch`'i uzak ucu çözüyor; motor bundan **yerel HEAD'in origin'den kaç commit geride olduğunu** da veriyor (`behind`). Bilgi iki yerde görünür: konsoldaki Sync satırı (§3.1) ve alt bardaki `3 behind` chip'i (§2.7-6a).

**Kural (bilinçli güncelleme):** "araç ana repoda pull yapmaz" kuralı duruyor — araç **kendiliğinden asla** pull etmez. Kullanıcı chip'e basarsa **yalnız ff-only**, **yalnız aktif branch'te**. Gerekçe: fetch zaten "3 commit gerideyim" diyor; bunu gördükten sonra terminale geçip `git pull --ff-only` yazmak aracın işini yarıda bırakmak olur. Fast-forward geri alınabilir, tarihi yeniden yazmaz, birleştirme kararı vermez — aracın git'e yazdığı dört kullanıcı kararından biridir (diğerleri: branch chip'inden checkout — kirli ağaçta stash dahil, §2.8 · harici köklerin build öncesi ff-only güncellemesi, §2.9 · Settings Save'de worktree kurulumu, §2.9; §8).

**Kilit (v1.21.0):** koşu sürerken ve **git işlemi yarıdayken** (merge/rebase/cherry-pick/revert yarım kaldıysa ya da `index.lock` var, §6.4 spec) chip disabled, tooltip nedeni söyler; kirli ağaçta uyarı verir ve pull'u yapmaz (aşağıdaki tablo). Chip `behind > 0` olduğu her an görünür (§2.7-6a).

**Akış (tıklama):** pill baştan `SYNC`, şerit `▸ Pulling origin/<branch>…` (sweep) → "yalnız geride miyim" kontrolü → konsola tek komut satırı `git merge --ff-only origin/<branch>`, ardından sonucun satırı → Sync. Asla merge commit'i, asla rebase, kirli ağaca dokunmaz. Ret pill'i düşürür ve stream'e warn satırı yazar (`pull refused — uncommitted changes` / `pull refused — branch diverged`). Sonuçlar konsola yazılır:

| Sonuç | Konsol | Sonrası |
|---|---|---|
| başarı | `Pulled origin/main — fast-forward a3f81c2..b7e91d4` (info) | chip düşer → **otomatik Sync** (yeni HEAD'in planı hesaplanır); pull satırları konsolda kalır |
| kirli ağaç | `warning: pull refused — uncommitted changes in the working tree; commit or stash them first` (warn) | chip kalır, başka hiçbir şey değişmez |
| ayrışmış | `warning: pull refused — local branch has diverged from origin/main; reconcile it manually` (warn) | chip kalır |
| ağ | `Pull failed — <sebep>` (error) | chip kalır |

- **Pull sonrası Sync konsolu silmez** (v1.21.0 — §3.1'deki genel kendiliğinden-Sync kuralının bir örneği): kullanıcı Sync butonuna basarsa motor resetleyip konsolu temizler, ama pull sonrası zincirlenen Sync geçmişi korur ve altına eklenir — yoksa kullanıcı kendi tetiklediği pull'un sonucunu hiç görmezdi.
- **Prototip sınırı:** başarı yolu simüle edilir (`behind: 3` → tıkla → 0). Üç ret dalı motorda kodlu (`eng.pullResult = 'dirty' | 'diverged' | 'error'`) ama UI'da ve `window.boDemo`'da tetikleyicisi yok; çevrimdışı durum da (`behind = null`) simüle edilmiyor — kural kodda: sayı bilinmiyorsa chip çizilmez ve Sync `warning: git fetch failed — continuing against the local HEAD; distance from origin/main is unknown (…)` + mesafesiz `HEAD <sha>` yazar. Ağ dalının prototip metni `Pull failed — could not resolve host git.acme.local`.

---

# 4. State (WPF/MVVM karşılığı)

- **Engine/VM durumu:** faz (`… running → stopping → done|stopped`, + `task`); proje başına `{status: discovered|queued|building|succeeded|failed|skipped, fresh: bool (bilinmiyor modu), will: dirty|clean|unknown (plan kanalı — görsel kanal DEĞİL), reason: changed|dependency|config|neverBuilt|lastFailed|behind|newer|outputMissing|null (satırdaki karar etiketi; son üçü dışarıda derlenmiş projenin zaman kipi), lastBuiltAt: zaman|null (null = hiç derlenmemiş), timeMode: bool (defterde kaydı yok, dışarıda derlenmiş), builtCfg: Debug|Release|null (son başarılı derlemenin configuration'ı), lastFailed: bool, builtSha (yalnız proje logu başlığı), startAt, doneDur, depIssue: string[]|null, log[]}` + koreografi alanları (`marked`, `markStep`, `markOrder`, `markStagger`, `endStep`, `endOrder`, `endStagger`, `op`) + **kalıcı cycle üyelik kümesi** (statü DEĞİL — v1.7.0); willBuild kümesi (kirli döngü grubunun tüm üyeleri dahil); `ledger` (proje → son başarılı derlemenin configuration'ı; reset'te korunur, `-t:Clean` siler); döngü grubu durumu `cyc {members, upstream, rounds, state: waiting|running|done, round, queue, cur, compiled, spent}` (düz koşuda); uzak uç durumu `{targetSha (yerel HEAD), remoteSha (origin ucu), behind: sayı|null}`; `resolveRun {list, pass, fin, total, startT}`; `cleanRun: bool` (Build menüsü Clean koşusu); `stopStage: null|now|terminating` (Stop'un etiketi); `syncStep {step: start|fetching|analyzing|indexing, found, done, total}` + `indexedRoots` (ilk indeksi yapılmış kopyalar); `work {kind: checkout|pull|worktree, target}|null` (uçuştaki workspace işi); `engine: ok|waking|dead|silent` + `engineMsg`; `runPar` / `runMode` (koşunun oturtulmuş paralelliği ve modu, koşu dışında null); `compactRun` (işlemin başında dondurulan kompakt kip); sayaçlar (building/succeeded/failed/skipped/queued, depIssueCount, cycle=üyelik sayısı); elapsed + ETA; anlatı satırları `{type, time, text}`; stream olayları `{kind, time, project?, text}`; aktif satır.
- **Görsel durum (sayaçlar, filtre, satır, node):** `standing` (çıktının durumu: unknown | current | stale | failed) + koşu katmanı (marked | queued | building | succeeded) → `vstate`. Skipped katman eklemez. Sayaç chip'leri ve filtre kovaları bu görsel durumdan okunur (uygulamada `VisualStatuses.For` · `StandingStatuses.From`).
- **Karar (`reason`) önceliği:** hiç derlenmemiş > son derleme hata verdi > kendi dosyası değişti > bağımlılığı değişti. `will=clean` → `reason=null` (satır `up to date`). Zorlamalı kapsam (Rebuild) içerik gerekçesi **uydurmaz**; konfigürasyon değişimi zorlamaz, yalnız defterdeki configuration'ı farklı projeye `config` gerekçesi verir — ayrıntı arşiv v1.16.0/1.
- **UI durumu:** `selected` (proje adı|null), `hovered` (liste ↔ graf ortak hover; proje adı|null), `filter` (**Set — çoklu, VEYA**), `query`, layout `{mode, col, left, right}` (kalıcı), `layerCfg` (kalıcı), cfg (Debug/Release), perf.
- **Çalışma kopyası:** `RootPath` = Settings'in repository kökü (kalıcı); `wtFolder` = worktree klasörü (kalıcı, tek); `copy: repo|worktree` → aktif kök (`ActiveRoot`). Motora giden her komut ve git kapısı okuması aktif kökü taşır; worktree bir mod değil, kopya geçişi bir kök değişimidir. Gösterilen branch aktif kopyada checkout edilmiş branch'tir. `gitOpInProgress` (merge/rebase/cherry-pick/revert yarım kaldıysa ya da `index.lock` var; branch chip'te amber nokta, checkout ve pull kilitli — §3.9); kirli ağaç kopya başına (`dirty {repo, worktree}`, prototipte demo).
- **Kaynak örneği (§2.1):** `{cpu: yüzde|null, app: MB, engine: MB, compilers: MB, total: MB, cap: 70|40|null}` — ilk örnek 0.5 sn'de, sonra 3 sn'de bir Process sayaçlarından; `null` = okunamadı → `—`. Paralellik: `buildingNow` (o an derlenen — canlı), `runPar` (koşunun oturtulmuş paralelliği; koşu dışında 0), mod sınırı (Full 6 · Balanced 4 · Light 2) — şerit `buildingNow/runPar Mode · sınır` gösterir; `runPar < sınır` iken tooltip `reduced: <neden>` der.
- **Ayarlar (Settings → General, kalıcı):** `startWin` · `startTray` · `closeTray` · `pull` · `replace` · `stash` · `compact` · `idleStop` · `notify` — varsayılanları katalog satırında (§2.9); Export anahtarları `startWithWindows` · `startMinimizedToTray` · `closeToTray` · `pullExternalBeforeBuild` · `replaceFilesInUse` · `stashOnBranchSwitch` · `compactConsole` · `stopEngineWhenIdle` · `showNotifications`.
- Konsol/stream tamponları sınırlı tutulur: anlatı 200 satırlık bir penceredir, tepeye varınca önceki dilim bellekteki son 2 000 satırdan geri yüklenir (§2.5); stream son 150 olayı.

# 5. Statü tablosu (UI metinleri İngilizce)

Satır, node ve sayaç chip'i **görsel durumu** gösterir (çıktının durumu + koşu katmanı, §4); koşu statüsü (`succeeded`, `skipped`…) run-story yüzeylerinde (ribbon özeti, event stream) yaşar.

| Görsel durum | Glyph (satır) | Renk | Ne zaman |
|---|---|---|---|
| bilinmiyor (`fresh`) | kesikli daire; satırda tam opak düz şerit + 4 yaylı nokta; grafta kesikli node border | `--status-skipped-border` | **yalnız hiç Sync yokken**; tooltip yok |
| güncel (`current`) | ✓ daire | #58cb80 | aracın kendi derlemesi ve içerik hâlâ aynı, **ya da** dışarıda (Visual Studio, CI, elle) yapılmış bir derlemenin kanıtı kendi girdilerinin tümünden yeni; ikisi de aynı yeşil, aynı glyph, etikette `up to date`. Atlanan ve Rebuild'in zorladığı ama güncel proje de burada kalır |
| derlenecek (`stale`) | kesikli daire | gri (`--status-skipped-border` şerit/border) | Sync bir sebep buldu (`modified` · `modified · local` · `affected` · `never built`); Build menüsü Clean'iyle temizlenen proje (`never built`) |
| bozuk (`failed`, kanıtlı) | ✗ daire | #ff706a | yalnız derleyici hatası (exit ≠ 0) kanıttır: hata imzası bugünkü imzayla eşit; timeout/Stop kanıt SAYILMAZ, gri `never built` kalır. Etikette `failed` |
| işaretli (`marked`) | kesikli daire, amber şerit + nokta + node | `--amber` | işlem tıklandıktan sonra random dalgayla yanar |
| kuyrukta (`queued`) | saat | #9a9aa2 (halka `--status-queued-border`) | koşuda sırasını bekliyor |
| derleniyor (`building`) | dönen kesikli halka + nefes; grafta beads | amber | şu an derleniyor; `building` sayacı yalnız bunları sayar |
| başarılı (`succeeded`, koşu katmanı) | ✓ daire | #58cb80 | bu koşuda derlendi; koşu bitince `güncel`e yazılır |
| döngü üyesi | satırda normal durum + uyarı üçgeni; grafta **amber küp** (her durumda) | `--amber-text` | tooltip `In a dependency cycle` (gerekçe proje logunda); Build ya da Resolve ile derlense de küp amber kalır |
| dep-affected (ortogonal) | uyarı slotunda ▲ (cycle ile birleşik, tek üçgen) | `--amber-text` | tooltip `Dependency issue: X +2` (konsol rozeti: `Dependency issue: X — last successful output referenced`) |
| ~~skipped~~ | — tire yalnız event stream'deki atlama satırında | `text-dim` | ayrı glyph/renk kanalı YOK; "N skipped" özeti ribbon'da |
| ~~will-build (ortogonal dot)~~ | **v1.11.0'da KALDIRILDI** | — | plan bilgisi renkte ve etikette |

# 6. Assets

**Ürün logosu (Build Orchestrator).** Uygulamanın kendi markası; Delta'ya değil ürüne ait. Üç varyant, üç ayrı iş için — birbirinin yerine kullanılmaz:

| Dosya | Nerede | Notlar |
|---|---|---|
| `docs/design-system/assets/App Icons/app-icon.svg` | Uygulama ikonu: .exe, taskbar, kısayol, bildirim, yükleyici | Near-black tile (`#141417→#0A0A0C`, border `#2A2A30`, radius %11 ≈ 31/286) + amber gradient chevron. **Yalnız zemini olan yerlerde**; 16/32/48/256 PNG/ICO buradan üretilir. |
| `docs/design-system/assets/Logos/app-mark.svg` | Uygulama içi: title bar (19px), About (28px), restart / kapanış ekranı (30px) | Tile YOK (şeffaf). Şeritler DS nötr rampası (`#44444B / #2A2A30 / #A9A9B0 / #EDEDEE`), chevron amber. |
| `docs/design-system/assets/Logos/app-mark-mono.svg` | Tray, mono/tek renk bağlamlar, devre dışı durumlar | Tek renk `#EDEDEE`; hiyerarşi opaklık kademeleriyle (1 / .6 / .5 / .32). Tray'de sistem temasına göre rengi değiştirilebilir. |

**Renk (karar).** Logo DS'in kurumsal paletinde: tile near-black (`#141417→#0A0A0C`), şeritler nötr rampadan, chevron **amber** (`#FFB52E→#8B5907`). Uygulama içi markada chevron gradienti kısaltıldı (`#FFB52E→#C9860C`) — 19px'te dibin near-black zeminde kaybolmaması için. Bu iki marka tonu token'dır: dip durağı `brand-chevron-deep #c9860c`, şeridin ara tonu `brand-strip-dim #44444b` (§1.1; uygulamada `Brush.Brand.ChevronDeep` · `Brush.Brand.StripDim`, AppMark ve tray build göstergesi kullanır). Logo artık UI accent'iyle aynı amber'ı kullanıyor; bu bilinçli — marka arayüzle tek palette konuşuyor. Karşılığında **title bar'daki chevron görsel olarak accent ağırlığı taşır**: o bölgede başka amber öğe (chip, buton, vurgu) konumlandırılmaz.

**Firma logosu (opsiyonel, müşteriye özel).** Uygulama Delta'ya özel değil; kurulumu yapan firma kendi logosunu ekleyebilir. Yalnız iki yerde görünür: **title bar** (ürün markasının sağında, ayraçtan sonra, 10px, %55 opaklık) ve **About başlığı** (`LICENSED TO` bloklu, 13px). Her ikisinde de ürün logosu önde: daha büyük, tam renk, solda. Firma logosu SVG olarak beklenir; dark UI için açık varyantı (ör. `delta-logo-dark.svg`) kullanılır. Yükseklik sınırlıdır, genişlik serbest — wordmark'lar bozulmadan sığar.

**Animasyonlu işaret — tray build göstergesi (§2.14).** Ayrı bir dosya değildir: `app-mark.svg`'nin geometrisi ve renkleri, 3 saniyelik bir zaman çizelgesiyle (uygulamada `Controls/TrayBuildIndicator.xaml`'in storyboard'u; geometri `BrandGeometry.xaml`'den paylaşılır). Kaynak ve önizleme: design system `components/TrayBuildIndicator/` — README parça başına zaman çizelgesini (giriş X, bekleme, oturma, çıkış, solma, eğriler) verir, `preview.html` aynı döngüyü SVG + CSS keyframe ile birebir oynatır; prototipte `BuildApp.jsx` → `TrayBuildIndicator`. Bant 375 × 134, ekranda 187.5 × 67 DIP; zemin yok. Reduced motion'da duruş karesi.

- `docs/design-system/assets/Logos/delta-logo-dark.svg` — örnek firma logosu (dark UI wordmark, amber yay korunur).
- `docs/design-system/assets/App Icons/delta-app-icon.svg` — Delta'nın kendi uygulama ikonu; **artık Build Orchestrator ikonu değil**, yalnız referans olarak duruyor.
- Fontlar: Geist + Geist Mono (şimdilik Google Fonts; pakete gömülecek — bkz. 1.2 not).
- Tokens: `docs/design-system/tokens.css` → WPF ResourceDictionary'ye çevrilecek tek doğruluk kaynağı (§7).

# 7. Dosyalar (referans)

Tasarım repoda iki klasörde durur: `docs/design/` (spesifikasyon, prototipin kaynağı, sürüm kaydı) ve `docs/design-system/` (design system).

```
docs/design/
├── README.md                          klasörün girişi: nerede ne var, sürüm yapmak, koda geçirmek
├── SPEC.md                            bu spesifikasyon
├── VERSIONS.md                        tasarım sürümleri ve uygulandıkları uygulama sürümü (§9)
├── prototype/                         prototipin kaynağı (aşağıda) ve tek dosyayı üreten betik
└── requests/                          repo dışında yapılan tasarım değişiklikleri
docs/design-system/                    design system (token'lar, bileşenler, logolar)
```

Kaynak ve spesifikasyon tek kopyadır: her tasarım sürümü bu dosyalarda yerinde yapılır ve `develop`'ta bir commit'tir (VERSIONS.md kaydı commit'i anar); eski bir sürüm kendi commit'inden açılır. Tek dosyalık prototip sürüm adını taşır (`BuildOrchestrator-prototype-<sürüm>.html`).

- **Tek dosyalık prototip — çift tıkla, tarayıcıda çalışır.** `prototype/` klasöründe `npm install` ve `npm run build` onu `release/BuildOrchestrator-prototype-<sürüm>.html`'e üretir (git-ignore'lu); sunucu gerekmez (Geist fontları Google Fonts'tan gelir, çevrimdışıysa sistem fontuna düşer). Tasarımı CANLI incelemek için bunu kullan. Üstteki sahne şeridi ve sağındaki Demo anahtarları (`auto-play` · `update pill` · `reduced parallelism`; tuvalde ayrıca `speed`) prototip iskelesidir, gerçek uygulamada yoktur — `reduced parallelism` açıkken kaynak şeridi derleme boyunca `2/2 Balanced · 4` gösterir (§2.1).
- **Demo kancaları (`window.boDemo`, tarayıcı konsolundan):** `engineDied(cause?)` · `engineSilent()` · `restartEngine()` · `gitOperation('Merge' | null)` · `createWorktree(folder?)` · `clearWorktree()` · `dirtyTree(n = 3, copy?)` (0 = temiz) · `stash(on)` · `exit()` · ekran görüntüsü testleri için `cfg('Release' | 'Debug')` · `select(name | null)` · `eng`. Uygulamada olup sahnelerden ulaşılamayan durumlar (motor ölümü/sessizliği, yarıdaki git işlemi, kirli ağaç, worktree kurulumu, Exit kartı) bunlarla görülür. Örnek repository kökü `D:\src\ACME` (alt barda workspace adı `ACME`), demo worktree klasörü `D:\src\ACME-wt` (orada `develop` açık).

**`prototype/` klasörü (kaynak):**
- `prototype/Main.dc.html` — Claude'un tasarım tuvalindeki giriş sayfası (sahne şeridi + Demo anahtarları + uygulama çerçevesi); yalnız tuvalde çalışır, sahne listesi tek dosyaya da buradan gider. Tarayıcıda görmek için tek dosyayı üret.
- `prototype/app/BuildApp.jsx` — tüm UI davranışının referans kodu (panel yerleşimi, animasyon süreleri, kopya metinleri buradan doğrulanabilir). `ABOUT_VERSION` = gösterilen **uygulama** sürümü (`1.15.0`); `DESIGN_VERSION` = tasarım sürümü (`2.0.1`), UI'da görünmez. Sahnelerin adı ve açıklaması `Main.dc.html`'in listesindedir (tek dosya da onu kullanır); `SCENES` yalnız ekran etiketi için adı taşır.
- `prototype/app/build-data.js` — simülasyon motoru + 36 projelik örnek grafı (`ACME.*` projeleri; solution'lar `Acme.sln`, `Acme.Sales.sln` …); **scheduler kuralları (§3.2) burada kodlanmıştır** — Core implementasyonunda birebir bu semantik hedeflenir.
- `prototype/app/release-notes.js` — What's new verisi; `npm run build` uygulamanın `CHANGELOG.md`'sinden üretir (§2.11), elle yazılmaz.
- `prototype/standalone.html`, `build.mjs`, `package.json` — tek dosyanın kabuğu ve onu üreten betik; logolar `docs/design-system/assets/`'tedir (§6).

**`docs/design-system/`** — design system **2.1.0**: token'lar (`tokens.css` / `tokens.json` — renk, tipografi, boşluk, efekt), bileşen stilleri (`components/`), README. WPF ResourceDictionary'ye çevrilecek tek doğruluk kaynağıdır.

# 8. Bilinçli KARARLAR / YAPILMAYACAKLAR (tekrar önerme)

- Toast/popup yok · "View failures" butonu yok · perf/Build tooltip'i yok · katman eşleşme sayacı yok · sürüm notları için açılış pop-up'ı yok (About → What's new + ⓘ noktası).
- **Kaynak şeridi (1.25.0):** yeri title bar'ın tam ortası — sticky şeridin sağı ve action bar'da perf çipinin yanı değerlendirildi, İSTENMEDİ (action bar 1240'ta ~175px'lik şeridi taşımaz; şerit koşu değil uygulama bilgisidir). Şeritte metin etiketi yok · CPU'ya minik bar yok · cap metinde yok · değer değişiminde animasyon yok · kısılmış paralellik (`2/2`) için uyarı rengi yok · mod sözcüğü tıklanmaz (mod değişimi yalnız perf çipinde) · açık tema yok (DS ve uygulama tek, koyu tasarım) · tek tasarım, tema anahtarı önerilmez.
- Keşif durumu (1.24.0): hayalet iskelet yok (nefesli, ışık bandı süpürmeli ve sabit soluk hâli denendi) · döngüsel animasyon/spinner yok · kaynak (solution/yol) adı yok · paydalı sayaç (`4/7 solutions`) yok · iki satırlık Repository/External sayaç tablosu yok (arşiv v1.24.0).
- Güncelleme (1.23.0): her zaman görünen sürüm etiketi yok · hapta amber yok · indirme sürerken gösterge yok · kurulum sonrası toast yok (§2.12). **Uygulamanın kararıyla (v1.26.0'da işlendi):** restart ekranı tek dürüst adımdır (`Closing…`; `Installing` / `Starting` adımları ve pencere içi yeniden açılış yok — kurulum süreç çıktıktan sonra olur) · kart dialog açılınca kapanır (dialogun altında durmaz).
- Clean/Optimize/Resolve: etiketli buton yok (bar taşıyor), onay dialogu yok; Clean/Optimize durdurulamaz, Resolve durdurulabilir (`running` fazı); ayrı loading overlay'i yok · şeritte yeşil `Clean/Optimize complete` sonuç satırı yok — iş Sync zincirler (uygulamanın kararı) · Build menüsünün Clean'i adımlı bir görev değil `-t:Clean` koşusudur.
- **Exit (uygulama, v1.26.0'da işlendi):** iş yokken onaysız çıkar; iş varken tek kart (Stop and exit / Wait and exit) — ayrı onay dialogu yok · Exit butonu ve kartı amber taşımaz · git yazımı ortasında kesilmez, beklenir.
- **Klavye (uygulama):** F5 yalnız başlatır, durdurmaz (Esc durdurur) · Rebuild `F6`, Clean `F7` · `Ctrl+F1`, `Alt+B`, `Ctrl+F5`/`Shift+F5` yok; global jestler `Shift+Space` / `Ctrl+Shift+Space`.
- **Continue ve Retry failed YOK (1.7.0)** — Build her zaman stale set'i derler (Stop sonrası devam + hata sonrası retry bunun doğal sonucu). Yeniden önerme.
- Building efekti yalnız sabit "nefes"; süpürme/parlama denendi, İSTENMEDİ.
- Konsol/stream akışı klasik (en yeni altta); "en yeni üstte" İSTENMEDİ.
- Cull/agrega (çok-solution rollup) görünümü GEREK YOK.
- Emoji, gradient, amber dışı dekoratif renk, panel gölgesi, backdrop-blur YASAK.
- Varsayılan katman konfigürasyonu BOŞ (başlıksız tek grup, grafta tek bant); örnek katman yükleme yok — `Add layer` boş satır ekler, placeholder'lar standart iskelettir (v1.19.0).
- **Konsol ve graf (uygulamanın kararları, v1.26.0'da işlendi):** konsol başlığında statü glyph'i/sözcüğü yok · `build in progress` işareti yok · panel geçişi düz kayma, perspektif tilt yok (GPU belleği) · satırlar sarmaz · yeşil `success` konsol satırı yok · grafta gezinme ipucu yok · stream'de `No events yet.` yok · karar etiketinde zaman kuyruğu ve `failed · retry` yok · satır şeridinde dikey iç boşluk yok · katman başlığı yalnız fare.
- **v1.20.0:** renk ÇIKTININ DURUMUNU anlatır; tek kanal ilkesi korunur (şerit + nokta + glyph + node border + küp aynı renk). Ayrı will-build noktası ve turuncu döngü işareti GERİ GELMEZ; döngü yalnız amber küp + amber üçgen + log ile ifade edilir. Kırmızı badge eklenmez (kırmızı = kanıtlı bozuk: derleyici hatası, bugünkü imzayla eşleşiyor). Çıktının tarihi tek başına güncel demeye yetmez: araç kendi derlediğinde kanıt içerikten gelir, başkasının derlediğinde kanıt yalnızca zamandan gelir — ikisi karıştırılmaz.
- **v1.11.0:** proje listesi KART yapısına geçmez (36 projede yoğun liste doğru form) · satırdan "Build with dependencies" eklenmez · bitiş koreografisi (neon) satırlara uygulanmaz, yalnız grafta yaşar · satır tooltip'i yalnız uyarı üçgeninde (gecikmeli/satır üstü tooltip denendi, istenmedi) · işaretleme dalgası derleme sırasıyla değil RANDOM akar.
- Cycle ile dep-affected uyarıları TEK amber üçgende birleşti (v1.7.0) — ayrım tooltip satırlarında; ayrı sütun/ayrı renk taşınmıyor.
- Dep-affected tooltip metni yeterli (`Dependency issue: X — last successful output referenced`); tıklanabilir bağımlılık adı, satır geneli hover ve ek "output may be stale" satırı İSTENMEDİ.
- `⚠` chip'i (döngü ve bekleyen bağımlılık notu birleşik; v1.20.0'da eski `▲` dep-affected chip'i buna katıldı) yalnız listede karşılığı varken görünür; kalan dört sayaç chip'i (Σ · building · ✓ · ✗) her zaman durur. **`○` (to build) chip'i yok** — v1.20.0 tasarımı onu öneriyordu, uygulama kullanıcı kararıyla (2026-09-20) kaldırdı; uygulamanın kararı geçerlidir, yeniden önerilmez.
- **v1.16.0:** satırda commit çifti (`a3f81c2 → b7e91d4`) geri gelmez — kararı commit değil diskteki içerik verir; yuvada karar etiketi durur. Etikette statü rengi, ikon ve DS Tooltip yok; satırda tooltip taşıyan tek öğe hâlâ uyarı üçgeni.
- **v1.16.0 / v1.21.0 / v1.26.0:** araç git'e **kendiliğinden** yazmaz — mutasyon yüzeyi yalnız **dört kullanıcı kararı**: (a) kayıtlı harici kökler — build anında, yalnız *Pull before build* açıksa, kendi köklerinde ff-only güncelleme; (b) `N behind` chip'ine basınca ana repoda ff-only pull (yalnız aktif branch, kirli ağaçta ret); (c) branch chip'inden checkout — kirli ağaçta ayar karar verir (*Stash and switch* açıksa `git stash push -u`, kapalıysa branch kartı sorar); diğer kopyada açık branch'e geçiş denenmez; (d) worktree kurulumu — yalnız Settings Save'de, klasör yoksa ya da boşsa, `git worktree add --detach` (§2.8, §2.9, §3.9). Merge commit'i, rebase, force-push, otomatik pull/checkout/stash YOK. Chip yerine pasif uyarı, şeritte metin ve chip menüsü denendi — İSTENMEDİ (arşiv v1.16.0).
- **Worktree (v1.21.0 → uygulamanın kararı):** v1.21.0 worktree'yi UI'dan kaldırmıştı (chip + popover + hedef listesi + silme). Uygulama onu **farklı bir biçimde geri getirdi** ve bu karar geçerlidir: Settings › Workspace'te **tek** worktree klasörü + alt barda etiketsiz kare anahtar (§2.7-5b); worktree bir derleme modu değil ikinci çalışma kopyasıdır, geçiş bir kök değişimidir. Eski `worktree: off ▾` chip'i, popover'ı, `Build in worktree` anahtarı, hedef listesi, `Delete worktree` ve About'taki `Worktree pool` satırı GERİ GELMEZ.

---

# 9. Sürüm geçmişi

Tasarımın sürümleri, numaralama ve her sürümün uygulamaya düşen işi [VERSIONS.md](VERSIONS.md)'dedir; design system'inkiler [docs/design-system/VERSIONS.md](../design-system/VERSIONS.md)'de. Bu dosya her zaman güncel tasarımı anlatır: değişen davranış §1–§8'de yerinde yeniden yazılır.
