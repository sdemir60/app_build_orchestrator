# Tasarım v1.12.1 → v1.14.0 aktarımı — TDD dökümü

> Kaynak paket: `.claude/outputs/2026-09-09-00-28-design-v1.14.0/` (README §9 sürüm geçmişi bağlayıcıdır).
> Uygulamanın bulunduğu yer: **v1.12.1** (`CursorHop`, `StartMode`, `VisualStatus` yorumları bunu pinliyor).
> Çalışma branch'i: `feat/design-v1.14`.

## Kapsam kararı

Kullanıcı kararı: **v1.14.0'ın harici projeler (External projects) motoru bu işte YOK.** O özellik
`feat/external-projects-prebuild` branch'inde uçtan uca uygulanmış (18 commit, Core/Externals + TFVC + IPC +
Supervisor + Settings UI) ve ayrı bir oturumda main'e taşınacak. Buradaki iş, v1.13.0 → v1.14.0 arasındaki
**tasarım** maddeleridir; v1.14.0'dan yalnız harici projelerden bağımsız olan **Settings gövde kaydırması ve
dialog genişliği** alınır.

Çelişki kuralı (kullanıcı kararı): **tasarım sürüm notu yenidir ve kazanır.** Notun pinlediği eski kural,
onu pinleyen testle birlikte yeni kurala göre yeniden yazılır (CLAUDE.md "davranış değişince testi de
değişir").

## Zaten karşılanan iki madde (iş yok)

| Tasarım maddesi | Neden iş yok |
|---|---|
| v1.13.2 — "⋯ menüsü toggle: aynı butona ikinci tıklama menüyü kapatır" | WPF'te zaten var: `App/Controls/PopoverToggle.cs`, `ProjectRowActions.xaml.cs:34` (`Bind(PART_MoreButton, PART_RowMenu)`). Prototipteki kusur DOM'a özgüydü. |
| v1.13.2 — "finale seçim ve filtre dimlemesini ezer (`es` dalı en başta)" | `GraphView.ApplyNodeOpacity` zinciri zaten `_endStep` dalını seçim/filtre çözümünden ÖNCE alıyor (`GraphView.xaml.cs:1301`). |
| v1.13.2 — "graf panel ölçüsü callback ref ile kurulur, ölçülmeden çizim yok" | Web'e özgü (`ResizeObserver` kopan node'da kalıyordu). WPF'te ölçüm `SizeChanged` üzerinden gelir; T7'de doğrulanır, kusur çıkmazsa iş yok. |

## Task listesi

Her task: önce kusuru yakalayan **kırmızı** test, sonra fix, sonra o task'ın testi + tam süit yeşil.

### T1 — Başlangıç modu satırda tam opak (v1.13.2)

**Kural (yeni):** Sync sonrası sol şerit ve 4 yaylı nokta `--status-skipped-border` tonunda **tam opak**
çizilir. İşlem başlayınca şeritte opaklık/renk geçişi olmaz; yalnız halka → dolu nokta çapraz-sönümü kalır.
**Eski kural:** şerit 0.5, halka 0.85 (v1.12.0).
**Ölçüm/gerekçe:** prototip `BuildApp.jsx:745` (`opacity: 1`) ve `:756` (halka `opacity: vs==='fresh' ? 1 : 0`).

- Dosya: `App/Controls/StartMode.cs` — `FaintOpacity 0.5 → 1.0`, `RingOpacity 0.85 → 1.0`.
- Şeridin opaklık animasyonu (`ProjectRow.SetStripeFill`) hedefi artık her iki durumda 1 olduğu için
  görünmez hâle gelir; **kaldırılmaz** (kural tek yerde durur, sabit değişince davranış değişir).
- Test: `StartModeTests` / `ProjectRowTests` — eski 0.5/0.85 iddiası yeni kurala göre yeniden yazılır;
  doc'una eski iddia + değişme gerekçesi yazılır.

### T2 — Dalgada ad, şerit ve nokta aynı anda yanar (v1.13.2)

**Kural (yeni):** proje adının `text-secondary → text-primary` geçişi de `markOrder × markStagger` kadar
gecikir (`color 200ms + waveDelay`). **Eski:** ad rengi anında değişiyordu (bütün adlar dalga başında birden
beyazlıyordu), şerit/nokta ise sıralı geliyordu.
**Kaynak:** prototip `BuildApp.jsx:700` (`waveDelay`) + `:761`.

- Dosya: `App/Views/ProjectRow.xaml.cs` — `ApplyStatusVisuals` içindeki `PART_Name.SetResourceReference`
  anlık; dalga sırasında renk `MarkingChoreography.LightMs` (200ms) + satırın dalga gecikmesiyle animasyona
  bağlanır. Gecikme kaynağı şerit/nokta ile **aynı** olmalı (kopya YASAK → tek yerden okunur).
- Test: dalga adımında ad rengi geçişinin gecikmesi = şeridin gecikmesi.

### T3 — Listede sönme/geri gelme kalktı (v1.13.2)

**Kural (yeni):** satır opaklığı koreografi boyunca **sabit 1**; veda ve neon finali yalnız graf
node'larında. **Eski:** kapsam dışı 0.3 (`RowEnvOpacity`), kapsam 0.45 (`MarkedOpacity`).

- Dosya: `App/Controls/MarkingChoreography.cs` (`RowEnvOpacity` kalkar) ·
  `App/Services/OperationChoreographer.cs:126` (satırlara `RowFade.None` yazılır).
- `MarkedOpacity` / `NodeEnvOpacity` **kalır** — graf tarafı değişmedi.
- Test: `ChoreographyTests` — satır fade'inin her adımda 1 olduğu pinlenir; eski `RowEnvOpacity` iddiaları
  yeniden yazılır.

### T4 — Şerit pill'i satırdan tetiklemede de sade (v1.13.2)

**Kural (yeni):** `opLabel()` hedef adını eklemez — `BUILD — Sales.Core` değil `BUILD`.
**Gerekçe (tasarım):** tek proje derlemesi şeritte tam koşudan ayırt edilmez, süreç birebir aynıdır.

- Dosya: `App/ViewModels/OperationLabel.cs` — `Compose` kalkar; çağıranlar yalnız etiketi yazar.
- Test: `OperationLabelTests` — eski "hedefli pill" iddiası yeni kurala göre yeniden yazılır.

### T5 — Sync listeyi başa alır (v1.13.2)

**Kural (yeni):** `revealKey` değiştiğinde (Sync · workspace kaydı) ve **seçim yokken** liste scroll'u
yumuşak 0'a döner. Build/Rebuild/Clean/Resolve ve satırdan tetiklenenler scroll'a **dokunmaz**
(kullanıcı kararı: imlecin altındaki satır kaçmasın).

- Dosya: `App/Controls/StickyLayerList.xaml.cs` — reveal tetiklendiğinde koşullu `AnimateScrollTo(0)`.
- Test: reveal + seçim yok → hedef 0; reveal + seçim var → scroll'a dokunulmaz.

### T6 — Bitiş koreografisi tam görünümde oynar (v1.13.2)

**Kural (yeni):** koşu biterken graf **seçim odağını bırakır** ve fit-all pozisyonuna **460ms** glide ile
döner; `hold` fazı (900ms) bu geçişi karşılar. Finale boyunca akan çizgiler, seçim halkası ve ad etiketi
kalkar. **Seçim silinmez ama odak geri de gelmez** — fit görünüm final hâldir; odak yalnız kullanıcı yeni
bir proje seçtiğinde (ya da aynısını yeniden seçtiğinde) açılır.

- Dosya: `App/Graph/GraphView.xaml.cs` — `PlayEndFinale` girişinde odak bırakma + kamera glide;
  `focusOff` durumu bırakılan seçimi hatırlar; seçim kromu (`_selectionEdges`, halka, ad etiketi) finale
  boyunca gizlenir.
- Test: finale başlayınca kamera hedefi fit-all; `SelectedNode` **korunur**; seçim kenarları temizlenir.

### T7 — Odak modunda derlenen node tam opak (v1.13.2)

**Kural (yeni):** seçim dimlemesinde `live` istisnası — o an **building** olan node odak kümesinde olmasa da
1.0'da kalır. **Gerekçe:** beads halkası amber dönerken gövdenin 0.1'de kalması "derlenmiyor" gibi okunuyordu.

- Dosya: `App/Graph/GraphNodeOpacity.cs` — `Resolve`'un `hasSelection` dalına building istisnası.
- Test: `hasSelection: true, inFocus: false, status: Building` → `Full`.

### T8 — What's new: title bar butonu, sparkle ikonu, Ctrl+F1, okunmadı noktası (v1.13.0 + v1.13.1)

- **İkon:** sparkle (tek 4 kollu yıldız, 1.7px stroke, 13px) — `Icons.xaml`'a yeni geometri.
- **Yer:** dişli ile ⓘ **arasında**, `Ds.IconButton` sm.
- **Kısayol:** `Ctrl+F1` = What's new (toggle) — `KeyboardShortcuts.WindowBindings` + yeni `WindowIntent`.
- **Esc sırası:** What's new → About → Settings → popover/menü → seçim.
- **Okunmadı noktası:** kayıtlı sürüm ≠ `AppIdentity.Version` ise butonda 5px amber nokta (top 2 / right 2) +
  tooltip `What's new in <sürüm> (Ctrl+F1)`; dialog **açıldığı anda** görüldü işaretlenir, o sürüm için bir
  daha gelmez. Kayıt yoksa (ilk kurulum) nokta vardır. Kalıcılık `UiStateStore`.
- **Shortcuts tablosuna satır:** `ShortcutCatalog` — açıklama tek yerde (title bar tooltip'i de oradan okur).
- Test: bağlama tablosu, Esc zinciri sırası, nokta kuralı (üç durum: kayıt yok · eşit · farklı).

### T9 — `NotesDialog`: What's new kendi dialogu (v1.13.0 + v1.13.1)

- **Yeni dialog** (620px): About kabuğu (`surface-raised`, `border-strong`, radius-lg, overlay gölge, scrim,
  180ms fade + 6px) ama **logo bloğu ve Segment YOK**. Başlık: `What's new` (15px/600) + `Release notes for
  Build Orchestrator.` (12px `text-dim`); sağda **iki satırlı blok** — 10px caps `INSTALLED VERSION`
  (`text-faint`) + mono 15px/500 sürüm (`text-primary`), sağa yaslı, taban çizgileri hizalı.
  Başlık sağ padding'i **28px** (gövdenin 10px scrollbar'ı içeriği içe aldığı için).
- **Gövde:** **sabit 400px** + dikey scroll (min değil — `Earlier versions` açılınca dialog uzamaz).
- **Sürüm başlığı:** kurulu sürümde `CURRENT` metni yerine **`INSTALLED` nötr çipi** (17px yüksek, `0 6px`,
  `surface-raised` + 1px `border-strong`, radius-xs, 10px caps `text-dim`).
- **`Earlier versions (N)`** butonu içerik koluna hizalanır (butonun kendi yatay padding'i iptal).
- **Footer:** yalnız `Close`. `Copy diagnostics` About'ta kalır.
- **About sadeleşir:** Segment `Shortcuts | Environment | Third-party`; `WhatsNewTab`, `openOnWhatsNew`,
  `NotesSeen` kalkar; ⓘ ve F1 **her zaman Shortcuts**'ta açar.
- Liste kuralları (kategori blokları, son 3 açık, kind sırası) v1.9.0'dan **değişmedi** — `ReleaseNotes` ve
  blok kurma kodu taşınır, kopyalanmaz.
- **Realize testi zorunlu** (yeni XAML kökü): `window.Content` üzerinde.

### T10 — About 660px + Environment'ta yatay kaydırma (v1.13.1)

**Kural (yeni):** değer hücresi kırpılmaz, **kaydırılır** — `nowrap` + yatay scroll, **scrollbar görünmez**,
hücrenin üzerinde fare tekerleği yolu sağa kaydırır. Ellipsis + tooltip **denendi, İSTENMEDİ**.
Genişlik 620 → **660px** (85 karakterlik MSBuild yolu 12px mono'da ~610px, tek satıra yine sığmaz).
Gövde min-yüksekliği 236px korunur.

- Dosya: `App/Views/AboutDialog.xaml(.cs)` — satır şablonunda `TextTrimming` + `ToolTip` kalkar, değer
  hücresi gizli-scrollbar'lı yatay `ScrollViewer` olur; wheel yönlendirmesi (yalnız taşan hücrede).
- Test: taşan değer için kırpma yok; wheel yatay ofseti artırır.

### T11 — Settings geri bildirim metinleri kısaldı (v1.13.1)

| Eski | Yeni |
|---|---|
| `Click again to clear root and all layers` | `Click again to clear` |
| `Cleared — nothing is applied until you save` | `Cleared — save to apply` |
| `Exported — build-orchestrator-settings.json` | `Exported — settings JSON` |

Onay dialogu **yok** (iki aşamalı ikon onayı korunur). Armed tooltip'i de yeni metni taşır.

- Dosya: `App/Views/SettingsDialog.xaml.cs` · `App/ViewModels/SettingsFile.cs` (export metni).
- Test: metinleri pinleyen mevcut testler yeni metinlere göre yeniden yazılır.

### T12 — Settings 760px + gövde kendi içinde kaydırılır (v1.13.1 + v1.14.0)

**Kural (yeni):** genişlik 620 → **760px** (form + iki kolonlu kart en geniş olan; gelecekte MSBuild yolu,
paralellik, worktree havuzu buraya gelecek). Gövde `maxHeight: min(56vh, 460px)`, **min 300px** (katman
listesi boşken dialog çökmesin); scrollbar içeriği kaydırmaz.

- Dosya: `App/Views/SettingsDialog.xaml` — kök `Width`, gövde `ScrollViewer`'ın Max/MinHeight'i.
- `56vh` karşılığı: WPF'te ekran/pencere yüksekliğine bağlı olduğu için **tek yerde** hesaplanır.
- Test: genişlik + gövde sınırları; realize testi (mevcut Settings realize testi genişletilir).

## Bitiş ölçütü

- Her task'ın kırmızı testi **kırmızı gösterildi**, sonra yeşil.
- **Tam süit yeşil** (`--filter "Category!=Acceptance"`), token/motion/D8 guard'ları dahil.
- Doküman: ARCHITECTURE.md §13/§14 (UI + design system) ve README kısayol listesi bu işte güncellenir —
  değişen davranış **yerinde** yeniden yazılır, changelog biriktirilmez.
