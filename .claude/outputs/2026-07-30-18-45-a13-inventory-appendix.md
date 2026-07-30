# A13 — Envanter eki: kalem-kalem ölçüm sonucu

**Neden bu dosya var:** walkthrough'un 228 alt-kalemi 6 paralel ajanla, **testlerin gövdesi okunarak** ölçüldü.
Plan dosyasında yalnız sayılar ve task grupları var; **kalem düzeyindeki kanıt burada.** T3–T6 brief'lerini ve
artık listesini (residue) yazmak için gereken asıl sermaye budur — kaybolursa envanteri baştan koşturmak gerekir.

> **Not:** T1 (tetikleyici zincirleri) ve T2 (üretim boşlukları) bu envanterin bir kısmını **zaten kapattı**.
> Aşağıda kapananlar işaretli. Kalanlar T3–T6'nın girdisidir.

| Bölüm | Alt-kalem | PINLI | Testsiz | Göz ister |
|---|---|---|---|---|
| §0–§3 | 53 | 32 | 20 | 1 |
| §4–§6 | 26 | 22 | 4 | 0 |
| §7–§12 | 42 | 35 | 5 | 2 |
| BÖLÜM 2 | 78 | 51 | 25 | 2 |
| BÖLÜM 3 | 29 | 22 | 2 | 5 |
| **TOPLAM** | **228** | **162** | **56** | **10** |

---

## A. GÖZ İSTER (10 kalem) — artık listenin çekirdeği

Her biri için "neden pinlenemedi" gerekçesi ölçülmüştür. **A12'nin PrintWindow/UIA ölçüm kanalı bu kalemlerde de
yetmez** — çünkü hiçbirinin assert edilebilir bir eşiği yok; kanal pikseli okur, "yeterince akıcı mı / hoş mu /
onaylıyor musun" sorusuna cevap veremez.

| # | Kalem | Neden göz ister |
|---|---|---|
| 1 | **Tray ikonu quarter-disc mark onayı** (§0.3b) | Bir marka-kimliği ONAYI isteniyor ("onaylıyor musun?"). Onay bir yargıdır; türetim `Assets/generate-tray-icon.ps1:29-30`'da SVG path'iyle zaten pinlenebilir ama onay pinlenemez. |
| 2 | **Fare tooltip'in üzerine gidince kaybolmuyor** (§7.11) | Ayrı HWND/Popup hit-test davranışı (A13.1 madde 7). Yakın pin var: `TooltipTests.cs:41` `ShowDuration==int.MaxValue` — gerekli ama yeterli değil. |
| 3 | **Alt+B pencereyi geri getirir** (§10.6) | `RegisterHotKey`'in OS'a gerçek kaydı + başka uygulamayla çakışma + WM_HOTKEY→`ShowFromTray` (`MainWindow.xaml.cs:667`) gizli pencereyi öne getirmesi. Parse/kayıt-ömrü tarafı zaten pinli (`HotkeyTests.cs:13/44/61/77`). |
| 4 | **Graf düğümü kare mi daire mi — kabul kararı** (§2.3-2⚠) | Ölçüm değil kullanıcı kabul kararı; 26px + 4px radius zaten pinli (`GraphRenderTests.cs:72-85`). |
| 5 | **Gölge yumuşaklığı** (§2.8-1⚠) | A13.1 madde 2: `DropShadowEffect`'te spread parametresi YOK. Konum/opaklık/renk pinli (`DesignTokenScaleTests.cs:144-158`); "yumuşaklık" temsil edilemeyen bir parametre. |
| 6 | **Light'a geçince MSBuild CPU'su gözle düşer** (§3.1-5) | Task Manager gözlemi; assert edilebilir eşik yok. Cap'in job'a YAZILDIĞI pinli (`RunCoordinatorTests.cs:1602`, `JobCpuRateTests`). |
| 7 | **Büyük grafta pan akıcı, takılma yok** (§3.2-1) | Akıcılık hissi; süitte kare-hızı/jank ölçümü yok. `GraphRealizationPerfTests.cs:60` yalnız realize duvar-saatini ölçer, pan maliyetini hiç ölçmez. |
| 8 | **İlk-hover'da gecikme hissedilmemeli** (§3.6-1) | Ölçülebilir eşiği olmayan his. Yapısal yarısı pinli (`ProjectRowTests.cs:125` lazy kurulum, `:156` token/ikon/tooltip çözümü) ama kurulum SÜRESİ için ne bütçe ne ölçüm var. |
| 9 | **Hızlı hover taramasında biriken yavaşlama yok** (§3.6-3) | Zamansal his; çok satırda ardışık hover senaryosu ölçülmüyor. Satır başına tek kurulum pinli (`ProjectRowTests.cs:151`). |
| 10 | **Liste ilk realize rahatsız edici mi (191 satır / 487,5 ms)** (§3.7-1) | Kalemin kendi metni "kabul kapısı DEĞİL, kararın gözle teyidi" diyor. Ölçüm var (`ListRealizationPerfTests.cs:43`) ama **400 ms bütçesi ERTELENMİŞ**: aşılınca süit kırılmıyor, yalnız 3000 ms felaket tavanı assert ediliyor → **487,5 ms sayısı hiçbir assert tarafından korunmuyor.** |

**Ek olarak artık listeye girmesi gerekenler (ölçüm sırasında çıktı):**
- **E6 kalanı:** `SystemParametersMotionSignal`'in canlı OS sinyali (kullanıcı kararı: makine-global ayarı çeviren
  test yazılmayacak) → "reduced-motion'ı aç/kapa, her şey duruyor/dönüyor mu".
- **§12.2 alt-cümlesi:** "banner/toast YOK" için doğrudan test yok (`AntiSlopTests` toast taramıyor) — T4'te
  guard yazılacak, yazılmazsa göz pasına.
- **§3.2-5 yarısı:** etiketi düşen düğümün tooltip'i `string` olarak pinli (`GraphCullTests.cs:155`), ama WPF
  `ToolTip` kontrolü ancak gösterimde kurulduğu için "hover'da GERÇEKTEN açılıyor mu" headless'ta doğrulanamaz.
- **OS yüzeyleri (A13.1 madde 6):** Explorer'ın dosyayı seçili açması · VS'in solution'ı açması · klasör seçici
  penceresi. Komut/argüman ve konsol notları tam pinli (`OsActionsTests.cs:45/74/95/113/201/215`).

---

## B. PİNLENEBİLİR AMA TESTSİZ (56 kalem) — T3–T6'nın girdisi

### B.1 → T3: kopya metinleri (birebir, byte-exact)

| Kalem | Üretimdeki yer | Durum |
|---|---|---|
| Worktree popover **üç açıklama + `source` satırı** | `Views/WorktreePopover.xaml.cs:94-104` | 5 string'in HİÇBİRİ süitte geçmiyor |
| Settings boş-katman kesikli kutu metni | `Views/SettingsDialog.xaml:59-63` | metin süitte hiç yok |
| `LAYERS` açıklaması | `Views/SettingsDialog.xaml:36, :44` | testsiz |
| Branch popover `SWITCH BRANCH` · `No branches match "q".` · alt not | `Views/BranchPopover.xaml:12, :49`, `.xaml.cs:94` | testsiz (arama davranışı `PopoverTests.cs:26-51`'de pinli) |
| Build menü açıklamaları (`Only changed projects` / `All {n} projects — cache ignored`) | `Views/BuildMenu.xaml.cs:82, :84` | Kind+Kbd pinli (`ActionBarTests.cs:163-206`), **kopya testsiz** |
| Konsol `build in progress ▮` | `Console/ConsoleView.xaml.cs:64, :426, :502+` | testsiz |
| Konsol ready satırı (damga + `▮` + dim) | `ConsoleViewTests.cs:300-314` yalnız `"ready"` gövdesini pinliyor | damga/glyph/renk testsiz |
| Panel caps başlıkları: `DEPENDENCY GRAPH` · `PROJECTS` · `EVENT STREAM` · `← Back` | `Graph/GraphView.xaml:19` · `ShellRoot.xaml:48` · `Views/EventStreamView` · `Console/ConsoleHeader` | literaller testsiz |
| Settings buton etiketleri (`Add layer` · `Cancel` · `Save` · `Load sample layers`) | `Views/SettingsDialog.xaml:156-168, :192-196` | davranış pinli, etiket testsiz |
| **Perf notu Balanced varyantı** `parallelism: 4 · cpu cap 70%` | `Core/ProcessControl/PerfNoteText.cs:35` | **yalnız Light ve Full pinli** (`RunViewModelStateTests.cs:246`) |
| Perf notunun `HH:mm:ss` damgası | `RunViewModel.cs:1072` `ComposeNarrativeLine` | bu notta doğrulanmıyor; damga başka satırda pinli (`RunViewModelTests.cs:1476`) |
| `EVENT STREAM` sayacı / `N lines` mono ailesi | `ConsoleModesTests.cs:34` sayıyı pinliyor | mono aile testsiz |

### B.2 → T3: ölçü / geometri / tipografi

| Kalem | Üretimdeki yer | Not |
|---|---|---|
| Branch popover **272px** · worktree **300px** | `Views/ActionBar.xaml:29, :42` | yalnız `ActualWidth>0` pinli (`PopoverTests.cs:205`) |
| Settings dialog **620px** | `Views/SettingsDialog.xaml:17` | testsiz (620 sayısı `DesignTokenScaleTests.cs:141`'de ama o `WindowMinHeight`) |
| Katman kartı **36 + 6 + 170px** | `Views/SettingsDialog.xaml` | 42px aritmetiği dolaylı pinli (`DragReorderTests.cs:31` eşik 21) |
| Düğüm etiketi **10px** | `GraphRenderTests.cs:96-113` pinli | ✔ (bu satır PINLI) |
| Süre kolonu **46px + mono** | `Views/ProjectRow.xaml:105, :107` | testsiz |
| Statü glyph'i **14px** | `Views/ProjectRow.xaml:80` | testsiz |
| Dep üçgeni **12px** | `Views/ProjectRow.xaml:90` | slot 14px pinli, üçgen boyutu testsiz |
| Sha **10.5px** | `Views/ProjectRow.xaml:75` | 118px sığma pinli (`ProjectRowTests.cs:454`), punto testsiz |
| Stream satırı **24px min** + glyph kolonu **12px** | `Views/EventStreamView.xaml.cs:323-324` | adlandırılmış sabit, testsiz |
| Konsol padding **12,8** | `Console/ConsoleView.xaml:17` | testsiz |
| Logo **15px** | `MainWindow.xaml:144` | testsiz |
| `DsSplitter` **7px/1px** design-v1'e karşı | `Controls/DsSplitter.cs:26, :28` | mevcut assert **TOTOLOJİK** (`ShellLayoutTests.cs:69` sabiti kendisiyle kıyaslıyor) |
| `Brush.TextPrimary` **#ededee** | `Resources/Tokens.xaml:42` | TokenBrushesTests TextFaint/TextSecondary'yi pinliyor, **TextPrimary'yi PİNLEMİYOR** |
| Pencere + title bar zemininin **doğru token'a bağlı** olması | `MainWindow.xaml:11, :69` | yalnız `Assert.IsType<SolidColorBrush>` (`MainWindowRealizeTests.cs:83-84`) — başka fırçaya bağlansa yeşil kalır |
| Graf panel başlığının **28px** bağı | `Graph/GraphView.xaml` | graf başlığı `PanelHeader` kontrolünü kullanmıyor → bağ testsiz |
| Action bar zemini + 1px üst border | `ShellRoot.xaml:137` | yükseklik pinli (`DesignTokenScaleTests.cs:49`), zemin/border testsiz |
| Action bar **çocuk sırası + ayraçlar** | `Views/ActionBar.xaml:25-78` | hiçbir test sırayı assert etmiyor |
| Sayaç chip **kümesi/glyph'leri** + `▲`>0'da kırmızı | `Views/ActionBar.xaml.cs:243` | chip DAVRANIŞI pinli, küme/glyph/kural testsiz |
| Koşarken **Stop takası** | `Views/ActionBar.xaml:63` | yalnız UIA adı pinli (`AccessibilityTests.cs:52`), görünürlük takası testsiz |
| **16px tray ikonu** kare testi | `Shell/AppTrayIcon.cs:21` | `app-icon.ico` için var (`IconGeometryTests.cs:111`), tray için YOK |
| Şerit zemini + alt çizgi | `Views/StickyRibbon.xaml:9-10` | 32px/2px pinli, zemin/çizgi testsiz |
| `RevealRisePx = 5` | `Controls/RevealStagger.cs:31` | `GraphRenderTests.cs:273` yalnız `Assert.NotNull(RenderTransform)` |
| Dep rozetinin **konumu** (sağ üst) | `Graph/GraphView.xaml.cs:709-712` | boyut/renk/ebeveyn pinli, `Margin` HİÇ okunmuyor → sol alta kaysa süit yeşil |

### B.3 → T4: motion sabitleri
`shake 360ms / ±3px / X ekseni / BİR KEZ` (`Views/ProjectRow.xaml.cs:34, :265-267, :457-468`) — mevcut testler
yalnız `ShakeTranslate`'in **Y** eksenini okuyor (`ProjectRowTests.cs:300, :314`) ·
imleç **7×13px + 1.1s blink + 420ms sönme** (`Controls/MotionTokens.cs:22`, `ConsoleView.xaml.cs:31`) ·
pop-in **140ms / 4px / .985** (`Controls/PopIn.cs:19-21`) · glow **1100ms** (`Views/EventStreamView.xaml.cs:322`) ·
nefes **tepe 0.32** · popover **8px** boşluk (`Views/ActionBar.xaml:27, :40` `VerticalOffset="-8"`).

### B.4 → T4: negatif guard'lar
Uygulama-içi **toast/banner yok** (`AntiSlopTests` bugün toast taramıyor) · settings'te **eşleşme sayacı yok** ·
**"View failures" butonu yok** · **perf chip'inde tooltip yok** (`Views/ActionBar.xaml:56`) ·
mono **dekoratif kullanılmıyor** · rakamlar **tabular** (üretimde set ediliyor: `ProjectRow.xaml:74/107`,
`EventStreamView.xaml:41`, `StickyRibbon.xaml:38`, `ActionBar.xaml.cs:243` — assert eden test yok).

### B.5 → T5: a11y `AutomationProperties.Name` eksikleri (ölçüldü)
**Adı OLMAYANLAR:** graf düğümlerinin **tamamı** (`rg AutomationProperties src/BuildOrchestrator.App/Graph/` → **0**) ·
Copy log butonu (`Console/ConsoleHeader.xaml:24`) · `LatestPill` · Settings katman ad/regex input'ları ·
WorktreePopover hedef-satırı çöp butonu.
**Adı OLANLAR (bozma):** ActionBar 11 kontrol · `ProjectRow` · 3 splitter · filtre kutusu (`ShellRoot.xaml:64`) ·
title-bar min/max/close + 3 layout ikonu + dişli · `ProjectRowActions` klasör/VS ikonları · Settings "Delete layer" ·
BranchPopover arama · WorktreePopover switch · "Build options" (`Controls.xaml`).
Kapsam testi yaz: etkileşimli yüzeyleri tarayıp adsız olanı RED etsin.

### B.6 → T6: kalanlar
- **`--autostart` → tepsi + Sync YOK:** `App.xaml.cs:27` `AutostartArg`, `MainWindow.xaml.cs:662-663`
  `StartInTray()` (EnsureHandle ile tray kurulur, `Show()` ÇAĞRILMAZ, oto-Sync tetiklenmez).
  **Süitte `AutostartArg`/`StartInTray` HİÇ geçmiyor.** Registry uzlaşması pinli (`AutostartServiceTests`).
- **Argüman dalları:** `--font-ab` (`App.xaml.cs:55-58`, DI ve single-instance kapısından ÖNCE) ve tanınmayan
  argüman (`--it4a-lab` → yok sayılır, `:62`). `OnStartup`/`e.Args` ayrıştırmasına değen tek test yok.
- **Kontrast 4.28 tam değeri:** `ContrastTests.cs:83` yalnız `< 4.5` pinliyor — token 4.40'a kaysa YEŞİL kalır.
  RATIFY kaydı yalnız XML yorumunda (`:20-24`).
- **Tek grafta karışık-katman LOD:** üretimde karar katman başına (`Graph/GraphView.xaml.cs:334-336`,
  `Graph/GraphLayout.cs:7-14`), ama tüm LOD testleri **homojen** graf kullanıyor (hepsi kısa ya da hepsi uzun ad).
- **Şeridin canlı `· N warnings` beslemesi:** `RibbonText.Compose` segmenti üretiyor ve `RibbonTextTests.cs:143`
  `"· 3 warnings"`'i birebir pinliyor — eksik olan **VM'in canlı warning sayısını Compose'a taşıdığı**.
- **`verify-publish.ps1`'e publish artefaktıyla Sync+Build check'i:** script en ileri gittiği yer boot satırı
  (`:165`); repo seçmiyor, `startRun` göndermiyor. Beklenen: publish edilmiş ikiliyle en az bir `runCompleted`.

---

## C. "TETİKLEYİCİ SINANMIYOR" bayrağı (T1 KAPATTI — kayıt için)

12 kalem: graf fare tıklaması/zemin · stream satırı tıklaması · `EventStreamView`'ın kendi latest-pill kablajı ·
gerçek `ScrollChanged` → sticky overlay · F5/Ctrl+F5/Esc/Ctrl+F gerçek tuştan · layout ikonu tıklaması ·
splitter sürükleme amber'ı · `ConsoleView` daktilosu motion açıkken · `ConsoleTypingGate` üretim yolunda ·
konsol imleci pozitif kanıt · liste satırında Enter/Space · kart hover gerçek `MouseEnter/Leave`.
**Hepsi T1'de kapatıldı** (`ae2c832..214e290`, 15/15 mutasyon kırmızı).

---

## D. BAYAT / GEÇERSİZ ÇIKAN WALKTHROUGH KALEMLERİ

- **§8.7 "RootPathBox DS Input görünümünde"** — `RootPathBox` diye eleman YOK; REPOSITORY satırı salt-okunur
  mono `TextBlock` + `Change…` (`Ds.Button.Secondary.Sm`) olarak yeniden tasarlanmış
  (`Views/SettingsDialog.xaml:172-186`). Kalem bayat.
- **§7.1 alt-cümlesi "PROJECTS başlığında kaldırılabilir filtre chip'i"** ve **§2.4-8 "No projects match this
  filter."** — üretimde YOKTU; **T2'de kapatıldı**.
- **§2.1-2 title bar bağlam metni** — üretimde hiç yazılmıyordu; **T2'de kapatıldı**.
- **§7.1 / §10.3 "liste filtrelenir"** — liste HİÇ filtrelenmiyordu (`VisibleProjects` sıfır tüketicili);
  **T2'de kapatıldı**.
- **§2.9-1** `Ds.Dialog` `surface-RAISED` kullanıyor, README §2.9 `surface-overlay` diyor — bu farkın kayıtlı bir
  sapma olup olmadığı **doğrulanmadı**; final review'da ya A13.1'e yazılmalı ya düzeltilmeli.
