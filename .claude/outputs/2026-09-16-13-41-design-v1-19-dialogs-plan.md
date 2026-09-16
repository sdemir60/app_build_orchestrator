# Design v1.19.0 uygulama planı — Settings · About · What's new (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-10-10-23-design-v1.19.0/README.md` → §2.9 (Settings,
satır ~272-319), §2.10 (About, ~321-333), §2.11 (What's new, ~335-350), §9 `## v1.19.0` (~580-595).
Prototip: aynı klasörde `prototype/app/BuildApp.jsx` — `DialogShell` · `RailItem` · `PaneHead` · `ToggleRow` ·
`SettingsDialog` · `AboutDialog` · `NotesDialog` · `WhatsNew` (satır ~1471-2170), `LAYER_PLACEHOLDERS` ·
`SET_SECTIONS` · `GENERAL_GROUPS` · `DEFAULT_GENERAL` · `ABOUT_TABS` · `ABOUT_ID` · `ABOUT_SHORTCUTS`;
first-run daveti `ProjectList` içinde (~921-950). DS: `prototype/_ds/.../_ds_bundle.js` (Segment :302-356),
token'lar `.../tokens/*.css`. 1.18 → 1.19 farkı YALNIZ bu üç dialogdadır (diğer prototip dosyaları CRLF dışında
aynı).

## Kullanıcı kararları (2026-09-16, bu oturum)

1. **Yeni ayarların altı BOŞ, yalnız tasarım.** General'daki `Start with Windows`, `Start minimized to tray`,
   `Close to tray`, `Show notifications` switch'leri yalnız diyalog taslağında yaşar: **kaydedilmez, export/import
   edilmez, konsola not düşmez, hiçbir davranışa bağlanmaz** (UiState.Autostart/AutostartService'e DE bağlanmaz).
   Her açılışta varsayılana döner (`off/off/on/on`). Sonraki oturumlar bunları sırayla bağlayacak.
   **Mevcut çalışan her şey çalışmaya devam eder:** Pull before build (yalnız yeri değişir), Export/Import/Clear,
   Browse, Save/Save and sync, tray, bildirimler, Copy diagnostics, okunmadı noktası.
2. **OSYS ön-dolumu kalkar.** `Load sample layers` ve `LayerDefaults` silinir; kayıtlı katman yokken liste BOŞ
   (boş-durum kutusu); `Add layer` boş satır ekler, ad/desen placeholder'ları ürün-bağımsız standart iskelet.
3. **Third-party sekmesi kalkar** (bilinçli tasarım kararı). `ThirdPartyNotices` + testleri + guard silinir;
   `Assets/GEIST-LICENSE.txt` dağıtımda KALIR.
4. **About bilgileri "olması gerektiği gibi":** gösterilen her değer gerçek kaynaktan gelir, tekrar yok, metinler
   tutarlı (ayrıntı Task 2).
5. **İlk açılış (ayar yokken) daveti** tasarımla birebir hizalanır (Task 5).

## Global Constraints

- Proje kuralları: `CLAUDE.md` (kökte). ÖZELLİKLE: **kırmızı test kuralı** (kod değişikliğinden ÖNCE testin KIRMIZI
  verdiği gösterilir); davranış değişince eski testi YENİ kuralı pinleyecek şekilde yeniden yaz + doc'una eski
  iddia + değişme gerekçesi (`[DEĞİŞEN KURAL — design v1.19.0]`); eşik/bütçe gevşetmek YASAK; **yeni XAML
  kökü/şablonu = realize testi** (`window.Content` üzerinde); **kopya YASAK / tek doğruluk kaynağı** (metin,
  ölçü, primitif iki yerde tanımlanmaz — testlerde de ortak host/fixture tek yerde); hardcoded hex/ms yerine token
  (`Resources/Tokens.xaml`, `Motion.xaml`); kod/UI metinleri İngilizce, kod yorumları Türkçe.
- Build: `dotnet build BuildOrchestrator.slnx`. Test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Açık uygulama Debug bin'ini kilitleyebilir → kilitlenirse `-c Release` ile derle/test et (`--no-build` bayat DLL
  koşturur; gerekirse `--list-tests` ile doğrula).
- Dosya düzenlemede PowerShell `Get-Content|Set-Content` ve `sed -i` KULLANMA (UTF-8/CRLF bozar) — Edit/Write.
- Commit mesajı dosyaya yazılıp `git commit -F` ile atılır; Türkçe, ASCII (`feat(settings): ...`). Sonuna:
  `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. Task başına commit. Branch:
  `design-v1-19-dialogs`.
- ARCHITECTURE.md §13.3 (Popovers and dialogs) üç dialogun anlatısıdır; davranışı değişen kısım AYNI task'ta
  yerinde yeniden yazılır (changelog değil, anlatı; rakam gömme yok — ölçüler tasarım ölçüsüdür, gömülebilir).
  §17.2 guard tablosu, §22 kod haritası, README (Settings/About/What's new kullanım bölümleri, lisans paragrafı)
  ilgili task'ta güncellenir.
- **Tipografi eşlemesi:** tasarımın "15px" başlığı prototipte `var(--text-md)` = **14px** = `FontSize.Md`
  (mevcut kayıtlı sapma, korunur). `text-2xs 11 · text-xs 12 · text-sm 13 · text-md 14 · text-lg 16`.
  600 = `FontWeight.Heading`, 500 = `FontWeight.Emphasis`. `leading-snug` = 1.35 (`LineHeight.Snug13` 13px için;
  12px için 16.2 gerekiyorsa token olarak ekle, çıplak sayı değil). `1.62` satır yüksekliği 13px'te 21.06 —
  tasarım ölçüsü, adlandırılmış tek sabit/token.
- **Renk eşlemesi:** `surface` = `Brush.Surface`, `surface-raised` = `Brush.SurfaceRaised`, `surface-overlay` =
  `Brush.SurfaceOverlay`, `surface-sunken` = `Brush.SurfaceSunken`, `border-subtle`/`border`/`border-strong` =
  `Brush.BorderSubtle`/`Brush.Border`/`Brush.BorderStrong`, `text-primary/secondary/dim/faint` =
  `Brush.TextPrimary/TextSecondary/TextDim/TextFaint`.
- **Süreler:** `--duration-fast` = `Duration.Fast` (120ms), dialog girişi `Duration.Base` (180ms, mevcut
  `PopIn.PlayDialog`). Yeni süre sabiti açılmaz.
- **Git-only metinler:** TFVC uygulamadan kaldırıldı (`9661e7f`, kullanıcı kararı). Tasarımda "Git or TFVC" /
  "git pull --ff-only or tf get" geçen iki metin git-only uyarlanır (aşağıda birebir verildi). Bu bilinçli
  sapmadır; TFVC geri getirilmez.
- Sıra: bloklayıcı yok. **Önemli:** T1-T4. **Kozmetik:** T5. **Kapanış:** T6.

---

### Task 1: Ortak dialog kabuğu + What's new (§2.11)

**Neden önce:** üç dialog aynı kabuğu paylaşacak; en küçük tüketici (What's new) kabuğu doğrular.

Mevcut kod: `Views/NotesDialog.xaml(.cs)`, `Views/AboutDialog.xaml(.cs)`, `Views/SettingsDialog.xaml(.cs)` — üçü de
scrim `Grid` + `Ds.Dialog` Border + odak tuzağı + `OnScrimClick`/`OnDialogClick`/`OnKeyDown(Esc)` kodunu
KOPYA taşıyor. `Controls/PopIn.PlayDialog`. Testler: `tests/.../App/NotesDialogTests.cs`, `NotesDialogHost.cs`,
`NotesDialogWiringTests.cs`, `WhatsNewTests.cs`, `SettingsDialogFocusTests.cs`, `FocusTrap.cs`.

**Kabuk (prototip `DialogShell`):**
- Scrim tam kanama (`Brush.Scrim`), dialog ortada; `surface-raised` zemin, 1px `border-strong`, `radius-lg`,
  overlay gölge (= mevcut `Ds.Dialog`), **içerik köşelerde kırpılır** (`overflow: hidden` — Settings rayının
  `surface` zemini ve footer köşeden taşmamalı: yuvarlak köşeli clip gerekir, düz `ClipToBounds` YETMEZ).
- Dikey dizilim: `head` · `tabs` (opsiyonel) · gövde (flex) · `footer` (opsiyonel; padding `12px 18px`, gap 8,
  üstte 1px `border-subtle`).
- Genişlik ve (opsiyonel) sabit yükseklik; **`maxWidth/maxHeight: calc(100% - 48px)`** → WPF'te host yüksekliğine
  göre kelepçe (saf hesap tek yerde, ör. `DialogSize.Clamp(design, host) = min(design, host − 48)`; pencere
  yeniden boyutlanınca yeniden uygulanır).
- Giriş animasyonu ÜÇÜ için de 180ms fade + 6px (`PopIn.PlayDialog`) — Settings'te daha önce YOKTU → DEĞİŞEN KURAL.
- Davranış ortak: scrim tıklaması kapatır, dialog içi tıklama scrim'e ulaşmaz, Esc kapatır (handled), odak tuzağı
  (`TabNavigation=Cycle`, `IsFocusScope`), açılışta odak dialog içine taşınır, `CloseDialog()` dış API'si.
- Uygulama biçimi implementer'ın (ör. `Controls/DialogShell` templated `ContentControl` + DP'ler `Head`/`Tabs`/
  `Footer`/`DialogWidth`/`DialogHeight` ve/veya ortak `ModalDialog : UserControl` taban sınıfı); **şart:** üç
  dialogun kopya scrim/Esc/odak kodu TEK yere iner (kaynak guard testi: üç `.xaml.cs`'te `OnScrimClick` vb.
  yok). zIndex sırası XAML sırasıyla korunur (Settings < About < What's new).

**What's new yeni düzen (720×600, zIndex en üst):**
- Başlık satırı: padding `20px 18px 16px`, altta `border-subtle`. Solda `What's new` (`FontSize.Md`/600
  `TextPrimary`) + 3px altında `Release notes for Build Orchestrator.` (12px `TextDim`). Sağda **mono sürüm çipi**:
  20px yüksek, padding `0 7px`, 1px `border-strong`, `radius-xs`, **`surface`** zemin, mono 12px `TextSecondary`,
  metin `AppIdentity.Version`. Eski iki satırlı `INSTALLED VERSION` bloğu ve "sağ padding 28 = scrollbar hizası"
  kuralı KALKAR.
- Gövde: kaydırılan alan, padding `22px 18px 24px`, flex (dialog yüksekliği sabit 600; eski sabit 400px gövde
  KALKAR → DEĞİŞEN KURAL, `The_body_height_is_fixed_at_400px` yeni kuralı pinleyecek şekilde yeniden yazılır).
- **Sürüm bloğu = 2 kolon** (`84px` + 26px kolon aralığı + kalan):
  - Sol kolon (**sticky**, gövde kayarken bloğun üstüne yapışır ve bloğun altını aşmaz): mono 13px/500
    `TextPrimary` sürüm (line-height 1) · 6px · mono 11px `TextFaint` tarih · (kurulu sürümde) 6+2px sonra
    **INSTALLED çipi** 16px yüksek, padding `0 5px`, `surface` zemin, 1px `border-strong`, `radius-xs`, caps
    **9.5px**/500 `TextDim`, sola yaslı (içeriğe sıkı). `paddingTop: 1`.
  - Sağ kolon: kategori blokları arası 15px. Blok: 6px kare (radius 1) + 7px + caps 11px/500 `TextDim` başlık,
    başlık altı 7px; maddeler `paddingLeft 13`, **maddeler arası 10px**, 13px `TextSecondary`, **satır yüksekliği
    1.62**, **max genişlik 500px**, sarmalı.
  - Sticky WPF'te yok: saf karar `StickyOffset(scrollTop, blockTop, blockHeight, columnHeight) =
    clamp(scrollTop − blockTop, 0, blockHeight − columnHeight)` + gövdenin `ScrollChanged`'inde sol kolona
    `TranslateTransform`. Karar saf sınıfta test edilir.
- Sürümler arası: 22px + 1px `border-subtle` + 22px.
- `Earlier versions (N)`: aynı 2 kolonlu grid'in SAĞ kolonunda, üstte 22px + 1px `border-subtle` + 14px; buton
  `Ds.Button.Ghost.Sm` + ikon `Icon.Down`, sol margin −10. Son 3 açık kuralı, katlama, `ReleaseNotes` veri kaynağı,
  kategori renk/sırası DEĞİŞMEZ.
- Footer: yalnız sağda secondary `Close`. Okunmadı noktası (`NotesSeen` → `UiState.SeenVersion`) DEĞİŞMEZ.
- `NotesDialog.CapsLabelPx` (10) artık tüketicisiz kalırsa silinir; 9.5 çipin kendi adlandırılmış sabiti olur.

Testler (önce KIRMIZI): kabuk realize (her dialog `window.Content` üzerinde: genişlik/yükseklik, köşe clip'i var,
footer padding/üst çizgi), host 500px iken yükseklik 452'ye kelepçelenir; Settings de giriş animasyonu oynatır
(reduced-motion'da snap); What's new: 720×600, başlık padding + sürüm çipi (20px, surface, mono, AppIdentity.Version),
eski `INSTALLED VERSION` yok, blok grid kolonları 84/26, tarih sol kolonda (sağa yaslı DEĞİL), INSTALLED çipi 16px
ve sol kolonda, madde aralığı 10 / satır yüksekliği 21.06 / MaxWidth 500, sürümler arası 22+1+22, sticky saf
fonksiyon (üst sınır, alt sınır, blok kısa ise 0), gövde kayınca sol kolonun ofseti değişir; kopya kabuk kodu
guard'ı. Mevcut Esc/odak/scrim testleri üç dialog için yeşil kalır.

Doküman: ARCHITECTURE.md §13.3 — "three modals share one shell" paragrafı + What's new paragrafı yerinde yeniden
yazılır; §22 kod haritasına kabuk dosyası.

### Task 2: About (§2.10) — sadeleşme + bilgilerin düzeni

Mevcut kod: `Views/AboutDialog.xaml(.cs)`, `Services/DiagnosticsReport.cs`, `Services/AppIdentity.cs`,
`Shell/ShortcutCatalog.cs`, `Services/ThirdPartyNotices.cs`, `MainWindow.xaml.cs` (`OnAboutRequested`,
`OnNotesRequested`), `Directory.Build.props` (`Copyright`). Testler: `AboutDialogTests.cs`, `AboutDialogHost.cs`,
`AboutWiringTests.cs`, `DiagnosticsReportTests.cs`, `ThirdPartyNoticesTests.cs`, `ShortcutCatalogTests.cs`,
`CopyTextTests.cs`, `AccessibilityTests.cs`, `AntiSlopTests.cs` (grep ile diğerleri).

Kural (Task 1 kabuğu üzerinde, genişlik **620**, yükseklik içeriğe göre, gövde **sabit 284px**):
- **Kimlik bloğu** padding `20px 18px 20px`, gap 13: `AppMark` **28px** (marginTop 1) · `Build Orchestrator`
  (`FontSize.Lg`/600) + 9px + **sürüm çipi** (19px yüksek, padding `0 6px`, 1px `border-strong`, `radius-xs`,
  `surface` zemin, mono 11px `TextSecondary`, `AppIdentity.Version`) · 4px altında tagline (12px `TextDim`,
  `AppIdentity.Tagline`). Eski `{version} · {copyright}` mono satırı KALKAR. Firma kilidi: 1×28 ayraç, gap 13,
  `LICENSED TO` caps + logo 13px %80, iki satır arası 6; logo yoksa blok düşer (değişmez).
- **Sekme bandı:** padding `6px 18px 14px`, altta tam genişlik 1px `border-subtle`. `Ds.Segment` **md** boy:
  dış yükseklik **26** (DS `size='md'` → h 24 + 2). Mevcut `Ds.Segment` 24 (sm) — md için ayrı stil
  (`Ds.Segment.Md`) ya da boy parametresi; action bar'ın sm segmenti DEĞİŞMEZ. Renkler DS varsayılanı (amber yok).
  Sekmeler ve sıra: **About · Environment · Shortcuts**; her açılışta (ⓘ ve F1) **About** seçili.
- Gövde padding `14px 18px 20px`, sabit 284, kendi içinde kayar.
- **About sekmesi:** paragraf 13px `TextSecondary`, satır yüksekliği 1.62, max 470, sarmalı, metin birebir:
  `Build Orchestrator discovers the projects under the repository root, works out the dependency graph and builds in that order — only what changed, in parallel where the graph allows. The plan, the running build and its result stay visible while it works.`
  (tek kaynak: `AppIdentity` yanında adlandırılmış sabit) · hairline `margin 18 0 14` · satırlar (min 27px,
  gap 18, etiket 124px 12px `TextDim`, değer mono 12px `TextSecondary`): **Version** = `AppIdentity.Version`,
  **Engine** = motorun bildirdiği sürüm (`run.EngineVersion`, yoksa `not started`), **Copyright** =
  `AppIdentity.Copyright` · 14px altında ghost sm buton **`What's new in {AppIdentity.Version}`** (ikon
  `Icon.WhatsNew`, sol margin −10) → About kapanır, What's new açılır (`MainWindow`'da `OnNotesRequested` yolu;
  okunmadı noktası söner).
- **Copyright metni:** tasarım `© 2026 Delta Yazılım`; `Directory.Build.props` `<Copyright>` buna güncellenir
  (tek kaynak; title bar/başka tüketici varsa hepsi kendiliğinden değişir).
- **Environment sekmesi:** iki caps grup (11px/500 `TextDim`, başlık altı 7, grup altı 16): **RUNTIME** —
  Engine PID · .NET runtime · OS; **PATHS** — MSBuild · Repository root · State file · Logs · Worktree pool.
  Satır 27px, etiket 124, gap 18. Yollar `PATHS`'ta yatay görünmez scroll + tekerlek devri (v1.13.1 davranışı
  DEĞİŞMEZ). `App version`/`Engine version` satırları bu sekmeden ÇIKAR. MSBuild lazy çözümü (Environment ilk
  seçildiğinde) DEĞİŞMEZ.
- **Shortcuts sekmesi:** iki caps grup — **BUILD** (Build, Rebuild) · **APPLICATION** (Focus filter, About,
  What's new, Escape, Restore from tray). Satır 27px: açıklama 13px `TextSecondary`, sağda `Ds.Kbd`'ler (4px
  arayla). Grup bilgisi `ShortcutCatalog`'da tek yerde (ör. `ShortcutGroup` alanı); açıklama metinleri
  **uygulamanınkiler birebir** korunur; `unavailable` notu korunur.
- **Third-party sekmesi, `ThirdPartyNotices.cs`, `NoticeRow`, `ThirdPartyNoticesTests.cs`, ilgili guard SİLİNİR.**
- **Diagnostics tek model:** `DiagnosticsReport` gruplu satırlar üretir (Identity: Version/Engine/Copyright ·
  Runtime · Paths); About sekmesi, Environment sekmesi ve `Copy diagnostics` AYNI modelden okur (kopya YASAK).
  Pano metni: `Build Orchestrator {version}` + `Engine: {engine}` + Runtime + Paths satırları (hizalı).
- **Footer:** solda ghost sm `Copy diagnostics` (sarmalayıcı sol margin −10 → etiket 18px gutter'a hizalı; 1.4s
  `Copied` + ✓ + başarı tonu DEĞİŞMEZ), sağda secondary `Close`.

Testler (önce KIRMIZI): realize — genişlik 620, gövde 284 sabit (üç sekmede aynı), sekme sırası/etiketleri ve
açılışta About seçili, segment dış yüksekliği 26 + band padding + tam genişlik hairline, kimlik bloğunda sürüm
çipi ve eski mono satırın yokluğu, AppMark 28; About sekmesi paragraf metni/ölçüsü, Version/Engine/Copyright
değerleri gerçek kaynaktan (Engine `not started` durumu dahil), `What's new in …` butonu About'u kapatıp Notes'u
açar ve SeenVersion yazılır; Environment iki grup ve App/Engine version satırlarının yokluğu; Shortcuts iki grup ve
metinlerin katalogdan geldiği; Third-party'nin yokluğu (sekme + tip); pano metninin yeni biçimi; Copyright props
değeri. Eski Shortcuts-ilk-sekme / Third-party / tek-liste environment testleri DEĞİŞEN KURAL doc'uyla yeniden
yazılır.

Doküman: ARCHITECTURE §13.3 About paragrafları, §17.2 (third-party guard satırı kalkar), §22 (notices kalkar);
README About bölümü (~satır 400-415) ve lisans paragrafı (~510-514: GEIST-LICENSE.txt kalır, Third-party
sekmesine atıf kalkar).

### Task 3: Settings — sol raylı iki panel (§2.9)

Mevcut kod: `Views/SettingsDialog.xaml(.cs)`, `ViewModels/SettingsDraftViewModel.cs`, `Shell/LayerDefaults.cs`,
`Controls/SettingsBodyHeight.cs`, `Resources/Controls.xaml` (`Ds.Settings.*`), `AccessibilityNames.cs`,
`MainWindow.xaml` (dişli tooltip'i). Testler: `SettingsDialogTests.cs`, `SettingsDialogHost.cs`,
`SettingsDialogFocusTests.cs`, `SettingsPortabilityTests.cs`, `SettingsBodyHeightTests.cs`,
`LayerDefaultsTests.cs`, `ExternalProjectsWiringTests.cs`, `PullBeforeBuildTests.cs`, `AccessibilityTests.cs`,
`RunViewModelStateTests.cs` (grep: `LoadSampleLayers|SampleLayers|LayerDefaults|SettingsBodyHeight|760`).

Kural (Task 1 kabuğu, **880×576**):
- **Başlık satırı:** padding `12px 12px 12px 18px`, altta `border-subtle`: `Settings` (`FontSize.Md`/600
  `TextPrimary`, esner) + sağda **kapat** `Ds.IconButton` (sm; `Icon.Close` = lucide X `M18 6 6 18` + `m6 6 12 12`,
  stroke 2 — `Icons.xaml`'e mevcut sözlük deseniyle eklenir; `AutomationProperties.Name` `Close settings`,
  tooltip `Close`) → Cancel ile aynı yol.
- **Gövde** = yatay: **ray 196px** + sayfa.
  - Ray: `surface` zemin, sağda 1px `border-subtle`, padding `10px 8px`, satırlar arası 2px. Satır: 30px, padding
    `0 8px`, `radius-sm`, 13px/500; pasif `TextDim` + saydam, hover `surface-raised`, **aktif `surface-overlay` +
    `TextPrimary`**; zemin/renk geçişi `Duration.Fast`. Sağda mono 11px tabular sayaç **yalnız >0 iken**:
    `External projects` = taslak kart sayısı, `Layers` = taslak katman sayısı; aktif satırda `TextSecondary`, pasifte
    `TextFaint`. Sıra: **General · Workspace · External projects · Layers**. Klavye erişilebilir (ör.
    `RadioButton` grubu + `Ds.Settings.RailItem` stili; UIA adı etiketi).
  - Sayfa: dikey kaydırılır, padding `20px 20px 24px`. Her sayfa **PaneHead** ile açılır: başlık `FontSize.Md`/600
    `TextPrimary` + 5px altında tek satır açıklama (12px `TextDim`, snug, max 520, sarmalı), altı 18px.
  - **Açılış bölümü:** first run (workspace yok) → **Workspace**; diğer açılışlar → **General**. Bölüm değişince
    dialog boyu değişmez.
- **General** — bu task'ta yalnız sayfa iskeleti + PaneHead (`How the app starts and behaves. Every setting applies when you save.`);
  satırlar Task 4.
- **Workspace:** PaneHead `The folder that holds the solutions. Projects and the dependency graph are discovered from it on Sync.` ·
  caps `REPOSITORY ROOT` (11px/500 `TextFaint`, altı 7) · satır gap 8: mono input (watermark **`D:\src\myapp`**) +
  secondary `Browse…` (folder ikonu) · 9px altında 11px `TextFaint` `Required — nothing is discovered without it.`
- **External projects:** PaneHead (git-only uyarlama, birebir):
  `Projects outside the repository root — a folder, a solution or a project file; the git working copy is found from the path. They are built before everything else.` ·
  boş-durum kutusu (metin değişmez) · kartlar (36px + 6, grip + mono path + sil) — **`PROJECT PATH` kolon başlığı
  KALKAR** · path watermark **`C:\src\shared\MyApp.Common\MyApp.Common.csproj`** · `+ Add external project`
  (ghost sm, marginTop 10, sol margin −10). Alt satır (pull bağlantısı) Task 4'te.
- **Layers:** PaneHead `Projects are grouped by the first matching pattern (regex on the project name), top to bottom. Non-matching projects fall under Other.`
  (`Other` mono) · boş-durum kutusu (metin değişmez) · kolon başlıkları yalnız katman varken: `LAYER NAME` (170) +
  `PATTERN`, `TextFaint`, padding `0 34px 5px 30px` · kartlar · `+ Add layer` (ghost sm, marginTop 10, sol margin −10).
  - **`Add layer` BOŞ satır ekler** (ad `""`, desen `""`) — eski `Layer N` adı KALKAR.
  - **Placeholder'lar satır indeksine göre** (6'dan sonra baştan), tek kaynak (ör. `Shell/LayerPlaceholders.cs`):
    `Core` / `^MyApp\.(Core|Common)\.` · `Infrastructure` / `^MyApp\.(Data|Infrastructure)\.` · `Domain` /
    `^MyApp\.Domain\.` · `Services` / `^MyApp\.Services\.` · `Api` / `\.Api$` · `Client` /
    `^MyApp\.(Web|Client|Mobile)\.`. Sürükle-bırak sonrası placeholder yeni indekse göre güncellenir.
  - **Kayıtlı katman yoksa taslak BOŞ** — `LayerDefaults.cs`, `LayerDefaultsTests.cs`, `LoadSampleLayers`,
    `SampleLayersButton` SİLİNİR.
- **Footer** (kabuk footer'ı): solda Export / Import / Clear `Ds.IconButton` sm; tooltip'ler birebir
  `Export settings — the whole form as a JSON file` · `Import settings — fill this form from a JSON file` ·
  `Clear settings — empty the form` (armed: `Click again to clear`). `Load sample layers` + ayraç KALKAR.
  Geri bildirim (2.4s, yeşil/kırmızı) DEĞİŞMEZ. **Geri bildirim yokken ve Save kapalıyken tek satır neden** (12px
  `TextFaint`, kırpılır), öncelik sırasıyla: `Repository root is required` · `Every external project needs a path`
  · `Every layer needs a name` · `Check the highlighted pattern` — karar `SettingsDraftViewModel`'de saf özellik
  (ör. `SaveBlockedReason`), `CanSave` ile aynı koşullardan türetilir (kopya YASAK). Sağda `Cancel` + primary
  `Save` / first run `Save and sync`.
- Export/Import/Clear/Save davranışı, JSON biçimi, commit sırası, konsol notları **DEĞİŞMEZ**.
- `SettingsBodyHeight.cs` + testleri SİLİNİR (gövde artık sabit dialog yüksekliğinde flex; host kelepçesi Task 1'in
  tek hesabında).
- Dişli tooltip'i: `Settings — general, workspace, external projects and layers`.

Testler (önce KIRMIZI): realize — 880×576, başlık satırı + kapat butonu Cancel gibi kapatır; ray 196 + sıra +
aktif/pasif renkleri + sayaçların yalnız >0'da görünmesi ve taslakla canlı güncellenmesi; açılış bölümü first
run'da Workspace, sonra General; her sayfada PaneHead metni; Workspace watermark/zorunluluk notu; External kolon
başlığının yokluğu + yeni watermark; Layers: kayıtlı katman yokken boş, `Add layer` boş satır, placeholder indeks
eşlemesi + 7. satırda başa dönüş + sürüklemede güncelleme; footer'da `Load sample layers`'ın yokluğu, yeni
tooltip'ler; `SaveBlockedReason` öncelik sırası (VM, WPF'siz) ve footer'da yalnız geri bildirim yokken görünmesi;
bölüm değişince dialog boyu sabit. Eski `LoadSampleLayers`/`LayerDefaults`/`SettingsBodyHeight`/760 testleri
DEĞİŞEN KURAL olarak yeniden yazılır ya da (tamamen kalkan özellik) gerekçeli silinir.

Doküman: ARCHITECTURE §13.3 Settings paragrafları yerinde (760/üç bölüm/tek kolon/ön-dolum/Load sample/gövde
kelepçesi anlatısı kalkar; ray, sayfa, footer neden satırı gelir). **Not — doküman zaten koddan sapmıştı:**
§13.3 harici kartı "`<select>`'in `ComboBox` portu" ve "Git-sourced card" diye anlatıyor; kod TFVC kaldırıldığından
beri yalnız path input'u. Bu bölüm zaten yeniden yazılacağı için koda göre düzeltilir. §22 kod haritası
(`LayerDefaults` satırı kalkar, placeholder dosyası gelir). README Settings bölümü.

### Task 4: Settings → General sayfası + Pull before build'in taşınması (§2.9)

Mevcut kod: Task 3'ün dosyaları; `SettingsDraftViewModel.PullExternalsBeforeBuild` (gerçek, kalır).

- **ToggleRow** (yeniden kullanılır tek bileşen/şablon): yatay, üstten hizalı, gap 20, padding `13px 0`; satırlar
  arası 1px `border-subtle` (grubun ilk satırında yok); solda ad 13px/500 `TextPrimary` + 3px altında tek satır
  açıklama 12px `TextDim` snug max 430; sağda `Ds.Switch` (paddingTop 1). **Bağımlı satır** üst anahtar kapalıyken
  %45 opaklık + etkileşim yok, layout sabit.
- Gruplar (caps 11px/500 **`TextDim`**, grup başlığı → satırlar 5px, gruplar arası 22px) ve satırlar birebir —
  tek kaynak bir katalog (prototip `GENERAL_GROUPS` karşılığı; yeni ayar = bir satır):
  - **STARTUP:** `Start with Windows` — `Launch when you sign in to Windows.` · `Start minimized to tray` —
    `No window on start — the tray icon brings it back.` (bağımlı: Start with Windows) · `Close to tray` —
    `Closing the window leaves the engine running in the tray.`
  - **BUILD:** `Pull before build` — git-only uyarlama: `Update every external working copy first — a fast-forward-only git pull, one per copy.`
  - **NOTIFICATIONS:** `Show notifications` — `A tray notification when a build finishes — succeeded or failed.`
- **Bağlama:** `Pull before build` → mevcut `PullExternalsBeforeBuild` (Save/persist/export/import/Clear/konsol
  notu DEĞİŞMEZ). Diğer dört anahtar → taslakta yalnız görsel durum (kullanıcı kararı 1): varsayılan
  `false/false/true/true`, **Save'de yazılmaz, `SettingsFile`'a girmez, Clear onları varsayılana döndürür**
  (prototip parity, yalnız taslak), konsol notu yok. VM'de bu dört değerin "henüz bağlı değil" olduğu XML doc'ta
  açıkça yazılır.
- **External projects header'ındaki eski switch + `PULL BEFORE BUILD` caps + tooltip KALKAR.**
- **External projects sayfasının altına** (kartlardan ve Add butonundan sonra): marginTop 20, paddingTop 14, üstte
  1px `border-subtle`, sarmalı satır, gap 6: 12px `TextDim`
  `Card order sets the order the working copies are updated. Updating them before a build is on.` / `… is off.`
  (taslak değerinden canlı) + ghost sm buton `Pull before build` (sol margin −6) → ray **General**'a geçer.
- `AccessibilityNames.PullExternalsBeforeBuild` General'daki switch'e taşınır; yeni switch'lere UIA adı = etiket.

Testler (önce KIRMIZI): realize — General'da üç grup, satır sırası/metinleri, ToggleRow ölçüleri (padding 13,
hairline ilk satırda yok, açıklama max 430), switch'lerin varsayılanları; Start with Windows kapalıyken
`Start minimized to tray` satırı %45 + tıklanamaz, açınca etkin; General'daki Pull switch'i taslağın gerçek
bayrağına bağlı (Save → UiState.UpdateExternals ve RunViewModel'e gider — mevcut `PullBeforeBuildTests`
yeni konumla); dört yeni anahtar Save'de UiState'e ve export JSON'a GİRMEZ, yeniden açılışta varsayılana döner;
External header'da switch'in yokluğu; alt satır metni on/off ile değişir ve buton rayı General'a alır.

Doküman: ARCHITECTURE §13.3 (pull switch'inin yeri ve gerekçesi yerinde yeniden yazılır; "rule row header"
anlatısı kalkar; dört anahtarın henüz davranışa bağlı olmadığı §20 Known limits'e tek cümle); README Settings.

### Task 5: İlk açılış daveti — tasarıma hizalama (§2.4 first run, prototip ~921-950)

Mevcut kod: `ShellRoot.xaml` `PART_ListInvite`, `ShellRoot.xaml.cs` (`SetupChecklist`),
`ViewModels/InteractionText.cs`, `MainWindow.xaml.cs` (`ListInvite.Resolve`). Metinler zaten birebir; sapmalar ölçü
ve renkte:

| Öğe | Prototip | Uygulama (şu an) |
|---|---|---|
| Başlık | `text-md` 14 / 600 `text-primary` | `FontSize.Sm` 13 / `Emphasis` |
| Açıklama | 12 `text-dim`, max 310, snug | `TextFaint`, max 292 (panelden) |
| Dikey ritim | gap 10: başlık→açıklama 10, açıklama→kart 14, kart→butonlar 16, butonlar→not 12 | 8 / 14 / 14 / 8 |
| Kart satır etiketi | 12 `text-secondary` | `TextDim` |
| Kart değeri | mono **11** (`text-2xs`); zorunlu `text-secondary`, opsiyonel `text-faint` | mono 12 |
| Kart | 292 genişlik, `surface`, 1px `border`, `radius-md`, satır 30, padding `0 10px`, satır arası `border-subtle` | aynı (doğrula) |
| Panel | `surface-base` zemin, padding 24, dikey ortalı | doğrula |

Testler (önce KIRMIZI): realize — başlık 14/SemiBold, açıklama TextDim + MaxWidth 310, dört dikey aralık, etiket
TextSecondary, değer 11px; kart ölçüleri. Butonlar/komutlar/`Import settings…` akışı DEĞİŞMEZ (mevcut testler).

Doküman: ARCHITECTURE §13.2 first-run anlatısında ölçü/renk yazıyorsa düzeltilir.

### Task 6: Uygulama sürüm notları + doküman süpürmesi + tam süit

- `Services/ReleaseNotes.cs` — yanlışlaşan maddeler YERİNDE yeniden yazılır (changelog biriktirilmez), yenileri
  eklenir. En az:
  - `Dialogs are sized by how they grow: Settings 760px …` → üç dialogun ortak kabuğu ve sabit ölçüleri
    (Settings 880×576, What's new 720×600, About 620).
  - `Pull before build, in the External projects header: …` → Settings → General'da; External projects sayfası
    nereye gittiğini ve açık/kapalı olduğunu söyler.
  - `First run opens Settings: …` / `Settings can be exported …` doğru kalıyorsa dokunma.
  - Added: Settings'in bölüm rayı (General · Workspace · External projects · Layers); General bölümü (yalnız
    **var olan** davranışı iddia et — yeni dört anahtar henüz bağlı değil: "switches for start-up, tray and
    notifications are laid out; they take effect in a later version" gibi dürüst bir cümle ya da hiç madde yok —
    uydurma davranış YAZILMAZ).
  - Changed: What's new sürüm kimliği sol sabit sütunda; About sadeleşti (About → Environment → Shortcuts, gruplar,
    What's new butonu); Add layer boş satır + standart placeholder'lar; Save neden kapalı satırı.
  - Removed: `Load sample layers` ve OSYS ön-dolumu; About'un Third-party sekmesi.
- ARCHITECTURE.md / README.md son süpürme: `grep -n "760\|660\|620\|Load sample\|Third-party\|ThirdParty\|LayerDefaults\|SettingsBodyHeight\|INSTALLED VERSION\|400 px\|Shortcuts tab\|rule row"` —
  her isabet koda göre doğru mu, yerinde düzelt. `.claude/outputs`/`summaries` tarihsel, dokunulmaz.
- **Tam süit yeşil** (`Category!=Acceptance`), token/motion/D8/anti-slop guard'ları dahil.
- Uygulamayı çalıştırıp üç dialogu ve first-run davetini gözle doğrula (canlı pencere ≠ ekran dışı kare).
