# A13 — Gözle-kontrol borcunun otomatikleştirilmesi + park listesi triyajı (TDD dökümü)

**Branch:** `a13-visual-debt-automation` · **BASE** `8e6ebbe` (= `main` = `origin/main`)
**Baseline (ölçüldü, bu makinede):** build 0/0 · suite **1433 passed / 2 skipped / 0 failed** (1435), exit 0.
**Otorite zinciri:** v7 (PLAN OF RECORD) → design-v1 → ledger. Çelişkide v7 kazanır.

> **Belgenin kuralı:** her satır bir kanıta bağlıdır (`dosya:satır`, test adı, ölçüm). Kanıtı olmayan iddia yazılmadı.

---

## 0. Envanter — ölçülen zemin

Walkthrough'un **BÖLÜM 1 + 2 + 3'ünün tamamı** kalem kalem tarandı; her kalem assert edilebilir alt-iddialara
bölündü ve mevcut süitte **testin GÖVDESİ okunarak** (isimden çıkarım yapılmadan) pin durumu ölçüldü.

| Bölüm | Alt-kalem | PINLI | Pinlenebilir-ama-testsiz | GÖZ İSTER |
|---|---|---|---|---|
| §0–§3 (açılış · kabuk · şerit · graf) | 53 | 32 | 20 | 1 |
| §4–§6 (liste · konsol/D4 · stream) | 26 | 22 | 4 | 0 |
| §7–§12 (action bar · settings · OS · klavye · motion · durumlar) | 42 | 35 | 5 | 2 |
| BÖLÜM 2 (prototiple yan yana, §2.1–§2.9 + Genel) | 78 | 51 | 25 | 2 |
| BÖLÜM 3 (It-5 görsel kalemleri) | 29 | 22 | 2 | 5 |
| **TOPLAM** | **228** | **162** | **56** | **10** |

**Ayrıca iki sınıf bulgu:**

1. **~12 PINLI kalemde "tetikleyici sınanmıyor"** — test davranışı doğruluyor ama **üretimdeki yoldan
   tetiklemiyor**. Bu, A12'nin kusurunun saklandığı sınıfın ta kendisidir (7 test animasyonu doğrudan
   çağırıyordu; üretimde hiç oynamıyordu). En riskli olanlar T1'de kapatılıyor.
2. **ÜÇ ÜRETİM BOŞLUĞU** — design-v1'in şart koştuğu, üretimde **hiç olmayan** kalemler. Test eksiği değil,
   sapma. T2'de kapatılıyor (kullanıcı kararı 2026-07-30).

---

## 1. Sınıflandırma kuralı (uygulanan)

- **PİNLENEBİLİR → TEST.** XAML/kod değerinin design-v1 ile karşılaştırılması · kopya metni birebirliği ·
  realize testi · ölçü/geometri · binding/CanExecute canlılığı · durum→şablon eşlemesi · kaynak-deseni guard'ı.
- **GÖZ İSTER → ARTIK LİSTE.** Yalnız: akıcılık/hız hissi, animasyon estetiği, renk algısı, prototiple genel
  izlenim, OS davranışı (tepsi balonu görünümü, Explorer/VS/klasör seçici pencereleri, ekran okuyucu sesi,
  global hotkey'in OS kaydı, gerçek DPI değişimi).
- **"Harness göremez" GEREKÇE DEĞİL.** A12'de ölçüldü: `PrintWindow(PW_RENDERFULLCONTENT)` + UIA + kare-arası
  piksel farkı canlı uygulamada animasyonu ölçebiliyor. Aşağıdaki 10 göz-ister kalemin her birinde **bu kanalın
  neden yetmediği** ayrıca yazıldı (artık liste belgesinde).

---

## 2. PART A — testler (task listesi)

Sıra bağımlılığa göre. Her task: **önce kırmızı test, ayırt ediciliği kanıtlanmış** (değeri bilerek boz →
KIRMIZI → geri al), sonra (gerekiyorsa) fix.

### T1 — Tetikleyici zincirleri (A12 sınıfı) · **en yüksek değer**
Üretimdeki yoldan sürülmeyen 12 kalem. Her test **üretim tetiğini** kullanır; doğrudan metot çağrısı yasak.

| # | Ne pinlenecek | Bugünkü boşluk |
|---|---|---|
| 1.1 | Graf düğümüne **fare tıklaması** seçer/toggle eder; **zemine** tıklama kaldırır; düğüm tıklaması zemine sızmaz | Tüm graf testleri `view.SelectedNode = …` programatik; `MouseLeftButtonDown` hiç yükseltilmiyor (`GraphView.xaml.cs:671-674`, `:172`) |
| 1.2 | Stream satırı **tıklaması** → `SelectProject` | `EventStreamView.xaml.cs:489 OnClicked` hiçbir testte raise edilmiyor |
| 1.3 | **EventStreamView'ın KENDİ** latest-pill kablajı | `LatestPillTests` kablajın **test-lokal kopyasını** kuruyor; `EventStreamView.xaml.cs:303` hiç sürülmüyor |
| 1.4 | Gerçek **ScrollChanged** → sticky overlay | `StickyOverlayTests` `UpdateOverlay(288)`'i doğrudan çağırıyor; `StickyLayerList.xaml.cs:56` tetiği sürülmüyor |
| 1.5 | **F5 / Ctrl+F5 / Esc / Ctrl+F** pencerede GERÇEK tuştan komutu ateşler | `WindowBindings`/`CommandFor` tabloları pinli ama `MainWindow.xaml.cs:234-235`'in tabloyu `InputBinding`'e çevirdiği testsiz |
| 1.6 | Title bar **layout ikonu tıklaması** → `SetMode` | `MainWindowRealizeTests.cs:114-117` yalnız `Assert.NotNull(Style)` (hep-yeşil) |
| 1.7 | Splitter **sürüklerken çizgi amber**, bırakınca geri | `DsSplitter.cs:66-67` testsiz |
| 1.8 | `ConsoleView` daktilosu **animasyon AÇIKken** üretim append yolunda GERÇEKTEN kademeli yazar | Yalnız saf scheduler + reduced-motion instant kolu pinli |
| 1.9 | `ConsoleTypingGate` üretim append yolunda GERÇEKTEN tüketiliyor | Gate doğrudan çağrılıyor; `ConsoleView`'ın onu kullandığı hiç doğrulanmıyor |
| 1.10 | Konsol imleci (ready + aktif satır) **motion AÇIKken** gerçekten yanıp söner | Yalnız NEGATİF kanıt var (reduced-motion'da saat yok) |
| 1.11 | Liste satırında **Enter/Space** seçim yapar | `Key.Enter`/`Key.Space` süitte hiç geçmiyor (`ProjectRow.xaml.cs:510` testsiz) |
| 1.12 | Kart hover ikonları **gerçek MouseEnter/MouseLeave**'den kurulur | `SimulateHover()` seam'i sürülüyor, gerçek olay değil |

> **Not:** `MainWindow` DI'sız kurulamıyor — 1.5/1.6 için `MainWindowRealizeTests`'in mevcut gerçek-DI
> kurulum deseni yeniden kullanılacak. Seçim zincirinin (kart → konsol + liste + graf **aynı anda**) MainWindow
> yarısı da burada kapanır.

### T2 — Üretim boşlukları (design-v1 sapması) · **fix, kırmızı-önce**
> Kullanıcı kararı 2026-07-30: **üçü de bu adımda kapanır.**

- **2.1 Title bar bağlamı** — `MainWindow.xaml:161` `ContextText` sabit `"no repository"` literali; **hiçbir C#
  dosyası ona yazmıyor** (`rg ContextText` → tek satır). design-v1 §2.1: `OSYS · main`, worktree aktifse
  `· main-2` (daha soluk), repo yokken `no repository`. → Yazıcı kablo + üç durumun testi.
- **2.2 Branch chip boş** — `RunViewModel.Branch` `""` başlıyor ve ona yazan yalnız iki yol var (kullanıcı
  popover'dan seçerse / diskteki UiState seed'i). **App `ListBranchesCommand`'ı HİÇ göndermiyor**
  (`rg ListBranchesCommand src/BuildOrchestrator.App` → 0). `syncCompleted` `Branch`'i yazmıyor; olay zaten
  App'in gönderdiğinin echo'su (`SyncWorkspaceService.cs:140`). → App komutu gönderir, `BranchListEvent`
  geldiğinde `Branch` aktif branch'e seed edilir.
  **RİSK (kayda geçiyor):** bu, bugün her zaman `false` dönen `IsWorktreeForced`'ı ilk kez canlandırır;
  worktree "forced" dalları gerçekten koşmaya başlar → `ActionBarTests` + `WorktreePopover` testleri
  regresyon için ayrıca sürülecek.
  **E2 bu iki maddeyle kapanır — ve kök neden `git fetch` DEĞİLDİR** (`SyncWorkspaceService.cs:77-81`:
  fetch degrade olsa da akış durmuyor, `syncCompleted` yine yayınlanıyor).
- **2.3 `PROJECTS` başlığında kaldırılabilir filtre chip'i** (`Failed ✕`) — design-v1 §2.4 şart koşuyor;
  `ShellRoot.xaml:48-76` yalnız `build-order` etiketi + filtre kutusu taşıyor.
- **2.4 `"No projects match this filter."`** — string tüm ağaçta yok (`InteractionText.cs:9-32` taşımıyor).

### T3 — Kopya metinleri (birebir)
Design-v1'in kesin string'lerinden testte karşılığı olmayanlar. Hepsi `Assert.Equal` ile **byte-exact**
(mevcut `InteractionStateTests` deseni):
worktree popover'ın **üç açıklaması + `source` satırı** (`WorktreePopover.xaml.cs:94-104`) · settings boş-katman
kesikli kutu metni · `LAYERS` açıklaması · branch popover `SWITCH BRANCH` + `No branches match "q".` + alt not ·
build menü açıklamaları (`Only changed projects` / `All {n} projects — cache ignored`) · konsol
`build in progress ▮` · konsol ready satırı (damga + `▮` + dim) · panel caps başlıkları
(`DEPENDENCY GRAPH` / `PROJECTS` / `EVENT STREAM` / `← Back`) · settings buton etiketleri ·
**perf notunun Balanced varyantı** (`parallelism: 4 · cpu cap 70%` — bugün yalnız Light ve Full pinli) ve
**notun `HH:mm:ss` damgası**.

### T4 — Ölçü / geometri / tipografi
272px branch popover · 300px worktree popover · 620px dialog · katman kartı 36+6+170px · düğüm etiketi 10px ·
süre kolonu 46px + mono · statü glyph'i 14px · dep üçgeni 12px · sha 10.5px · stream satırı 24px + glyph kolonu ·
konsol padding `12,8` · logo 15px · `DsSplitter` 7px/1px'in **design-v1'e karşı** pinlenmesi (bugünkü assert
totolojik: `Assert.Equal(DsSplitter.GrabBand, col.Width)`) · `Brush.TextPrimary` `#ededee` · pencere ve title bar
zeminlerinin **doğru token'a bağlı** olması (bugün yalnız `Assert.IsType<SolidColorBrush>`) · graf panel
başlığının 28px bağı · action bar zemini + 1px üst border · action bar **çocuk sırası + ayraçlar** ·
sayaç chip **kümesi/glyph'leri** + `▲`'nin >0'da kırmızıya dönmesi · koşarken **Stop takası** ·
16px tray ikonu kare testi (bugün yalnız `app-icon.ico` için var).

### T5 — Motion sabitleri
shake **360ms / ±3px / X ekseni / BİR KEZ** (bugün yalnız `ShakeTranslate`'in **Y** ekseni okunuyor) ·
`RevealRisePx = 5` · imleç **7×13px + 1.1s blink + 420ms sönme** · pop-in **140ms / 4px / .985** ·
glow **1100ms** · nefes **tepe 0.32** · popover **8px** boşluk.

### T6 — Negatif guard'lar
Uygulama-içi **toast/banner yok** (kaynak-deseni guard'ı — `AntiSlopTests` bugün toast taramıyor) ·
settings'te **eşleşme sayacı yok** · **"View failures" butonu yok** · **perf chip'inde tooltip yok** ·
mono **dekoratif kullanılmıyor**.

### T7 — a11y `AutomationProperties.Name` (park #7'nin en ucuz ve tek kullanıcı-etkili ayağı)
Bugün adı OLMAYANLAR (ölçüldü): **graf düğümlerinin tamamı** (`rg AutomationProperties src/.../Graph/` → 0) ·
Copy log butonu · `LatestPill` · settings katman ad/regex input'ları · worktree hedef-satırı çöp butonu.
→ Ad verilir + `AccessibilityTests`'e **kapsam testi** (etkileşimli yüzeyleri tarayıp adsız olanı RED eder).

### T8 — Kalanlar
`--autostart` → tepsi + **Sync YOK** (`AutostartArg`/`StartInTray` süitte hiç geçmiyor) ·
`--font-ab` ve tanınmayan argüman (`--it4a-lab`) dalları · kontrast **4.28 tam değeri** (bugün yalnız `< 4.5`;
token 4.40'a kaysa test yeşil kalır) · **tek grafta karışık-katman LOD** (bugün tüm LOD testleri homojen graf) ·
şeridin canlı `· N warnings` besleme boşluğu · `verify-publish.ps1`'e **publish artefaktıyla Sync+Build** check'i
(bugün en ileri gittiği yer boot satırı).

---

## 3. PART B — park listesi triyajı (3 kova)

### Kova 1 — GERÇEK KUSUR → bu adımda kapanır

| Kalem | Ne | Nasıl |
|---|---|---|
| **E1** | Flaky üçlüsü — **ikisinin kökü TEK satır**: `EngineHost.cs:92` sabit `WaitAsync(5s)` | `EngineHostTests` + `RunViewModelTests`×3: timeout enjekte edilebilir yapılır (üretim 5sn'de kalır, test sınırsız bekler). `MsBuildInvokerTests:144`: duvar-saati assert'i (`:164 sw.Elapsed < 15s`) **silinir**, yerine **IOCP sıralama** iddiası gelir ("invoke, torun HÂLÂ YAŞARKEN döndü") — aynı dosyada `:200-201` zaten bu deseni kullanıyor |
| **E2** | Title bar + branch chip | T2.1 + T2.2 |
| **E3** | Kullanıcıya ulaşan **~40 Türkçe string** | İngilizceye çevrilir (kullanıcı kararı: UI + git/worktree + planlama + run/decision log + exception mesajları). Kapsam dışı yalnız `Debug.WriteLine` ve GUI'de yutulan stderr. IPC sözleşmesi kırılmaz (tüketiciler `Code`'a bakıyor); metne assert eden Git/Worktree/Sync testleri birlikte güncellenir |
| **E5** | `CollectRows()`'un **iki sessiz atlaması** (`StickyLayerList.xaml.cs:266`, `:267`) | Önce "kısmen realize listede reveal eksik oynar" testi (KIRMIZI), sonra eksik-satır telafisi. `:267`'de `continue` bile yok — satır sessizce düşüyor |
| **#14** | `debugSpawnChildren` üretimde dinleniyor | **Komut kaldırılmaz, kapıya alınır:** Supervisor'a `--debug-hooks` bayrağı; App bunu asla geçmez, testler geçer. **Neden kaldırılmıyor:** `CascadeKillTests.cs:28`/`:64` §6.1 kaskat garantisinin **TEK otomatik kanıtı**; komut silinirse o pin kaybolur. `docs/TRUST-BOUNDARY.md` §3 (`:158`, `:168-170`, `:283`, `:378`) güncellenir — mevcut satır koordinatları da bayat (`:73`/`:207-223` → bugün `:80`/`:214-229`) |
| **#11a** | Restart'ta **sürüm satırı yok** (`RunViewModel.cs:548-563` dönen `EngineReadyEvent.EngineVersion`'ı hiç okumuyor) | 8 minor içinde tek kullanıcı-görünür olan; tek satır + test |
| **#4a** | `ProjectRowTests.cs:175-176` zayıf `Assert.NotNull(...Style)` | `Assert.Same(FindResource("Ds.IconButton"), …)` |
| **#7-a11y** | Graf düğümlerinde sıfır UIA adı | T7 |
| **#1c** | `PerfProfileParityTests`'te `Category` trait yok | Tek satır |

### Kova 2 — KABUL EDİLEN BORÇ (gerekçesiyle kalır)

| Kalem | Gerekçe |
|---|---|
| **#3** L2 virtualization | Kullanıcı kararı (2026-07-25). Açmak `LayoutMetrics` + `CollectRows` + `FollowScrollController` + `ScrollArbiter`'ı aynı anda geçersizleştirir |
| **#5** DrawingVisual göçü | STOP GATE, ölçüme dayalı |
| **#9** Guard/primitif kopyalarının katlanması | Kontrolcü kararı. Ölçüldü: gecikmeli-yükseliş **3 kopya**, `AnimationsEnabledProvider()` çağrısı **20 yerde** — `MotionGate` yalnız provider'ı tekleştirdi, guard gövdesini değil. Motion koduna dokunmanın riski A12 ile kanıtlandı |
| **#16** `*.targets` ikinci-seviye ağı | Kapatmak repoya yapay `.targets` eklemeyi gerektirir; zararsızlığı kodla teyitli |
| **#13** `Show()` başlatma yolu | Gerçek `Application` + tepsi + hotkey kaydı ister; yeni flake sınıfı yaratır |
| **#15** "BİR KEZ" kaynak-deseni guard'ı | Testin kendi XML doc'u sınırı itiraf ediyor; ispat değil denetim |
| **E4** "no changes"ta `SetGroups` çağrılmaz | **KARAR KAYDI ZATEN VAR** — gerekçe kodda **üç yerde** yazılı (`RunViewModel.Workspace.cs:230-232`, `:62-68`, `MainWindow.xaml.cs:124-125`) ve testle pinli (`RunViewModelTests.cs:1249`). Koşulsuz çağırmak E2/§5-b'nin kapattığı bug'ı (mid-run Sync'te re-reveal + kamera re-home) geri açar. Satır verisi bayat KALMIYOR (`Workspace.cs:190-215` yerinde uzlaştırıyor) |
| **E6** OS motion sinyali | Kullanıcı kararı: **yalnız risksiz kısım** test edilir (`Changed` filtresi + getter); canlı OS ayarını çeviren test **yazılmaz**, artık listeye geçer |
| **#6, #10, #12, #1a/#1b, #8c/#8d, #11 (kalan 7)** | Kanıtlanabilir no-op / doc / ölçüm notu; kapatmaları mevcut hiçbir testi kırmızıya çekmez |

### Kova 3 — ARTIK GEÇERSİZ (silinir, nedeni yazılır)

| Kalem | Neden geçersiz |
|---|---|
| **#2** P3 `EffectivePriorityLocked` drain'i kontrol etmiyor | **Kod düzeltilmiş.** `RunCoordinator.cs:439-442` bugün `(!CapWritableLocked \|\| _copyFloorDepth > 0)` diyor; `CapWritableLocked` (`:354`) `_capDrained`'i içeriyor → drain priority yolunu da bağlıyor. XML doc `:426-432` bunu "final review I-1" olarak anlatıyor |
| **#17** CLAUDE.md 3 bayat ifade | **A11'de kapatıldı** (`4bb6158`) |
| **#8b** Asimetrik "yalın-ok" | `ProjectRow.xaml.cs:331` bugün **tam tersini** yapıyor; `:327-328` yorumu birebir "yalın-ok pürüzü üretilmez" |
| **#4b** "dar tavan (40)" | Tavan bugün **41** (`ListRealizationPerfTests.cs:40`, T49 fix round 2) |
| **§8.7** walkthrough kalemi "RootPathBox DS Input" | **Kalem bayat:** `RootPathBox` diye eleman yok; REPOSITORY satırı salt-okunur mono `TextBlock` + `Change…` (`SettingsDialog.xaml:172-186`) olarak yeniden tasarlanmış |

---

## 4. Yöntem (bağlayıcı)

- **PER-TASK:** taze implementer → `review-package BASE HEAD` → **3-lens paralel review**
  (spec/design-fidelity · WPF/threading+A13.2 · testler/yapı) → tek fix wave → scoped re-review → ledger satırı.
  Aynı worktree'de **iki implementer paralel koşmaz**; read-only reviewer'lar paralel serbest.
- **Ayırt edicilik zorunlu:** her yeni test için değer bilerek bozulur, **KIRMIZI gösterilir**, geri alınır.
  Her zaman yeşil kalan test kalem kapatmaz.
- **TETİKLEYİCİ DERSİ (A12):** "X doğru oynar" testi yazarken sor — X üretimde gerçekten çağrılıyor mu, onu kim
  tetikliyor, o tetikleyici testli mi? Kurulum sırası üretimle aynı olmalı (kabuk realize → sonra veri akar).
- **REALIZE TESTİ:** yeni XAML kökü/template için ZORUNLU; realize `window.Content` üzerinde yapılır.
- **Token guard'ları** her task sonunda koşulur (renk/motion/D8/token: 69 test).
- **Süit maliyeti** ölçülür ve raporlanır (baseline yük altında 5 dk 59 sn; kapanışta sessiz makinede ölçülecek).

---

## 5. Çıktılar

1. `.claude/outputs/2026-07-30-…-visual-check-residue.md` — **yalnız göz isteyen kalemler**, gezme sırasına göre
   (pencere → sol panel → graf → konsol → action bar → popover → ayarlar → tepsi). Her kalem tek satır.
2. `.claude/outputs/2026-07-30-…-parked-items-triage.md` — üç kovalı tablo + kapatılanların commit'i.
3. Süite eklenen testler + "X yeni test, walkthrough'un Y/228 alt-kalemini pinliyor" raporu.

**Doküman senkronu:** `docs/TRUST-BOUNDARY.md` §3 (debugSpawnChildren kapıya alınınca) · `it5-records` §2'nin
geçersiz çıkan 4 satırı · davranış değişirse `CLAUDE.md`/`README.md`. Değişmeyen belgeye dokunulmaz; sayı gömülmez.
