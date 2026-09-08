# Tasarım sadakati kusurları — TDD dökümü

Branch: `fix/design-fidelity-defects` · Tasarım otoritesi: `.claude/outputs/2026-08-05-01-26-design-v1.12.1/`
(`prototype/app/BuildApp.jsx`, `prototype/_ds/.../_ds_bundle.js`, `README.md`).

Sıra: **bloklayıcı → önemli → kozmetik**. Her görev kendi KIRMIZI testiyle başlar.

---

## T1 — Popover'lar ikinci tıkta kapanmıyor (BLOKLAYICI)

**Kusur.** `StaysOpen="False"` bir `Popup` açıkken tetikleyici `ToggleButton`'a basınca popup önce KENDİ
kapanma yolundan geçiyor (`IsOpen=false` → iki-yönlü bağ `IsChecked=false`), ardından aynı tık ToggleButton'u
yeniden işaretliyor → popup tekrar açılıyor. Repoda bu deseni kesen hiçbir kod yok.

**Konum (değişen kod olacak yerler, hepsi aynı desen):**

| # | Yer | Tetikleyici | Popup |
|---|---|---|---|
| P1 | `Views/ActionBar.xaml:34-42` | `PART_BranchChip` | `PART_BranchPopup` |
| P2 | `Views/ActionBar.xaml:47-55` | `PART_WorktreeChip` | `PART_WorktreePopup` |
| P3 | `Resources/Controls.xaml:276-298` | `PART_Menu` (Build chevron) | `PART_MenuPopup` |
| P4 | `Views/ProjectRowActions.xaml:67-71` | `PART_MoreButton` (⋯) | `PART_RowMenu` |
| P5 | `Views/ProjectRowActions.xaml:77-80` | `PART_VsButton` | `PART_VsChooser` |

**Kanıt / mevcut kod (değişmez):** `Views/PopoverBase.cs:88-98` yalnız açılış tarafını (içerik tazeleme,
pop-in, odak) yönetir; kapanış/yeniden-açılış jesti hakkında hiçbir şey söylemez.

**Tasarımın kuralı.** `BuildApp.jsx:2399` `onClick={() => { setBranchPop(!branchPop); … }}` — tetikleyici
DEĞİŞTİRİR (toggle), açmaz. Satır menüsünde açıkça yazılmış: `BuildApp.jsx:657`
`if (menuOpen) { setMenu(null); return; }`.

**Kırmızı test.** Üretim sırasını birebir sürer (popup'ın kendi dış-tık kapanışı + ToggleButton'un tıkı):
popup açıkken `popup.IsOpen = false` (WPF'in capture yolunun yaptığı) → tetikleyiciye gerçek bir tık
(`MouseInput`) → **popup kapalı kalmalı**. Bugün açılıyor. Beş yer için tablo-testi.

**Fix.** Tek yer: `Controls/PopoverToggle.cs` — `Bind(ButtonBase toggle, Popup popup)`. Popup kapanınca
damga vurur; tetikleyicinin `PreviewMouseLeftButtonDown`'ı damgadan bu yana geçen süre koruma penceresinin
altındaysa tıkı yutar (`e.Handled = true`). Zaman kaynağı enjekte edilebilir → test deterministik. Beş yer
bu tek binder'ı çağırır (kopya YASAK).

---

## T2 — Split button'ın sağ yarımı pasifleşmiyor + ayraç iki çizgi (BLOKLAYICI + KOZMETİK, tek kök)

**Kusur A (bloklayıcı).** `PART_Split.IsEnabled = hasWs && !syncing` (`Views/ActionBar.xaml.cs:501`) ama sol
yarım ayrıca `PrimaryCommand`'ın `CanExecute`'uyla da kısılıyor (`Resources/Controls.xaml:271-273`). Komut
`CanExecute=false` verdiğinde YALNIZ sol yarım 0.45 opaklığa düşüyor; chevron yarımı tam parlaklıkta kalıyor.
Kaynağın kendi yorumu da bunu söylüyor — `ActionBar.xaml.cs:500` "primary komut running'i ayrıca kısar".

**Kusur B (kozmetik).** Ayraç ayrı bir `<Rectangle Width="1">` (`Resources/Controls.xaml:275`), iki butonun
ARASINDA duran bağımsız bir öğe. `SnapsToDevicePixels`/`UseLayoutRounding` almadığı için kesirli DPI'da iki
yarım-yoğunluklu piksel sütununa yayılıyor ("iki çizgi"). Ayrıca butonların `Opacity` pasiflik trigger'ından
etkilenmiyor → Kusur A'nın görünen yüzünün ikinci yarısı.

**Tasarımın kuralı.** İki yarım AYNI `disabled` ifadesini alır (`BuildApp.jsx:2417` ve `:2419` birebir aynı
koşul) ve ayraç sağ yarımın KENDİ `borderLeft: '1px solid var(--amber-dim)'`'idir (`BuildApp.jsx:2421`).

**Kırmızı test.** (a) `SplitButton.IsEnabled=true` + `PrimaryCommand.CanExecute=false` iken `PART_Menu`
etkin/opak kalıyor — kapanınca sol yarımla aynı etkin duruma düşmeli. (b) Şablonda iki yarım arasında
bağımsız bir ayraç öğesi bulunmamalı; ayraç `PART_Menu`'nün `BorderThickness`'i olmalı.

**Fix.** Şablonda `Rectangle` kalkar; `PART_Menu`'ye `BorderThickness="1,0,0,0"` +
`BorderBrush={DynamicResource Brush.AmberDim}` ve `IsEnabled="{Binding IsEnabled, ElementName=PART_Primary}"`.
Tek doğruluk kaynağı sol yarımın ETKİN durumu olur.

---

## T3 — Building spinner yanlış çizim (ÖNEMLİ)

**Kusur.** `Resources/Controls.xaml:961-981` `Icon.Spinner` (270° yay, `Resources/Icons.xaml:233`) çiziyor,
`Controls/BuildingSpinner.cs:26` 900 ms döndürüyor, varsayılan renk `Brush.TextSecondary`
(`Controls.xaml:962`).

**Tasarımın kuralı — üç kaynak da aynı şeyi söylüyor:**
- `BuildApp.jsx:162-171` `BuildingSpin`: `<circle r="6.7" strokeWidth="1.5" strokeDasharray="2.3 2.5"
  opacity="0.9">`, renk `--amber-text`, `bo-rot` = **1.4 s** lineer sonsuz (`BuildApp.jsx:17`).
- design README:69 — "Building spinner = discovered'ın kesikli halkasının amber, dönen hali;
  `stroke-dasharray 2.3 2.5`, 1.4 s. (Ayrı bir 'spinner' çizimi DEĞİL.)"
- **ARCHITECTURE.md §14.4 ZATEN doğruyu yazıyor** — "not a separate drawing — it is the start-mode dashed
  ring, in amber, rotating linearly over 1.4 s". Yani doküman tasarımla hemfikir, sapan taraf koddur.

Kaynağın kendi yorumu bu sapmayı "hakemlik bekliyor" diye kaydetmiş (`Controls/BuildingSpinner.cs:14-19`);
hakemliği kullanıcı verdi: **tasarım kazanır**.

**Ölçü notu (kopya YASAK).** Halka `Icon.StatusRing` olarak ZATEN var (`Icons.xaml:216`, `r=6.7`) ve kesik
deseni `Icon.StatusRing.DashArray` = `2.3 2.5` (`Icons.xaml:219`). SVG'de dash birimi KULLANICI BİRİMİ, WPF'te
`StrokeThickness` ÇARPANIDIR: glyph halkası 1 kalınlıkta olduğu için sayılar orada birebir geçiyor, spinner
1.5 kalınlıkta olduğu için aynı desen `2.3/1.5` ve `2.5/1.5` olarak türetilmeli. Türetme kodda yapılır —
ikinci bir sayı tablosu YAZILMAZ.

**Kırmızı test.** (a) `BuildingSpinner` şablonu `Icon.StatusRing`'i çizer, `Icon.Spinner`'ı DEĞİL;
(b) dönüş süresi 1400 ms; (c) kesik deseni kullanıcı-birimi olarak `Icon.StatusRing.DashArray` ile aynı;
(d) varsayılan renk `Brush.AmberText`, opaklık 0.9.

**Not.** `Icon.Spinner` sahipsiz kalır → silinir (`IconGeometryTests` referansı da birlikte).

**Etki alanı (tek çizim, üç yer):** satır statü glyph'i (`Controls/StatusGlyph.cs`), ribbon
(`Views/StickyRibbon.xaml:40` + `.xaml.cs:439`), aksiyon barı building sayaç chip'i
(`Views/ActionBar.xaml.cs:278`).

---

## T4 — Satır ⋯ menüsü fazla solda açılıyor (ÖNEMLİ)

**Kusur.** `Views/ProjectRowActions.xaml:67-71` menüyü ⋯ düğmesine çakıp `HorizontalOffset="-118"` sihirli
sayısıyla sola kaydırıyor. ⋯'den sonra iki ikon daha (folder, VS) olduğu için menünün sağ kenarı satırın sağ
kenarından ~44 px içeride kalıyor — kullanıcı gözlemi: "hep altta ama baya solda".

**Tasarımın kuralı.** `BuildApp.jsx:601-610` — menü ⋯'ye DEĞİL, liste panelinin sağ kenarına çakılır
(`position:absolute; right: 8`), dikeyde `top = satır üstü + satır yüksekliği − 3` (`BuildApp.jsx:659`) ve
panelin görünür alanına clamp'lenir (`:661-664`).

**Kırmızı test.** Realize testi: liste + satır kurulur, ⋯ menüsü açılır; menü kabuğunun SAĞ kenarı ile
listenin görünür alanının sağ kenarı arasındaki fark 8 px olmalı. Bugün ~44 px.

**Fix.** Sihirli sayı kalkar; yerleşim `Placement=Relative` + liste kabına göre hesap, ya da
`PlacementTarget` = satır kökü + `Placement=Bottom` ile sağa hizalı offset (menü genişliği kabuktan okunur,
literal yazılmaz). Hesap saf bir sınıfa çıkar → test doğrudan aritmetiği de sürebilir.

---

## T5 — OSYS ↔ branch chip aralığı (KOZMETİK)

**Kusur.** `Views/ActionBar.xaml:26` workspace etiketi `Margin="0,0,2,0"`, branch chip'inin Grid'inde sol
margin YOK → aralık 2 px, ikisi birleşik okunuyor.

**Tasarımın kuralı.** `BuildApp.jsx:2393-2396` — bar `display:flex; gap: 8` (`:2325`) + span'in
`marginRight: 2` = toplam **10 px**.

**Kırmızı test.** `WorkspaceLabelTests`'e: etiketin sağ kenarı ile branch chip'inin sol kenarı arasındaki
gerçek boşluk 10 px olmalı.

**Fix.** Barın kendi öğe-arası boşluk kuralı (8) branch Grid'ine de uygulanır, etiketin 2'si kalır.

---

## Kapsam DIŞI — incelendi, kusur değil

**Satır zemininin renk geçişi VAR.** `Views/ProjectRow.xaml.cs:52` her satıra donmamış kendi
`SolidColorBrush`'ını verir (`:84`), `ApplyBackground` (`:476-484`) onu `MotionTokens.TransitionColor` ile
geçirir; o da `Duration.Fast` (120 ms) + `KeySpline.EaseStandard` okur (`MotionTokens.cs:238-246`) —
tasarımdaki `transition: background var(--duration-fast) var(--ease-standard)` (`BuildApp.jsx:683`) ile
birebir. Building sırasındaki amber nefes katmanı da yerinde (`ProjectRow.xaml:21`).

**Satırdaki play ve ⋯ menü maddelerinde hover yok — bilinçli.** Tek-proje koşusunun arka ucu yazılmadığı
için play `IsEnabled=false` (`Views/ProjectRowActions.xaml.cs:26`) ve menü maddeleri `IsEnabled=false` +
hover zemini KASITEN takılmamış (`Views/ProjectRowMenu.xaml.cs:88-93`). Bu bir kusur değil, kayıtlı bir
eksik özellik.

**⋯ düğmesinin hover ZEMİNİ görünmez — tasarımın kendi token'larından.** `Brush.SurfaceHover` ve
`Brush.SurfaceRaised` AYNI renktir (`Tokens.xaml:35` ve `:39`, ikisi de `#1a1a1e`), çünkü tasarımda da
`--surface-hover` ve `--surface-raised` aynı `--neutral-850`'dir (`tokens/colors.css:12` ve `:15`). İkon
butonunun hover zemini `surface-raised` (`_ds_bundle.js:255`), satırın hover zemini `surface-hover`
(`BuildApp.jsx:681`) → hover edilmiş bir satırın üstünde ikon butonunun zemini prototipte de görünmez. Orada
tek geri bildirim ikonun RENGİdir (`text-secondary` → `text-primary`, `_ds_bundle.js:256`) ve bizim stilimiz
de bunu ilan eder (`Controls.xaml:388-392`). Kod tasarımla uyumlu; değiştirmek tasarım kararı olur.
