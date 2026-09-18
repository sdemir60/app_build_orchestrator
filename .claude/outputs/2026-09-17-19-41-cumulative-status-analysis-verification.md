# Kümülatif durum modeli ve tek çalışma ağacı — v2 raporunun kodla doğrulaması ve değerlendirme

Tarih: 2026-09-17 · Durum: yalnız analiz, kodda değişiklik YOK · `main` = `ea3ab62`
Doğrulanan: `2026-09-17-18-16-cumulative-status-and-single-worktree-analysis-v2.md` (Claude web, koda erişimsiz yazıldı).
Kanıt biçimi: `dosya:satır` = bu commit'teki gerçek satır. OSYS ölçümleri `D:\Projects\Delta\OSYS` üzerinde bugün alındı.

## 1. Sonuç

- **v2 raporu koda büyük ölçüde sadık.** Andığı 28 iddianın 24'ü satırıyla tutuyor (§2). Mimari çıkarımları
  doğru: renk modeli App'te değiştirilebilir, verisi önizlemede zaten var; defter global ve içerik tabanlı; tek
  ağaç fikri motorun bugünkü matematiğiyle uyumlu.
- **Dört boşluk, bir ters karar, bir doküman/kod uyumsuzluğu var (§3).** En önemlisi: raporun "yeşil + ▲"
  durumu bugün Sync sonrası **yok** — üçgen yalnız koşu event'inden gelir. Rapor bu duruma dayanarak üç parçalı
  etiketi kaldırıyor; düzeltilmezse Sync sonrası bekleyen projenin hiçbir görsel izi kalmaz.
- **`D:\Projects\Delta\OSYS-Build` bu makinede zaten var** ve OSYS'nin detached bir git worktree'si (8 Eylül).
  Raporun "klasör var ama worktree değil" kenar durumu burada tetiklenmez; "benimse" yolu canlı yol.
- **Tasarım paketi §8 bu değişikliği kapalı karar sayıyor** ("v1.11.0: renk YALNIZ son işlemin hikâyesini
  anlatır — tekrar önerme"). Değişiklik meşru (tek kanal ilkesi kalıyor, kanalın anlamı değişiyor) ama
  paketsiz başlamamalı: testler "design vX §Y" ile pinli.
- **Öneri:** Faz 1 önce, §5'teki kapsam düzeltmeleriyle; ondan önce §4'teki kararlar ve design v1.20.0.

## 2. Doğrulama tablosu

| # | v2 iddiası | Durum | Kanıt |
|---|---|---|---|
| 1 | Sync ana kökü tarar, worktree'ye dokunmaz; seçili ≠ aktif iken önizleme aktif ağacı anlatır ("bilinen seam") | ✓ | `SyncWorkspaceService.cs:26-32`, `:126-127`; ARCH `1375-1378` |
| 2 | Önizleme satırı `WillBuild` / `Reason` / `OwnFilesChanged` / `LastBuiltAt` / `Conditional` / `DependencyRoots` taşır | ✓ | `IpcMessages.cs:467-469` |
| 3 | Karar sırası: kayıt yok → son koşu hatalı → imza değişti → bağımlılık notu → güncel | ✓ | `WillBuildEvaluator.cs:79-88` |
| 4 | Hatada yalnız `LastResult=Failed`; hata anındaki imza tutulmaz; kayıt yoksa açılmaz | ✓ | `RunCoordinator.cs:1881-1891` (`:1886` erken dönüş) |
| 5 | `NonConvergentSignature` aynı ilke; kayıt yoksa taze kayıt açar | ✓ | `RunCoordinator.cs:1614-1617`; `BuildStateStore.cs:116-121` |
| 6 | `VisualStatus` tek kanal; enum listesi | ✓ | `VisualStatus.cs:14-38`, `:53-67` |
| 7 | Sync ve açılış boyamaz (Fresh / başlangıç modu) | ✓ | `RunViewModel.Workspace.cs:601`, `:616`; ARCH `3380-3391` |
| 8 | İşlem başında herkes nötr griye iner | ✓ | `RunViewModel.cs:827`, `:958-981` |
| 9 | Döngü küpü amber; Sync'te başlangıç moduna düşer | ✓ | `VisualStatus.cs:63-66`, `:109-117` |
| 10 | Koreografi: 440 ms nötr an, 110 ms/node, ≤ 1,1 s, 0,18 / 0,13 | ✓ | `MarkingChoreography.cs:51-57`, `:146`; ARCH `3565-3571` |
| 11 | Kapsam: Build = `WillBuild && !Conditional`, Rebuild = döngü dışı, Cycles = üyeler | ✓ | `RunViewModel.cs:988-993` |
| 12 | Etiket sözlüğü, `retry` koşulu, üç parçalı etiket; yuva 118 → 134 → 204 px | ✓ | `DecisionLabel.cs:89-136`; `ProjectRow.xaml:71-79` |
| 13 | Canlı geçiş `OnProjectDone`: UpToDate / WaitingForDependency / LastFailed | ✓ | `RunViewModel.cs:1876-1899` |
| 14 | Üç durum matrisi; farklı branch → worktree zorunlu; aktif + kapalı → in-place | ✓ | `WorktreeManager.cs:140-166`; `Program.cs:253-256`, `:290-293` |
| 15 | Branch değişimi: hollow + Boot + "Sync required", otomatik Sync yok | ✓ | `RunViewModel.ActionBar.cs:124-142` |
| 16 | Açılışta otomatik Sync yok; açık seçim yoksa checkout'a dönülür | ✓ | `MainWindow.xaml.cs:156-160`; `RunViewModel.Workspace.cs:731-743` |
| 17 | Havuz `%LOCALAPPDATA%`, 20 GiB LRU, hep detached, reuse = `reset --hard`, üç kapı | ✓ | `WorktreeManager.cs:123`, `:283-297`, `:331-358`; `Program.cs:55`, `:303-316` |
| 18 | Commit çözümü: aktif → yerel HEAD; başka → `refs/heads/X`, yoksa `origin/X` | ✓ | `Program.cs:360-374` |
| 19 | Kimlik rebase imzadan önce; defter mantıksal anahtarla | ✓ | `ProjectIdentityRebase.cs:7-19`; `Program.cs:180-185`; ARCH `1018-1033` |
| 20 | OSYS çıktısı ortak havuza düşer (absolute HintPath + post-build copy) | ✓* | Bugün: 1792 absolute HintPath, 217 copy satırı, 177/177 projede PostBuildEvent; hedef `C:\OSYS\{Client,Server,…}\bin` **düz klasör**, iki ağacın da dışında. Rapordaki 1927/178 Haziran eng review (D12) sayıları; öz aynı |
| 21 | Sayaçlar koşu odaklı | ✓ | `RunCounters.cs:47-55` |
| 22 | Stop in-flight projeyi bitirtir; App hard stop göndermez | ✓ | `StopKind.Hard` yalnız Supervisor içinde yazılır; ARCH §4.5 |
| 23 | `StaleObjRunStartWarner` yalnız in-place içindir | ✓ | `RunCoordinator.cs:857-863` |
| 24 | Sync'in `Conditional` alanı da düz Build'i simüle eder | ✓ | `SyncWorkspaceService.cs:162-178`, `:273-276` |
| 25 | **"Current + ▲": Sync sonrası bekleyen proje yeşil + üçgen görünür** | ✗ | Üçgen yalnız koşu event'inden — §3 B1 |
| 26 | `EndFinale` "kalan griler → herkes" bir kod değişikliği | ✗ (yalnız doküman) | `Grey` adımı yalnız opaklık: `GraphView.xaml.cs:501-516` |
| 27 | Kenar durum (a): `<repo>-Build` var ama bu reponun worktree'si değil | ✗ bu makinede | §3 B2 |
| 28 | Doküman ile kod uyumlu, çelişki yok | ✓* | CLAUDE.md'nin `reset` cümlesi hariç — §3 B8 |

## 3. Raporun gözden kaçırdıkları

**B1 — Üçgen Sync sonrası yok; Faz 1'e eklenmeli.** `HasDepIssue` yalnız `DepIssues`'tan türer
(`RunViewModel.cs:174-182`), `DepIssues` yalnız koşu sonucundan yazılır (`:1849`), `OnBuildPreview` ona
dokunmaz (`:1719-1745`, yalnız `DependencyRoots` yazar) ve hem Sync'in nötrlemesi hem her işlem onu siler
(`:963`). ARCH §14.3 bunu ilke olarak söylüyor: "dependency issues come last because they are the most
transient — the next run clears them" (`3441-3442`). Yani bugün `WaitingForDependency` bir projenin Sync
sonrası tek izi `affected · up to date · 2h` etiketidir. Rapor o etiketi "üçgen zaten söylüyor" diye kaldırıyor —
üçgen söylemiyor. Düzeltme: kümülatif modelde ▲ de kümülatif olur: `HasDepIssue = DepIssues ∪ (Reason ==
WaitingForDependency)`; tooltip `RowWarning.DepIssuePrefix` + `DependencyRoots` (metin zaten tek kaynaktan,
`RowWarning.cs:27`). §14.3'ün "en geçici" cümlesi yeniden yazılır. Bu eklenirse raporun etiket sadeleştirmesi
tutarlı hâle gelir; eklenmezse üçlü etiket kalmalı.

**B2 — `OSYS-Build` zaten var.** `git worktree list`: `OSYS` (`feature/wo-wap-rule-constraint`), `OSYS-AI`
(detached), `OSYS-Build` (detached `688a9ec`, 8 Eylül). Havuzda ayrıca beş worktree var
(`%LOCALAPPDATA%\BuildOrchestrator\worktrees\`: `developer`, `version_v79.1`, `version_v79.7`, iki feature).
Faz 2'de "benimse" yolu — `worktree list`'te var, detached, `reset --hard` kapısı geçer — bu makinedeki gerçek
ilk adımdır; raporun (a) kenar durumu (klasör var ama worktree değil) burada hiç tetiklenmez. Bilinmesi
gereken: `OSYS-Build`'i kim açtı, içinde korunacak bir şey var mı (her Sync `reset --hard` atacak).

**B3 — Tasarım paketi §8 bu kararı kapalı sayıyor.** `design-v1.19.0/README.md:534`: "v1.11.0: renk YALNIZ son
işlemin hikâyesini anlatır — tek statü kanalı … tekrar önerme". Yeni model tek kanal ilkesini korur (şerit,
nokta, glyph, çerçeve, küp yine aynı değerden boyanır) ama kanalın **anlamını** değiştirir: "son işlemde ne
oldu" yerine "bu projenin çıktısı ne durumda". Bu, paketin §2.3 renk kuralı (`:149-150`), §2.4 etiket sözlüğü
(`:195`), §5 statü tablosu (`:480-495`, `fresh` satırı ve "will-build kaldırıldı" satırı), §8 kararı ve §9
sürüm notu demek. Kod tarafındaki testler "design vX §Y" referansıyla pinli (CLAUDE.md: davranış değişince test
yeni kuralı pinler + gerekçe yazılır); gerekçenin adresi paket olmalı. Alternatif: bu rapor + v2 spec sayılsın
(§4-6).

**B4 — Kırmızının kanıtı yalnız `exit ≠ 0` olmalı.** Rapor `FailedSignature`'ı "exit ≠ 0, timeout, invoke
hatası"nda yazıyor. Timeout ve invoke hatası kaynağın bozuk olduğuna kanıt değildir; "bu kaynak derlenmiyor"
iddiasını taşımazlar (`ReasonFor`, `RunCoordinator.cs:1893-1904`: `stopped` / `timeout` / `exit N`). Öneri:
`FailedSignature` yalnız `exit ≠ 0`; timeout / stopped / invoke hatasında bugünkü gibi yalnız
`LastResult=Failed` (`BuiltSignature` korunur — Fast geçişi gerekçesi `:1863-1866`), satır **gri `never
built`** (raporun yeni tooltip'i "No build output known to this tool" bu durumda da doğru). Nadir yol (App
hard stop göndermez; timeout sınırlı) ama kural netliği için önemli.

**B5 — Koşunun hikâyesi `ProjectRowState`'te yaşamaya devam eder; yalnız renk birleşir.** Ad vurgusu
(`VisualStatuses.NameIsEmphasised`, `VisualStatus.cs:131-133`), süre sütunu, ribbon'un kapanış tablosu ve
`RunCounters` hepsi `State` okur (`RunCounters.cs:47-55`). Yeni modelde bunlar aynen `State`'ten okumaya devam
eder; `VisualStatuses.For` yalnız yeni bir "standing" (Current / Stale / Failed / Unknown) girdisi alır ve
motor sessizken (`Discovered`) ya da `Skipped` dediğinde ona düşer. Açık kalan tek nokta: koşu bittikten sonra
`Skipped` satırın glyph'i. Glyph `GraphStatus`'tan çizilir (`StatusGlyph`, DP `:43`), yani `State=Skipped`
kaldıkça "—" görünür — şerit yeşilken. Seçenekler: (a) koşu bitince (`PropagateRunActive`,
`RunViewModel.cs:1390-1401`) `Skipped → Pending`, glyph standing'e döner; (b) `StatusGlyph` `VisualStatus`
okur. Önerim (a): rapor "— yalnız koşu içinde" diyor, `SkipReason` zaten satırda kalıyor (proje sayfası oradan
okur, `:184-188`), kapanış satırı tabloyu taşıyor.

**B6 — Chip'ler filtredir; anlamı değişiyor.** `ProjectFilter.cs:16` chip adları filtre anahtarı; `✓ + ✗ =
"bu koşuda derlenenler"` (ARCH `2016-2017`, README `256-257`) kümülatif modelde "güncel + bozuk" olur. `building`
filtresi bugün `Started | Pending` (`:46`, "queued dahil") — Pending artık "derlenecek gri" demek olmadığı
için yalnız `Started` olmalı. Rapor §4.8 chip'leri sayıyor, filtre anlamını söylemiyor; README "Reading the
list" cümlesi değişir.

**B7 — Finale'de kod değişikliği yok.** `EndStep.Grey` yalnız `ApplyAllOpacities()` çağırır
(`GraphView.xaml.cs:501-516`); renk zaten node'un kendi rengidir. Değişen yalnız adlandırma/yorum ve
ARCH `3637-3641`, design README `:157` cümleleri ("kalan griler" → "kalan herkes kendi renginde").

**B8 — Doküman ile kod uyuşmuyor (CLAUDE.md).** CLAUDE.md değişmezi: "`checkout`/`switch`/`pull`/`reset`
hiçbir akışta çalıştırılmaz". Kod: `reset --hard` havuz worktree'sinde koşar (`WorktreeManager.cs:297`);
ARCH §10.1 bunu açıkça belgeler (`1337`, "cwd is a pool worktree, never the main repository") ve kaynak guard'ı
`NoGitMutationOutsideExternalsTests.cs:55-58` yalnız `FastForwardUpdater.cs` ile `WorktreeManager.cs`'ye izin
verir. ARCH ve kod tutarlı, CLAUDE.md eksik. Faz 2 bu `reset`'i her Sync'e taşıyacağı için cümle nasılsa
yeniden yazılacak; hangisinin doğru olduğunu sen söyle (önerim: CLAUDE.md'ye "ana repoda" niteliği + havuz/build
tree istisnası).

**B9 — Türetilen yol test dikişi ister.** Rapor `<repo>-Build`'i "ayar yok, türetilir" diyor. Bugün IPC ve
acceptance testleri havuzu `--worktrees` ile izole ediyor (`SupervisorIpcTests.cs:50`, `:276`; ARCH `3767`).
Tek ağaçta da Supervisor'a bir override (`--build-tree` gibi) gerekir; yoksa üç acceptance testi gerçek
`OSYS-Build`'e `reset --hard` atar.

**B10 — Planner taşıma gerçek bir refactor, sınırlı.** `BuildRunPlan` Supervisor'da yerel fonksiyon
(`Program.cs:115-197`) + `PrepareAsync` (`:244-341`) + `ComputeIncremental` (`:419-463`);
`SyncWorkspaceService` aynı hattın kopyasını worktree'siz ve rebase'siz koşuyor (`:126-142`, `:227-284`) ve
ek olarak `changed` sayacı için Fast geçişi yapıyor (`:253`, `:259-262`). Ortak `Core/Planning/…` planlayıcı
iki çağıranı besler; Sync'in Fast geçişi parametre olur. "Planlama Core'da" değişmezine de uyar.

**B11 — Küçükler.** `StaleObjRunStartWarner` tek ağaçta ölü (`worktreeObjRoot` hep dolu); harici projeler
bugün de worktree modunda kapsam dışı → kaldır ya da haricilere daralt. `PruneToCapAsync`, `ListWorktreesAsync`,
`DeleteAsync`, `Worktree` modeli (`ProjectModels.cs:222`), `WorktreeListEvent`, `UiState.UseWorktree/
WorktreeName` (`UiStateStore.cs:44-45`) kalkar; `StartRunCommand.UseWorktree/WorktreeName`
(`IpcMessages.cs:114`) kalkar, `SyncCompletedEvent` (`:323-325`) build tree alanlarını alır.

## 4. Kararlar — senin

Raporun beş sorusu ve benim dört ekim; her birinde önerim.

| # | Soru | Önerim |
|---|---|---|
| 1 | Varsayılan branch modu: checkout'u izle mi, son seçimi hatırla mı? | **İzle.** Bugünkü `_branchChosenByUser` (`RunViewModel.ActionBar.cs:72`) zaten "sabitle"; eksik olan kalıcılık + popover'da "● Checked-out branch" satırı. |
| 2 | Açılışta otomatik Sync? | **Evet.** Not: her Sync build tree'de `reset --hard` demek; açılışta bir git yazımı olur (ana repoya değil). Offline degrade var (`SyncWorkspaceService.cs:94-100`). |
| 3 | Sync'te reveal: her Sync yeniden listeleme mi, yerinde renk geçişi mi? | **Bugünkü reveal kalsın.** "Listed from scratch" hikâyesi (ARCH `1942-1947`) doğru; Faz 3'e bile gerek yok. |
| 4 | In-place mod: tamamen kalksın mı, gizli ayar mı? | **Tamamen.** Motorda kalan ölü yol = üç durum matrisi + fallback + warner = sapma riski. |
| 5 | Sıra: Faz 1 önce mi? | **Faz 1 önce**, B1 / B4 / B5 / B6 düzeltmeleriyle (§5). |
| 6 | Tasarım paketi v1.20.0 mı, rapor = spec mi? | **Paket.** §8 kararı tersine dönüyor; testlerin gerekçe adresi paket. Küçük bir sürüm: §2.3, §2.4, §5, §8, §9. |
| 7 | Kesilen deneme (timeout / stopped / invoke hatası): kırmızı mı, gri mi? | **Gri `never built`.** Kırmızı yalnız `exit ≠ 0` kanıtıyla (B4). |
| 8 | Koşu bitince `Skipped` satırın glyph'i: "—" mi, standing mi? | **Standing** (B5-a): "—" yalnız koşu içinde. |
| 9 | `OSYS-Build`'in bugünkü sahibi kim; Faz 2 onu devralsın mı? | Devralsın; içinde korunacak bir şey varsa önce söyle. |

Not: `failed` satırına isteğe bağlı `· 2h` kuyruğu (kanıtın yaşı, `LastRunAt`) eklenebilir; yeşilin
kuyruğuyla simetrik, ucuz. Rapor kuyruksuz bırakıyor; iki yol da tutarlı.

## 5. Faz 1 kapsamı — düzeltilmiş

Rapordaki listeye B1 / B4 / B5 / B6 eklenmiş hâli. Plan değil, kapsam.

**Core / Supervisor**
- `BuildState.FailedSignature` (`ProjectModels.cs:124-159`; alan SONA, default'lu — eski kayıtlar alansız çözülür).
- `InvalidateBuildStateOnFailure` (`RunCoordinator.cs:1881-1891`) sonucun **nedenini** alır: `exit ≠ 0` ise
  `FailedSignature = run.Incremental.SignatureById[id]` (başarı yolundaki kaynak, `:1818-1819`; Rebuild ve
  Cycles'ta da hesaplanır, `Program.cs:419-453`) ve kayıt yoksa açar (`:1616` deseni); diğer sebeplerde
  bugünkü gibi. Politika invalidasyonu (`!trustedResult`, `:1306`) yazmaz.
- `WillBuildEvaluator` (`:79-88`) sırası: `FailedSignature == current` → `LastFailed` · `BuiltSignature is null`
  (ya da `LastResult=Failed` kanıtsız) → `NeverBuilt` · imza değişti → `SignatureChanged` · not → `UpToDate`.
- `BuildStateStore.LastBuiltAtOf` aynen; `FailedAtOf` yalnız 4-not'taki kuyruk istenirse.

**App**
- `VisualStatuses.For` (`VisualStatus.cs:53-67`) standing girdisi alır; brush tabloları (`:73-117`) Current /
  Stale / Failed / Unknown satırları; `IsStartMode` = Unknown; `NameIsEmphasised` değişmez (State okur).
- `ProjectRowViewModel.VisualStatus` (`RunViewModel.cs:296`) standing'i `WillBuild` / `WillBuildReason`'dan
  türetir; `HasDepIssue` (`:182`) ∪ `WaitingForDependency` (B1).
- `NeutralizeRows` (`:958-981`): `State`, süre, `CycleWaiting`, `SkipReason`, `Marked`; `Fresh` yalnız
  `WillBuild == null` (Sync'in `fresh: true` çağrısı `:616` düşer). `ResetRowsToHollow` (`ActionBar.cs:362-373`)
  Unknown'a düşürmeye devam eder.
- Koşu bitişi: `Skipped → Pending` (B5-a, `:1390-1401`).
- `DecisionLabel` (`:89-136`): beş sözcük; `retry` ve üçlü kalkar, tooltip'ler rapordaki gibi;
  `ProjectRow.xaml:79` `MinWidth` 204 → 134.
- `RunCounters` (`:35-58`) + `ProjectFilter` (`:16-46`): Σ · building (`Started`) · ✓ Current · ○ Stale ·
  ✗ Failed · ⚠; "—" chip'i kalkar; ribbon kapanış satırı aynen.
- `GraphBinder.cs:49-51` aynı `For`'u çağırır, ek eşleme yok. Koreografiler ve finale dokunulmaz (B7).

**Testler (yeniden pinlenecek — ada göre, içerik okunmadı):** `StartModeContinuousTests`, `ChoreographyTests`,
`CycleCubeTests`, `DecisionLabelTests`, `GraphBinderTests`, `GraphWillBuildFeedTests`,
`GraphRunLifecycleTests`, `GraphSkippedProjectTests`, `RunViewModelStateTests`, `RunViewModelTests`,
`ProjectRowTests`, `RunCountersTests`, `Planning/WillBuildTests`, `Supervisor/RunCoordinatorTests` (defter),
`State/BuildStateStoreTests`. Her biri kırmızı test kuralıyla.

**Doküman:** ARCH §7.4-7.5, §13.2, §13.6, §14.3 (başlangıç modu, üçgen, "en geçici"), §16; README "Sync
colours nothing", "Reading the list"; design README §2.3, §2.4, §5, §8, §9 (B3).

Faz 2'nin kapsamı rapordaki gibi + B2 (benimse), B8 (CLAUDE.md), B9 (test dikişi), B10 (planner), B11.

## 6. Süreç

Bu iş architectural: kararlar (§4) → design v1.20.0 ya da "rapor = spec" kararı → spec dosyası
(`.claude/outputs/…-design.md`) → `superpowers:writing-plans` → task-by-task TDD. Bu oturumda kod yazılmadı;
kaynak guard'ları ve süit dokunulmadan duruyor.
